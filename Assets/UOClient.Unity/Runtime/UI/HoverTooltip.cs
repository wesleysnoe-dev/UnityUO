using System;
using System.Text;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>The property tooltip of what the mouse rests on, in a window or in the world.</summary>
    public sealed class HoverTooltip
    {
        public const int TooltipWindowId = 0x7EEF;

        private uint _hoverSerial, _forcedHover;
        private Vector2 _forcedHoverAt;
        private float _hoverSince;
        private GUIStyle _tooltipStyle;
        private Texture2D _tooltipBack;

        /// <summary>Hover a serial with the tooltip at a point (UI units), as the capture tour does (no real mouse).</summary>
        public void ForceHover(uint serial, Vector2 at)
        {
            _forcedHover = serial;
            _forcedHoverAt = at;
        }

        public void ClearForced() => _forcedHover = 0;

        /// <summary>What the mouse rests on: an item in a window, else whatever <paramref name="pick"/> finds under it in the world.</summary>
        public void UpdateHover(UiManager ui, Func<Vector2, uint> pick)
        {
            uint serial = 0;
            Vector2 mouse = Input.mousePosition;
            if (ui.IsOverUi(mouse))
            {
                var p = new Vector2(mouse.x / ui.Scale, (Screen.height - mouse.y) / ui.Scale);
                for (int i = ui.Windows.Count - 1; i >= 0; i--)
                    if (ui.Windows[i].Rect.Contains(p))
                    {
                        serial = ui.Windows[i].Hovered;
                        break;
                    }
            }
            else if (ui.Dragging == null)
            {
                serial = pick(mouse);
            }
            if (_forcedHover != 0)
                serial = _forcedHover; // the capture tour (no real mouse)
            if (serial != _hoverSerial)
            {
                _hoverSerial = serial;
                _hoverSince = Time.realtimeSinceStartup;
            }
        }

        public void OnGUI(GameSession session, UiManager ui)
        {
            if (session == null || _hoverSerial == 0 || Time.realtimeSinceStartup - _hoverSince < 0.4f || ui.Dragging != null)
                return;
            PropertyList props = session.GetProperties(_hoverSerial);
            if (props == null || props.Lines.Count == 0)
                return;
            if (_tooltipStyle == null)
            {
                _tooltipStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 14, alignment = TextAnchor.UpperCenter, padding = new RectOffset(8, 8, 6, 6) };
                _tooltipStyle.normal.textColor = new Color(0.93f, 0.91f, 0.84f);
                _tooltipBack = new Texture2D(1, 1);
                _tooltipBack.SetPixel(0, 0, new Color(0.05f, 0.04f, 0.03f, 0.88f));
                _tooltipBack.Apply();
            }
            var sb = new StringBuilder();
            for (int i = 0; i < props.Lines.Count; i++)
            {
                string line = GumpHtml.ToRichText(props.Lines[i], out _);
                sb.Append(i == 0 ? $"<color=#ffd27a><b>{line}</b></color>" : line);
                if (i < props.Lines.Count - 1)
                    sb.Append('\n');
            }
            var content = new GUIContent(sb.ToString());
            float s = ui.Scale;
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float width = Mathf.Min(320, _tooltipStyle.CalcSize(content).x + 4);
            float height = _tooltipStyle.CalcHeight(content, width);
            Vector2 m = _forcedHover != 0 ? _forcedHoverAt : Event.current.mousePosition / s;
            var r = new Rect(m.x + 18, m.y + 18, width, height);
            if (r.xMax > Screen.width / s) r.x = m.x - width - 8;
            if (r.yMax > Screen.height / s) r.y = m.y - height - 8;
            // Windows always draw above plain controls, so over a paperdoll or backpack the tooltip has to be
            // a window too, kept in front.
            GUI.Window(TooltipWindowId, r, _ =>
            {
                var local = new Rect(0, 0, r.width, r.height);
                GUI.DrawTexture(local, _tooltipBack);
                GUI.Label(local, content, _tooltipStyle);
            }, GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(TooltipWindowId);
            GUI.matrix = saved;
        }
    }
}
