using UOClient.Core;

namespace UOClient.Tests;

/// <summary>Other characters block the player's step by the classic shove rule (Shove).</summary>
public class ShoveTests
{
    private static World WorldWith(int stamina, int staminaMax, ushort playerBody = 0x190, ushort otherBody = 0x190, int otherZ = 0)
    {
        var w = new World();
        w.Mobiles[1] = new Mobile { Serial = 1, Body = playerBody, X = 10, Y = 10, Z = 0, Stamina = stamina, StaminaMax = staminaMax };
        w.Mobiles[2] = new Mobile { Serial = 2, Body = otherBody, X = 11, Y = 10, Z = (sbyte)otherZ };
        typeof(World).GetProperty(nameof(World.PlayerSerial))!.SetValue(w, 1u);
        return w;
    }

    [Fact]
    public void BelowFullStamina_SomeoneBlocks() => Assert.NotNull(Shove.Blocker(WorldWith(50, 100), 11, 10, 0));

    [Fact]
    public void FullStamina_ShovesThrough() => Assert.Null(Shove.Blocker(WorldWith(100, 100), 11, 10, 0));

    [Fact]
    public void Ghosts_PassAndArePassed()
    {
        Assert.Null(Shove.Blocker(WorldWith(50, 100, playerBody: 0x192), 11, 10, 0)); // the player is a ghost
        Assert.Null(Shove.Blocker(WorldWith(50, 100, otherBody: 0x192), 11, 10, 0));  // the other is
    }

    [Fact]
    public void AGmBody_Passes() => Assert.Null(Shove.Blocker(WorldWith(50, 100, playerBody: Shove.GmBody), 11, 10, 0));

    [Fact]
    public void SomeoneOnAnotherFloor_DoesNotBlock() => Assert.Null(Shove.Blocker(WorldWith(50, 100, otherZ: 20), 11, 10, 0));

    [Fact]
    public void AnEmptyTile_IsFree() => Assert.Null(Shove.Blocker(WorldWith(50, 100), 12, 10, 0));
}
