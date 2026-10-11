function Remove-RetiredShieldConfigEntries {
    param([Parameter(Mandatory)][string]$Text)
    $sectionPattern='(?ms)(^\[4\. Магический щит\]\r?\n)(.*?)(?=^\[|\z)'
    $sections=[regex]::Matches($Text,$sectionPattern)
    if($sections.Count -gt 1){throw 'Duplicate shield config sections; leave config untouched.'}
    if($sections.Count -eq 0){return $Text}
    $section=$sections[0]
    $clean=[regex]::Replace($section.Groups[2].Value,'(?m)(?:^#[^\r\n]*\r?\n)*^(?:Ширина|Высота|Глубина)\s*=[^\r\n]*(?:\r?\n|$)','')
    return $Text.Substring(0,$section.Index)+$section.Groups[1].Value+$clean+$Text.Substring($section.Index+$section.Length)
}

function ConvertTo-IntegerEffectsConfig {
    param([Parameter(Mandatory)][string]$Text,[switch]$KeepSingleShieldColor)
    $Text=Remove-RetiredShieldConfigEntries $Text
    $percentKeys='Яркость света|Свечение|Непрозрачность|Размер|Количество частиц|Запуск атаки|Полёт|Попадание'
    $sections='(?ms)(^\[(?:2\. Оружие — общие настройки|3\. Оружие — этапы|4\. Магический щит)\]\r?\n)(.*?)(?=^\[|\z)'
    return [regex]::Replace($Text,$sections,[Text.RegularExpressions.MatchEvaluator]{param($section)
        $body=[regex]::Replace($section.Groups[2].Value,"(?m)^($percentKeys)(\s*=\s*)([^\r\n]*)",[Text.RegularExpressions.MatchEvaluator]{param($entry)
            $parsed=0.0
            $inputValue=$entry.Groups[3].Value.Trim().Replace(',','.')
            if(-not [double]::TryParse($inputValue,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$parsed) -or [double]::IsNaN($parsed) -or [double]::IsInfinity($parsed)){throw 'Invalid numeric setting; leave config untouched.'}
            $whole=[int][math]::Clamp([math]::Round($parsed,[MidpointRounding]::AwayFromZero),0,100)
            return $entry.Groups[1].Value+$entry.Groups[2].Value+$whole.ToString([Globalization.CultureInfo]::InvariantCulture)
        })
        $body=$body.Replace('# Setting type: Single','# Setting type: Int32')
        if($section.Groups[1].Value -match '4\. Магический щит') {
            $newValues=[ordered]@{}
            $oldColor=[regex]::Match($body,'(?m)^Цвет\s*=\s*([^\r\n]*)')
            if($oldColor.Success -and -not $KeepSingleShieldColor) {
                $hex=$oldColor.Groups[1].Value.Trim()
                if($hex -notmatch '^[0-9a-fA-F]{8}$'){throw 'Invalid legacy shield color; leave config untouched.'}
                $newValues['Красный']=[Convert]::ToInt32($hex.Substring(0,2),16)
                $newValues['Зелёный']=[Convert]::ToInt32($hex.Substring(2,2),16)
                $newValues['Синий']=[Convert]::ToInt32($hex.Substring(4,2),16)
                $body=[regex]::Replace($body,'(?m)(?:^#[^\r\n]*\r?\n)*^Цвет\s*=[^\r\n]*(?:\r?\n|$)','')
            }
            $oldOffset=[regex]::Match($body,'(?m)^Смещение\s*=\s*([^\r\n]*)')
            if($oldOffset.Success) {
                $offset=$oldOffset.Groups[1].Value | ConvertFrom-Json
                foreach($axis in @('x','y','z')) {
                    if($offset.PSObject.Properties.Name -notcontains $axis){throw 'Invalid legacy shield offset; leave config untouched.'}
                    $value=[double]$offset.$axis
                    if([double]::IsNaN($value) -or [double]::IsInfinity($value)){throw 'Invalid legacy shield offset; leave config untouched.'}
                    $newValues[('Смещение '+$axis.ToUpperInvariant()+', см')]=[int][math]::Clamp([math]::Round($value*100,[MidpointRounding]::AwayFromZero),-1000,1000)
                }
                $body=[regex]::Replace($body,'(?m)(?:^#[^\r\n]*\r?\n)*^Смещение\s*=[^\r\n]*(?:\r?\n|$)','')
            }
            foreach($key in $newValues.Keys) {
                if($body -notmatch ('(?m)^'+[regex]::Escape($key)+'\s*=')) { $body += "`n# Setting type: Int32`n$key = $($newValues[$key])`n" }
            }
        }
        return $section.Groups[1].Value+$body
    })
}

function ConvertTo-StageInfluenceConfig {
    param([Parameter(Mandatory)][string]$Text,[switch]$InitializeStageInfluence,[switch]$KeepSingleShieldColor)
    $Text=ConvertTo-IntegerEffectsConfig $Text -KeepSingleShieldColor:$KeepSingleShieldColor
    if(-not $InitializeStageInfluence){return $Text}
    $pattern='(?ms)(^\[3\. Оружие — этапы\]\r?\n)(.*?)(?=^\[|\z)'
    if([regex]::Matches($Text,$pattern).Count -gt 1){throw 'Duplicate stage config sections; leave config untouched.'}
    return [regex]::Replace($Text,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($section)
        $body=$section.Groups[2].Value
        foreach($key in @('Запуск атаки','Полёт','Попадание')) {
            $value=if($key -eq 'Полёт'){0}else{100}
            $entryPattern='(?m)(?:^#[^\r\n]*\r?\n)*^'+[regex]::Escape($key)+'\s*=[^\r\n]*(?:\r?\n|$)'
            $replacement="## 100% — настройки пункта 2; 0% — исходный эффект.`n# Setting type: Int32`n# Acceptable value range: From 0 to 100`n$key = $value`n"
            if([regex]::IsMatch($body,$entryPattern)){$body=[regex]::Replace($body,$entryPattern,$replacement)}else{$body+="`n"+$replacement}
        }
        return $section.Groups[1].Value+$body
    })
}

function ConvertTo-OvalShieldConfig {
    param([Parameter(Mandatory)][string]$Text,[switch]$InitializeStageInfluence)
    $Text=ConvertTo-StageInfluenceConfig $Text -InitializeStageInfluence:$InitializeStageInfluence -KeepSingleShieldColor
    $pattern='(?ms)(^\[4\. Магический щит\]\r?\n)(.*?)(?=^\[|\z)'
    return [regex]::Replace($Text,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($section)
        $body=$section.Groups[2].Value
        $color=[regex]::Match($body,'(?m)^Цвет\s*=\s*([^\r\n]*)')
        if($color.Success) {
            $hex=$color.Groups[1].Value.Trim().TrimStart('#')
            if($hex -notmatch '^(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$'){throw 'Invalid single shield color; leave config untouched.'}
            $hex=$hex.Substring(0,6).ToUpperInvariant()
            $body=[regex]::Replace($body,'(?m)^Цвет\s*=[^\r\n]*',"Цвет = $hex")
            $body=$body.Replace('# Setting type: Color','# Setting type: String')
        } else {
            $rgb=@(51,0,0); $keys=@('Красный','Зелёный','Синий')
            for($i=0;$i -lt 3;$i++) {
                $channel=[regex]::Match($body,'(?m)^'+$keys[$i]+'\s*=\s*([^\r\n]*)')
                if($channel.Success) {
                    $value=0
                    if(-not [int]::TryParse($channel.Groups[1].Value.Trim(),[ref]$value) -or $value -lt 0 -or $value -gt 255){throw 'Invalid RGB channel; leave config untouched.'}
                    $rgb[$i]=$value
                }
            }
            $hex='{0:X2}{1:X2}{2:X2}' -f $rgb[0],$rgb[1],$rgb[2]
            $body += "`n## Единый код цвета RGB.`n# Setting type: String`nЦвет = $hex`n"
        }
        $body=[regex]::Replace($body,'(?m)(?:^#[^\r\n]*\r?\n)*^(?:Красный|Зелёный|Синий)\s*=[^\r\n]*(?:\r?\n|$)','')
        if($body -notmatch '(?m)^Овальность\s*='){$body += "`n## 0 — круглая форма; 50 — прежний овал; 100 — узкий овал.`n# Setting type: Int32`n# Acceptable value range: From 0 to 100`nОвальность = 50`n"}
        return $section.Groups[1].Value+$body
    })
}

function ConvertTo-EnglishEffectsConfig {
    param([Parameter(Mandatory)][string]$Text,[switch]$InitializeStageInfluence)
    $sections=[ordered]@{
        '1. Общие'='1. General'; '2. Оружие — общие настройки'='2. Weapons - General'
        '3. Оружие — этапы'='3. Weapons - Stages'; '4. Магический щит'='4. Magic Shield'
    }
    foreach($section in $sections.Keys) {
        $matches=[regex]::Matches($Text,'(?m)^\[(?:'+[regex]::Escape($section)+'|'+[regex]::Escape($sections[$section])+')\]\r?$')
        if($matches.Count -gt 1){throw 'Duplicate localized/English config sections; leave config untouched.'}
    }
    $Text=ConvertTo-OvalShieldConfig $Text -InitializeStageInfluence:$InitializeStageInfluence
    $keys=[ordered]@{
        'Включить мод'='Enable mod'; 'Эффекты оружия'='Weapon effects'; 'Магический щит'='Magic shield'
        'Яркость света'='Light brightness'; 'Свечение'='Glow'; 'Непрозрачность'='Opacity'; 'Размер'='Size'; 'Количество частиц'='Particle count'
        'Запуск атаки'='Attack launch'; 'Полёт'='Flight'; 'Попадание'='Impact'
        'Овальность'='Ovality'; 'Изменять цвет'='Change color'; 'Цвет'='Color'
        'Смещение X, см'='Offset X, cm'; 'Смещение Y, см'='Offset Y, cm'; 'Смещение Z, см'='Offset Z, cm'
    }
    foreach($section in $sections.Keys) {
        $newName=$sections[$section]
        $pattern='(?ms)(^\['+[regex]::Escape($section)+'\]\r?\n)(.*?)(?=^\[|\z)'
        $Text=[regex]::Replace($Text,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($match)
            $body=$match.Groups[2].Value
            foreach($key in $keys.Keys) {
                $englishKey=$keys[$key]
                $entryPattern='(?m)(?:^#[^\r\n]*\r?\n)*^'+[regex]::Escape($key)+'\s*=\s*([^\r\n]*)(?:\r?\n|$)'
                $body=[regex]::Replace($body,$entryPattern,[Text.RegularExpressions.MatchEvaluator]{param($entry)
                    $type=if($englishKey -in @('Enable mod','Weapon effects','Magic shield','Change color')){'Boolean'}elseif($englishKey -eq 'Color'){'String'}else{'Int32'}
                    $description=if($newName -eq '3. Weapons - Stages'){'100% fully applies section 2 settings; 0% restores the original effect. Whole numbers only.'}elseif($englishKey -eq 'Ovality'){'0 is round; 50 is the previous oval appearance; 100 is a narrower oval.'}elseif($type -eq 'Int32'){'Whole numbers only.'}elseif($englishKey -eq 'Color'){'One RGB color code; used when Change color is enabled.'}else{'Enable or disable this setting.'}
                    return "## $description`n# Setting type: $type`n$englishKey = $($entry.Groups[1].Value.Trim())`n"
                })
            }
            return '['+$newName+"]`n"+$body
        })
    }
    return [regex]::Replace($Text,'(?m)^# Configuration file was created by plugin EffectsControl v[0-9.]+','# Configuration file was created by plugin EffectsControl v0.0.6')
}
