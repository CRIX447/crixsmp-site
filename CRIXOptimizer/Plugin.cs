// CRIX OPTIMIZER - unofficial performance mod for Gorilla Tag PCVR.
// Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Derived from Potassium 1.0.0, Copyright (C) 2026 Potassium contributors (https://github.com/niko-tar-gz/Potassium).
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed WITHOUT ANY WARRANTY; see LICENSE for details.

using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using CRIXOptimizer.Configuration;
using CRIXOptimizer.Optimization;
using CRIXOptimizer.Utilities;

namespace CRIXOptimizer
{
    /// <summary>
    /// BepInEx entry point. Kept deliberately thin: everything lives in <see cref="OptimizerRuntime"/>.
    /// The soft dependency on Potassium only orders loading (Potassium first) so CRIX can detect it and avoid
    /// patching the same methods twice; CRIX works without Potassium.
    /// </summary>
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    [BepInProcess(ModInfo.GameProcess)]
    [BepInDependency(ModInfo.PotassiumGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private OptimizerRuntime runtime;

        private void Awake()
        {
            var log = new BepInExLogSink(Logger);
            try
            {
                var config = new PluginConfig(Config);
                var profilesFile = new ConfigFile(Path.Combine(Paths.ConfigPath, ModInfo.Guid + ".profiles.cfg"), true, Info.Metadata);
                var profiles = new ProfileStore(profilesFile, Config);
                runtime = new OptimizerRuntime(this, config, profiles, log);
                runtime.Initialize();
            }
            catch (Exception e)
            {
                // A startup failure must never take the game down: shut off and leave everything as it was.
                log.Error("CRIX OPTIMIZER failed to start and is inactive: " + e);
                try { runtime?.Shutdown(); } catch { /* best effort */ }
                runtime = null;
                enabled = false;
            }
        }

        private void Update()
        {
            runtime?.Update();
        }

        private void OnApplicationQuit()
        {
            runtime?.Shutdown();
        }

        private void OnDestroy()
        {
            runtime?.Shutdown();
            runtime = null;
        }
    }
}
