using System;
using System.Collections.Generic;
using Xunit;

namespace WeaveSharp.Protocol.Tests;

public sealed class VersionTests
{
    [Fact]
    public void PicksTheNewestVersionBothPeersExplicitlyOffer()
    {
        var result = VersionNegotiator.Negotiate([new(0, 0), new(0, 1), new(0, 3)],
            [new(0, 1), new(0, 0), new(1, 0)], new HashSet<string>(), new Dictionary<string, ProtocolExtension>());
        Assert.True(result.Accepted);
        Assert.Equal(new ProtocolVersion(0, 1), result.Version);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 7)]
    public void RefusesDisjointVersions(int major, int minor)
    {
        var result = VersionNegotiator.Negotiate([new(0, 0)], [new(major, minor)],
            new HashSet<string>(), new Dictionary<string, ProtocolExtension>());
        Assert.False(result.Accepted);
        Assert.NotNull(result.Refusal);
        Assert.Null(result.Version);
        Assert.Throws<InvalidOperationException>(() => new ProtocolSession(result, SessionTests.Context,
            new SessionTests.RecordingTransport(), "s", "v"));
    }

    [Fact]
    public void EmptyVersionOffersCannotOpenASession()
    {
        Assert.False(VersionNegotiator.Negotiate([], [new(0, 0)], new HashSet<string>(),
            new Dictionary<string, ProtocolExtension>()).Accepted);
    }

    [Fact]
    public void UnknownOptionalExtensionIsReportedAndDoesNotBecomeEnabled()
    {
        var result = Negotiate(new Dictionary<string, ProtocolExtension>
        {
            ["future-hint"] = new(false, "compact"),
            ["known-hint"] = new(true, "expanded")
        });
        Assert.True(result.Accepted);
        Assert.Equal(["future-hint"], result.IgnoredExtensions);
        Assert.Equal(["known-hint"], result.EnabledExtensions.Keys);
    }

    [Fact]
    public void UnknownMandatoryExtensionRefusesTheAgreement()
    {
        var result = Negotiate(new Dictionary<string, ProtocolExtension> { ["future-rule"] = new(true, "v2") });
        Assert.False(result.Accepted);
        Assert.Contains("future-rule", result.Refusal);
        Assert.Empty(result.EnabledExtensions);
    }

    private static NegotiationResult Negotiate(IReadOnlyDictionary<string, ProtocolExtension> extensions) =>
        VersionNegotiator.Negotiate([new(0, 0)], [new(0, 0)], new HashSet<string> { "known-hint" }, extensions);
}
