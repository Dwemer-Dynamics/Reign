using ReignBeta.World;
using Newtonsoft.Json;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class CampaignReportPolicyTests
{
    [Fact]
    public void Saved_pending_delivery_and_acknowledgment_survive_reload_without_regenerating_reports()
    {
        var order = new ReignCampaignOrderRecord { OrderId = "scout-order", Objective = "scout_report",
            Stage = "awaiting_report", PendingReportId = "report-a", PendingReportText = "Our local observation.",
            ReportLetterPending = true, NextReportLetterAttemptDay = 91214.5 };
        var loaded = JsonConvert.DeserializeObject<ReignCampaignOrderRecord>(JsonConvert.SerializeObject(order))!;
        Assert.Equal(order.PendingReportId, loaded.PendingReportId);
        Assert.Equal(order.PendingReportText, loaded.PendingReportText);
        Assert.Equal(order.NextReportLetterAttemptDay, loaded.NextReportLetterAttemptDay);
        Assert.False(ReignCampaignReportPolicy.CanFinishScouting(loaded.Objective, loaded.Stage,
            loaded.ReportLetterPending, loaded.PendingReportId, loaded.ReportLetterDeliveredId));
        order.ReportLetterPending = false;
        order.ReportLetterDeliveredId = order.PendingReportId;
        loaded = JsonConvert.DeserializeObject<ReignCampaignOrderRecord>(JsonConvert.SerializeObject(order))!;
        Assert.True(ReignCampaignReportPolicy.CanFinishScouting(loaded.Objective, loaded.Stage,
            loaded.ReportLetterPending, loaded.PendingReportId, loaded.ReportLetterDeliveredId));
        var legacy = JsonConvert.DeserializeObject<ReignCampaignOrderRecord>("{\"Status\":\"completed\",\"PendingReportId\":\"old-report\"}")!;
        Assert.False(legacy.ReportLetterPending);
        Assert.Equal(string.Empty, legacy.ReportLetterDeliveredId);
    }

    [Theory]
    [InlineData(true, "letter-a", "delivered", true)]
    [InlineData(true, "letter-a", "read", true)]
    [InlineData(true, "letter-a", "in_transit", false)]
    [InlineData(false, "letter-a", "delivered", false)]
    [InlineData(true, "", "delivered", false)]
    [InlineData(true, "letter-a", "unknown", false)]
    public void Only_a_delivered_or_already_read_letter_acknowledges_the_outbox(
        bool ok, string id, string status, bool expected) => Assert.Equal(expected,
        ReignCampaignReportPolicy.LetterDelivered(ok, id, status));

    [Theory]
    [InlineData("scout_report", "awaiting_report", false, "report-a", "report-a", true)]
    [InlineData("scout_report", "awaiting_report", true, "report-a", "report-a", false)]
    [InlineData("scout_report", "awaiting_report", false, "report-a", "report-b", false)]
    [InlineData("scout_report", "awaiting_report", false, "", "", false)]
    [InlineData("scout_report", "executing", false, "report-a", "report-a", false)]
    [InlineData("patrol", "awaiting_report", false, "report-a", "report-a", false)]
    [InlineData("scout_report", "awaiting_report", false, "Report-a", "report-a", false)]
    public void Completion_requires_the_current_exact_delivered_report(string objective, string stage,
        bool pending, string id, string delivered, bool expected) => Assert.Equal(expected,
        ReignCampaignReportPolicy.CanFinishScouting(objective, stage, pending, id, delivered));

    [Theory]
    [InlineData("completed", "The order is complete.")]
    [InlineData("failed", "The order ended without completion.")]
    [InlineData("active", "Here is my report.")]
    public void An_ordinary_report_never_requests_guidance(string status, string ending)
    {
        string body = ReignCampaignReportPolicy.Body("Lucon", "scout_report", "Diathma",
            "I observed two hostile parties nearby.", status, false);
        Assert.Contains("Lucon reports on scout report around Diathma", body);
        Assert.Contains("I observed two hostile parties nearby.", body);
        Assert.EndsWith(ending, body);
        Assert.DoesNotContain("paused", body);
        Assert.DoesNotContain("12 campaign hours", body);
        Assert.DoesNotContain("player", body);
        Assert.DoesNotContain("sa_", body);
    }

    [Fact]
    public void Only_real_guidance_uses_the_urgent_reply_source()
    {
        Assert.Equal("campaign_command_report", ReignCampaignReportPolicy.LetterSource(false));
        Assert.Equal("campaign_command_urgent_report", ReignCampaignReportPolicy.LetterSource(true));
        string body = ReignCampaignReportPolicy.Body("Lucon", "move", string.Empty,
            "The bridge is blocked.", "awaiting_guidance", true);
        Assert.Contains("paused for guidance", body);
        Assert.Contains("12 campaign hours", body);
        Assert.DoesNotContain("The order is complete.", body);
    }
}
