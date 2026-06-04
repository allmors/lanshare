param(
    [string[]]$ScriptPaths = @()
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
if ($ScriptPaths.Count -eq 0) {
    $ScriptPaths = @(
        (Join-Path $projectRoot "installer\LanShare.Client.iss"),
        (Join-Path $projectRoot "installer\LanShare.Server.iss")
    )
}

$candidatePaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$iscc = $candidatePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe was not found. Please install Inno Setup 6 first."
}

foreach ($scriptPath in $ScriptPaths) {
    Write-Host "Building installer: $scriptPath" -ForegroundColor Cyan
    & $iscc $scriptPath
}

Write-Host ""
Write-Host "Installer build completed." -ForegroundColor Green
