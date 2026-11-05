using System;
using System.Collections.Generic;
using UOClient.IO.Formats;
using UOClient.Net;

namespace UOClient.Core
{
    public enum SessionState
    {
        Idle,
        LoggingIn,
        GameLogin,
        CharacterSelect,
        EnteringWorld,
        InWorld,
        Disconnected
    }

    public sealed class LoginSettings
    {
        public string Host = "127.0.0.1";
        public int Port = 2593;
        public string Account;
        public string Password;
        public ClientVersion Version = ClientVersion.Default;

        /// <summary>Character to play; created if the account has no character with this name.</summary>
        public string CharacterName = "Adventurer";

        /// <summary>Starting city for a new character (matched against the server's city names).</summary>
        public string StartCity = "Britain";

        /// <summary>
        /// Let the player choose: stop at the server list (when there is more than one) and at the character
        /// list, raising <see cref="GameSession.ServersListed"/> and <see cref="GameSession.CharactersListed"/>.
        /// Otherwise the first server and <see cref="CharacterName"/> are used.
        /// </summary>
        public bool Interactive;

        /// <summary>Tell UO Offline this client draws chat links ("[uoclient 1", as its own client does at login).</summary>
        public bool AnnounceChatLinks = true;
    }

    /// <summary>
    /// A connection to a ModernUO (or RunUO/ServUO-style) server for an unencrypted 7.x client:
    /// login server, relay, game server, character select or creation, then the world.
    /// Call <see cref="Update"/> every frame on the game thread; all <see cref="World"/> changes happen there.
    /// </summary>
    public sealed partial class GameSession : IDisposable
    {
        private const double PingInterval = 30;

        private readonly LoginSettings _settings;
        private readonly Dictionary<byte, Action<PacketReader>> _handlers = new Dictionary<byte, Action<PacketReader>>();
        private NetConnection _conn;
        private uint _authKey;
        private double _now, _lastPing;
        private byte _pingSeq;

        // Movement: each request carries a sequence number; the server answers each with 0x22 (ok) or 0x21 (denied).
        private int _moveSeq;
        private readonly Queue<int> _pendingSteps = new Queue<int>();
        private readonly HashSet<uint> _nameRequested = new HashSet<uint>();

        public GameSession(LoginSettings settings)
        {
            _settings = settings;
            World = new World();

            _handlers[0x82] = LoginDenied;
            _handlers[0x53] = PopupMessage;
            _handlers[0xA8] = ServerList;
            _handlers[0x8C] = Relay;
            _handlers[0xB9] = _ => { };
            _handlers[0xA9] = CharacterList;
            _handlers[0x86] = CharacterListUpdate;
            _handlers[0x85] = DeleteRefused;
            _handlers[0xBD] = _ => SendClientVersion();
            _handlers[0x1B] = LoginConfirm;
            _handlers[0x55] = LoginComplete;
            _handlers[0x20] = PlayerUpdate;
            _handlers[0x78] = MobileIncoming;
            _handlers[0x77] = MobileMoving;
            _handlers[0x1D] = r => World.Remove(r.ReadUInt32());
            _handlers[0xF3] = WorldEntity;
            _handlers[0x1A] = WorldItemOld;
            _handlers[0x2E] = EquipUpdate;
            _handlers[0x11] = MobileStatus;
            _handlers[0x22] = MoveAck;
            _handlers[0x21] = MoveDenied;
            _handlers[0x4F] = r => World.SetLight(r.ReadByte());
            _handlers[0x4E] = r => { if (r.ReadUInt32() == World.PlayerSerial) World.PersonalLight = r.ReadByte(); };
            _handlers[0xAE] = UnicodeSpeech;
            _handlers[0x1C] = AsciiSpeech;
            _handlers[0xC1] = ClilocMessage;
            _handlers[0xCC] = ClilocMessageAffix;
            _handlers[0xBF] = Extended;
            _handlers[0x73] = _ => { };
            _handlers[0x97] = r => { }; // move player (forced step); position follows in 0x20
            _handlers[0x98] = MobileName;
            _handlers[0x76] = ServerChange;
            RegisterInteractionHandlers();
            RegisterCombatHandlers();
            RegisterTradeHandlers();
            RegisterTooltipHandlers();
            RegisterHousingHandlers();
            RegisterTrackingHandlers();
            RegisterProfileHandlers();
        }

        public World World { get; }
        public SessionState State { get; private set; }

        /// <summary>Milliseconds per <see cref="Update"/> spent handling packets (including what the world's event handlers do).</summary>
        public double HandleBudgetMs { get; set; } = 4;
        public string Status { get; private set; } = "Not connected";

        private readonly byte[][] _recent = new byte[256][];
        private int _recentNext;

        /// <summary>The last 256 packets handled, oldest first (for diagnosing what the server sent).</summary>
        public IEnumerable<byte[]> RecentPackets
        {
            get
            {
                for (int i = 0; i < _recent.Length; i++)
                    if (_recent[(_recentNext + i) % _recent.Length] is byte[] p)
                        yield return p;
            }
        }

        /// <summary>Packets whose handling (with the world's event handlers) took over 5 ms; for diagnosing hitches.</summary>
        public readonly List<string> SlowPackets = new List<string>();

        /// <summary>Optional: resolves cliloc messages to text for the journal.</summary>
        public Cliloc Cliloc { get; set; }

        /// <summary>Optional packet hook (incoming = true). Called on network threads.</summary>
        public Action<bool, byte[]> PacketLogger { get; set; }

        public event Action<string> StatusChanged;
        public event Action Entered;

        /// <summary>The server rejected a step and put the player back here.</summary>
        public event Action<int, int, sbyte, byte> MoveRejected;

        /// <summary>Steps sent but not yet acknowledged.</summary>
        public int PendingSteps => _pendingSteps.Count;

        public void Start()
        {
            if (string.IsNullOrEmpty(_settings.Account))
                throw new InvalidOperationException("No account configured.");
            Connect(_settings.Host, _settings.Port);
            SetState(SessionState.LoggingIn, $"Logging in to {_settings.Host}:{_settings.Port} as {_settings.Account}");

            // 0xEF: seed + client version (6.0.5+ clients), then 0x80 account login.
            var seed = (uint)new Random().Next(1, int.MaxValue);
            _conn.Send(new PacketWriter(0xEF, 21).UInt32(seed)
                .Int32(_settings.Version.Major).Int32(_settings.Version.Minor)
                .Int32(_settings.Version.Revision).Int32(_settings.Version.Patch).ToArray());
            _conn.Send(new PacketWriter(0x80, 62).Ascii(_settings.Account, 30).Ascii(_settings.Password, 30).Byte(0x5D).ToArray());
        }

        /// <summary>Handles a packet as if the server had sent it (tests and packet replays).</summary>
        public void Inject(byte[] packet, bool variableLength)
        {
            if (_handlers.TryGetValue(packet[0], out Action<PacketReader> handler))
                handler(new PacketReader(packet, variableLength));
        }

        public void Update(double now)
        {
            _now = now;
            if (_conn == null)
                return;

            // Handle packets for at most HandleBudgetMs per frame (a burst, e.g. walking into a crowd, spreads
            // over a few frames instead of stalling one); always at least a few so nothing starves.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int handled = 0;
            while ((handled < 8 || watch.Elapsed.TotalMilliseconds < HandleBudgetMs) && _conn != null && _conn.TryDequeue(out byte[] packet))
            {
                handled++;
                byte id = packet[0];
                _recent[_recentNext] = packet;
                _recentNext = (_recentNext + 1) % _recent.Length;
                if (_handlers.TryGetValue(id, out Action<PacketReader> handler))
                {
                    double before = watch.Elapsed.TotalMilliseconds;
                    try
                    {
                        handler(new PacketReader(packet, _conn.IsVariable(id)));
                    }
                    catch (Exception e)
                    {
                        SetStatus($"Error handling packet 0x{id:X2}: {e.Message}");
                    }
                    double took = watch.Elapsed.TotalMilliseconds - before;
                    if (took > 5 && SlowPackets.Count < 50)
                        SlowPackets.Add($"0x{id:X2} {took:F1} ms ({packet.Length} bytes)");
                }
            }

            if (_conn != null && !_conn.IsConnected && State != SessionState.Disconnected)
                SetState(SessionState.Disconnected, $"Disconnected: {_conn.CloseReason}");

            SendQueuedUse();

            if (State == SessionState.InWorld && now - _lastPing > PingInterval)
            {
                _lastPing = now;
                _conn.Send(new byte[] { 0x73, _pingSeq++ });
            }
        }

        /// <summary>
        /// Asks the server to step (or turn, if facing a new direction). The caller predicts the move;
        /// a rejection arrives as <see cref="MoveRejected"/>. Returns false if too many steps are unanswered.
        /// </summary>
        public bool RequestStep(int direction, bool run)
        {
            if (State != SessionState.InWorld || _pendingSteps.Count >= 4)
                return false;

            byte seq = (byte)_moveSeq;
            _moveSeq = _moveSeq == 255 ? 1 : _moveSeq + 1;
            _pendingSteps.Enqueue(seq);
            _conn.Send(new PacketWriter(0x02, 7).Byte((direction & 7) | (run ? 0x80 : 0)).Byte(seq).UInt32(0).ToArray());
            return true;
        }

        /// <summary>Speech in the world (0xAD unicode speech, regular type).</summary>
        public void Say(string text, ushort hue = 0x3B2)
        {
            if (State != SessionState.InWorld || string.IsNullOrWhiteSpace(text))
                return;
            _conn.Send(new PacketWriter(0xAD).Byte(0).UInt16(hue).UInt16(3).Ascii("ENU", 4).UnicodeZ(text).ToArray());
        }

        // ---- login ----

        private void LoginDenied(PacketReader r)
        {
            byte reason = r.ReadByte();
            string why = reason switch
            {
                0 => "unknown account or password",
                1 => "account already in use",
                2 => "account blocked",
                3 => "bad password",
                4 => "communication problem",
                5 => "IGR concurrency limit",
                6 => "IGR time limit",
                7 => "general IGR failure",
                _ => $"reason {reason}"
            };
            Fail($"Login denied: {why}");
        }

        private void PopupMessage(PacketReader r)
        {
            byte reason = r.ReadByte();
            string message = reason switch
            {
                0 => "Server: incorrect name or password",
                1 => "Server: someone is already using this account",
                2 => "Server: your account has been blocked",
                5 => "Server: another character from this account is in the world",
                _ => $"Server popup {reason}"
            };
            // A refused Play or Create keeps the connection: back to the character list, with the reason.
            if (_settings.Interactive && State == SessionState.EnteringWorld)
            {
                SetState(SessionState.CharacterSelect, message);
                CharactersListed?.Invoke();
                return;
            }
            SetStatus(message);
        }

        private void ServerList(PacketReader r)
        {
            ReadServers(r);
            if (Servers.Count == 0)
            {
                Fail("Server list is empty.");
                return;
            }
            if (_settings.Interactive && Servers.Count > 1)
            {
                SetStatus("Choose a shard");
                ServersListed?.Invoke();
                return;
            }
            SetStatus($"Selecting server '{Servers[0].Name}'");
            _conn.Send(new PacketWriter(0xA0, 3).UInt16(Servers[0].Index).ToArray());
        }

        private void Relay(PacketReader r)
        {
            string ip = $"{r.ReadByte()}.{r.ReadByte()}.{r.ReadByte()}.{r.ReadByte()}";
            int port = r.ReadUInt16();
            _authKey = r.ReadUInt32();

            _conn.Dispose();
            // A server listening on 0.0.0.0 may relay its LAN address; the host we reached it by works too.
            string host = ip == "0.0.0.0" ? _settings.Host : ip;
            Connect(host, port);
            SetState(SessionState.GameLogin, $"Connecting to game server {host}:{port}");

            // Compression must be on before the login goes out: on a fast link the reply can be read before Send returns.
            _conn.EnableCompression();
            var seed = new byte[] { (byte)(_authKey >> 24), (byte)(_authKey >> 16), (byte)(_authKey >> 8), (byte)_authKey };
            _conn.SendRaw(seed);
            _conn.Send(new PacketWriter(0x91, 65).UInt32(_authKey).Ascii(_settings.Account, 30).Ascii(_settings.Password, 30).ToArray());
        }

        private void CharacterList(PacketReader r)
        {
            int slots = r.ReadByte();
            int found = -1;
            Characters.Clear();
            for (int i = 0; i < slots; i++)
            {
                string name = r.ReadAscii(30);
                r.Skip(30);
                Characters.Add(name);
                if (found < 0 && name.Length > 0 && string.Equals(name, _settings.CharacterName, StringComparison.OrdinalIgnoreCase))
                    found = i;
            }

            int cityCount = r.ReadByte();
            int city = 0;
            Cities.Clear();
            for (int i = 0; i < cityCount; i++)
            {
                int index = r.ReadByte();
                string cityName = r.ReadAscii(32);
                string building = r.ReadAscii(32);
                r.Skip(24);      // x, y, z, map, cliloc, 0
                Cities.Add(new StartCity { Index = index, Name = cityName, Building = building });
                if (cityName.IndexOf(_settings.StartCity ?? "", StringComparison.OrdinalIgnoreCase) >= 0)
                    city = index;
            }

            if (_settings.Interactive)
            {
                SetState(SessionState.CharacterSelect, "Choose a character");
                CharactersListed?.Invoke();
                return;
            }

            SetState(SessionState.CharacterSelect, found >= 0
                ? $"Playing '{_settings.CharacterName}'"
                : $"Creating '{_settings.CharacterName}' in city #{city}");

            if (found >= 0)
                SendPlayCharacter(found);
            else
                SendCreateCharacter(city);
            SetState(SessionState.EnteringWorld, Status);
        }

        private void SendPlayCharacter(int slot)
        {
            _conn.Send(new PacketWriter(0x5D, 73)
                .UInt32(0xEDEDEDED).Ascii(_settings.CharacterName, 30).UInt16(0)
                .UInt32(0x1F)       // client flags: felucca .. tokuno
                .Zero(24)
                .Int32(slot)
                .UInt32(0x0100007F) // client IP (unused)
                .ToArray());
        }

        private void SendCreateCharacter(int city)
        {
            // 7.0.16+ layout (0xF8, 106 bytes). Stats total 90, four skills total 120 (each <= 50).
            _conn.Send(new PacketWriter(0xF8, 106)
                .UInt32(0xEDEDEDED).UInt32(0xFFFFFFFF).Byte(0)
                .Ascii(_settings.CharacterName, 30).UInt16(0)
                .UInt32(0x1F).UInt32(1).UInt32(0)
                .Byte(0).Zero(15)                     // profession: custom
                .Byte(2)                              // human male (post-7.0 numbering)
                .Byte(35).Byte(30).Byte(25)           // str, dex, int
                .Byte(40).Byte(30)                    // swords 30
                .Byte(27).Byte(30)                    // tactics 30
                .Byte(17).Byte(30)                    // healing 30
                .Byte(1).Byte(30)                     // anatomy 30
                .UInt16(0x03EA)                       // skin hue
                .UInt16(0x203B).UInt16(0x044E)        // long hair, brown
                .UInt16(0).UInt16(0)                  // no beard
                .Byte(0).Byte(city)
                .Int32(0).UInt32(0x0100007F)          // slot, client IP
                .UInt16(0x0515).UInt16(0x01BB)        // shirt, pants hues
                .ToArray());
        }

        private void SendClientVersion()
        {
            _conn.Send(new PacketWriter(0xBD).AsciiZ(_settings.Version.ToString()).ToArray());
        }

        private void LoginConfirm(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            r.Skip(4);
            Mobile p = World.GetOrCreateMobile(serial);
            World.PlayerSerial = serial;
            p.Body = r.ReadUInt16();
            p.X = r.ReadUInt16();
            p.Y = r.ReadUInt16();
            p.Z = (sbyte)r.ReadUInt16();
            SetDirection(p, r.ReadByte());
            r.Skip(9);
            World.MapWidth = r.ReadUInt16();
            World.MapHeight = r.ReadUInt16();
            _moveSeq = 0;
            _pendingSteps.Clear();
            SetStatus($"Entering the world at {p.X},{p.Y},{p.Z}");
            World.RaiseMobileUpdated(p);
        }

        private void LoginComplete(PacketReader r)
        {
            _lastPing = _now;
            SetState(SessionState.InWorld, $"In the world ({World.Mobiles.Count} mobiles, {World.Items.Count} items in view)");
            if (_settings.AnnounceChatLinks)
                Say(LinkHello);
            Entered?.Invoke();
        }

        // ---- world ----

        private void PlayerUpdate(PacketReader r)
        {
            Mobile m = World.GetOrCreateMobile(r.ReadUInt32());
            m.Body = r.ReadUInt16();
            r.ReadByte();
            m.Hue = r.ReadUInt16();
            m.Flags = r.ReadByte();
            m.X = r.ReadUInt16();
            m.Y = r.ReadUInt16();
            r.ReadUInt16();
            SetDirection(m, r.ReadByte());
            m.Z = r.ReadSByte();
            if (m.Serial == World.PlayerSerial)
            {
                // A full update resynchronizes movement.
                _moveSeq = 0;
                _pendingSteps.Clear();
            }
            m.AppearanceVersion++;
            World.RaiseMobileUpdated(m);
        }

        private void MobileIncoming(PacketReader r)
        {
            Mobile m = World.GetOrCreateMobile(r.ReadUInt32());
            m.Body = r.ReadUInt16();
            m.X = r.ReadUInt16();
            m.Y = r.ReadUInt16();
            m.Z = r.ReadSByte();
            SetDirection(m, r.ReadByte());
            m.Hue = r.ReadUInt16();
            m.Flags = r.ReadByte();
            m.Notoriety = r.ReadByte();

            m.Equipment.Clear();
            while (r.Remaining >= 4)
            {
                uint serial = r.ReadUInt32();
                if (serial == 0)
                    break;
                ushort graphic = r.ReadUInt16();
                byte layer = r.ReadByte();
                ushort hue = 0;
                if ((graphic & 0x8000) != 0)
                {
                    graphic &= 0x7FFF;
                    hue = r.ReadUInt16();
                }
                m.Equipment[layer] = new EquippedItem { Serial = serial, Graphic = graphic, Layer = layer, Hue = hue };
            }

            m.AppearanceVersion++;
            RequestName(m);
            World.RaiseMobileUpdated(m);
        }

        /// <summary>Other mobiles' names are only sent on request (0x98), once per mobile.</summary>
        private void RequestName(Mobile m)
        {
            if (m.Name != null || m.Serial == World.PlayerSerial || !_nameRequested.Add(m.Serial))
                return;
            _conn.Send(new PacketWriter(0x98).UInt32(m.Serial).ToArray());
        }

        private void MobileName(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            string name = r.ReadAscii(30);
            if (World.Mobiles.TryGetValue(serial, out Mobile m))
            {
                m.Name = name;
                World.RaiseMobileUpdated(m);
            }
        }

        private void MobileMoving(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            Mobile m = World.GetOrCreateMobile(serial);
            ushort body = r.ReadUInt16();
            m.X = r.ReadUInt16();
            m.Y = r.ReadUInt16();
            m.Z = r.ReadSByte();
            SetDirection(m, r.ReadByte());
            ushort hue = r.ReadUInt16();
            m.Flags = r.ReadByte();
            m.Notoriety = r.ReadByte();
            if (body != m.Body || hue != m.Hue)
            {
                m.Body = body;
                m.Hue = hue;
                m.AppearanceVersion++;
            }
            World.RaiseMobileMoved(m);
        }

        private void WorldEntity(PacketReader r)
        {
            r.ReadUInt16();
            int type = r.ReadByte();
            uint serial = r.ReadUInt32();
            ushort graphic = r.ReadUInt16();
            r.ReadByte();
            int amount = r.ReadUInt16();
            r.ReadUInt16();
            int x = r.ReadUInt16(), y = r.ReadUInt16();
            sbyte z = r.ReadSByte();
            r.ReadByte(); // light
            ushort hue = r.ReadUInt16();
            byte flags = r.ReadByte();

            if (type == 1)
            {
                // A mobile described as an entity (e.g. out of the normal update range).
                Mobile m = World.GetOrCreateMobile(serial);
                m.Body = graphic;
                m.X = x;
                m.Y = y;
                m.Z = z;
                m.Hue = hue;
                m.Flags = flags;
                m.AppearanceVersion++;
                World.RaiseMobileUpdated(m);
                return;
            }

            World.Detach(serial, ground: false); // dropped from a container or taken off
            if (!World.Items.TryGetValue(serial, out WorldItem item))
                World.Items[serial] = item = new WorldItem { Serial = serial };
            // Type 2 is a house or boat; its graphic is the multi id. Item graphics can be 0x4000 and up
            // (newer art, the shard's 0x4C00 block), so that bit must not mean "multi".
            item.IsMulti = type == 2;
            item.Graphic = graphic;
            item.Amount = amount;
            item.X = x;
            item.Y = y;
            item.Z = z;
            item.Hue = hue;
            item.Flags = flags;
            World.RaiseItemUpdated(item);
        }

        private void WorldItemOld(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            ushort graphic = r.ReadUInt16();
            int amount = (serial & 0x80000000) != 0 ? r.ReadUInt16() : 1;
            serial &= 0x7FFFFFFF;
            int x = r.ReadUInt16(), y = r.ReadUInt16();
            if ((x & 0x8000) != 0)
                r.ReadByte(); // direction
            sbyte z = r.ReadSByte();
            ushort hue = (y & 0x8000) != 0 ? r.ReadUInt16() : (ushort)0;
            byte flags = (y & 0x4000) != 0 ? r.ReadByte() : (byte)0;

            World.Detach(serial, ground: false);
            if (!World.Items.TryGetValue(serial, out WorldItem item))
                World.Items[serial] = item = new WorldItem { Serial = serial };
            // Pre-HS packet: multis carry their id with 0x4000 set (item graphics stay below 0x4000 here).
            item.IsMulti = graphic >= 0x4000;
            item.Graphic = item.IsMulti ? (ushort)(graphic & 0x3FFF) : graphic;
            item.Amount = amount;
            item.X = x & 0x7FFF;
            item.Y = y & 0x3FFF;
            item.Z = z;
            item.Hue = hue;
            item.Flags = flags;
            World.RaiseItemUpdated(item);
        }

        private void EquipUpdate(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            ushort graphic = r.ReadUInt16();
            r.ReadByte();
            byte layer = r.ReadByte();
            uint parent = r.ReadUInt32();
            ushort hue = r.ReadUInt16();
            World.Detach(serial); // put on from the ground, a container or another layer
            Mobile m = World.GetOrCreateMobile(parent);
            m.Equipment[layer] = new EquippedItem { Serial = serial, Graphic = graphic, Layer = layer, Hue = hue };
            m.AppearanceVersion++;
            World.RaiseMobileUpdated(m);
        }

        private void MobileStatus(PacketReader r)
        {
            Mobile m = World.GetOrCreateMobile(r.ReadUInt32());
            m.Name = r.ReadAscii(30);
            m.Hits = r.ReadUInt16();
            m.HitsMax = r.ReadUInt16();
            r.ReadByte(); // can rename
            int version = r.ReadByte();
            if (version > 0)
            {
                // Full status (our own character): version 3 classic, 4 AOS, 5 ML, 6 HS.
                m.HasFullStatus = true;
                m.Female = r.ReadBool();
                m.Str = r.ReadUInt16();
                m.Dex = r.ReadUInt16();
                m.Int = r.ReadUInt16();
                m.Stamina = r.ReadUInt16();
                m.StaminaMax = r.ReadUInt16();
                m.Mana = r.ReadUInt16();
                m.ManaMax = r.ReadUInt16();
                m.Gold = r.ReadInt32();
                m.Armor = r.ReadUInt16();
                m.Weight = r.ReadUInt16();
                if (version >= 5)
                {
                    m.MaxWeight = r.ReadUInt16();
                    r.ReadByte(); // race
                }
                m.StatCap = r.ReadUInt16();
                m.Followers = r.ReadByte();
                m.FollowersMax = r.ReadByte();
                if (version >= 4)
                {
                    m.FireResist = r.ReadUInt16();
                    m.ColdResist = r.ReadUInt16();
                    m.PoisonResist = r.ReadUInt16();
                    m.EnergyResist = r.ReadUInt16();
                    m.Luck = r.ReadUInt16();
                    m.DamageMin = r.ReadUInt16();
                    m.DamageMax = r.ReadUInt16();
                    m.Tithe = r.ReadInt32();
                }
                if (m.MaxWeight == 0)
                    m.MaxWeight = 40 + (int)(3.5 * m.Str); // the AOS formula, when the server doesn't send it
            }
            World.RaiseMobileUpdated(m);
        }

        private void MoveAck(PacketReader r)
        {
            r.ReadByte(); // seq
            byte noto = r.ReadByte();
            if (_pendingSteps.Count > 0)
                _pendingSteps.Dequeue();
            Mobile p = World.Player;
            if (p != null)
                p.Notoriety = noto;
        }

        private void MoveDenied(PacketReader r)
        {
            r.ReadByte(); // seq
            int x = r.ReadUInt16(), y = r.ReadUInt16();
            byte dir = r.ReadByte();
            sbyte z = r.ReadSByte();
            _moveSeq = 0;
            _pendingSteps.Clear();

            Mobile p = World.Player;
            if (p != null)
            {
                p.X = x;
                p.Y = y;
                p.Z = z;
                SetDirection(p, dir);
            }
            MoveRejected?.Invoke(x, y, z, (byte)(dir & 7));
        }

        /// <summary>
        /// 0x76 (16 bytes): x, y, z (int16 each), 0, two unused int16, map width, map height. ModernUO sends it
        /// when the player changes facet, before the new map (0xBF 0x08) and the player update (0x20).
        /// </summary>
        private void ServerChange(PacketReader r)
        {
            int x = r.ReadUInt16(), y = r.ReadUInt16();
            var z = (sbyte)r.ReadInt16();
            r.ReadByte();
            r.ReadUInt16();
            r.ReadUInt16();
            int width = r.ReadUInt16(), height = r.ReadUInt16();
            if (width > 0 && height > 0)
            {
                World.MapWidth = width;
                World.MapHeight = height;
            }
            // Steps in flight belong to the old place.
            _moveSeq = 0;
            _pendingSteps.Clear();
            SetStatus($"Server change: moved to {x},{y},{z} (map {width}x{height})");

            Mobile p = World.Player;
            if (p != null)
            {
                p.X = x;
                p.Y = y;
                p.Z = z;
                World.RaiseMobileUpdated(p);
            }
            ServerChanged?.Invoke(x, y, z);
        }

        /// <summary>The server moved us to another facet or server area (0x76); the new map follows in 0xBF 0x08.</summary>
        public event Action<int, int, sbyte> ServerChanged;

        /// <summary>Applies a step the player predicted locally, so the world model matches what is drawn.</summary>
        public void ApplyPredictedStep(int x, int y, sbyte z, int direction)
        {
            Mobile p = World.Player;
            if (p == null)
                return;
            p.X = x;
            p.Y = y;
            p.Z = z;
            p.Direction = (byte)(direction & 7);
        }

        private void UnicodeSpeech(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            r.ReadUInt16(); // body
            byte type = r.ReadByte();
            ushort hue = r.ReadUInt16();
            r.ReadUInt16(); // font
            r.ReadAscii(4); // language
            string name = r.ReadAscii(30);
            string text = r.ReadUnicodeZ();
            AddSpeech(serial, name, text, hue, type);
        }

        private void AsciiSpeech(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            r.ReadUInt16();
            byte type = r.ReadByte();
            ushort hue = r.ReadUInt16();
            r.ReadUInt16();
            string name = r.ReadAscii(30);
            string text = r.ReadAsciiZ();
            AddSpeech(serial, name, text, hue, type);
        }

        private void ClilocMessage(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            r.ReadUInt16();
            byte type = r.ReadByte();
            ushort hue = r.ReadUInt16();
            r.ReadUInt16();
            int number = r.ReadInt32();
            string name = r.ReadAscii(30);
            string args = r.ReadUnicodeLEZ();
            string text = Cliloc != null ? Cliloc.Format(number, args) : $"[cliloc {number}] {args}";
            AddSpeech(serial, name, text, hue, type);
        }

        private void ClilocMessageAffix(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            r.ReadUInt16();
            byte type = r.ReadByte();
            ushort hue = r.ReadUInt16();
            r.ReadUInt16();
            int number = r.ReadInt32();
            byte flags = r.ReadByte();
            string name = r.ReadAscii(30);
            string affix = r.ReadAsciiZ();
            string args = r.ReadUnicodeZ();
            string text = Cliloc != null ? Cliloc.Format(number, args) : $"[cliloc {number}]";
            text = (flags & 1) != 0 ? affix + text : text + affix;
            AddSpeech(serial, name, text, hue, type);
        }

        private void AddSpeech(uint serial, string name, string text, ushort hue, byte type)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            bool system = serial == 0xFFFFFFFF || serial == 0 || string.IsNullOrEmpty(name) || name == "System";
            string link = ExtractLink(ref text);
            World.AddJournal(new JournalEntry
            {
                Time = DateTime.Now,
                Speaker = system ? null : name,
                Name = name,
                Text = text.Trim(),
                Hue = hue,
                Type = type,
                Serial = serial,
                Link = link
            });
        }

        /// <summary>
        /// UO Offline marks chat lines that offer something ("join group 12", "view lot 38", "fill order 5")
        /// with a trailing "{{link join 12}}", sent only to clients that said <see cref="LinkHello"/>.
        /// Cut off here, so no window or overhead ever shows it; the journal entry keeps "join 12".
        /// </summary>
        public static string ExtractLink(ref string text)
        {
            const string open = "{{link ", close = "}}";
            if (string.IsNullOrEmpty(text) || !text.EndsWith(close, StringComparison.Ordinal))
                return null;
            int start = text.LastIndexOf(open, StringComparison.Ordinal);
            if (start < 0)
                return null;
            string link = text.Substring(start + open.Length, text.Length - start - open.Length - close.Length).Trim();
            int space = link.IndexOf(' ');
            if (space <= 0 || !int.TryParse(link.Substring(space + 1), out _))
                return null;
            string verb = link.Substring(0, space);
            if (verb != "join" && verb != "view" && verb != "fill")
                return null;
            text = text.Substring(0, start).TrimEnd();
            return link;
        }

        /// <summary>Said once in the world so UO Offline sends chat links (as its own client does).</summary>
        public const string LinkHello = "[uoclient 1";

        private void Extended(PacketReader r)
        {
            int sub = r.ReadUInt16();
            if (TradeExtended(sub, r) || HousingExtended(sub, r))
                return;
            if (sub == 0x08) // set map
                World.Map = r.ReadByte();
            else if (sub == 0x04) // close gump
                CloseGumpFromServer((uint)r.ReadInt32(), r.ReadInt32());
        }

        // ---- plumbing ----

        private static void SetDirection(Mobile m, byte raw)
        {
            m.Direction = (byte)(raw & 7);
            m.Running = (raw & 0x80) != 0;
        }

        private void Connect(string host, int port)
        {
            _conn = new NetConnection(host, port, _settings.Version) { PacketLogger = PacketLogger };
        }

        private void Fail(string message)
        {
            SetState(SessionState.Disconnected, message);
            _conn?.Close(message);
        }

        private void SetState(SessionState state, string status)
        {
            State = state;
            SetStatus(status);
        }

        private void SetStatus(string status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }

        public void Dispose()
        {
            _conn?.Dispose();
            _conn = null;
        }
    }
}
