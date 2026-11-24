// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CRIXOptimizer.Compatibility
{
    /// <summary>
    /// Inspects Harmony's global patch registry (read-only): which plugins patch how many methods, and which
    /// methods CRIX patches that another plugin also patches. Overlaps are not necessarily conflicts; they
    /// are listed so a user can find the cause when two mods misbehave together. Runs once after startup and
    /// on demand.
    /// </summary>
    internal static class HarmonyFootprint
    {
        public sealed class Overlap
        {
            public string Method;
            public List<string> OtherOwners = new List<string>();
            public bool OtherPrefixCanSkip;
        }

        public sealed class Report
        {
            public int PatchedMethods;
            public Dictionary<string, int> MethodsPerOwner = new Dictionary<string, int>(StringComparer.Ordinal);
            public List<Overlap> Overlaps = new List<Overlap>();
            public string Error;
        }

        public static Report Scan(string ownHarmonyId)
        {
            var report = new Report();
            try
            {
                foreach (MethodBase method in Harmony.GetAllPatchedMethods().ToList())
                {
                    Patches info = Harmony.GetPatchInfo(method);
                    if (info == null)
                        continue;
                    report.PatchedMethods++;

                    var owners = new HashSet<string>(info.Owners ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
                    foreach (string owner in owners)
                    {
                        report.MethodsPerOwner.TryGetValue(owner, out int n);
                        report.MethodsPerOwner[owner] = n + 1;
                    }

                    if (owners.Contains(ownHarmonyId) && owners.Count > 1)
                    {
                        var overlap = new Overlap { Method = (method.DeclaringType?.FullName ?? "?") + "." + method.Name };
                        foreach (string owner in owners)
                            if (owner != ownHarmonyId)
                                overlap.OtherOwners.Add(owner);
                        // A foreign prefix returning bool may skip the original (and change what CRIX sees).
                        overlap.OtherPrefixCanSkip = info.Prefixes.Any(p => p.owner != ownHarmonyId && p.PatchMethod.ReturnType == typeof(bool));
                        report.Overlaps.Add(overlap);
                    }
                }
            }
            catch (Exception e)
            {
                report.Error = e.GetType().Name + ": " + e.Message;
            }
            return report;
        }
    }
}
