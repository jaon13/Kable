namespace Kable.Engine.Disruptor;

using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// Cache-line isolated atomic sequence counter (128 bytes total, aligned at 64-byte boundary).
/// Eliminates false sharing between producer and consumer cores.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct PaddedSequence
{
    [FieldOffset(64)]
    public long Value;

    public long ReadVolatile() => Volatile.Read(ref Value);

    public void WriteVolatile(long val) => Volatile.Write(ref Value, val);
}
