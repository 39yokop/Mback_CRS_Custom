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

# 6. 再配布に必要なライセンス表示ファイルを同梱する
#    自己完結ビルドで.NETランタイムを、Magick.NETでLGPL等のネイティブライブラリを再配布するため。
#    MBack.issの C:\MBackRelease\* のワイルドカードで自動的にインストーラーへ含まれる。
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }

function Copy-NoticeFile($sourcePath, $destName) {
    if (-not (Test-Path $sourcePath)) { throw "ライセンス表示ファイルが見つかりません: $sourcePath" }
    Copy-Item $sourcePath (Join-Path $releaseDir $destName) -Force
    Write-Host "==> ライセンス表示を同梱: $destName" -ForegroundColor Cyan
}

function Get-IncludedFrameworkVersion($runtimeConfigPath, $frameworkName) {
    $cfg = Get-Content $runtimeConfigPath -Raw | ConvertFrom-Json
    $fw = $cfg.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq $frameworkName } | Select-Object -First 1
    if (-not $fw) { throw "$frameworkName のバージョンを取得できません: $runtimeConfigPath" }
    return $fw.version
}

$netVersion = Get-IncludedFrameworkVersion (Join-Path $releaseDir "MBack.Service.runtimeconfig.json") "Microsoft.NETCore.App"
$netPack = Join-Path $nugetRoot "microsoft.netcore.app.runtime.win-x64\$netVersion"
Copy-NoticeFile (Join-Path $netPack "LICENSE.TXT") "DOTNET-LICENSE.txt"
Copy-NoticeFile (Join-Path $netPack "THIRD-PARTY-NOTICES.TXT") "DOTNET-THIRD-PARTY-NOTICES.txt"

$desktopVersion = Get-IncludedFrameworkVersion (Join-Path $releaseDir "MBack.Config.runtimeconfig.json") "Microsoft.WindowsDesktop.App"
$desktopPack = Join-Path $nugetRoot "microsoft.windowsdesktop.app.runtime.win-x64\$desktopVersion"
Copy-NoticeFile (Join-Path $desktopPack "LICENSE") "DOTNET-WINDOWSDESKTOP-LICENSE.txt"

# Magick.NET(ImageMagickと、LGPL等を含むネイティブライブラリ一式)の著作権・ライセンス表示
$serviceProj = [xml](Get-Content (Join-Path $repoRoot "MBack.Service\MBack.Service.csproj") -Raw)
$magickRef = $serviceProj.Project.ItemGroup.PackageReference | Where-Object { $_.Include -eq "Magick.NET-Q8-AnyCPU" } | Select-Object -First 1
if (-not $magickRef) { throw "MBack.Service.csproj から Magick.NET-Q8-AnyCPU のバージョンを取得できません" }
Copy-NoticeFile (Join-Path $nugetRoot "magick.net-q8-anycpu\$($magickRef.Version)\Notice.txt") "MAGICK-NET-NOTICE.txt"

Write-Host ""
Write-Host "完了しました。$releaseDir の準備ができました。" -ForegroundColor Green
Write-Host "あとは MBack.iss を Inno Setup Compiler でコンパイルしてください。" -ForegroundColor Green
exit 0
