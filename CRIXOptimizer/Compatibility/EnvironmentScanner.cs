// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CRIXOptimizer.Core;

namespace CRIXOptimizer.Compatibility
{
    /// <summary>
    /// Background, read-only detection of the software around the game:
    ///  - companion apps by process name (Virtual Desktop, OBS, Discord, SteamVR, Quest Link, ALVR);
    ///  - graphics hooks loaded into the game process (ReShade, OBS game capture, overlays) by DLL name;
    ///  - ReShade files in the game folder (ReShade.ini, proxy DLLs whose version info names ReShade).
    /// Nothing is injected, modified, terminated or re-prioritised; only names and version strings are read.
    /// Work runs on a thread-pool thread so the main thread never waits for it.
    /// </summary>
    internal sealed class EnvironmentScanner
    {
        public sealed class Result
        {
            public CompanionApp Apps;
            /// <summary>True when the process list could be read (Apps is then meaningful, even if None).</summary>
            public bool ProcessesScanned;
            public GraphicsHook Hooks;
            public string ReShadeDetail = "";
            public List<string> HookModules = new List<string>();
            public DateTime ScannedAtUtc;
            public string Error;
        }

        private readonly ILogSink log;
        private readonly string gameRoot;
        private int running;

        public EnvironmentScanner(ILogSink log, string gameRoot)
        {
            this.log = log;
            this.gameRoot = gameRoot;
        }

        public volatile Result Latest;

        /// <summary>Starts a scan unless one is in progress. The callback runs on the worker thread.</summary>
        public void ScanAsync(bool processes, bool hooks, Action<Result> onDone)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
                return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = new Result { ScannedAtUtc = DateTime.UtcNow };
                try
                {
                    if (processes)
                    {
                        result.Apps = ScanProcesses(out bool scanned);
                        result.ProcessesScanned = scanned;
                    }
                    if (hooks)
                    {
                        ScanHooks(result);
                    }
                    else if (Latest != null)
                    {
                        result.Hooks = Latest.Hooks;
                        result.HookModules = Latest.HookModules;
                        result.ReShadeDetail = Latest.ReShadeDetail;
                    }
                }
                catch (Exception e)
                {
                    result.Error = e.Message;
                }
                finally
                {
                    Latest = result;
                    Interlocked.Exchange(ref running, 0);
                }
                try
                {
                    onDone?.Invoke(result);
                }
                catch (Exception e)
                {
                    log.Debug("Environment scan callback failed: " + e.Message);
                }
            });
        }

        private static CompanionApp ScanProcesses(out bool scanned)
        {
            CompanionApp apps = CompanionApp.None;
            scanned = false;
            Process[] all;
            try
            {
                all = Process.GetProcesses();
            }
            catch
            {
                return apps;
            }
            scanned = true;
            foreach (Process p in all)
            {
                try
                {
                    apps |= CompanionApps.ClassifyProcess(p.ProcessName);
                }
                catch
                {
                    // Access to some system processes is denied; their names are irrelevant here.
                }
                finally
                {
                    p.Dispose();
                }
            }
            return CompanionApps.Normalize(apps);
        }

        private void ScanHooks(Result result)
        {
            try
            {
                using (Process self = Process.GetCurrentProcess())
                {
                    foreach (ProcessModule module in self.Modules)
                    {
                        string name = module.ModuleName;
                        GraphicsHook hook = CompanionApps.ClassifyModule(name);
                        if (hook != GraphicsHook.None)
                        {
                            result.Hooks |= hook;
                            result.HookModules.Add(name);
                        }
                        else if (IsProxyName(name) && LooksLikeReShade(module.FileName))
                        {
                            result.Hooks |= GraphicsHook.ReShade;
                            result.HookModules.Add(name + " (ReShade)");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Module enumeration is not available everywhere; the game-folder check below still runs.
                log.Debug("Loaded-module scan unavailable: " + e.Message);
            }

            try
            {
                if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
                    return;
                if (File.Exists(Path.Combine(gameRoot, "ReShade.ini")))
                {
                    result.Hooks |= GraphicsHook.ReShade;
                    result.ReShadeDetail = "ReShade.ini in game folder";
                }
                foreach (string proxy in ProxyNames)
                {
                    string path = Path.Combine(gameRoot, proxy);
                    if (File.Exists(path) && LooksLikeReShade(path))
                    {
                        result.Hooks |= GraphicsHook.ReShade;
                        string version = SafeVersion(path);
                        result.ReShadeDetail = $"{proxy}{(string.IsNullOrEmpty(version) ? "" : " v" + version)}";
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                log.Debug("Game-folder ReShade check failed: " + e.Message);
            }
        }

        private static readonly string[] ProxyNames = { "dxgi.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll", "d3d9.dll" };

        private static bool IsProxyName(string moduleName)
        {
            foreach (string proxy in ProxyNames)
                if (string.Equals(proxy, moduleName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool LooksLikeReShade(string path)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                return Contains(info.ProductName, "ReShade") || Contains(info.FileDescription, "ReShade");
            }
            catch
            {
                return false;
            }
        }

        private static string SafeVersion(string path)
        {
            try
            {
                return FileVersionInfo.GetVersionInfo(path).ProductVersion;
            }
            catch
            {
                return "";
            }
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
