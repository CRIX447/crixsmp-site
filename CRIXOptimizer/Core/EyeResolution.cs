// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;

namespace CRIXOptimizer.Core
{
    /// <summary>
    /// Pure maths for the eye-resolution cap. VR runtimes choose the eye-texture size (Virtual Desktop's quality
    /// preset, Meta Quest Link's render resolution, SteamVR's resolution slider). When that size is far above what
    /// the headset displays, every frame renders millions of pixels nobody sees; the cap lowers the game's render
    /// scale until one eye is at most a set number of megapixels.
    /// </summary>
    public static class EyeResolution
    {
        /// <summary>Quest 3 default per-eye render size (2064x2208), for messages only.</summary>
        public const double Quest3DefaultMegapixels = 2064.0 * 2208.0 / 1e6;
        /// <summary>The cap never lowers render scale below this (URP's own minimum is 0.1).</summary>
        public const float MinimumScale = 0.35f;

        /// <summary>Megapixels per eye at render scale 1.0, from the current eye-texture size and render scale.</summary>
        public static double MegapixelsAtScale1(int width, int height, float currentScale)
        {
            if (width <= 0 || height <= 0 || !(currentScale > 0.05f))
                return 0;
            return (double)width * height / ((double)currentScale * currentScale) / 1e6;
        }

        /// <summary>Largest render scale that keeps one eye at or below capMegapixels (float.MaxValue = no cap).</summary>
        public static float MaxScaleFor(double megapixelsAtScale1, float capMegapixels)
        {
            if (!(capMegapixels > 0f) || !(megapixelsAtScale1 > 0))
                return float.MaxValue;
            double scale = Math.Sqrt(capMegapixels / megapixelsAtScale1);
            return (float)Math.Max(MinimumScale, Math.Floor(scale * 100.0) / 100.0);
        }
    }
}
