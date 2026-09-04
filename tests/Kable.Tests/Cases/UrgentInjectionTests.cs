namespace Kable.Tests.Cases;

using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
using Kable.Tests.Fixtures;
using Xunit;

public class UrgentInjectionTests
{
    [Fact]
    public async Task SendUrgentAsync_WhileFifoCommandIsWaiting_TransmitsImmediatelyWithoutBlockingOnFifoLock()
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();


        var slowCommandTask = session.RequestAsync<string>("SLOW_MEASURE_CMD", TimeSpan.FromSeconds(5));

        await session.SendUrgentAsync("EMERGENCY_STOP");


        var readResult = await factory.Context.RemoteRead.ReadAsync();
        var sentString = Encoding.UTF8.GetString(BuffersExtensions.ToArray(readResult.Buffer));
        factory.Context.RemoteRead.AdvanceTo(readResult.Buffer.End);

        sentString.Should().Contain("SLOW_MEASURE_CMD");
        sentString.Should().Contain("EMERGENCY_STOP");


        await factory.Context.WriteAsciiLineAsync("SLOW_MEASURE_ACK", 0x0A);
        var res = await slowCommandTask;
        res.Should().Be("SLOW_MEASURE_ACK");
    }
}
