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
    public string MaintenanceStart { get; set; } = "00:00";
    public string MaintenanceEnd { get; set; } = "00:00";
    public bool SendDailySummary { get; set; } = false;
    public MailSettings MailConfig { get; set; } = new(); 

    // ==========================================
    // ★ MBack_CRS カスタム機能設定
    // ==========================================
    public bool HeicConversionEnabled { get; set; } = false;
    public bool KeepOriginalHeic { get; set; } = true;
    
    // ★新規：HEIC変換時の画質パラメータ
    public int HeicLongSide { get; set; } = 3500; // 0ならリサイズなし
    public int HeicQuality { get; set; } = 85;
    public bool PreserveExif { get; set; } = true;

    public bool ResizeTaskEnabled { get; set; } = false;
    public string AccessDbPath { get; set; } = "";
    public string SiteFolderPath { get; set; } = "";
    public string ResizeTaskTime { get; set; } = "02:00";
}

public class BackupPair
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string PreCommand { get; set; } = "";
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