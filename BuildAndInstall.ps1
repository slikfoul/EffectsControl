param([string]$Profile='Default')
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'BuildPackage.ps1')
& (Join-Path $PSScriptRoot 'RegisterLocalMod.ps1') -Profile $Profile
