param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$ReadyToRun
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnet = "C:\Program Files\dotnet\dotnet.exe"

if (-not (Test-Path $dotnet)) {
    throw "dotnet SDK was not found: $dotnet"
}

$publishTargets = @(
    @{
        Name = "Client"
        Project = Join-Path $projectRoot "LanShare.Client\LanShare.Client.csproj"
        Output = Join-Path $projectRoot ("publish\client-" + $RuntimeIdentifier)
    },
    @{
        Name = "Server"
        Project = Join-Path $projectRoot "LanShare.Server\LanShare.Server.csproj"
        Output = Join-Path $projectRoot ("publish\server-" + $RuntimeIdentifier)
    }
)

foreach ($target in $publishTargets) {
    if (Test-Path $target.Output) {
        Remove-Item -LiteralPath $target.Output -Recurse -Force
    }

    $publishArgs = @(
        "publish",
        $target.Project,
        "-c", $Configuration,
        "-r", $RuntimeIdentifier,
        "--self-contained", "true",
        "-p:PublishSingleFile=false",
        "-o", $target.Output
    )

    if ($ReadyToRun) {
        $publishArgs += "-p:PublishReadyToRun=true"
    }

    Write-Host "Publishing $($target.Name)..." -ForegroundColor Cyan
    & $dotnet @publishArgs
}

Write-Host ""
Write-Host "Publish completed:" -ForegroundColor Green
Write-Host (Join-Path $projectRoot ("publish\client-" + $RuntimeIdentifier))
Write-Host (Join-Path $projectRoot ("publish\server-" + $RuntimeIdentifier))
