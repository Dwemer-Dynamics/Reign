using Reign.Core.Contracts.Court;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class PatronageVisitorRulesTests
{
    [Fact]
    public void PublicPatronageCardsDoNotExposePrivateGenerationInstructions()
    {
        Assert.All(ReignPatronageCatalog.Templates, template =>
        {
            string summary = ReignPatronageRules.PublicSummary(template);
            Assert.Contains(template.Title, summary);
            Assert.DoesNotContain(template.Premise, summary);
            Assert.DoesNotContain("unimplemented", summary);
        });
    }

    [Fact]
    public void CommissionRecordPreservesActualAgreedWordsAndDoesNotInventMissingContent()
    {
        const string player = "Use the corrected account. Five entries need not mean five battles.";
        const string creator = "Agreed. I will preserve that uncertainty for your household.";
        string description = ReignPatronageRules.AgreedWorkDescription("Chronicle", player, creator);
        Assert.Contains("Ruler: " + player, description);
        Assert.Contains("Creator: " + creator, description);
        Assert.DoesNotContain(creator, ReignPatronageRules.AgreedWorkDescription("Chronicle", "", creator));
        Assert.Contains("surviving discussion", ReignPatronageRules.AgreedWorkDescription("Chronicle", player, ""));
    }

    [Theory]
    [InlineData("battle_completed", true, false, false, false, "victory")]
    [InlineData("battle_completed", false, true, true, true, "")]
    [InlineData("hero_prisoner_released", false, true, false, false, "release")]
    [InlineData("hero_killed", false, false, true, false, "death")]
    [InlineData("settlement_owner_changed", false, false, false, true, "settlement")]
    [InlineData("peace_made", false, false, false, false, "reconciliation")]
    [InlineData("royal_proclamation", true, true, true, true, "")]
    [InlineData("hero_relation_changed", true, true, true, true, "")]
    [InlineData("court_life_familyvisit", true, true, true, true, "")]
    public void HistoricalPlotsRequireTypedNativeOutcomesAndTheCorrectOwnKingdomRole(string type, bool winner,
        bool released, bool victim, bool owner, string expected)
        => Assert.Equal(expected, ReignPatronageRules.PublicNativeHistoryContext(type, winner, released, victim, owner));

    [Theory]
    [InlineData(ReignPatronageReach.Court, 500, 0)]
    [InlineData(ReignPatronageReach.Settlement, 2000, 1)]
    [InlineData(ReignPatronageReach.Regional, 5000, 3)]
    [InlineData(ReignPatronageReach.Kingdom, 8000, 7)]
    public void FundingBuysTheAgreedReachAndDelivery(ReignPatronageReach reach, int gold, int days)
    {
        Assert.Equal(gold, ReignPatronageRules.Cost(reach));
        Assert.Equal(days, ReignPatronageRules.DeliveryDays(reach, ReignPatronageObjective.Praise));
        Assert.Equal(days, ReignPatronageRules.DeliveryDays(reach, ReignPatronageObjective.Loyalty));
    }

    [Fact]
    public void CatalogContainsFortyDistinctSituationsWithGroundedHistoricalRequirements()
    {
        var all = ReignPatronageCatalog.Templates;
        Assert.True(all.Count >= 40);
        Assert.Equal(all.Count, all.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, template =>
        {
            Assert.False(string.IsNullOrWhiteSpace(template.Premise));
            Assert.False(string.IsNullOrWhiteSpace(template.CreatorRole));
            Assert.Same(template, ReignPatronageCatalog.Find(template.Id));
            Assert.Equal(template.RequiresHistoricalSubject, template.RequiredContext.Length > 0);
        });
        Assert.True(all.Count(x => x.SupportsPublicPerformance) >= 20);
        Assert.True(all.Count(x => !x.SupportsPublicPerformance) >= 8);
        Assert.True(ReignPatronageCatalog.Find("patronage-victory-ballad")!.RequiresHistoricalSubject);
        Assert.True(ReignPatronageCatalog.Find("patronage-memorial-work")!.RequiresHistoricalSubject);
        Assert.True(ReignPatronageCatalog.Find("patronage-spouse-poem")!.RequiresSpouse);
        Assert.True(ReignPatronageCatalog.Find("patronage-child-discovery-book")!.RequiresChild);
    }

    [Fact]
    public void RivalHousePraiseRequestsGroundingBeforeClaimingAContribution()
    {
        string premise = ReignPatronageCatalog.Find("patronage-rival-house-praise")!.Premise;
        Assert.Contains("Ask the ruler to name another real house", premise, StringComparison.Ordinal);
        Assert.Contains("specific contribution the ruler's records support", premise, StringComparison.Ordinal);
        Assert.Contains("offer only a conditional outline", premise, StringComparison.Ordinal);
        Assert.Contains("do not imply that a contribution", premise, StringComparison.Ordinal);
    }

    [Fact]
    public void LoyaltyStartsAtSettlementTierAndRegionalNeedsThreeDistinctDestinations()
    {
        var template = ReignPatronageCatalog.Find("patronage-roadside-storyteller")!;
        Assert.False(ReignPatronageRules.Supports(template, ReignPatronageObjective.Loyalty, ReignPatronageReach.Court, 5));
        Assert.True(ReignPatronageRules.Supports(template, ReignPatronageObjective.Praise, ReignPatronageReach.Court, 1));
        Assert.True(ReignPatronageRules.Supports(template, ReignPatronageObjective.Loyalty, ReignPatronageReach.Settlement, 1));
        Assert.False(ReignPatronageRules.Supports(template, ReignPatronageObjective.Loyalty, ReignPatronageReach.Regional, 2));
        Assert.True(ReignPatronageRules.Supports(template, ReignPatronageObjective.Loyalty, ReignPatronageReach.Regional, 3));
        Assert.True(ReignPatronageRules.Supports(template, ReignPatronageObjective.Loyalty, ReignPatronageReach.Kingdom, 1));
        Assert.False(ReignPatronageRules.Supports(template, ReignPatronageObjective.Loyalty, ReignPatronageReach.Kingdom, 0));
    }

    [Fact]
    public void SmallWorksCannotBeTurnedIntoAutomaticKingdomRewards()
    {
        var personal = ReignPatronageCatalog.Find("patronage-spouse-portrait")!;
        Assert.True(ReignPatronageRules.Supports(personal, ReignPatronageObjective.PersonalWork, ReignPatronageReach.Court, 1));
        Assert.Equal(1, ReignPatronageRules.DeliveryDays(ReignPatronageReach.Court, ReignPatronageObjective.PersonalWork));
        Assert.False(ReignPatronageRules.Supports(personal, ReignPatronageObjective.Praise, ReignPatronageReach.Kingdom, 8));
        Assert.False(ReignPatronageRules.Supports(personal, ReignPatronageObjective.PersonalWork, ReignPatronageReach.Kingdom, 8));
        Assert.False(ReignPatronageRules.Supports(personal, (ReignPatronageObjective)999, ReignPatronageReach.Court, 1));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(40, 45)]
    [InlineData(95, 100)]
    [InlineData(98, 100)]
    [InlineData(100, 100)]
    public void LoyaltyIsOneFivePointDeliveryWithinNativeBounds(float before, float after)
    {
        Assert.Equal(after, ReignPatronageRules.ApplyLoyalty(before));
        Assert.Equal(3, ReignPatronageRules.PraiseRelation);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(29, true)]
    public void PraiseFundingRequiresAtLeastOneEligibleNoble(int audienceCount, bool expected)
        => Assert.Equal(expected, ReignPatronageRules.HasEligiblePraiseAudience(audienceCount));

    [Fact]
    public void MarriedPrivateInitiativeHonorsExactPercentageBoundaries()
    {
        int permitted = 0;
        for (int boldness = 0; boldness <= 100; boldness++)
        for (int honor = 0; honor <= 100; honor++)
        {
            bool actual = ReignNobleVisitorRules.CanInitiatePrivateRomance(true, boldness, honor);
            Assert.Equal(boldness >= 61 && honor <= 40, actual);
            if (actual) permitted++;
        }
        Assert.Equal(40 * 41, permitted);
        Assert.True(ReignNobleVisitorRules.CanInitiatePrivateRomance(false, 50, 90));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, true)]
    [InlineData(0, 1, false)]
    [InlineData(3, 1, false)]
    [InlineData(1, 3, false)]
    [InlineData(2, 0, false)]
    public void FamiliesHaveOneOrTwoParentsAndOneOrTwoAdultChildren(int parents, int children, bool allowed)
        => Assert.Equal(allowed, ReignNobleVisitorRules.ValidFamilyGroup(parents, children));

    [Fact]
    public void VisitorSelectionNeverInterruptsNativeObligations()
    {
        bool Eligible(bool noble = true, bool alive = true, bool active = true, bool prisoner = false,
            float age = 30, bool atWar = false, bool party = false, bool governor = false,
            bool office = false, bool assigned = false, bool quest = false, bool ruler = false)
            => ReignNobleVisitorRules.Eligible(noble, alive, active, prisoner, age, 18, atWar, party, governor, office, assigned, quest, ruler);
        Assert.True(Eligible());
        Assert.False(Eligible(noble: false));
        Assert.False(Eligible(alive: false));
        Assert.False(Eligible(active: false));
        Assert.False(Eligible(prisoner: true));
        Assert.False(Eligible(age: 17.999f));
        Assert.False(Eligible(atWar: true));
        Assert.False(Eligible(party: true));
        Assert.False(Eligible(governor: true));
        Assert.False(Eligible(office: true));
        Assert.False(Eligible(assigned: true));
        Assert.False(Eligible(quest: true));
        Assert.False(Eligible(ruler: true));
    }

    [Fact]
    public void StayRulesCoverTheirFullAgreedRange()
    {
        Assert.Equal(new[] { 3, 4, 5, 6, 7 }, Enumerable.Range(0, 5).Select(ReignNobleVisitorRules.StayDays));
        Assert.All(new[] { int.MinValue, -1, 0, 99, int.MaxValue }, x => Assert.InRange(ReignNobleVisitorRules.StayDays(x), 3, 7));
    }
}
