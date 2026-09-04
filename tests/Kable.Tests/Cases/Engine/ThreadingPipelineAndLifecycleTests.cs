namespace Kable.Tests.Cases.Engine;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
using Kable.Tests.Fixtures;
using Xunit;

public sealed class ThreadingPipelineAndLifecycleTests
{
    [Fact]
    public async Task StopAsync_ShouldGracefullyJoinAllInternalTasks_WithoutExceptions()
    {
        // Arrange
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        var session = new KableSession<string>(factory, codec);

        await session.StartAsync();
        session.IsConnected.Should().BeTrue();

        // Act
        var sw = Stopwatch.StartNew();
        await session.StopAsync();
        sw.Stop();

        // Assert
        session.IsConnected.Should().BeFalse();
        sw.ElapsedMilliseconds.Should().BeLessThan(2500, "StopAsync should gracefully join within timeout limit");
    }

    [Fact]
    public async Task ProducerConsumer_UnderHighThroughputBursts_DeliversAllMessagesInOrder()
    {
        // Arrange
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        const int totalMessages = 1000;
        var received = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var consumerTask = Task.Run(async () =>
        {
            await foreach (var item in session.GetStreamAsync(cts.Token))
            {
                received.Enqueue(item);
                if (received.Count == totalMessages) break;
            }
        });

        // Act: Fast producer writing directly to memory pipe
        for (int i = 0; i < totalMessages; i++)
        {
            await factory.Context.WriteAsciiLineAsync($"$MSG_{i:D4}", 0x0A);
        }

        await Task.WhenAny(consumerTask, Task.Delay(4000, cts.Token));

        // Assert
        received.Count.Should().Be(totalMessages, "All messages through producer-consumer pipeline must be received");
        var receivedList = received.ToArray();
        for (int i = 0; i < totalMessages; i++)
        {
            receivedList[i].Should().Be($"$MSG_{i:D4}");
        }
    }
}
