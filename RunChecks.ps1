param([string]$UnityEditor='G:\Development\Unity\6000.0.75f1\Editor\Unity.exe')
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'tests\ConfigMigrationChecks.ps1')
$project=Join-Path $PSScriptRoot 'TestProject'
$artifacts=Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$log=Join-Path $artifacts 'unity-checks.log'
$result=Join-Path $artifacts 'unity-checks.txt'
if(Test-Path -LiteralPath $result){Remove-Item -LiteralPath $result}
$arguments=@('-batchmode','-quit','-nographics','-projectPath',('"'+$project+'"'),'-executeMethod','EffectsChecks.Run','-logFile',('"'+$log+'"'))
$process=Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result)){throw "Unity checks failed. Read $log"}
Get-Content -LiteralPath $result
