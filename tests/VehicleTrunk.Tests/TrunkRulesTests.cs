using VehicleTrunk;
using Xunit;

namespace VehicleTrunk.Tests;

public class TrunkRulesTests
{
    [Fact]
    public void TrunkFlag_IsBit16()
    {
        Assert.Equal(65536, TrunkRules.TrunkFlag);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void TrunkFlag_DoesNotOverlapOtherBits(int otherBit)
    {
        Assert.Equal(0, TrunkRules.TrunkFlag & otherBit);
    }

    [Fact]
    public void ShouldRebuild_TrueWhenFlagSetAndNotDisposed()
    {
        Assert.True(TrunkRules.ShouldRebuild(TrunkRules.TrunkFlag, false));
    }

    [Fact]
    public void ShouldRebuild_FalseWhenFlagSetAndDisposed()
    {
        Assert.False(TrunkRules.ShouldRebuild(TrunkRules.TrunkFlag, true));
    }

    [Fact]
    public void ShouldRebuild_FalseWhenFlagNotSetAndNotDisposed()
    {
        Assert.False(TrunkRules.ShouldRebuild(0, false));
    }

    [Fact]
    public void ShouldRebuild_FalseWhenFlagNotSetAndDisposed()
    {
        Assert.False(TrunkRules.ShouldRebuild(0, true));
    }

    [Fact]
    public void ShouldRebuild_FalseWithOtherBitsButNotTrunkBit()
    {
        Assert.False(TrunkRules.ShouldRebuild(1 | 2 | 4, false));
    }

    [Fact]
    public void ShouldRebuild_TrueWithOtherBitsPlusTrunkBit()
    {
        Assert.True(TrunkRules.ShouldRebuild(TrunkRules.TrunkFlag | 1 | 2, false));
    }

    [Theory]
    [InlineData(-5f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(100f, 100f)]
    public void ClampCapacity_ClampsNegativeAndPassesThroughRest(float input, float expected)
    {
        Assert.Equal(expected, TrunkRules.ClampCapacity(input));
    }

    [Fact]
    public void ClampCapacity_NaNBecomesZero()
    {
        Assert.Equal(0f, TrunkRules.ClampCapacity(float.NaN));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ShowTrunk_TruthTable(bool isPlayer, bool isCarrying, bool expected)
    {
        Assert.Equal(expected, TrunkRules.ShowTrunk(isPlayer, isCarrying));
    }
}

public class TrunkFlagTransitionTests
{
    [Fact]
    public void WasStashedFlag_IsBit17AndDistinct()
    {
        Assert.Equal(1 << 17, TrunkRules.WasStashedFlag);
        Assert.Equal(0, TrunkRules.WasStashedFlag & TrunkRules.TrunkFlag);
        Assert.Equal(0, TrunkRules.WasStashedFlag & TrunkRules.StashedFlag);
    }

    [Fact]
    public void EnterFlags_SetsTrunkAndRemembersUnstashed()
    {
        int flags = TrunkRules.EnterFlags(4);
        Assert.Equal(4 | TrunkRules.TrunkFlag, flags);
    }

    [Fact]
    public void EnterFlags_SetsTrunkAndRemembersStashed()
    {
        int flags = TrunkRules.EnterFlags(TrunkRules.StashedFlag | 4);
        Assert.Equal(TrunkRules.StashedFlag | 4 | TrunkRules.TrunkFlag | TrunkRules.WasStashedFlag, flags);
    }

    [Fact]
    public void EnterFlags_LeavesRebuiltItemAlone()
    {
        int rebuilt = TrunkRules.TrunkFlag | TrunkRules.StashedFlag;
        Assert.Equal(rebuilt, TrunkRules.EnterFlags(rebuilt));
    }

    [Fact]
    public void LeaveFlags_ClearsStashedWhenItWasNotStashedBefore()
    {
        int inTrunk = TrunkRules.EnterFlags(4) | TrunkRules.StashedFlag;
        Assert.Equal(4, TrunkRules.LeaveFlags(inTrunk));
    }

    [Fact]
    public void LeaveFlags_KeepsStashedWhenItWasStashedBefore()
    {
        int inTrunk = TrunkRules.EnterFlags(TrunkRules.StashedFlag | 4) | TrunkRules.StashedFlag;
        Assert.Equal(TrunkRules.StashedFlag | 4, TrunkRules.LeaveFlags(inTrunk));
    }
}
