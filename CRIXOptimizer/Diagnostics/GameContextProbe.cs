// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CRIXOptimizer.Utilities;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Reads, by name and read-only, which zones (maps) are active and how many players are in the room, for the
    /// performance notes. ZoneManagement.instance.activeZones (GTZone values) and NetworkSystem.Instance.InRoom /
    /// RoomPlayerCount. Anything missing after a game update reads as "unknown"; nothing is ever written.
    /// For hitch causes: the time-of-day step (BetterDayNightManager.instance.currentTimeIndex; a change swaps the
    /// lightmaps of every loaded map), how many loaded maps are loading lightmaps (BetterDayNightManager.allScenesRenderData,
    /// PerSceneRenderData.IsLoadingLightmaps) and whether zone scenes are still loading (ZoneManagement.AnyActiveLoadOps).
    /// Custom (mod.io) maps: CustomMapLoader.IsMapLoaded(), IsLoading(), LoadedMapModId and IsCustomScene(name); while
    /// one is loaded the "customMaps" zone reads as "customMaps:&lt;map id&gt;" so each map gets its own summary. A map that
    /// loads more of its scenes while you play it (its load zones) keeps its id meanwhile.
    /// </summary>
    internal sealed class GameContextProbe
    {
        private readonly Func<object> zoneInstance;
        private readonly Func<object, object> activeZones;
        private readonly Func<object> netInstance;
        private readonly Func<object, bool> inRoom;
        private readonly Func<object, int> playerCount;
        private readonly List<string> names = new List<string>(8);
        private readonly Func<object> dayNightInstance;
        private readonly Func<object, int> timeIndex;
        private readonly Func<object> renderDataList;
        private readonly Func<object, bool> renderDataLoading;
        private readonly MethodInfo anyActiveLoadOps;
        private readonly Func<bool> mapLoaded;
        private readonly Func<bool> mapLoading;
        private string lastMapId = "";
        private readonly Func<object> mapId;
        private readonly Func<string, bool> isCustomScene;

        public bool CanReadCustomMaps => mapLoaded != null && mapId != null && isCustomScene != null;

        public bool CanReadZones => zoneInstance != null && activeZones != null;
        public bool CanReadPlayers => netInstance != null && inRoom != null && playerCount != null;

        public GameContextProbe(GameTypes types)
        {
            if (types == null)
                return;
            Type zm = types.Find("ZoneManagement");
            if (zm != null)
            {
                zoneInstance = Accessors.StaticGetter<object>(zm, "instance");
                activeZones = Accessors.Getter<object>(zm, "activeZones");
                MethodInfo m = Accessors.FindMethod(zm, "AnyActiveLoadOps", 0);
                if (m != null && !m.IsStatic && m.ReturnType == typeof(bool))
                    anyActiveLoadOps = m;
            }
            Type dn = types.Find("BetterDayNightManager");
            if (dn != null)
            {
                dayNightInstance = Accessors.StaticGetter<object>(dn, "instance");
                timeIndex = Accessors.Getter<int>(dn, "currentTimeIndex");
                renderDataList = Accessors.StaticGetter<object>(dn, "allScenesRenderData");
            }
            Type rd = types.Find("PerSceneRenderData");
            if (rd != null)
                renderDataLoading = Accessors.Getter<bool>(rd, "IsLoadingLightmaps");
            Type loader = types.Find("CustomMapLoader");
            if (loader != null)
            {
                mapLoaded = StaticFunc<Func<bool>>(Accessors.FindMethod(loader, "IsMapLoaded", 0));
                mapLoading = StaticFunc<Func<bool>>(Accessors.FindMethod(loader, "IsLoading", 0));
                isCustomScene = StaticFunc<Func<string, bool>>(Accessors.FindMethod(loader, "IsCustomScene", 1));
                mapId = Accessors.StaticGetter<object>(loader, "LoadedMapModId");
            }
            Type ns = types.Find("NetworkSystem");
            if (ns != null)
            {
                netInstance = Accessors.StaticGetter<object>(ns, "Instance");
                inRoom = Accessors.Getter<bool>(ns, "InRoom");
                playerCount = Accessors.Getter<int>(ns, "RoomPlayerCount");
            }
        }

        /// <summary>Active zones joined with '+', in a stable order (e.g. "city+forest"), or "unknown".</summary>
        public string ReadZones()
        {
            if (!CanReadZones)
                return "unknown";
            try
            {
                object instance = zoneInstance();
                if (!UnityObjects.IsAlive(instance) || !(activeZones(instance) is IEnumerable list))
                    return "unknown";
                names.Clear();
                string map = null;
                foreach (object zone in list)
                {
                    if (zone == null)
                        continue;
                    string name = zone.ToString();
                    if (name == "customMaps")
                        name += (map = map ?? ReadCustomMapId()).Length > 0 ? ":" + map : "";
                    names.Add(name);
                }
                if (names.Count == 0)
                    return "none";
                names.Sort(StringComparer.Ordinal);
                return string.Join("+", names.ToArray());
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>
        /// The loaded custom map's mod.io id (digits only), or "" when none is loaded or it cannot be read. The game
        /// reports no map while it loads one, also while a loaded map loads more of its scenes; the last id is kept then.
        /// </summary>
        public string ReadCustomMapId()
        {
            if (mapLoaded == null || mapId == null)
                return "";
            try
            {
                if (!mapLoaded())
                {
                    if (lastMapId.Length > 0 && mapLoading != null && mapLoading())
                        return lastMapId;
                    return lastMapId = "";
                }
                string text = mapId()?.ToString() ?? "";
                var digits = new System.Text.StringBuilder(text.Length);
                foreach (char c in text)
                    if (c >= '0' && c <= '9')
                        digits.Append(c);
                return lastMapId = digits.Length > 0 && digits.Length <= 20 ? digits.ToString() : "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>The game is loading a custom map or more of its scenes (false when unreadable).</summary>
        public bool CustomMapLoading()
        {
            try
            {
                return mapLoading != null && mapLoading();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The game's check for a scene belonging to the loaded custom map (false when unreadable).</summary>
        public bool IsCustomScene(string sceneName)
        {
            if (isCustomScene == null)
                return false;
            try
            {
                return isCustomScene(sceneName);
            }
            catch
            {
                return false;
            }
        }

        private static T StaticFunc<T>(MethodInfo method) where T : class
        {
            if (method == null || !method.IsStatic)
                return null;
            try
            {
                return Delegate.CreateDelegate(typeof(T), method, false) as T;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The game's time-of-day step, or -1 when it cannot be read.</summary>
        public int ReadTimeOfDay()
        {
            if (dayNightInstance == null || timeIndex == null)
                return -1;
            try
            {
                object instance = dayNightInstance();
                return UnityObjects.IsAlive(instance) ? timeIndex(instance) : -1;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>How many loaded maps are loading lightmaps right now, or -1 when it cannot be read. Hitch frames only.</summary>
        public int ReadLightmapLoads()
        {
            if (renderDataList == null || renderDataLoading == null)
                return -1;
            try
            {
                if (!(renderDataList() is IList list))
                    return -1;
                int loading = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    object data = list[i];
                    if (UnityObjects.IsAlive(data) && renderDataLoading(data))
                        loading++;
                }
                return loading;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>Zone scenes still loading: 1 yes, 0 no, -1 unknown. Allocates inside the game: only for hitch frames.</summary>
        public int ReadZoneLoadsRunning()
        {
            if (anyActiveLoadOps == null || zoneInstance == null)
                return -1;
            try
            {
                object instance = zoneInstance();
                if (!UnityObjects.IsAlive(instance))
                    return -1;
                return (bool)anyActiveLoadOps.Invoke(instance, null) ? 1 : 0;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>Players in the room including you; 0 when not in a room, -1 when it cannot be read.</summary>
        public int ReadPlayers()
        {
            if (!CanReadPlayers)
                return -1;
            try
            {
                object system = netInstance();
                if (!UnityObjects.IsAlive(system))
                    return -1;
                return inRoom(system) ? Math.Max(1, playerCount(system)) : 0;
            }
            catch
            {
                return -1;
            }
        }
    }
}
