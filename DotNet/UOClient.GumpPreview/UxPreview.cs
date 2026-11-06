using System;
using System.Collections.Generic;
using System.IO;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Files;
using UOClient.IO.Formats;
using UOClient.Net;
using UOClient.Unity;
using UOClient.Unity.UI;

/// <summary>
/// gumppreview &lt;uoDataDir&gt; --ux &lt;outDir&gt;: synthetic health bars, the AoS status and the paperdoll's chrome, drawn as
/// HealthBarWindow.Draw, StatusWindow.Draw and PaperdollWindow.Draw lay them out (their .Layout files) from made-up
/// mobiles at our client version. RENDERER-ONLY:
/// an offline fixture, not a capture of the running client (no Unity, Session or server; no ui.Scale, HD art or sampling).
/// </summary>
internal static class UxPreview
{
    private const uint Me = 0x42, Orc = 0x77;

    public static int Run(string dataDir, string outDir)
    {
        using var data = new UOData(dataDir);
        var text = new UniText(data.GetUniFont);
        HueEntry[] hues = data.Hues.Entries;
        Directory.CreateDirectory(outDir);

        var me = new Mobile
        {
            Serial = Me, Name = "Wes", Hits = 62, HitsMax = 100, Mana = 30, ManaMax = 40, Stamina = 95, StaminaMax = 100,
            Female = false, Str = 90, Dex = 45, Int = 25, Armor = 12, Gold = 1234, Weight = 150, MaxWeight = 390,
            StatCap = 225, Followers = 1, FollowersMax = 5, FireResist = 20, ColdResist = 15, PoisonResist = 10, EnergyResist = 5,
            Luck = 100, DamageMin = 11, DamageMax = 13, HasFullStatus = true, StrLock = 0, DexLock = 1, IntLock = 2
        };
        var orc = new Mobile { Serial = Orc, Name = "an orc", Hits = 18, HitsMax = 25, Notoriety = 5 };
        var poisoned = new Mobile { Serial = Orc, Name = "a giant spider", Hits = 9, HitsMax = 25, Notoriety = 3, Poisoned = true };
        var pet = new Mobile { Serial = Orc, Name = "a horse", Hits = 25, HitsMax = 25, Notoriety = 1, Renamable = true };

        Write(outDir, "healthbar-self", Bar(data, text, hues, HealthBarWindow.Compute(Me, Me, false, me, null)));
        Write(outDir, "healthbar-self-war", Bar(data, text, hues, HealthBarWindow.Compute(Me, Me, true, me, null)));
        Write(outDir, "healthbar-enemy", Bar(data, text, hues, HealthBarWindow.Compute(Orc, Me, false, orc, null)));
        Write(outDir, "healthbar-poisoned-gray", Bar(data, text, hues, HealthBarWindow.Compute(Orc, Me, false, poisoned, null)));
        Write(outDir, "healthbar-pet", Bar(data, text, hues, HealthBarWindow.Compute(Orc, Me, false, pet, null)));
        Write(outDir, "healthbar-out-of-range", Bar(data, text, hues, HealthBarWindow.Compute(Orc, Me, false, null, "an orc")));
        var friend = new Mobile
        {
            Serial = Orc, Name = "Friend", Hits = 40, HitsMax = 100, Mana = 70, ManaMax = 100, Stamina = 100, StaminaMax = 100, Notoriety = 1
        };
        Write(outDir, "partybar-self", PartyBar(data, text, hues, HealthBarWindow.ComputeParty(Me, Me, me, null)));
        Write(outDir, "partybar-member", PartyBar(data, text, hues, HealthBarWindow.ComputeParty(Orc, Me, friend, null)));
        Write(outDir, "partybar-out-of-range", PartyBar(data, text, hues, HealthBarWindow.ComputeParty(Orc, Me, null, "Friend")));
        Write(outDir, "status-aos", Status(data, text, hues, me, ClientVersion.Default));
        Write(outDir, "paperdoll-self-aos", Paperdoll(data, hues, ClientVersion.Default, self: true, war: false));
        Write(outDir, "paperdoll-self-aos-war", Paperdoll(data, hues, ClientVersion.Default, self: true, war: true));
        Write(outDir, "paperdoll-other", Paperdoll(data, hues, ClientVersion.Default, self: false, war: false));
        Write(outDir, "paperdoll-slots-books", PaperdollSlots(data, text, hues, Armour, books: true, outlineLayer: 0));
        Write(outDir, "paperdoll-slots-hover-katana", PaperdollSlots(data, text, hues, Armour, books: true, outlineLayer: 1));
        Write(outDir, "paperdoll-slots-naked-nobooks", PaperdollSlots(data, text, hues, new (ushort, ushort)[] { (0x0E75, 0) }, books: false, outlineLayer: 0));
        Write(outDir, "combatbook-index-primary-on", CombatBook(data, text, hues, 1, weapon: 0x13FF, primaryOn: true));
        Write(outDir, "combatbook-index-p3-bare", CombatBook(data, text, hues, 3, weapon: 0, primaryOn: false));
        Write(outDir, "combatbook-armor-ignore", CombatBook(data, text, hues, CombatBookWindow.PageOf(WeaponAbilities.ArmorIgnore), 0, false));
        Write(outDir, "combatbook-mystic-arc", CombatBook(data, text, hues, CombatBookWindow.PageOf(WeaponAbilities.MysticArc), 0, false));
        Write(outDir, "racialbook-human-index", RacialBook(data, text, hues, RacialAbilitiesWindow.Race.Human, 1));
        Write(outDir, "racialbook-human-p2", RacialBook(data, text, hues, RacialAbilitiesWindow.Race.Human, 2));
        Write(outDir, "racialbook-elf-p3", RacialBook(data, text, hues, RacialAbilitiesWindow.Race.Elf, 3));
        Write(outDir, "question-criminal", Question(data, text, hues, QuestionWindow.CriminalText));
        return 0;
    }

    // ---- QuestionWindow.Draw (the box alone, without the screen dimming) ---------------------------------------------

    private static Bitmap32 Question(UOData data, UniText text, HueEntry[] hues, string message)
    {
        Bitmap32 bg = Gump(data, hues, QuestionWindow.Background);
        var canvas = Canvas(bg?.Width ?? QuestionWindow.FallbackWidth, bg?.Height ?? QuestionWindow.FallbackHeight);
        Blit(canvas, bg, 0, 0, canvas.Width);
        Blit(canvas, Label(text, hues, message, QuestionWindow.LabelHue), QuestionWindow.LabelX, QuestionWindow.LabelY,
            QuestionWindow.LabelX + QuestionWindow.LabelWidth);
        Blit(canvas, Gump(data, hues, QuestionWindow.CancelNormal), QuestionWindow.CancelX, QuestionWindow.ButtonY, canvas.Width);
        Blit(canvas, Gump(data, hues, QuestionWindow.OkNormal), QuestionWindow.OkX, QuestionWindow.ButtonY, canvas.Width);
        return canvas;
    }

    private static void Write(string outDir, string name, Bitmap32 bmp)
    {
        string path = Path.Combine(outDir, name + ".png");
        Png.Write(path, bmp);
        Console.WriteLine($"{path}: {bmp.Width}x{bmp.Height} (RENDERER-ONLY offline fixture)");
    }

    // ---- HealthBarWindow.Draw ---------------------------------------------------------------------------------------

    private static Bitmap32 Bar(UOData data, UniText text, HueEntry[] hues, HealthBarWindow.State s)
    {
        Bitmap32 bg = Gump(data, hues, s.Background, s.BackgroundHue);
        var canvas = Canvas(bg?.Width ?? 130, bg?.Height ?? 50);
        Blit(canvas, bg, 0, 0, canvas.Width);
        if (s.Self)
        {
            int[] widths = { s.Hits, s.Mana, s.Stamina };
            for (int i = 0; i < HealthBarWindow.PlayerLines.Length; i++)
            {
                (int x, int y) = HealthBarWindow.PlayerLines[i];
                if (i > 0 || s.ShowsRedLine)
                    Blit(canvas, Gump(data, hues, HealthBarWindow.LineRed), x, y, canvas.Width);
                if (s.InRange)
                    Tiled(canvas, Gump(data, hues, i == 0 ? s.HitsLine : HealthBarWindow.LineBlue), x, y, widths[i]);
            }
        }
        else
        {
            if (s.ShowsRedLine)
                Blit(canvas, Gump(data, hues, HealthBarWindow.LineRed), HealthBarWindow.OtherLineX, HealthBarWindow.OtherLineY, canvas.Width);
            if (s.InRange)
                Tiled(canvas, Gump(data, hues, s.HitsLine), HealthBarWindow.OtherLineX, HealthBarWindow.OtherLineY, s.Hits);
            Blit(canvas, Label(text, hues, s.Name, s.NameHue), HealthBarWindow.NameX, HealthBarWindow.NameY,
                HealthBarWindow.NameX + HealthBarWindow.NameWidth);
            if (HealthBarWindow.ShowsPetHealButtons(s, inParty: false))
            {
                Blit(canvas, Gump(data, hues, HealthBarWindow.HealButton), 0, HealthBarWindow.HealButtonY, canvas.Width);
                Blit(canvas, Gump(data, hues, HealthBarWindow.CureButton), 0, HealthBarWindow.CureButtonY, canvas.Width);
            }
        }
        return canvas;
    }

    // ---- HealthBarWindow.DrawParty (the 0x0803 background is invisible: Alpha 0) -------------------------------------

    private static Bitmap32 PartyBar(UOData data, UniText text, HueEntry[] hues, HealthBarWindow.PartyState s)
    {
        var canvas = Canvas(HealthBarWindow.PartyWidth, HealthBarWindow.PartyHeight);
        int nameWidth = s.Self ? HealthBarWindow.PartyNameWidthSelf : HealthBarWindow.PartyNameWidthOther;
        Blit(canvas, Label(text, hues, s.Name, s.NameHue), HealthBarWindow.PartyNameX, HealthBarWindow.PartyNameY,
            HealthBarWindow.PartyNameX + nameWidth);
        if (s.ShowsBarsAndButtons)
        {
            Blit(canvas, Gump(data, hues, HealthBarWindow.HealButton), 0, HealthBarWindow.HealButtonY, canvas.Width);
            Blit(canvas, Gump(data, hues, HealthBarWindow.CureButton), 0, HealthBarWindow.CureButtonY, canvas.Width);
        }
        int[] widths = { s.Hits, s.Mana, s.Stamina };
        for (int i = 0; i < HealthBarWindow.PartyLineY.Length; i++)
        {
            if (i > 0 || s.ShowsHitsRedLine)
                Blit(canvas, Gump(data, hues, HealthBarWindow.LineRedParty), HealthBarWindow.PartyLineX, HealthBarWindow.PartyLineY[i], canvas.Width);
            if (s.ShowsBarsAndButtons)
                Tiled(canvas, Gump(data, hues, HealthBarWindow.LineBlueParty), HealthBarWindow.PartyLineX, HealthBarWindow.PartyLineY[i], widths[i]);
        }
        return canvas;
    }

    // ---- StatusWindow.Draw ------------------------------------------------------------------------------------------

    private static Bitmap32 Status(UOData data, UniText text, HueEntry[] hues, Mobile p, ClientVersion v)
    {
        Bitmap32 bg = Gump(data, hues, StatusWindow.Background);
        var canvas = Canvas(bg?.Width ?? 432, bg?.Height ?? 184);
        Blit(canvas, bg, 0, 0, canvas.Width);
        Bitmap32 name = Label(text, hues, p.Name, StatusWindow.LabelHue);
        if (name != null)
            Blit(canvas, name, new StatusWindow.Label(StatusWindow.NameX, StatusWindow.NameY, null, StatusWindow.NameWidth).TextX(name.Width),
                StatusWindow.NameY, canvas.Width);
        if (StatusWindow.HasBuffButton(v))
            Blit(canvas, Gump(data, hues, StatusWindow.BuffButton), StatusWindow.BuffButtonX, StatusWindow.BuffButtonY, canvas.Width);
        for (int stat = 0; stat < 3; stat++)
            Blit(canvas, Gump(data, hues, StatusWindow.LockGump(StatusWindow.LockOf(p, stat))), StatusWindow.LockX, StatusWindow.LockY[stat], canvas.Width);
        foreach (StatusWindow.Label l in StatusWindow.Labels(p))
        {
            Bitmap32 img = Label(text, hues, l.Text, StatusWindow.LabelHue);
            if (img != null)
                Blit(canvas, img, l.TextX(img.Width), l.Y, canvas.Width);
        }
        foreach (StatusWindow.Rule r in StatusWindow.Rules)
            for (int x = r.X; x < r.X + r.Width && x < canvas.Width; x++)
                canvas.Pixels[r.Y * canvas.Width + x] = StatusWindow.RuleArgb;
        return canvas;
    }

    // ---- PaperdollWindow.Draw's chrome (background, virtue gem, scrolls, button column; no body or worn items) -------

    private static Bitmap32 Paperdoll(UOData data, HueEntry[] hues, ClientVersion v, bool self, bool war)
    {
        Bitmap32 bg = Gump(data, hues, self ? PaperdollWindow.BackgroundPlayer : PaperdollWindow.BackgroundOther)
                      ?? Gump(data, hues, PaperdollWindow.BackgroundPlayer);
        var canvas = Canvas(bg?.Width ?? 262, bg?.Height ?? 324);
        Blit(canvas, bg, 0, 0, canvas.Width);
        Blit(canvas, Gump(data, hues, PaperdollWindow.VirtueGem), PaperdollWindow.VirtueX, PaperdollWindow.VirtueY, canvas.Width);
        Bitmap32 scroll = Gump(data, hues, PaperdollWindow.ScrollGump);
        Blit(canvas, scroll, PaperdollWindow.ProfileX, PaperdollWindow.ProfileY, canvas.Width);
        if (self && scroll != null)
            Blit(canvas, scroll, PaperdollWindow.PartyX(scroll.Width), PaperdollWindow.ProfileY, canvas.Width);
        foreach (PaperdollWindow.Button b in PaperdollWindow.Buttons(v, self, war))
            Blit(canvas, Gump(data, hues, b.Normal), b.X, b.Y, canvas.Width);
        return canvas;
    }

    // ---- PaperdollWindow.Draw for our own, with slots, body, worn art and books (made-up gear; no title) -----------

    /// <summary>A plate fighter with a katana, ring, cloak, boots and pack: (graphic, hue).</summary>
    private static readonly (ushort, ushort)[] Armour =
    {
        (0x1412, 0), (0x1413, 0), (0x1415, 0), (0x1410, 0), (0x1414, 0), (0x1411, 0), (0x13FF, 0), (0x108A, 0),
        (0x1515, 0x0021), (0x170B, 0), (0x0E75, 0)
    };

    /// <summary>PaperdollWindow.PaperdollOrder (back to front).</summary>
    private static readonly byte[] PaperdollOrder = { 20, 5, 4, 3, 24, 19, 13, 17, 8, 14, 7, 23, 22, 12, 10, 11, 16, 18, 6, 1, 2, 9, 21 };

    private static Bitmap32 PaperdollSlots(UOData data, UniText text, HueEntry[] hues, (ushort Graphic, ushort Hue)[] gear, bool books, byte outlineLayer)
    {
        var worn = new Dictionary<byte, (ushort Graphic, ushort Hue)>();
        foreach (var g in gear)
            worn[data.TileData.GetStatic(g.Graphic).Layer] = g;

        bool racial = books; // 7.0.23.1 is CV_7000+, so the racial book comes with the combat book
        Bitmap32 bg = Gump(data, hues, PaperdollWindow.BackgroundPlayer);
        var canvas = Canvas(bg?.Width ?? 262, bg?.Height ?? 324);
        Blit(canvas, bg, 0, 0, canvas.Width);
        Blit(canvas, Gump(data, hues, PaperdollWindow.VirtueGem), PaperdollWindow.VirtueX, PaperdollWindow.VirtueY, canvas.Width);

        foreach (PaperdollWindow.Slot s in PaperdollWindow.Slots)
        {
            Bitmap32 stone = Gump(data, hues, PaperdollWindow.SlotBackground);
            if (stone != null)
                Blit(canvas, stone, s.X, s.Y, s.X + PaperdollWindow.SlotWidth, PaperdollWindow.SlotWidth, PaperdollWindow.SlotHeight);
            Blit(canvas, Gump(data, hues, PaperdollWindow.SlotBorder), s.X, s.Y, canvas.Width);
            if (!worn.TryGetValue(s.Layer, out var item) || ItemArt(data, hues, item.Graphic, item.Hue) is not Bitmap32 art)
                continue;
            var (bx, by, bw, bh) = PaperdollWindow.ArtBounds(art.Pixels, art.Width, art.Height);
            var (rx, ry, rw, rh) = PaperdollWindow.SlotItemRect(bw, bh);
            if (s.Layer == outlineLayer)
            {
                Bitmap32 o = ObjectHighlight.Outline(art, 1, ObjectHighlight.OutlineArgb);
                float sx = rw / (float)bw, sy = rh / (float)bh;
                BlitScaled(canvas, o, bx, by, bw + 2, bh + 2, s.X + rx - sx, s.Y + ry - sy, rw + 2 * sx, rh + 2 * sy);
            }
            BlitScaled(canvas, art, bx, by, bw, bh, s.X + rx, s.Y + ry, rw, rh);
        }

        Blit(canvas, Gump(data, hues, 0x000C), PaperdollWindow.BodyX, PaperdollWindow.BodyY, canvas.Width);
        foreach (byte layer in PaperdollOrder)
        {
            if (!worn.TryGetValue(layer, out var item))
                continue;
            int anim = data.TileData.GetStatic(item.Graphic).AnimId;
            if (anim != 0)
                Blit(canvas, Gump(data, hues, anim + 50000, item.Hue), PaperdollWindow.BodyX, PaperdollWindow.BodyY, canvas.Width);
        }

        Bitmap32 scroll = Gump(data, hues, PaperdollWindow.ScrollGump);
        Blit(canvas, scroll, PaperdollWindow.ProfileXFor(racial), PaperdollWindow.ProfileY, canvas.Width);
        if (scroll != null)
            Blit(canvas, scroll, PaperdollWindow.PartyX(scroll.Width, racial), PaperdollWindow.ProfileY, canvas.Width);
        if (racial)
            Blit(canvas, Gump(data, hues, PaperdollWindow.RacialBookIcon), PaperdollWindow.RacialBookX, PaperdollWindow.RacialBookY, canvas.Width);
        if (books)
            Blit(canvas, Gump(data, hues, PaperdollWindow.CombatBookIcon), PaperdollWindow.CombatBookX, PaperdollWindow.CombatBookY, canvas.Width);
        foreach (PaperdollWindow.Button b in PaperdollWindow.Buttons(ClientVersion.Default, true, false))
            Blit(canvas, Gump(data, hues, b.Normal), b.X, b.Y, canvas.Width);
        Blit(canvas, Label(text, hues, "Wes the Warrior", 0x0386), PaperdollWindow.TitleX, PaperdollWindow.TitleY,
            PaperdollWindow.TitleX + PaperdollWindow.TitleWidth);
        return canvas;
    }

    // ---- CombatBookWindow.DrawPage / RacialAbilitiesWindow.DrawPage -------------------------------------------------

    private const int Ink = 0x0288;

    private static Bitmap32 Book(UOData data, HueEntry[] hues, int background, int page, int maxPage)
    {
        Bitmap32 bg = Gump(data, hues, background);
        var canvas = Canvas(bg?.Width ?? 406, bg?.Height ?? 229);
        Blit(canvas, bg, 0, 0, canvas.Width);
        if (page != 1)
            Blit(canvas, Gump(data, hues, 0x08BB), 50, 8, canvas.Width);
        if (page != maxPage)
            Blit(canvas, Gump(data, hues, 0x08BC), 321, 8, canvas.Width);
        return canvas;
    }

    private static Bitmap32 CombatBook(UOData data, UniText text, HueEntry[] hues, int page, int weapon, bool primaryOn)
    {
        var canvas = Book(data, hues, CombatBookWindow.Background, page, CombatBookWindow.MaxPage);
        void L(string s, int x, int y) => Blit(canvas, Label(text, hues, s, Ink), x, y, canvas.Width);
        if (CombatBookWindow.ShowsAbilityIcons(page))
        {
            L("INDEX", CombatBookWindow.IndexTitleLeftX, CombatBookWindow.IndexTitleY);
            L("INDEX", CombatBookWindow.IndexTitleRightX, CombatBookWindow.IndexTitleY);
            foreach (CombatBookWindow.IndexEntry e in CombatBookWindow.Index)
                if (e.Page == page)
                    L(WeaponAbilities.Names[e.Ability], e.X, e.Y);
            var (p, s) = WeaponAbilities.Of(weapon, g => data.TileData.GetStatic(g).AnimId);
            Blit(canvas, Gump(data, hues, WeaponAbilities.Icon(p), primaryOn ? CombatBookWindow.ActiveHue : 0), CombatBookWindow.AbilityIconX, CombatBookWindow.PrimaryIconY, canvas.Width);
            Blit(canvas, Gump(data, hues, WeaponAbilities.Icon(s)), CombatBookWindow.AbilityIconX, CombatBookWindow.SecondaryIconY, canvas.Width);
            L(CombatBookWindow.PrimaryText, CombatBookWindow.IconLabelX, CombatBookWindow.PrimaryIconY);
            L(CombatBookWindow.SecondaryText, CombatBookWindow.IconLabelX, CombatBookWindow.SecondaryIconY);
            return canvas;
        }
        int a = page - CombatBookWindow.IndexPages;
        Blit(canvas, Gump(data, hues, WeaponAbilities.Icon(a)), CombatBookWindow.DetailIconX, CombatBookWindow.DetailIconY, canvas.Width);
        L(CombatBookWindow.CapitalizeAllWords(WeaponAbilities.Names[a]), CombatBookWindow.DetailNameX, CombatBookWindow.DetailNameY);
        Tiled(canvas, Gump(data, hues, CombatBookWindow.Rule), CombatBookWindow.RuleX, CombatBookWindow.RuleY, CombatBookWindow.RuleWidth);
        int[] items = CombatBookWindow.Items[a - 1];
        for (int j = 0; j < items.Length; j++)
        {
            var (x, y) = CombatBookWindow.ItemLine(j);
            L(CombatBookWindow.CapitalizeAllWords(data.TileData.GetStatic(items[j]).Name), x, y);
        }
        return canvas;
    }

    private static Bitmap32 RacialBook(UOData data, UniText text, HueEntry[] hues, RacialAbilitiesWindow.Race race, int page)
    {
        RacialAbilitiesWindow.Book book = RacialAbilitiesWindow.BookOf(race);
        var canvas = Book(data, hues, RacialAbilitiesWindow.Background, page, book.MaxPage);
        void L(string s, int x, int y) => Blit(canvas, Label(text, hues, s, Ink), x, y, canvas.Width);
        if (page <= RacialAbilitiesWindow.IndexPages)
        {
            L("INDEX", RacialAbilitiesWindow.IndexTitleLeftX, RacialAbilitiesWindow.IndexTitleY);
            L("INDEX", RacialAbilitiesWindow.IndexTitleRightX, RacialAbilitiesWindow.IndexTitleY);
            for (int i = 0; i < book.Count; i++)
            {
                var (p, x, y) = RacialAbilitiesWindow.IndexLine(i);
                if (p == page)
                    L(book.Names[i], x, y);
            }
        }
        for (int i = 0; i < book.Count; i++)
        {
            if (RacialAbilitiesWindow.PageOf(i) != page)
                continue;
            var (iconX, textX) = RacialAbilitiesWindow.DetailX(i);
            L(book.Names[i], textX, RacialAbilitiesWindow.NameY);
            if (RacialAbilitiesWindow.IsPassive(race, i))
                L(RacialAbilitiesWindow.PassiveText, textX, RacialAbilitiesWindow.PassiveY);
            Blit(canvas, Gump(data, hues, book.FirstIcon + i), iconX, RacialAbilitiesWindow.IconY, canvas.Width);
            Tiled(canvas, Gump(data, hues, RacialAbilitiesWindow.Rule), iconX, RacialAbilitiesWindow.RuleY, RacialAbilitiesWindow.RuleWidth);
        }
        return canvas;
    }

    /// <summary>GumpArt.Item: the static art, hued (partly where the tiledata says so).</summary>
    private static Bitmap32 ItemArt(UOData data, HueEntry[] hues, int graphic, int hue)
    {
        Bitmap32 bmp = data.GetStatic(graphic);
        hue &= 0x3FFF;
        if (bmp != null && hue > 0 && hue <= hues.Length)
            bmp = HueApplier.Apply(bmp, hues[hue - 1], (data.TileData.GetStatic(graphic).Flags & TileFlags.PartialHue) != 0);
        return bmp;
    }

    /// <summary>GUI.DrawTextureWithTexCoords with point sampling: the source rectangle stretched over the destination.</summary>
    private static void BlitScaled(Bitmap32 canvas, Bitmap32 src, int sx, int sy, int sw, int sh, float dx, float dy, float dw, float dh)
    {
        if (sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0)
            return;
        for (int y = (int)Math.Floor(dy); y < Math.Ceiling(dy + dh); y++)
            for (int x = (int)Math.Floor(dx); x < Math.Ceiling(dx + dw); x++)
            {
                float u = (x + 0.5f - dx) / dw, v = (y + 0.5f - dy) / dh;
                if (u < 0 || v < 0 || u >= 1 || v >= 1 || x < 0 || y < 0 || x >= canvas.Width || y >= canvas.Height)
                    continue;
                int px = sx + (int)(u * sw), py = sy + (int)(v * sh);
                if (px < 0 || py < 0 || px >= src.Width || py >= src.Height)
                    continue;
                var one = new Bitmap32(1, 1);
                one.Pixels[0] = src.Pixels[py * src.Width + px];
                Blit(canvas, one, x, y, canvas.Width);
            }
    }

    // ---- art and a small software canvas (as Program.cs's) ---------------------------------------------------------

    private static readonly Dictionary<(int, int), Bitmap32> GumpCache = new Dictionary<(int, int), Bitmap32>();

    /// <summary>GumpArt.Gump: the gump, fully hued when a hue is given.</summary>
    private static Bitmap32 Gump(UOData data, HueEntry[] hues, int id, int hue = 0)
    {
        hue &= 0x3FFF;
        if (GumpCache.TryGetValue((id, hue), out Bitmap32 bmp))
            return bmp;
        bmp = id >= 0 && id < 0x10000 ? data.GetGump(id) : null;
        if (bmp != null && hue > 0 && hue <= hues.Length)
            bmp = HueApplier.Apply(bmp, hues[hue - 1], false);
        return GumpCache[(id, hue)] = bmp;
    }

    /// <summary>CachedLabel.Get: unicode font 1 in the client hue, no border.</summary>
    private static Bitmap32 Label(UniText text, HueEntry[] hues, string s, int hue) =>
        string.IsNullOrEmpty(s) ? null
            : text.Generate(s, ServerGumpWindow.GumpFont, ServerGumpWindow.LabelArgb(hues, hue), 0, UniTextAlign.Left, UniTextStyle.None).Bitmap;

    private static Bitmap32 Canvas(int w, int h)
    {
        var c = new Bitmap32(w, h);
        for (int i = 0; i < c.Pixels.Length; i++)
            c.Pixels[i] = 0xFF606060; // mid-grey where the world would show
        return c;
    }

    /// <summary>Source-over, clipped to the canvas and to x &lt; clipRight.</summary>
    private static void Blit(Bitmap32 canvas, Bitmap32 src, int x, int y, int clipRight, int width = int.MaxValue, int height = int.MaxValue)
    {
        if (src == null)
            return;
        for (int py = 0; py < Math.Min(src.Height, height); py++)
            for (int px = 0; px < Math.Min(src.Width, width); px++)
            {
                int dx = x + px, dy = y + py;
                if (dx < 0 || dy < 0 || dx >= canvas.Width || dy >= canvas.Height || dx >= clipRight)
                    continue;
                uint s = src.Pixels[py * src.Width + px], a = s >> 24;
                if (a == 0)
                    continue;
                uint d = canvas.Pixels[dy * canvas.Width + dx];
                uint Mix(int sh) => ((((s >> sh) & 0xFF) * a + ((d >> sh) & 0xFF) * (255 - a)) / 255) << sh;
                canvas.Pixels[dy * canvas.Width + dx] = 0xFF000000 | Mix(16) | Mix(8) | Mix(0);
            }
    }

    /// <summary>GumpPicWithWidth / UiWindow.DrawTiled: the gump repeated across <paramref name="width"/>, its own height.</summary>
    private static void Tiled(Bitmap32 canvas, Bitmap32 img, int x, int y, int width)
    {
        if (img == null || width <= 0 || img.Width <= 0)
            return;
        for (int tx = 0; tx < width; tx += img.Width)
            Blit(canvas, img, x + tx, y, x + width, width - tx);
    }
}
