<#
    Writes your Discord Application ID into the Sekiro and Bloodborne presence configs.

    The id is 18 or 19 digits and is shown in the General Information page of your
    application at https://discord.com/developers/applications

    .\set-appid.ps1
    .\set-appid.ps1 -AppId 1234567890123456789
    .\set-appid.ps1 -AppId 1234567890123456789 -Target "C:\Games\MultiPresence"

    Without -AppId it asks for it interactively. Nothing else in the file is touched.
#>
[CmdletBinding()]
param(
    [string]$AppId,
    [string]$Target = 'G:\Mi unidad\Mi unidad\[Herramientas]\MultiPresence - Discord Presence para multilpes juegos; DMC, Dark souls, Resident etc (WindowsOnly)',
    [string[]]$Games = @('Sekiro', 'Bloodborne')
)

$ErrorActionPreference = 'Stop'

if (-not $AppId) {
    $AppId = Read-Host 'Discord Application ID'
}

$AppId = $AppId.Trim()

# Discord snowflakes are unsigned 64 bit integers, rendered in decimal.
if ($AppId -notmatch '^\d{17,20}$') {
    throw "'$AppId' no parece un Application ID de Discord (se esperan 17 a 20 digitos)."
}

$configDir = Join-Path $Target 'Assets\config'
if (-not (Test-Path -LiteralPath $configDir)) {
    throw "No existe la carpeta de configs: $configDir"
}

foreach ($game in $Games) {
    $path = Join-Path $configDir "$game.json"
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Warning "No existe $path, se omite $game."
        continue
    }

    # Respaldo por si el archivo esta a medio editar.
    $backup = "$path.bak"
    if (-not (Test-Path -LiteralPath $backup)) {
        Copy-Item -LiteralPath $path -Destination $backup
    }

    # Se reemplaza solo la linea del AppId en vez de reserializar el archivo entero, para no
    # reformatear los placeholders que el usuario edita a mano.
    $raw = Get-Content -LiteralPath $path -Raw

    if ($raw -match '"AppId"\s*:\s*"[^"]*"') {
        $raw = [regex]::Replace($raw, '"AppId"\s*:\s*"[^"]*"', ('"AppId": "' + $AppId + '"'), 1)
    } else {
        $raw = [regex]::Replace($raw, '(\s*\{)', "`$1`n  `"AppId`": `"$AppId`",", 1)
    }

    Set-Content -LiteralPath $path -Value $raw -Encoding UTF8 -NoNewline

    Write-Host "$game.json -> AppId $AppId" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Listo. Reinicia MultiPresence para que tome el id.' -ForegroundColor Cyan
Write-Host 'Discord debe estar abierto; sin Discord no hay presencia.' -ForegroundColor DarkGray