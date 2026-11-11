using System.Text.Json;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class NordHouseholdContentTests
{
    private static string Data => Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "ModuleData");

    [Fact]
    public void EveryAuthoredNordHoldingHasAnIndependentSixAdultFamilyWithRealKinship()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "reign_nord_households.json")));
        var records = manifest.RootElement.GetProperty("records").EnumerateArray().ToArray();
        var lords = XDocument.Load(Path.Combine(Data, "reign_court_lords.xml")).Descendants("NPCCharacter").ToDictionary(x => x.Attribute("id")!.Value);
        var heroes = XDocument.Load(Path.Combine(Data, "reign_court_heroes.xml")).Descendants("Hero").ToDictionary(x => x.Attribute("id")!.Value);
        var clans = XDocument.Load(Path.Combine(Data, "reign_court_clans.xml")).Descendants("Faction").ToDictionary(x => x.Attribute("id")!.Value);
        Assert.Equal(78, records.Length);
        Assert.Equal(798, lords.Count);
        Assert.Equal(798, heroes.Count);
        Assert.Equal(133, clans.Count);
        Assert.Equal(78, records.Select(r => r.GetProperty("id").GetString()).Distinct().Count());
        var homes = records.GroupBy(r => r.GetProperty("home_settlement_id").GetString()!).ToArray();
        Assert.Equal(4, homes.Count(h => h.Key.StartsWith("town_N", StringComparison.Ordinal)));
        Assert.Equal(9, homes.Count(h => h.Key.StartsWith("castle_N", StringComparison.Ordinal)));
        Assert.Equal(lords.Count, lords.Values.Select(x => (string?)x.Attribute("name")!).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var family in homes)
        {
            string prefix = "reign_court_" + family.Key;
            string father = prefix + "_father", mother = prefix + "_mother";
            XElement clan = clans["reign_house_" + family.Key];
            Assert.Equal(6, family.Count());
            Assert.Equal("Culture.nord", (string?)clan.Attribute("culture"));
            Assert.Equal("Kingdom.nord", (string?)clan.Attribute("super_faction"));
            Assert.Equal("1", (string?)clan.Attribute("tier"));
            Assert.Equal("Hero." + father, (string?)clan.Attribute("owner"));
            Assert.Equal("Settlement." + family.Key, (string?)clan.Attribute("initial_home_settlement"));
            Assert.Null(clan.Attribute("settlement"));
            Assert.Equal("Hero." + mother, (string?)heroes[father].Attribute("spouse"));
            Assert.Equal("Hero." + father, (string?)heroes[mother].Attribute("spouse"));
            foreach (var row in family)
            {
                string id = row.GetProperty("id").GetString()!;
                XElement lord = lords[id], hero = heroes[id];
                Assert.Equal("Culture.nord", (string?)lord.Attribute("culture"));
                Assert.Equal("Lord", (string?)lord.Attribute("occupation"));
                Assert.Equal("true", (string?)lord.Attribute("is_hero"));
                Assert.Equal("Faction.reign_house_" + family.Key, (string?)hero.Attribute("faction"));
                int age = (int)lord.Attribute("age")!;
                Assert.InRange(age, 19, 44);
                Assert.Contains("landless Nord noble", (string?)hero.Attribute("text")!, StringComparison.Ordinal);
                Assert.NotEmpty(lord.Descendants("EquipmentSet"));
                Assert.NotNull(lord.Attribute("skill_template"));
                Assert.Equal(5, lord.Descendants("Trait").Count(t => new[] { "Valor", "Mercy", "Honor", "Generosity", "Calculating" }.Contains((string?)t.Attribute("id"))));
                Assert.EndsWith("_nord", (string?)lord.Descendants("face_key_template").Single().Attribute("value")!, StringComparison.Ordinal);
                if (id.Contains("_child_", StringComparison.Ordinal))
                {
                    Assert.Equal("Hero." + father, (string?)hero.Attribute("father"));
                    Assert.Equal("Hero." + mother, (string?)hero.Attribute("mother"));
                    Assert.True(44 - age >= 18 && 42 - age >= 17);
                    Assert.Null(hero.Attribute("spouse"));
                }
            }
        }
    }

    [Fact]
    public void NewCampaignRosterUsesExpandedCountsWithoutReplayingCompletedMigrationsOrGrantingResources()
    {
        string root = TestOptions.FindWorkspace();
        string legacy = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignCourtNobleCampaignBehavior.cs"));
        Assert.Contains("AuthoredHouseholdCount = 133", legacy, StringComparison.Ordinal);
        Assert.Contains("AuthoredCourtNobleCount = AuthoredHouseholdCount * 6", legacy, StringComparison.Ordinal);
        Assert.Contains("if (_householdClanMigrationVersion >= HouseholdClanMigrationVersion)", legacy, StringComparison.Ordinal);
        Assert.DoesNotContain("GiveGoldAction", legacy, StringComparison.Ordinal);
        Assert.DoesNotContain("ChangeOwnerOfSettlementAction", legacy, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLordParty", legacy, StringComparison.Ordinal);
        Assert.DoesNotContain("MarriageAction.Apply", legacy, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNordIdentityShipsCompleteMatchingProfilesNarrativeAndPortraitProducts()
    {
        string root = TestOptions.FindWorkspace();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "reign_nord_households.json")));
        using var current = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ReignServer/profiles/profile_catalog.json")));
        using var preserved = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ReignServer/profiles/profile_catalog_v4.json")));
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ReignBeta/PortraitCache/shared-portrait-inventory.json")));
        var currentRows = current.RootElement.GetProperty("profiles").EnumerateArray().ToDictionary(p => p.GetProperty("heroStringId").GetString()!);
        var preservedRows = preserved.RootElement.GetProperty("profiles").EnumerateArray().ToDictionary(p => p.GetProperty("heroStringId").GetString()!);
        var files = inventory.RootElement.GetProperty("files").EnumerateArray().ToDictionary(p => p.GetProperty("path").GetString()!);
        Assert.Equal(1195, currentRows.Count);
        Assert.Equal(1195, preservedRows.Count);
        var lives = new HashSet<string>(StringComparer.Ordinal);
        var masters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in manifest.RootElement.GetProperty("records").EnumerateArray())
        {
            string id = row.GetProperty("id").GetString()!, name = row.GetProperty("name").GetString()!;
            JsonElement profile = currentRows[id], facts = profile.GetProperty("sourceFacts"), traits = profile.GetProperty("traits");
            Assert.Equal(name, facts.GetProperty("name").GetString());
            Assert.Equal(1, facts.GetProperty("clanTier").GetInt32());
            Assert.Equal("nord", facts.GetProperty("cultureId").GetString());
            Assert.Equal(21, facts.GetProperty("skills").EnumerateObject().Count());
            Assert.True(facts.GetProperty("skills").TryGetProperty("polearm", out _));
            Assert.True(facts.GetProperty("skills").GetProperty("charm").GetInt32() > 0);
            Assert.Equal(facts.GetProperty("skills").GetRawText(), traits.GetProperty("nativeSkills").GetRawText());
            Assert.Equal(43, traits.GetProperty("traitPercentages").EnumerateObject().Count());
            Assert.Equal(facts.GetRawText(), preservedRows[id].GetProperty("sourceFacts").GetRawText());
            Assert.Equal(traits.GetRawText(), preservedRows[id].GetProperty("traits").GetRawText());
            string narrativeFile = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..24] + ".json";
            Assert.Equal(narrativeFile, profile.GetProperty("narrativeFile").GetString());
            using var narrative = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ReignServer/profiles/narrative/profiles", narrativeFile)));
            Assert.Equal(id, narrative.RootElement.GetProperty("heroStringId").GetString());
            Assert.Equal("ready", narrative.RootElement.GetProperty("status").GetString());
            Assert.True(narrative.RootElement.GetProperty("items").GetArrayLength() >= 20);
            Assert.True(lives.Add(narrative.RootElement.GetProperty("life").GetProperty("summary").GetString()!));
            string key = name.Replace(' ', '_') + " (" + id + ")";
            string folder = Path.Combine(root, "ReignBeta/PortraitCache/_shared", key);
            foreach (string file in new[] { "portrait.png", "portrait_chest.png", "thumbnail.png", "thumbnail_wide.png", "zoom.png", ".portrait_derivatives.json", "formal_outfit.json", "portrait_input.json", "prompt.txt", ".ai_generation.json" })
            {
                JsonElement entry = files[key + "/" + file];
                byte[] bytes = File.ReadAllBytes(Path.Combine(folder, file));
                Assert.Equal(entry.GetProperty("bytes").GetInt64(), bytes.LongLength);
                Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            }
            using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, ".ai_generation.json")));
            JsonElement product = receipt.RootElement;
            Assert.Equal(id, product.GetProperty("heroStringId").GetString());
            Assert.Equal("Codex built-in image_gen", product.GetProperty("provider").GetString());
            Assert.Equal("text_only", product.GetProperty("generationMethod").GetString());
            Assert.Equal(0, product.GetProperty("inputImages").GetInt32());
            Assert.True(product.GetProperty("normalPortrait").GetBoolean());
            Assert.False(product.GetProperty("adultClothingEditPerformed").GetBoolean());
            Assert.True(product.GetProperty("derivativesReady").GetBoolean());
            Assert.True(masters.Add(product.GetProperty("portraitSha256").GetString()!));
            JsonElement percentages = traits.GetProperty("traitPercentages");
            int confidence = (int)Math.Round(.75 * percentages.GetProperty("flirtatiousness").GetInt32() + .25 * percentages.GetProperty("confidence").GetInt32(), MidpointRounding.AwayFromZero);
            Assert.Equal(confidence, product.GetProperty("physicalConfidence").GetProperty("score").GetInt32());
        }
        Assert.Equal(78, lives.Count);
        Assert.Equal(78, masters.Count);
    }
}
