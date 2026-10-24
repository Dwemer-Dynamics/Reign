using Reign.Mcp.Server;
using ReignBeta.Shared.Characters;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class NpcLivesHarnessTests
{
    [Fact]
    public void RelocationCommerceScopeIsExactNestedAndExceptionSafe()
    {
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor(null!));
        Assert.Throws<ArgumentException>(() => NpcPartyRelocationScope.Enter(" "));
        var outer = NpcPartyRelocationScope.Enter("party");
        var inner = NpcPartyRelocationScope.Enter("party");
        Assert.True(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("Party"));
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("other"));
        outer.Dispose(); outer.Dispose();
        Assert.True(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
        using (NpcPartyRelocationScope.Enter("other"))
        {
            inner.Dispose();
            Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
            Assert.True(NpcPartyRelocationScope.SuppressesCommerceFor("other"));
        }
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("other"));
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using (NpcPartyRelocationScope.Enter("party")) throw new InvalidOperationException("native entry failed");
        }));
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
    }

    [Fact]
    public void RelocationCommerceScopeCannotSuppressOrBeReleasedFromAnotherThread()
    {
        using var scope = NpcPartyRelocationScope.Enter("party");
        bool foreignSuppressed = true;
        Exception? releaseError = null;
        Exception? threadError = null;
        var thread = new Thread(() => threadError = Record.Exception(() =>
        {
            foreignSuppressed = NpcPartyRelocationScope.SuppressesCommerceFor("party");
            releaseError = Record.Exception(scope.Dispose);
            using (NpcPartyRelocationScope.Enter("other")) Assert.True(NpcPartyRelocationScope.SuppressesCommerceFor("other"));
        }));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(threadError);
        Assert.False(foreignSuppressed);
        Assert.IsType<InvalidOperationException>(releaseError);
        Assert.True(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("other"));
        scope.Dispose();
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
    }

    [Fact]
    public void MeetingInterceptionIsRegisteredAndItsReadOnlyReadinessIsCataloged()
    {
        string root = TestOptions.FindWorkspace();
        string startup = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Platform/SubModule.cs"));
        Assert.Contains("ReignNpcLivesPatches.Apply();", startup);
        Assert.Contains("ReignNpcLivesPatches.Unapply();", startup);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("relocationEntrySafety").GetString()!;
        foreach (string field in new[] { "meetingInterception", "12 owned Harmony hooks", "relocationCommerceSuppressed", "Synchronous", "no new mutation or consent authority" })
            Assert.Contains(field, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
        {
            string help = File.ReadAllText(Path.Combine(root, path));
            Assert.Contains("Summons entry safety (2026-10-12)", help);
            Assert.Contains("12 owned Harmony hooks", help);
        }
    }

    [Fact]
    public void SoloCampObservationPreservesTheGuardedSnapshotBoundary()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("soloSummonsCamps").GetString()!;
        foreach (string field in new[] { "summonsCamp", "attendanceMode", "SoloAttendance", "20", "original position", "no new mutation or consent authority" })
            Assert.Contains(field, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Solo summons camps (2026-10-12)", File.ReadAllText(Path.Combine(root, path)));
    }

    [Fact]
    public void PartyContentPreviewsAreBoundedButDigestsCoverEveryRowRegardlessOfOrder()
    {
        var members = Enumerable.Range(0, 80).Select(i => new NpcPartyRosterEntry { CharacterId = $"troop_{i:D3}", Number = i + 1, Xp = i }).ToList();
        var prisoners = Enumerable.Range(0, 70).Select(i => new NpcPartyRosterEntry { CharacterId = $"prisoner_{i:D3}", Number = 1 }).ToList();
        var items = Enumerable.Range(0, 90).Select(i => new NpcPartyItemEntry { ItemId = $"item_{i:D3}", ModifierId = "fine", Amount = 2 }).ToList();
        var original = NpcPartyContentObservation.Capture("native_party", members, prisoners, items);
        var reordered = NpcPartyContentObservation.Capture("native_party", members.AsEnumerable().Reverse(), prisoners.AsEnumerable().Reverse(), items.AsEnumerable().Reverse());
        Assert.True(original.Truncated);
        Assert.Equal(64, original.Members.Count); Assert.Equal(64, original.Prisoners.Count); Assert.Equal(64, original.Inventory.Count);
        Assert.Equal(80, original.Digest.MemberRows); Assert.Equal(70, original.Digest.PrisonerRows); Assert.Equal(90, original.Digest.InventoryRows);
        Assert.Equal(3240, original.Digest.MemberCount); Assert.Equal(70, original.Digest.PrisonerCount); Assert.Equal(180, original.Digest.ItemCount);
        Assert.Equal(original.Digest.MembersSha256, reordered.Digest.MembersSha256);
        Assert.Equal(original.Digest.PrisonersSha256, reordered.Digest.PrisonersSha256);
        Assert.Equal(original.Digest.InventorySha256, reordered.Digest.InventorySha256);
        members[79].Wounded++; prisoners[69].Xp++; items[89].ModifierId = "rusty";
        var changed = NpcPartyContentObservation.Capture("native_party", members, prisoners, items);
        Assert.NotEqual(original.Digest.MembersSha256, changed.Digest.MembersSha256);
        Assert.NotEqual(original.Digest.PrisonersSha256, changed.Digest.PrisonersSha256);
        Assert.NotEqual(original.Digest.InventorySha256, changed.Digest.InventorySha256);
    }
    [Fact]
    public void PartyContentDigestsDistinguishIdentityQuantityInjuryExperienceAndItemVariants()
    {
        NpcPartyContentObservation Observe(string troop = "hero", int count = 2, int wounded = 1, int xp = 10,
            string item = "a|b", string modifier = "c", int amount = 3) => NpcPartyContentObservation.Capture("party",
                new[] { new NpcPartyRosterEntry { CharacterId = troop, Number = count, Wounded = wounded, Xp = xp } },
                Array.Empty<NpcPartyRosterEntry>(), new[] { new NpcPartyItemEntry { ItemId = item, ModifierId = modifier, Amount = amount } });
        var baseline = Observe();
        foreach (var changed in new[] { Observe(troop: "other"), Observe(count: 3), Observe(wounded: 0), Observe(xp: 11) })
            Assert.NotEqual(baseline.Digest.MembersSha256, changed.Digest.MembersSha256);
        foreach (var changed in new[] { Observe(item: "a", modifier: "b|c"), Observe(modifier: "fine"), Observe(amount: 4) })
            Assert.NotEqual(baseline.Digest.InventorySha256, changed.Digest.InventorySha256);
        Assert.Equal(64, baseline.Digest.MembersSha256.Length);
        Assert.NotEqual(baseline.Digest.MembersSha256, baseline.Digest.PrisonersSha256);
        Assert.False(baseline.Truncated);
        Assert.Equal("party", baseline.Digest.PartyId);
    }
    [Fact]
    public void PartyContentObservationAndRelocationDigestsAreCatalogedWithoutNewAuthority()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("partyContentObservation").GetString()!;
        foreach (string field in new[] { "partyContents", "64", "SHA-256", "all rows", "TargetRelocationBefore", "TargetRelocationAfter", "TargetRelocationDay", "no new mutation or consent authority" })
            Assert.Contains(field, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Party contents observation (2026-10-12)", File.ReadAllText(Path.Combine(root, path)));
    }
    [Fact]
    public void ScheduledMeetingObservationsRetainTheExistingNativeGates()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("scheduledSummons").GetString()!;
        foreach (string field in new[] { "meetingJourneys (64)", "meetingDuty", "meetingHeld", "meetingSettlementHeld",
            "partyDecisionLock", "partyLeaderId", "partyPosition", "partyTargetSettlementId", "no new mutation or consent authority" })
            Assert.Contains(field, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Scheduled summons observation (2026-10-12)", File.ReadAllText(Path.Combine(root, path)));
    }
    [Fact]
    public void ControlledCleanupEvidenceAndRecoveryAreCataloged()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("controlledEncounter").GetString()!;
        Assert.Contains("120 seconds", contract);
        Assert.Contains("settled and cleanup", contract);
        Assert.Contains("exact armed session/campaign", contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Controlled outcome cleanup (2026-10-12)", File.ReadAllText(Path.Combine(root, path)));
    }
    [Theory]
    [InlineData("observe", "")]
    [InlineData("place", "")]
    [InlineData("start", ",\"objectiveId\":\"duel\"")]
    [InlineData("finish", ",\"encounterId\":\"encounter\",\"outcome\":\"won\"")]
    [InlineData("encounter", ",\"objectiveId\":\"duel\",\"outcome\":\"won\"")]
    [InlineData("encounter", ",\"objectiveId\":\"duel\",\"outcome\":\"lost\"")]
    [InlineData("encounter", ",\"objectiveId\":\"hideout\",\"outcome\":\"withdrawn\"")]
    public void AcceptsOnlyBoundedMechanicCases(string phase, string fields) => Assert.Single(
        NpcLivesTestTools.ParseCases("[{\"caseId\":\"case_1\",\"storyId\":\"story_1\",\"phase\":\"" + phase + "\"" + fields + "}]"));
    [Theory]
    [InlineData("[{\"caseId\":\"snapshot\",\"heroId\":\"companion\",\"phase\":\"snapshot\"}]")]
    [InlineData("[{\"caseId\":\"prepare\",\"heroId\":\"companion\",\"phase\":\"prepare\",\"templateId\":\"authored\",\"chapterId\":\"duel\"}]")]
    [InlineData("[{\"caseId\":\"prepare\",\"heroId\":\"companion\",\"phase\":\"prepare\",\"templateId\":\"authored\",\"chapterId\":\"duel\",\"locationId\":\"town\"}]")]
    [InlineData("[{\"caseId\":\"prepare\",\"heroId\":\"companion\",\"phase\":\"prepare\",\"templateId\":\"outing\",\"chapterId\":\"visit\",\"invitationKind\":\"leave\"}]")]
    public void AcceptsExactCharacterSnapshotAndUnacceptedAuthoredInvitation(string input) => Assert.Single(NpcLivesTestTools.ParseCases(input));
    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"place\",\"locationId\":\"other_town\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"place\",\"outcome\":\"won\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"place\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"snapshot\",\"invitationKind\":\"leave\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"prepare\",\"templateId\":\"outing\",\"chapterId\":\"visit\",\"invitationKind\":\"forced_departure\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"encounter\",\"objectiveId\":\"duel\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"encounter\",\"objectiveId\":\"hideout\",\"outcome\":\"rescued\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"encounter\",\"objectiveId\":\"duel\",\"outcome\":\"won\",\"encounterId\":\"forged\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"prepare\",\"templateId\":\"story\",\"chapterId\":\"duel\",\"locationId\":\"\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"snapshot\",\"locationId\":\"town\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"prepare\",\"templateId\":\"story\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"prepare\",\"templateId\":\"story\",\"chapterId\":\"duel\",\"consent\":\"true\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"snapshot\",\"storyId\":\"story\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"observe\",\"storyId\":\"story\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"finish\",\"encounterId\":\"e\",\"outcome\":\"rescued\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"observe\",\"outcome\":\"won\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"observe\",\"enrollment\":\"forged\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"observe\",\"phase\":\"finish\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"observe\"},{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"observe\"}]")]
    public void RejectsUnboundedOrFabricatedAuthority(string input) => Assert.ThrowsAny<ArgumentException>(() => NpcLivesTestTools.ParseCases(input));
}
