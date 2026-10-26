using System;
using Xunit;

namespace WeaveSharp.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void RunsOnNet10()
    {
        Assert.Equal(10, Environment.Version.Major);
    }
}
