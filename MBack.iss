; --- MBack_CRS_Custom 完全版 インストーラー作成スクリプト ---

#define MyAppName "MBack_CRS_Custom"
#define MyAppVersion "3.0"
#define MyAppPublisher "My Company"
#define MyAppExeName "MBack.Config.exe"
#define MyServiceExeName "MBack.Service.exe"
#define MyServiceName "MBackService"
#define MyIconFile "app.ico"
#define MyRestoreExeName "Mrestore.exe"
; MRestoreリポジトリの自己完結パブリッシュ出力先(dotnet publish -c Release の結果)
#define MyRestorePublishDir "D:\Nextcloud\Developer\MRestore\bin\Release\net10.0-windows\win-x64\publish"

[Setup]
; アプリケーション情報
AppId={{A8F93641-7D22-4E90-95D9-3C5798725964}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; ★出力先: リポジトリ直下に保存(Gitからは除外)
OutputDir={#SourcePath}
OutputBaseFilename=MBackSetup_CRS

; ★圧縮設定
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible

; ★管理者権限を要求（サービス登録のため必須）
PrivilegesRequired=admin

; ★インストーラー自体のアイコン設定
SetupIconFile=C:\MBackRelease\{#MyIconFile}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
; 1. 設定ファイル以外をすべてコピー（上書きOK）
Source: "C:\MBackRelease\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.json"

; 2. 設定ファイルだけは「ファイルがない時だけ」コピーする（上書き禁止！）
Source: "C:\MBackRelease\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

; 3. OSSサードパーティライセンス通知ファイルをコピー
Source: "{#SourcePath}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

; 4. 災害復旧ツール(Mrestore)をビルド出力から直接取り込む(MBackReleaseへのコピー不要)
Source: "{#MyRestorePublishDir}\Mrestore.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; スタートメニューにショートカット作成
Name: "{group}\{#MyAppName} 設定"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyIconFile}"
; 災害復旧ツール(Mrestore)へのショートカット作成
Name: "{group}\{#MyAppName} 災害復旧ツール(Mrestore)"; Filename: "{app}\{#MyRestoreExeName}"; IconFilename: "{app}\{#MyIconFile}"
; デスクトップにショートカット作成
Name: "{autodesktop}\{#MyAppName} 設定"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyIconFile}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "デスクトップにアイコンを作成する"; GroupDescription: "追加タスク"; Flags: unchecked

[Run]
; --- インストール後の処理 ---
; 1. サービスを登録する
Filename: "sc.exe"; Parameters: "create {#MyServiceName} binPath= ""{app}\{#MyServiceExeName}"" start= auto"; Flags: runhidden
; 2. サービスの説明文を設定
Filename: "sc.exe"; Parameters: "description {#MyServiceName} ""MBack 自動バックアップサービス"""; Flags: runhidden
; 2b. 起動タイプを「自動(遅延開始)」にする(OS起動直後の混雑による起動タイムアウト対策)。
;     上書きインストール時は sc create が失敗するため、別エントリとして毎回実行する
Filename: "sc.exe"; Parameters: "config {#MyServiceName} start= delayed-auto"; Flags: runhidden
; 2c. 異常終了した場合は60秒後に自動再起動する(最大3回、1日でカウントリセット)
Filename: "sc.exe"; Parameters: "failure {#MyServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000"; Flags: runhidden
; 3. サービスを開始する
Filename: "sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden
; 4. 設定ツールを起動するか聞く
Filename: "{app}\{#MyAppExeName}"; Description: "MBack 設定ツールを起動する"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; --- アンインストール前の処理 ---
; 1. サービスを停止する
Filename: "sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopService"
; 2. サービスを削除する
Filename: "sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"
; 3. 念のため少し待つ (警告対策済み)
Filename: "timeout"; Parameters: "/t 2"; Flags: runhidden; RunOnceId: "WaitTimeout"

[UninstallDelete]
; --- アンインストール時のゴミ掃除 ---
; 設定ファイルもきれいに消す
Type: files; Name: "{app}\appsettings.json"
Type: files; Name: "{app}\backup.trigger"
; フォルダごと削除
Type: dirifempty; Name: "{app}"

; =====================================================================
; ★ここから下はプログラムコード（上書きインストール対応用）
; =====================================================================
[Code]

// インストール作業が始まる直前に呼ばれる処理
// 目的: 既存のサービスが動いているとファイルが上書きできないため、先に停止させる
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // サービス停止コマンド (sc stop MBackService) を実行
  // ※サービスが存在しない場合のエラーは無視して続行させる
  Exec('sc.exe', 'stop {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  
  // 停止処理が完了するまで少し待つ（1秒）
  Sleep(1000);
  
  // 正常に処理を続行
  Result := '';
end;