using ReignBeta.Economy;
using Newtonsoft.Json;

namespace Reign.Mcp.Tests;

public sealed class MedicalDischargeTests
{
    [Theory]
    [InlineData(0,60)][InlineData(75,45)][InlineData(150,30)][InlineData(225,15)]
    [InlineData(300,5)][InlineData(301,5)][InlineData(-50,60)][InlineData(250,11)]
    public void ExactCapAnchorsAndClamps(int medicine, int maximum)
        => Assert.Equal(maximum, ReignMedicalDischargePolicy.Maximum(100, medicine));

    [Fact]
    public void HardCapDoesNotRoundUpTinyParties()
    {
        Assert.Equal(0, ReignMedicalDischargePolicy.Maximum(1, 0));
        Assert.Equal(1, ReignMedicalDischargePolicy.Maximum(2, 0));
        Assert.Equal(0, ReignMedicalDischargePolicy.Maximum(19, 300));
        Assert.Equal(1, ReignMedicalDischargePolicy.Maximum(20, 300));
        for (int m = 0; m < 300; m++)
            Assert.True(ReignMedicalDischargePolicy.Maximum(10000, m) >= ReignMedicalDischargePolicy.Maximum(10000, m + 1));
    }

    [Fact]
    public void UniformRandomCountHasBothEndpointsAndCorrectMeanAndVariance()
    {
        var cohort = new Dictionary<string,int> { ["regular"] = 100 };
        var bins = new int[61]; double sum = 0, squared = 0;
        for (ulong seed = 0; seed < 61000; seed++)
        {
            int d = ReignMedicalDischargePolicy.Select(cohort, 0, seed).Values.Sum();
            bins[d]++; sum += d; squared += d*d;
        }
        Assert.All(bins, n => Assert.InRange(n, 800, 1200));
        double mean = sum / 61000;
        Assert.InRange(mean, 29.7, 30.3);
        Assert.InRange(squared / 61000 - mean*mean, 300, 320);
    }

    [Fact]
    public void SoldierSelectionIsProportionalAndIndependentOfStackOrderAndTierNames()
    {
        var a = new Dictionary<string,int> { ["elite"] = 10, ["recruit"] = 90 };
        var b = new Dictionary<string,int> { ["recruit"] = 90, ["elite"] = 10 };
        long elites = 0, recruits = 0;
        for (ulong seed = 0; seed < 5000; seed++)
        {
            var x = ReignMedicalDischargePolicy.Select(a, 0, seed);
            var y = ReignMedicalDischargePolicy.Select(b, 0, seed);
            Assert.Equal(x.OrderBy(k => k.Key), y.OrderBy(k => k.Key));
            elites += x.GetValueOrDefault("elite"); recruits += x.GetValueOrDefault("recruit");
        }
        Assert.InRange((double)elites / (elites+recruits), .095, .105);
    }

    [Fact]
    public void OldWoundsAreRetainedAndCapturedOrRescuedMembersCannotSubstitute()
    {
        var join = new Dictionary<string,int[]> { ["t"] = new[] {100,40,0} };
        var wounds = new Dictionary<string,int> { ["t"] = 20 };
        var none = new Dictionary<string,int>();
        Assert.True(ReignMedicalDischargePolicy.Retained(join, new Dictionary<string,int[]> { ["t"] = new[]{100,60,0} }, wounds, none, none));
        Assert.False(ReignMedicalDischargePolicy.Retained(join, new Dictionary<string,int[]>(), wounds, none, none));
        Assert.False(ReignMedicalDischargePolicy.Retained(join, new Dictionary<string,int[]> { ["t"] = new[]{110,70,0} }, wounds, none, none));
        Assert.False(ReignMedicalDischargePolicy.Retained(join, new Dictionary<string,int[]> { ["t"] = new[]{100,59,0} }, wounds, none, none));
    }

    [Fact]
    public void SaveRoundtripRetainsSeedSelectionsAndUncertainJournalWithoutReroll()
    {
        var e = new MedicalEvent { Id = 1, Seed = ulong.MaxValue, Status = "uncertain", Parties = new() {
            new MedicalParty { Key = "native-generation", Status = "applying", Medicine = 75,
                Selected = ReignMedicalDischargePolicy.Select(new Dictionary<string,int> { ["t"] = 100 },75,ulong.MaxValue) } } };
        var copy = JsonConvert.DeserializeObject<MedicalEvent>(JsonConvert.SerializeObject(e))!;
        Assert.Equal(e.Seed, copy.Seed); Assert.Equal("uncertain",copy.Status);
        Assert.Equal("applying", copy.Parties[0].Status);
        Assert.Equal(e.Parties[0].Selected, copy.Parties[0].Selected);
    }
}
