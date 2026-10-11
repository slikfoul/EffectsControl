# EffectsControl

A client-side BepInEx mod for Valheim that reduces obstructive weapon and magic shield visuals. Version 0.0.6 is a development build for testing in game.

## In-game settings

Open F1 Configuration Manager and select EffectsControl. Configuration Manager is optional and installed separately. Changes apply immediately.

All numeric settings are whole integers. Percentages use 0-100; bubble offsets use whole centimeters. Color uses one RGB code.

## Weapon effects

Section **2. Weapons - General** applies to supported weapon effects without per-weapon setup:

| Setting | Default | Controls |
|---|---:|---|
| Light brightness | 0 | Light intensity; range is unchanged |
| Glow | 50 | Particle colors and supported material emission |
| Opacity | 25 | Particle and supported material alpha |
| Size | 100 | Particle size, readable mesh geometry and trail width |
| Particle count | 100 | Automatic emission rates and burst counts |

Section **3. Weapons - Stages** controls how strongly each stage uses section 2:

- **100%** fully applies the general settings.
- **0%** restores the original appearance regardless of general settings.
- Intermediate values blend between the original and configured appearance. For example, Glow 20 with Flight 50 gives 60% of the original projectile glow.

Stage defaults: **Attack launch 100, Flight 0, Impact 100**. General reductions therefore apply to launch and impact, while flying projectiles retain their original appearance. Set all stages to 0, all general settings to 100, or disable Weapon effects to restore original weapon visuals.

Values are captured from each instance. Repeated changes do not accumulate. Updates apply to observed active effects and new instances. Particle count changes future automatic emission; existing particles retain their lifetime.

## Magic shield

Section **4. Magic Shield** controls light, glow, opacity, ovality, optional color and visual bubble offsets.

**Ovality** affects only the readable Sphere mesh of a registered shield effect: 0 is the original round shape, 50 gives an oval shape with horizontal dimensions at 75% and unchanged height, and 100 gives a narrower oval. Default: **50**. There are no separate width, height or depth settings.

**Change color** is disabled by default. **Color** is one RGB code, such as `330000` or `#330000`. Shield opacity controls alpha independently.

For original shield visuals, set light, glow and opacity to 100, ovality to 0, disable Change color and reset offsets, or disable Magic shield. Protection, damage absorption and shield duration are unchanged.

## Installation

Download the ZIP from [GitHub Releases](https://github.com/slikfoul/EffectsControl/releases). Close Valheim and Thunderstore Mod Manager. Use **Settings -> Profile -> Import local mod** to import the ZIP, then enable EffectsControl. Configuration is stored at `BepInEx/config/Slikfoul.EffectsControl.cfg`.

From source, `BuildAndInstall.ps1 -Profile Default` builds, validates and registers the local package. It creates a backup, preserves selected configuration values, enabled state and unrelated mods, and leaves one package-managed DLL. Older configuration names are migrated to the current fields. The script does not close applications or publish the package.

## Building from source

Requires a .NET SDK, an installed copy of Valheim and BepInExPack Valheim. Game and BepInEx assemblies are referenced locally and are not included in this repository. The project defaults point to the author's Windows installation; override them for your machine:

```powershell
dotnet build EffectsControl.csproj -c Release -p:ValheimDir="C:\Games\Valheim" -p:ModProfile="C:\ModProfiles\Default"
```

The profile must contain `BepInEx/core`. Build the importable archive with `./BuildPackage.ps1 -SkipBuild`. Run configuration migration checks with `./tests/ConfigMigrationChecks.ps1`. Unity component checks use `./RunChecks.ps1 -UnityEditor "C:\Program Files\Unity\Hub\Editor\6000.0.75f1\Editor\Unity.exe"`; their installed-game reference paths are defined at the start of `EffectsChecks.Run` and need to match your installation.

Author: Slikfoul. [MIT License](LICENSE).
