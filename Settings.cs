using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace EffectsControl
{
    public enum EffectStage { Launch, Flight, Impact, Shield, NorthernVengeance }

    internal static class EffectStages
    {
        public static bool IsShield(this EffectStage stage) => stage == EffectStage.Shield || stage == EffectStage.NorthernVengeance;
        public static int ShieldBit(this EffectStage stage) => stage == EffectStage.Shield ? 1 : stage == EffectStage.NorthernVengeance ? 2 : 0;
    }

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
        public readonly ConfigEntry<bool> Enabled, Weapons, Shields;
        public readonly ConfigEntry<int> Light, Glow, Opacity, Size, Density;
        public readonly ConfigEntry<int> Launch, Flight, Impact;
        public readonly ConfigEntry<int> ShieldLight, ShieldGlow, ShieldOpacity;
        public readonly ConfigEntry<int> ShieldOvality;
        public readonly ConfigEntry<int> ShieldColor;
        public readonly ConfigEntry<bool> VengeanceEnabled;
        public readonly ConfigEntry<int> VengeanceColor, VengeanceGlow, VengeanceLight, VengeanceOpacity, VengeanceOvality;
        public event Action Changed;

        public Settings(ConfigFile config)
        {
            Enabled = config.Bind("1. General", "Enable mod", true, "Enable visual settings. Disabling restores the saved original appearance of active effects.");
            Weapons = config.Bind("1. General", "Weapon effects", true, "Control attack launch, flight and impact effects. Weather, fog and building lights are excluded.");
            var legacyShields = config.Bind("1. General", "Magic shield", true,
                new ConfigDescription("Compatibility value for older releases.", null, new ConfigurationManagerAttributes { Visible = () => false }));
            Light = Percent(config, "2. Weapons - General", "Light brightness", 0, "Light intensity: reduce surrounding illumination and whiteout. Light range is unchanged.");
            Glow = Percent(config, "2. Weapons - General", "Glow", 50, "Particle colors and supported emissive/additive material colors. Some custom shaders ignore these properties.");
            Opacity = Percent(config, "2. Weapons - General", "Opacity", 25, "Particle and material alpha. Lower values increase transparency. Custom and opaque shaders may ignore alpha; 0 hides the visual renderers.");
            Size = Percent(config, "2. Weapons - General", "Size", 100, "Particle size, readable mesh geometry and trail width. Root transforms, colliders, trajectories, damage radius and particle travel distance are unchanged.");
            Density = Percent(config, "2. Weapons - General", "Particle count", 100, "Emission rates and burst counts relative to the original. Existing particles retain their lifetime; 0 hides particles. Direct Emit calls from other code are not limited at intermediate values.");
            Launch = Influence(config, "Attack launch", 100, "Influence of general settings on windup and projectile launch effects.");
            Flight = Influence(config, "Flight", 0, "Influence of general settings on a flying projectile. Speed and collisions are unchanged.");
            Impact = Influence(config, "Impact", 100, "Influence of general settings on hits, explosions and Aoe, including Demolisher hits.");
            const string protection = "4. Magic Shield";
            const string protectionToggle = "Magic Shield Effect";
            var unbound = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(config)
                as IDictionary<ConfigDefinition, string>;
            bool hasProfileToggle = config.TryGetEntry(protection, protectionToggle, out ConfigEntry<bool> _)
                || unbound?.ContainsKey(new ConfigDefinition(protection, protectionToggle)) == true;
            Shields = BindShieldToggle(config, protection, protectionToggle, unbound != null || legacyShields.Value);
            // Migrate the legacy selection once, while keeping Reset at the factory default.
            if (!hasProfileToggle && !legacyShields.Value) Shields.Value = false;
            // Keep the old key hidden but readable by earlier compatibility checks.
            if (legacyShields.Value != Shields.Value) legacyShields.Value = Shields.Value;
            Shields.SettingChanged += (sender, args) => legacyShields.Value = Shields.Value;
            ShieldLight = ShieldInteger(config, protection, "Light brightness", 35, 100, "Shield light intensity. 100 is original brightness; light range is unchanged.");
            ShieldGlow = ShieldInteger(config, protection, "Glow", 72, 100, "Bubble and particle glow. 100 is original brightness.");
            ShieldOpacity = ShieldInteger(config, protection, "Opacity", 30, 100, "Bubble and particle opacity. 100 is original opacity; 0 hides the visual shield without changing its gameplay effect.");
            ShieldOvality = ShieldInteger(config, protection, "Ovality", 50, 100, "Bubble shape: 0 round, 50 oval, 100 narrow oval. Height and colliders are unchanged.");
            ShieldColor = ShieldInteger(config, protection, "Color", 0, 360, HueDescription);
            const string vengeance = "5. Northern Vengeance";
            VengeanceEnabled = BindShieldToggle(config, vengeance, "Northern Vengeance Effect", true);
            VengeanceColor = ShieldInteger(config, vengeance, "Color", 200, 360, HueDescription);
            VengeanceGlow = ShieldInteger(config, vengeance, "Glow", 72, 100, "Bubble and particle glow. 100 is original brightness.");
            VengeanceLight = ShieldInteger(config, vengeance, "Light brightness", 35, 100, "Shield light intensity. 100 is original brightness; light range is unchanged.");
            VengeanceOpacity = ShieldInteger(config, vengeance, "Opacity", 30, 100, "Bubble and particle opacity. 100 is original opacity; 0 hides the visual shield without changing its gameplay effect.");
            VengeanceOvality = ShieldInteger(config, vengeance, "Ovality", 50, 100, "Bubble shape: 0 round, 50 oval, 100 narrow oval. Height and colliders are unchanged.");
            config.SettingChanged += (sender, args) => { if (Changed != null) Changed(); };
        }

        private const string HueDescription = "Hue: 0/360 red, 60 yellow, 120 green, 180 cyan, 240 blue, 300 magenta. Applied whenever this appearance is enabled.";

        private static ConfigEntry<bool> BindShieldToggle(ConfigFile config, string section, string key, bool value) =>
            config.Bind(section, key, value, new ConfigDescription("Customize shield appearance on this client. Turning this off restores the original appearance. Shield mechanics are unaffected.",
                null, new ConfigurationManagerAttributes { Order = 100 }));

        private static ConfigEntry<int> Percent(ConfigFile config, string section, string key, int value, string description)
        {
            return config.Bind(section, key, value, new ConfigDescription(description + " 100% is the original value; 0% disables this visual component. Whole numbers only. Scaling does not accumulate.", new AcceptableValueRange<int>(0, 100)));
        }

        internal static void BindOwnerNotices(ConfigFile config, Func<EffectStage, bool> ownedElsewhere)
        {
            const string notice = "Controlled by Improved Magic Shield.";
            void Bind(string section, EffectStage stage) =>
                config.Bind(section, "Appearance control", notice,
                    new ConfigDescription("This mod's saved settings apply when Improved Magic Shield releases this shield.", null,
                        new ConfigurationManagerAttributes {
                            Order = 200, ReadOnly = true, HideDefaultButton = true, HideSettingName = true,
                            CustomDrawer = _ => GUILayout.Label(notice),
                            Visible = () => ownedElsewhere(stage)
                        }));
            Bind("4. Magic Shield", EffectStage.Shield);
            Bind("5. Northern Vengeance", EffectStage.NorthernVengeance);
        }

        private static ConfigEntry<int> Influence(ConfigFile config, string key, int value, string description)
        {
            return config.Bind("3. Weapons - Stages", key, value, new ConfigDescription(description + " 100% fully applies section 2 settings; 0% restores the original effect without their influence. Intermediate values blend the influence. Whole numbers only.", new AcceptableValueRange<int>(0, 100)));
        }

        private static ConfigEntry<int> ShieldInteger(ConfigFile config, string section, string key, int value, int max, string description) =>
            config.Bind(section, key, value, new ConfigDescription(description + " Whole numbers only.", new AcceptableValueRange<int>(0, max)));

        public VisualOptions Get(EffectStage stage, bool shieldConflict)
        {
            if (!Enabled.Value || (stage.IsShield() && shieldConflict)) return VisualOptions.Original();
            if (stage == EffectStage.NorthernVengeance)
            {
                if (!VengeanceEnabled.Value) return VisualOptions.Original();
                return new VisualOptions {
                    TintEnabled = true, Tint = Color.HSVToRGB(VengeanceColor.Value / 360f, 1, 1),
                    Glow = VengeanceGlow.Value / 100f, Light = VengeanceLight.Value / 100f,
                    Opacity = VengeanceOpacity.Value / 100f, ShieldOvality = VengeanceOvality.Value / 100f,
                    Size = 0.90f
                };
            }
            if (stage == EffectStage.Shield) {
                if (!Shields.Value) return VisualOptions.Original();
                Color tint = Color.HSVToRGB(ShieldColor.Value / 360f, 1, 1);
                return new VisualOptions {
                Light = ShieldLight.Value / 100f, Glow = ShieldGlow.Value / 100f, Opacity = ShieldOpacity.Value / 100f,
                ShieldOvality = ShieldOvality.Value / 100f,
                TintEnabled = true, Tint = tint
                };
            }
            if (!Weapons.Value) return VisualOptions.Original();
            float influence = (stage == EffectStage.Launch ? Launch.Value : stage == EffectStage.Flight ? Flight.Value : Impact.Value) / 100f;
            return new VisualOptions { Light = Mathf.Lerp(1, Light.Value / 100f, influence), Glow = Mathf.Lerp(1, Glow.Value / 100f, influence),
                Opacity = Mathf.Lerp(1, Opacity.Value / 100f, influence), Size = Mathf.Lerp(1, Size.Value / 100f, influence), Density = Mathf.Lerp(1, Density.Value / 100f, influence) };
        }
    }

    internal sealed class ConfigurationManagerAttributes
    {
        public int Order;
        public bool? ReadOnly;
        public bool HideDefaultButton, HideSettingName;
        public Action<ConfigEntryBase> CustomDrawer;
        internal Func<bool> Visible;
        public bool Browsable => Visible == null || Visible();
    }
}
