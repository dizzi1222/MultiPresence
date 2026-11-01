<#
Builds the fork and updater, backs up an existing installation, and deploys it.
Existing JSON configurations are preserved; missing templates are added.
Without -Target, uses the desktop MultiPresence shortcut. Requires .NET 9 SDK.
#>
[CmdletBinding()]
param([string]$Target)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $Target) {
    $shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'MultiPresence - Acceso directo.lnk'
    if (-not (Test-Path -LiteralPath $shortcut)) { throw 'Desktop shortcut not found. Pass -Target.' }
    $shell = New-Object -ComObject WScript.Shell
    $Target = Split-Path -Path $shell.CreateShortcut($shortcut).TargetPath -Parent
}
$Target = (Resolve-Path -LiteralPath $Target).ProviderPath
$targetExe = Join-Path $Target 'MultiPresence.exe'
if (-not (Test-Path -LiteralPath $targetExe -PathType Leaf)) {
    throw "Not an existing MultiPresence installation: $Target"
}

function Assert-InstallationStopped {
    foreach ($process in Get-Process -Name MultiPresence,Updater -ErrorAction SilentlyContinue) {
        if ($process.Path -and (Split-Path -Path $process.Path -Parent) -eq $Target) {
            throw "Close $($process.ProcessName) in $Target before deploying."
        }
    }
}
Assert-InstallationStopped

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$staging = Join-Path $root "dist\$stamp"
$updaterOutput = Join-Path $root "artifacts\updater-$stamp"
& $dotnet publish (Join-Path $root 'MultiPresence\MultiPresence.csproj') -c Release -r win-x64 --self-contained false -o $staging --nologo
if ($LASTEXITCODE -ne 0) { throw "MultiPresence publish failed: $LASTEXITCODE" }
& $dotnet publish (Join-Path $root 'Updater\Updater.csproj') -c Release -r win-x64 --self-contained false -o $updaterOutput --nologo
if ($LASTEXITCODE -ne 0) { throw "Updater publish failed: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $updaterOutput 'Updater.exe') -Destination $staging
foreach ($name in 'MultiPresence.exe','Updater.exe') {
    if (-not (Test-Path -LiteralPath (Join-Path $staging $name))) { throw "Missing build output: $name" }
}
Assert-InstallationStopped

# Only files in this fresh publish are deployed. Never copy a developer's config into an install.
$payload = @(Get-ChildItem -LiteralPath $staging -File -Recurse | Where-Object Extension -ne '.pdb' | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($staging, $_.FullName)
    if ($relative -match '^\.\.' -or [IO.Path]::IsPathRooted($relative)) { throw 'Invalid publish path.' }
    if ($relative -notmatch '^Assets[\\/]Config[\\/]') {
        [pscustomobject]@{ Source = $_.FullName; Relative = $relative }
    }
})
$configRoot = Join-Path $Target 'Assets\Config'
$preserved = @(Get-ChildItem -LiteralPath $configRoot -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
    [pscustomobject]@{ Relative = [IO.Path]::GetRelativePath($Target, $_.FullName); Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'MultiPresence\Config') -Filter *.json -File) {
    $relative = Join-Path 'Assets\Config' $file.Name
    if (-not (Test-Path -LiteralPath (Join-Path $Target $relative))) {
        $payload += [pscustomobject]@{ Source = $file.FullName; Relative = $relative }
    }
}

$backup = Join-Path $Target "Backups\update-$stamp"
[IO.Directory]::CreateDirectory($backup) | Out-Null
$backedUp = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($relative in (@($payload.Relative) + @($preserved.Relative))) {
    $path = Join-Path $Target $relative
    if ((Test-Path -LiteralPath $path -PathType Leaf) -and $backedUp.Add($relative)) {
        $copy = Join-Path $backup $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($copy)) | Out-Null
        Copy-Item -LiteralPath $path -Destination $copy
        if ((Get-FileHash -LiteralPath $path).Hash -ne (Get-FileHash -LiteralPath $copy).Hash) {
            throw "Backup verification failed: $relative"
        }
    }
}

$written = [Collections.Generic.List[string]]::new()
$verified = @()
try {
    Assert-InstallationStopped
    foreach ($file in $payload) {
        $destination = Join-Path $Target $file.Relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        $hash = (Get-FileHash -LiteralPath $file.Source -Algorithm SHA256).Hash
        $temporary = $destination + '.deploy-' + [guid]::NewGuid().ToString('N') + '.tmp'
        try {
            Copy-Item -LiteralPath $file.Source -Destination $temporary
            if ($hash -ne (Get-FileHash -LiteralPath $temporary).Hash) { throw "Staging failed: $($file.Relative)" }
            [IO.File]::Move($temporary, $destination, $true)
            $written.Add($file.Relative)
        } finally {
            if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary -Force }
        }
        if ($hash -ne (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
            throw "Deployment verification failed: $($file.Relative)"
        }
        $verified += [pscustomobject]@{ File = $file.Relative; Sha256 = $hash }
    }
    foreach ($file in $preserved) {
        if ($file.Sha256 -ne (Get-FileHash -LiteralPath (Join-Path $Target $file.Relative)).Hash) {
            throw "An existing configuration changed: $($file.Relative)"
        }
    }
} catch {
    $failure = $_
    foreach ($relative in $written) {
        $destination = Join-Path $Target $relative
        if ($backedUp.Contains($relative)) {
            Copy-Item -LiteralPath (Join-Path $backup $relative) -Destination $destination -Force
        } elseif (Test-Path -LiteralPath $destination -PathType Leaf) {
            Remove-Item -LiteralPath $destination -Force
        }
    }
    throw $failure
}

$revision = (& git -C $root rev-parse HEAD)
[ordered]@{ Revision = $revision; Target = $Target; Build = $staging; Files = $verified; PreservedConfiguration = $preserved } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'deployment.json') -Encoding utf8
Write-Host "Deployed and hash-verified $($verified.Count) files; preserved $($preserved.Count) configuration files."
[pscustomobject]@{ Target = $Target; Build = $staging; Backup = $backup }
