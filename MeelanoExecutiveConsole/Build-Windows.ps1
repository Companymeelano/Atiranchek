[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    throw 'This script must run on Windows; WPF cannot be executed on Linux or macOS.'
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

if ($Installer) {
    if ($RuntimeIdentifier -ne 'win-x64') {
        throw 'The current Inno Setup definition packages win-x64 only. Publish win-x64 to create an installer.'
    }

    $compilerCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $compiler) {
        $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
        if ($command) { $compiler = $command.Source }
    }
    if (-not $compiler) {
        throw 'Inno Setup 6 was not found. Install Inno Setup 6, then run this script again with -Installer.'
    }

    $installerScript = Join-Path $PSScriptRoot 'Installer\MeelanoExecutiveConsole.iss'
    & $compiler $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }

    $installerExe = Join-Path $PSScriptRoot 'artifacts\installer\MEELANO-Executive-Console-Setup-win-x64.exe'
    if (-not (Test-Path $installerExe)) {
        throw "Inno Setup completed but the installer was not found: $installerExe"
    }
    Write-Host "Installer ready: $installerExe" -ForegroundColor Green
}
