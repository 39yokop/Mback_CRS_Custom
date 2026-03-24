using System;
using System.IO;
using ImageMagick;

namespace MBack.Service;

public class ImageProcessor
{
    // 1MB = 1,048,576 Bytes (比較用の定数)
    private const long MaxFileSize = 1048576;

    /// <summary>
    /// HEIC画像をJPGに変換し、設定に応じてリサイズやEXIF保持を行います。
    /// </summary>
    public static string? ConvertHeicToJpg(string heicPath, bool keepOriginal, int maxLongSide, int quality, bool preserveExif)
    {
        if (!File.Exists(heicPath)) return null;

        string dir = Path.GetDirectoryName(heicPath) ?? "";
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(heicPath);
        string newJpgPath = Path.Combine(dir, $"{fileNameWithoutExt}.jpg");

        try {
            // Magick.NETを使用してHEICを読み込み
            using (var image = new MagickImage(heicPath)) {
                image.Format = MagickFormat.Jpeg;
                
                // 画質の設定 (uintへのキャスト)
                image.Quality = (uint)quality;

                // EXIFメタデータの処理 (維持しない場合はStripで全削除して軽量化)
                if (!preserveExif) {
                    image.Strip();
                }

                // 長辺リサイズの処理 (0の場合はリサイズしない)
                if (maxLongSide > 0) {
                    // 縦横どちらかが指定サイズを超えている場合のみ縮小
                    if (image.Width > maxLongSide || image.Height > maxLongSide) {
                        // MagickGeometryを使うと、縦横比(アスペクト比)を自動で維持してくれます
                        var geometry = new MagickGeometry((uint)maxLongSide, (uint)maxLongSide);
                        image.Resize(geometry);
                    }
                }

                image.Write(newJpgPath);
            }

            // 元ファイル（HEIC）の処理
            if (keepOriginal) {
                string originalDir = Path.Combine(dir, "Original");
                if (!Directory.Exists(originalDir)) Directory.CreateDirectory(originalDir);
                
                string backupPath = Path.Combine(originalDir, Path.GetFileName(heicPath));
                
                // 退避先に同名ファイルが既にある場合は削除して上書き
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Move(heicPath, backupPath);
            } else {
                File.Delete(heicPath); // 残さない設定の場合は削除
            }
            
            return newJpgPath;
        } catch (Exception) {
            // 変換中のエラー（ファイルロック等）はnullを返して安全に終了
            return null;
        }
    }

    /// <summary>
    /// 1MBを超える画像を対象に、画質調整とリサイズを行い1MB以下に最適化します。
    /// PDFファイルや既に1MB以下のファイルは安全にスキップされます。
    /// </summary>
    public static (bool Success, long OldSize, long NewSize) OptimizeImageSize(string imagePath)
    {
        if (!File.Exists(imagePath)) return (false, 0, 0);
        
        var fileInfo = new FileInfo(imagePath);
        long oldSize = fileInfo.Length;

        // 既に1MB以下の場合は即座にスキップ（ディスクI/Oの節約）
        if (oldSize <= MaxFileSize) return (false, oldSize, oldSize);

        // 拡張子チェック（PDFなどの非画像ファイルをブロック）
        string ext = fileInfo.Extension.ToLower();
        if (ext == ".pdf" || ext == ".doc" || ext == ".xls") return (false, oldSize, oldSize);
        if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".heic") return (false, oldSize, oldSize);

        try {
            using (var image = new MagickImage(imagePath)) {
                bool isModified = false;

                // ステップ1: まずJPEG品質（Quality）を75に落として圧縮を試みる
                image.Format = MagickFormat.Jpeg; 
                image.Quality = 75; 
                
                // メモリ上でサイズをシミュレーション
                byte[] buffer = image.ToByteArray();
                
                // ステップ2: 品質の低下だけでは1MBを切れない場合のみ、解像度（縦横サイズ）を縮小する
                if (buffer.Length > MaxFileSize) {
                    // 目標を安全圏の800KB (819200 Bytes) として面積比から縮小率を計算
                    double scaleFactor = Math.Sqrt(819200.0 / buffer.Length);
                    
                    uint newWidth = (uint)(image.Width * scaleFactor);
                    uint newHeight = (uint)(image.Height * scaleFactor);
                    
                    // 極端に小さくなりすぎるのを防ぐガード（幅800px以下にはしない）
                    if (newWidth > 800) {
                        image.Resize(newWidth, newHeight);
                        isModified = true;
                    }
                } else {
                    isModified = true; // 品質変更のみで1MB以下を達成
                }

                // 変更が加わった場合のみファイルに上書き保存
                if (isModified) {
                    image.Write(imagePath);
                    var newFileInfo = new FileInfo(imagePath);
                    return (true, oldSize, newFileInfo.Length);
                }
            }
        } catch {
            // 画像が破損している場合や、ImageMagickが非対応の謎ファイルだった場合は安全に無視する
        }
        
        return (false, oldSize, oldSize);
    }
}