namespace Kable.Codecs;

using System;
using System.Buffers;
using System.Buffers.Binary;
using Kable.Exceptions;

public sealed class BinaryLengthPrefixedCodec : IProtocolCodec<ReadOnlyMemory<byte>>
{
    private readonly int _headerLength;
    private readonly bool _isBigEndian;
    private readonly int _maxFrameSize;

    public bool SupportsCorrelationId => false;
    public int MaxFrameSize => _maxFrameSize;

    public BinaryLengthPrefixedCodec(int headerLength = 4, bool isBigEndian = false, int maxFrameSize = 65536)
    {
        if (headerLength != 2 && headerLength != 4)
        {
            throw new ArgumentOutOfRangeException(nameof(headerLength), "Header length must be 2 or 4 bytes.");
        }

        _headerLength = headerLength;
        _isBigEndian = isBigEndian;
        _maxFrameSize = maxFrameSize;
    }

    public bool TryDecode(ref ReadOnlySequence<byte> buffer, out ReadOnlyMemory<byte> message)
    {
        if (buffer.Length < _headerLength)
        {
            message = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        Span<byte> headerSpan = stackalloc byte[_headerLength];
        buffer.Slice(0, _headerLength).CopyTo(headerSpan);

        int bodyLength = _headerLength == 2
            ? (_isBigEndian ? BinaryPrimitives.ReadInt16BigEndian(headerSpan) : BinaryPrimitives.ReadInt16LittleEndian(headerSpan))
            : (_isBigEndian ? BinaryPrimitives.ReadInt32BigEndian(headerSpan) : BinaryPrimitives.ReadInt32LittleEndian(headerSpan));

        if (bodyLength < 0 || bodyLength > _maxFrameSize)
        {
            throw new ProtocolViolationException($"Frame size limit exceeded ({bodyLength} > {_maxFrameSize}).");
        }

        if (buffer.Length < _headerLength + bodyLength)
        {
            message = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        var bodySequence = buffer.Slice(_headerLength, bodyLength);
        message = bodySequence.ToArray();
        buffer = buffer.Slice(buffer.GetPosition(_headerLength + bodyLength));
        return true;
    }

    public void Encode(ReadOnlyMemory<byte> message, IBufferWriter<byte> output)
    {
        var span = output.GetSpan(_headerLength + message.Length);

        if (_headerLength == 2)
        {
            if (_isBigEndian)
            {
                BinaryPrimitives.WriteInt16BigEndian(span, (short)message.Length);
            }
            else
            {
                BinaryPrimitives.WriteInt16LittleEndian(span, (short)message.Length);
            }
        }
        else
        {
            if (_isBigEndian)
            {
                BinaryPrimitives.WriteInt32BigEndian(span, message.Length);
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(span, message.Length);
            }
        }

        message.Span.CopyTo(span.Slice(_headerLength));
        output.Advance(_headerLength + message.Length);
    }

    public string? ExtractCorrelationId(ReadOnlyMemory<byte> message) => null;

    public bool IsAutonomousMessage(ReadOnlyMemory<byte> message) => false;
}
