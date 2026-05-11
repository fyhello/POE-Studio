$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$url = "http://localhost:5087"
$exePath = Join-Path $root "PoeStudio.Api.exe"
$dllPath = Join-Path $root "PoeStudio.Api.dll"
$projectPath = Join-Path $root "src\PoeStudio.Api\PoeStudio.Api.csproj"

Set-Location $root

if (Test-Path $exePath) {
    Start-Process -FilePath $exePath -ArgumentList @("--urls", $url) -WorkingDirectory $root -WindowStyle Hidden
}
elseif (Test-Path $dllPath) {
    Start-Process -FilePath "dotnet" -ArgumentList @($dllPath, "--urls", $url) -WorkingDirectory $root -WindowStyle Hidden
}
elseif (Test-Path $projectPath) {
    Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", "src\PoeStudio.Api\PoeStudio.Api.csproj", "--urls", $url) `
        -WorkingDirectory $root `
        -WindowStyle Hidden
}
else {
    throw "找不到 PoeStudio.Api.exe、PoeStudio.Api.dll 或源码项目。"
}

Start-Sleep -Seconds 3
Start-Process $url

Write-Host "POE Studio 已启动：$url"
