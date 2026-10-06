using System.Runtime.Versioning;

// このサービスはWindowsサービス専用(DPAPI・Windowsサービスホスト等に依存)であることを明示する
[assembly: SupportedOSPlatform("windows")]
