$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $root "artifacts\POE-Studio"
$zipPath = Join-Path $root "artifacts\POE-Studio.zip"

Set-Location $root
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

dotnet publish "src\PoeStudio.Api\PoeStudio.Api.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    --nologo

Get-ChildItem $root -Filter "*_POE_Studio.ps1" | Copy-Item -Destination $publishDir -Force
Get-ChildItem $root -Filter "*_POE_Studio.bat" | Copy-Item -Destination $publishDir -Force
Copy-Item (Join-Path $root "README.md") (Join-Path $publishDir "README.md") -Force
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "发布完成：$publishDir"
Write-Host "压缩包：$zipPath"
