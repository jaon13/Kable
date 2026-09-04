namespace Kable.Codecs;

using System.Buffers;

public interface IProtocolCodec<TMessage>
{
    bool SupportsCorrelationId { get; }
    bool TryDecode(ref ReadOnlySequence<byte> buffer, out TMessage message);
    void Encode(TMessage message, IBufferWriter<byte> output);
    string? ExtractCorrelationId(TMessage message);
    bool IsAutonomousMessage(TMessage message);
}
