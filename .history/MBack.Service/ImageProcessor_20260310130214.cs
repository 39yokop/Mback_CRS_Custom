using System;
using System.IO;
using ImageMagick;

namespace MBack.Service;

public class ImageProcessor
{
    private const long MaxFileSize = 1048576;

    /// <summary>
    /// HEIC画像をJPGに変換します。同名ファイルが存在する場合は自動で (1), (2)... と連番を付与します。
    /// ファイル操作を行う直前に markSafeAction を呼び出し、監視システムの誤爆を防ぎます。
    /// </summary>
    public static (string? JpgPath, string? BackupHeicPath) ConvertHeicToJpg(
        string heicPath, bool keepOriginal, int maxLongSide, int quality, bool preserveExif, Action<string>? markSafeAction = null)
    {
        if (!File.Exists(heicPath)) return (null, null);

        string dir = Path.GetDirectoryName(heicPath) ?? "";
        string originalDir = Path.Combine(dir, "Original");
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(heicPath);
        
        string newJpgPath = Path.Combine(dir, $"{fileNameWithoutExt}.jpg");
        string backupHeicPath = Path.Combine(originalDir, $"{fileNameWithoutExt}.heic"); // 拡張子を小文字に統一

        // ★同名ファイル回避ロジック（JPG側と退避側、両方が空いている番号を探す）
        int counter = 1;
        while (File.Exists(newJpgPath) || (keepOriginal && File.Exists(backupHeicPath))) {
            newJpgPath = Path.Combine(dir, $"{fileNameWithoutExt} ({counter}).jpg");
            backupHeicPath = Path.Combine(originalDir, $"{fileNameWithoutExt} ({counter}).heic");
            counter++;
            
            // 安全装置（無限ループ回避）：1000回被ったら異常とみなして処理を諦める
            if (counter > 1000) return (null, null);
        }

        try {
            // ファイルを実際に作成・移動する「直前」に、Workerへ安全フラグを立ててもらう
            markSafeAction?.Invoke(heicPath);
            markSafeAction?.Invoke(newJpgPath);
            if (keepOriginal) markSafeAction?.Invoke(backupHeicPath);

            // Magick.NETを使用してHEICを読み込み
            using (var image = new MagickImage(heicPath)) {
                image.Format = MagickFormat.Jpeg;
                image.Quality = (uint)quality;

                if (!preserveExif) image.Strip();

                if (maxLongSide > 0) {
                    if (image.Width > maxLongSide || image.Height > maxLongSide) {
                        var geometry = new MagickGeometry((uint)maxLongSide, (uint)maxLongSide);
                        image.Resize(geometry);
                    }
                }
                image.Write(newJpgPath);
            }

            // 元ファイル（HEIC）の処理
            if (keepOriginal) {
                if (!Directory.Exists(originalDir)) Directory.CreateDirectory(originalDir);
                File.Move(heicPath, backupHeicPath);
            } else {
                File.Delete(heicPath);
            }
            
            return (newJpgPath, backupHeicPath);
        } catch (Exception) {
            return (null, null);
        }
    }

    public static (bool Success, long OldSize, long NewSize) OptimizeImageSize(string imagePath)
    {
        if (!File.Exists(imagePath)) return (false, 0, 0);
        
        var fileInfo = new FileInfo(imagePath);
        long oldSize = fileInfo.Length;

        if (oldSize <= MaxFileSize) return (false, oldSize, oldSize);

        string ext = fileInfo.Extension.ToLower();
        if (ext == ".pdf" || ext == ".doc" || ext == ".xls") return (false, oldSize, oldSize);
        if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".heic") return (false, oldSize, oldSize);

        try {
            using (var image = new MagickImage(imagePath)) {
                bool isModified = false;

                image.Format = MagickFormat.Jpeg; 
                image.Quality = 75; 
                
                byte[] buffer = image.ToByteArray();
                
                if (buffer.Length > MaxFileSize) {
                    double scaleFactor = Math.Sqrt(819200.0 / buffer.Length);
                    uint newWidth = (uint)(image.Width * scaleFactor);
                    uint newHeight = (uint)(image.Height * scaleFactor);
                    
                    if (newWidth > 800) {
                        image.Resize(newWidth, newHeight);
                        isModified = true;
                    }
                } else {
                    isModified = true; 
                }

                if (isModified) {
                    image.Write(imagePath);
                    var newFileInfo = new FileInfo(imagePath);
                    return (true, oldSize, newFileInfo.Length);
                }
            }
        } catch { }
        
        return (false, oldSize, oldSize);
    }
}