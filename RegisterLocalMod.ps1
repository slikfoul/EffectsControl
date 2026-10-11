param([string]$Profile = 'Default')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ConfigMigration.ps1')
if ($Profile -notmatch '^[A-Za-z0-9 _-]+$') { throw 'Invalid profile name.' }
function AssertClosed {
    # Overwolf may keep running in the tray after the Thunderstore app is closed.
    $blockingProcesses = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(valheim|Thunderstore.*)$' })
    if ($blockingProcesses.Count) {
        $names = ($blockingProcesses.ProcessName | Sort-Object -Unique) -join ', '
        throw ('Close Valheim and Thunderstore before updating the registered local mod. Still running: ' + $names)
    }
}
AssertClosed
$dataRoot = Join-Path $env:APPDATA 'Thunderstore Mod Manager\DataFolder\Valheim'
$profileRoot = Join-Path $dataRoot ('profiles\' + $Profile)
$registryPath = Join-Path $profileRoot 'mods.yml'
if (-not (Test-Path -LiteralPath $registryPath)) { throw 'The existing profile registry was not found.' }
$originalRegistry = [IO.File]::ReadAllBytes($registryPath)
$registryText = [Text.Encoding]::UTF8.GetString($originalRegistry).TrimStart([char]0xFEFF)
$blocks = [regex]::Matches($registryText, '(?ms)^- manifestVersion:.*?(?=^- manifestVersion:|\z)')
$existing = @($blocks | Where-Object { $_.Value -match '(?m)^  name: Slikfoul-EffectsControl\r?$' })
if ($existing.Count -gt 1) { throw 'Duplicate EffectsControl entries exist; leave the registry untouched.' }
$enabled = $existing.Count -eq 0 -or $existing[0].Value -notmatch '(?m)^  enabled: false\r?$'
$initializeStageInfluence=$true
if($existing.Count) {
    $registeredVersion=[regex]::Match($existing[0].Value,'(?m)^  versionNumber:\r?\n    major: (\d+)\r?\n    minor: (\d+)\r?\n    patch: (\d+)')
    if(-not $registeredVersion.Success){throw 'Cannot determine installed EffectsControl version; leave config untouched.'}
    $oldVersion=[version]('{0}.{1}.{2}' -f $registeredVersion.Groups[1].Value,$registeredVersion.Groups[2].Value,$registeredVersion.Groups[3].Value)
    $initializeStageInfluence=$oldVersion -lt [version]'0.0.4'
}
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'packaging\manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.version_number
$archivePath = Join-Path $PSScriptRoot "artifacts\Slikfoul-EffectsControl-$version.zip"
if (-not (Test-Path -LiteralPath $archivePath)) { throw 'Build the Thunderstore package first.' }
$pluginsRoot = [IO.Path]::GetFullPath((Join-Path $profileRoot 'BepInEx\plugins'))
$manualDirectory = [IO.Path]::GetFullPath((Join-Path $pluginsRoot 'EffectsControl'))
$managerDirectory = [IO.Path]::GetFullPath((Join-Path $pluginsRoot 'Slikfoul-EffectsControl'))
$managedPluginDirectory = [IO.Path]::GetFullPath((Join-Path $managerDirectory 'EffectsControl'))
$configPath = Join-Path $profileRoot 'BepInEx\config\Slikfoul.EffectsControl.cfg'
$originalConfigHash = if (Test-Path -LiteralPath $configPath) { (Get-FileHash -LiteralPath $configPath).Hash } else { $null }
$originalConfigBytes = if ($originalConfigHash) { [IO.File]::ReadAllBytes($configPath) } else { $null }
$updatedConfig = if ($originalConfigHash) { ConvertTo-EnglishEffectsConfig ([IO.File]::ReadAllText($configPath)) -InitializeStageInfluence:$initializeStageInfluence } else { $null }
$configChanged = $false
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $dataRoot 'cache\Slikfoul-EffectsControl'))
$cacheDirectory = [IO.Path]::GetFullPath((Join-Path $cacheRoot $version))
foreach ($target in @($manualDirectory, $managerDirectory, $managedPluginDirectory)) {
    if (-not $target.StartsWith($pluginsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Resolved plugin path escaped the intended profile.'
    }
}
$unexpectedCopies = @(Get-ChildItem -LiteralPath $pluginsRoot -Recurse -File | Where-Object {
    $_.Name -in @('EffectsControl.dll','EffectsControl.dll.old') -and
    -not $_.FullName.StartsWith($managerDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
    -not $_.FullName.StartsWith($manualDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
})
if ($unexpectedCopies.Count) { throw ('Unexpected duplicate copies need review: ' + ($unexpectedCopies.FullName -join ', ')) }
if (-not $cacheDirectory.StartsWith($cacheRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved cache path escaped this mod cache.'
}
if (Test-Path -LiteralPath $manualDirectory) {
    $manualEntries = @(Get-ChildItem -LiteralPath $manualDirectory -Force)
    if (@($manualEntries | Where-Object { $_.PSIsContainer -or $_.Name -notin @('EffectsControl.dll','EffectsControl.dll.config') }).Count) {
        throw 'The old manual directory has unexpected files; leave it untouched.'
    }
}
$backupDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('backups\before-local-import-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))))
if (-not $backupDirectory.StartsWith([IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Backup path escaped the project.'
}
New-Item -ItemType Directory -Path $backupDirectory | Out-Null
[IO.File]::WriteAllBytes((Join-Path $backupDirectory 'mods.yml'), $originalRegistry)
if ($originalConfigHash) { Copy-Item -LiteralPath $configPath -Destination (Join-Path $backupDirectory 'Slikfoul.EffectsControl.cfg') }
$stagingDirectory = Join-Path $backupDirectory 'new-cache'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $stagingDirectory)
$required = @('manifest.json','README.md','CHANGELOG.md','icon.png','LICENSE','BepInEx/plugins/EffectsControl/EffectsControl.dll')
if (@(Get-ChildItem -LiteralPath $stagingDirectory -Recurse -File).Count -ne $required.Count) { throw 'The archive has unexpected contents.' }
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $stagingDirectory $relative))) { throw "Missing archive content: $relative" }
}
foreach ($fileName in @('EffectsControl.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $stagingDirectory ('BepInEx\plugins\EffectsControl\' + $fileName))).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('bin\Release\' + $fileName))).Hash) {
        throw 'The archive and current build differ.'
    }
}
$parts = $version.Split('.')
$installedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$managerManifest = [ordered]@{
    manifestVersion = 1
    name = 'Slikfoul-EffectsControl'
    authorName = 'Slikfoul'
    websiteUrl = $manifest.website_url
    displayName = 'EffectsControl'
    description = $manifest.description
    gameVersion = '0'
    networkMode = 'both'
    packageType = 'other'
    installMode = 'managed'
    installedAtTime = $installedAt
    loaders = @()
    dependencies = @($manifest.dependencies)
    incompatibilities = @()
    optionalDependencies = @()
    versionNumber = @{ major = [int]$parts[0]; minor = [int]$parts[1]; patch = [int]$parts[2] }
    enabled = $enabled
    icon = Join-Path $cacheDirectory 'icon.png'
    onlineSource = $false
    trustedPackage = $false
}
[IO.File]::WriteAllText((Join-Path $stagingDirectory 'mm_v2_manifest.json'), ($managerManifest | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))
$description = $manifest.description.Replace("'", "''")
$websiteUrl = $manifest.website_url.Replace("'", "''")
$enabledYaml = $enabled.ToString().ToLowerInvariant()
$entry = @"
- manifestVersion: 1
  name: Slikfoul-EffectsControl
  authorName: Slikfoul
  websiteUrl: '$websiteUrl'
  displayName: EffectsControl
  description: '$description'
  gameVersion: '0'
  networkMode: both
  packageType: other
  installMode: managed
  installedAtTime: $installedAt
  loaders: []
  dependencies:
    - denikson-BepInExPack_Valheim-5.4.2351
  incompatibilities: []
  optionalDependencies: []
  versionNumber:
    major: $($parts[0])
    minor: $($parts[1])
    patch: $($parts[2])
  enabled: $enabledYaml
  onlineSource: false
  trustedPackage: false
"@
if ($existing.Count) {
    $match = $existing[0]
    $updatedRegistry = $registryText.Substring(0,$match.Index) + $entry + [Environment]::NewLine + $registryText.Substring($match.Index + $match.Length)
} else {
    $updatedRegistry = $registryText.TrimEnd() + [Environment]::NewLine + $entry + [Environment]::NewLine
}
$backupManual = Join-Path $backupDirectory 'previous-manual'
$backupManager = Join-Path $backupDirectory 'previous-managed'
$backupCache = Join-Path $backupDirectory 'previous-cache'
$tempRegistryPath = Join-Path $profileRoot ('mods.yml.effectscontrol-import-' + [Guid]::NewGuid().ToString('N'))
$registryChanged = $false
$newManagerCreated = $false
$newCacheInstalled = $false
try {
    AssertClosed
    if ((Get-FileHash -LiteralPath $registryPath).Hash -ne (Get-FileHash -LiteralPath (Join-Path $backupDirectory 'mods.yml')).Hash) { throw 'The registry changed during preparation.' }
    # Recursive moves use absolute paths verified above and only this mod's directories.
    if (Test-Path -LiteralPath $manualDirectory) { Move-Item -LiteralPath $manualDirectory -Destination $backupManual }
    if (Test-Path -LiteralPath $managerDirectory) { Move-Item -LiteralPath $managerDirectory -Destination $backupManager }
    if (Test-Path -LiteralPath $cacheDirectory) { Move-Item -LiteralPath $cacheDirectory -Destination $backupCache }
    New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
    Move-Item -LiteralPath $stagingDirectory -Destination $cacheDirectory
    $newCacheInstalled = $true
    New-Item -ItemType Directory -Path $managerDirectory | Out-Null
    $newManagerCreated = $true
    foreach ($document in @('manifest.json','README.md','CHANGELOG.md','icon.png','LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $cacheDirectory $document) -Destination (Join-Path $managerDirectory $document)
    }
    Copy-Item -LiteralPath (Join-Path $cacheDirectory 'BepInEx\plugins\EffectsControl') -Destination $managedPluginDirectory -Recurse
    if (-not $enabled) {
        foreach ($file in Get-ChildItem -LiteralPath $managerDirectory -Recurse -File) {
            Rename-Item -LiteralPath $file.FullName -NewName ($file.Name + '.old')
        }
    }
    [IO.File]::WriteAllText($tempRegistryPath, $updatedRegistry, (New-Object Text.UTF8Encoding $false))
    # PowerShell can convert a null string argument to an empty path; use a real backup path.
    [IO.File]::Replace($tempRegistryPath, $registryPath, (Join-Path $backupDirectory 'atomic-registry-backup.yml'))
    $registryChanged = $true
    foreach ($fileName in @('EffectsControl.dll')) {
        $installedFileName = if ($enabled) { $fileName } else { $fileName + '.old' }
        if ((Get-FileHash -LiteralPath (Join-Path $managedPluginDirectory $installedFileName)).Hash -ne
            (Get-FileHash -LiteralPath (Join-Path $cacheDirectory ('BepInEx\plugins\EffectsControl\' + $fileName))).Hash) {
            throw 'Registered plugin differs from the archive.'
        }
    }
    $savedRegistry = [IO.File]::ReadAllText($registryPath)
    $newBlocks = [regex]::Matches($savedRegistry, '(?ms)^- manifestVersion:.*?(?=^- manifestVersion:|\z)')
    $othersBefore = @($blocks | Where-Object { $_.Value -notmatch '(?m)^  name: Slikfoul-EffectsControl\r?$' } | ForEach-Object { $_.Value.TrimEnd() })
    $othersAfter = @($newBlocks | Where-Object { $_.Value -notmatch '(?m)^  name: Slikfoul-EffectsControl\r?$' } | ForEach-Object { $_.Value.TrimEnd() })
    if (($othersBefore -join [Environment]::NewLine) -cne ($othersAfter -join [Environment]::NewLine)) { throw 'Other registry entries changed.' }
    if ([regex]::Matches($savedRegistry, '(?m)^  name: Slikfoul-EffectsControl\r?$').Count -ne 1) { throw 'Expected exactly one manager entry.' }
    if (Test-Path -LiteralPath $manualDirectory) { throw 'The old unregistered copy still exists.' }
    $copies = @(Get-ChildItem -LiteralPath $pluginsRoot -Recurse -File | Where-Object { $_.Name -in @('EffectsControl.dll','EffectsControl.dll.old') })
    if ($copies.Count -ne 1) { throw 'Expected exactly one package-managed EffectsControl DLL.' }
    if ($originalConfigHash -and (Get-FileHash -LiteralPath $configPath).Hash -cne $originalConfigHash) { throw 'Existing config changed during registration.' }
    if ([Reflection.AssemblyName]::GetAssemblyName($copies[0].FullName).Version.ToString(3) -cne $version) { throw 'Installed DLL version differs from package.' }
    AssertClosed
    if ($originalConfigHash -and $updatedConfig -cne [IO.File]::ReadAllText($configPath)) {
        $configChanged=$true
        [IO.File]::WriteAllText($configPath,$updatedConfig,(New-Object Text.UTF8Encoding $false))
        if ([IO.File]::ReadAllText($configPath) -cne $updatedConfig) { throw 'Config migration verification failed.' }
    }
    [ordered]@{ version=$version; profile=$Profile; enabled=$enabled; dll=$copies[0].FullName; dllSha256=(Get-FileHash -LiteralPath $copies[0].FullName).Hash; archiveSha256=(Get-FileHash -LiteralPath $archivePath).Hash; configPreserved=$true; configMigrated=$configChanged; backup=$backupDirectory } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot "artifacts\installation-$version.json") -Encoding utf8
    Write-Output "Registered EffectsControl $version as a local mod in $Profile; version, icon, cache and installed files verified."
}
catch {
    if ($configChanged) { [IO.File]::WriteAllBytes($configPath,$originalConfigBytes) }
    if ($registryChanged) { [IO.File]::WriteAllBytes($registryPath, $originalRegistry) }
    if ($newManagerCreated) { Move-Item -LiteralPath $managerDirectory -Destination (Join-Path $backupDirectory 'failed-managed') }
    if ($newCacheInstalled) { Move-Item -LiteralPath $cacheDirectory -Destination (Join-Path $backupDirectory 'failed-cache') }
    if (Test-Path -LiteralPath $backupManager) { Move-Item -LiteralPath $backupManager -Destination $managerDirectory }
    if (Test-Path -LiteralPath $backupManual) { Move-Item -LiteralPath $backupManual -Destination $manualDirectory }
    if (Test-Path -LiteralPath $backupCache) { Move-Item -LiteralPath $backupCache -Destination $cacheDirectory }
    throw
}
finally {
    if (Test-Path -LiteralPath $tempRegistryPath) { Remove-Item -LiteralPath $tempRegistryPath }
}
