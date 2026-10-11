<#
    Builds MultiPresence in Release and deploys it to an existing install folder.

    .\build-and-deploy.ps1
    .\build-and-deploy.ps1 -Target "G:\Mi unidad\Mi unidad\[Herramientas]\MultiPresence - Discord Presence ... (WindowsOnly)"

    Requires the .NET 9 SDK. It is normally already installed; if "dotnet" is not on PATH
    the script falls back to C:\Program Files\dotnet\dotnet.exe.
#>
[CmdletBinding()]
param(
    [string]$Target = 'G:\Mi unidad\Mi unidad\[Herramientas]\MultiPresence - Discord Presence para multilpes juegos; DMC, Dark souls, Resident etc (WindowsOnly)'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'The .NET SDK was not found. Install it with: winget install Microsoft.DotNet.SDK.9' }

$staging = Join-Path $root 'dist'
Write-Host '==> Building' -ForegroundColor Cyan
& $dotnet publish (Join-Path $root 'MultiPresence\MultiPresence.csproj') `
    -c Release -r win-x64 --self-contained false -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $staging 'MultiPresence.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "The publish did not produce $exe" }

Write-Host '==> Deploying to' $Target -ForegroundColor Cyan
if (-not (Test-Path -LiteralPath $Target)) { throw "The target folder does not exist: $Target" }

# Presence templates. Copy them one by one so a renamed config never drags the whole folder over.
$srcConfig = Join-Path $staging 'Assets\Config'
$dstConfig = Join-Path $Target 'Assets\config'
New-Item -ItemType Directory -Force -Path $dstConfig | Out-Null
$copied = 0
foreach ($file in Get-ChildItem -LiteralPath $srcConfig -Filter *.json) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $dstConfig $file.Name) -Force
    $copied++
}
Write-Host "    $copied presence config(s) copied" -ForegroundColor DarkGray

# The executable. MultiPresenceGame.exe, steam_api64.dll and blacklist.txt are signed release
# files, so they are left untouched; MultiPresence.exe must be closed before this runs.
$dstExe = Join-Path $Target 'MultiPresence.exe'
if (Get-Process -Name 'MultiPresence' -ErrorAction SilentlyContinue) {
    throw 'MultiPresence is still running. Close the tray icon first.'
}
Remove-Item -LiteralPath $dstExe -Force
Copy-Item -LiteralPath $exe -Destination $dstExe -Force

$expected = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$actual = (Get-FileHash -LiteralPath $dstExe -Algorithm SHA256).Hash
if ($expected -ne $actual) { throw 'The deployed executable does not match the build.' }

Write-Host '==> Done.' $dstExe 'deployed and hash verified.' -ForegroundColor Green
Write-Host '    Remember to set "AppId" in Assets\config\Sekiro.json for the Sekiro presence.' -ForegroundColor Yellow