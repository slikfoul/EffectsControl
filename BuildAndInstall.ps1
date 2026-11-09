param([string]$Profile='Default', [string]$DataRoot = (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim'))
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'BuildPackage.ps1')
& (Join-Path $PSScriptRoot 'RegisterLocalMod.ps1') -Profile $Profile -DataRoot $DataRoot
