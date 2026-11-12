using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// Character creation in the game's look (the modern paperdoll's frame, gilt and serif), in the steps of UO's own
    /// (TazUO's CharCreationGump): appearance, then a profession, then (for Advanced only) stats and skills, then the town
    /// to start in, chosen on the map. The shard is sent the choices as a custom character: a profession number with the
    /// 7.0.16+ packet makes ModernUO reset the stats (its professions total 80, that packet's must total 90), so each
    /// profession's skills and stats are filled in here instead (ModernUO's AOS prof.txt, stats brought up to 90).
    /// </summary>
    public sealed partial class LoginScreen
    {
        // ---- the professions ----

        private sealed class Profession
        {
            public string Name, Fallback;
            public int Gump, NameCliloc, DescCliloc;
            public int Str, Dex, Int;
            public (int Id, int Value)[] Skills;
            public bool Advanced;
        }

        // Skill ids (the client's order, GameSession.SkillNames).
        private const int Anatomy = 1, Blacksmithy = 7, EvalInt = 16, Healing = 17, Magery = 25, Tactics = 27, Tinkering = 37,
            Swords = 40, Wrestling = 43, Mining = 45, Meditation = 46, Necromancy = 49, Chivalry = 51;

        private static readonly Profession[] Professions =
        {
            new Profession { Name = "Warrior", Gump = 5577, NameCliloc = 1061180, DescCliloc = 1061230, Str = 45, Dex = 35, Int = 10,
                Skills = new[] { (Tactics, 50), (Healing, 45), (Swords, 5), (Wrestling, 0) },
                Fallback = "A fighter, trained in the sword and in binding wounds." },
            new Profession { Name = "Mage", Gump = 5569, NameCliloc = 1061179, DescCliloc = 1061229, Str = 25, Dex = 10, Int = 55,
                Skills = new[] { (Magery, 50), (Meditation, 50), (Wrestling, 0), (EvalInt, 0) },
                Fallback = "A student of the arcane, casting spells from a book of magic." },
            new Profession { Name = "Blacksmith", Gump = 5555, NameCliloc = 1061181, DescCliloc = 1061231, Str = 60, Dex = 15, Int = 15,
                Skills = new[] { (Blacksmithy, 50), (Tinkering, 45), (Mining, 5), (Wrestling, 0) },
                Fallback = "A crafter who mines ore and forges arms, armour and tools." },
            new Profession { Name = "Necromancer", Gump = 5557, NameCliloc = 1061178, DescCliloc = 1061228, Str = 25, Dex = 20, Int = 45,
                Skills = new[] { (Necromancy, 50), (Swords, 30), (Tactics, 20), (Wrestling, 0) },
                Fallback = "A dark caster who commands the powers of death." },
            new Profession { Name = "Paladin", Gump = 5587, NameCliloc = 1061177, DescCliloc = 1061227, Str = 40, Dex = 25, Int = 25,
                Skills = new[] { (Chivalry, 50), (Tactics, 50), (Wrestling, 0), (Healing, 0) },
                Fallback = "A holy warrior who calls upon the virtues of chivalry." },
            new Profession { Name = "Advanced", Gump = 5545, NameCliloc = 1061176, DescCliloc = 1061226, Str = 35, Dex = 30, Int = 25,
                Skills = new[] { (Swords, 30), (Tactics, 30), (Healing, 30), (Anatomy, 30) }, Advanced = true,
                Fallback = "Choose your own skills and attributes." },
        };

        /// <summary>
        /// The skills a new character may start with on this shard (ModernUO's _allowedStartingSkills, less Bushido and
        /// Ninjitsu (no SE) and Mysticism, Imbuing and Throwing (no SA)), in alphabetical order.
        /// </summary>
        private static readonly int[] StartingSkills = Enumerable.Range(0, GameSession.SkillNames.Length)
            .Where(i => i != 47 && i != 48 && i != 52 && i != 53 && i != 54 && i != 55 && i != 56 && i != 57)
            .OrderBy(i => GameSession.SkillNames[i]).ToArray();

        private int _profession = -1, _hoverProfession = -1;

        // ---- appearance values (TazUO's CharacterCreationValues: the palettes' hues are one more than listed there) ----

        private static readonly ushort[] SkinPalette =
        {
            0x03E9, 0x03F1, 0x03F9, 0x0401, 0x0409, 0x0411, 0x0419, 0x0421, 0x03EA, 0x03F2, 0x03FA, 0x0402, 0x040A, 0x0412, 0x041A, 0x0421,
            0x03EB, 0x03F3, 0x03FB, 0x0403, 0x040B, 0x0413, 0x041B, 0x0421, 0x03EC, 0x03F4, 0x03FC, 0x0404, 0x040C, 0x0414, 0x041C, 0x0421,
            0x03ED, 0x03F5, 0x03FD, 0x0405, 0x040D, 0x0415, 0x041D, 0x0421, 0x03EE, 0x03F6, 0x03FE, 0x0406, 0x040E, 0x0416, 0x041E, 0x0421,
            0x03EF, 0x03F7, 0x03FF, 0x0407, 0x040F, 0x0417, 0x041F, 0x0421, 0x03F0, 0x03F8, 0x0400, 0x0408, 0x0410, 0x0418, 0x0420, 0x0421
        };

        private static readonly ushort[] HairPalette =
        {
            0x044D, 0x0455, 0x045D, 0x0465, 0x046D, 0x0475, 0x044E, 0x0456, 0x045E, 0x0466, 0x046E, 0x0476, 0x044F, 0x0457, 0x045F, 0x0467,
            0x046F, 0x0477, 0x0450, 0x0458, 0x0460, 0x0468, 0x0470, 0x0478, 0x0451, 0x0459, 0x0461, 0x0469, 0x0471, 0x0479, 0x0452, 0x045A,
            0x0462, 0x046A, 0x0472, 0x047A, 0x0453, 0x045B, 0x0463, 0x046B, 0x0473, 0x047B, 0x0454, 0x045C, 0x0464, 0x046C, 0x0474, 0x047C
        };

        private static readonly ushort[] SkinHues = SkinPalette.Select(h => (ushort)(h + 1)).ToArray();
        private static readonly ushort[] HairHues = HairPalette.Select(h => (ushort)(h + 1)).ToArray();

        /// <summary>UO's dye picker for the shirt and pants: 200 hues, the 3rd and every 5th after (ColorPickerBox, graduation 1).</summary>
        private static readonly ushort[] ClothHues = Enumerable.Range(0, 200).Select(i => (ushort)(3 + i * 5)).ToArray();

        private static readonly (int Graphic, int Cliloc, string Name)[] MaleHair =
        {
            (0, 3000340, "None"), (0x203B, 3000341, "Short"), (0x203C, 3000342, "Long"), (0x203D, 3000343, "Ponytail"),
            (0x2044, 3000344, "Mohawk"), (0x2045, 3000345, "Pageboy"), (0x204A, 3000346, "Topknot"), (0x2047, 3000347, "Curly"),
            (0x2048, 3000348, "Receding"), (0x2049, 3000349, "Two tails"),
        };

        private static readonly (int Graphic, int Cliloc, string Name)[] FemaleHair =
        {
            (0, 3000340, "None"), (0x203B, 3000341, "Short"), (0x203C, 3000342, "Long"), (0x203D, 3000343, "Ponytail"),
            (0x2044, 3000344, "Mohawk"), (0x2045, 3000345, "Pageboy"), (0x204A, 3000346, "Topknot"), (0x2047, 3000347, "Curly"),
            (0x2049, 3000349, "Two tails"), (0x2046, 3000350, "Buns"),
        };

        private static readonly (int Graphic, int Cliloc, string Name)[] FacialHair =
        {
            (0, 3000340, "None"), (0x2040, 3000351, "Goatee"), (0x203E, 3000352, "Long beard"), (0x203F, 3000353, "Short beard"),
            (0x2041, 3000354, "Moustache"), (0x204B, 0, "Short & moustache"), (0x204C, 0, "Long & moustache"),
            (0x204D, 3000357, "Vandyke"),
        };

        // ---- the steps ----

        /// <summary>Starts a new character: the first step, appearance.</summary>
        private void BeginCreate()
        {
            _new = new NewCharacter
            {
                SkinHue = SkinHues[9], HairStyle = 0x203B, HairHue = HairHues[7], ShirtHue = Muted(0.45f, 0.65f), PantsHue = Muted(0.2f, 0.38f),
                City = DefaultCity()
            };
            _profession = -1;
            _pickingSkill = -1;
            Current = Page.Create;
        }

        /// <summary>
        /// The login tour's (LoginTour) way through creation: a step shown as a player would reach it, with a name and a
        /// profession chosen; nothing is sent.
        /// </summary>
        /// <summary>The login tour's "create": the city page's Create Character button, pressed.</summary>
        internal void TourCreate()
        {
            _new.Name = _new.Name.Trim();
            Current = Page.Connecting;
            _session.CreateCharacter(_new);
        }

        internal void TourShow(Page page, string name, int profession)
        {
            if (page == Page.Create)
                BeginCreate();
            _new.Name = name;
            _profession = profession;
            if (profession >= 0)
                ApplyProfession(Professions[profession]);
            Current = page;
        }

        /// <summary>The quietest dye (least colour, but not grey) between the two brightnesses: the default clothes.</summary>
        private ushort Muted(float minValue, float maxValue)
        {
            ushort best = ClothHues[0];
            float bestSat = float.MaxValue;
            foreach (ushort hue in ClothHues)
            {
                Color.RGBToHSV(Swatch(hue), out _, out float sat, out float val);
                if (sat > 0.15f && sat < bestSat && val >= minValue && val <= maxValue)
                {
                    best = hue;
                    bestSat = sat;
                }
            }
            return best;
        }

        private int DefaultCity()
        {
            if (_session.Cities.Count == 0)
                return 0;
            int britain = _session.Cities.FindIndex(c => c.Name.IndexOf("Britain", StringComparison.OrdinalIgnoreCase) >= 0);
            return _session.Cities[britain >= 0 ? britain : 0].Index;
        }

        private string Cliloc(int number, string fallback)
        {
            string text = number > 0 ? _session?.Cliloc?.Get(number) : null;
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        /// <summary>Step 1: name, body, hair and colours, with the character as they will look.</summary>
        private void DrawCreate(float w, float h)
        {
            Rect p = StyledPanel(w, h, 940, 660, "Create Your Character");
            NewCharacter c = _new;
            float x = p.x + 40, y = p.y + 72, colW = 260;

            Heading(new Rect(x, y, colW, 22), "Name");
            c.Name = Field(new Rect(x, y + 24, colW, 30), c.Name, 16, "create-name");
            y += 66;

            Heading(new Rect(x, y, colW, 22), "Body");
            if (Chip(new Rect(x, y + 24, colW / 2f - 4f, 28), "Male", !c.Female))
                c.Female = false;
            if (Chip(new Rect(x + colW / 2f + 4f, y + 24, colW / 2f - 4f, 28), "Female", c.Female))
            {
                c.Female = true;
                c.BeardStyle = 0;
            }
            y += 64;

            var hair = c.Female ? FemaleHair : MaleHair;
            if (!hair.Any(o => o.Graphic == c.HairStyle))
                c.HairStyle = 0x203B;
            Heading(new Rect(x, y, colW, 22), "Hair");
            y += 24;
            for (int i = 0; i < hair.Length; i++)
            {
                var r = new Rect(x + (i % 2) * (colW / 2f + 4f), y + (i / 2) * 30, colW / 2f - 4f, 26);
                if (Chip(r, Cliloc(hair[i].Cliloc, hair[i].Name), c.HairStyle == hair[i].Graphic, 14))
                    c.HairStyle = (ushort)hair[i].Graphic;
            }
            y += (hair.Length + 1) / 2 * 30 + 12;

            if (!c.Female)
            {
                Heading(new Rect(x, y, colW, 22), "Facial hair");
                y += 24;
                for (int i = 0; i < FacialHair.Length; i++)
                {
                    var r = new Rect(x + (i % 2) * (colW / 2f + 4f), y + (i / 2) * 30, colW / 2f - 4f, 26);
                    if (Chip(r, Cliloc(FacialHair[i].Cliloc, FacialHair[i].Name), c.BeardStyle == FacialHair[i].Graphic, 14))
                        c.BeardStyle = (ushort)FacialHair[i].Graphic;
                }
            }

            // The character, lit in the paperdoll's niche.
            var niche = new Rect(p.x + 330, p.y + 72, 290, p.height - 160);
            PaperdollSkin.Niche(niche);
            DrawPreview(niche, c);

            // The colours.
            float rx = p.x + 650, rw = 250f;
            float ry = p.y + 72;
            Heading(new Rect(rx, ry, rw, 22), "Skin");
            c.SkinHue = Swatches(new Rect(rx, ry + 24, rw, 0), SkinHues, 16, c.SkinHue, out float used);
            ry += 24 + used + 14;
            Heading(new Rect(rx, ry, rw, 22), "Hair colour");
            c.HairHue = Swatches(new Rect(rx, ry + 24, rw, 0), HairHues, 16, c.HairHue, out used);
            c.BeardHue = c.HairHue;
            ry += 24 + used + 14;
            Heading(new Rect(rx, ry, rw, 22), "Shirt");
            c.ShirtHue = Swatches(new Rect(rx, ry + 24, rw, 0), ClothHues, 25, c.ShirtHue, out used);
            ry += 24 + used + 14;
            Heading(new Rect(rx, ry, rw, 22), "Pants");
            c.PantsHue = Swatches(new Rect(rx, ry + 24, rw, 0), ClothHues, 25, c.PantsHue, out used);

            bool named = c.Name.Trim().Length >= 2;
            if (!named)
                Note(new Rect(p.x + 220, p.yMax - 64, p.width - 440, 28), "Give your character a name to continue.");
            if (Scroll(new Rect(p.x + 40, p.yMax - 70, 170, 40), "Back"))
                Current = Page.Characters;
            if (Scroll(new Rect(p.xMax - 210, p.yMax - 70, 170, 40), "Next", named) || named && EnterPressed())
            {
                c.Name = c.Name.Trim();
                Current = Page.Profession;
            }
        }

        /// <summary>Step 2: a profession, its skills and stats; Advanced lets the player choose their own.</summary>
        private void DrawProfession(float w, float h)
        {
            Rect p = StyledPanel(w, h, 820, 620, "Choose a Profession");
            const float cardW = 230f, cardH = 150f, gap = 20f;
            float gx = p.x + (p.width - (cardW * 3 + gap * 2)) / 2f, gy = p.y + 76;
            _hoverProfession = -1;
            for (int i = 0; i < Professions.Length; i++)
            {
                Profession pr = Professions[i];
                var r = new Rect(gx + (i % 3) * (cardW + gap), gy + (i / 3) * (cardH + gap), cardW, cardH);
                bool hot = r.Contains(Event.current.mousePosition), chosen = _profession == i;
                if (hot)
                    _hoverProfession = i;
                Card(r, hot, chosen);
                UiImage icon = _art.GumpDetailed(pr.Gump) ?? _art.Gump(pr.Gump);
                if (icon != null)
                    GUI.DrawTexture(new Rect(r.center.x - 40, r.y + 18, 80, 80), icon.Texture, ScaleMode.ScaleToFit);
                PaperdollSkin.Title(new Rect(r.x, r.y + 104, r.width, 30), Cliloc(pr.NameCliloc, pr.Name), 20, chosen || hot ? Color.white : PaperdollSkin.Gold);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    _profession = i;
                    if (!pr.Advanced || _new.Skills.All(s => s.Value == 0))
                        ApplyProfession(pr);
                }
            }

            // What the profession is, and what it starts with.
            int shown = _hoverProfession >= 0 ? _hoverProfession : _profession;
            var info = new Rect(p.x + 40, gy + cardH * 2 + gap + 14, p.width - 80, 130);
            Inset(info);
            if (shown >= 0)
            {
                Profession pr = Professions[shown];
                Body(new Rect(info.x + 14, info.y + 8, info.width - 28, 86), StripHtml(Cliloc(pr.DescCliloc, pr.Fallback)));
                string skills = pr.Advanced ? "Your own choice of four skills and your attributes."
                    : $"Strength {pr.Str} · Dexterity {pr.Dex} · Intelligence {pr.Int}     "
                      + string.Join(", ", pr.Skills.Where(s => s.Value > 0).Select(s => $"{GameSession.SkillNames[s.Id]} {s.Value}"));
                PaperdollSkin.Title(new Rect(info.x + 14, info.yMax - 34, info.width - 28, 26), skills, 15, PaperdollSkin.GoldFaded, false, TextAnchor.MiddleLeft);
            }
            else
                Body(new Rect(info.x + 14, info.y + 8, info.width - 28, 90), "Choose how your character begins: a profession sets their skills and attributes, or Advanced lets you set them yourself.");

            if (Scroll(new Rect(p.x + 40, p.yMax - 70, 170, 40), "Back"))
                Current = Page.Create;
            if (Scroll(new Rect(p.xMax - 210, p.yMax - 70, 170, 40), "Next", _profession >= 0))
            {
                if (Professions[_profession].Advanced)
                    Current = Page.Skills;
                else
                {
                    ApplyProfession(Professions[_profession]);
                    Current = Page.City;
                }
            }
        }

        private void ApplyProfession(Profession pr)
        {
            _new.Str = pr.Str;
            _new.Dex = pr.Dex;
            _new.Int = pr.Int;
            _new.Skills = pr.Skills.ToArray();
        }

        /// <summary>Step 2b (Advanced): the three attributes (total 90) and four skills (total 120), each kept in balance.</summary>
        private void DrawSkills(float w, float h)
        {
            Rect p = StyledPanel(w, h, 820, 600, "Skills & Attributes");
            NewCharacter c = _new;
            float x = p.x + 50, y = p.y + 80, labelW = 150, barW = 300;

            Heading(new Rect(x, y, 300, 22), "Attributes");
            y += 30;
            int[] stats = { c.Str, c.Dex, c.Int };
            string[] statNames = { "Strength", "Dexterity", "Intelligence" };
            for (int i = 0; i < 3; i++)
            {
                PaperdollSkin.Title(new Rect(x, y, labelW, 26), statNames[i], 17, PaperdollSkin.Gold, true, TextAnchor.MiddleLeft);
                int v = Slider(new Rect(x + labelW, y + 4, barW, 18), stats[i], 10, 60);
                PaperdollSkin.Title(new Rect(x + labelW + barW + 14, y, 50, 26), stats[i].ToString(), 17, Color.white, true, TextAnchor.MiddleLeft);
                if (v != stats[i])
                    Rebalance(stats, i, v, 10, 60, 90);
                y += 34;
            }
            (c.Str, c.Dex, c.Int) = (stats[0], stats[1], stats[2]);
            y += 18;

            Heading(new Rect(x, y, 300, 22), "Skills");
            y += 30;
            int[] values = c.Skills.Select(s => s.Value).ToArray();
            for (int i = 0; i < 4; i++)
            {
                var (id, _) = c.Skills[i];
                if (Chip(new Rect(x, y, labelW + 40, 28), GameSession.SkillNames[id], _pickingSkill == i, 15))
                    _pickingSkill = _pickingSkill == i ? -1 : i;
                int v = Slider(new Rect(x + labelW + 54, y + 5, barW - 54, 18), values[i], 0, 50);
                PaperdollSkin.Title(new Rect(x + labelW + barW + 14, y, 50, 26), values[i].ToString(), 17, Color.white, true, TextAnchor.MiddleLeft);
                if (v != values[i])
                    Rebalance(values, i, v, 0, 50, 120);
                y += 36;
            }
            for (int i = 0; i < 4; i++)
                c.Skills[i] = (c.Skills[i].Id, values[i]);

            bool distinct = c.Skills.Select(s => s.Id).Distinct().Count() == 4;
            int total = values.Sum();
            Note(new Rect(x, y + 6, 520, 26), !distinct ? "Choose four different skills." : $"Skill points: {total} of 120");

            // The skill list, beside them while one is being chosen.
            if (_pickingSkill >= 0)
            {
                var list = new Rect(p.xMax - 280, p.y + 80, 240, p.height - 170);
                Inset(list);
                _skillScroll = GUI.BeginScrollView(new Rect(list.x + 6, list.y + 6, list.width - 12, list.height - 12), _skillScroll,
                    new Rect(0, 0, list.width - 30, StartingSkills.Length * 28));
                for (int k = 0; k < StartingSkills.Length; k++)
                {
                    int sk = StartingSkills[k];
                    bool taken = c.Skills.Where((s, j) => j != _pickingSkill).Any(s => s.Id == sk);
                    if (Chip(new Rect(0, k * 28, list.width - 32, 25), GameSession.SkillNames[sk], c.Skills[_pickingSkill].Id == sk, 14, !taken))
                    {
                        c.Skills[_pickingSkill] = (sk, c.Skills[_pickingSkill].Value);
                        _pickingSkill = -1;
                    }
                }
                GUI.EndScrollView();
            }

            bool valid = distinct && c.Str + c.Dex + c.Int == 90 && total == 120;
            if (Scroll(new Rect(p.x + 40, p.yMax - 70, 170, 40), "Back"))
            {
                _pickingSkill = -1;
                Current = Page.Profession;
            }
            if (Scroll(new Rect(p.xMax - 210, p.yMax - 70, 170, 40), "Next", valid))
            {
                _pickingSkill = -1;
                Current = Page.City;
            }
        }

        /// <summary>
        /// Moves one value to <paramref name="to"/> and spreads the difference over the others (each within its bounds), so
        /// the total stays what it was, as UO's sliders do.
        /// </summary>
        private static void Rebalance(int[] values, int index, int to, int min, int max, int total)
        {
            values[index] = to;
            int excess = values.Sum() - total;
            for (int guard = 0; excess != 0 && guard < 400; guard++)
            {
                // Take from (or give to) the others in turn, the largest first when taking, the smallest first when giving.
                var order = Enumerable.Range(0, values.Length).Where(k => k != index)
                    .OrderBy(k => excess > 0 ? -values[k] : values[k]).ToArray();
                bool moved = false;
                foreach (int k in order)
                {
                    if (excess > 0 && values[k] > min) { values[k]--; excess--; moved = true; }
                    else if (excess < 0 && values[k] < max) { values[k]++; excess++; moved = true; }
                    if (excess == 0)
                        break;
                }
                if (!moved)
                {
                    values[index] -= excess; // the others are at their bounds: this one gives way
                    excess = 0;
                }
            }
        }

        /// <summary>Step 3: the town to start in, on the painted map of Britannia; Create sends it all.</summary>
        private void DrawCity(float w, float h)
        {
            Rect p = StyledPanel(w, h, 1000, 700, "Choose Your Starting Town");
            NewCharacter c = _new;
            var mapRect = new Rect(p.x + 40, p.y + 72, 530, 530);
            Inset(mapRect);
            var art = new Rect(mapRect.x + 6, mapRect.y + 6, mapRect.width - 12, mapRect.height - 12);
            UiImage map = _art.GumpDetailed(0x1598) ?? _art.Gump(0x1598);
            if (map != null)
                GUI.DrawTexture(art, map.Texture, ScaleMode.StretchToFill);

            // The painted map shows Britannia's 5120 x 4096 tiles (TazUO's town button places agree within a few pixels).
            int hovered = -1;
            Vector2 mouse = Event.current.mousePosition;
            for (int i = 0; i < _session.Cities.Count; i++)
            {
                StartCity city = _session.Cities[i];
                if (city.X <= 0 && city.Y <= 0)
                    continue;
                var at = new Vector2(art.x + city.X / 5120f * art.width, art.y + city.Y / 4096f * art.height);
                var hit = new Rect(at.x - 12, at.y - 12, 24, 24);
                bool chosen = c.City == city.Index, hot = hit.Contains(mouse);
                if (hot)
                    hovered = i;
                TownMarker(at, chosen, hot);
                var label = new Rect(at.x - 80, at.y - 34, 160, 22);
                Haloed(label, city.Name, chosen ? 18 : 16, chosen ? Color.white : hot ? new Color(1f, 0.95f, 0.8f) : new Color(1f, 0.86f, 0.55f));
                if (GUI.Button(hit, GUIContent.none, GUIStyle.none) | GUI.Button(label, GUIContent.none, GUIStyle.none))
                    c.City = city.Index;
            }

            // The chosen town: its name, the inn, and its story.
            int shownIndex = hovered >= 0 ? hovered : _session.Cities.FindIndex(ct => ct.Index == c.City);
            var info = new Rect(mapRect.xMax + 30, mapRect.y, p.xMax - 40 - (mapRect.xMax + 30), mapRect.height);
            Inset(info);
            if (shownIndex >= 0)
            {
                StartCity city = _session.Cities[shownIndex];
                PaperdollSkin.Title(new Rect(info.x + 16, info.y + 14, info.width - 32, 36), city.Name, 28, PaperdollSkin.Gold);
                PaperdollSkin.Title(new Rect(info.x + 16, info.y + 50, info.width - 32, 24), city.Building, 16, PaperdollSkin.GoldFaded);
                Rule(new Rect(info.x + 30, info.y + 84, info.width - 60, 1));
                string text = StripHtml(Cliloc(city.Cliloc, ""));
                if (text.Length > 0)
                {
                    float textH = _bodyStyle != null ? _bodyStyle.CalcHeight(new GUIContent(text), info.width - 48) : 400;
                    _cityScroll = GUI.BeginScrollView(new Rect(info.x + 16, info.y + 96, info.width - 24, info.height - 110), _cityScroll,
                        new Rect(0, 0, info.width - 48, textH + 10));
                    Body(new Rect(0, 0, info.width - 48, textH + 10), text);
                    GUI.EndScrollView();
                }
            }

            if (Scroll(new Rect(p.x + 40, p.yMax - 70, 170, 40), "Back"))
                Current = _profession >= 0 && Professions[_profession].Advanced ? Page.Skills : Page.Profession;
            if (Scroll(new Rect(p.xMax - 250, p.yMax - 70, 210, 40), "Create Character", _session.Cities.Count > 0))
            {
                c.Name = c.Name.Trim();
                Current = Page.Connecting;
                _session.CreateCharacter(c);
            }
        }

        private Vector2 _cityScroll;

        /// <summary>Words over the map, ringed in dark so they read over land and sea alike.</summary>
        private static void Haloed(Rect r, string text, int size, Color color)
        {
            var dark = new Color(0.04f, 0.03f, 0.02f, 0.9f);
            foreach (var o in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1), new Vector2(1, 1), new Vector2(-1, 1) })
                PaperdollSkin.Title(new Rect(r.x + o.x, r.y + o.y, r.width, r.height), text, size, dark, false);
            PaperdollSkin.Title(r, text, size, color, false);
        }

        /// <summary>A town's mark on the map: a gilt pin, brighter when chosen or under the mouse.</summary>
        private void TownMarker(Vector2 at, bool chosen, bool hot)
        {
            _pin ??= MakeDisc(32);
            float size = chosen ? 22f : hot ? 20f : 16f;
            Color was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(at.x - size / 2f + 2, at.y - size / 2f + 2, size, size), _pin);
            GUI.color = chosen ? new Color(1f, 0.85f, 0.45f) : hot ? new Color(1f, 0.92f, 0.7f) : new Color(0.8f, 0.6f, 0.3f);
            GUI.DrawTexture(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), _pin);
            GUI.color = was;
        }

        private Texture2D _pin;

        /// <summary>A disc with a dark centre and a light rim (tinted by GUI.color).</summary>
        private static Texture2D MakeDisc(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01((1f - d) * n / 2f);
                    float v = d > 0.62f ? 1f : Mathf.Lerp(0.55f, 0.85f, d / 0.62f);
                    px[y * n + x] = new Color(v, v, v, a);
                }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        // ---- the character preview ----

        /// <summary>The new character as the paperdoll shows them, as large as the niche allows.</summary>
        private void DrawPreview(Rect niche, NewCharacter c)
        {
            int bodyGump = c.Female ? 0x000D : 0x000C;
            UiImage body = _art.Gump(bodyGump, c.SkinHue);
            if (body == null)
                return;
            // The body gump has room round the figure: fitted by where it is drawn, feet near the niche's floor.
            RectInt b = DrawnBounds(body);
            float k = Mathf.Min((niche.width - 40f) / b.width, (niche.height - 50f) / b.height);
            var at = new Vector2(niche.center.x - (b.x + b.width / 2f) * k, niche.yMax - 22f - (b.y + b.height) * k);
            void Piece(int gump, int hue, bool partial)
            {
                UiImage img = _art.GumpSmooth(gump, hue, partial) ?? _art.Gump(gump, hue, partial);
                UiImage size = _art.Gump(gump, hue, partial);
                if (img != null && size != null)
                    GUI.DrawTexture(new Rect(at.x, at.y, size.Width * k, size.Height * k), img.Texture);
            }
            Piece(bodyGump, c.SkinHue, false);
            void Worn(int itemGraphic, int hue)
            {
                if (itemGraphic == 0)
                    return;
                StaticTileData td = _art.Data.TileData.GetStatic(itemGraphic);
                if (td.AnimId == 0)
                    return;
                bool partial = (td.Flags & TileFlags.PartialHue) != 0;
                int gump = c.Female && _art.Gump(td.AnimId + 60000, hue, partial) != null ? td.AnimId + 60000 : td.AnimId + 50000;
                Piece(gump, hue, partial);
            }
            Worn(0x152E, c.PantsHue);                       // long pants
            Worn(c.Female ? 0x1F01 : 0x1517, c.ShirtHue);   // plain dress / shirt
            Worn(0x170F, 0);                                // shoes
            Worn(c.HairStyle, c.HairHue);
            if (!c.Female)
                Worn(c.BeardStyle, c.BeardHue);
        }

        private RectInt _bodyBounds;
        private UiImage _boundsOf;

        /// <summary>Where an image has pixels (cached for the last one asked: the body, which changes only with the sex).</summary>
        private RectInt DrawnBounds(UiImage img)
        {
            if (ReferenceEquals(img, _boundsOf))
                return _bodyBounds;
            int x0 = img.Width, y0 = img.Height, x1 = -1, y1 = -1;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    if ((img.Bitmap[x, y] >> 24) != 0)
                    {
                        x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                    }
            _boundsOf = img;
            return _bodyBounds = x1 < 0 ? new RectInt(0, 0, img.Width, img.Height) : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        // ---- widgets in the game's look ----

        private GUIStyle _bodyStyle, _fieldStyle, _sliderStyle, _thumbStyle;

        /// <summary>A framed panel in the middle of the screen with its title; returns its rect.</summary>
        private static Rect StyledPanel(float w, float h, float pw, float ph, string title)
        {
            pw = Mathf.Min(pw, w - 20f);
            ph = Mathf.Min(ph, h - 20f);
            var r = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
            PaperdollSkin.Frame(r, 18f, 2.6f);
            if (title != null)
            {
                PaperdollSkin.Title(new Rect(r.x, r.y + 22, r.width, 36), title, 28, PaperdollSkin.Gold);
                Rule(new Rect(r.center.x - 140, r.y + 60, 280, 1));
            }
            return r;
        }

        /// <summary>A thin gilt rule, fading at both ends.</summary>
        private static void Rule(Rect r)
        {
            Color was = GUI.color;
            for (int i = 0; i < 8; i++)
            {
                float f = (i + 1) / 8f;
                GUI.color = new Color(PaperdollSkin.Gold.r, PaperdollSkin.Gold.g, PaperdollSkin.Gold.b, f * 0.7f);
                float inset = r.width * (1f - f) / 2f;
                GUI.DrawTexture(new Rect(r.x + inset, r.y, r.width - inset * 2f, r.height), Texture2D.whiteTexture);
            }
            GUI.color = was;
        }

        private static void Heading(Rect r, string text) =>
            PaperdollSkin.Title(r, text, 17, PaperdollSkin.Gold, true, TextAnchor.MiddleLeft);

        private void Note(Rect r, string text) =>
            PaperdollSkin.Title(r, text, 15, PaperdollSkin.GoldFaded, true, TextAnchor.MiddleCenter);

        /// <summary>Running words in the serif, wrapped.</summary>
        private void Body(Rect r, string text)
        {
            if (_bodyStyle == null)
            {
                _bodyStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 16, richText = false, alignment = TextAnchor.UpperLeft };
                if (PaperdollSkin.SerifFont != null)
                    _bodyStyle.font = PaperdollSkin.SerifFont;
                _bodyStyle.normal.textColor = new Color(0.9f, 0.86f, 0.76f);
            }
            GUI.Label(r, text, _bodyStyle);
        }

        /// <summary>A dark recessed area with a gilt edge (lists, the map, descriptions).</summary>
        private static void Inset(Rect r)
        {
            Fill(r, new Color(0.025f, 0.022f, 0.02f, 0.88f));
            Outline(r, PaperdollSkin.GoldDim, 1f);
        }

        /// <summary>A profession card: a dark tile with a gilt edge, lit under the mouse, bright when chosen.</summary>
        private static void Card(Rect r, bool hot, bool chosen)
        {
            Fill(r, chosen ? new Color(0.13f, 0.1f, 0.06f, 0.95f) : hot ? new Color(0.09f, 0.08f, 0.065f, 0.95f) : new Color(0.04f, 0.037f, 0.033f, 0.92f));
            Outline(r, chosen ? PaperdollSkin.Gold : hot ? PaperdollSkin.GoldFaded : PaperdollSkin.GoldDim, chosen ? 2f : 1f);
        }

        /// <summary>A choice: dark with a gilt edge, filled when selected. Returns true when clicked.</summary>
        private static bool Chip(Rect r, string text, bool selected, int size = 16, bool enabled = true)
        {
            bool hot = enabled && r.Contains(Event.current.mousePosition);
            Fill(r, selected ? new Color(0.36f, 0.27f, 0.12f, 0.95f) : hot ? new Color(0.12f, 0.1f, 0.075f, 0.95f) : new Color(0.05f, 0.045f, 0.04f, 0.9f));
            Outline(r, selected ? PaperdollSkin.Gold : hot ? PaperdollSkin.GoldFaded : PaperdollSkin.GoldDim, 1f);
            Color ink = !enabled ? new Color(0.5f, 0.46f, 0.4f) : selected ? Color.white : new Color(0.92f, 0.87f, 0.75f);
            PaperdollSkin.Title(new Rect(r.x + 6, r.y, r.width - 12, r.height), text, size, ink, selected, TextAnchor.MiddleCenter);
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>A scroll button (the paperdoll's); dimmed and inert when not enabled. Returns true when clicked.</summary>
        private static bool Scroll(Rect r, string label, bool enabled = true)
        {
            bool hot = enabled && r.Contains(Event.current.mousePosition);
            PaperdollSkin.ScrollButton(r, null, label, hot, hot && Input.GetMouseButton(0));
            if (!enabled)
                Fill(r, new Color(0.02f, 0.02f, 0.02f, 0.55f));
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>A text box: dark, gilt-edged, in the serif.</summary>
        private string Field(Rect r, string text, int max, string controlName, int fontSize = 18)
        {
            if (_fieldStyle == null)
            {
                _fieldStyle = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = 18, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 2, 2),
                    border = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), overflow = new RectOffset(0, 0, 0, 0)
                };
                if (PaperdollSkin.SerifFont != null)
                    _fieldStyle.font = PaperdollSkin.SerifFont;
                var ink = new Color(0.96f, 0.92f, 0.82f);
                foreach (GUIStyleState st in new[] { _fieldStyle.normal, _fieldStyle.hover, _fieldStyle.active, _fieldStyle.focused })
                {
                    st.background = null;
                    st.textColor = ink;
                }
            }
            _fieldStyle.fontSize = fontSize;
            Fill(r, new Color(0.02f, 0.018f, 0.016f, 0.95f));
            Outline(r, GUI.GetNameOfFocusedControl() == controlName ? PaperdollSkin.Gold : PaperdollSkin.GoldDim, 1f);
            GUI.SetNextControlName(controlName);
            return GUI.TextField(r, text, max, _fieldStyle);
        }

        /// <summary>A gilt slider: the track, the filled part and a round knob; returns the (whole) value.</summary>
        private int Slider(Rect r, int value, int min, int max)
        {
            if (_sliderStyle == null)
            {
                _sliderStyle = new GUIStyle();
                _thumbStyle = new GUIStyle { fixedWidth = 18, fixedHeight = 18 };
            }
            float f = (value - min) / (float)(max - min);
            var track = new Rect(r.x, r.center.y - 3f, r.width, 6f);
            Fill(track, new Color(0.03f, 0.03f, 0.03f, 0.95f));
            Outline(track, PaperdollSkin.GoldDim, 1f);
            Fill(new Rect(track.x + 1, track.y + 1, (track.width - 2) * f, track.height - 2), new Color(0.62f, 0.46f, 0.2f));
            _pin ??= MakeDisc(32);
            Color was = GUI.color;
            GUI.color = new Color(0.95f, 0.8f, 0.5f);
            GUI.DrawTexture(new Rect(r.x + f * r.width - 9f, r.center.y - 9f, 18f, 18f), _pin);
            GUI.color = was;
            float v = GUI.HorizontalSlider(r, value, min, max, _sliderStyle, _thumbStyle);
            return Mathf.Clamp(Mathf.RoundToInt(v), min, max);
        }

        /// <summary>
        /// A palette of hues in rows of <paramref name="columns"/> filling <paramref name="area"/>'s width; the chosen one
        /// outlined. Returns the hue chosen (the current one unless a swatch was clicked) and the height used.
        /// </summary>
        private ushort Swatches(Rect area, ushort[] hues, int columns, ushort current, out float height)
        {
            float cell = area.width / columns;
            int rows = (hues.Length + columns - 1) / columns;
            height = rows * cell;
            Fill(new Rect(area.x - 1, area.y - 1, area.width + 2, height + 2), new Color(0f, 0f, 0f, 0.9f));
            ushort picked = current;
            Rect chosenRect = default;
            for (int i = 0; i < hues.Length; i++)
            {
                var r = new Rect(area.x + (i % columns) * cell, area.y + (i / columns) * cell, cell, cell);
                Fill(new Rect(r.x + 0.5f, r.y + 0.5f, r.width - 1f, r.height - 1f), Swatch(hues[i]));
                if (hues[i] == current)
                    chosenRect = r;
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(Event.current.mousePosition))
                {
                    picked = hues[i];
                    Event.current.Use();
                }
            }
            if (chosenRect.width > 0)
            {
                Outline(new Rect(chosenRect.x - 2, chosenRect.y - 2, chosenRect.width + 4, chosenRect.height + 4), Color.black, 1f);
                Outline(new Rect(chosenRect.x - 1, chosenRect.y - 1, chosenRect.width + 2, chosenRect.height + 2), Color.white, 1f);
            }
            return picked;
        }

        private readonly Dictionary<int, Color> _swatchColors = new Dictionary<int, Color>();

        /// <summary>A hue's colour as a swatch shows it: the middle of its ramp (what cloth or skin mostly shows).</summary>
        private Color Swatch(int hue)
        {
            if (_swatchColors.TryGetValue(hue, out Color c))
                return c;
            var entries = _art.Data.Hues.Entries;
            c = Color.gray;
            if (hue > 0 && hue <= entries.Length)
            {
                uint argb = Color16.ToArgb32(entries[hue - 1].Colors[18]);
                c = new Color(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, 1f);
            }
            return _swatchColors[hue] = c;
        }

        private static bool EnterPressed() =>
            Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

        /// <summary>Cliloc descriptions carry a little HTML (line breaks, centring): plain text with its breaks.</summary>
        private static string StripHtml(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            text = System.Text.RegularExpressions.Regex.Replace(text, "<br\\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "").Trim();
        }

        private static void Fill(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }

        private static void Outline(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y + t, t, r.height - t * 2f), c);
            Fill(new Rect(r.xMax - t, r.y + t, t, r.height - t * 2f), c);
        }
    }
}
