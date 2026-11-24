// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.IO;
using System.Security.Cryptography;

namespace CRIXOptimizer
{
    internal static class ModInfo
    {
        public const string Guid = "com.crix.crixoptimizer";
        public const string Name = "CRIX OPTIMIZER";
        public const string Version = "1.1.0";
        public const string HarmonyId = Guid;
        public const string GameProcess = "Gorilla Tag.exe";

        /// <summary>Potassium 1.0.0 (https://github.com/niko-tar-gz/Potassium), the GPL-3.0 project CRIX is derived from.</summary>
        public const string PotassiumGuid = "zone.xenon.potassium";

        private static string build;

        /// <summary>The first 8 hex digits of the plugin DLL's SHA-256 (the release notes list the full hash), so a notes
        /// file shows which build wrote it even when the version number stayed the same. "unknown" if the file cannot be read.</summary>
        public static string Build
        {
            get
            {
                if (build != null)
                    return build;
                try
                {
                    using (var sha = SHA256.Create())
                    using (FileStream f = File.OpenRead(typeof(ModInfo).Assembly.Location))
                        build = BitConverter.ToString(sha.ComputeHash(f), 0, 4).Replace("-", "").ToLowerInvariant();
                }
                catch
                {
                    build = "unknown";
                }
                return build;
            }
        }

        /// <summary>"1.1.0 (build 9b435e42)".</summary>
        public static string VersionAndBuild => Version + " (build " + Build + ")";
    }
}
