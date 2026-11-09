using UOClient.Core;

namespace UOClient.Tests;

/// <summary>Seats (ClassicUO's chair table) and the facing a sitter takes.</summary>
public class SeatsTests
{
    [Fact]
    public void Table_HasTheCommonSeats()
    {
        Assert.True(Seats.Count > 150);
        Assert.True(Seats.TryGet(0x0B5F, out _)); // Britain's benches
        Assert.True(Seats.TryGet(0x0459, out _)); // a marble bench
        Assert.False(Seats.TryGet(0x0001, out _));
    }

    [Fact]
    public void Facing_TakesTheSeatsDirection_OrANeighboursWhenItHasNone()
    {
        Assert.True(Seats.TryGet(0x0B5F, out Seats.Seat bench)); // -1, 2, -1, 6: faces east or west
        Assert.Equal(2, Seats.Facing(bench, 1));
        Assert.Equal(2, Seats.Facing(bench, 2));
        Assert.Equal(6, Seats.Facing(bench, 5));
        Assert.Equal(6, Seats.Facing(bench, 6));
        Assert.Equal(2, Seats.Facing(bench, 0)); // north: none, so the east neighbour
        Assert.Equal(6, Seats.Facing(bench, 7)); // north-west: the west one
    }
}
