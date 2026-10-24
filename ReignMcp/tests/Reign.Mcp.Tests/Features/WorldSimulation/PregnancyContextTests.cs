using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Family;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class PregnancyContextTests
{
    private static PregnancyFacts Pregnancy() => new() { PregnancyId = "pregnancy", MotherId = "mother", MotherName = "Ingunde",
        BiologicalFatherId = "father", BiologicalFatherName = "Actual Father", LegalFatherId = "husband", LegalFatherName = "Husband",
        SpouseId = "husband", SpouseName = "Husband", SpouseAtConceptionId = "husband", MarriageAtConceptionKnown = true,
        Active = true, Illicit = true, TimingKnown = true, ConceptionDay = 10, DueDay = 46, WorldDay = 28 };
    private static PregnancyKnowledge Knowledge(bool paternity = false) => new() {
        Pregnancy = true, Illicit = true, Paternity = paternity, EvidenceId = "accepted_source" };

    [Theory]
    [InlineData(27.999, false)]
    [InlineData(28, true)]
    [InlineData(46, true)]
    [InlineData(50, true)]
    public void VisibilityStartsExactlyHalfwayAndWaitsForActualBirth(double day, bool visible)
    {
        var f = Pregnancy(); f.WorldDay = day;
        Assert.Equal(visible, PregnancyContextPolicy.Visible(f));
    }
    [Fact]
    public void UnknownOrInvalidTimingDoesNotInventVisibleStage()
    {
        var f = Pregnancy(); f.TimingKnown = false;
        Assert.Null(PregnancyContextPolicy.Progress(f)); Assert.False(PregnancyContextPolicy.Visible(f));
        f.TimingKnown = true; f.DueDay = double.NaN; Assert.Null(PregnancyContextPolicy.Progress(f));
    }
    [Fact]
    public void MotherAlwaysKnowsIncludingLegacyFlagWithoutFatherOrDates()
    {
        var f = new PregnancyFacts { MotherId = "mother", MotherName = "Ingunde", Active = true };
        string prompt = PregnancyContextPolicy.SelfPrompt(f);
        Assert.Contains("ARE pregnant", prompt); Assert.Contains("timing is unknown", prompt);
        Assert.Contains("paternity is unknown", prompt); Assert.DoesNotContain("Private biological father:", prompt);
    }
    [Fact]
    public void VisiblePregnancyDoesNotRevealFatherAndRemoteContactDoesNotObserve()
    {
        var f = Pregnancy();
        Assert.Contains("is pregnant", PregnancyContextPolicy.ObserverPrompt(f, new(), true));
        Assert.DoesNotContain("Actual Father", PregnancyContextPolicy.ObserverPrompt(f, new(), true));
        Assert.DoesNotContain("Ingunde", PregnancyContextPolicy.ObserverPrompt(f, new(), false));
    }
    [Fact]
    public void LegitimatePregnancyCannotBeExposedEvenWithCompleteKnowledge()
    {
        var f = Pregnancy(); f.Illicit = false; f.BiologicalFatherId = f.LegalFatherId;
        Assert.NotEmpty(PregnancyContextPolicy.ExposureError(f, Knowledge(true), "mother", true));
        Assert.Contains("ordinary family", PregnancyContextPolicy.SelfPrompt(f));
    }
    [Fact]
    public void ExposureRequiresKnowledgeOfIllicitCircumstancesAndSource()
    {
        var f = Pregnancy();
        Assert.NotEmpty(PregnancyContextPolicy.ExposureError(f, new() { Pregnancy = true }, "mother", false));
        var k = Knowledge(); k.EvidenceId = "";
        Assert.NotEmpty(PregnancyContextPolicy.ExposureError(f, k, "mother", false));
    }
    [Fact]
    public void MotherOnlyExposureKeepsFatherSecretButAllowsLaterNaming()
    {
        var f = Pregnancy(); Assert.Empty(PregnancyContextPolicy.ExposureError(f, Knowledge(), "mother", false));
        f.MotherExposed = true;
        Assert.DoesNotContain("Actual Father", PregnancyContextPolicy.ObserverPrompt(f, new(), false));
        Assert.NotEmpty(PregnancyContextPolicy.ExposureError(f, Knowledge(), "mother", false));
        Assert.Empty(PregnancyContextPolicy.ExposureError(f, Knowledge(true), "mother", true));
        f.FatherExposed = true; Assert.NotEmpty(PregnancyContextPolicy.ExposureError(f, Knowledge(true), "mother", true));
    }
    [Fact]
    public void FatherTargetRequiresHisConfirmedIdentityAndNamesBoth()
    {
        var f = Pregnancy();
        Assert.NotEmpty(PregnancyContextPolicy.ExposureError(f, Knowledge(), "father", true));
        Assert.NotEmpty(PregnancyContextPolicy.ExposureError(f, Knowledge(true), "father", false));
        Assert.Empty(PregnancyContextPolicy.ExposureError(f, Knowledge(true), "father", true));
        Assert.NotEmpty(PregnancyContextPolicy.ExposureError(f, Knowledge(true), "husband", true));
        Assert.Equal(-50, PregnancyContextPolicy.MotherPenalty); Assert.Equal(-5, PregnancyContextPolicy.FatherPenalty);
    }
    [Fact]
    public void UnilateralDeclarationCannotBeTurnedIntoBargainOrDeferredAction()
    {
        var a = new ActionAgreement { PlayerId = "player", NpcId = "mother", ProposerId = "player", Origin = ActionOrigin.Conversation };
        a.Clauses.Add(new ActionClause { ActionId = PregnancyContextPolicy.ActionId, ActorId = "player", RecipientId = "mother",
            Arguments = new() { ["pregnancyId"] = "pregnancy", ["disclosure"] = "mother_only" } });
        Assert.True(AgreementRules.IsPlayerDeclaration(a));
        Assert.Empty(AgreementRules.Validate(a, ReignActionCatalog.Find, 1));
        a.PlayerAcceptedHash = a.TermsHash(); Assert.True(a.HasRequiredConsents); Assert.False(a.HasBothConsents);
        a.Clauses[0].DueDay = 5; Assert.NotEmpty(AgreementRules.Validate(a, ReignActionCatalog.Find, 1));
        a.Clauses[0].DueDay = 0; a.Clauses.Add(new() { ActionId = "give.gold", ActorId = "player", RecipientId = "mother" });
        Assert.False(AgreementRules.IsPlayerDeclaration(a)); Assert.NotEmpty(AgreementRules.Validate(a, ReignActionCatalog.Find, 1));
    }
    [Fact]
    public void BirthOrEndedPregnancyRemovesExposureWithoutErasingHistory()
    {
        var f = Pregnancy(); f.Active = false;
        Assert.Empty(PregnancyContextPolicy.SelfPrompt(f)); Assert.NotEmpty(PregnancyContextPolicy.ExposureError(f, Knowledge(true), "mother", true));
        Assert.Equal("father", f.BiologicalFatherId); Assert.Equal("husband", f.LegalFatherId);
    }
    [Fact]
    public void LicensedNativeAssemblyRetainsReadOnlyScheduleBinding()
    {
        string? root = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH");
        string installation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".reign", "installation.json");
        if (string.IsNullOrWhiteSpace(root) && File.Exists(installation))
        {
            using var record = JsonDocument.Parse(File.ReadAllText(installation));
            root = record.RootElement.GetProperty("bannerlordRoot").GetString();
        }
        Assert.False(string.IsNullOrWhiteSpace(root), "Licensed Bannerlord path is required for native pregnancy compatibility coverage.");
        using var file = File.OpenRead(Path.Combine(root!, "bin", "Win64_Shipping_Client", "TaleWorlds.CampaignSystem.dll"));
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        var behavior = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == "PregnancyCampaignBehavior");
        Assert.Contains(behavior.GetFields(), field => metadata.GetString(metadata.GetFieldDefinition(field).Name) == "_heroPregnancies");
        var schedule = behavior.GetNestedTypes().Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == "Pregnancy");
        var fields = schedule.GetFields().Select(field => metadata.GetString(metadata.GetFieldDefinition(field).Name)).ToArray();
        Assert.Contains("Mother", fields); Assert.Contains("Father", fields); Assert.Contains("DueDate", fields);
    }
}
