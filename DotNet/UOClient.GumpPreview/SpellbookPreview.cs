using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Unity.UI;

/// <summary>
/// gumppreview &lt;uoDataDir&gt; --spellbook &lt;outDir&gt;: the classic Magery, Necromancy and Chivalry books drawn as
/// SpellbookWindow.Draw / DrawIndex / DrawDetails lay them out (SpellbookState over SpellbookWindow.Layout, TazUO's
/// SpellbookGump) from real gump art and unicode font 1. RENDERER-ONLY: synthetic content bits, not a capture of the
/// running client (no Unity, Session or server; no hover/pressed art, ui.Scale or texture sampling).
/// </summary>
internal static class SpellbookPreview
{
    private const uint Book = 0x40001234;

    public static int Run(string dataDir, string outDir)
    {
        using var data = new UOData(dataDir);
        var p = new Painter(data);
        Directory.CreateDirectory(outDir);

        // A full magery book: the first index spread (circles 1 and 2), and the spread with Feeblemind and Heal.
        var magery = new SpellbookState(Book, 1, ulong.MaxValue);
        Write(outDir, "magery-index-p1", p.Draw(magery, 0));
        magery.GoTo(SpellbookWindow.DetailSlot(magery.Info, 3).Page);
        Write(outDir, "magery-detail-heal", p.Draw(magery, 0));
        magery.GoTo(3);
        Write(outDir, "magery-index-p3", p.Draw(magery, 0));

        // A necro book holding Animate Dead, Corpse Skin, Evil Omen, Wither and Exorcism: its cover (the first index
        // spread) and first detail spread.
        var necro = new SpellbookState(Book, 101, (1UL << 0) | (1UL << 2) | (1UL << 4) | (1UL << 14) | (1UL << 16));
        Write(outDir, "necro-cover", p.Draw(necro, 0));
        necro.GoTo(necro.Info.IndexPages + 1);
        Write(outDir, "necro-detail-p3", p.Draw(necro, 0));

        // A full paladin book: its cover with the tithing line, and the Cleanse by Fire / Close Wounds spread.
        var chivalry = new SpellbookState(Book, 201, 0x3FFUL);
        Write(outDir, "chivalry-cover", p.Draw(chivalry, 1234));
        chivalry.GoTo(chivalry.Info.IndexPages + 1);
        Write(outDir, "chivalry-detail-p2", p.Draw(chivalry, 1234));
        return 0;
    }

    private static void Write(string outDir, string name, Bitmap32 bmp)
    {
        string path = Path.Combine(outDir, name + ".png");
        Png.Write(path, bmp);
        Console.WriteLine($"{path}: {bmp.Width}x{bmp.Height} (RENDERER-ONLY offline fixture, synthetic bits)");
    }

    private sealed class Painter
    {
        private readonly UOData _data;
        private readonly UniText _text;
        private readonly HueEntry[] _hues;
        private readonly Cliloc _cliloc;
        private readonly Dictionary<int, Bitmap32> _gumps = new Dictionary<int, Bitmap32>();
        private Bitmap32 _canvas;

        public Painter(UOData data)
        {
            _data = data;
            _text = new UniText(data.GetUniFont);
            _hues = data.Hues.Entries;
            string clilocPath = Path.Combine(data.Directory, "Cliloc.enu");
            _cliloc = File.Exists(clilocPath) ? new Cliloc(clilocPath) : null;
        }

        // ---- SpellbookWindow.Draw (open, not minimized) --------------------------------------------------------------

        public Bitmap32 Draw(SpellbookState s, int tithe)
        {
            Bitmap32 bg = Gump(s.Info.BookGraphic);
            _canvas = new Bitmap32(bg?.Width ?? 360, bg?.Height ?? 220);
            for (int i = 0; i < _canvas.Pixels.Length; i++)
                _canvas.Pixels[i] = 0xFF606060; // mid-grey where the world would show
            Blit(Gump(s.Info.BookGraphic), 0, 0);

            if (s.Info.Kind == SpellbookKind.Magery)
                foreach ((int gump, int x, int y, int _) in SpellbookWindow.CircleButtons)
                    Blit(Gump(gump), x, y);

            if (s.OnIndex)
                DrawIndex(s, tithe);
            else
                DrawDetails(s);

            if (s.Page != 1)
                Blit(Gump(SpellbookWindow.PrevCorner), SpellbookWindow.PrevCornerX, SpellbookWindow.CornerY);
            if (s.Page != s.MaxPage)
                Blit(Gump(SpellbookWindow.NextCorner), SpellbookWindow.NextCornerX, SpellbookWindow.CornerY);
            return _canvas;
        }

        private void DrawIndex(SpellbookState s, int tithe)
        {
            for (int half = 0; half < 2; half++)
            {
                Label(SpellbookSpells.IndexText, SpellbookWindow.IndexTitleX(half), SpellbookWindow.IndexTitleY);
                if (s.Info.Kind == SpellbookKind.Magery)
                {
                    int circle = SpellbookWindow.CircleOfHalf(s.Page, half);
                    Label(Localized(SpellbookSpells.CircleCliloc + circle, SpellbookSpells.CircleNames[circle]),
                        SpellbookWindow.IndexDataX(half), SpellbookWindow.IndexSubtitleY);
                }
            }
            if (s.Page == 1 && s.Info.Kind == SpellbookKind.Chivalry)
            {
                string tithing = Localized(SpellbookSpells.TithingCliloc, null) is string t && Localized(SpellbookSpells.AvailableCliloc, null) is string a
                    ? t + "\n" + a
                    : SpellbookSpells.TithingText;
                Label($"{tithing} {tithe}", SpellbookWindow.TithingX, SpellbookWindow.TithingY);
            }
            foreach (SpellbookWindow.IndexEntry entry in s.Index)
                if (entry.Page == s.Page)
                    Label(SpellName(s, entry.Spell), entry.X, entry.Y, SpellbookWindow.InkHue, SpellbookWindow.IndexNameWidth);
        }

        private void DrawDetails(SpellbookState s)
        {
            foreach (SpellbookWindow.DetailEntry entry in s.Detail)
            {
                if (entry.Page != s.Page || !(SpellbookSpells.Get(s.Info.Kind, entry.Spell) is SpellbookSpells.Spell spell))
                    continue;
                string name = Localized(spell.Cliloc, spell.Name);
                if (s.Info.Kind == SpellbookKind.Magery)
                {
                    int circle = entry.Spell >> 3;
                    Label(Localized(SpellbookSpells.CircleCliloc + circle, SpellbookSpells.CircleNames[circle]), entry.TopTextX, SpellbookWindow.TopTextY + 4);
                    int nameHeight = Label(name, entry.IconTextX, SpellbookWindow.NameY, SpellbookWindow.InkHue, SpellbookWindow.WordsWidth);
                    Label(SpellbookSpells.Abbreviate(spell.Words), entry.IconTextX, SpellbookWindow.MageryWordsY(nameHeight));
                }
                else
                {
                    Label(name, entry.TopTextX, SpellbookWindow.TopTextY);
                    Label(spell.Words, entry.IconTextX, SpellbookWindow.NameY, SpellbookWindow.InkHue, SpellbookWindow.WordsWidth);
                }

                Blit(Gump(SpellbookWindow.IconOf(s.Info, entry.Spell)), entry.IconX, SpellbookWindow.IconY);
                if (spell.Reagents.Length > 0)
                {
                    Tiled(Gump(SpellbookWindow.ReagentRule), entry.IconX, SpellbookWindow.ReagentRuleY, SpellbookWindow.ReagentRuleW, SpellbookWindow.ReagentRuleH);
                    Label(Localized(SpellbookSpells.ReagentsCliloc, SpellbookSpells.ReagentsText), entry.IconX, SpellbookWindow.ReagentsTitleY);
                    Label(string.Join("\n", spell.Reagents.Select(r => Localized(r.Cliloc, r.Name))), entry.IconX, SpellbookWindow.ReagentListY);
                }
                if (s.Info.Kind != SpellbookKind.Magery)
                    Label(SpellbookSpells.Requires(spell.Mana, spell.MinSkill), entry.IconX, SpellbookWindow.RequiresY);
            }
        }

        private string SpellName(SpellbookState s, int index) =>
            SpellbookSpells.Get(s.Info.Kind, index) is SpellbookSpells.Spell sp ? Localized(sp.Cliloc, sp.Name) : "";

        private string Localized(int number, string fallback) =>
            _cliloc?.Get(number) is string s && s.Length > 0 ? s : fallback;

        /// <summary>SpellbookWindow.Label / LabelImage: unicode font 1 in the client hue, index names cropped; returns its height.</summary>
        private int Label(string text, int x, int y, int hue = SpellbookWindow.InkHue, int maxWidth = 0)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            UniTextStyle style = maxWidth == SpellbookWindow.IndexNameWidth ? UniTextStyle.Cropped : UniTextStyle.None;
            Bitmap32 img = _text.Generate(text, ServerGumpWindow.GumpFont, ServerGumpWindow.LabelArgb(_hues, hue), maxWidth, UniTextAlign.Left, style).Bitmap;
            Blit(img, x, y);
            return img?.Height ?? 0;
        }

        private Bitmap32 Gump(int id)
        {
            if (!_gumps.TryGetValue(id, out Bitmap32 bmp))
                _gumps[id] = bmp = _data.GetGump(id);
            return bmp;
        }

        /// <summary>Source-over, clipped to the canvas and to the given box.</summary>
        private void Blit(Bitmap32 src, int x, int y, int clipW = int.MaxValue, int clipH = int.MaxValue)
        {
            if (src == null)
                return;
            for (int py = 0; py < Math.Min(src.Height, clipH); py++)
                for (int px = 0; px < Math.Min(src.Width, clipW); px++)
                {
                    int dx = x + px, dy = y + py;
                    if (dx < 0 || dy < 0 || dx >= _canvas.Width || dy >= _canvas.Height)
                        continue;
                    uint s = src.Pixels[py * src.Width + px], a = s >> 24;
                    if (a == 0)
                        continue;
                    uint d = _canvas.Pixels[dy * _canvas.Width + dx];
                    uint Mix(int sh) => ((((s >> sh) & 0xFF) * a + ((d >> sh) & 0xFF) * (255 - a)) / 255) << sh;
                    _canvas.Pixels[dy * _canvas.Width + dx] = 0xFF000000 | Mix(16) | Mix(8) | Mix(0);
                }
        }

        /// <summary>UiWindow.DrawTiled: the gump repeated over a w x h box.</summary>
        private void Tiled(Bitmap32 img, int x, int y, int w, int h)
        {
            if (img == null || img.Width <= 0 || img.Height <= 0)
                return;
            for (int ty = 0; ty < h; ty += img.Height)
                for (int tx = 0; tx < w; tx += img.Width)
                    Blit(img, x + tx, y + ty, w - tx, h - ty);
        }
    }
}
