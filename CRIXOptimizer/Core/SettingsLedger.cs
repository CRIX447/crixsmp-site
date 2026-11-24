// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;

namespace CRIXOptimizer.Core
{
    /// <summary>All engine settings CRIX may change, with collective drift checks and restore.</summary>
    public sealed class SettingsLedger
    {
        private readonly List<ITrackedSetting> settings = new List<ITrackedSetting>();
        private readonly ILogSink log;

        public SettingsLedger(ILogSink log) { this.log = log ?? NullLogSink.Instance; }

        public IReadOnlyList<ITrackedSetting> Settings => settings;

        public TrackedSetting<T> Register<T>(TrackedSetting<T> setting)
        {
            if (setting == null)
                throw new ArgumentNullException(nameof(setting));
            settings.Add(setting);
            setting.ExternalChangeDetected += (s, ours, found) =>
                log.Warn($"{s.Name} was changed by the game or another plugin ({ours} -> {found}). " +
                         "CRIX will leave it alone until the profile is re-applied.");
            return setting;
        }

        public void Unregister(ITrackedSetting setting) => settings.Remove(setting);

        /// <summary>Checks every applied setting; returns how many drifted since the last check.</summary>
        public int CheckDrift()
        {
            int drifted = 0;
            for (int i = 0; i < settings.Count; i++)
            {
                try
                {
                    if (settings[i].CheckDrift())
                        drifted++;
                }
                catch (Exception e)
                {
                    log.Debug($"Drift check failed for {settings[i].Name}: {e.Message}");
                }
            }
            return drifted;
        }

        public void RearmAll()
        {
            for (int i = 0; i < settings.Count; i++)
                settings[i].Rearm();
        }

        public struct RestoreReport
        {
            public int Restored;
            public int SkippedExternal;
            public int Failed;
            public List<string> Notes;
        }

        /// <summary>Restores everything CRIX changed, never overwriting values changed by someone else.</summary>
        public RestoreReport RestoreAll()
        {
            var report = new RestoreReport { Notes = new List<string>() };
            for (int i = settings.Count - 1; i >= 0; i--)
            {
                ITrackedSetting setting = settings[i];
                ReleaseOutcome outcome;
                try
                {
                    outcome = setting.Release();
                }
                catch (Exception e)
                {
                    outcome = ReleaseOutcome.Failed;
                    report.Notes.Add($"{setting.Name}: {e.Message}");
                }

                switch (outcome)
                {
                    case ReleaseOutcome.Restored:
                        report.Restored++;
                        break;
                    case ReleaseOutcome.SkippedExternallyChanged:
                        report.SkippedExternal++;
                        report.Notes.Add($"{setting.Name}: not restored because it was changed by the game or another plugin");
                        break;
                    case ReleaseOutcome.Failed:
                        report.Failed++;
                        report.Notes.Add($"{setting.Name}: could not be restored ({setting.Describe()})");
                        break;
                }
            }
            return report;
        }

        public int CountInState(SettingState state)
        {
            int n = 0;
            for (int i = 0; i < settings.Count; i++)
                if (settings[i].State == state)
                    n++;
            return n;
        }
    }
}
