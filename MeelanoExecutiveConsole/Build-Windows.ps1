[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    throw 'این اسکریپت باید روی Windows اجرا شود؛ WPF روی Linux/macOS قابل اجرا نیست.'
}

$project = Join-Path $PSScriptRoot 'MeelanoExecutiveConsole.csproj'
$publishDirectory = Join-Path $PSScriptRoot "artifacts\$RuntimeIdentifier"
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Write-Host "Publishing MEELANO for $RuntimeIdentifier..." -ForegroundColor Cyan
& dotnet publish $project `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $publishDirectory 'MeelanoExecutiveConsole.exe'
if (-not (Test-Path $executable)) {
    throw "Publish completed but the expected executable was not found: $executable"
}

Write-Host "Ready: $executable" -ForegroundColor Green
Write-Host 'This is a self-contained Windows build. The WPF interface is Windows-only.' -ForegroundColor DarkGray
