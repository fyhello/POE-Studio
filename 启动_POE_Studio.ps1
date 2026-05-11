$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$url = "http://localhost:5087"

Set-Location $root
Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--project", "src\PoeStudio.Api\PoeStudio.Api.csproj", "--urls", $url) `
    -WorkingDirectory $root `
    -WindowStyle Hidden

Start-Sleep -Seconds 3
Start-Process $url

Write-Host "POE Studio 已启动：$url"
