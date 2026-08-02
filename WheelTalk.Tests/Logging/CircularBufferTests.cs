using WheelTalk.Core.Logging;

namespace WheelTalk.Tests.Logging;

public class CircularBufferTests
{
    [Fact]
    public void Overflow_keeps_the_last_capacity_items_in_order()
    {
        var buffer = new CircularBuffer<int>(512);
        for (int i = 0; i < 600; i++) buffer.Add(i);

        var snapshot = buffer.Snapshot();

        Assert.Equal(512, snapshot.Length);
        Assert.Equal(512, buffer.Count);
        Assert.Equal(Enumerable.Range(88, 512), snapshot); // items 0..87 were pushed out
    }

    [Fact]
    public void Partial_fill_returns_exactly_what_was_written_with_no_gaps()
    {
        var buffer = new CircularBuffer<int>(16);
        for (int i = 0; i < 10; i++) buffer.Add(i);

        var snapshot = buffer.Snapshot();

        Assert.Equal(10, buffer.Count);
        Assert.Equal(Enumerable.Range(0, 10), snapshot);
    }

    [Fact]
    public void An_empty_buffer_snapshots_to_nothing()
    {
        var buffer = new CircularBuffer<int>(8);

        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.Snapshot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-8)]
    [InlineData(3)]
    [InlineData(100)]
    [InlineData(511)]
    public void Non_power_of_two_capacity_is_rejected(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircularBuffer<int>(capacity));
    }

    [Fact]
    public void Clear_empties_the_buffer_and_it_refills_from_the_start()
    {
        var buffer = new CircularBuffer<int>(4);
        for (int i = 0; i < 6; i++) buffer.Add(i);

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.Snapshot());

        buffer.Add(42);
        Assert.Equal([42], buffer.Snapshot());
    }

    [Fact]
    public void A_second_snapshot_after_the_first_still_sees_the_same_frames()
    {
        // The whole point of a non-destructive read: collecting the debug report by hand and then
        // hitting a crash a minute later must both see the ring, not just the first one.
        var buffer = new CircularBuffer<int>(4);
        buffer.Add(1);
        buffer.Add(2);

        var first = buffer.Snapshot();
        var second = buffer.Snapshot();

        Assert.Equal(first, second);
        Assert.Equal([1, 2], second);
    }
}
