﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Automatics.ModUtils;
using BepInEx;
using LitJson;
using UnityEngine;

namespace Automatics.AutomaticMapping
{
    using PinData = Minimap.PinData;
    using PinType = Minimap.PinType;

    internal static class Map
    {
        private static readonly CustomIcon DefaultIcon;
        private static readonly List<CustomIcon> CustomIcons;

        static Map()
        {
            DefaultIcon = new CustomIcon
            {
                PinType = PinType.Icon3,
                Options = new Options(),
            };
            CustomIcons = new List<CustomIcon>();
        }

        private static Minimap ValheimMap => Minimap.instance;

        private static IEnumerable<PinData> GetAllPins() => Reflection.GetField<List<PinData>>(ValheimMap, "m_pins");

        public static void Initialize()
        {
            Deprecated.Map.Initialize();
            CustomIcons.Clear();
            LoadCustomIcons(Automatics.GetDefaultResourcePath("Textures"));
            LoadCustomIconMods();
            LoadCustomIcons(Automatics.GetInjectedResourcePath("Textures"));
            RegisterCustomIcons();
        }

        private static void LoadCustomIconMods()
        {
            foreach (var directory in Directory.GetDirectories(Paths.PluginPath))
            {
                LoadCustomIcons(Path.Combine(directory, "Textures"));
            }
        }

        private static void LoadCustomIcons(string texturesDir)
        {
            if (string.IsNullOrEmpty(texturesDir)) return;

            var customIconsJson = Path.Combine(texturesDir, "custom-map-icon.json");
            if (!File.Exists(customIconsJson)) return;

            try
            {
                Automatics.ModLogger.LogInfo($"Load custom icon data from {customIconsJson}");

                var reader = new JsonReader(File.ReadAllText(customIconsJson))
                {
                    AllowComments = true,
                };

                foreach (var data in JsonMapper.ToObject<List<CustomIconData>>(reader))
                {
                    var sprite = Image.CreateSprite(texturesDir, data.sprite);
                    if (sprite == null) continue;

                    CustomIcons.Add(new CustomIcon
                    {
                        Target = data.target,
                        Sprite = sprite,
                        Options = data.options,
                    });

                    Automatics.ModLogger.LogInfo($"* Loaded custom icon data for {data.target.name}");
                }
            }
            catch (Exception e)
            {
                Automatics.ModLogger.LogError($"Failed to load custom icon data: {customIconsJson}\n{e}");
            }
        }

        private static void RegisterCustomIcons()
        {
            if (!CustomIcons.Any()) return;

            var visibleIconTypes = Reflection.GetField<bool[]>(ValheimMap, "m_visibleIconTypes");
            var originalArraySize = visibleIconTypes.Length;
            var newVisibleIconTypes = new bool[originalArraySize + CustomIcons.Count];
            for (var i = 0; i < newVisibleIconTypes.Length; i++)
                newVisibleIconTypes[i] = i < originalArraySize && visibleIconTypes[i];
            Reflection.SetField(ValheimMap, "m_visibleIconTypes", newVisibleIconTypes);

            Automatics.ModLogger.LogInfo(
                $"Minimap.m_visibleIconTypes Expanded: {originalArraySize} -> {newVisibleIconTypes.Length}");

            for (var j = 0; j < CustomIcons.Count; j++)
            {
                var icon = CustomIcons[j];
                var pinType = (PinType)(originalArraySize + j);

                icon.PinType = pinType;
                ValheimMap.m_icons.Add(new Minimap.SpriteData
                {
                    m_name = pinType,
                    m_icon = icon.Sprite
                });

                Automatics.ModLogger.LogInfo(
                    $"Register new sprite data: ({pinType}, {Image.GetTextureFileName(icon.Sprite)})");
            }
        }

        private static CustomIcon GetCustomIcon(PinningTarget target)
        {
            var customIcon = GetCustomIconInternal(target);
            if (customIcon == DefaultIcon)
            {
                var pinType = Deprecated.Map.GetCustomIcon(target.name);
                customIcon = new CustomIcon
                {
                    PinType = pinType,
                    Options = new Options()
                };
            }

            return customIcon;
        }

        private static CustomIcon GetCustomIconInternal(PinningTarget target)
        {
            if (!CustomIcons.Any()) return DefaultIcon;

            var internalName = target.name;
            var displayName = L10N.TranslateInternalNameOnly(internalName);
            var meta = target.metadata;
            return (from x in CustomIcons
                    where (L10N.IsInternalName(x.Target.name)
                              ? internalName.Equals(x.Target.name, StringComparison.Ordinal)
                              : displayName.IndexOf(x.Target.name, StringComparison.OrdinalIgnoreCase) >= 0) &&
                          (x.Target.metadata == null || IsMetaDataEquals(x.Target.metadata, meta))
                    orderby x.Target.metadata != null descending,
                        x.Target.metadata
                    select x)
                .DefaultIfEmpty(DefaultIcon)
                .FirstOrDefault();
        }

        private static bool IsMetaDataEquals(MetaData a, MetaData b)
        {
            if (a == null || b == null) return false;
            return a.level == b.level;
        }

        public static bool HavePinInRange(Vector3 pos, float radius)
        {
            return GetAllPins().Any(data => Utils.DistanceXZ(data.m_pos, pos) <= radius);
        }

        public static bool FindPin(Func<PinData, bool> predicate, out PinData data)
        {
            data = GetAllPins().FirstOrDefault(predicate);
            return data != null;
        }

        public static bool FindPinInRange(Vector3 pos, float radius, out PinData data)
        {
            return FindPin(x => Utils.DistanceXZ(x.m_pos, pos) <= radius, out data);
        }

        public static PinData AddPin(Vector3 pos, PinType type, string name, bool save)
        {
            var data = ValheimMap.AddPin(pos, type, name, save, false);
            Log.Debug(() => $"Add pin: [name: {name}, pos: {data.m_pos}, icon: {(int)type}]");
            return data;
        }

        public static PinData AddPin(Vector3 pos, string name, bool save, PinningTarget target)
        {
            var customIcon = GetCustomIcon(target);
            return AddPin(pos, customIcon.PinType, customIcon.Options.hideNameTag ? "" : name, save);
        }

        public static void RemovePin(PinData data)
        {
            ValheimMap.RemovePin(data);
            Log.Debug(() => $"Remove pin: [name: {data.m_name}, pos: {data.m_pos}, icon: {(int)data.m_type}]");
        }

        public static void RemovePin(Vector3 pos, bool save = true)
        {
            var pin = GetAllPins().FirstOrDefault(x => (save ? x.m_save : !x.m_save) && x.m_pos == pos);
            if (pin != null)
                RemovePin(pin);
        }

        private class CustomIcon
        {
            public PinningTarget Target;
            public PinType PinType;
            public Sprite Sprite;
            public Options Options;
        }
    }

    [Serializable]
    public struct CustomIconData
    {
        public PinningTarget target;
        public SpriteInfo sprite;
        public Options options;
    }

    [Serializable]
    public struct PinningTarget
    {
        public string name;
        public MetaData metadata;
    }

    [Serializable]
    public class MetaData : IComparable<MetaData>
    {
        public int level = -1;

        public int CompareTo(MetaData other)
        {
            if (this == other) return 0;
            if (other == null) return 1;
            return level.CompareTo(other.level);
        }
    }

    [Serializable]
    public struct Options
    {
        public bool hideNameTag;
    }
}