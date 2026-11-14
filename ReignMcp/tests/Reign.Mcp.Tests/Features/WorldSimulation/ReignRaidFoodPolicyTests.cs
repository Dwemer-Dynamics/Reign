using Newtonsoft.Json;
using ReignBeta.Economy;
using Reign.Core.Contracts.Economy;

namespace Reign.Mcp.Tests;

public sealed class ReignRaidFoodPolicyTests
{
    [Theory]
    [InlineData(0,1)] [InlineData(.06,.76)] [InlineData(.1,.6)] [InlineData(.24,.04)]
    [InlineData(.25,0)] [InlineData(.4,0)] [InlineData(1,0)]
    public void AppliedDamageUsesOneContinuousFoodOnlyCapacity(double damage, double expected)
        => Assert.Equal(expected, ReignRaidFoodPolicy.Damage(1,damage,.25),8);
    [Fact]
    public void RepeatedRealPartialsCompoundButRepeatedProgressDoesNot()
    {
        var raid = new RaidFoodAttempt();
        double first = ReignRaidFoodPolicy.ObserveApplied(raid,1,.9,.1);
        double duplicate = ReignRaidFoodPolicy.ObserveApplied(raid,1,.9,.1);
        Assert.Equal(.1,first,8); Assert.Equal(0,duplicate);
        Assert.Equal(.2,ReignRaidFoodPolicy.Damage(ReignRaidFoodPolicy.Damage(1,first,.25),.1,.25),8);
        Assert.Equal(0,ReignRaidFoodPolicy.ObserveApplied(raid,.9,.9,.1));
        Assert.Equal(.06,ReignRaidFoodPolicy.ObserveApplied(raid,.96,.9,.16),8);
    }
    [Fact]
    public void BufferedAndUncorroboratedHpWritesDoNotBecomeRaidDamage()
    {
        var raid = new RaidFoodAttempt();
        Assert.Equal(0,ReignRaidFoodPolicy.ObserveApplied(raid,1,1,0));
        Assert.Equal(0,ReignRaidFoodPolicy.ObserveApplied(raid,1,.8,0));
        Assert.Equal(.1,ReignRaidFoodPolicy.ObserveApplied(raid,1,.8,.1),8);
    }
    [Theory]
    [InlineData("partial",false,40)] [InlineData("partial",true,80)]
    [InlineData("full",false,60)] [InlineData("full",true,120)]
    public void RecoveryIsFiniteDuringFoodStressAndUsesSpeedOnlyOnce(string severity,bool stressed,int days)
    {
        double c=0;
        for(int i=0;i<days;i++) c=ReignRaidFoodPolicy.Recover(c,1,true,stressed,severity,40,60,.5);
        Assert.Equal(1,c);
        Assert.Equal(.5,ReignRaidFoodPolicy.Recover(0,days/2d,true,stressed,severity,40,60,.5),8);
        Assert.Equal(0,ReignRaidFoodPolicy.Recover(0,days,false,stressed,severity,40,60,.5));
        Assert.Equal(.25,ReignRaidFoodPolicy.Recover(.25,-1,true,stressed,severity,40,60,.5));
    }
    [Fact]
    public void FractionalTimeDoesNotRequireAnAdditionalDailyCallback()
        => Assert.Equal(.0125,ReignRaidFoodPolicy.Recover(0,.5,true,false,"partial",40,60,.5),8);
    [Fact]
    public void FoodRecoveryIsMinimumNeverProductAndToggleDoesNotMutateDamage()
    {
        Assert.Equal(.5,ReignRaidFoodPolicy.Effective(.5,.5,true));
        Assert.Equal(.4,ReignRaidFoodPolicy.Effective(.4,.6,true));
        Assert.Equal(.8,ReignRaidFoodPolicy.Effective(.8,.1,false));
        Assert.Equal(.1,ReignRaidFoodPolicy.Effective(.8,.1,true));
    }
    [Fact]
    public void FishingRemainderConservesFractionalOutputAcrossSaveAndBoatReplacement()
    {
        var village=new RaidFoodVillage(); int caught=0;
        for(int i=0;i<3;i++) caught+=ReignRaidFoodPolicy.Catch(1,.4,ref village.CatchCarry);
        var saved=JsonConvert.DeserializeObject<RaidFoodVillage>(JsonConvert.SerializeObject(village))!;
        for(int i=0;i<7;i++) caught+=ReignRaidFoodPolicy.Catch(1,.4,ref saved.CatchCarry);
        Assert.Equal(4,caught); Assert.InRange(saved.CatchCarry,0,1e-12);
        saved.CatchCarry=.7;
        Assert.Equal(0,ReignRaidFoodPolicy.Catch(1,0,ref saved.CatchCarry));
        Assert.Equal(.7,saved.CatchCarry);
        Assert.Equal(1,ReignRaidFoodPolicy.Catch(1,1,ref saved.CatchCarry));
        Assert.Equal(.7,saved.CatchCarry,8);
    }
    [Fact]
    public void ASecondRaidCannotImproveOrRenewTheRateOfAnExistingFishingVoyage()
    {
        var boat=new RaidFoodBoat{VoyageStartHours=10};
        ReignRaidFoodPolicy.ProtectVoyage(boat,.4);
        Assert.True(boat.Grandfathered);Assert.Equal(.4,boat.GrandfatherFactor);
        ReignRaidFoodPolicy.ProtectVoyage(boat,.8);
        Assert.Equal(.4,boat.GrandfatherFactor);
        boat.Grandfathered=false;boat.ExemptionConsumed=true;
        ReignRaidFoodPolicy.ProtectVoyage(boat,1);
        Assert.False(boat.Grandfathered);
        var restored=JsonConvert.DeserializeObject<RaidFoodBoat>(JsonConvert.SerializeObject(new RaidFoodBoat{Grandfathered=true,GrandfatherFactor=.4,Hold=false,VoyageStartHours=10}))!;
        Assert.True(restored.Grandfathered);Assert.Equal(.4,restored.GrandfatherFactor);Assert.Equal(10,restored.VoyageStartHours);
    }
    [Fact]
    public void HistoryCapCannotEvictUnresolvedDamageActiveRaidsOrHolds()
    {
        var state=new RaidFoodState();
        state.Villages["v"]=new RaidFoodVillage{Capacity=.2,Active=new RaidFoodAttempt{Generation=3}};
        state.Boats["boat:new-id"]=new RaidFoodBoat{Hold=true};
        for(int i=0;i<1000;i++) state.Recent.Add(new RaidFoodReceipt{Generation=i,EndHours=i,Village="v"});
        ReignRaidFoodPolicy.Prune(state);
        Assert.Equal(512,state.Recent.Count); Assert.Equal(999,state.Recent[0].Generation);
        var restored=JsonConvert.DeserializeObject<RaidFoodState>(JsonConvert.SerializeObject(state))!;
        Assert.Equal(.2,restored.Villages["v"].Capacity); Assert.Equal(3,restored.Villages["v"].Active.Generation);
        Assert.True(restored.Boats["boat:new-id"].Hold);
    }
    [Fact]
    public void MissingCollectionsAreExplicitAndSequenceCannotGoBelowSavedNativeTokens()
    {
        var state=new RaidFoodState{Villages=null!,Boats=null!,Recent=null!};
        ReignRaidFoodPolicy.NormalizeCollections(state);
        Assert.True(state.MalformedCollections);Assert.True(state.Unsupported>0);
        state.Villages["Village"]=new RaidFoodVillage{LastClosedNativeGeneration=500};
        ReignRaidFoodPolicy.NormalizeCollections(state);
        Assert.Equal(500,state.Sequence);
        Assert.False(state.Villages.ContainsKey("village"));
        state.Villages["null"]=null!;state.Boats["null"]=null!;
        ReignRaidFoodPolicy.NormalizeCollections(state);
        Assert.DoesNotContain("null",state.Villages.Keys);
        Assert.Equal(2,state.MalformedEntries.Count);
    }
    [Fact]
    public void FishingConservesOverManyChangingRatesIncludingFeatureOffAndZero()
    {
        var random=new Random(1234);double carry=.37,expected=carry;long actual=0;
        for(int i=0;i<10000;i++)
        {
            int planned=random.Next(0,8);double factor=i%7==0?0:i%11==0?1:random.NextDouble();
            int caught=ReignRaidFoodPolicy.Catch(planned,factor,ref carry);
            Assert.InRange(caught,0,planned);actual+=caught;expected+=planned*factor;
            Assert.InRange(carry,0,1-double.Epsilon);
        }
        Assert.InRange(Math.Abs(actual+carry-expected),0,1e-8);
    }
    [Fact]
    public void OlderPriceSettingsGainOnlyMissingNewPreferences()
    {
        var old=WarEconomyOptions.DefaultValues();
        foreach(var key in old.Keys.Where(k=>k.StartsWith("economyRaid")).ToArray()) old.Remove(key);
        for(int tier=0;tier<8;tier++) old["economyRecruitTier"+tier]=10.5;
        Assert.True(WarEconomyOptions.TryCreate(old,false,out var settings,out var error),error);
        for(int tier=0;tier<8;tier++) Assert.Equal(10.5,settings.RecruitMultiplier(tier));
        Assert.Equal(40,settings.Number("economyRaidPartialDays"));
        old["economyRaidPartialDays"]=70d;old["economyRaidFullDays"]=60d;
        Assert.False(WarEconomyOptions.TryCreate(old,false,out _,out _));
    }
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-.1)]
    public void InvalidDamageCannotResetARecordedVillage(double invalid)
        => Assert.Throws<ArgumentOutOfRangeException>(()=>ReignRaidFoodPolicy.Damage(.4,invalid,.25));
}
