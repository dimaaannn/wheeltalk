namespace WheelTalk.Core.Logging;

/// <summary>
/// A fixed-capacity ring: the last <see cref="Capacity"/> items written, oldest to newest, read
/// without disturbing what is still there. Built for one job — <see cref="BleFrameTail"/> — where
/// the write side is a BLE notification callback (must not block, must not allocate) and the read
/// side is a diagnostics report that has to be safe to collect twice (a manual "send debug info"
/// tap followed by a crash a minute later must both see frames).
/// </summary>
public interface ICircularBuffer<T>
{
    int Capacity { get; }

    /// <summary>How many slots actually hold something written — <c>min(written so far, Capacity)</c>.</summary>
    int Count { get; }

    void Add(in T item);

    /// <summary>Drops everything written so far and releases the references held in the slots.
    /// Meant for the writer's own thread — see <see cref="CircularBuffer{T}"/> on why nothing here
    /// is synchronised against a concurrent <see cref="Snapshot"/>.</summary>
    void Clear();

    /// <summary>Copies out the current contents, oldest to newest. Does not remove or mark anything
    /// — the same items are still there for the next call.</summary>
    T[] Snapshot();
}

/// <summary>
/// Port note: this has no original in WheelLog — it is this project's own diagnostics, not a 1:1
/// port of anything, so "Отклонения от оригинала" in AGENTS.md does not apply to it.
/// <para>
/// Lock-free by construction, not by careful discipline: the only shared mutable state is the write
/// counter, advanced with a single <see cref="Interlocked.Increment(ref long)"/>, and the slot that
/// counter names is written to unconditionally right after — no read-modify-write on the array
/// itself, no compare-and-swap loop, nothing a lock would need to protect.
/// </para>
/// <para>
/// <b>Reading races with writing, on purpose, and that is not a bug to fix.</b> <see cref="Snapshot"/>
/// reads the write counter once, then copies the slots that counter implies are live. A write landing
/// in one of those slots while the copy is in flight can hand back a frame that belongs to a
/// different moment than its neighbours — one row in a report is not from where you'd expect. For a
/// diagnostics tail this is a fair trade: a mutex on the hot BLE-callback path to make an occasional
/// misplaced row impossible would cost every frame something to protect a text file nobody reads
/// under a microscope. Do not add one here.
/// </para>
/// </summary>
public sealed class CircularBuffer<T> : ICircularBuffer<T>
{
    private readonly T[] _items;
    private readonly int _mask;
    private long _written;

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be a power of two.");
        }

        _items = new T[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _items.Length;

    public int Count => (int)Math.Min(Interlocked.Read(ref _written), Capacity);

    public void Add(in T item)
    {
        long slot = Interlocked.Increment(ref _written) - 1;
        _items[slot & _mask] = item;
    }

    /// <summary>Счётчик обнуляется первым, и только потом чистятся слоты: читатель, попавший в
    /// середину, увидит пустой буфер, а не живой счётчик поверх уже стёртых ячеек. Сами ссылки
    /// затираются, а не остаются лежать — иначе кадры прошлого колеса продолжали бы удерживать
    /// память до следующего полного оборота кольца.</summary>
    public void Clear()
    {
        Interlocked.Exchange(ref _written, 0);
        Array.Clear(_items);
    }

    public T[] Snapshot()
    {
        long written = Interlocked.Read(ref _written);
        int count = (int)Math.Min(written, Capacity);
        long start = written - count;

        var result = new T[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = _items[(start + i) & _mask];
        }
        return result;
    }
}
