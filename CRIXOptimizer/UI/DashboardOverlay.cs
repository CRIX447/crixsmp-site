// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System.Diagnostics;
using CRIXOptimizer.Configuration;
using UnityEngine;

namespace CRIXOptimizer.UI
{
    /// <summary>
    /// IMGUI overlay on the desktop window (not inside the headset). The component only exists while the
    /// dashboard or an alert is visible, so a hidden dashboard costs nothing: Unity does not call OnGUI on
    /// components that do not exist. Text is rebuilt by the runtime a few times per second; OnGUI only draws
    /// the cached string during the Repaint event.
    /// </summary>
    internal sealed class DashboardOverlay : MonoBehaviour
    {
        private GUIStyle style;
        private GUIStyle alertStyle;
        private Texture2D background;
        private readonly GUIContent content = new GUIContent();
        private int styledFontSize = -1;

        public string Text = "";
        public string Alert = "";
        public bool ShowPanel = true;
        public DashboardCorner Corner = DashboardCorner.TopLeft;
        public int FontSize = 14;

        /// <summary>Stopwatch ticks spent in OnGUI since the runtime last read it (for the overhead meter).</summary>
        public long TicksSpent;

        private void Awake()
        {
            useGUILayout = false; // no layout pass needed: everything is drawn with absolute rects
        }

        private void EnsureStyles()
        {
            if (style != null && styledFontSize == FontSize)
                return;
            if (background == null)
            {
                background = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
                background.Apply();
                background.hideFlags = HideFlags.HideAndDontSave;
            }
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = FontSize,
                richText = true,
                wordWrap = false,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(10, 10, 8, 8),
            };
            style.normal.background = background;
            style.normal.textColor = Color.white;
            alertStyle = new GUIStyle(style) { fontStyle = FontStyle.Bold };
            alertStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);
            styledFontSize = FontSize;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;
            long start = Stopwatch.GetTimestamp();
            EnsureStyles();

            float y = 0f;
            if (ShowPanel && !string.IsNullOrEmpty(Text))
            {
                content.text = Text;
                Vector2 size = style.CalcSize(content);
                Rect rect = Place(size);
                GUI.Label(rect, content, style);
                y = rect.height + 4f;
            }

            if (!string.IsNullOrEmpty(Alert))
            {
                content.text = Alert;
                Vector2 size = alertStyle.CalcSize(content);
                Rect rect = Place(size);
                bool top = Corner == DashboardCorner.TopLeft || Corner == DashboardCorner.TopRight;
                rect.y += top ? y : -y;
                GUI.Label(rect, content, alertStyle);
            }

            TicksSpent += Stopwatch.GetTimestamp() - start;
        }

        private Rect Place(Vector2 size)
        {
            const float margin = 12f;
            float x = Corner == DashboardCorner.TopLeft || Corner == DashboardCorner.BottomLeft ? margin : Screen.width - size.x - margin;
            float y = Corner == DashboardCorner.TopLeft || Corner == DashboardCorner.TopRight ? margin : Screen.height - size.y - margin;
            return new Rect(x, y, size.x, size.y);
        }

        private void OnDestroy()
        {
            if (background != null)
                Destroy(background);
            background = null;
        }
    }
}
