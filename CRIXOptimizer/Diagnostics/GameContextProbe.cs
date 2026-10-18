// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections;
using System.Collections.Generic;
using CRIXOptimizer.Utilities;

namespace CRIXOptimizer.Diagnostics
{
    /// <summary>
    /// Reads, by name and read-only, which zones (maps) are active and how many players are in the room, for the
    /// performance notes. ZoneManagement.instance.activeZones (GTZone values) and NetworkSystem.Instance.InRoom /
    /// RoomPlayerCount. Anything missing after a game update reads as "unknown"; nothing is ever written.
    /// </summary>
    internal sealed class GameContextProbe
    {
        private readonly Func<object> zoneInstance;
        private readonly Func<object, object> activeZones;
        private readonly Func<object> netInstance;
        private readonly Func<object, bool> inRoom;
        private readonly Func<object, int> playerCount;
        private readonly List<string> names = new List<string>(8);

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
                foreach (object zone in list)
                    if (zone != null)
                        names.Add(zone.ToString());
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
