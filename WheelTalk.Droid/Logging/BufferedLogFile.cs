namespace WheelTalk.Droid.Logging;

/// <summary>
/// One open log file. Writes are buffered and flushed every so many lines rather than one by one:
/// the raw dump is written straight from the GATT callback, on the same thread that feeds the
/// decoder, and a flush per frame would put the file system in that path twenty-odd times a second.
/// <para>
/// Lines end with CRLF, as the original's <c>FileUtil.writeLine</c> does — a <c>StreamWriter</c> on
/// Android would otherwise write LF and produce files subtly unlike WheelLog's.
/// </para>
/// </summary>
public sealed class BufferedLogFile : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly int _flushEvery;
    private int _sinceFlush;

    public string Path { get; }
    public int LinesWritten { get; private set; }

    public BufferedLogFile(string path, int flushEvery)
    {
        Path = path;
        _flushEvery = flushEvery;
        _writer = new StreamWriter(path, append: false) { AutoFlush = false, NewLine = "\r\n" };
    }

    public void Write(string line)
    {
        _writer.WriteLine(line);
        LinesWritten++;

        if (++_sinceFlush < _flushEvery) return;
        _writer.Flush();
        _sinceFlush = 0;
    }

    public void Dispose() => _writer.Dispose();
}
