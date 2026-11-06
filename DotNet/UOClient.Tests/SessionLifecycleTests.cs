using UOClient.Core;
using UOClient.Net;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// A session's end at each stage (connecting, the character list, the world), logging out, cancelling, and what the
/// game's <see cref="SessionLifecycle"/> makes of it: the message for the login screen, and handlers of an ended
/// session going quiet. No server: sessions run offline and the server's packets are injected.
/// </summary>
public sealed class SessionLifecycleTests
{
    private const uint Me = 0x00000042;
    private const string Password = "s3cret-pass";

    private static GameSession Connecting(List<byte[]> sent, bool interactive = true)
    {
        var session = new GameSession(new LoginSettings { Account = "tester", Password = Password, Interactive = interactive, AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        return session;
    }

    private static GameSession InWorld(List<byte[]> sent)
    {
        GameSession session = Connecting(sent);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        sent.Clear();
        return session;
    }

    // ModernUO's CharacterList: slots (name, password), cities, flags.
    private static byte[] CharacterList(params string[] names)
    {
        PacketWriter w = new PacketWriter(0xA9).Byte(names.Length);
        foreach (string n in names)
            w.Ascii(n, 30).Zero(30);
        return w.Byte(0).UInt32(0).ToArray();
    }

    // ---- disconnect at each stage ----

    [Fact]
    public void DropWhileConnectingEndsTheSessionWithTheConnectionsReason()
    {
        var life = new SessionLifecycle();
        GameSession session = Connecting(new List<byte[]>());
        life.Begin(session);
        Assert.False(life.HasEnded);

        session.SimulateConnectionLost("connection lost: refused");
        session.Update(1);

        Assert.Equal(SessionState.Disconnected, session.State);
        Assert.True(life.HasEnded);
        Assert.Equal("Disconnected: connection lost: refused", life.End());
        Assert.Null(life.Session);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public void LoginDeniedEndsTheSessionWithTheServersReasonAndNoPassword()
    {
        var life = new SessionLifecycle();
        GameSession session = Connecting(new List<byte[]>());
        life.Begin(session);

        session.Inject(new byte[] { 0x82, 3 }, false);

        Assert.True(life.HasEnded);
        string message = life.End();
        Assert.Equal("Login denied: bad password", message);
        Assert.DoesNotContain(Password, message);
    }

    [Fact]
    public void DropAtTheCharacterListEndsTheSession()
    {
        var life = new SessionLifecycle();
        GameSession session = Connecting(new List<byte[]>());
        life.Begin(session);
        bool listed = false;
        session.CharactersListed += life.Guard(() => listed = true);

        session.Inject(CharacterList("Wes", ""), true);
        Assert.True(listed);
        Assert.Equal(SessionState.CharacterSelect, session.State);
        Assert.False(life.HasEnded);

        session.SimulateConnectionLost("connection lost: reset by peer");
        session.Update(1);

        Assert.True(life.HasEnded);
        Assert.Equal("Disconnected: connection lost: reset by peer", life.End());
    }

    [Fact]
    public void DropInTheWorldEndsTheSessionAsADisconnectNotALogout()
    {
        var life = new SessionLifecycle();
        GameSession session = InWorld(new List<byte[]>());
        life.Begin(session);

        session.SimulateConnectionLost("connection lost: timed out");
        session.Update(1);

        Assert.True(life.HasEnded);
        Assert.False(session.LoggedOut);
        string message = life.End();
        Assert.Equal("Disconnected: connection lost: timed out", message);
        Assert.DoesNotContain(Password, message);
    }

    // ---- logging out ----

    [Fact]
    public void LogoutSendsOneRequestAndTheAckEndsTheSessionAsLoggedOut()
    {
        var sent = new List<byte[]>();
        var life = new SessionLifecycle();
        GameSession session = InWorld(sent);
        life.Begin(session);

        session.Logout();
        session.Logout(); // a second click: nothing more goes out
        Assert.Single(sent, p => p[0] == 0xD1);
        Assert.True(session.LogoutRequested);
        Assert.False(life.HasEnded);

        session.Inject(new byte[] { 0xD1, 0x01 }, false);

        Assert.True(session.LoggedOut);
        Assert.Equal(SessionState.Disconnected, session.State);
        Assert.True(life.HasEnded);
        Assert.Equal(GameSession.LoggedOutStatus, life.End());
    }

    [Fact]
    public void TheServerClosingAfterALogoutRequestIsStillALogout()
    {
        var life = new SessionLifecycle();
        GameSession session = InWorld(new List<byte[]>());
        life.Begin(session);

        session.Logout();
        session.SimulateConnectionLost("closed by remote host");
        session.Update(1);

        Assert.True(session.LoggedOut);
        Assert.Equal(GameSession.LoggedOutStatus, session.Status);
        Assert.Equal(GameSession.LoggedOutStatus, life.End());
    }

    [Fact]
    public void LogoutBeforeTheWorldOrAfterTheEndSendsNothing()
    {
        var sent = new List<byte[]>();
        GameSession connecting = Connecting(sent);
        connecting.Logout();
        Assert.DoesNotContain(sent, p => p[0] == 0xD1);
        Assert.False(connecting.LogoutRequested);

        GameSession world = InWorld(sent);
        world.Dispose();
        world.Logout(); // no connection left: no throw, nothing sent
        Assert.Empty(sent);
    }

    // ---- the client ending it ----

    [Fact]
    public void CancelledByTheClientEndsTheSessionWithNoMessage()
    {
        var life = new SessionLifecycle();
        GameSession session = Connecting(new List<byte[]>());
        life.Begin(session);

        session.Dispose(); // the login screen's Cancel (or the character page's Log out) says why itself
        session.Update(1);

        Assert.True(life.HasEnded);
        Assert.Null(life.End());
    }

    [Fact]
    public void ADisposedSessionHandlesAndRaisesNothing()
    {
        GameSession session = InWorld(new List<byte[]>());
        int events = 0;
        session.StatusChanged += _ => events++;
        session.World.JournalAdded += _ => events++;
        session.Dispose();

        session.Inject(new byte[] { 0xD1, 0x01 }, false);
        session.Inject(new PacketWriter(0x1C).UInt32(0xFFFFFFFF).UInt16(0).Byte(0).UInt16(0).UInt16(3).Ascii("System", 30).AsciiZ("hello").ToArray(), true);
        session.Update(5);

        Assert.Equal(0, events);
        Assert.False(session.LoggedOut);
        Assert.Equal(SessionState.InWorld, session.State);
    }

    // ---- stale handlers ----

    [Fact]
    public void GuardedHandlersGoQuietWhenTheirSessionEnds()
    {
        var life = new SessionLifecycle();
        life.Begin(Connecting(new List<byte[]>()));
        int plain = 0, one = 0, four = 0;
        Action a = life.Guard(() => plain++);
        Action<string> b = life.Guard<string>(_ => one++);
        Action<int, int, sbyte, byte> c = life.Guard<int, int, sbyte, byte>((_, _, _, _) => four++);
        a();
        b("x");
        c(1, 2, 3, 4);
        Assert.Equal((1, 1, 1), (plain, one, four));

        life.End();
        a();
        b("x");
        c(1, 2, 3, 4);
        Assert.Equal((1, 1, 1), (plain, one, four));
    }

    [Fact]
    public void ANewSessionSilencesTheOldSessionsHandlersButNotItsOwn()
    {
        var life = new SessionLifecycle();
        GameSession first = Connecting(new List<byte[]>());
        life.Begin(first);
        var seen = new List<string>();
        Action<string> firstHandler = life.Guard<string>(s => seen.Add("first:" + s));
        first.StatusChanged += firstHandler;

        GameSession second = Connecting(new List<byte[]>());
        life.Begin(second); // ends the first
        second.StatusChanged += life.Guard<string>(s => seen.Add("second:" + s));

        Assert.True(first.IsDisposed);
        Assert.Same(second, life.Session);
        firstHandler("late"); // e.g. a closure kept by a window of the old session
        second.Inject(new byte[] { 0x82, 0 }, false);

        Assert.Equal(new[] { "second:Login denied: unknown account or password" }, seen);
    }

    // ---- repeated and idempotent ----

    [Fact]
    public void EndIsIdempotent()
    {
        var life = new SessionLifecycle();
        Assert.Null(life.End()); // nothing begun
        Assert.False(life.HasEnded);

        GameSession session = InWorld(new List<byte[]>());
        life.Begin(session);
        int generation = life.Generation;
        session.Inject(new byte[] { 0xD1, 0x01 }, false);

        Assert.Equal(GameSession.LoggedOutStatus, life.End());
        Assert.Null(life.End());
        Assert.Null(life.End());
        Assert.Equal(generation + 1, life.Generation);
        Assert.False(life.HasEnded);
    }

    [Fact]
    public void RepeatedLoginAndLogoutCyclesEachEndCleanly()
    {
        var life = new SessionLifecycle();
        var entered = new List<int>();
        for (int round = 0; round < 3; round++)
        {
            var sent = new List<byte[]>();
            GameSession session = Connecting(sent);
            life.Begin(session);
            int r = round;
            session.Entered += life.Guard(() => entered.Add(r));
            session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
                .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
            session.Inject(new byte[] { 0x55 }, false);
            Assert.Equal(SessionState.InWorld, session.State);

            session.Logout();
            Assert.Single(sent, p => p[0] == 0xD1);
            session.Inject(new byte[] { 0xD1, 0x01 }, false);

            Assert.True(life.HasEnded);
            Assert.Equal(GameSession.LoggedOutStatus, life.End());
            Assert.Null(life.Session);
            Assert.True(session.IsDisposed);
        }
        Assert.Equal(new[] { 0, 1, 2 }, entered);
    }

    [Fact]
    public void ADroppedSessionThenAFreshLoginStartsClean()
    {
        var life = new SessionLifecycle();
        GameSession dropped = InWorld(new List<byte[]>());
        life.Begin(dropped);
        int oldJournal = 0;
        dropped.World.JournalAdded += life.Guard<JournalEntry>(_ => oldJournal++);
        dropped.SimulateConnectionLost("connection lost");
        dropped.Update(1);
        Assert.StartsWith("Disconnected:", life.End());

        GameSession fresh = Connecting(new List<byte[]>());
        life.Begin(fresh);
        Assert.False(life.HasEnded);
        Assert.False(fresh.LoggedOut);
        Assert.False(fresh.LogoutRequested);
        Assert.Equal(SessionState.GameLogin, fresh.State);
        // Nothing of the old session runs into the new one.
        dropped.Inject(new PacketWriter(0x1C).UInt32(0xFFFFFFFF).UInt16(0).Byte(0).UInt16(0).UInt16(3).Ascii("System", 30).AsciiZ("late").ToArray(), true);
        Assert.Equal(0, oldJournal);
    }

    [Fact]
    public void BeginNeedsASession()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionLifecycle().Begin(null!));
    }

    // ---- the Unity side (MonoBehaviour code, checked as source) ----

    private static string Runtime(params string[] parts)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets", "UOClient.Unity")))
            dir = Path.GetDirectoryName(dir)!;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!, "Assets", "UOClient.Unity", "Runtime" }.Concat(parts).ToArray()));
    }

    [Fact]
    public void TheGameReturnsToLoginOnEveryEndAndNeverQuitsOnLogout()
    {
        string demo = Runtime("WorldDemo.cs");
        Assert.Contains("if (_life.HasEnded)\n                ReturnToLogin();", demo.Replace("\r\n", "\n"));
        Assert.DoesNotContain("_quitAfterLogout", demo);
        Assert.DoesNotContain("LogoutConfirmed", demo);
        int ret = demo.IndexOf("private void ReturnToLogin()", StringComparison.Ordinal);
        Assert.True(ret > 0);
        string body = demo.Substring(ret, demo.IndexOf("\n        }", ret, StringComparison.Ordinal) - ret);
        Assert.Contains("TearDownSession()", body);
        Assert.Contains("StartLoginMedia()", body);
        Assert.Contains("_login.Failed(message)", body);
        Assert.DoesNotContain("Quit()", body);
        Assert.DoesNotContain("StartOnline", body); // no reconnecting by itself

        int tear = demo.IndexOf("private string TearDownSession()", StringComparison.Ordinal);
        string teardown = demo.Substring(tear, demo.IndexOf("\n        }", tear, StringComparison.Ordinal) - tear);
        foreach (string step in new[] { "_life.End()", "_binder.Dispose()", "_binder = null", "_entities.Reset()", "_effects.Dispose()",
                                        "_overheads.Clear()", "_chatLine.Close()", "_ctx.World = null", "_ctx.Player = null", "_ctx.Session = null" })
            Assert.Contains(step, teardown);
    }

    [Fact]
    public void LogoutConfirmSaysLoginAndVersionFollowsTheServerOnScreen()
    {
        string binder = Runtime("World", "SessionUiBinder.cs");
        Assert.Contains("\"Return to the login screen?\"", binder);
        Assert.DoesNotContain("return to the desktop", binder);

        string flow = Runtime("World", "LoginFlow.cs");
        Assert.Contains("ReadVersion(screen.Host)", flow);
        Assert.DoesNotContain("ReadVersion(_settings.host)", flow);
    }

    [Fact]
    public void LoginAndLoadingTextHasNoMojibake()
    {
        foreach (string[] file in new[] { new[] { "WorldDemo.cs" }, new[] { "UI", "LoginScreen.cs" } })
        {
            string text = Runtime(file);
            Assert.DoesNotContain("â€", text); // UTF-8 dashes and ellipses read as Windows-1252
        }
    }
}
