# EffectsControl

Customize weapon and magic shield visuals in Valheim to keep combat effects clear and comfortable.

## Features

- Adjust weapon light brightness, glow, opacity, size and particle count.
- Choose how strongly these settings affect attack launch, projectile flight and impact.
- Customize magic shield light brightness, glow, opacity, ovality and color.
- Select shield color with one hue slider.
- Use whole-number settings with immediate updates through F1 Configuration Manager.
- Restore original visuals by disabling the mod or its weapon and shield controls.

## Screenshots

### Frost attack effects

![Frost attack effects](https://raw.githubusercontent.com/slikfoul/EffectsControl/v1.0.0/docs/screenshots/frost-attack.png)

### Oval magic shield

![Oval magic shield](https://raw.githubusercontent.com/slikfoul/EffectsControl/v1.0.0/docs/screenshots/magic-shield.png)

### Melee impact effects

![Melee impact effects](https://raw.githubusercontent.com/slikfoul/EffectsControl/v1.0.0/docs/screenshots/melee-impact.png)

## Weapon settings

**2. Weapons - General** sets the appearance of weapon effects. All values range from **0 to 100**.

| Setting | Default |
|---|---:|
| Light brightness | 0 |
| Glow | 50 |
| Opacity | 25 |
| Size | 100 |
| Particle count | 100 |

**3. Weapons - Stages** determines how strongly each stage uses the general settings.

| Stage | Default |
|---|---:|
| Attack launch | 100 |
| Flight | 0 |
| Impact | 100 |

- **0** keeps the original effect.
- **100** fully applies the general settings.
- Values between 0 and 100 blend between the original and configured appearance.

With the defaults, attack launch and impact use the general settings, while flying projectiles retain their original appearance.

## Magic shield settings

**4. Magic Shield** controls the Staff of Protection's visual shield.

| Setting | Default |
|---|---:|
| Light brightness | 35 |
| Glow | 45 |
| Opacity | 30 |
| Ovality | 50 |
| Change color | Off |
| Color | 0 |

**Ovality** ranges from **0 to 100**: 0 is round, 50 is oval and 100 is a narrower oval.

Enable **Change color**, then use **Color** to choose a hue from **0 to 360**: red at 0/360, yellow at 60, green at 120, cyan at 180, blue at 240 and magenta at 300. **Glow** controls intensity and **Opacity** controls transparency.

Shield protection and duration remain unchanged.

## Installation

Install EffectsControl with Thunderstore Mod Manager or r2modman. BepInExPack Valheim is installed as a dependency.

For manual installation, copy the archive's `BepInEx` folder into your Valheim installation. The DLL belongs in `BepInEx/plugins/EffectsControl/`.

## Configuration

Open **F1 Configuration Manager** and select **EffectsControl** to change settings in game. Configuration Manager is optional and installed separately.

Configuration file: `BepInEx/config/Slikfoul.EffectsControl.cfg`.

Author: **Slikfoul**. [Source code and MIT license](https://github.com/slikfoul/EffectsControl).

## Building from source

Requires a .NET SDK, an installed copy of Valheim and BepInExPack Valheim. Game and BepInEx assemblies are referenced locally and are not included in this repository. The project defaults point to the author's Windows installation; override them for your machine:

```powershell
dotnet build EffectsControl.csproj -c Release -p:ValheimDir="C:\Games\Valheim" -p:ModProfile="C:\ModProfiles\Default"
```

The profile must contain `BepInEx/core`. Build the importable archive with `./BuildPackage.ps1 -SkipBuild`. Run configuration migration checks with `./tests/ConfigMigrationChecks.ps1`. Unity component checks use `./RunChecks.ps1 -UnityEditor "C:\Program Files\Unity\Hub\Editor\6000.0.75f1\Editor\Unity.exe"`; their installed-game reference paths are defined at the start of `EffectsChecks.Run` and need to match your installation.
