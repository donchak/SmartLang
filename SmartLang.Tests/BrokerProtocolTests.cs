namespace SmartLang.Tests;

public sealed class BrokerProtocolTests {
    [Fact]
    public async Task RequestRoundTripsThroughLengthPrefixedJson() {
        var expected = new BrokerRequest(
            BrokerProtocol.CurrentVersion,
            Guid.NewGuid(),
            BrokerCommand.SaveSettings,
            new AppSettings {
                PrimaryLanguageTag = "en-US",
                SecondaryLanguageTag = "fr-FR",
                AdministratorAppSupport = true
            });
        await using var stream = new MemoryStream();

        await BrokerProtocol.WriteAsync(stream, expected, CancellationToken.None);
        stream.Position = 0;
        var actual = await BrokerProtocol.ReadAsync<BrokerRequest>(
            stream,
            CancellationToken.None);

        Assert.Equal(expected.ProtocolVersion, actual.ProtocolVersion);
        Assert.Equal(expected.RequestId, actual.RequestId);
        Assert.Equal(expected.Command, actual.Command);
        Assert.Equal("en-US", actual.Settings?.PrimaryLanguageTag);
        Assert.True(actual.Settings?.AdministratorAppSupport);
    }

    [Fact]
    public async Task InvalidFrameLengthIsRejected() {
        await using var stream = new MemoryStream(
            BitConverter.GetBytes(BrokerProtocol.MaximumMessageLength + 1));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BrokerProtocol.ReadAsync<BrokerRequest>(
                stream,
                CancellationToken.None));
    }

    [Fact]
    public async Task OversizedMessageIsRejectedBeforeWriting() {
        var request = new BrokerRequest(
            BrokerProtocol.CurrentVersion,
            Guid.NewGuid(),
            BrokerCommand.SaveSettings,
            new AppSettings {
                PrimaryLanguageTag = new string('x', BrokerProtocol.MaximumMessageLength)
            });
        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BrokerProtocol.WriteAsync(stream, request, CancellationToken.None));
        Assert.Equal(0, stream.Length);
    }
}
