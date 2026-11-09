using System.Diagnostics;
using System.Text.Json;
using UOClient.Core;
using UOClient.IO.Formats;
using UOClient.Net;

namespace UOClient.Cli;

/// <summary>
/// uoclient online [--config file] [--walk N] [--seconds S] [--log file]
/// Logs in headless, enters the world, optionally walks N steps, and reports what the server sent.
/// </summary>
internal static class Online
{
    public static async Task<int> Run(List<string> args, string uoPath, UOClient.IO.ClientSettings server)
    {
        string configPath = Take(args, "--config") ?? Path.Combine(SettingsFile.ProjectRoot(), "UserSettings", "uoclient-account.json");
        int walk = int.Parse(Take(args, "--walk") ?? "0");
        double seconds = double.Parse(Take(args, "--seconds") ?? "8");
        string? logPath = Take(args, "--log");
        bool interact = args.Remove("--interact");
        bool interactive = args.Remove("--interactive");

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"No account config at {configPath}.");
            return 2;
        }
        using JsonDocument cfg = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement root = cfg.RootElement;
        var settings = new LoginSettings
        {
            // The server is a setting (unity-uo-settings.json or --server), not part of the account file.
            Host = server.host,
            Port = server.port,
            Account = root.GetProperty("account").GetString(),
            Password = root.GetProperty("password").GetString(),
            CharacterName = root.GetProperty("character").GetString()!,
            StartCity = root.TryGetProperty("startCity", out JsonElement c) ? c.GetString()! : "Britain",
            Version = ClientVersion.Parse(root.TryGetProperty("clientVersion", out JsonElement v) ? v.GetString()! : "7.0.23.1"),
            Interactive = interactive
        };

        Console.WriteLine(server.IsLocal
            ? $"Server: {server.Server} (local test server)"
            : $"Server: {server.Server} — NOT the local test server");
        using var session = new GameSession(settings);
        string cliloc = Path.Combine(uoPath, "Cliloc.enu");
        if (File.Exists(cliloc))
            session.Cliloc = new Cliloc(cliloc);
        string speech = Path.Combine(uoPath, "speech.mul");
        if (File.Exists(speech))
            session.SpeechKeywords = new SpeechKeywords(speech);
        else
            Console.Error.WriteLine($"speech.mul not found at {speech}; speech goes out untagged and keyword NPCs (banker, stablemaster, vendors) will ignore it.");

        StreamWriter? log = logPath != null ? new StreamWriter(logPath) { AutoFlush = true } : null;
        var counts = new System.Collections.Concurrent.ConcurrentDictionary<byte, int>();
        session.PacketLogger = (incoming, p) =>
        {
            if (incoming)
                counts.AddOrUpdate(p[0], 1, (_, n) => n + 1);
            if (log != null)
            {
                byte[] shown = Redact(incoming, p);
                lock (log)
                    log.WriteLine($"{(incoming ? "<<" : ">>")} 0x{p[0]:X2} len {p.Length}: {Convert.ToHexString(shown.AsSpan(0, Math.Min(shown.Length, 48)))}");
            }
        };

        var clock = Stopwatch.StartNew();
        session.StatusChanged += s => Console.WriteLine($"[{clock.Elapsed.TotalSeconds,5:F1}s] {s}");
        session.World.JournalAdded += j => Console.WriteLine($"         journal: {(j.Speaker != null ? j.Speaker + ": " : "")}{j.Text}");
        int rejected = 0;
        session.MoveRejected += (x, y, z, d) => { rejected++; Console.WriteLine($"         step rejected, back at {x},{y},{z}"); };

        session.World.PaperdollOpened += (s, t) => Console.WriteLine($"         paperdoll opened for 0x{s:X8}: \"{t}\"");
        session.World.ContainerOpened += c => Console.WriteLine($"         container 0x{c.Serial:X8} opened with gump 0x{c.Gump:X4}");
        session.World.ContainerChanged += c => Console.WriteLine($"         container 0x{c:X8} now holds {session.World.ItemsIn(c).Count()} items");
        bool dumpGumps = args.Remove("--gumps");
        session.World.GumpOpened += g =>
        {
            Console.WriteLine($"         gump type 0x{g.TypeId:X} at {g.X},{g.Y}: {g.Commands.Count} commands, {g.Lines.Length} lines; " +
                              $"first: {string.Join(" ", g.Commands.Take(4))}; text: {string.Join(" | ", g.Lines.Take(3))}");
            if (dumpGumps)
            {
                foreach (GumpCommand c in g.Commands)
                    Console.WriteLine($"             {c}");
                for (int i = 0; i < g.Lines.Length; i++)
                    Console.WriteLine($"             line {i}: {g.Lines[i]}");
            }
        };
        bool interacted = false;
        session.SkillsUpdated += () => Console.WriteLine($"         skills: {session.Skills.Count} — " +
            string.Join(", ", session.Skills.Values.Where(s => s.Base > 0).Select(s => $"{GameSession.SkillNames[s.Id]} {s.Base:F1}")));
        session.WarModeChanged += w => Console.WriteLine($"         war mode {(w ? "on" : "off")}");

        // --interactive: stop at the lists like the login screens do, then pick our character by slot.
        if (interactive)
        {
            session.ServersListed += () =>
            {
                Console.WriteLine($"         servers: {string.Join(", ", session.Servers.Select(s => s.Name))}");
                session.SelectServer(session.Servers[0].Index);
            };
            session.CharactersListed += () =>
            {
                Console.WriteLine($"         characters: [{string.Join(", ", session.Characters.Select(n => n.Length == 0 ? "-" : n))}], " +
                                  $"cities: {string.Join(", ", session.Cities.Select(ct => ct.Name))}");
                int slot = session.Characters.FindIndex(n => string.Equals(n, settings.CharacterName, StringComparison.OrdinalIgnoreCase));
                if (slot >= 0)
                    session.PlayCharacter(slot);
                else
                    Console.WriteLine("         (test character not on this account; not creating one in interactive mode)");
            };
        }

        session.Start();

        bool itemsTest = args.Remove("--items");
        int dropDx = int.Parse(Take(args, "--drop-dx") ?? "0");
        int itemStage = 0;
        ContainedItem? testItem = null;
        double dropAt = 0, nextSay = 0;
        var sayQueue = new Queue<string>();
        while (Take(args, "--say") is string said)
            sayQueue.Enqueue(said);
        session.LiftRejected += reason => Console.WriteLine($"         lift refused (reason {reason})");
        // --buy: say "vendor buy", then buy the cheapest item (up to 25 gp) from the first list that comes.
        bool buyTest = args.Remove("--buy");
        bool bought = false;
        if (buyTest)
        {
            sayQueue.Enqueue("vendor buy");
            session.ShopOpened += list =>
            {
                Console.WriteLine($"         shop from 0x{list.Vendor:X8} ({(list.Selling ? "sell" : "buy")}): {list.Items.Count} items");
                ShopItem? cheap = list.Items.Where(i => i.Price > 0 && i.Price <= 25).OrderBy(i => i.Price).FirstOrDefault();
                if (bought || list.Selling || cheap == null)
                    return;
                bought = true;
                Console.WriteLine($"         -> buy 1 {cheap.Name} (0x{cheap.Graphic:X4}, serial 0x{cheap.Serial:X8}) for {cheap.Price} gp from 0x{list.Vendor:X8}; gold {session.World.Player?.Gold}");
                session.Buy(list.Vendor, new[] { (cheap.Serial, 1) });
            };
            session.World.ContainerChanged += c =>
            {
                if (bought && c == session.World.Worn(session.World.Player, 21))
                    Console.WriteLine($"         backpack changed: {session.World.ItemsIn(c).Count()} items; gold {session.World.Player?.Gold}");
            };
        }

        // --worldlayer: the world editor's messages (WORLD_EDITOR.md, step 1) against a server that has them: hello,
        // raise and paint sand at our feet, request the region, check the server's answer, then undo it.
        bool worldTest = args.Remove("--worldlayer");
        int worldStage = 0;
        double worldAt = 0;
        UOClient.WorldLayer.WorldRegion? worldRegion = null;
        if (worldTest)
        {
            session.WorldEditorReady += can => Console.WriteLine($"         world editor: hello answered, can edit = {can}");
            session.WorldEditResult += (ok, msg) => Console.WriteLine($"         world editor: result {(ok ? "ok" : "refused")}: {msg}");
            session.WorldRegionChanged += (m, rx, ry, v) => Console.WriteLine($"         world editor: region {m}:{rx},{ry} changed to version {v}");
            session.WorldRegionReceived += (m, rx, ry, v, data) =>
            {
                worldRegion = data == null ? null : UOClient.WorldLayer.WorldRegion.Deserialize(data);
                Console.WriteLine(data == null
                    ? $"         world editor: region {m}:{rx},{ry} unpainted"
                    : $"         world editor: region {m}:{rx},{ry} version {v}, {data.Length} bytes, {worldRegion!.Edited.Count(e => e)} edited tile(s)");
            };
        }

        double enteredAt = -1, nextStep = 0;
        int sent = 0;
        int[] route = { 3, 3, 1, 1, 7, 7, 5, 5 }; // a small loop: down, right, up, left (screen directions)
        while (clock.Elapsed.TotalSeconds < seconds + (enteredAt < 0 ? 15 : enteredAt))
        {
            double now = clock.Elapsed.TotalSeconds;
            session.Update(now);
            if (session.State == SessionState.Disconnected)
                break;

            if (session.State == SessionState.InWorld)
            {
                if (enteredAt < 0)
                    enteredAt = now;
                if (worldTest && now - enteredAt > 1 && now >= worldAt && session.World.Player is { } wme)
                {
                    int map = session.World.Map;
                    (int rx, int ry) = UOClient.WorldLayer.WorldRegion.RegionOfTile(wme.X, wme.Y);
                    worldAt = now + 1.5;
                    switch (worldStage++)
                    {
                        case 0:
                            Console.WriteLine($"         -> world editor hello at {wme.X},{wme.Y} (region {map}:{rx},{ry})");
                            session.WorldHello();
                            break;
                        case 1:
                            Console.WriteLine("         -> raise x3 and paint sand at our feet (radius 3), end stroke");
                            for (int k = 0; k < 3; k++)
                                session.SendWorldStroke(map, new UOClient.WorldLayer.BrushStroke { Tool = UOClient.WorldLayer.BrushTool.Raise, X = wme.X + 0.5f, Y = wme.Y + 0.5f, Radius = 3, Softness = 0.6f, Strength = 1 });
                            session.SendWorldStroke(map, new UOClient.WorldLayer.BrushStroke { Tool = UOClient.WorldLayer.BrushTool.Paint, X = wme.X + 0.5f, Y = wme.Y + 0.5f, Radius = 3, Softness = 0.5f, Strength = 1, Material = UOClient.WorldLayer.WorldMaterials.Sand });
                            session.EndWorldStroke();
                            break;
                        case 2:
                            Console.WriteLine("         -> request the region");
                            session.RequestWorldRegion(map, rx, ry, -1);
                            break;
                        case 3:
                            if (worldRegion != null)
                            {
                                int s = UOClient.WorldLayer.WorldRegion.SamplesPerTile;
                                int i = UOClient.WorldLayer.WorldRegion.Index(wme.X * s - worldRegion.SampleX0, wme.Y * s - worldRegion.SampleY0);
                                Console.WriteLine($"         region at our feet: height {worldRegion.Height[i] / 8f:0.0} Z (we stand at {wme.Z}), dominant material {UOClient.WorldLayer.WorldMaterials.Names[worldRegion.Dominant(i)]}");
                            }
                            Console.WriteLine("         -> undo");
                            session.UndoWorld(false);
                            break;
                        case 4:
                            session.RequestWorldRegion(map, rx, ry, -1);
                            break;
                        case 5:
                            Console.WriteLine($"         -> tile brush: flatten a 2x2 tile footprint at our feet to Z {wme.Z + 10}");
                            session.SendWorldStroke(map, new UOClient.WorldLayer.BrushStroke { Tool = UOClient.WorldLayer.BrushTool.Flatten, Shape = UOClient.WorldLayer.BrushShape.Tile, X = wme.X + 0.5f, Y = wme.Y + 0.5f, Radius = 1, Strength = 1, TargetZ = wme.Z + 10 });
                            session.EndWorldStroke();
                            break;
                        case 6:
                        case 9:
                            session.RequestWorldRegion(map, rx, ry, -1);
                            break;
                        case 7:
                        case 10:
                            if (worldRegion != null)
                            {
                                int s = UOClient.WorldLayer.WorldRegion.SamplesPerTile;
                                int i = UOClient.WorldLayer.WorldRegion.Index(wme.X * s - worldRegion.SampleX0, wme.Y * s - worldRegion.SampleY0);
                                Console.WriteLine($"         region at our feet after the {(worldStage == 8 ? "tile flatten" : "erase")}: height {worldRegion.Height[i] / 8f:0.0} Z (we stand at {wme.Z})");
                            }
                            break;
                        case 8:
                            Console.WriteLine("         -> eraser: the same footprint back to the original map");
                            session.SendWorldStroke(map, new UOClient.WorldLayer.BrushStroke { Tool = UOClient.WorldLayer.BrushTool.Erase, Shape = UOClient.WorldLayer.BrushShape.Tile, X = wme.X + 0.5f, Y = wme.Y + 0.5f, Radius = 1, Strength = 1 });
                            session.EndWorldStroke();
                            break;
                    }
                }
                if (interact && !interacted && now - enteredAt > 1)
                {
                    interacted = true;
                    Console.WriteLine("         -> open paperdoll, open backpack, press Help");
                    session.OpenPaperdoll();
                    if (!session.OpenBackpack())
                        Console.WriteLine("         (no backpack worn)");
                    session.RequestHelp();
                    session.RequestSkills();
                    session.SetWarMode(true);
                    session.UseSkill(1); // Anatomy: asks for a target
                }
                if (interact && interacted && session.Target != null && now - enteredAt > 3)
                {
                    Console.WriteLine($"         target cursor {session.Target.CursorId:X} (type {session.Target.Type}, flags {session.Target.Flags}) -> targeting self");
                    session.TargetObject(session.World.PlayerSerial);
                    session.SetWarMode(false);
                }

                // --items: list the backpack, put one item on the ground at our feet, then back (packet log shows the replies).
                if (itemsTest && itemStage == 0 && now - enteredAt > 1)
                {
                    itemStage = 1;
                    session.OpenBackpack();
                }
                else if (itemsTest && itemStage == 1 && now - enteredAt > 2.5)
                {
                    itemStage = 2;
                    uint pack = session.World.Worn(session.World.Player, 21);
                    foreach (ContainedItem ci in session.World.ItemsIn(pack))
                        Console.WriteLine($"         pack: 0x{ci.Serial:X8} graphic 0x{ci.Graphic:X4} x{ci.Amount} at {ci.X},{ci.Y} '{session.World.Player?.Name}'");
                    testItem = session.World.ItemsIn(pack).FirstOrDefault(i => i.Graphic != 0x0EED);
                    if (testItem != null)
                    {
                        Mobile me = session.World.Player!;
                        Console.WriteLine($"         -> lift 0x{testItem.Serial:X8} and drop it at our feet {me.X},{me.Y},{me.Z}");
                        session.Lift(testItem.Serial, testItem.Amount);
                        dropAt = now + 0.6;
                    }
                }
                else if (itemsTest && itemStage == 2 && testItem != null && now > dropAt)
                {
                    itemStage = 3;
                    Mobile me = session.World.Player!;
                    session.DropOnGround(testItem.Serial, me.X + dropDx, me.Y, me.Z);
                    dropAt = now + 1.5;
                }
                else if (itemsTest && itemStage == 3 && testItem != null && now > dropAt)
                {
                    itemStage = 4;
                    Console.WriteLine($"         on the ground now: {session.World.Items.ContainsKey(testItem.Serial)}; putting it back");
                    session.Lift(testItem.Serial, testItem.Amount);
                    dropAt = now + 0.6;
                }
                else if (itemsTest && itemStage == 4 && testItem != null && now > dropAt)
                {
                    itemStage = 5;
                    session.DropInto(testItem.Serial, session.World.Worn(session.World.Player, 21), testItem.X, testItem.Y);
                }
                if (sayQueue.Count > 0 && now - enteredAt > 2 && now > nextSay)
                {
                    string line = sayQueue.Dequeue();
                    Console.WriteLine($"         -> say \"{line}\"");
                    session.Say(line);
                    nextSay = now + 2;
                }

                Mobile? p = session.World.Player;
                if (p != null && sent < walk && now >= nextStep && session.PendingSteps == 0)
                {
                    int dir = route[sent % route.Length];
                    // Turning to a new direction costs a request without moving; send it and step after.
                    if (session.RequestStep(dir, false))
                    {
                        if (p.Direction == dir)
                            sent++;
                        else
                            p.Direction = (byte)dir;
                        nextStep = now + 0.45;
                    }
                }
            }
            await Task.Delay(15);
        }

        World w = session.World;
        Mobile? player = w.Player;
        Console.WriteLine();
        Console.WriteLine($"State: {session.State}  ({session.Status})");
        if (player != null)
        {
            Console.WriteLine($"Player: '{player.Name}' 0x{player.Serial:X8} body {player.Body} at {player.X},{player.Y},{player.Z} map {w.Map}, hits {player.Hits}/{player.HitsMax}, {player.Equipment.Count} equipped");
            Console.WriteLine($"Steps: {sent} sent, {rejected} rejected, {session.PendingSteps} unanswered");
        }
        Console.WriteLine($"In view: {w.Mobiles.Count} mobiles, {w.Items.Count} items, light level {w.GlobalLight}");
        foreach (Mobile m in w.Mobiles.Values.Where(m => m.Serial != w.PlayerSerial).OrderBy(m => Math.Abs(m.X - (player?.X ?? 0)) + Math.Abs(m.Y - (player?.Y ?? 0))).Take(8))
            Console.WriteLine($"  {m.Name ?? "?",-24} body {m.Body,4} at {m.X},{m.Y},{m.Z}  {m.Equipment.Count} equipped");
        Console.WriteLine("Packets received: " + string.Join(" ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key:X2}x{k.Value}")));
        log?.Dispose();
        return session.State == SessionState.InWorld ? 0 : 1;
    }

    /// <summary>Blanks the password field of outgoing login packets (0x80 at 31, 0x91 at 35) before logging.</summary>
    private static byte[] Redact(bool incoming, byte[] p)
    {
        int at = incoming ? -1 : p[0] == 0x80 ? 31 : p[0] == 0x91 ? 35 : -1;
        if (at < 0 || p.Length < at + 30)
            return p;
        var copy = (byte[])p.Clone();
        Array.Fill(copy, (byte)'*', at, 30);
        return copy;
    }

    private static string? Take(List<string> args, string name)
    {
        int i = args.IndexOf(name);
        if (i < 0 || i + 1 >= args.Count)
            return null;
        string v = args[i + 1];
        args.RemoveRange(i, 2);
        return v;
    }

}
