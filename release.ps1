# MBack_CRS_Custom - リリース用ステージング自動化スクリプト
#
# MBack.Service / MBack.Config / MRestore(別リポジトリ) をそれぞれ自己完結で
# dotnet publish し、C:\MBackRelease を作り直す。
# 完了後、MBack.iss を Inno Setup Compiler でコンパイルすればインストーラーが作れる。

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$mrestoreRepo = "D:\Nextcloud\Developer\MRestore"
$releaseDir = "C:\MBackRelease"

function Invoke-DotnetPublish($csprojPath) {
    Write-Host "==> dotnet publish: $csprojPath" -ForegroundColor Cyan
    dotnet publish $csprojPath -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish に失敗しました: $csprojPath" }
}

function Copy-PublishOutput($sourceDir, $destDir) {
    if (-not (Test-Path $sourceDir)) { throw "publish出力が見つかりません: $sourceDir" }
    Write-Host "==> コピー: $sourceDir -> $destDir" -ForegroundColor Cyan
    robocopy $sourceDir $destDir /E /NFL /NDL /NJH /NJS | Out-Null
    $robocopyExitCode = $LASTEXITCODE
    if ($robocopyExitCode -ge 8) { throw "コピーに失敗しました: $sourceDir -> $destDir (robocopy exit code $robocopyExitCode)" }
    # robocopyの0-7は正常終了(1〜7は上書き/余剰ファイルありなどの想定内の状態)なので、
    # 呼び出し元スクリプト全体の終了コードに誤って伝播しないようクリアしておく
    $global:LASTEXITCODE = 0
}

# 1. C:\MBackRelease を作り直す(前回別プロダクトをビルドした際の残骸を持ち越さないため)
if (Test-Path $releaseDir) {
    Write-Host "==> 既存の $releaseDir を削除中..." -ForegroundColor Yellow
    Remove-Item $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDir | Out-Null

# 2. MBack.Service / MBack.Config を自己完結でパブリッシュ
Invoke-DotnetPublish (Join-Path $repoRoot "MBack.Service\MBack.Service.csproj")
Invoke-DotnetPublish (Join-Path $repoRoot "MBack.Config\MBack.Config.csproj")

# 3. MRestore もパブリッシュ(MBack.issがpublishフォルダを直接参照するため)
if (Test-Path $mrestoreRepo) {
    Invoke-DotnetPublish (Join-Path $mrestoreRepo "MRestore.csproj")
} else {
    Write-Host "MRestoreリポジトリが見つかりません ($mrestoreRepo)。" -ForegroundColor Yellow
    Write-Host "MBack.issのコンパイル時にMrestore.exeが見つからずエラーになります。" -ForegroundColor Yellow
}

# 4. Service/Config の publish 出力を C:\MBackRelease にまとめる
Copy-PublishOutput (Join-Path $repoRoot "MBack.Service\bin\Release\net10.0\win-x64\publish") $releaseDir
Copy-PublishOutput (Join-Path $repoRoot "MBack.Config\bin\Release\net10.0-windows\win-x64\publish") $releaseDir

# 5. app.ico を配置(ApplicationIconはexeに埋め込まれるだけでpublish出力には含まれないため)
Copy-Item (Join-Path $repoRoot "app.ico") (Join-Path $releaseDir "app.ico") -Force

Write-Host ""
Write-Host "完了しました。$releaseDir の準備ができました。" -ForegroundColor Green
Write-Host "あとは MBack.iss を Inno Setup Compiler でコンパイルしてください。" -ForegroundColor Green
exit 0
