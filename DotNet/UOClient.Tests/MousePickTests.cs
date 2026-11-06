using UOClient.Unity;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// <see cref="MousePick"/>, the one pick the world (EntityViews.PickAt: hover, click, drag start, drop) and the windows'
/// items (container, paperdoll, secure trade) run: what it takes must be what the screen shows under the cursor's
/// hotspot, at every zoom and UI scale, on the pixels WorldSprite.shader draws and nowhere else.
/// </summary>
public sealed class MousePickTests
{
    // ---- the world ----

    /// <summary>An orthographic camera as CameraRig sets it up: orthographicSize = height / 2 / zoom, full-screen viewport.</summary>
    private sealed record Cam(float X, float Y, float Zoom, int ScreenW, int ScreenH)
    {
        public float OrthoSize => ScreenH / 2f / Zoom;

        /// <summary>The display pixel's centre (bottom-left origin) on the world plane: what the GPU samples there.</summary>
        public (float X, float Y) PixelCentre(int col, int row) =>
            (X + (col + 0.5f - ScreenW / 2f) / Zoom, Y + (row + 0.5f - ScreenH / 2f) / Zoom);

        /// <summary>Input.mousePosition with the hotspot on display pixel (col, row from the bottom): its left edge, its top edge.</summary>
        public static (float X, float Y) Mouse(int col, int row) => (col, row + 1);

        public int Pick(IReadOnlyList<MousePick.Sprite> sprites, int col, int row, float cutZ, Func<int, int, int, byte> alpha)
        {
            (float mx, float my) = Mouse(col, row);
            return MousePick.World(sprites, mx, my, X, Y, OrthoSize, 0, 0, ScreenW, ScreenH, cutZ, alpha);
        }
    }

    [Theory]
    [InlineData(1f, 1920, 1080)]
    [InlineData(2f, 1920, 1080)]
    [InlineData(0.5f, 1920, 1080)]
    [InlineData(1.12f, 1280, 721)] // a wheel step, and an odd height: the screen's centre falls inside a pixel
    public void ScreenToWorld_IsTheOrthographicCamerasMapping_AtAnyZoom(float zoom, int w, int h)
    {
        var cam = new Cam(311f, -4207f, zoom, w, h);
        // The screen's centre is the camera's position; one display pixel is 1 / zoom world units, x right and y up.
        Assert.Equal((cam.X, cam.Y), MousePick.ScreenToWorld(w / 2f, h / 2f, cam.X, cam.Y, cam.OrthoSize, 0, 0, w, h));
        var (x, y) = MousePick.ScreenToWorld(w / 2f + 10, h / 2f - 20, cam.X, cam.Y, cam.OrthoSize, 0, 0, w, h);
        Assert.Equal(cam.X + 10 / zoom, x, 3);
        Assert.Equal(cam.Y - 20 / zoom, y, 3);
        // A camera drawing into part of the screen maps from its own viewport.
        var (vx, vy) = MousePick.ScreenToWorld(100 + w / 4f, 50 + h / 4f, cam.X, cam.Y, cam.OrthoSize / 2, 100, 50, w / 2f, h / 2f);
        Assert.Equal(cam.X, vx, 3);
        Assert.Equal(cam.Y, vy, 3);
    }

    [Fact]
    public void PixelCenter_IsHalfAPixelRightAndDown_FromTheMousePosition()
    {
        Assert.Equal((10.5f, 19.5f), MousePick.PixelCenter(10, 20));
        Assert.Equal((0.5f, 0.5f), MousePick.PixelCenter(0, 1)); // the bottom-left pixel
    }

    /// <summary>A 4x4 sprite: alpha 255 in its 2x2 middle, 64 (an HD art's soft edge, which the shader clips) on its rim.</summary>
    private static byte SoftEdged(int x, int y) => x is 1 or 2 && y is 1 or 2 ? (byte)255 : (byte)64;

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(3f)]
    [InlineData(0.5f)]
    [InlineData(1.12f)]
    [InlineData(1f / 1.12f)]
    public void World_PicksExactlyThePixelsTheShaderDraws_AtEveryZoom(float zoom)
    {
        // A soft-edged sprite drawn at the art scale (2 world units per sprite pixel) over a plain one behind it, mirrored.
        var cam = new Cam(0.37f, -0.81f, zoom, 640, 481);
        var sprites = new[]
        {
            new MousePick.Sprite { Left = -6, Bottom = -5, Width = 16, Height = 12, SpriteWidth = 8, SpriteHeight = 6, Depth = 3 },
            new MousePick.Sprite { Left = -3, Bottom = -4, Width = 8, Height = 8, SpriteWidth = 4, SpriteHeight = 4, Mirror = true, Depth = 1 },
        };
        byte Alpha(int i, int x, int y) => i == 0 ? (byte)255 : SoftEdged(x, y);

        // What the GPU draws on a display pixel: the nearest sprite whose texel at the pixel's centre passes clip(a - 0.5).
        int Drawn(int col, int row)
        {
            (float wx, float wy) = cam.PixelCentre(col, row);
            int best = -1;
            for (int i = 0; i < sprites.Length; i++)
            {
                MousePick.Sprite s = sprites[i];
                if (wx < s.Left || wx >= s.Left + s.Width || wy < s.Bottom || wy >= s.Bottom + s.Height)
                    continue;
                int u = (int)((wx - s.Left) / s.Width * s.SpriteWidth), v = (int)((s.Bottom + s.Height - wy) / s.Height * s.SpriteHeight);
                if (s.Mirror)
                    u = s.SpriteWidth - 1 - u;
                if (Alpha(i, u, v) / 255f - 0.5f >= 0 && (best < 0 || s.Depth < sprites[best].Depth))
                    best = i;
            }
            return best;
        }

        int seenFront = 0, seenBack = 0;
        for (int col = 280; col < 360; col++)
            for (int row = 200; row < 280; row++)
            {
                int expect = Drawn(col, row);
                Assert.True(expect == cam.Pick(sprites, col, row, 0, Alpha), $"zoom {zoom}: display pixel ({col}, {row})");
                seenFront += expect == 1 ? 1 : 0;
                seenBack += expect == 0 ? 1 : 0;
            }
        Assert.True(seenFront > 0 && seenBack > 0); // both sprites, and the clipped rim between, were under the sweep
    }

    [Fact]
    public void World_ATexelBelowTheShadersCutoff_IsNotPicked_TheSpriteBehindIs()
    {
        Assert.False(MousePick.Drawn(0));
        Assert.False(MousePick.Drawn(127));
        Assert.True(MousePick.Drawn(128)); // clip(128/255 - 0.5) keeps it
        Assert.True(MousePick.Drawn(255));

        var sprites = new[]
        {
            new MousePick.Sprite { Width = 4, Height = 4, SpriteWidth = 4, SpriteHeight = 4, Depth = 5 },
            new MousePick.Sprite { Width = 4, Height = 4, SpriteWidth = 4, SpriteHeight = 4, Depth = 1 },
        };
        byte Alpha(int i, int x, int y) => i == 0 ? (byte)255 : SoftEdged(x, y);
        Assert.Equal(0, MousePick.World(sprites, 0.5f, 3.5f, 0, Alpha)); // the front one's soft corner: the one behind
        Assert.Equal(1, MousePick.World(sprites, 1.5f, 2.5f, 0, Alpha)); // its solid middle
    }

    [Fact]
    public void World_WhatTheUpperFloorCutHides_IsNotPicked()
    {
        // A chair upstairs (z 20) in front of a bag downstairs (z 0); indoors at z 0 the cut is the floor at z 20.
        var sprites = new[]
        {
            new MousePick.Sprite { Width = 4, Height = 4, SpriteWidth = 4, SpriteHeight = 4, Depth = 5, CutHeight = 1000 },
            new MousePick.Sprite { Width = 4, Height = 4, SpriteWidth = 4, SpriteHeight = 4, Depth = 1, CutHeight = 1020 },
        };
        byte Solid(int i, int x, int y) => 255;
        Assert.Equal(0, MousePick.World(sprites, 2, 2, cutZ: 1020, Solid));
        Assert.Equal(1, MousePick.World(sprites, 2, 2, cutZ: 0, Solid));      // outdoors, nothing cut
        Assert.Equal(1, MousePick.World(sprites, 2, 2, cutZ: 1021, Solid));   // the floor overhead is higher
        Assert.True(MousePick.IsCut(1020, 1020));
        Assert.False(MousePick.IsCut(1020, 0)); // never-cut sprites (land, effects)
    }

    [Fact]
    public void World_AtEqualDepth_TheLaterOneWins_AsTazUOsSelectionAndTheDepthTest()
    {
        var sprites = new[]
        {
            new MousePick.Sprite { Width = 4, Height = 4, SpriteWidth = 4, SpriteHeight = 4, Depth = 2 },
            new MousePick.Sprite { Width = 4, Height = 4, SpriteWidth = 4, SpriteHeight = 4, Depth = 2 },
            new MousePick.Sprite { Width = 4, Height = 4, SpriteWidth = 4, SpriteHeight = 4, Depth = 7 },
        };
        Assert.Equal(1, MousePick.World(sprites, 2, 2, 0, (_, _, _) => 255));
    }

    [Fact]
    public void TieRanks_APileOnOneTile_DrawsAndPicksTheNewestInFront()
    {
        // Three items on one tile at one height, placed in the order 30, 10, 20, and one elsewhere.
        long pile = MousePick.TieKey(100, 7), other = MousePick.TieKey(101, 7);
        var keys = new List<(long Key, long Placed)> { (pile, 30), (pile, 10), (other, 5), (pile, 20) };
        int[] ranks = new int[keys.Count];
        MousePick.TieRanks(keys, new List<int>(), ranks, cap: 8);
        Assert.Equal(new[] { 2, 0, 0, 1 }, ranks);

        // ItemView's depth: its tie level steps it nearer (DepthKey.Step per level), so the newest is drawn and picked.
        const float step = 0.005f;
        var sprites = ranks.Select(r => new MousePick.Sprite { Width = 4, Height = 4, SpriteWidth = 4, SpriteHeight = 4, Depth = 10 - r * step }).ToArray();
        sprites[2].Left = 100; // the other tile's item is elsewhere
        Assert.Equal(0, MousePick.World(sprites, 2, 2, 0, (_, _, _) => 255));

        // More than the tie levels hold: the rest share the last level.
        var many = Enumerable.Range(0, 11).Select(i => (pile, (long)i)).ToList();
        int[] capped = new int[many.Count];
        MousePick.TieRanks(many, new List<int>(), capped, cap: 8);
        Assert.Equal(7, capped.Max());
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 7, 7, 7 }, capped);
        Assert.NotEqual(MousePick.TieKey(100, 7), MousePick.TieKey(100, -7));
    }

    // ---- the windows ----

    /// <summary>
    /// Display pixel (col, row from the top) to the window-local point IMGUI reports there under GUI.matrix = scale:
    /// Input.mousePosition is (col, height - row), UiRouting.ScreenToUi takes it to UI pixels, minus the window's place.
    /// </summary>
    private static (float X, float Y) Local(int col, int row, float scale, (float X, float Y) window, int screenH = 1080)
    {
        (float ux, float uy) = UiRouting.ScreenToUi(col, screenH - row, screenH, scale);
        return (ux - window.X, uy - window.Y);
    }

    /// <summary>The art pixel GUI.DrawTexture shows on display pixel (col, row) for art drawn at local (ox, oy): sampled at the pixel's centre.</summary>
    private static (int X, int Y) Shown(int col, int row, float scale, (float X, float Y) window, float ox, float oy) =>
        ((int)Math.Floor((col + 0.5f) / scale - window.X - ox), (int)Math.Floor((row + 0.5f) / scale - window.Y - oy));

    private static bool OnTexelEdge(float v) => Math.Abs(v - Math.Round(v)) < 1e-3;

    [Theory]
    [InlineData(1f)]
    [InlineData(1.08f)] // UiManager.Scale = Screen.height / 1000 at 1080p
    [InlineData(1.44f)] // at 1440p
    [InlineData(2f)]
    [InlineData(0.75f)]
    public void WindowItem_TakesThePixelTheWindowShows_AtEveryUiScale(float scale)
    {
        // A container at (300, 200) with a 5x4 item at (17, 33), its left column and top row clear.
        var window = (X: 300f, Y: 200f);
        bool Opaque(int x, int y) => x >= 1 && x < 5 && y >= 1 && y < 4;
        int checkedPixels = 0;
        for (int col = (int)((window.X + 14) * scale); col < (int)((window.X + 25) * scale); col++)
            for (int row = (int)((window.Y + 30) * scale); row < (int)((window.Y + 40) * scale); row++)
            {
                if (OnTexelEdge((col + 0.5f) / scale) || OnTexelEdge((row + 0.5f) / scale))
                    continue; // the pixel's centre is on a texel boundary: either texel is what the GPU may sample
                (float lx, float ly) = Local(col, row, scale, window);
                var (i, px, py) = MousePick.WindowItem(1, lx, ly, scale, _ => (17f, 33f), _ => false, (_, x, y) => Opaque(x, y));
                (int sx, int sy) = Shown(col, row, scale, window, 17, 33);
                Assert.True(Opaque(sx, sy) == (i == 0), $"scale {scale}: display pixel ({col}, {row}) shows art ({sx}, {sy})");
                if (i == 0)
                    Assert.Equal((sx, sy), (px, py)); // and the grab offset is that pixel
                checkedPixels++;
            }
        Assert.True(checkedPixels > 20);
    }

    [Fact]
    public void WindowItem_JustLeftOrAboveTheArt_IsOutsideIt()
    {
        // An (int) cast took -0.5 for 0: the column left of the art and the row above it lit its first pixel.
        Assert.Equal((-1, -1), MousePick.UiPixel(9.4f, 19.4f, 10, 20, 1f));
        Assert.Equal(-1, MousePick.WindowItem(1, 9.4f, 25f, 1f, _ => (10f, 20f), _ => false, (_, x, y) => x >= 0 && y >= 0).Index);
        Assert.Equal(0, MousePick.WindowItem(1, 9.6f, 25f, 1f, _ => (10f, 20f), _ => false, (_, x, y) => x >= 0 && y >= 0).Index);
    }

    [Fact]
    public void WindowItem_AStack_IsHitOnEitherCopy_ATransparentPixelFallsThrough()
    {
        // Drawn in order: a bag (solid 10x10 at 0,0), then a stack of 3x3 coins at (2, 2) whose second copy is at (7, 7).
        bool Opaque(int i, int x, int y) => i == 0 ? x >= 0 && y >= 0 && x < 10 && y < 10 : x >= 0 && y >= 0 && x < 3 && y < 3;
        int At(float x, float y) => MousePick.WindowItem(2, x, y, 1f, i => i == 0 ? (0f, 0f) : (2f, 2f), i => i == 1, Opaque).Index;
        Assert.Equal(1, At(3, 3));   // the first copy
        Assert.Equal(1, At(8, 8));   // the second, 5 pixels down-right
        Assert.Equal(0, At(6, 3));   // between the copies: the bag under them
        Assert.Equal(-1, At(11, 11));
        Assert.Equal(5, MousePick.StackOffset);
    }

    [Theory]
    [InlineData(1f, 44, 22, 20, 10)]
    [InlineData(2f, 88, 44, 40, 20)]
    [InlineData(1.08f, 48, 24, 22, 11)]
    [InlineData(1.44f, 63, 32, 29, 14)]
    public void DragCursor_IsTheArtAtTheUiScale_HeldWhereItWasGrabbed(float scale, int w, int h, int hx, int hy)
    {
        // A 44x22 item grabbed at (20, 10) of its art: the cursor shows it at the windows' size, the hotspot on that pixel,
        // so a drop at the hotspot puts the art's top-left where the cursor's picture has it (Drop: DropOrigin).
        Assert.Equal((w, h, hx, hy), MousePick.DragCursor(44, 22, 20, 10, scale));
        Assert.Equal((1, 1, 0, 0), MousePick.DragCursor(1, 1, 5, 5, 0)); // a degenerate scale is 1, the hotspot kept on the art
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.08f)]
    [InlineData(1.44f)]
    [InlineData(2f)]
    [InlineData(0.75f)]
    public void Drop_WhereItWasPicked_PutsTheItemBackInPlace_AtEveryUiScale(float scale)
    {
        // ContainerWindow: press on an item (the grab point is WindowItem's pixel), let go at the same display pixel:
        // DropOrigin gives the item's own place back. The old (int)(local - grab) moved it a pixel up-left wherever the
        // pixel's centre and its corner fell in different UI pixels (fractional scales).
        var window = (X: 300f, Y: 200f);
        var item = (X: 17, Y: 33);
        int drops = 0, oldMoved = 0;
        for (int col = (int)((window.X + item.X) * scale); col < (int)((window.X + item.X + 20) * scale); col++)
            for (int row = (int)((window.Y + item.Y) * scale); row < (int)((window.Y + item.Y + 12) * scale); row++)
            {
                (float lx, float ly) = Local(col, row, scale, window);
                var (i, gx, gy) = MousePick.WindowItem(1, lx, ly, scale, _ => (item.X, item.Y), _ => false, (_, x, y) => x is >= 0 and < 20 && y is >= 0 and < 12);
                if (i != 0)
                    continue;
                Assert.Equal(item, MousePick.DropOrigin(lx, ly, gx, gy, scale));
                if (((int)(lx - gx), (int)(ly - gy)) != item)
                    oldMoved++;
                drops++;
            }
        Assert.True(drops > 100);
        if (scale != 1f && scale != 2f)
            Assert.True(oldMoved > 0); // the bug this replaces
    }

    [Fact]
    public void CentreGrab_DropsTheArtWhereTheCursorPictureShowsIt_ForOddSizes()
    {
        // Lifted from the ground or the paperdoll, an item is held by its middle. Over a window at UI scale 1 the cursor
        // shows the art's top-left HotX pixels left of the mouse pixel; the drop must put it there.
        int oldOff = 0;
        for (int w = 1; w <= 64; w++)
        {
            int g = MousePick.CentreGrab(w);
            int hot = MousePick.DragCursor(w, w, g, g, 1f).HotX;
            for (int d = 100; d < 103; d++)
                Assert.Equal(d - hot, MousePick.DropOrigin(d, d, g, g, 1f).X);
            float half = w / 2f; // the old grab
            if (MousePick.DropOrigin(100, 100, half, half, 1f).X != 100 - MousePick.DragCursor(w, w, half, half, 1f).HotX)
                oldOff++;
        }
        Assert.True(oldOff > 0); // e.g. 23 wide: grab 11.5, hotspot 12, drop 11
    }
}
