$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\ConfigMigration.ps1')
$fixture=@'
[1. Общие]
Включить мод = true
Other value = 0.75

[2. Оружие — общие настройки]
# Setting type: Single
Яркость света = 100
Свечение = 50.5
Непрозрачность = 35.49
Размер = 55.0
Количество частиц = 40

[3. Оружие — этапы]
Запуск атаки = 35
Полёт = 100
Попадание = 35

[4. Магический щит]
Яркость света = 35
Свечение = 45
Непрозрачность = 30
## Old dimension
# Setting type: Single
Ширина = 75
Высота = 100
Глубина = 75
Изменять цвет = true
Цвет = 3380FFFF
Смещение = {"x":0.255,"y":-0.255,"z":1.0}

[Unrelated section]
Цвет = ABCDEF00
Размер = 2.75
'@
$converted=ConvertTo-IntegerEffectsConfig $fixture
foreach($line in @('Свечение = 51','Непрозрачность = 35','Размер = 55','Красный = 51','Зелёный = 128','Синий = 255','Смещение X, см = 26','Смещение Y, см = -26','Смещение Z, см = 100','Изменять цвет = true','Полёт = 100','Запуск атаки = 35','Попадание = 35','Other value = 0.75','Размер = 2.75','Цвет = ABCDEF00')) {
    if($converted -notmatch ('(?m)^'+[regex]::Escape($line)+'\r?$')){throw "Migration mismatch: $line"}
}
if($converted -match '(?m)^(?:Ширина|Высота|Глубина)\s*='){throw 'Retired dimensions remain'}
if((ConvertTo-IntegerEffectsConfig $converted) -cne $converted){throw 'Repeated migration changes the config'}
$failed=$false
try { ConvertTo-IntegerEffectsConfig $fixture.Replace('Цвет = 3380FFFF','Цвет = invalid') | Out-Null } catch { $failed=$true }
if(-not $failed){throw 'Invalid legacy color was not rejected'}
Write-Output 'Integer config migration checks passed: rounding, percentage preservation, RGB, signed centimeters, unrelated values, retired keys, idempotence and malformed input.'
$stageConfig=ConvertTo-StageInfluenceConfig $fixture -InitializeStageInfluence
foreach($line in @('Запуск атаки = 100','Полёт = 0','Попадание = 100','Свечение = 51','Количество частиц = 40','Изменять цвет = true','Красный = 51','Other value = 0.75')) {
    if($stageConfig -notmatch ('(?m)^'+[regex]::Escape($line)+'\r?$')){throw "Stage influence migration mismatch: $line"}
}
if((ConvertTo-StageInfluenceConfig $stageConfig -InitializeStageInfluence) -cne $stageConfig){throw 'Stage initialization is not idempotent'}
$customized=$stageConfig.Replace('Полёт = 0','Полёт = 65')
if((ConvertTo-StageInfluenceConfig $customized) -cne $customized){throw 'Reinstall overwrites new stage selections'}
Write-Output 'Stage influence migration checks passed: requested preset, globals preserved, idempotence and later selections preserved.'
$ovalConfig=ConvertTo-OvalShieldConfig $converted
foreach($line in @('Цвет = 3380FF','Овальность = 50','Свечение = 51','Количество частиц = 40','Изменять цвет = true','Полёт = 100','Other value = 0.75')) {
    if($ovalConfig -notmatch ('(?m)^'+[regex]::Escape($line)+'\r?$')){throw "Oval shield migration mismatch: $line"}
}
if($ovalConfig -match '(?m)^(?:Красный|Зелёный|Синий)\s*='){throw 'Separate RGB entries remain'}
if((ConvertTo-OvalShieldConfig $ovalConfig) -cne $ovalConfig){throw 'Oval shield migration is not idempotent'}
$customOval=$ovalConfig.Replace('Овальность = 50','Овальность = 80').Replace('Цвет = 3380FF','Цвет = 00FF00')
if((ConvertTo-OvalShieldConfig $customOval) -cne $customOval){throw 'Reinstall resets selected shape or color'}
$oldColorConfig=ConvertTo-OvalShieldConfig $fixture
if($oldColorConfig -notmatch '(?m)^Цвет = 3380FF\r?$'){throw 'Old single color was not preserved'}
$failed=$false
try { ConvertTo-OvalShieldConfig $converted.Replace('Красный = 51','Красный = 300') | Out-Null } catch { $failed=$true }
if(-not $failed){throw 'Invalid channel was not rejected'}
Write-Output 'Oval shield migration checks passed: original color preserved, RGB sliders removed, oval default, idempotence and later selections preserved.'
$englishConfig=ConvertTo-EnglishEffectsConfig $fixture
foreach($line in @('[1. General]','[2. Weapons - General]','[3. Weapons - Stages]','[4. Magic Shield]','Enable mod = true','Light brightness = 100','Glow = 51','Opacity = 35','Size = 55','Particle count = 40','Attack launch = 35','Flight = 100','Impact = 35','Change color = true','Color = 217','Ovality = 50','Offset X, cm = 26','Offset Y, cm = -26','Offset Z, cm = 100','Other value = 0.75','Цвет = ABCDEF00','Размер = 2.75')) {
    if($englishConfig -notmatch ('(?m)^'+[regex]::Escape($line)+'\r?$')){throw "English migration mismatch: $line"}
}
if((ConvertTo-EnglishEffectsConfig $englishConfig) -cne $englishConfig){throw 'English conversion is not idempotent'}
$englishOwnSections=($englishConfig -split '\[Unrelated section\]')[0]
if($englishOwnSections -match '[\u0400-\u04FF]'){throw 'Localized keys or comments remain in mod sections'}
$failed=$false
try { ConvertTo-EnglishEffectsConfig ($fixture+"`n[4. Magic Shield]`nColor = FF0000`n") | Out-Null } catch {$failed=$true}
if(-not $failed){throw 'Ambiguous mixed sections were not rejected'}
Write-Output 'English migration checks passed: all labels, values and unrelated sections preserved; English comments, idempotence and mixed-section guard verified.'
$fixtureDirectory=Join-Path $PSScriptRoot '..\artifacts'
New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $fixtureDirectory 'english-migration-fixture.cfg'),$englishConfig,(New-Object Text.UTF8Encoding $false))
$colors=[ordered]@{FF0000=0;'#00FF00'=120; '0000FF'=240; FFFF00=60; '00FFFF'=180; FF00FF=300; '3380FFFF'=217; '330000'=0; '808080'=0; '000000'=0; '360'=360; '42'=42}
foreach($color in $colors.Keys) {
    $old="[4. Magic Shield]`n# Setting type: String`nColor = $color`nOpacity = 25`n`n[Unrelated]`nColor = ABCDEF`n"
    $result=ConvertTo-EnglishEffectsConfig $old
    if($result -notmatch ('(?m)^Color = '+$colors[$color]+'\r?$')){throw "Hue conversion failed: $color"}
    if($result -notmatch '(?m)^Color = ABCDEF\r?$' -or $result -notmatch '(?m)^Opacity = 25\r?$'){throw 'Hue conversion changed unrelated settings'}
    if((ConvertTo-EnglishEffectsConfig $result) -cne $result){throw 'Hue conversion is not idempotent'}
}
foreach($invalid in @('invalid','361','-1')) {
    $failed=$false
    try { ConvertTo-EnglishEffectsConfig "[4. Magic Shield]`nColor = $invalid`n" | Out-Null } catch {$failed=$true}
    if(-not $failed){throw "Invalid hue was accepted: $invalid"}
}
Write-Output 'Hue migration checks passed: RGB primaries, secondary hues, dark colors, integer selections, range guards, unrelated values and idempotence.'
