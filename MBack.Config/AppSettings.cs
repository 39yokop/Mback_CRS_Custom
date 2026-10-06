using System.Collections.Generic;

namespace MBack.Config;

public class AppSettingsRaw
{
    // 基本設定
    public List<BackupPair> BackupSettings { get; set; } = new();
    public List<string> GlobalExclusions { get; set; } = new();
    
    // 詳細設定
    public int LogRetentionDays { get; set; } = 60;
    public int RansomwareThreshold { get; set; } = 2000;
    public int ImageEncryptionThreshold { get; set; } = 10;   // 画像の暗号化検知(個/5分)。0で無効
    public string MaintenanceStart { get; set; } = "00:00";
    public string MaintenanceEnd { get; set; } = "00:00";
    public bool SendDailySummary { get; set; } = false;
    public MailSettings MailConfig { get; set; } = new(); 

    // ==========================================
    // ★ MBack_CRS カスタム機能設定
    // ==========================================
    public bool HeicConversionEnabled { get; set; } = false;
    public bool KeepOriginalHeic { get; set; } = true;
    
    // HEIC変換時の画質パラメータ
    public int HeicLongSide { get; set; } = 3500; // 0ならリサイズなし
    public int HeicQuality { get; set; } = 85;
    public bool PreserveExif { get; set; } = true;

    // 過去案件リサイズ機能
    public bool ResizeTaskEnabled { get; set; } = false;
    public string AccessDbPath { get; set; } = "";
    public string SiteFolderPath { get; set; } = "";
    public string ResizeTaskTime { get; set; } = "02:00";

    // ==========================================
    // ★ ルートβ（完全版）追加設定
    // ==========================================
    public string ResizeSchedule { get; set; } = "Monthly_1"; // 月1回などの実行スケジュール
    public int MaxHistory { get; set; } = 10;                 // エコ化：保存する世代数の上限（デフォルト10）
}

public class BackupPair
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string PreCommand { get; set; } = "";
    
    // ★新規：事後実行スクリプト（クラウド連携等）
    public string PostCommand { get; set; } = ""; 
}

public class MailSettings
{
    public bool Enabled { get; set; } = false;
    public string ToAddress { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string SmtpServer { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpSsl { get; set; } = true;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public bool UsePopBeforeSmtp { get; set; } = false;
    public string PopServer { get; set; } = "";
    public int PopPort { get; set; } = 110;
    public bool PopSsl { get; set; } = false;
}