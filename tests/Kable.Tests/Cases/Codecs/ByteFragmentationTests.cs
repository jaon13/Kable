namespace Kable.Tests.Cases;

using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Kable.Codecs;
using Kable.Engine;
using Kable.Tests.Fixtures;
using Xunit;

public class ByteFragmentationTests
{
    [Theory]
    [InlineData(1, 0x0A)]
    [InlineData(2, 0x0D)]
    [InlineData(3, 0x0A)]
    [InlineData(7, 0x0D)]
    public async Task RequestAsync_FragmentedBytesStream_ReassemblesCompleteResponse(int chunkSize, byte delimiter)
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: delimiter);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        string expectedResponse = "FRAG_RESPONSE_PAYLOAD_123456789_OK";
        byte[] payloadWithDelim = Encoding.UTF8.GetBytes(expectedResponse + (char)delimiter);

        var requestTask = session.RequestAsync<string>("GET_FRAG_DATA", TimeSpan.FromSeconds(5));
        await factory.Context.WriteFragmentedBytesAsync(payloadWithDelim, chunkSize, TimeSpan.FromMilliseconds(5));

        var actualResponse = await requestTask;
        actualResponse.Should().Be(expectedResponse);
    }

    [Theory]
    [InlineData(3, 0x0A)]
    [InlineData(5, 0x0D)]
    public async Task RequestAsync_BackToBackPackedFramesInSingleRead_HandlesEachCorrectly(int totalFrames, byte delimiter)
    {
        var factory = new TestMemoryConnectionFactory();
        var codec = new AsciiLineCodec(delimiter: delimiter);
        await using var session = new KableSession<string>(factory, codec);
        await session.StartAsync();

        var sb = new StringBuilder();
        for (int i = 0; i < totalFrames; i++)
        {
            sb.Append("BATCH_FRAME_").Append(i).Append((char)delimiter);
        }
        byte[] packedBytes = Encoding.UTF8.GetBytes(sb.ToString());

        var requestTask = session.RequestAsync<string>("TRIGGER_BATCH", TimeSpan.FromSeconds(5));
        await factory.Context.RemoteWrite.WriteAsync(packedBytes);
        await factory.Context.RemoteWrite.FlushAsync();

        var firstResp = await requestTask;
        firstResp.Should().Be("BATCH_FRAME_0");

        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
        var streamReader = session.GetStreamAsync(cts.Token);
        int frameIndex = 1;
        await foreach (var item in streamReader)
        {
            item.Should().Be("BATCH_FRAME_" + frameIndex);
            frameIndex++;
            if (frameIndex >= totalFrames) break;
        }
        frameIndex.Should().Be(totalFrames);
    }
}