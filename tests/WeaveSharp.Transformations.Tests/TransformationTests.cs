using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace WeaveSharp.Transformations.Tests;

public sealed class TransformationTests
{
    public static TheoryData<string, string, string[]> PreservingCases => new()
    {
        { "receiver-arguments", "success", ["receiver", "label", "ticket-next", "accept:parcel:1", "ticket:1"] },
        { "receiver-arguments", "null-receiver", ["receiver", "label", "ticket-next", "throw:NullReferenceException", "ticket:1"] },
        { "receiver-arguments", "receiver-throws", ["receiver", "throw:InvalidOperationException", "ticket:0"] },
        { "receiver-arguments", "argument-throws", ["receiver", "label", "throw:ApplicationException", "ticket:0"] },
        { "conditional-effects", "both-true", ["left", "right", "result:true"] },
        { "conditional-effects", "left-false", ["left", "result:false"] },
        { "conditional-effects", "right-false", ["left", "right", "result:false"] },
        { "conditional-effects", "left-throws", ["left", "throw:InvalidOperationException"] },
        { "conditional-effects", "right-throws", ["left", "right", "throw:ApplicationException"] },
        { "conditional-effects", "skipped-throw", ["left", "result:false"] },
        { "return-cleanup", "success", ["read", "cleanup", "caller:17", "stored:91"] },
        { "return-cleanup", "read-throws", ["read", "cleanup", "throw:InvalidOperationException", "stored:91"] },
        { "return-cleanup", "cleanup-throws", ["read", "cleanup", "throw:ApplicationException", "stored:91"] },
        { "return-cleanup", "both-throw", ["read", "cleanup", "throw:ApplicationException", "stored:91"] },
        { "storage-alias", "success", ["array", "index", "delta", "cells:13:30"] },
        { "storage-alias", "mutating-delta", ["array", "index", "delta", "delta-write:70", "cells:13:30"] },
        { "storage-alias", "null-array", ["array", "index", "throw:NullReferenceException", "cells:10:30"] },
        { "storage-alias", "bad-index", ["array", "index", "throw:IndexOutOfRangeException", "cells:10:30"] },
        { "storage-alias", "overflow", ["array", "index", "delta", "throw:OverflowException", "cells:2147483647:30"] },
        { "storage-alias", "delta-throws", ["array", "index", "delta", "throw:ApplicationException", "cells:10:30"] }
    };

    [Theory]
    [MemberData(nameof(PreservingCases))]
    public async Task PreservationPairsMatchObservedEffects(string fixture, string scenario, string[] expected)
    {
        string[] before = await FixtureRunner.Run($"Fixtures/{fixture}/Before.cs", scenario);
        string[] after = await FixtureRunner.Run($"Fixtures/{fixture}/After.cs", scenario);

        Assert.Equal(expected, before);
        Assert.Equal(expected, after);
        Assert.True(FixtureRunner.SameEffects(before, after));
    }

    public static TheoryData<string, string[], string[], bool> AsyncCases => new()
    {
        {
            "success",
            ["start:oak", "observed:O", "start:elm", "observed:E", "result:O:E"],
            ["start:oak", "start:elm", "observed:O", "observed:E", "result:O:E"],
            false
        },
        {
            "first-fault",
            ["start:oak", "throw:ApplicationException"],
            ["start:oak", "start:elm", "throw:ApplicationException"],
            false
        },
        {
            "second-fault",
            ["start:oak", "observed:O", "start:elm", "throw:ApplicationException"],
            ["start:oak", "start:elm", "throw:ApplicationException"],
            false
        },
        {
            "synchronous-failure",
            ["start:oak", "throw:InvalidOperationException"],
            ["start:oak", "throw:InvalidOperationException"],
            true
        }
    };

    [Theory]
    [MemberData(nameof(AsyncCases))]
    public async Task StartingTasksEarlyHasTheDeclaredEffectChange(
        string scenario, string[] expectedBefore, string[] expectedAfter, bool sameEffects)
    {
        string[] before = await FixtureRunner.Run("Fixtures/start-before-await/Before.cs", scenario);
        string[] after = await FixtureRunner.Run("Fixtures/start-before-await/After.cs", scenario);

        Assert.Equal(expectedBefore, before);
        Assert.Equal(expectedAfter, after);
        Assert.Equal(sameEffects, FixtureRunner.SameEffects(before, after));
    }

    [Theory]
    [InlineData("left-false", "result:false")]
    [InlineData("skipped-throw", "throw:ApplicationException")]
    public async Task ComparisonRejectsAnEagerConditionalOperand(string scenario, string lastEffect)
    {
        string[] before = await FixtureRunner.Run("Fixtures/conditional-effects/Before.cs", scenario);
        string[] incorrect = await FixtureRunner.Run("NegativeControls/eager-condition/After.cs", scenario);

        Assert.Equal(["left", "result:false"], before);
        Assert.Equal(["left", "right", lastEffect], incorrect);
        Assert.False(FixtureRunner.SameEffects(before, incorrect));
    }

    [Fact]
    public void FivePairsHaveSourcesAndDescriptions()
    {
        string[] expected = ["conditional-effects", "receiver-arguments", "return-cleanup", "start-before-await", "storage-alias"];
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        string[] directories = Directory.GetDirectories(root);
        Assert.Equal(expected, directories.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (string directory in directories)
        {
            Assert.Equal(["After.cs", "Before.cs"],
                Directory.GetFiles(directory, "*.cs").Select(Path.GetFileName).Order(StringComparer.Ordinal));
            string description = Assert.Single(File.ReadAllLines(Path.Combine(directory, "description.txt")));
            Assert.False(string.IsNullOrWhiteSpace(description));
        }
    }
}
