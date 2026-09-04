namespace Kable.Codecs;

using System;
using System.Buffers;
using System.Text;

public sealed class AsciiLineCodec : IProtocolCodec<string>
{
    private readonly byte _delimiter;
    private readonly Encoding _encoding;
    private readonly int _maxFrameSize;

    public bool SupportsCorrelationId => false;
    public int MaxFrameSize => _maxFrameSize;

    public AsciiLineCodec(byte delimiter = 0x0A, Encoding? encoding = null, int maxFrameSize = 65536)
    {
        _delimiter = delimiter;
        _encoding = encoding ?? Encoding.ASCII;
        _maxFrameSize = maxFrameSize;
    }

    public bool TryDecode(ref ReadOnlySequence<byte> buffer, out string message)
    {
        var position = buffer.PositionOf(_delimiter);
        if (position == null)
        {
            if (buffer.Length > _maxFrameSize)
            {
                throw new Kable.Exceptions.ProtocolViolationException($"Frame size limit exceeded ({buffer.Length} > {_maxFrameSize}) without delimiter.");
            }
            message = string.Empty;
            return false;
        }

        var lineSlice = buffer.Slice(0, position.Value);
        if (lineSlice.Length > _maxFrameSize)
        {
            buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
            throw new Kable.Exceptions.ProtocolViolationException($"Frame size limit exceeded ({lineSlice.Length} > {_maxFrameSize}).");
        }

        message = GetStringFromSequence(lineSlice).TrimEnd('\r', '\n');
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    public void Encode(string message, IBufferWriter<byte> output)
    {
        var bytes = _encoding.GetBytes(message);
        var span = output.GetSpan(bytes.Length + 1);
        bytes.CopyTo(span);
        span[bytes.Length] = _delimiter;
        output.Advance(bytes.Length + 1);
    }

    public string? ExtractCorrelationId(string message) => null;

    public bool IsAutonomousMessage(string message)
    {
        return message.StartsWith("$", StringComparison.Ordinal) ||
               message.StartsWith("#", StringComparison.Ordinal);
    }

    private string GetStringFromSequence(ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
        {
#if NETSTANDARD2_0
            var array = sequence.First.ToArray();
            return _encoding.GetString(array);
#else
            return _encoding.GetString(sequence.First.Span);
#endif
        }

        var length = (int)sequence.Length;
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            sequence.CopyTo(rented);
            return _encoding.GetString(rented, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
