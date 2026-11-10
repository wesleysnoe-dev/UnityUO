using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A map item the server opened (0x90/0xF5) with its pins (0x56): the area it covers, from the world map's image
    /// once that facet has been drawn (else a plain parchment panel), and the pins numbered in order. While the server
    /// lets us edit it, a click on the map plots a pin and Clear removes them all.
    /// </summary>
    public sealed class MapItemWindow : UiWindow
    {
        /// <summary>Our own window, in the game's look (IronSkin).</summary>
        public override bool Themed => true;

        private const float Margin = 20, TitleHeight = 30, FooterHeight = 30, MaxView = 480;
        private static readonly Color Parchment = new Color(0.86f, 0.78f, 0.6f), Ink = new Color(0.3f, 0.2f, 0.1f);
        private static readonly Color PinColor = new Color(0.55f, 0.08f, 0.05f);

        private readonly uint _serial;

        public MapItemWindow(MapItemDetails map)
        {
            _serial = map.Serial;
            (float w, float h) = MapItemLayout.ViewSize(map, MaxView);
            Rect = new Rect(240, 90, w + Margin * 2, h + TitleHeight + FooterHeight);
        }

        public override string Key => $"mapitem:{_serial}";

        public override bool StillValid => Ui != null && Session.Maps.ContainsKey(_serial);

        public override void Draw()
        {
            if (!Session.Maps.TryGetValue(_serial, out MapItemDetails map))
                return;
            (float vw, float vh) = MapItemLayout.ViewSize(map, MaxView);
            var view = new Rect(Margin, TitleHeight, vw, vh);

            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(12, 7, Rect.width - 24, 20), $"Map 0x{map.Serial:X8}   ({map.X1}, {map.Y1}) - ({map.X2}, {map.Y2})",
                new Color(1f, 0.9f, 0.6f), 13, TextAnchor.UpperCenter, true);

            DrawArea(map, view);
            DrawPins(map, view);
            HandleClick(map, view);

            float y = Rect.height - FooterHeight + 4;
            Text(new Rect(Margin, y + 2, 220, 20), map.Editable ? $"Click to plot a course   {map.Pins.Count} pins" : $"{map.Pins.Count} pins",
                new Color(0.85f, 0.82f, 0.72f), 12);
            if (map.Editable && map.Pins.Count > 0 && GUI.Button(new Rect(Rect.width - Margin - 64, y, 64, 22), "Clear"))
                Session.ClearMapPins(_serial);
        }

        /// <summary>The map's gump art (0x90) as parchment, with the facet's world map image over it once it has been drawn.</summary>
        private void DrawArea(MapItemDetails map, Rect view)
        {
            UiImage art = Art.Gump(map.Gump);
            if (art != null)
                GUI.DrawTexture(view, art.Texture, ScaleMode.StretchToFill);
            else
            {
                GUI.color = Parchment;
                GUI.DrawTexture(view, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            int facet = map.Facet >= 0 ? map.Facet : Session.World.Map;
            Texture2D tex = WorldMapWindow.Cached(facet);
            if (tex != null && map.X2 > map.X1 && map.Y2 > map.Y1)
            {
                float t = WorldMapWindow.TilesPerPixel;
                var src = new Rect(map.X1 / t, map.Y1 / t, (map.X2 - map.X1) / t, (map.Y2 - map.Y1) / t);
                GUI.color = new Color(1f, 0.94f, 0.82f, 0.85f); // a parchment tint over the radar colors
                GUI.DrawTextureWithTexCoords(view, tex,
                    new Rect(src.x / tex.width, 1f - (src.y + src.height) / tex.height, src.width / tex.width, src.height / tex.height));
                GUI.color = Color.white;
            }
            else if (art == null)
                Text(new Rect(view.x, view.y + view.height / 2 - 10, view.width, 20), "(open the world map to draw this area)", Ink, 12, TextAnchor.UpperCenter);
        }

        private void DrawPins(MapItemDetails map, Rect view)
        {
            for (int i = 0; i < map.Pins.Count; i++)
            {
                MapPin pin = map.Pins[i];
                (float px, float py) = MapItemLayout.ToView(map, pin.X, pin.Y, view.width, view.height);
                var at = new Vector2(view.x + px, view.y + py);
                GUI.color = PinColor;
                GUI.DrawTexture(new Rect(at.x - 3, at.y - 3, 7, 7), Texture2D.whiteTexture);
                GUI.color = Color.white;
                Text(new Rect(at.x + 5, at.y - 8, 30, 16), (i + 1).ToString(), PinColor, 11, TextAnchor.UpperLeft, true);
            }
        }

        private void HandleClick(MapItemDetails map, Rect view)
        {
            Event e = Event.current;
            if (!map.Editable || e.type != EventType.MouseDown || e.button != 0 || !view.Contains(e.mousePosition))
                return;
            e.Use(); // a click on an editable map plots instead of moving the window
            if (MapItemLayout.TryToMap(map, e.mousePosition.x - view.x, e.mousePosition.y - view.y, view.width, view.height, out int x, out int y))
                Session.AddMapPin(_serial, x, y);
        }
    }
}
