// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;

namespace CRIXOptimizer.Core
{
    public enum SettingState
    {
        /// <summary>CRIX has not changed this setting.</summary>
        Untouched,
        /// <summary>CRIX applied a value and it is still in effect.</summary>
        Applied,
        /// <summary>Something else changed the value after CRIX applied it; CRIX no longer manages it.</summary>
        ExternallyModified,
        /// <summary>Reading or writing the setting threw; it is disabled for this session.</summary>
        Failed,
    }

    public enum ReleaseOutcome
    {
        NothingToRestore,
        Restored,
        SkippedExternallyChanged,
        Failed,
    }

    public interface ITrackedSetting
    {
        string Name { get; }
        SettingState State { get; }
        /// <summary>True when CRIX manages the setting and its value differs from the game's original.</summary>
        bool ChangesValue { get; }
        string Describe();
        /// <summary>Returns true when the live value no longer matches what CRIX applied.</summary>
        bool CheckDrift();
        ReleaseOutcome Release();
        /// <summary>Allow CRIX to manage the setting again after an external change (adopts the current value as the new original).</summary>
        void Rearm();
    }

    /// <summary>
    /// One engine setting managed by CRIX.
    /// - The original value is captured the first time CRIX changes it.
    /// - Every apply records what the engine actually reports back (Unity may clamp values).
    /// - If the live value later differs from what CRIX applied, the change came from the game or another
    ///   mod: CRIX stops managing the setting (Respect policy) instead of fighting over it.
    /// - Release restores the original only if the value is still CRIX's own; otherwise it is left alone.
    /// </summary>
    public sealed class TrackedSetting<T> : ITrackedSetting
    {
        private readonly Func<T> getter;
        private readonly Action<T> setter;
        private readonly IEqualityComparer<T> comparer;
        private readonly Func<T, string> format;

        private bool hasOriginal;
        private T original;
        private bool hasApplied;
        private T applied;
        private T requested;

        public TrackedSetting(string name, Func<T> getter, Action<T> setter, IEqualityComparer<T> comparer = null, Func<T, string> format = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            this.getter = getter ?? throw new ArgumentNullException(nameof(getter));
            this.setter = setter ?? throw new ArgumentNullException(nameof(setter));
            this.comparer = comparer ?? EqualityComparer<T>.Default;
            this.format = format ?? (v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
        }

        public string Name { get; }
        public SettingState State { get; private set; } = SettingState.Untouched;
        public bool HasOriginal => hasOriginal;
        public T Original => original;
        public T AppliedValue => applied;
        public string LastError { get; private set; }

        /// <summary>When true, a detected external change is overwritten on the next Apply instead of respected.</summary>
        public bool ReapplyAfterExternalChange { get; set; }

        /// <summary>Fired once when an external change is detected: (setting, value CRIX applied, value found).</summary>
        public event Action<TrackedSetting<T>, T, T> ExternalChangeDetected;

        /// <summary>Reads the live value (original capture semantics are unaffected).</summary>
        public bool TryRead(out T value)
        {
            try
            {
                value = getter();
                return true;
            }
            catch (Exception e)
            {
                MarkFailed(e);
                value = default;
                return false;
            }
        }

        /// <summary>
        /// The value CRIX would restore to (captured original), or the current live value if CRIX has not changed it.
        /// Use this as the "game baseline" when computing limits such as min(baseline, cap).
        /// </summary>
        public bool ChangesValue => State == SettingState.Applied && hasOriginal && hasApplied && !comparer.Equals(original, applied);

        public bool TryGetBaseline(out T value)
        {
            if (hasOriginal)
            {
                value = original;
                return true;
            }
            return TryRead(out value);
        }

        public bool Apply(T value)
        {
            if (State == SettingState.Failed)
                return false;
            if (State == SettingState.ExternallyModified && !ReapplyAfterExternalChange)
                return false;

            T current;
            try
            {
                current = getter();
            }
            catch (Exception e)
            {
                MarkFailed(e);
                return false;
            }

            if (!hasOriginal)
            {
                original = current;
                hasOriginal = true;
            }
            else if (hasApplied && !comparer.Equals(current, applied))
            {
                // Someone else changed it since our last apply.
                NoteExternalChange(current);
                if (!ReapplyAfterExternalChange)
                    return false;
            }

            try
            {
                requested = value;
                if (!comparer.Equals(current, value))
                    setter(value);
                applied = getter();
                hasApplied = true;
                State = SettingState.Applied;
                return true;
            }
            catch (Exception e)
            {
                MarkFailed(e);
                return false;
            }
        }

        /// <summary>True when the engine stored a different value than requested (e.g. clamped).</summary>
        public bool WasAdjustedByEngine => hasApplied && !comparer.Equals(requested, applied);

        public bool CheckDrift()
        {
            if (State != SettingState.Applied)
                return false;
            T current;
            try
            {
                current = getter();
            }
            catch (Exception e)
            {
                MarkFailed(e);
                return false;
            }
            if (comparer.Equals(current, applied))
                return false;
            NoteExternalChange(current);
            return true;
        }

        public ReleaseOutcome Release()
        {
            if (!hasApplied)
            {
                if (State == SettingState.ExternallyModified)
                    State = SettingState.Untouched;
                return State == SettingState.Failed ? ReleaseOutcome.Failed : ReleaseOutcome.NothingToRestore;
            }

            try
            {
                T current = getter();
                hasApplied = false;
                if (!comparer.Equals(current, applied))
                {
                    State = SettingState.ExternallyModified;
                    return ReleaseOutcome.SkippedExternallyChanged;
                }
                if (!comparer.Equals(current, original))
                    setter(original);
                State = SettingState.Untouched;
                // Forget the original: the next Apply captures a fresh one, so changes the game makes
                // while CRIX is not managing the setting are respected.
                hasOriginal = false;
                return ReleaseOutcome.Restored;
            }
            catch (Exception e)
            {
                MarkFailed(e);
                return ReleaseOutcome.Failed;
            }
        }

        public void Rearm()
        {
            if (State != SettingState.ExternallyModified)
                return;
            hasApplied = false;
            hasOriginal = false;
            State = SettingState.Untouched;
        }

        public string Describe()
        {
            switch (State)
            {
                case SettingState.Applied:
                    return hasOriginal
                        ? $"{Name}: {format(original)} -> {format(applied)}{(WasAdjustedByEngine ? $" (requested {format(requested)}, engine adjusted)" : "")}"
                        : $"{Name}: {format(applied)}";
                case SettingState.ExternallyModified:
                    return $"{Name}: changed externally, not managed";
                case SettingState.Failed:
                    return $"{Name}: unavailable ({LastError})";
                default:
                    return $"{Name}: unchanged";
            }
        }

        private void NoteExternalChange(T found)
        {
            T ours = applied;
            State = SettingState.ExternallyModified;
            hasApplied = false;
            // What the external party set is now the value to keep if we ever release.
            original = found;
            hasOriginal = true;
            ExternalChangeDetected?.Invoke(this, ours, found);
        }

        private void MarkFailed(Exception e)
        {
            State = SettingState.Failed;
            LastError = e.GetType().Name + ": " + e.Message;
        }
    }

    /// <summary>Float comparer with an absolute tolerance (engine round-trips are not always bit-exact).</summary>
    public sealed class ApproxFloatComparer : IEqualityComparer<float>
    {
        public static readonly ApproxFloatComparer Default = new ApproxFloatComparer(0.0005f);
        private readonly float epsilon;
        public ApproxFloatComparer(float epsilon) { this.epsilon = epsilon; }
        public bool Equals(float x, float y) => Math.Abs(x - y) <= epsilon || (float.IsNaN(x) && float.IsNaN(y));
        public int GetHashCode(float obj) => 0;
    }
}
