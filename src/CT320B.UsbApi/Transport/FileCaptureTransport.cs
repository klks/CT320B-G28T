namespace CT320B.UsbApi.Transport;

/// <summary>
/// Capture transport: appends written bytes to a file instead of sending them to a device —
/// the managed equivalent of the DLL's "output to file" mode (<c>SetOutputFile</c> /
/// <c>OutputToFile</c>, which fopen's the path in "ab" and fwrites each packet). Useful for
/// debugging a label job and for byte-exact diffing against the native oracle captures.
/// <see cref="Read"/> is unsupported (returns -1), matching a write-only sink.
/// </summary>
public sealed class FileCaptureTransport : IPrinterTransport
{
    private readonly string _path;

    /// <param name="path">Target file. Created if missing; subsequent writes append.</param>
    /// <param name="truncate">If true, clears the file on construction (fresh capture).</param>
    public FileCaptureTransport(string path, bool truncate = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        if (truncate && File.Exists(path)) File.Delete(path);
    }

    public bool IsOpen => true;

    public int Write(ReadOnlySpan<byte> data, int timeoutMs = IPrinterTransport.DefaultTimeoutMs)
    {
        using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        fs.Write(data);
        return 0;
    }

    public int Read(Span<byte> buffer, int timeoutMs = IPrinterTransport.DefaultTimeoutMs) => -1;

    public void Close() { }

    public void Dispose() { }
}
