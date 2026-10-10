using System.Text.RegularExpressions;
using System.Text.Json;
using Reign.Core.Contracts.Actions;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ActionCoverageTests
{
    [Fact] public void NativeTestingInventoryContainsEveryRegisteredCommandExactlyOnce()
    {
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "docs", "agent", "action-system", "action-test-inventory.json")));
        var rows = inventory.RootElement.GetProperty("actions").EnumerateArray().ToArray();
        var ids = rows.Select(row => row.GetProperty("actionId").GetString()!).Order().ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ReignActionCatalog.All.Select(a => a.Id).Concat(ReignNativeActionCatalog.All.Select(a => a.Id)).Order().ToArray(), ids);
        Assert.All(rows, row => {
            Assert.True(File.Exists(Path.Combine(TestOptions.FindWorkspace(), row.GetProperty("source").GetString()!)));
            if (row.GetProperty("nativeStatus").GetString() == "passed") Assert.NotEmpty(row.GetProperty("evidence").EnumerateArray());
        });
    }
    [Fact] public void EveryWorldActionHasAnExposedAdapterOrAnExplicitRetirementOrAlias()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules", "WorldSimulation", "World", "ReignWorldActionType.cs"));
        var native = Regex.Matches(source, @"(?m)^\s*(\w+)\s*=").Select(m => m.Groups[1].Value).Order().ToArray();
        var registered = ReignActionCatalog.All.Select(a => a.Adapter).Where(a => !a.Contains(':')).Distinct().ToArray();
        var classified = registered.Concat(ReignActionCatalog.WorldActionExclusions.Keys).Order().ToArray();
        Assert.Equal(native, classified);
        Assert.All(ReignActionCatalog.WorldActionExclusions.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        Assert.DoesNotContain(ReignActionCatalog.All, a => ReignActionCatalog.WorldActionExclusions.ContainsKey(a.Adapter));
    }
    [Fact] public void AttackSettlementUsesTheApplicableNativeObjective()
    {
        var action = ReignActionCatalog.Find("attack_settlement")!;
        Assert.Equal("raid", ReignActionCatalog.ObjectiveFor(action, true));
        Assert.Equal("besiege_capture", ReignActionCatalog.ObjectiveFor(action, false));
    }
    [Fact] public void ChancellorOfficeChangesHaveTypedTermsAndNoImplicitSalary()
    {
        var appoint = ReignActionCatalog.Find("court.serve_chancellor")!;
        Assert.True(appoint.ConversationEligible);
        var salary = Assert.Single(appoint.Parameters);
        Assert.Equal("activeDailySalary", salary.Key);
        Assert.Equal("money", salary.Kind);
        Assert.True(salary.Required);
        var duties = Assert.Single(ReignActionCatalog.Find("court.set_chancellor_active")!.Parameters);
        Assert.Equal(new[] { "false", "true" }, duties.Choices.Order().ToArray());
        Assert.Empty(ReignActionCatalog.Find("court.leave_chancellor")!.Parameters);
    }
    [Fact] public void RebellionBackingCannotUseAnAbsentRulersConsent()
    {
        var facts = new ActionAuthorityFacts { ActorId = "sponsor", CounterpartyId = "rebel", ActingRulerId = "sponsor", RebelRulerId = "rebel", DestinationRulerId = "parent" };
        Assert.Empty(ReignActionAuthority.Validate("DiplomacyBackRebellion", facts));
        facts.RebelRulerId = "new-ruler";
        Assert.NotEmpty(ReignActionAuthority.Validate("DiplomacyBackRebellion", facts));
        facts.DestinationRulerId = "rebel";
        Assert.Empty(ReignActionAuthority.Validate("PoliticsResolveCivilWar", facts));
        facts.DestinationRulerId = "absent-parent";
        Assert.NotEmpty(ReignActionAuthority.Validate("PoliticsResolveCivilWar", facts));
    }
}
