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

        /// <summary>The player confirmed "Log out?"; raised just before the logout request is sent.</summary>
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
            s.World.ContainerOpened += OnContainerOpened;
            s.World.GumpOpened += OnGumpOpened;
            s.LiftRejected += OnLiftRejected;

            Ui.Command = RunCommand;
            ChatLog = new UI.ChatLog();
            s.World.JournalAdded += ChatLog.Add;
            Ui.Open(new UI.TopBarWindow());
            _ctx.Hud.TopInset = UI.TopBarWindow.Height + 4;
            Hotkeys = new Hotkeys(GameContext.ProjectRoot) { Log = m => _ctx.Hud.Log(null, m), Command = RunCommand };
            s.TargetRequested += OnTargetRequested;
            s.TargetEnded += OnTargetEnded;
            s.WarModeChanged += OnWarModeChanged;
            s.Damaged += OnDamaged;
            s.World.JournalAdded += OnJournalOverhead;
            s.PlayerDied += OnPlayerDied;
            _weatherLogged = WeatherType.None;
            s.WeatherChanged += OnWeatherChanged;

            s.ContextMenuOpened += OnContextMenuOpened;
            s.ShopOpened += OnShopOpened;
            s.ShopClosed += OnShopClosed;
            s.TradeOpened += OnTradeOpened;
            s.TradeClosed += OnTradeClosed;
            s.PromptRequested += OnPromptRequested;
            s.TextEntryRequested += OnTextEntryRequested;
            s.SpellbookContent += OnSpellbookContent;
            s.ProfileReceived += OnProfileReceived;
            s.MapDisplayed += OnMapDisplayed;
            s.EffectStarted += _effects.Start;

            if (_ctx.Camera.GetComponent<AudioListener>() == null && UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null)
                _ctx.Camera.gameObject.AddComponent<AudioListener>();
            if (Sound == null)
            {
                Sound = _game.gameObject.AddComponent<Audio>();
                Sound.Init(_ctx.Data);
            }
            s.SoundPlayed += OnSoundPlayed;
            s.MusicRequested += OnMusicRequested;
        }

        /// <summary>Unsubscribes everything <see cref="Attach"/> subscribed. The windows, chat log, macros and audio stay as they are.</summary>
        public void Dispose()
        {
            GameSession s = _session;
            if (s == null)
                return;
            _session = null;
            s.World.PaperdollOpened -= OnPaperdollOpened;
            s.World.ContainerOpened -= OnContainerOpened;
            s.World.GumpOpened -= OnGumpOpened;
            s.LiftRejected -= OnLiftRejected;
            s.World.JournalAdded -= ChatLog.Add;
            s.TargetRequested -= OnTargetRequested;
            s.TargetEnded -= OnTargetEnded;
            s.WarModeChanged -= OnWarModeChanged;
            s.Damaged -= OnDamaged;
            s.World.JournalAdded -= OnJournalOverhead;
            s.PlayerDied -= OnPlayerDied;
            s.WeatherChanged -= OnWeatherChanged;
            s.ContextMenuOpened -= OnContextMenuOpened;
            s.ShopOpened -= OnShopOpened;
            s.ShopClosed -= OnShopClosed;
            s.TradeOpened -= OnTradeOpened;
            s.TradeClosed -= OnTradeClosed;
            s.PromptRequested -= OnPromptRequested;
            s.TextEntryRequested -= OnTextEntryRequested;
            s.SpellbookContent -= OnSpellbookContent;
            s.ProfileReceived -= OnProfileReceived;
            s.MapDisplayed -= OnMapDisplayed;
            s.EffectStarted -= _effects.Start;
            s.SoundPlayed -= OnSoundPlayed;
            s.MusicRequested -= OnMusicRequested;
        }

        private void OnPaperdollOpened(uint serial, string title)
        {
            UI.PaperdollWindow w = Ui.Open(new UI.PaperdollWindow(serial, title));
            w.SetTitle(title);
        }

        private void OnContainerOpened(OpenContainer c) => Ui.Open(new UI.ContainerWindow(c.Serial, c.Gump));

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

        private void OnTargetRequested(TargetRequest _) => _cursor.Update(_ctx.Session, Ui);

        private void OnTargetEnded() => _cursor.Update(_ctx.Session, Ui);

        private void OnWarModeChanged(bool war) => _ctx.Hud.WarMode = war;

        private void OnDamaged(uint serial, int amount) => _overheads.Add(serial, amount.ToString(), new Color(1f, 0.25f, 0.2f), 18, 1.6f, true);

        private void OnJournalOverhead(JournalEntry j)
        {
            // Speech and labels appear over whoever said them (system messages only in the journal).
            // Said, emoted, whispered, yelled, cast or a click label: overhead. Guild, alliance, broadcast: journal only.
            bool local = j.Type == 0 || j.Type == 2 || (j.Type >= 6 && j.Type <= 10);
            if (local && j.Serial != 0xFFFFFFFF && j.Serial != 0)
                _overheads.Add(j.Serial, j.Text, _gumpArt.HueColor(j.Hue, new Color(0.95f, 0.95f, 0.9f)), 14, 3f + j.Text.Length * 0.05f, false);
        }

        private void OnPlayerDied() => _ctx.Hud.Log(null, "You are dead.");

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
            Ui.Open(shop);
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

        private void OnPromptRequested(TextPrompt prompt)
        {
            // A new prompt replaces an open one (the server cancelled it): a fresh window, so stale text isn't sent.
            if (Ui.Find("prompt") is UI.PromptWindow old)
                Ui.Close(old);
            Ui.Open(new UI.PromptWindow(prompt));
        }

        private void OnTextEntryRequested(TextEntryRequest e) => Ui.Open(new UI.TextEntryWindow(e));

        private void OnSpellbookContent(uint book, int first, ulong bits) => Ui.Open(new UI.SpellbookWindow(book, first, bits));

        private void OnProfileReceived(CharacterProfile p) => Ui.Open(new UI.ProfileWindow(p)).SetProfile(p);

        private void OnMapDisplayed(MapItemDetails map) => Ui.Open(new UI.MapItemWindow(map));

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
                    Ui.Open(new UI.ConfirmWindow("Log out?", "Quit the game and return to the desktop?", () =>
                    {
                        LogoutConfirmed?.Invoke();
                        _ctx.Session.Logout();
                    }));
                    break;
                case "journal": _ctx.Hud.JournalVisible = !_ctx.Hud.JournalVisible; break;
                case "skills": Ui.Open(new UI.SkillsWindow()); break;
                case "chat": _chatLine.Open(); break;
                case "war": _ctx.Session.SetWarMode(!_ctx.Session.WarMode); break;
                case "status": Ui.Open(new UI.StatusWindow()); break;
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
    }
}
