using System.Collections.Concurrent;

namespace MBack.Service;

public enum ImageCheckResult { Valid, Invalid, Indeterminate }

public readonly record struct ImageIntegrityResult(bool Tripped, bool NewlySuspect, int SuspectCount);

// 画像ファイルの中身が「画像として読める」かを確認する。
// 暗号化された画像は、拡張子が .jpg のままでも先頭の識別バイト列(シグネチャ)が失われる。
// 一方、カメラからの大量コピーやリサイズソフトによる書き込みは中身が正しい画像のままなので影響を受けない。
public static class ImageSignatureChecker
{
    public static ImageCheckResult Check(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[16];
            int read = 0;
            while (read < header.Length)
            {
                int n = fs.Read(header, read, header.Length - read);
                if (n <= 0) break;
                read += n;
            }

            if (read < 12) return ImageCheckResult.Indeterminate;                  // 書き込み途中(空・極小)
            if (header.All(b => b == 0)) return ImageCheckResult.Indeterminate;    // 領域だけ確保された書き込み途中
            return HasKnownSignature(header) ? ImageCheckResult.Valid : ImageCheckResult.Invalid;
        }
        catch (Exception)
        {
            return ImageCheckResult.Indeterminate; // ロック中・既に削除済み等は判断しない
        }
    }

    // 拡張子とは無関係に、いずれかの画像形式として正しければ有効とみなす
    internal static bool HasKnownSignature(byte[] h)
    {
        if (h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return true;                                                // JPEG
        if (h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47 &&
            h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A) return true;                                // PNG
        if (h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8') return true;                                    // GIF
        if (h[0] == 'B' && h[1] == 'M') return true;                                                                  // BMP
        if (h[0] == 'I' && h[1] == 'I' && (h[2] == 0x2A || h[2] == 0x2B) && h[3] == 0) return true;                   // TIFF/BigTIFF (リトルエンディアン)
        if (h[0] == 'M' && h[1] == 'M' && h[2] == 0 && (h[3] == 0x2A || h[3] == 0x2B)) return true;                   // TIFF/BigTIFF (ビッグエンディアン)
        if (h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' &&
            h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return true;                                  // WebP
        if (h[4] == 'f' && h[5] == 't' && h[6] == 'y' && h[7] == 'p') return true;                                    // HEIC/HEIF/AVIF
        return false;
    }
}

// 「画像として読めない内容に書き換えられたファイル」を一定期間内に何個見つけたかを数える。
// 件数ではなく中身で判定するため、大量コピー・大量リサイズのような正常な作業では増えない。
public sealed class ImageIntegrityMonitor
{
    private static readonly TimeSpan ValidCacheTime = TimeSpan.FromSeconds(2);
    private const int ValidCachePruneSize = 5000;

    private readonly int _threshold;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, DateTime> _suspects = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _recentlyValid = new(StringComparer.OrdinalIgnoreCase);

    public ImageIntegrityMonitor(int threshold, int windowSeconds)
    {
        _threshold = threshold;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    public ImageIntegrityResult Evaluate(string path, DateTime now)
    {
        // 同じファイルの連続イベントで何度も読み直さない(正常と確認できたものだけ短時間キャッシュ)
        if (_recentlyValid.TryGetValue(path, out var checkedAt) && now - checkedAt < ValidCacheTime)
        {
            return new ImageIntegrityResult(false, false, _suspects.Count);
        }

        switch (ImageSignatureChecker.Check(path))
        {
            case ImageCheckResult.Valid:
                _recentlyValid[path] = now;
                _suspects.TryRemove(path, out _);
                if (_recentlyValid.Count > ValidCachePruneSize)
                {
                    foreach (var kv in _recentlyValid)
                    {
                        if (now - kv.Value >= ValidCacheTime) _recentlyValid.TryRemove(kv.Key, out _);
                    }
                }
                return new ImageIntegrityResult(false, false, _suspects.Count);

            case ImageCheckResult.Invalid:
                foreach (var kv in _suspects)
                {
                    if (now - kv.Value > _window) _suspects.TryRemove(kv.Key, out _);
                }
                bool isNew = _suspects.TryAdd(path, now);
                if (!isNew) _suspects[path] = now;
                int count = _suspects.Count;
                return new ImageIntegrityResult(count >= _threshold, isNew, count);

            default:
                return new ImageIntegrityResult(false, false, _suspects.Count);
        }
    }
}
