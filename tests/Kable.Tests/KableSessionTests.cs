namespace Kable.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
using Kable.Tests.Fixtures;
using Xunit;

public class KableSessionTests
{
    [Theory]
    [InlineData("CMD_A", "ACK_A", 0x0A)]
    [InlineData("oPON", "ACK", 0x0D)]
    [InlineData("ENQUIRE", "STAT_OK", 0x0A)]
    [InlineData("GET_VALVE", "VALVE_1_OPEN", 0x0D)]
    public async Task RequestAsync_SequentialCommands_ReturnsCorrespondingResponses(string command, string responseText, byte delimiter)
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: delimiter);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();


        var requestTask = session.RequestAsync<string>(command, TimeSpan.FromSeconds(3));
        await factory.Context.WriteAsciiLineAsync(responseText, delimiter);
        var actualResponse = await requestTask;


        actualResponse.Should().Be(responseText);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task RequestAsync_ConcurrentMultiCallers_FifoLockSerializesExecutionSafely(int callerCount)
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: 0x0A);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();


        var tasks = new List<Task<string>>();
        for (int i = 0; i < callerCount; i++)
        {
            int callerId = i;
            tasks.Add(Task.Run(async () =>
            {
                return await session.RequestAsync<string>("CALLER_" + callerId, TimeSpan.FromSeconds(5));
            }));
        }


        for (int i = 0; i < callerCount; i++)
        {
            await Task.Delay(20);
            await factory.Context ?.WriteAsciiLineAsync("RESP_ACK_" + i, 0x0A);
        }

        var results = await Task.WhenAll(tasks);
        results.Length.Should().Be(callerCount);
        results.Should().OnlyHaveUniqueItems();
    }
}
