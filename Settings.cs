using System;
using BepInEx.Configuration;
using UnityEngine;

namespace EffectsControl
{
    public enum EffectStage { Launch, Flight, Impact, Shield }

    public sealed class VisualOptions
    {
        public float Light = 1, Glow = 1, Opacity = 1, Size = 1, Density = 1;
        public float ShieldOvality;
        public bool TintEnabled;
        public Color Tint = Color.white;
        public static VisualOptions Original() { return new VisualOptions(); }
    }

    internal sealed class Settings
    {
        public readonly ConfigEntry<bool> Enabled, Weapons, Shields, ShieldTint;
        public readonly ConfigEntry<int> Light, Glow, Opacity, Size, Density;
        public readonly ConfigEntry<int> Launch, Flight, Impact;
        public readonly ConfigEntry<int> ShieldLight, ShieldGlow, ShieldOpacity;
        public readonly ConfigEntry<int> ShieldOvality;
        public readonly ConfigEntry<int> ShieldColor;
        public event Action Changed;

        public Settings(ConfigFile config)
        {
            Enabled = config.Bind("1. General", "Enable mod", true, "Enable visual settings. Disabling restores the saved original appearance of active effects.");
            Weapons = config.Bind("1. General", "Weapon effects", true, "Control attack launch, flight and impact effects. Weather, fog and building lights are excluded.");
            Shields = config.Bind("1. General", "Magic shield", true, "Control Staff of Protection visuals. Shield control is suspended while SubtleShield is active; disable it and restart to use these settings.");
            Light = Percent(config, "2. Weapons - General", "Light brightness", 0, "Light intensity: reduce surrounding illumination and whiteout. Light range is unchanged.");
            Glow = Percent(config, "2. Weapons - General", "Glow", 50, "Particle colors and supported emissive/additive material colors. Some custom shaders ignore these properties.");
            Opacity = Percent(config, "2. Weapons - General", "Opacity", 25, "Particle and material alpha. Lower values increase transparency. Custom and opaque shaders may ignore alpha; 0 hides the visual renderers.");
            Size = Percent(config, "2. Weapons - General", "Size", 100, "Particle size, readable mesh geometry and trail width. Root transforms, colliders, trajectories, damage radius and particle travel distance are unchanged.");
            Density = Percent(config, "2. Weapons - General", "Particle count", 100, "Emission rates and burst counts relative to the original. Existing particles retain their lifetime; 0 hides particles. Direct Emit calls from other code are not limited at intermediate values.");
            Launch = Influence(config, "Attack launch", 100, "Influence of general settings on windup and projectile launch effects.");
            Flight = Influence(config, "Flight", 0, "Influence of general settings on a flying projectile. Speed and collisions are unchanged.");
            Impact = Influence(config, "Impact", 100, "Influence of general settings on hits, explosions and Aoe, including Demolisher hits.");
            ShieldLight = Percent(config, "4. Magic Shield", "Light brightness", 35, "Intensity of lights in the magic shield effect.");
            ShieldGlow = Percent(config, "4. Magic Shield", "Glow", 45, "Supported emissive/additive colors of the bubble and particles.");
            ShieldOpacity = Percent(config, "4. Magic Shield", "Opacity", 30, "Bubble and particle alpha. 0 hides the visual shield; protection and duration are unchanged.");
            ShieldOvality = Integer(config, "Ovality", 50, 0, 100, "Bubble shape: 0 is round; 50 is oval; 100 is a narrower oval. Height, protection and colliders are unchanged. Whole numbers only.");
            ShieldTint = config.Bind("4. Magic Shield", "Change color", false, "Replace supported material and particle RGB colors. Disable for original colors; alpha is controlled separately.");
            ShieldColor = Integer(config, "Color", 0, 0, 360, "Hue slider: 0/360 red, 60 yellow, 120 green, 180 cyan, 240 blue, 300 magenta. Used when Change color is enabled. Glow controls intensity; Opacity controls transparency. Whole numbers only.");
            config.SettingChanged += (sender, args) => { if (Changed != null) Changed(); };
        }

        private static ConfigEntry<int> Percent(ConfigFile config, string section, string key, int value, string description)
        {
            return config.Bind(section, key, value, new ConfigDescription(description + " 100% is the original value; 0% disables this visual component. Whole numbers only. Scaling does not accumulate.", new AcceptableValueRange<int>(0, 100)));
        }

        private static ConfigEntry<int> Influence(ConfigFile config, string key, int value, string description)
        {
            return config.Bind("3. Weapons - Stages", key, value, new ConfigDescription(description + " 100% fully applies section 2 settings; 0% restores the original effect without their influence. Intermediate values blend the influence. Whole numbers only.", new AcceptableValueRange<int>(0, 100)));
        }

        private static ConfigEntry<int> Integer(ConfigFile config, string key, int value, int min, int max, string description)
        {
            return config.Bind("4. Magic Shield", key, value, new ConfigDescription(description, new AcceptableValueRange<int>(min, max)));
        }

        public VisualOptions Get(EffectStage stage, bool shieldConflict)
        {
            if (!Enabled.Value || (stage == EffectStage.Shield ? (!Shields.Value || shieldConflict) : !Weapons.Value)) return VisualOptions.Original();
            if (stage == EffectStage.Shield) {
                Color tint = Color.HSVToRGB(ShieldColor.Value / 360f, 1, 1);
                return new VisualOptions {
                Light = ShieldLight.Value / 100f, Glow = ShieldGlow.Value / 100f, Opacity = ShieldOpacity.Value / 100f,
                ShieldOvality = ShieldOvality.Value / 100f,
                TintEnabled = ShieldTint.Value, Tint = tint
                };
            }
            float influence = (stage == EffectStage.Launch ? Launch.Value : stage == EffectStage.Flight ? Flight.Value : Impact.Value) / 100f;
            return new VisualOptions { Light = Mathf.Lerp(1, Light.Value / 100f, influence), Glow = Mathf.Lerp(1, Glow.Value / 100f, influence),
                Opacity = Mathf.Lerp(1, Opacity.Value / 100f, influence), Size = Mathf.Lerp(1, Size.Value / 100f, influence), Density = Mathf.Lerp(1, Density.Value / 100f, influence) };
        }
    }
}
