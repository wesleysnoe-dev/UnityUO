using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// Ghosts draw as TazUO draws them: <see cref="AnimationLibrary.AnimationBody"/> is <c>Mobile.GetGraphicForAnimation</c>
/// (MobileAnimation.cs:53-81), and a dead mobile skips hair, beard (MobileView.cs:372) and mount (:172).
/// </summary>
public sealed class GhostVisibilityTests
{
    [Theory]
    [InlineData(402, 400)]
    [InlineData(403, 401)]
    [InlineData(607, 605)]
    [InlineData(608, 606)]
    [InlineData(694, 667)] // 0x2B6 -> 667, as TazUO has it
    [InlineData(695, 666)] // 0x2B7 -> 666
    public void GhostBodiesDrawAsTazUOsGraphicForAnimation(int ghost, int drawn)
    {
        Assert.Equal(drawn, AnimationLibrary.AnimationBody(ghost));
        Assert.True(AnimationLibrary.IsGhostBody(ghost));
        Assert.True(Mobile.IsGhostBody((ushort)ghost)); // Core's dead state agrees for every remapped body
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(605)]
    [InlineData(666)]
    [InlineData(970)] // a ghost to ModernUO's Body.IsGhost, but a monster body with its own art: not remapped (TazUO neither)
    [InlineData(0x00C8)]
    [InlineData(0)]
    public void OtherBodiesAreThemselves(int body)
    {
        Assert.Equal(body, AnimationLibrary.AnimationBody(body));
        Assert.False(AnimationLibrary.IsGhostBody(body));
    }
}

/// <summary>Ghost looks against the installed 7.0.23.1 data (fails, never skips, without it).</summary>
[Collection("UO install")]
public sealed class GhostVisibilityInstallTests
{
    private readonly UOData _data;

    public GhostVisibilityInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    private const int Skin = 0x83EA;
    private static readonly (byte, int, int) DeathRobe = (22, 0x204E, 0);   // ModernUO DeathRobe on OuterTorso
    private static readonly (byte, int, int) Hair = (11, 0x203B, 0x044E);
    private static readonly (byte, int, int) Beard = (16, 0x2040, 0x044E);
    private static readonly (byte, int, int) Horse = (25, 0x3EA2, 0);

    [Theory]
    [InlineData(402)]
    [InlineData(403)]
    public void TheGhostBodiesHaveNoArtOfTheirOwn(int ghost)
    {
        // The audit's premise, on the real files: anim.idx holds nothing for 402/403 and mobtypes keeps them off UOP.
        Assert.False(_data.AnimationLibrary.UsesUop(ghost));
        for (int action = 0; action < 35; action++)
            for (int dir = 0; dir < 5; dir++)
                Assert.Empty(_data.AnimationLibrary.Read(ghost, action, dir));
    }

    [Theory]
    [InlineData(402, 400)]
    [InlineData(403, 401)]
    [InlineData(607, 605)]
    [InlineData(608, 606)]
    public void AGhostDrawsTheLivingBodyInItsRobeWithoutHairBeardOrMount(int ghost, int living)
    {
        MobileLook look = MobileLook.From(_data, ghost, Skin, new[] { DeathRobe, Hair, Beard, Horse });

        Assert.Equal(ghost, look.Body);                       // the body as sent, for server actions
        Assert.False(look.Mounted);
        Assert.DoesNotContain(look.Layers, l => l.IsMount);
        Assert.DoesNotContain(look.Layers, l => l.Slot == 11 || l.Slot == 16);
        MobileLook.Layer body = Assert.Single(look.Layers, l => l.Slot == 0);
        Assert.Equal(living, body.Body);
        Assert.Equal(Skin & 0x3FFF, body.Hue);               // hue as sent: no 0x0386 for a human ghost
        Assert.Contains(look.Layers, l => l.Slot == 22);
        Assert.Equal(AnimGroup.Human, look.Group);
        Assert.Equal((0, 2, 4), look.BodyActions);

        // Every drawn layer has frames for the stand, walk and run it will play (MobileArt draws nothing without the body).
        foreach (MobileLook.Layer l in look.Layers)
            foreach (int action in new[] { look.BodyActions.Stand, look.BodyActions.Walk, look.BodyActions.Run })
                for (int dir = 0; dir < 5; dir++)
                    Assert.NotEmpty(_data.AnimationLibrary.Read(l.Body, action, dir));
    }

    [Fact]
    public void TheLivingKeepHairBeardAndMount()
    {
        MobileLook look = MobileLook.From(_data, 400, Skin, new[] { DeathRobe, Hair, Beard, Horse });
        Assert.True(look.Mounted);
        Assert.Contains(look.Layers, l => l.IsMount);
        Assert.Contains(look.Layers, l => l.Slot == 11);
        Assert.Contains(look.Layers, l => l.Slot == 16);
        Assert.Equal(400, Assert.Single(look.Layers, l => l.Slot == 0 && !l.IsMount).Body);
    }

    [Fact]
    public void AGhostSharesFramesWithTheSameLivingLook()
    {
        // Same drawn layers and hues: same key, so the frame cache serves both.
        MobileLook ghost = MobileLook.From(_data, 402, Skin, new[] { DeathRobe, Hair });
        MobileLook living = MobileLook.From(_data, 400, Skin, new[] { DeathRobe });
        Assert.Equal(living.Key, ghost.Key);
    }

    [Fact]
    public void EquipConvStaysKeyedOnTheBodyAsSent()
    {
        // TazUO looks EquipConversions up by Graphic, the network body (MobileView.cs:400-412), not the drawn one.
        // Equipconv.def converts some male armour for body 401; find one such worn item in the install's tiledata.
        int graphic = -1, converted = -1, anim = -1;
        for (int g = 0; g < 0x4000 && graphic < 0; g++)
        {
            int a = _data.TileData.GetStatic(g).AnimId;
            if (a != 0 && _data.TryEquipConv(401, a, out EquipConvEntry e) && e.Anim != a && !_data.TryEquipConv(403, a, out _))
                (graphic, anim, converted) = (g, a, e.Anim);
        }
        Assert.True(graphic >= 0, "Equipconv.def has no 401 entry reachable from tiledata");

        var worn = new[] { ((byte)13, graphic, 0) };
        Assert.Contains(MobileLook.From(_data, 401, Skin, worn).Layers, l => l.Slot == 13 && l.Body == converted);
        Assert.Contains(MobileLook.From(_data, 403, Skin, worn).Layers, l => l.Slot == 13 && l.Body == anim);
    }

    [Fact]
    public void AGhostsServerActionsResolveOnTheLivingBody()
    {
        AnimationLibrary anims = _data.AnimationLibrary;
        for (int action = 0; action < 35; action++)
        {
            Assert.Equal(anims.ServerAction(400, action, false, false), anims.ServerAction(402, action, false, false));
            Assert.Equal(anims.ServerAction(401, action, false, true), anims.ServerAction(403, action, false, true));
        }
    }
}
