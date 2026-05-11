$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $root "artifacts\POE-Studio"

Set-Location $root
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

dotnet publish "src\PoeStudio.Api\PoeStudio.Api.csproj" `
    -c Release `
    -o $publishDir `
    --nologo

Copy-Item (Join-Path $root "启动_POE_Studio.ps1") (Join-Path $publishDir "启动_POE_Studio.ps1") -Force
Copy-Item (Join-Path $root "README.md") (Join-Path $publishDir "README.md") -Force

Write-Host "发布完成：$publishDir"
