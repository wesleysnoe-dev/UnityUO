using UOClient.Core;

namespace UOClient.Tests;

/// <summary>The hairstylist's three gumps read into services, styles and colours (BarberMenus), as ModernUO lays them out.</summary>
public class BarberMenusTests
{
    private static ServerGump Gump(string layout, params string[] lines) =>
        new ServerGump { Commands = ServerGump.ParseLayout(layout), Lines = lines };

    private const string Services =
        "{page 0}{resizepic 50 10 2600 450 205}{xmfhtmlgump 100 40 350 20 1018356 0 0}" +
        "{xmfhtmlgump 140 75 300 20 1018357 0 0}{button 100 75 4005 4007 1 0 1}" +
        "{htmlgump 140 100 300 20 0 0 0}" +
        "{xmfhtmlgump 140 125 300 20 1018359 0 0}{button 100 125 4005 4007 1 0 3}" +
        "{htmlgump 100 160 350 20 1 0 0}";

    [Fact]
    public void Services_OfferedAndUnaffordableRows_AndTheBankLine()
    {
        BarberMenus.Services menu = BarberMenus.ReadServices(Gump(Services,
            "<BASEFONT COLOR=#808080>New Beard (50000 gold)</BASEFONT>",
            "<BASEFONT COLOR=#606060>Paid from your bank. Your bank holds 120 gold.</BASEFONT>"));
        Assert.NotNull(menu);
        Assert.Equal(new[] { (1018357, 1), (0, 0), (1018359, 3) }, menu.Items.ConvertAll(s => (s.Cliloc, s.Button)));
        Assert.Equal("New Beard (50000 gold)", menu.Items[1].Text);
        Assert.Equal("Paid from your bank. Your bank holds 120 gold.", menu.Footer);
    }

    private const string Styles =
        "{page 0}{resizepic 0 0 2600 450 300}{button 45 240 4005 4007 1 0 1}{xmfhtmlgump 77 240 90 35 1006044 0 0}" +
        "{button 270 240 4005 4007 1 0 0}{xmfhtmlgump 302 240 90 35 1006045 0 0}{xmfhtmlgump 50 15 350 20 1018353 0 0}" +
        "{radio 40 70 208 209 0 0}{resizepic 87 50 2620 50 50}{gumppic 20 10 50700}" +
        "{radio 163 70 208 209 0 1}{resizepic 210 50 2620 50 50}{gumppic 143 8 60710}" +
        "{radio 286 240 208 209 0 2}{xmfhtmlgump 306 240 85 35 1011064 0 0}";

    [Fact]
    public void Styles_PicturesWithTheirCrop_AndBald()
    {
        BarberMenus.Styles menu = BarberMenus.ReadStyles(Gump(Styles));
        Assert.NotNull(menu);
        Assert.False(menu.Beard);
        Assert.Equal(new[] { (0, 50700), (1, 60710), (2, 0) }, menu.Items.ConvertAll(s => (s.Switch, s.Gump)));
        // The server's 50 x 50 box at (87, 50) over a picture drawn at (20, 10): its middle is (92, 65) in the picture.
        Assert.Equal((92, 65), (menu.Items[0].CropX, menu.Items[0].CropY));
        Assert.Equal("Short", BarberMenus.StyleName(menu.Items[0].Gump));
        Assert.Equal("Pageboy", BarberMenus.StyleName(menu.Items[1].Gump));
        Assert.Equal("Bald", BarberMenus.StyleName(menu.Items[2].Gump));
        Assert.Equal("Buns", BarberMenus.StyleName(60712));
        // The picture drawn: the stock list's wrong numbers put right, from the body's own set.
        Assert.Equal(50701, BarberMenus.PictureFor(60708, female: false)); // long
        Assert.Equal(60902, BarberMenus.PictureFor(60707, female: true));  // pigtails
        Assert.Equal(50903, BarberMenus.PictureFor(60713, female: false)); // topknot
        Assert.Equal(60700, BarberMenus.PictureFor(50700, female: true));
        Assert.Equal(0, BarberMenus.PictureFor(0, female: true));
    }

    private const string Colours =
        "{page 0}{resizepic 100 10 2600 350 370}{resizepic 120 54 5100 110 270}{xmfhtmlgump 155 25 240 30 1011013 0 0}" +
        "{xmfhtmlgump 150 330 220 35 1011014 0 0}{button 380 330 4005 4007 1 0 1}" +
        "{text 130 59 1601 0}{button 207 60 5224 5224 0 1 0}{text 130 81 1627 0}{button 207 82 5224 5224 0 2 0}" +
        "{page 1}{text 278 52 1601 0}{radio 260 52 210 211 0 0}{text 278 69 1602 0}{radio 260 69 210 211 0 2}" +
        "{page 2}{text 278 52 1627 0}{radio 260 52 210 211 0 1}";

    [Fact]
    public void Colours_FamiliesAndTheirShades()
    {
        BarberMenus.Colours menu = BarberMenus.ReadColours(Gump(Colours, "*****"));
        Assert.NotNull(menu);
        Assert.Equal(new[] { (1, 1602), (2, 1628) }, menu.Groups.ConvertAll(g => (g.Page, g.Hue)));
        Assert.Equal(new[] { (0, 1602), (2, 1603) }, menu.Groups[0].Shades.ConvertAll(s => (s.Switch, s.Hue)));
        Assert.Equal(new[] { (1, 1628) }, menu.Groups[1].Shades.ConvertAll(s => (s.Switch, s.Hue)));
    }

    [Fact]
    public void EachReader_TakesOnlyItsOwnGump()
    {
        Assert.Null(BarberMenus.ReadServices(Gump(Styles)));
        Assert.Null(BarberMenus.ReadServices(Gump(Colours, "*****")));
        Assert.Null(BarberMenus.ReadStyles(Gump(Services, "a", "b")));
        Assert.Null(BarberMenus.ReadStyles(Gump(Colours, "*****")));
        Assert.Null(BarberMenus.ReadColours(Gump(Services, "a", "b")));
        Assert.Null(BarberMenus.ReadColours(Gump(Styles)));
        Assert.Null(MoongateMenu.TryRead(ServerGump.ParseLayout(Styles)));
        // A control these menus do not have leaves the gump as the server sent it.
        Assert.Null(BarberMenus.ReadStyles(Gump(Styles + "{textentry 10 10 100 20 0 5 0}")));
    }
}
