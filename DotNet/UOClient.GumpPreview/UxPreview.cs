using System;
using System.Collections.Generic;
using System.IO;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Files;
using UOClient.IO.Formats;
using UOClient.Net;
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
    private static void Blit(Bitmap32 canvas, Bitmap32 src, int x, int y, int clipRight, int width = int.MaxValue)
    {
        if (src == null)
            return;
        for (int py = 0; py < src.Height; py++)
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
