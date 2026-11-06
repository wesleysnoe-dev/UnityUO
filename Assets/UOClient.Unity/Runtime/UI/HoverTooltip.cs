using System;
using System.Text;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>The property tooltip of what the mouse rests on, in a window or in the world.</summary>
    public sealed partial class HoverTooltip
    {
        public const int TooltipWindowId = 0x7EEF;

        private uint _hoverSerial, _forcedHover;
        private string _hoverText, _hoverId;
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

        /// <summary>Seconds the mouse rests before the tooltip shows (the options' tooltip delay).</summary>
        public float Delay { get; set; } = 0.4f;

        /// <summary>The options' UseTooltip (TazUO's): off hides window parts' text tooltips, not item or mobile properties (GameOptions.ShowsTooltip).</summary>
        public bool UseTooltip { get; set; } = true;

        /// <summary>What the mouse rests on: an item in a window, else whatever <paramref name="pick"/> finds under it in the world.</summary>
        public void UpdateHover(UiManager ui, Func<Vector2, uint> pick)
        {
            uint serial = 0;
            string text = null, id = null;
            Vector2 mouse = Input.mousePosition;
            if (ui.IsOverUi(mouse))
            {
                var p = new Vector2(mouse.x / ui.Scale, (Screen.height - mouse.y) / ui.Scale);
                for (int i = ui.Windows.Count - 1; i >= 0; i--)
                    if (ui.Windows[i].Rect.Contains(p))
                    {
                        serial = ui.Windows[i].Hovered;
                        text = serial == 0 ? ui.Windows[i].HoveredText : null;
                        id = text != null ? ui.Windows[i].HoveredTextId ?? text : null;
                        break;
                    }
            }
            else if (ui.Dragging == null)
            {
                serial = pick(mouse);
            }
            if (_forcedHover != 0)
            {
                serial = _forcedHover; // the capture tour (no real mouse)
                text = id = null;
            }
            if (serial != _hoverSerial || id != _hoverId)
            {
                _hoverSerial = serial;
                _hoverId = id;
                _hoverSince = Time.realtimeSinceStartup;
            }
            _hoverText = text; // the same element's text may change (a countdown) without restarting the delay
        }

        public void OnGUI(GameSession session, UiManager ui)
        {
            if (session == null || (_hoverSerial == 0 && string.IsNullOrEmpty(_hoverText))
                || Time.realtimeSinceStartup - _hoverSince < Delay || ui.Dragging != null)
                return;
            PropertyList props = _hoverSerial != 0 ? session.GetProperties(_hoverSerial) : null;
            if (_hoverSerial != 0 && (props == null || props.Lines.Count == 0))
                return;
            if (!GameOptions.ShowsTooltip(UseTooltip, props == null))
                return; // a window part's text tooltip, with UseTooltip off
            if (_tooltipStyle == null)
            {
                _tooltipStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 14, alignment = TextAnchor.UpperCenter, padding = new RectOffset(8, 8, 6, 6) };
                _tooltipStyle.normal.textColor = new Color(0.93f, 0.91f, 0.84f);
                _tooltipBack = new Texture2D(1, 1);
                _tooltipBack.SetPixel(0, 0, new Color(0.05f, 0.04f, 0.03f, 0.88f));
                _tooltipBack.Apply();
            }
            // TazUO's buff text is wrapped in <left> (BuffDebuff.Receive); everything else is centered.
            _tooltipStyle.alignment = props == null && _hoverText.StartsWith("<left>", StringComparison.OrdinalIgnoreCase)
                ? TextAnchor.UpperLeft : TextAnchor.UpperCenter;
            var sb = new StringBuilder();
            if (props == null)
                sb.Append(GumpHtml.ToRichText(_hoverText, out _)); // a gump element's cliloc tooltip
            for (int i = 0; props != null && i < props.Lines.Count; i++)
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
            // The mouse in UI units, straight from the screen so it doesn't depend on how IMGUI maps it through GUI.matrix
            // (it already applies the scale to Event.current.mousePosition; dividing that again put the tooltip up-left).
            Vector2 m = _forcedHover != 0 ? _forcedHoverAt
                : new Vector2(Input.mousePosition.x / s, (Screen.height - Input.mousePosition.y) / s);
            (float x, float y) = Place(m.x, m.y, width, height, Screen.width / s, Screen.height / s);
            var r = new Rect(x, y, width, height);
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
