using System.Text.RegularExpressions;
using UOClient.Core;
using UOClient.Net;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// The hotkeys' newer macro actions (TazUO's MacroType AttackLast, TargetNext, LastSpell, LastSkill, Primary / Secondary
/// Ability, Bow, Salute, Delay, OpenDoor) from an offline session: what goes out checked byte for byte. Hotkeys.Run
/// itself needs Unity, so its dispatch is read from the source.
/// </summary>
public sealed class MacroActionsTests
{
    private const uint Me = 0x00000042, Orc = 0x00000077, Ettin = 0x00000090, Llama = 0x00000101;

    private const byte Innocent = 1, Ally = 2, CanBeAttacked = 3, Murderer = 6, Invulnerable = 7;

    private static GameSession InWorld(List<byte[]> sent, uint features = 0x10)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0xB9, 5).UInt32(features).ToArray(), false);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        session.Inject(new byte[] { 0x22, 0, Innocent }, false);
        sent.Clear();
        return session;
    }

    // ModernUO's MobileMoving (0x77): serial, body, x, y, z, direction, hue, flags, notoriety.
    private static void Moving(GameSession session, uint serial, int x, byte notoriety) =>
        session.Inject(new PacketWriter(0x77, 17).UInt32(serial).UInt16(0x190).UInt16(x).UInt16(1629).Byte(0).Byte(2)
            .UInt16(0x83EA).Byte(0).Byte(notoriety).ToArray(), false);

    private static void Cursor(GameSession session) =>
        session.Inject(new PacketWriter(0x6C, 19).Byte(0).UInt32(0x01).Byte(1).Zero(12).ToArray(), false);

    // ---- last spell / last skill ----

    [Fact]
    public void LastSpellAndSkillRepeatWhatWasSent()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Assert.False(s.CastLastSpell());
        Assert.False(s.UseLastSkill());
        Assert.Empty(sent);

        s.CastSpell(29);
        s.UseSkill(21);
        Assert.Equal(29, s.LastSpell);
        Assert.Equal(21, s.LastSkill);
        sent.Clear();

        Assert.True(s.CastLastSpell());
        Assert.True(s.UseLastSkill());
        Assert.Equal(GameSession.CastSpellRequest(29, s.Version), sent[0]);
        Assert.Equal(new byte[] { 0x12, 0, 9, 0x24, (byte)'2', (byte)'1', (byte)' ', (byte)'0', 0 }, sent[1]);

        s.CastSpellFromBook(5, 0x40000010);
        Assert.Equal(5, s.LastSpell);
        s.UseSkill(0); // Alchemy is skill 0: still a last skill
        Assert.Equal(0, s.LastSkill);
    }

    [Fact]
    public void NotInWorldRemembersNothing()
    {
        var s = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        s.CastSpell(4);
        s.UseSkill(21);
        Assert.Equal(0, s.LastSpell);
        Assert.Equal(-1, s.LastSkill);
        Assert.False(s.CastLastSpell());
        Assert.False(s.UseLastSkill());
    }

    [Fact]
    public void DisposeForgetsLastSpellAndSkill()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        s.CastSpell(4);
        s.UseSkill(46);
        s.Dispose();
        Assert.Equal(0, s.LastSpell);
        Assert.Equal(-1, s.LastSkill);
        Assert.False(s.CastLastSpell());
    }

    [Fact]
    public void DisconnectForgetsLastSpellAndSkill()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        s.CastSpell(4);
        s.UseSkill(46);
        s.SimulateConnectionLost("gone");
        s.Update(1);
        Assert.Equal(SessionState.Disconnected, s.State);
        Assert.Equal(0, s.LastSpell);
        Assert.Equal(-1, s.LastSkill);
    }

    [Fact]
    public void ANewSessionStartsWithNoLastAnything()
    {
        var sent = new List<byte[]>();
        GameSession first = InWorld(sent);
        Moving(first, Orc, 1496, CanBeAttacked);
        first.CastSpell(4);
        first.TargetNext();
        first.Dispose();

        GameSession second = InWorld(sent);
        Assert.Equal((0, -1, 0u, 0u), (second.LastSpell, second.LastSkill, second.LastTarget, second.LastAttack));
        Assert.False(second.AttackLast());
        Assert.Empty(sent);
    }

    // ---- attack last ----

    [Fact]
    public void AttackLastWithNoTargetSendsNothing()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Assert.Equal(0u, s.LastTarget);
        Assert.False(s.AttackLast());
        Assert.Empty(sent);
    }

    [Fact]
    public void AttackLastAttacksTheLastTargetedMobile()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Moving(s, Orc, 1496, CanBeAttacked);
        Cursor(s);
        s.TargetObject(Orc);
        sent.Clear();

        Assert.True(s.AttackLast());
        Assert.Equal(GameSession.AttackRequest(Orc), Assert.Single(sent));
        Assert.Equal(Orc, s.LastAttack);
    }

    [Fact]
    public void AttackLastKeepsTheCriminalQuery()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Moving(s, Llama, 1496, Innocent);
        s.TargetNext();
        var asked = new List<uint>();
        s.AttackQueryRequested += asked.Add;

        Assert.True(s.AttackLast());
        Assert.Equal(new[] { Llama }, asked);
        Assert.Empty(sent);
    }

    [Fact]
    public void AttackLastSkipsItemsSelfAndMobilesOutOfView()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Cursor(s);
        s.TargetObject(0x40000123); // an item
        Assert.False(s.AttackLast());
        Cursor(s);
        s.TargetSelf();
        Assert.False(s.AttackLast());
        Cursor(s);
        s.TargetObject(Ettin); // a mobile we never saw
        sent.Clear();
        Assert.False(s.AttackLast());
        Assert.Empty(sent);
        Assert.False(new GameSession(new LoginSettings()).AttackLast());
    }

    // ---- target next / nearest ----

    [Fact]
    public void TargetNextCyclesMobilesBySerialAndSendsNothing()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Assert.Equal(0u, s.TargetNext()); // only us
        Moving(s, Llama, 1497, Innocent);
        Moving(s, Orc, 1496, CanBeAttacked);
        Moving(s, Ettin, 1498, Murderer);
        var bars = new List<uint>();
        s.LastAttackSet += bars.Add;

        Assert.Equal(new[] { Orc, Ettin, Llama, Orc }, new[] { s.TargetNext(), s.TargetNext(), s.TargetNext(), s.TargetNext() });
        Assert.Equal(Orc, s.LastTarget);
        Assert.Equal(Orc, s.LastAttack);
        Assert.Equal(new[] { Orc, Ettin, Llama, Orc }, bars); // TazUO sets LastAttack: the attack bar follows
        Assert.Empty(sent);
    }

    [Fact]
    public void NextMobileStartsAtTheFirstWhenTheLastIsGone()
    {
        var mobiles = new[] { new Mobile { Serial = Me }, new Mobile { Serial = Orc }, new Mobile { Serial = Ettin } };
        Assert.Equal(Orc, GameSession.NextMobile(mobiles, Me, 0));
        Assert.Equal(Ettin, GameSession.NextMobile(mobiles, Me, 0x80));      // a serial no longer here
        Assert.Equal(Orc, GameSession.NextMobile(mobiles, Me, Ettin));       // wraps
        Assert.Equal(0u, GameSession.NextMobile(new[] { new Mobile { Serial = Me } }, Me, 0));
    }

    [Fact]
    public void TargetNearestPicksTheClosestHostile()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Moving(s, Llama, 1496, Innocent);     // nearest, but innocent
        Moving(s, 0x00000050, 1496, Ally);
        Moving(s, 0x00000051, 1496, Invulnerable);
        Assert.Equal(0u, s.TargetNearest());
        Moving(s, Ettin, 1500, Murderer);
        Moving(s, Orc, 1498, CanBeAttacked);

        Assert.Equal(Orc, s.TargetNearest());
        Assert.Equal((Orc, Orc), (s.LastTarget, s.LastAttack));
        Assert.Empty(sent);
        Assert.Equal(0u, new GameSession(new LoginSettings()).TargetNearest());
    }

    // ---- abilities, bow, salute, open door ----

    [Fact]
    public void AbilitySlotsUseTheWornWeaponsPair()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        Assert.True(s.UseWeaponAbilitySlot(false)); // bare hands: Paralyzing Blow
        Assert.Equal(GameSession.CombatAbilityRequest(Me, WeaponAbilities.ParalyzingBlow), sent[^1]);
        s.Inject(new PacketWriter(0x2E, 15).UInt32(0x40000001).UInt16(0x0F43).Byte(0).Byte(1).UInt32(Me).UInt16(0).ToArray(), false);
        sent.Clear();

        Assert.True(s.UseWeaponAbilitySlot(true)); // a hatchet: Disarm
        Assert.Equal(GameSession.CombatAbilityRequest(Me, WeaponAbilities.Disarm), Assert.Single(sent));
        Assert.True(s.IsWeaponAbilityOn(true));
    }

    [Fact]
    public void AbilityNotInWorldOrPreAosSendsNothing()
    {
        Assert.False(new GameSession(new LoginSettings()).UseWeaponAbilitySlot(false));
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent, features: 0x08);
        Assert.False(s.UseWeaponAbilitySlot(true));
        Assert.Empty(sent);
    }

    [Fact]
    public void BowSaluteAndOpenDoorAreTextCommands()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        s.Bow();
        s.Salute();
        s.OpenDoor();
        Assert.Equal(new byte[] { 0x12, 0, 8, 0xC7, (byte)'b', (byte)'o', (byte)'w', 0 }, sent[0]);
        Assert.Equal(new PacketWriter(0x12).Byte(0xC7).AsciiZ("salute").ToArray(), sent[1]);
        Assert.Equal(new byte[] { 0x12, 0, 5, 0x58, 0 }, sent[2]);

        var off = new GameSession(new LoginSettings());
        off.Bow(); // not in the world: nothing, no throw
        off.OpenDoor();
    }

    // ---- arguments ----

    [Theory]
    [InlineData("primary", true, false)]
    [InlineData(" PRIMARY ", true, false)]
    [InlineData("1", true, false)]
    [InlineData("Secondary", true, true)]
    [InlineData("2", true, true)]
    [InlineData("", false, false)]
    [InlineData("3", false, false)]
    [InlineData("tertiary", false, false)]
    [InlineData(null, false, false)]
    public void AbilityArgument(string? arg, bool ok, bool secondary)
    {
        Assert.Equal(ok, MacroArgs.TryParseAbility(arg, out bool s));
        Assert.Equal(secondary, s);
    }

    [Theory]
    [InlineData("500", true, 500)]
    [InlineData(" 0 ", true, 0)]
    [InlineData("30000", true, 30000)]
    [InlineData("30001", false, 0)]
    [InlineData("-5", false, 0)]
    [InlineData("1.5", false, 0)]
    [InlineData("soon", false, 0)]
    [InlineData("", false, 0)]
    public void DelayArgument(string arg, bool ok, int ms)
    {
        Assert.Equal(ok, MacroArgs.TryParseDelay(arg, out int got));
        if (ok)
            Assert.Equal(ms, got);
    }

    [Theory]
    [InlineData("delay 500", true, "500")]
    [InlineData("WAIT  250 ", true, "250")]
    [InlineData("Delay", true, "")]
    [InlineData("delayed 5", false, "")]
    [InlineData("say delay 5", false, "")]
    public void DelayLines(string action, bool isDelay, string arg)
    {
        Assert.Equal(isDelay, MacroArgs.IsDelay(action, out string a));
        if (isDelay)
            Assert.Equal(arg, a);
    }

    [Theory]
    [InlineData("attacklast")]
    [InlineData("targetnext")]
    [InlineData("targetnearest")]
    [InlineData("lastspell")]
    [InlineData("lastskill")]
    [InlineData("ability primary")]
    [InlineData("bow")]
    [InlineData("salute")]
    [InlineData("opendoor")]
    [InlineData("delay 500")]
    [InlineData("wait 500")]
    public void NewActionsDoNotWaitForACursor(string action) => Assert.False(MacroRecorder.IsTargetAction(action));

    [Fact]
    public void TheEditorKnowsTheNewActions()
    {
        foreach (string a in new[] { "attacklast", "targetnext", "targetnearest", "lastspell", "lastskill", "bow", "salute", "opendoor",
                     "AttackLast", "ability primary", "ability 2", "delay 500", "wait 100" })
            Assert.Null(MacroDraft.Problem(new MacroDraft.Row { Key = "Ctrl+F1", Action = a }));
        foreach (string a in new[] { "ability", "delay", "wait" })
            Assert.Contains("needs something", MacroDraft.Problem(new MacroDraft.Row { Key = "F1", Action = a }));
    }

    // ---- Hotkeys.Run (Unity-only, read from the source) ----

    [Theory]
    [InlineData("attacklast", "session.AttackLast()")]
    [InlineData("targetnext", "session.TargetNext()")]
    [InlineData("targetnearest", "session.TargetNearest()")]
    [InlineData("lastspell", "session.CastLastSpell()")]
    [InlineData("lastskill", "session.UseLastSkill()")]
    [InlineData("ability", "session.UseWeaponAbilitySlot(secondary)")]
    [InlineData("bow", "session.Bow()")]
    [InlineData("salute", "session.Salute()")]
    [InlineData("opendoor", "session.OpenDoor()")]
    [InlineData("delay", "MacroArgs.TryParseDelay(arg")]
    [InlineData("wait", "MacroArgs.TryParseDelay(arg")]
    public void RunDispatchesEachNewVerb(string verb, string call)
    {
        string run = RunBody();
        int at = run.IndexOf($"case \"{verb}\":", StringComparison.Ordinal);
        Assert.True(at >= 0, verb);
        int end = run.IndexOf("break;", at, StringComparison.Ordinal);
        Assert.Contains(call, run.Substring(at, end - at));
    }

    [Fact]
    public void NoNewActionHasADefaultKey()
    {
        string src = HotkeysSource();
        int start = src.IndexOf("Binding[] Defaults", StringComparison.Ordinal);
        string defaults = src.Substring(start, src.IndexOf("};", start, StringComparison.Ordinal) - start);
        foreach (string verb in new[] { "attacklast", "targetnext", "targetnearest", "lastspell", "lastskill", "ability", "bow", "salute", "opendoor", "delay", "wait" })
            Assert.DoesNotMatch($"action = \"{verb}\\b", defaults);
    }

    [Fact]
    public void PlaybackPausesOnDelayAndANewSessionDropsTheQueue()
    {
        string src = HotkeysSource();
        int pump = src.IndexOf("private void Pump(", StringComparison.Ordinal);
        string body = src.Substring(pump, src.IndexOf("private void QueueTarget", pump, StringComparison.Ordinal) - pump);
        Assert.Contains("MacroArgs.IsDelay(next", body);
        Assert.Contains("_playbackDelayUntil", body);
        Assert.Matches(@"if \(!ReferenceEquals\(session, _session\)\)\s*\{\s*_session = session;\s*_playback\.Clear\(\);", src);
    }

    private static string RunBody()
    {
        string src = HotkeysSource();
        int run = src.IndexOf("public void Run(", StringComparison.Ordinal);
        return src.Substring(run, src.IndexOf("private void ToggleRecording", run, StringComparison.Ordinal) - run);
    }

    private static string HotkeysSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, "Assets", "UOClient.Unity", "Runtime", "Hotkeys.cs");
            if (File.Exists(p))
                return Regex.Replace(File.ReadAllText(p), "\r\n", "\n");
        }
        throw new FileNotFoundException("Hotkeys.cs");
    }
}
