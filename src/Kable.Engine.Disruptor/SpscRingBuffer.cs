namespace Kable.Engine.Disruptor;

using System;
using System.Runtime.CompilerServices;

/// <summary>
/// Cache-line isolated Lock-Free Single Producer Single Consumer (SPSC) RingBuffer.
/// Eliminates false sharing and guarantees true zero-allocation packet slot transfer.
/// </summary>
public sealed class SpscRingBuffer<T> where T : class
{
    private readonly T[] _buffer;
    private readonly int _mask;

    private PaddedSequence _head; // Written only by Producer
    private PaddedSequence _tail; // Written only by Consumer

    public int Capacity { get; }

    public SpscRingBuffer(int capacityPowerOfTwo = 4096)
    {
        if ((capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
        {
            throw new ArgumentException("Capacity must be a power of two.", nameof(capacityPowerOfTwo));
        }

        Capacity = capacityPowerOfTwo;
        _mask = capacityPowerOfTwo - 1;
        _buffer = new T[capacityPowerOfTwo];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnqueue(T item)
    {
        long currentHead = _head.ReadVolatile();
        long currentTail = _tail.ReadVolatile();

        if (currentHead - currentTail >= Capacity)
        {
            return false; // Buffer full
        }

        _buffer[currentHead & _mask] = item;
        _head.WriteVolatile(currentHead + 1);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDequeue(out T? item)
    {
        long currentTail = _tail.ReadVolatile();
        long currentHead = _head.ReadVolatile();

        if (currentTail >= currentHead)
        {
            item = null;
            return false; // Buffer empty
        }

        int index = (int)(currentTail & _mask);
        item = _buffer[index];
        _buffer[index] = null!; // Avoid holding object references
        _tail.WriteVolatile(currentTail + 1);
        return true;
    }

    public int Count
    {
        get
        {
            long head = _head.ReadVolatile();
            long tail = _tail.ReadVolatile();
            long diff = head - tail;
            return diff > 0 ? (int)diff : 0;
        }
    }
}
