using Newtonsoft.Json;
using ReignBeta.World;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class CampaignControlConsentTests
{
    private static ReignCampaignOrderRecord Reload(string authority = "explicit_agreement", string status = "active") =>
        JsonConvert.DeserializeObject<ReignCampaignOrderRecord>(JsonConvert.SerializeObject(new ReignCampaignOrderRecord
        {
            OrderId = "accepted-hold", CommanderHeroStringId = "lord_1_1", IssuerHeroStringId = "main_hero",
            AuthorityMode = authority, Status = status, Objective = "hold_position", TermsJson = "{\"positionX\":577}"
        }))!;

    private static bool Allowed(ReignCampaignOrderRecord? order, bool proposal = false, bool accepted = false,
        string commander = "lord_1_1", string issuer = "main_hero", bool structured = true, bool console = false) =>
        ReignCampaignControlConsent.Validate(order!, commander, issuer, structured, console, proposal, accepted, out _);

    [Theory]
    [InlineData("explicit_agreement")]
    [InlineData("operator")]
    [InlineData("sovereign_authority")]
    public void Mutual_proposal_is_allowed_but_execution_requires_exact_saved_authorization(string authority)
    {
        var order = Reload(authority);
        string original = JsonConvert.SerializeObject(order);
        Assert.True(Allowed(order, proposal: true));
        Assert.False(Allowed(order));
        Assert.True(Allowed(order, accepted: true));
        Assert.Equal(original, JsonConvert.SerializeObject(order));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Accepted_control_cannot_substitute_the_commander_or_original_issuer(bool proposal)
    {
        var order = Reload();
        Assert.False(Allowed(order, proposal, true, commander: "lord_2_1"));
        Assert.False(Allowed(order, proposal, true, issuer: "lord_2_1"));
        order.CommanderHeroStringId = "";
        Assert.False(Allowed(order, proposal, true, commander: ""));
        order = Reload(); order.IssuerHeroStringId = "";
        Assert.False(Allowed(order, proposal, true, issuer: ""));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public void Missing_or_terminal_orders_cannot_be_proposed_or_changed(string status)
    {
        Assert.False(Allowed(null, proposal: true, accepted: true));
        Assert.False(Allowed(Reload(status: status), proposal: true, accepted: true));
        Assert.False(Allowed(Reload(status: status), accepted: true, console: true));
    }

    [Theory]
    [InlineData("explicit_agreement")]
    [InlineData("operator")]
    public void Unilateral_controls_stay_blocked_and_console_origin_does_not_forge_structured_consent(string authority)
    {
        var order = Reload(authority);
        Assert.False(Allowed(order, structured: false));
        Assert.False(Allowed(order, proposal: true, structured: false));
        Assert.True(Allowed(order, structured: false, console: true));
        Assert.False(Allowed(order, console: true));
        Assert.True(Allowed(order, accepted: true, console: true));
    }

    [Fact]
    public void Existing_nonbinding_authority_route_is_preserved() =>
        Assert.True(Allowed(Reload("sovereign_authority"), structured: false));
}
