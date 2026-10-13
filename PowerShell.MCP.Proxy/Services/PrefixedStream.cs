namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// A stream that first returns bytes already read from the inner stream, then
/// reads on from it. The accept port peeks at one byte to tell TLS (0x16)
/// from HTTP ('G'/'H') and hands the connection on without losing it.
/// </summary>
internal sealed class PrefixedStream(byte[] prefix, Stream inner) : Stream
{
    private int _offset;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_offset < prefix.Length)
        {
            var n = Math.Min(buffer.Length, prefix.Length - _offset);
            prefix.AsSpan(_offset, n).CopyTo(buffer);
            _offset += n;
            return n;
        }
        return inner.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_offset < prefix.Length)
        {
            var n = Math.Min(buffer.Length, prefix.Length - _offset);
            prefix.AsMemory(_offset, n).CopyTo(buffer);
            _offset += n;
            return ValueTask.FromResult(n);
        }
        return inner.ReadAsync(buffer, cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}