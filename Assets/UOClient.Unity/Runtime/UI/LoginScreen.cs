using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The screens before the world: account login, shard choice (when there is more than one),
    /// character select (play, delete), and character creation with a paperdoll preview.
    /// </summary>
    public sealed class LoginScreen
    {
        public enum Page { Login, Connecting, Servers, Characters, Create }

        public Page Current = Page.Login;
        public string Host = "127.0.0.1", Account = "", Password = "", Status = "";
        public int Port { get => _port; set { _port = value; _portText = value.ToString(); } }
        private int _port = 2593;
        public bool RememberPassword = true;

        /// <summary>Called with the filled-in settings when the player presses Log in.</summary>
        public Action<LoginScreen> LoginPressed;
        public Action QuitPressed;

        /// <summary>Opens the options window over the login screen (music volume works here); null hides the button.</summary>
        public Action OptionsPressed;

        /// <summary>The login video's current frame, or null for the mockup / dark backdrop.</summary>
        public Func<Texture> VideoFrame;

        /// <summary>The account and password remembered for a server (empty when none).</summary>
        public Func<string, (string Account, string Password)> RememberedFor;

        private readonly GumpArt _art;
        private GameSession _session;
        private Texture2D _backdrop, _shade;
        private Texture2D _logo, _accountPanel, _passwordPanel, _loginButton;
        private GUIStyle _ornamentField;
        private bool _ornamentLoaded;
        private GUIStyle _title, _label, _small, _field, _toggle;
        private string _portText = "2593";
        private NewCharacter _new = new NewCharacter();
        private int _deleteArmed = -1;
        private bool _focusedOnce;
        private float _deleteArmedAt;
        private Vector2 _skillScroll;
        private int _pickingSkill = -1;

        public LoginScreen(GumpArt art, string backdropPath)
        {
            _art = art;
            if (File.Exists(backdropPath))
            {
                _backdrop = new Texture2D(2, 2);
                _backdrop.LoadImage(File.ReadAllBytes(backdropPath));
            }
            _shade = new Texture2D(1, 1);
            _shade.SetPixel(0, 0, Color.white);
            _shade.Apply();
            LoadOrnamentArt();
        }

        private void LoadOrnamentArt()
        {
            if (_ornamentLoaded)
                return;
            _ornamentLoaded = true;
            string root = Application.streamingAssetsPath;
            _logo = LoadPng(LoginMediaRules.OrnamentPath(root, LoginMediaRules.LogoRelativePath));
            _accountPanel = LoadPng(LoginMediaRules.OrnamentPath(root, LoginMediaRules.AccountPanelRelativePath));
            _passwordPanel = LoadPng(LoginMediaRules.OrnamentPath(root, LoginMediaRules.PasswordPanelRelativePath));
            _loginButton = LoadPng(LoginMediaRules.OrnamentPath(root, LoginMediaRules.LoginButtonRelativePath));
        }

        private static Texture2D LoadPng(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private bool HasOrnament =>
            _logo != null && _accountPanel != null && _passwordPanel != null && _loginButton != null;

        /// <summary>Follows a session through login: shard list, character list, or a failure back to the form.</summary>
        public void Attach(GameSession session)
        {
            _session = session;
            Current = Page.Connecting;
            Status = "Connecting...";
            // Only the session followed now: one cancelled or dropped (Failed) changes nothing here any more.
            session.StatusChanged += s => { if (_session == session) Status = s; };
            session.ServersListed += () => { if (_session == session) Current = Page.Servers; };
            session.CharactersListed += () =>
            {
                if (_session != session)
                    return;
                Current = Page.Characters;
                _deleteArmed = -1;
            };
        }

        /// <summary>The session ended (dropped, cancelled, logged out, or lost in the world): back to the form, keeping the reason.</summary>
        public void Failed(string reason)
        {
            _session = null;
            Current = Page.Login;
            Status = reason;
        }

        public void Draw()
        {
            EnsureStyles();
            float s = UiScale.Current;
            float w = Screen.width / s, h = Screen.height / s;

            // Backdrop: the login video, whole (contain) on dark bars under a light scrim; without it the
            // art-direction mockup, cropped to fill and darkened; without that, plain dark.
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            Texture video = VideoFrame?.Invoke();
            if (video != null)
            {
                GUI.color = new Color(0.02f, 0.02f, 0.03f, 1f);
                GUI.DrawTexture(screen, _shade);
                var (vx, vy, vw, vh) = LoginMediaRules.Contain(Screen.width, Screen.height, video.width, video.height);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(vx, vy, vw, vh), video, ScaleMode.StretchToFill);
                GUI.color = new Color(0.02f, 0.02f, 0.03f, LoginMediaRules.VideoScrim);
                GUI.DrawTexture(screen, _shade);
            }
            else
            {
                GUI.color = Color.white;
                if (_backdrop != null)
                    GUI.DrawTexture(screen, _backdrop, ScaleMode.ScaleAndCrop);
                GUI.color = new Color(0.02f, 0.02f, 0.03f, _backdrop != null ? 0.55f : 1f);
                GUI.DrawTexture(screen, _shade);
            }
            GUI.color = Color.white;

            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            if (!HasOrnament)
                GUI.Label(new Rect(0, h * 0.08f, w, 60), "Unity UO", _title);
            // Top right, clear of the panel and of the video's bottom-right caption.
            if (OptionsPressed != null && GUI.Button(new Rect(w - 110, 16, 94, 28), "Options"))
                OptionsPressed();

            switch (Current)
            {
                case Page.Login: DrawLogin(w, h); break;
                case Page.Connecting: DrawConnecting(w, h); break;
                case Page.Servers: DrawServers(w, h); break;
                case Page.Characters: DrawCharacters(w, h); break;
                case Page.Create: DrawCreate(w, h); break;
            }
            GUI.matrix = saved;
        }

        private Rect Panel(float w, float h, float pw, float ph)
        {
            var r = new Rect((w - pw) / 2, Mathf.Max(h * 0.2f, (h - ph) / 2), pw, ph);
            ResizePic(9200, r);
            return r;
        }

        private void DrawLogin(float w, float h)
        {
            if (HasOrnament)
                DrawOrnamentLogin(w, h);
            else
                DrawPlainLogin(w, h);
        }

        private void DrawPlainLogin(float w, float h)
        {
            Rect p = Panel(w, h, 420, 330);
            float x = p.x + 30, y = p.y + 28, fw = p.width - 60;
            GUI.Label(new Rect(x, y, fw, 20), "Account", _label);
            GUI.SetNextControlName("login-account");
            Account = GUI.TextField(new Rect(x, y + 20, fw, 26), Account, 30, _field);
            y += 56;
            GUI.Label(new Rect(x, y, fw, 20), "Password", _label);
            GUI.SetNextControlName("login-password");
            Password = GUI.PasswordField(new Rect(x, y + 20, fw, 26), Password, '*', 30, _field);
            y += 56;
            GUI.Label(new Rect(x, y, fw, 20), "Server", _label);
            string typedHost = GUI.TextField(new Rect(x, y + 20, fw - 90, 26), Host, 64, _field);
            if (typedHost != Host)
            {
                // Each shard has its own remembered login: another server's account is never carried over.
                Host = typedHost;
                (string account, string password) = RememberedFor?.Invoke(Host.Trim()) ?? ("", "");
                Account = account;
                Password = password;
                RememberPassword = password.Length > 0;
            }
            _portText = GUI.TextField(new Rect(x + fw - 80, y + 20, 80, 26), _portText, 5, _field);
            y += 56;
            RememberPassword = GUI.Toggle(new Rect(x, y, fw, 22), RememberPassword, " Remember password on this PC", _toggle);
            y += 32;

            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if ((GUI.Button(new Rect(x, y, 170, 32), "Log in") || enter) && Account.Length > 0)
            {
                _port = int.TryParse(_portText, out int port) ? port : 2593;
                LoginPressed?.Invoke(this);
            }
            if (GUI.Button(new Rect(x + fw - 120, y, 120, 32), "Quit"))
                QuitPressed?.Invoke();
            if (!string.IsNullOrEmpty(Status))
                GUI.Label(new Rect(p.x, p.yMax + 10, p.width, 40), Status, _small);
            // Start in the account box once; after that the player clicks where they like.
            if (!_focusedOnce && Event.current.type == EventType.Repaint)
            {
                _focusedOnce = true;
                if (Account.Length == 0)
                    GUI.FocusControl("login-account");
            }
        }

        private void DrawOrnamentLogin(float w, float h)
        {
            EnsureStyles();
            float logoAspect = _logo.width / (float)_logo.height;
            float accountAspect = _accountPanel.width / (float)_accountPanel.height;
            float passwordAspect = _passwordPanel.width / (float)_passwordPanel.height;
            float loginAspect = _loginButton.width / (float)_loginButton.height;
            var layout = LoginMediaRules.LayoutOrnament(w, h, logoAspect, accountAspect, passwordAspect, loginAspect);
            if (!layout.Valid)
            {
                // Tiny window: keep the video, use the plain panel form for this frame only.
                DrawPlainLogin(w, h);
                return;
            }

            GUI.color = Color.white;
            GUI.DrawTexture(ToRect(layout.Logo), _logo, ScaleMode.StretchToFill);
            GUI.DrawTexture(ToRect(layout.Account), _accountPanel, ScaleMode.StretchToFill);
            GUI.DrawTexture(ToRect(layout.Password), _passwordPanel, ScaleMode.StretchToFill);

            // Baked labels stay in the art — typed text + caret + hit target share AccountInput/PasswordInput (recessed fill).
            // Font tracks the slot height so 1280x720 / 4:3 stay legible.
            int fieldFont = Mathf.Clamp(Mathf.RoundToInt(layout.AccountInput.H * 0.55f), 11, 22);
            _ornamentField.fontSize = fieldFont;
            _ornamentField.padding = new RectOffset(Mathf.Max(4, fieldFont / 3), Mathf.Max(4, fieldFont / 3), 2, 2);
            GUI.SetNextControlName("login-account");
            Account = GUI.TextField(ToRect(layout.AccountInput), Account, 30, _ornamentField);
            GUI.SetNextControlName("login-password");
            Password = GUI.PasswordField(ToRect(layout.PasswordInput), Password, '*', 30, _ornamentField);

            bool loginClicked = OrnamentButton(ToRect(layout.Login), _loginButton);
            bool enter = Event.current.type == EventType.KeyDown &&
                         (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if ((loginClicked || enter) && Account.Length > 0)
            {
                _port = int.TryParse(_portText, out int port) ? port : 2593;
                LoginPressed?.Invoke(this);
            }

            // Compact server / remember / quit in the bottom-margin reserve under the Login art.
            float secondaryY = layout.Login.YMax + 4f;
            float secondaryW = layout.Account.W;
            float sx = layout.Account.X;
            float rowH = 22f;
            GUI.Label(new Rect(sx, secondaryY, 48, rowH), "Server", _small);
            string typedHost = GUI.TextField(new Rect(sx + 50, secondaryY, secondaryW - 136, rowH), Host, 64, _field);
            if (typedHost != Host)
            {
                Host = typedHost;
                (string account, string password) = RememberedFor?.Invoke(Host.Trim()) ?? ("", "");
                Account = account;
                Password = password;
                RememberPassword = password.Length > 0;
            }
            _portText = GUI.TextField(new Rect(sx + secondaryW - 78, secondaryY, 78, rowH), _portText, 5, _field);
            RememberPassword = GUI.Toggle(new Rect(sx, secondaryY + 24, secondaryW - 120, rowH), RememberPassword, " Remember password", _toggle);
            if (GUI.Button(new Rect(sx + secondaryW - 110, secondaryY + 22, 110, 24), "Quit"))
                QuitPressed?.Invoke();

            if (!string.IsNullOrEmpty(Status))
                GUI.Label(new Rect(sx, Mathf.Min(h - 28f, secondaryY + 48f), secondaryW, 28), Status, _small);

            if (!_focusedOnce && Event.current.type == EventType.Repaint)
            {
                _focusedOnce = true;
                if (Account.Length == 0)
                    GUI.FocusControl("login-account");
            }
        }

        private static Rect ToRect(LoginRect r) => new Rect(r.X, r.Y, r.W, r.H);

        private static bool OrnamentButton(Rect r, Texture2D tex)
        {
            if (Event.current.type == EventType.Repaint && tex != null)
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private void DrawConnecting(float w, float h)
        {
            Rect p = Panel(w, h, 420, 130);
            GUI.Label(new Rect(p.x + 20, p.y + 30, p.width - 40, 40), Status, _label);
            if (GUI.Button(new Rect(p.x + p.width / 2 - 60, p.y + 80, 120, 30), "Cancel"))
            {
                _session?.Dispose();
                Failed("Cancelled.");
            }
        }

        private void DrawServers(float w, float h)
        {
            Rect p = Panel(w, h, 420, 90 + _session.Servers.Count * 36);
            GUI.Label(new Rect(p.x, p.y + 20, p.width, 24), "Choose a shard", _label);
            for (int i = 0; i < _session.Servers.Count; i++)
                if (GUI.Button(new Rect(p.x + 40, p.y + 54 + i * 36, p.width - 80, 30), _session.Servers[i].Name))
                {
                    Current = Page.Connecting;
                    _session.SelectServer(_session.Servers[i].Index);
                }
        }

        private void DrawCharacters(float w, float h)
        {
            int slots = Mathf.Max(1, _session.Characters.Count);
            Rect p = Panel(w, h, 440, 150 + slots * 38);
            GUI.Label(new Rect(p.x, p.y + 20, p.width, 24), "Characters", _label);
            bool hasFree = false;
            for (int i = 0; i < _session.Characters.Count; i++)
            {
                string name = _session.Characters[i];
                var row = new Rect(p.x + 40, p.y + 56 + i * 38, p.width - 140, 32);
                if (name.Length == 0)
                {
                    hasFree = true;
                    GUI.Label(row, "- empty -", _small);
                    continue;
                }
                if (GUI.Button(row, name))
                {
                    Current = Page.Connecting;
                    _session.PlayCharacter(i);
                }
                // Delete takes two clicks within three seconds.
                bool armed = _deleteArmed == i && Time.realtimeSinceStartup - _deleteArmedAt < 3f;
                if (GUI.Button(new Rect(row.xMax + 8, row.y, 82, 32), armed ? "Sure?" : "Delete"))
                {
                    if (armed)
                    {
                        _session.DeleteCharacter(i);
                        _deleteArmed = -1;
                    }
                    else
                    {
                        _deleteArmed = i;
                        _deleteArmedAt = Time.realtimeSinceStartup;
                    }
                }
            }
            float by = p.y + 66 + slots * 38;
            GUI.enabled = hasFree && _session.Cities.Count > 0;
            if (GUI.Button(new Rect(p.x + 40, by, 170, 32), "New character"))
            {
                _new = new NewCharacter { City = _session.Cities.Count > 0 ? _session.Cities[0].Index : 0 };
                Current = Page.Create;
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(p.xMax - 160, by, 120, 32), "Log out"))
            {
                _session.Dispose();
                Failed("");
            }
            if (!string.IsNullOrEmpty(Status) && Status != "Choose a character")
                GUI.Label(new Rect(p.x, p.yMax + 10, p.width, 40), Status, _small);
        }

        // ---- character creation ----

        private static readonly ushort[] SkinHues = { 0x03EA, 0x03F2, 0x03FA, 0x0402, 0x040A, 0x0412, 0x041A, 0x0422 };
        private static readonly ushort[] HairHues = { 0x044E, 0x0451, 0x0455, 0x045A, 0x045E, 0x0466, 0x046B, 0x0471, 0x0476, 0x047C };
        private static readonly ushort[] ClothHues = { 0x0515, 0x01BB, 0x0026, 0x0058, 0x0090, 0x00B3, 0x0213, 0x0386, 0x0455, 0x0001 };
        private static readonly (ushort Id, string Name)[] HairStyles =
        {
            (0x203B, "Short"), (0x203C, "Long"), (0x203D, "Ponytail"), (0x2044, "Mohawk"), (0x2045, "Pageboy"),
            (0x2047, "Afro"), (0x2048, "Receding"), (0x2049, "Two tails"), (0x204A, "Topknot"), (0, "Bald")
        };
        private static readonly (ushort Id, string Name)[] Beards =
        {
            (0, "None"), (0x203E, "Long"), (0x203F, "Short"), (0x2040, "Goatee"), (0x2041, "Mustache"),
            (0x204B, "Short + mustache"), (0x204C, "Long + mustache"), (0x204D, "Vandyke")
        };

        private void DrawCreate(float w, float h)
        {
            Rect p = Panel(w, h, 760, 520);
            NewCharacter c = _new;
            float x = p.x + 30, y = p.y + 24;

            GUI.Label(new Rect(x, y, 80, 22), "Name", _label);
            c.Name = GUI.TextField(new Rect(x + 60, y, 150, 26), c.Name, 16, _field);
            c.Female = GUI.Toggle(new Rect(x + 220, y + 2, 80, 22), c.Female, " Female", _toggle);
            y += 40;

            // Stats: each 10..60, total 90.
            int Stat(string label, int v)
            {
                GUI.Label(new Rect(x, y, 120, 22), $"{label} {v}", _label);
                v = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 125, y + 6, 165, 20), v, 10, 60));
                y += 28;
                return v;
            }
            c.Str = Stat("Strength", c.Str);
            c.Dex = Stat("Dexterity", c.Dex);
            c.Int = Stat("Intelligence", c.Int);
            int statTotal = c.Str + c.Dex + c.Int;
            GUI.Label(new Rect(x, y, 300, 20), $"Stat total {statTotal} / 90", statTotal == 90 ? _small : Warn());
            y += 30;

            // Skills: four, each 0..50, total 120.
            GUI.Label(new Rect(x, y, 300, 22), "Skills", _label);
            y += 24;
            for (int i = 0; i < 4; i++)
            {
                var (id, value) = c.Skills[i];
                string name = id < GameSession.SkillNames.Length ? GameSession.SkillNames[id] : "?";
                if (GUI.Button(new Rect(x, y, 170, 24), name))
                    _pickingSkill = _pickingSkill == i ? -1 : i;
                value = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 180, y + 6, 80, 20), value, 0, 50));
                GUI.Label(new Rect(x + 266, y + 2, 40, 20), value.ToString(), _small);
                c.Skills[i] = (id, value);
                y += 28;
            }
            int skillTotal = c.Skills.Sum(sk => sk.Value);
            bool distinct = c.Skills.Select(sk => sk.Id).Distinct().Count() == 4;
            GUI.Label(new Rect(x, y, 320, 20), $"Skill total {skillTotal} / 120{(distinct ? "" : " Â· pick four different skills")}", skillTotal == 120 && distinct ? _small : Warn());
            y += 30;

            // City.
            int cityPos = Mathf.Max(0, _session.Cities.FindIndex(ct => ct.Index == c.City));
            if (_session.Cities.Count > 0)
            {
                GUI.Label(new Rect(x, y, 60, 22), "City", _label);
                if (GUI.Button(new Rect(x + 60, y, 230, 26), _session.Cities[cityPos].Name))
                    c.City = _session.Cities[(cityPos + 1) % _session.Cities.Count].Index;
            }

            // Middle column: skill picker or looks.
            float mx = p.x + 340;
            if (_pickingSkill >= 0)
            {
                GUI.Label(new Rect(mx, p.y + 24, 200, 22), "Choose a skill", _label);
                var view = new Rect(mx, p.y + 50, 200, p.height - 80);
                _skillScroll = GUI.BeginScrollView(view, _skillScroll, new Rect(0, 0, 180, GameSession.SkillNames.Length * 24));
                for (int sk = 0; sk < GameSession.SkillNames.Length; sk++)
                    if (GUI.Button(new Rect(0, sk * 24, 180, 22), GameSession.SkillNames[sk]))
                    {
                        c.Skills[_pickingSkill] = (sk, c.Skills[_pickingSkill].Value);
                        _pickingSkill = -1;
                    }
                GUI.EndScrollView();
            }
            else
            {
                float ly = p.y + 24;
                void Cycle(string label, string value, Action next)
                {
                    GUI.Label(new Rect(mx, ly, 70, 22), label, _label);
                    if (GUI.Button(new Rect(mx + 70, ly, 130, 24), value))
                        next();
                    ly += 32;
                }
                Cycle("Skin", $"Tone {Array.IndexOf(SkinHues, c.SkinHue) + 1}", () => c.SkinHue = Next(SkinHues, c.SkinHue));
                Cycle("Hair", HairStyles.FirstOrDefault(hs => hs.Id == c.HairStyle).Name ?? "Short", () => c.HairStyle = Next(HairStyles.Select(hs => hs.Id).ToArray(), c.HairStyle));
                Cycle("Hair hue", $"Shade {Array.IndexOf(HairHues, c.HairHue) + 1}", () => c.HairHue = Next(HairHues, c.HairHue));
                if (!c.Female)
                {
                    Cycle("Beard", Beards.FirstOrDefault(b => b.Id == c.BeardStyle).Name ?? "None", () => c.BeardStyle = Next(Beards.Select(b => b.Id).ToArray(), c.BeardStyle));
                    c.BeardHue = c.HairHue;
                }
                Cycle("Shirt", $"Dye {Array.IndexOf(ClothHues, c.ShirtHue) + 1}", () => c.ShirtHue = Next(ClothHues, c.ShirtHue));
                Cycle("Pants", $"Dye {Array.IndexOf(ClothHues, c.PantsHue) + 1}", () => c.PantsHue = Next(ClothHues, c.PantsHue));
            }

            // Right column: the paperdoll preview.
            DrawPreview(new Vector2(p.xMax - 230, p.y + 30), c);

            bool valid = c.Name.Trim().Length >= 2 && statTotal == 90 && skillTotal == 120 && distinct;
            GUI.enabled = valid;
            if (GUI.Button(new Rect(p.x + 30, p.yMax - 52, 160, 32), "Create"))
            {
                c.Name = c.Name.Trim();
                Current = Page.Connecting;
                _session.CreateCharacter(c);
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(p.xMax - 150, p.yMax - 52, 120, 32), "Back"))
                Current = Page.Characters;
        }

        private static ushort Next(ushort[] options, ushort current)
        {
            int i = Array.IndexOf(options, current);
            return options[(i + 1) % options.Length];
        }

        private void DrawPreview(Vector2 at, NewCharacter c)
        {
            UiImage body = _art.Gump(c.Female ? 0x000D : 0x000C, c.SkinHue);
            if (body == null)
                return;
            void Layer(int itemGraphic, int hue)
            {
                if (itemGraphic == 0)
                    return;
                StaticTileData td = _art.Data.TileData.GetStatic(itemGraphic);
                if (td.AnimId == 0)
                    return;
                UiImage img = (c.Female ? _art.Gump(td.AnimId + 60000, hue) : null) ?? _art.Gump(td.AnimId + 50000, hue);
                if (img != null)
                    GUI.DrawTexture(new Rect(at.x, at.y, img.Width, img.Height), img.Texture);
            }
            GUI.DrawTexture(new Rect(at.x, at.y, body.Width, body.Height), body.Texture);
            Layer(0x152E, c.PantsHue);                       // long pants
            Layer(c.Female ? 0x1F01 : 0x1517, c.ShirtHue);   // plain dress / shirt
            Layer(0x170F, 0);                                // shoes
            Layer(c.HairStyle, c.HairHue);
            if (!c.Female)
                Layer(c.BeardStyle, c.BeardHue);
        }

        // ---- drawing helpers ----

        private GUIStyle Warn()
        {
            var st = new GUIStyle(_small);
            st.normal.textColor = new Color(1f, 0.55f, 0.4f);
            return st;
        }

        private void ResizePic(int id, Rect r)
        {
            UiImage[] p = new UiImage[9];
            for (int i = 0; i < 9; i++)
                p[i] = _art.Gump(id + i);
            if (p[0] == null || p[8] == null)
            {
                GUI.color = new Color(0.12f, 0.1f, 0.08f, 0.95f);
                GUI.DrawTexture(r, _shade);
                GUI.color = Color.white;
                return;
            }
            float l = p[0].Width, t = p[0].Height, rr = p[8].Width, b = p[8].Height;
            Tile(p[4], new Rect(r.x + l, r.y + t, r.width - l - rr, r.height - t - b));
            Tile(p[1], new Rect(r.x + l, r.y, r.width - l - rr, t));
            Tile(p[7], new Rect(r.x + l, r.yMax - b, r.width - l - rr, b));
            Tile(p[3], new Rect(r.x, r.y + t, l, r.height - t - b));
            Tile(p[5], new Rect(r.xMax - rr, r.y + t, rr, r.height - t - b));
            GUI.DrawTexture(new Rect(r.x, r.y, l, t), p[0].Texture);
            if (p[2] != null) GUI.DrawTexture(new Rect(r.xMax - p[2].Width, r.y, p[2].Width, p[2].Height), p[2].Texture);
            if (p[6] != null) GUI.DrawTexture(new Rect(r.x, r.yMax - p[6].Height, p[6].Width, p[6].Height), p[6].Texture);
            GUI.DrawTexture(new Rect(r.xMax - rr, r.yMax - b, rr, b), p[8].Texture);
        }

        private static void Tile(UiImage img, Rect r)
        {
            if (img == null || r.width <= 0 || r.height <= 0)
                return;
            GUI.BeginGroup(r);
            for (float ty = 0; ty < r.height; ty += img.Height)
                for (float tx = 0; tx < r.width; tx += img.Width)
                    GUI.DrawTexture(new Rect(tx, ty, img.Width, img.Height), img.Texture);
            GUI.EndGroup();
        }

        private void EnsureStyles()
        {
            if (_title != null)
                return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = new Color(1f, 0.88f, 0.6f);
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            _label.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
            _label.onNormal.textColor = _label.normal.textColor;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperCenter };
            _small.normal.textColor = new Color(0.85f, 0.8f, 0.68f);
            _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 15 };
            _toggle.normal.textColor = _toggle.onNormal.textColor = _toggle.hover.textColor = _toggle.onHover.textColor = new Color(0.95f, 0.92f, 0.84f);
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 15, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 3, 3) };
            _ornamentField = new GUIStyle(_field)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 4, 4),
                // Skin textField borders 9-slice a 1×1 fill and visually shift the chrome off the art slot.
                border = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0),
            };
            var ink = new Color(0.95f, 0.92f, 0.84f, 1f);
            _ornamentField.normal.textColor = ink;
            _ornamentField.active.textColor = ink;
            _ornamentField.focused.textColor = ink;
            _ornamentField.hover.textColor = ink;
            // Flat near-black chrome so the recessed gold slot stays the visible frame.
            var dark = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            dark.SetPixel(0, 0, new Color(0.02f, 0.02f, 0.03f, 0.92f));
            dark.Apply();
            _ornamentField.normal.background = dark;
            _ornamentField.active.background = dark;
            _ornamentField.focused.background = dark;
            _ornamentField.hover.background = dark;
        }
    }
}
