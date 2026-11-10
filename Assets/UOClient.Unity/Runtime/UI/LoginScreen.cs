using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The screens before the world: account login, shard choice (when there is more than one), character select (play,
    /// delete), and character creation (LoginScreen.Create.cs), all but the login form in the modern paperdoll's look.
    /// </summary>
    public sealed partial class LoginScreen
    {
        /// <summary>The page shown; Create, Profession, Skills and City are character creation's steps in order.</summary>
        public enum Page { Login, Connecting, Servers, Characters, Create, Profession, Skills, City }

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
            if (OptionsPressed != null && Chip(new Rect(w - 116, 16, 100, 30), "Options", false, 16))
                OptionsPressed();

            switch (Current)
            {
                case Page.Login: DrawLogin(w, h); break;
                case Page.Connecting: DrawConnecting(w, h); break;
                case Page.Servers: DrawServers(w, h); break;
                case Page.Characters: DrawCharacters(w, h); break;
                case Page.Create: DrawCreate(w, h); break;
                case Page.Profession: DrawProfession(w, h); break;
                case Page.Skills: DrawSkills(w, h); break;
                case Page.City: DrawCity(w, h); break;
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
            rowH = 26f;
            // A dark plate under the row, so it reads over the bright video.
            var plate = new Rect(sx - 10f, secondaryY - 6f, secondaryW + 20f, rowH * 2f + 18f);
            Fill(plate, new Color(0.02f, 0.018f, 0.016f, 0.78f));
            Outline(plate, PaperdollSkin.GoldDim, 1f);
            PaperdollSkin.Title(new Rect(sx, secondaryY, 56, rowH), "Server", 15, PaperdollSkin.Gold, true, TextAnchor.MiddleLeft);
            string typedHost = Field(new Rect(sx + 58, secondaryY, secondaryW - 144, rowH), Host, 64, "login-host", 15);
            if (typedHost != Host)
            {
                Host = typedHost;
                (string account, string password) = RememberedFor?.Invoke(Host.Trim()) ?? ("", "");
                Account = account;
                Password = password;
                RememberPassword = password.Length > 0;
            }
            _portText = Field(new Rect(sx + secondaryW - 78, secondaryY, 78, rowH), _portText, 5, "login-port", 15);
            if (Chip(new Rect(sx, secondaryY + 32, 200, rowH), RememberPassword ? "✓  Remember password" : "Remember password", RememberPassword, 14))
                RememberPassword = !RememberPassword;
            if (Chip(new Rect(sx + secondaryW - 110, secondaryY + 32, 110, rowH), "Quit", false, 15))
                QuitPressed?.Invoke();

            if (!string.IsNullOrEmpty(Status))
                Note(new Rect(sx, Mathf.Min(h - 28f, secondaryY + 62f), secondaryW, 28), Status);

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
            Rect p = StyledPanel(w, h, 460, 210, null);
            PaperdollSkin.Title(new Rect(p.x + 30, p.y + 40, p.width - 60, 60), Status, 20, PaperdollSkin.Gold);
            if (Scroll(new Rect(p.center.x - 80, p.yMax - 76, 160, 40), "Cancel"))
            {
                _session?.Dispose();
                Failed("Cancelled.");
            }
        }

        private void DrawServers(float w, float h)
        {
            Rect p = StyledPanel(w, h, 480, 150 + _session.Servers.Count * 50, "Choose a Shard");
            for (int i = 0; i < _session.Servers.Count; i++)
                if (Scroll(new Rect(p.x + 60, p.y + 84 + i * 50, p.width - 120, 42), _session.Servers[i].Name))
                {
                    Current = Page.Connecting;
                    _session.SelectServer(_session.Servers[i].Index);
                }
        }

        /// <summary>The account's characters, each played with a click; an empty slot starts a new one.</summary>
        private void DrawCharacters(float w, float h)
        {
            int slots = Mathf.Max(1, _session.Characters.Count);
            Rect p = StyledPanel(w, h, 560, 196 + slots * 58, "Choose Your Character");
            bool canCreate = _session.Cities.Count > 0;
            bool hasFree = false;
            Vector2 mouse = Event.current.mousePosition;
            for (int i = 0; i < _session.Characters.Count; i++)
            {
                string name = _session.Characters[i];
                var row = new Rect(p.x + 50, p.y + 84 + i * 58, p.width - 100, 48);
                if (name.Length == 0)
                {
                    hasFree = true;
                    bool hotEmpty = canCreate && row.Contains(mouse);
                    Card(row, hotEmpty, false);
                    PaperdollSkin.Title(row, "Empty slot", 17, hotEmpty ? PaperdollSkin.GoldFaded : new Color(0.45f, 0.4f, 0.33f), false);
                    if (canCreate && GUI.Button(row, GUIContent.none, GUIStyle.none))
                        BeginCreate();
                    continue;
                }
                var delete = new Rect(row.xMax - 96, row.y + 10, 84, 28);
                bool hot = row.Contains(mouse) && !delete.Contains(mouse);
                Card(row, hot, false);
                PaperdollSkin.Title(new Rect(row.x + 20, row.y, row.width - 140, row.height), name, 22, hot ? Color.white : PaperdollSkin.Gold, true, TextAnchor.MiddleLeft);
                // Delete takes two clicks within three seconds.
                bool armed = _deleteArmed == i && Time.realtimeSinceStartup - _deleteArmedAt < 3f;
                if (Chip(delete, armed ? "Sure?" : "Delete", armed, 14))
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
                else if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    Current = Page.Connecting;
                    _session.PlayCharacter(i);
                }
            }
            float by = p.yMax - 72;
            if (Scroll(new Rect(p.x + 50, by, 200, 42), "New Character", hasFree && canCreate))
                BeginCreate();
            if (Scroll(new Rect(p.xMax - 210, by, 160, 42), "Log Out"))
            {
                _session.Dispose();
                Failed("");
            }
            if (!string.IsNullOrEmpty(Status) && Status != "Choose a character")
                Note(new Rect(p.x, p.yMax + 10, p.width, 30), Status);
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
