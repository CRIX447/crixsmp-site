// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

namespace CRIXOptimizer.Configuration
{
    public enum ProfileKind
    {
        Balanced,
        MaximumPerformance,
        VirtualDesktop,
        OBSStreaming,
        DiscordCall,
        ReShadeCompatible,
        MultiMod,
        Custom,
    }

    /// <summary>Variants of the Virtual Desktop profile (each is its own editable section in the profiles file).</summary>
    public enum VirtualDesktopPreset
    {
        Balanced,
        Performance,
        Quality,
        Streaming,
    }

    public enum TriState
    {
        Unchanged,
        On,
        Off,
    }

    /// <summary>Upper limit for Unity's shadow quality. The game's value is kept when it is already lower.</summary>
    public enum ShadowQualityLimit
    {
        Unchanged,
        HardOnly,
        Disable,
    }

    /// <summary>Upper limit for shadow-map resolution. The game's value is kept when it is already lower.</summary>
    public enum ShadowResolutionLimit
    {
        Unchanged,
        Low,
        Medium,
        High,
    }

    public enum AnisotropicMode
    {
        Unchanged,
        Disable,
        Enable,
        ForceEnable,
    }

    /// <summary>Upper limit for bones per vertex used in skinning.</summary>
    public enum SkinWeightLimit
    {
        Unchanged,
        OneBone,
        TwoBones,
        FourBones,
    }

    /// <summary>What the desktop window shows while in VR (Unity XR mirror blit mode).</summary>
    public enum DesktopMirrorMode
    {
        Unchanged,
        LeftEye,
        RightEye,
        SideBySide,
        None,
    }

    public enum LoadingPriority
    {
        Unchanged,
        Low,
        BelowNormal,
        Normal,
        High,
    }

    /// <summary>How the forced garbage collection the game performs when leaving a room is handled.</summary>
    public enum RoomLeaveGcMode
    {
        /// <summary>Keep the game's forced full collection.</summary>
        GameDefault,
        /// <summary>Skip it and let the runtime decide (Potassium behaviour).</summary>
        Skip,
        /// <summary>Replace it with a short, time-sliced incremental collection when the game uses incremental GC; otherwise skip.</summary>
        Incremental,
    }

    public enum SanitizerMode
    {
        /// <summary>Leave the game's custom-map sanitizer untouched.</summary>
        Off,
        /// <summary>Keep the game's sanitizer logic; only its List.Contains allow-list lookups become cached hash lookups.</summary>
        SafeLookups,
        /// <summary>Potassium's full re-implementation. Faster, but any rule the game added after Potassium 1.0.0 is bypassed.</summary>
        FullReplacement,
    }

    public enum PotassiumCoexistence
    {
        /// <summary>When Potassium is installed, CRIX does not apply its Potassium-derived patches (no duplicate patches).</summary>
        DeferToPotassium,
        /// <summary>Apply CRIX patches anyway. Both mods will patch the same methods - not recommended.</summary>
        ApplyBoth,
    }

    public enum ExternalChangePolicy
    {
        /// <summary>When the game or another mod changes a setting CRIX manages, CRIX stops managing it.</summary>
        Respect,
        /// <summary>CRIX re-applies its value (can fight other mods; use only if you know nothing else manages it).</summary>
        Reapply,
    }

    public enum ReShadeGuardMode
    {
        /// <summary>Guard is active only when ReShade is detected.</summary>
        Auto,
        On,
        Off,
    }

    public enum AdaptiveMode
    {
        /// <summary>Adapt quality to hold the target frame rate (default).</summary>
        On,
        Off,
    }

    public enum DashboardCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }
}
