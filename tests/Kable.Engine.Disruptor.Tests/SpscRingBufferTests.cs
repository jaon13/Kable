namespace Kable.Tests.Cases.Engine;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Engine.Disruptor;
using Xunit;

public sealed class SpscRingBufferTests
{
    private sealed class TestItem
    {
        public int Id { get; }
        public TestItem(int id) => Id = id;
    }

    [Fact]
    public void SpscRingBuffer_SequentialEnqueueDequeue_MaintainsFIFOOrder()
    {
        var ring = new SpscRingBuffer<TestItem>(16);

        for (int i = 0; i < 10; i++)
        {
            ring.TryEnqueue(new TestItem(i)).Should().BeTrue();
        }

        ring.Count.Should().Be(10);

        for (int i = 0; i < 10; i++)
        {
            ring.TryDequeue(out var item).Should().BeTrue();
            item.Should().NotBeNull();
            item!.Id.Should().Be(i);
        }

        ring.Count.Should().Be(0);
        ring.TryDequeue(out _).Should().BeFalse();
    }

    [Fact]
    public async Task SpscRingBuffer_ConcurrentProducerConsumer_NeverLosesData()
    {
        const int count = 50_000;
        var ring = new SpscRingBuffer<TestItem>(1024);

        var producer = Task.Run(() =>
        {
            for (int i = 0; i < count; i++)
            {
                while (!ring.TryEnqueue(new TestItem(i)))
                {
                    Thread.Yield();
                }
            }
        });

        int receivedCount = 0;
        var consumer = Task.Run(() =>
        {
            int expected = 0;
            while (expected < count)
            {
                if (ring.TryDequeue(out var item) && item != null)
                {
                    item.Id.Should().Be(expected);
                    expected++;
                    receivedCount++;
                }
                else
                {
                    Thread.Yield();
                }
            }
        });

        await Task.WhenAll(producer, consumer);
        receivedCount.Should().Be(count);
    }
}
