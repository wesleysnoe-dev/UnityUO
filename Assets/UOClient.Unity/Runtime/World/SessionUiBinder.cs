using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>
    /// Windows the server opens (paperdolls, containers, gumps, menus, shops, prompts, profiles, map items), the journal and log lines,
    /// the target cursor, damage and speech overheads, effects and sound for one session, plus the commands the
    /// windows' buttons send. Every session event it subscribes to is unsubscribed again by <see cref="Dispose"/>.
    /// </summary>
    public sealed class SessionUiBinder : IDisposable
    {
        private readonly GameContext _ctx;
        private readonly WorldDemo _game;
        private readonly UI.GumpArt _gumpArt;
        private readonly WorldInput _input;
        private readonly UI.TargetCursor _cursor;
        private readonly OverheadText _overheads;
        private readonly EffectsLayer _effects;
        private readonly ChatLine _chatLine;
        private GameSession _session;

        /// <summary><paramref name="sound"/> is the audio from an earlier session, if any: it is added to the game object only once.</summary>
        public SessionUiBinder(GameContext ctx, WorldDemo game, UI.GumpArt gumpArt, WorldInput input, UI.TargetCursor cursor,
                               OverheadText overheads, EffectsLayer effects, ChatLine chatLine, Audio sound)
        {
            _ctx = ctx;
            _game = game;
            _gumpArt = gumpArt;
            _input = input;
            _cursor = cursor;
            _overheads = overheads;
            _effects = effects;
            _chatLine = chatLine;
            Sound = sound;
        }

        public UI.UiManager Ui { get; private set; }
        public UI.ChatLog ChatLog { get; private set; }
        public Hotkeys Hotkeys { get; private set; }
        public Audio Sound { get; private set; }

        /// <summary>The player confirmed "Log out?"; raised just before the logout request is sent (the game returns to login on the drop).</summary>
        public event Action LogoutConfirmed;

        public void Attach(GameSession s)
        {
            _session = s;
            Ui = new UI.UiManager(_ctx.Session, _gumpArt, _ctx.Session.Cliloc)
            {
                // Released over the world: onto what is under the mouse, as in the classic client.
                DropOutside = (item, gui) => _input.DropInWorld(item, new Vector2(gui.x, Screen.height - gui.y))
            };
            s.World.PaperdollOpened += OnPaperdollOpened;
            s.Entered += OnEnteredWorld;
            s.World.ContainerOpened += OnContainerOpened;
            s.World.GumpOpened += OnGumpOpened;
            s.LiftRejected += OnLiftRejected;

            Ui.Command = RunCommand;
            ChatLog = new UI.ChatLog();
            s.World.JournalAdded += ChatLog.Add;
            SyncTopBar();
            // TazUO brings its buff gump back from the saved layout; we save none, so it opens with each session
            // (closed, the status window's buff button opens it again).
            Ui.Open(new UI.BuffBarWindow());
            SyncBars();
            Hotkeys = new Hotkeys(GameContext.ProjectRoot) { Log = m => _ctx.Hud.Log(null, m), Command = RunCommand };
            s.TargetRequested += OnTargetRequested;
            s.TargetEnded += OnTargetEnded;
            s.WarModeChanged += OnWarModeChanged;
            s.Damaged += OnDamaged;
            s.MobileAnimated += OnAnimatedForSparks;
            s.LastAttackSet += OnLastAttackSet;
            s.AttackQueryRequested += OnAttackQueryRequested;
            s.TargetQueryRequested += OnTargetQueryRequested;
            s.World.JournalAdded += OnJournalOverhead;
            s.World.JournalAdded += OnJournalCooldowns;
            s.PlayerDied += OnPlayerDied;
            s.PlayerDeadChanged += OnPlayerDeadChanged;
            _weatherLogged = WeatherType.None;
            s.WeatherChanged += OnWeatherChanged;

            s.ContextMenuOpened += OnContextMenuOpened;
            s.ShopOpened += OnShopOpened;
            s.ShopClosed += OnShopClosed;
            s.TradeOpened += OnTradeOpened;
            s.TradeClosed += OnTradeClosed;
            s.PartyInvited += OnPartyInvited;
            s.PromptRequested += OnPromptRequested;
            s.TextEntryRequested += OnTextEntryRequested;
            s.SpellbookContent += OnSpellbookContent;
            s.ProfileReceived += OnProfileReceived;
            s.BookOpened += OnBookOpened;
            s.BulletinBoardOpened += OnBulletinBoardOpened;
            s.BulletinPostOpened += OnBulletinPostOpened;
            s.MapDisplayed += OnMapDisplayed;
            s.HuePickerRequested += OnHuePickerRequested;
            s.MenuOpened += OnMenuOpened;
            s.OpenUrlRequested += OnOpenUrlRequested;
            s.EffectStarted += _effects.Start;
            s.HouseCustomizationChanged += OnHouseCustomizationChanged;

            if (_ctx.Camera.GetComponent<AudioListener>() == null && UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null)
                _ctx.Camera.gameObject.AddComponent<AudioListener>();
            if (Sound == null)
            {
                Sound = _game.gameObject.AddComponent<Audio>();
                Sound.Init(_ctx.Data);
            }
            s.SoundPlayed += OnSoundPlayed;
            s.MusicRequested += OnMusicRequested;
            Avatar.FootstepSound = (id, x, y) => OnSoundPlayed(id, x, y, 0); // the client's own footsteps
            if (UI.OptionsWindow.LaunchPage != null)
                Ui.Open(new UI.OptionsWindow(_game));
        }

        /// <summary>
        /// Unsubscribes everything <see cref="Attach"/> subscribed. The windows, chat log, macros and audio stay as they are
        /// (the game drops the binder, and with it the windows, when the session ends; the audio goes to the next binder).
        /// </summary>
        public void Dispose()
        {
            GameSession s = _session;
            if (s == null)
                return;
            _session = null;
            s.World.PaperdollOpened -= OnPaperdollOpened;
            s.Entered -= OnEnteredWorld;
            s.World.ContainerOpened -= OnContainerOpened;
            s.World.GumpOpened -= OnGumpOpened;
            s.LiftRejected -= OnLiftRejected;
            s.World.JournalAdded -= ChatLog.Add;
            s.TargetRequested -= OnTargetRequested;
            s.TargetEnded -= OnTargetEnded;
            s.WarModeChanged -= OnWarModeChanged;
            s.Damaged -= OnDamaged;
            s.MobileAnimated -= OnAnimatedForSparks;
            s.LastAttackSet -= OnLastAttackSet;
            s.AttackQueryRequested -= OnAttackQueryRequested;
            s.TargetQueryRequested -= OnTargetQueryRequested;
            s.World.JournalAdded -= OnJournalOverhead;
            s.World.JournalAdded -= OnJournalCooldowns;
            UI.CooldownBarsWindow.Timers.Clear();
            s.PlayerDied -= OnPlayerDied;
            s.PlayerDeadChanged -= OnPlayerDeadChanged;
            s.WeatherChanged -= OnWeatherChanged;
            s.ContextMenuOpened -= OnContextMenuOpened;
            s.ShopOpened -= OnShopOpened;
            s.ShopClosed -= OnShopClosed;
            s.TradeOpened -= OnTradeOpened;
            s.TradeClosed -= OnTradeClosed;
            s.PartyInvited -= OnPartyInvited;
            s.PromptRequested -= OnPromptRequested;
            s.TextEntryRequested -= OnTextEntryRequested;
            s.SpellbookContent -= OnSpellbookContent;
            s.ProfileReceived -= OnProfileReceived;
            s.BookOpened -= OnBookOpened;
            s.BulletinBoardOpened -= OnBulletinBoardOpened;
            s.BulletinPostOpened -= OnBulletinPostOpened;
            s.MapDisplayed -= OnMapDisplayed;
            s.HuePickerRequested -= OnHuePickerRequested;
            s.MenuOpened -= OnMenuOpened;
            s.OpenUrlRequested -= OnOpenUrlRequested;
            s.EffectStarted -= _effects.Start;
            s.HouseCustomizationChanged -= OnHouseCustomizationChanged;
            s.SoundPlayed -= OnSoundPlayed;
            s.MusicRequested -= OnMusicRequested;
            Avatar.FootstepSound = null;
        }

        private void OnPaperdollOpened(uint serial, string title)
        {
            UI.PaperdollWindow w = Ui.Open(new UI.PaperdollWindow(serial, title));
            w.SetTitle(title);
        }

        // A container already open (in either style) is brought forward as it is: both use the same window key.
        private void OnContainerOpened(OpenContainer c)
        {
            bool corpse = _ctx.Session != null && _ctx.Session.World.Items.TryGetValue(c.Serial, out WorldItem w) && w.IsCorpse;
            if (GameOptions.OpensGrid(UI.GridContainerWindow.DefaultToOldStyleView, c.Gump, corpse, CorpseContainerStyle))
                Ui.Open(new UI.GridContainerWindow(c.Serial, c.Gump));
            else
                Ui.Open(new UI.ContainerWindow(c.Serial, c.Gump));
        }

        private void OnGumpOpened(ServerGump g) => Ui.Open(new UI.ServerGumpWindow(g));

        private void OnLiftRejected(int reason)
        {
            Ui.CancelDrag();
            _ctx.Hud.Log(null, reason switch
            {
                1 => "That is too far away.",
                2 => "That is out of sight.",
                3 => "That item does not belong to you.",
                4 => "You are already holding an item.",
                _ => "You cannot pick that up."
            });
        }

        private void OnTargetRequested(TargetRequest _)
        {
            CloseLapsedTargetQuery();
            _cursor.Update(_ctx.Session, Ui);
        }

        private void OnTargetEnded()
        {
            CloseLapsedTargetQuery();
            _cursor.Update(_ctx.Session, Ui);
        }

        /// <summary>
        /// TargetManager.Target's criminal query: a question already up for another mobile goes (its answer no longer counts),
        /// and this one's answer goes back to the session, which alone sends the target, on yes.
        /// </summary>
        private void OnTargetQueryRequested(GameSession.TargetQuery q)
        {
            if (Ui.Find(UI.QuestionWindow.KeyFor("target")) is UI.UiWindow old)
                Ui.Close(old);
            GameSession s = _session;
            Ui.Open(new UI.QuestionWindow("target", UI.QuestionWindow.CriminalText, yes => s.AnswerTargetQuery(q, yes)));
        }

        /// <summary>The cursor a target question was holding went (answered, cancelled, replaced): its question goes too.</summary>
        private void CloseLapsedTargetQuery()
        {
            if (_session?.PendingTargetQuery == null && Ui.Find(UI.QuestionWindow.KeyFor("target")) is UI.UiWindow w)
                Ui.Close(w);
        }

        /// <summary>The options' corpse container style (GameOptions.CorpseStyle*).</summary>
        public static int CorpseContainerStyle;

        /// <summary>The options' combat music switch (TazUO's EnableCombatMusic).</summary>
        public static bool CombatMusic = true;

        private void OnWarModeChanged(bool war)
        {
            _ctx.Hud.WarMode = war;
            // One of the three combat tracks while in war mode; the region's music again in peace.
            Sound?.SetCombatMusic(war && CombatMusic, war && CombatMusic ? Sound.RandomCombatTrack() : 0);
        }

        /// <summary>TazUO's OpenHealthBarForLastAttack (on by default, hardcoded here; nothing is written to the settings).</summary>
        private void OnLastAttackSet(uint serial) => UI.HealthBarWindow.ShowLastAttack(Ui, serial);

        /// <summary>
        /// GameActions.Attack's criminal query: a question already up for another mobile goes (answering no, which sends
        /// nothing), and this one's answer goes back to the session, which alone sends the attack, on yes.
        /// </summary>
        private void OnAttackQueryRequested(uint serial)
        {
            if (Ui.Find(UI.QuestionWindow.KeyFor("attack")) is UI.UiWindow old)
                Ui.Close(old);
            GameSession s = _session;
            Ui.Open(new UI.QuestionWindow("attack", UI.QuestionWindow.CriminalText, yes => s.AnswerAttackQuery(serial, yes)));
        }

        private void OnDamaged(uint serial, int amount)
        {
            _overheads.Add(serial, amount.ToString(), new Color(1f, 0.25f, 0.2f), 18, 1.6f, true);
            Fx.HitSparks.Damaged(_ctx.Session, serial); // a weapon's hit throws sparks
        }

        private void OnAnimatedForSparks(Core.ServerAnimation a) => Fx.HitSparks.Animated(_ctx.Session, a);

        // Every journal line may start a cooldown bar (the options' Cooldowns).
        private void OnJournalCooldowns(JournalEntry j) =>
            UI.CooldownBarsWindow.Timers.OnText(j.Text, Time.realtimeSinceStartupAsDouble);

        private void OnJournalOverhead(JournalEntry j)
        {
            // Speech and labels appear over whoever said them (system messages only in the journal).
            // Said, emoted, whispered, yelled, cast or a click label: overhead. Guild, alliance, broadcast: journal only.
            bool local = j.Type == 0 || j.Type == 2 || (j.Type >= 6 && j.Type <= 10);
            if (local && j.Serial != 0xFFFFFFFF && j.Serial != 0)
                _overheads.Add(j.Serial, j.Text, _gumpArt.HueColor(j.Hue, new Color(0.95f, 0.95f, 0.9f)), 14, _overheads.SpeechSeconds(j.Text), false);
        }

        private void OnPlayerDied()
        {
            // ModernUO's Kill drops whatever the player was holding without telling the client.
            Ui.CancelDrag();
            _ctx.Hud.DeathScreenUntil = Time.unscaledTime + DemoHud.DeathScreenSeconds;
            _ctx.Hud.Log(null, "You are dead.");
        }

        private void OnPlayerDeadChanged(bool dead)
        {
            if (!dead)
            {
                _ctx.Hud.DeathScreenUntil = 0;
                _ctx.Hud.Log(null, "You have been resurrected.");
            }
        }

        // ModernUO resends the weather every 30 seconds while it lasts, so only a change of kind is logged.
        private WeatherType _weatherLogged = WeatherType.None;

        private void OnWeatherChanged(Weather w)
        {
            if (w.Type == _weatherLogged)
                return;
            _weatherLogged = w.Type;
            _ctx.Hud.Log(null, w.Type switch
            {
                WeatherType.Rain => "It begins to rain.",
                WeatherType.FierceStorm => "A fierce storm approaches.",
                WeatherType.Snow => "It begins to snow.",
                WeatherType.StormBrewing => "A storm is brewing.",
                _ => "The weather clears."
            });
        }

        private void OnContextMenuOpened(Core.ContextMenu menu)
        {
            // A newer menu replaces an open one: Open would keep the old window (same key), and with it the old
            // serial, so a click would choose an entry of the menu the server no longer has.
            CloseByKey("context");
            Vector2 m = Input.mousePosition;
            var at = new Vector2(m.x / Ui.Scale, (Screen.height - m.y) / Ui.Scale);
            Ui.Open(new UI.ContextMenuWindow(menu, at)).FitTo(at, Screen.width / Ui.Scale, Screen.height / Ui.Scale);
        }

        private void OnShopOpened(ShopList list)
        {
            // A fresh list from the same vendor (asked again, or after a trade) replaces the open one's stale stock and prices.
            var shop = new UI.ShopWindow(list);
            CloseByKey(shop.Key);
            if (list.Selling)
                Ui.Open(shop);
            else
                Ui.OpenWithIntro(shop, UI.WindowIntro.Scroll, UI.UiManager.ScrollGraphic); // buying unrolls
        }

        private void CloseByKey(string key)
        {
            if (Ui.Find(key) is UI.UiWindow open)
                Ui.Close(open);
        }

        private void OnShopClosed(uint vendor)
        {
            foreach (UI.UiWindow w in Ui.Windows.ToList())
                if (w is UI.ShopWindow s && s.Vendor == vendor)
                    Ui.Close(w);
        }

        private void OnTradeOpened(SecureTrade t)
        {
            // A trade re-displayed on the same container replaces the old window (its trade is no longer open, so closing it sends nothing).
            CloseByKey(UI.SecureTradeWindow.KeyFor(t));
            Ui.Open(new UI.SecureTradeWindow(t));
        }

        /// <summary>The server ended the trade (cancelled, or both accepted): the window goes without sending a cancel.</summary>
        private void OnTradeClosed(SecureTrade t) => CloseByKey(UI.SecureTradeWindow.KeyFor(t));

        private void OnPartyInvited(uint leader)
        {
            // A newer invitation replaces an older one's window (that one would answer the wrong leader).
            CloseByKey("partyinvite");
            Ui.Open(new UI.PartyInviteWindow(leader));
        }

        private void OnPromptRequested(TextPrompt prompt)
        {
            // A new prompt replaces an open one (the server cancelled it): a fresh window, so stale text isn't sent.
            if (Ui.Find("prompt") is UI.PromptWindow old)
                Ui.Close(old);
            Ui.Open(new UI.PromptWindow(prompt));
        }

        private void OnTextEntryRequested(TextEntryRequest e) => Ui.Open(new UI.TextEntryWindow(e));

        // Already open (the book used again, a scroll added): the open one takes the new content.
        private void OnSpellbookContent(uint book, int first, ulong bits) =>
            Ui.OpenWithIntro(new UI.SpellbookWindow(book, first, bits), UI.WindowIntro.Book, Ui.GraphicOf(book)).SetContent(first, bits);

        private void OnProfileReceived(CharacterProfile p) =>
            Ui.OpenWithIntro(new UI.ProfileWindow(p), UI.WindowIntro.Scroll, UI.UiManager.ScrollGraphic).SetProfile(p);

        /// <summary>A cover (0xD4/0x93): open the book, or refresh the open one (pages arrive into it through 0x66).</summary>
        private void OnBookOpened(BookInfo book) =>
            Ui.OpenWithIntro(new UI.BookWindow(book), UI.WindowIntro.Book, Ui.GraphicOf(book.Serial)).SetBook(book);

        /// <summary>A board (0x71 0): open it, or refresh the open one (its messages follow as 0x3C, then 0x71 1).</summary>
        private void OnBulletinBoardOpened(BulletinBoardInfo board) => Ui.Open(new UI.BulletinBoardWindow(board)).SetBoard(board);

        /// <summary>A message's body (0x71 2): open it to read.</summary>
        private void OnBulletinPostOpened(BulletinBoardInfo board, BulletinPost post) =>
            Ui.Open(new UI.BulletinPostWindow(board, post)).SetPost(post);

        /// <summary>
        /// Customize mode (0xBF 0x20): begin opens the exit window (a new begin for another house replaces it), end closes
        /// it (it also goes by itself once the session no longer customizes that house).
        /// </summary>
        private void OnHouseCustomizationChanged(uint house, bool begin)
        {
            if (Ui.Find(UI.HouseCustomizeWindow.WindowKey) is UI.HouseCustomizeWindow open && (!begin || open.House != house))
                Ui.Close(open);
            if (begin)
                Ui.Open(new UI.HouseCustomizeWindow(house));
        }

        private void OnMapDisplayed(MapItemDetails map) => Ui.Open(new UI.MapItemWindow(map));

        /// <summary>A dye tub's picker (0x95): a picker already up for the same tub goes (closing it sends nothing).</summary>
        private void OnHuePickerRequested(HuePickerRequest request)
        {
            CloseByKey(UI.HuePickerWindow.KeyFor(request.Serial));
            Ui.Open(new UI.HuePickerWindow(request));
        }

        /// <summary>A legacy menu (0x7C): one already open with the same serial goes without answering (that would answer this one).</summary>
        private void OnMenuOpened(LegacyMenu menu)
        {
            if (Ui.Find(UI.LegacyMenuWindow.KeyFor(menu.Serial)) is UI.LegacyMenuWindow old)
            {
                old.Forget();
                Ui.Close(old);
            }
            Ui.Open(new UI.LegacyMenuWindow(menu));
        }

        /// <summary>
        /// 0xA5: never opened unasked (TazUO launches it). Only an http/https link is offered, in a question showing it; a
        /// newer link replaces an unanswered one. Anything else is refused with a line in the log.
        /// </summary>
        private void OnOpenUrlRequested(string url)
        {
            CloseByKey(UI.OpenUrlWindow.WindowKey);
            if (GameSession.TryParseSafeUrl(url, out _))
                Ui.Open(new UI.OpenUrlWindow(url.Trim()));
            else
                _ctx.Hud.Log(null, "The server sent a link that was not opened (only http and https links are offered).");
        }

        private void OnSoundPlayed(int id, int x, int y, int z)
        {
            if (_ctx.Player != null)
                Sound.PlaySound(id, x - _ctx.Player.X, y - _ctx.Player.Y);
        }

        private void OnMusicRequested(int id) => Sound.PlayMusic(id);

        /// <summary>The paperdoll's buttons.</summary>
        public void RunCommand(string command)
        {
            switch (command)
            {
                case "help": _ctx.Session.RequestHelp(); break;
                case "options": Ui.Open(new UI.OptionsWindow(_game)); break;
                case "logout":
                    // The server saves us and closes; the game goes back to the login screen (Quit there leaves).
                    Ui.Open(new UI.ConfirmWindow("Log out?", "Return to the login screen?", () =>
                    {
                        if (_session == null)
                            return; // the session already ended
                        LogoutConfirmed?.Invoke();
                        _session.Logout();
                    }));
                    break;
                case "journal": _ctx.Hud.JournalVisible = !_ctx.Hud.JournalVisible; break;
                case "skills": Ui.Open(new UI.SkillsWindow()); break;
                case "chat": _chatLine.Open(); break;
                case "quests": _ctx.Session.RequestQuestMenu(); break;
                case "guild": _ctx.Session.RequestGuildMenu(); break;
                case "war": _ctx.Session.SetWarMode(!_ctx.Session.WarMode); break;
                case "status": Ui.Open(new UI.StatusWindow()); break;
                case "party":
                    if (Ui.Find("party") is UI.UiWindow partyWindow)
                        Ui.Close(partyWindow);
                    else
                        Ui.OpenWithIntro(new UI.PartyWindow(), UI.WindowIntro.Scroll, UI.UiManager.ScrollGraphic);
                    break;
                // The top bar's own buttons.
                case "minimap": _ctx.Hud.MinimapVisible = !_ctx.Hud.MinimapVisible; break;
                case "paperdoll": _ctx.Session.OpenPaperdoll(); break;
                case "backpack":
                    if (!_ctx.Session.OpenBackpack())
                        _ctx.Hud.Log(null, "You have no backpack.");
                    break;
                case "worldchat":
                    if (Ui.Find("worldchat") is UI.UiWindow chatWindow)
                        Ui.Close(chatWindow);
                    else
                        Ui.Open(new UI.WorldChatWindow(ChatLog, hue => _gumpArt.HueColor((int)hue, new Color(0.92f, 0.9f, 0.84f))));
                    break;
                case "worldmap":
                    if (Ui.Find("worldmap") is UI.UiWindow mapWindow)
                        Ui.Close(mapWindow);
                    else
                        Ui.Open(new UI.WorldMapWindow(_ctx.Data, Path.Combine(GameContext.ProjectRoot, "Library", "UOClient")));
                    break;
                case "groups": _ctx.Session.Say("[groups"); break; // the server's Group Finder, as the UO Smallz client's button does
            }
        }

        /// <summary>The modern paperdoll opens as the world does: it holds what the top bar did, and nothing else opens it.</summary>
        private void OnEnteredWorld()
        {
            if (UI.PaperdollWindow.Modern)
                _ctx.Session.OpenPaperdoll();
            // -open-worldmap[=zoom]: the world map open from the start (captures of it).
            string mapArg = System.Array.Find(System.Environment.GetCommandLineArgs(), a => a.StartsWith("-open-worldmap", System.StringComparison.OrdinalIgnoreCase));
            if (mapArg != null && Ui.Find("worldmap") == null)
            {
                var map = new UI.WorldMapWindow(_ctx.Data, Path.Combine(GameContext.ProjectRoot, "Library", "UOClient"));
                if (mapArg.Contains("=") && float.TryParse(mapArg.Substring(mapArg.IndexOf('=') + 1), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float zoom))
                    map.Zoom = zoom;
                Ui.Open(map);
            }
        }

        /// <summary>The top bar with the classic paperdoll; none with the modern one (its buttons are the paperdoll's).</summary>
        /// <summary>Opens or closes the counter bar and the info bar as the options say (they stay open across Apply).</summary>
        public void SyncBars()
        {
            if (Ui == null || _ctx?.Session == null)
                return;
            UI.UiWindow counters = Ui.Find(UI.CounterBarWindow.WindowKey), info = Ui.Find(UI.InfoBarWindow.WindowKey);
            bool showCounters = UI.CounterBarWindow.Enabled || UI.CounterBarWindow.SelfTest;
            bool showInfo = UI.InfoBarWindow.Enabled || UI.CounterBarWindow.SelfTest;
            // The cooldown bars' window is there whenever a cooldown is set up; it draws nothing while none runs.
            UI.UiWindow timers = Ui.Find(UI.CooldownBarsWindow.WindowKey);
            bool showTimers = UI.CooldownBarsWindow.Timers.Defs.Count > 0;
            if (showTimers && timers == null)
                Ui.Open(new UI.CooldownBarsWindow());
            else if (!showTimers && timers != null)
                Ui.Close(timers);
            if (showCounters && counters == null)
                Ui.Open(new UI.CounterBarWindow());
            else if (!showCounters && counters != null)
                Ui.Close(counters);
            if (showInfo && info == null)
                Ui.Open(new UI.InfoBarWindow());
            else if (!showInfo && info != null)
                Ui.Close(info);
        }

        public void SyncTopBar()
        {
            if (Ui == null || _ctx?.Session == null)
                return;
            UI.UiWindow bar = Ui.Find("topbar");
            if (UI.PaperdollWindow.Modern)
            {
                if (bar != null)
                    Ui.Close(bar);
                _ctx.Hud.TopInset = 0;
            }
            else
            {
                if (bar == null)
                    Ui.Open(new UI.TopBarWindow());
                _ctx.Hud.TopInset = UI.TopBarWindow.Height + 4;
            }
        }
    }
}
