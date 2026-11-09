using UOClient.Core;

namespace UOClient.Tests;

/// <summary>The server's speed control (0xBF 0x26, ModernUO SendSpeedControl) as the session reads it.</summary>
public class SpeedControlTests
{
    private static GameSession With(byte mode)
    {
        var s = new GameSession(new LoginSettings());
        s.Inject(new byte[] { 0xBF, 0x00, 0x06, 0x00, 0x26, mode }, true);
        return s;
    }

    [Fact]
    public void Normal_RunsAtNormalSpeed()
    {
        GameSession s = With(0);
        Assert.True(s.MayRun);
        Assert.False(s.FastOnFoot);
        Assert.True(s.MayMove);
    }

    [Fact]
    public void Mount_IsFastOnFoot() => Assert.True(With(1).FastOnFoot); // ModernUO SpeedControlSetting.Mount

    [Fact]
    public void Walk_CannotRun()
    {
        GameSession s = With(2); // ModernUO SpeedControlSetting.Walk
        Assert.False(s.MayRun);
        Assert.True(s.MayMove);
    }

    [Fact]
    public void Four_HoldsThePlayerStill() => Assert.False(With(4).MayMove);

    [Fact]
    public void AnUnknownSubcommand_IsCounted()
    {
        var s = new GameSession(new LoginSettings());
        s.Inject(new byte[] { 0xBF, 0x00, 0x06, 0x00, 0x7E, 0x00 }, true);
        Assert.Equal(1, s.Unhandled[0xBF7E]);
    }
}
