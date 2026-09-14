using System.Reflection;
using System.Text;

namespace Reign.Mcp.Tests;

public sealed class ReignSavePayloadCodecTests
{
    [Fact]
    public void Starting_children_population_survives_signed_short_archive_round_trip()
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            Schema = "reign-starting-children-state-v1",
            Completed = true,
            Families = Enumerable.Range(0, 120).Select(i => new
            {
                MotherId = "mother_" + i, FatherId = "father_" + i,
                Children = Enumerable.Range(0, 5).Select(j => new
                {
                    SlotId = "family_" + i + "_child_" + j,
                    HeroId = "generated_" + i + "_" + j, Finalized = true,
                    Evidence = "Preserve parentage, identity and initialization receipts."
                })
            })
        });
        Assert.True(Encoding.UTF8.GetByteCount(payload) + 4 > short.MaxValue);
        var chunks = ReignBeta.Save.ReignSavePayloadCodec.Encode(payload);
        using var archive = new MemoryStream();
        using (var writer = new BinaryWriter(archive, Encoding.UTF8, true))
            foreach (string chunk in chunks)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(chunk);
                writer.Write(checked((short)(bytes.Length + 4)));
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        archive.Position = 0;
        var restored = new List<string>();
        using var reader = new BinaryReader(archive, Encoding.UTF8, true);
        while (archive.Position < archive.Length)
        {
            short entryBytes = reader.ReadInt16();
            Assert.InRange(entryBytes, 4, short.MaxValue);
            int stringBytes = reader.ReadInt32();
            Assert.Equal(entryBytes - 4, stringBytes);
            restored.Add(Encoding.UTF8.GetString(reader.ReadBytes(stringBytes)));
        }
        Assert.Equal(payload, ReignBeta.Save.ReignSavePayloadCodec.Decode(restored));
    }

    [Fact]
    public void Starting_children_save_writer_clears_legacy_string_and_reads_both_versions()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src",
            "Modules", "Characters", "Campaign", "ReignStartingChildrenCampaignBehavior.cs"));
        Assert.Contains("string json = null;", source);
        Assert.Contains("ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_state))", source);
        Assert.Contains("dataStore.SyncData(\"_reign_startingChildren_v1\", ref json);", source);
        Assert.Contains("dataStore.SyncData(\"_reign_startingChildren_chunks_v2\", ref chunks);", source);
        Assert.Contains("if (chunks != null && chunks.Count > 0) json = ReignSavePayloadCodec.Decode(chunks);", source);
        Assert.Contains("_loadedSave = true;", source);
    }

    [Fact]
    public void Character_editor_rejects_unloaded_and_replaced_campaign_before_polling_or_applying()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src",
            "Modules", "Characters", "Campaign", "ReignCharacterEditorCampaignBehavior.cs"));
        Assert.Contains("ReferenceEquals(_owningCampaign, TaleWorlds.CampaignSystem.Campaign.Current)", source);
        Assert.Contains("ReferenceEquals(Instance, this)", source);
        Assert.Contains("if (!IsCurrentCampaign || _polling || Hero.MainHero == null) return;", source);
        Assert.Contains("if (!IsCurrentCampaign || _identitySyncing || Hero.MainHero == null) return;", source);
        Assert.Contains("IsCurrentCampaign ? Apply(command) : null", source);
    }

    [Fact]
    public void Large_payload_round_trips_without_any_bannerlord_archive_entry_overflow()
    {
        var random = new Random(9876600);
        var characters = new char[96_000];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = (char)random.Next(32, 127);
        }
        var payload = new string(characters);

        Type? codecType = typeof(ReignSavePayloadCodecTests).Assembly.GetType(
            "ReignBeta.Save.ReignSavePayloadCodec");
        Assert.NotNull(codecType);

        MethodInfo? encode = codecType.GetMethod(
            "Encode",
            BindingFlags.Public | BindingFlags.Static);
        MethodInfo? decode = codecType.GetMethod(
            "Decode",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(encode);
        Assert.NotNull(decode);

        var chunks = Assert.IsType<List<string>>(
            encode.Invoke(null, new object[] { payload }));
        Assert.True(chunks.Count > 1);
        Assert.All(
            chunks,
            chunk => Assert.InRange(Encoding.UTF8.GetByteCount(chunk) + 4, 1, 12_004));

        var restored = Assert.IsType<string>(
            decode.Invoke(null, new object[] { chunks }));
        Assert.Equal(payload, restored);
    }

    [Fact]
    public void Castle_transcripts_use_chunked_payloads_before_bannerlord_serialization()
    {
        string root = TestOptions.FindWorkspace();
        string domain = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignCourtDomain.cs"));
        string behavior = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignCourtCampaignBehavior.cs"));
        string viewModel = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Dialogue", "UI", "ViewModels", "ReignPartyChatScreenVM.cs"));

        Assert.Contains("[SaveableField(22)] public List<string> TranscriptChunks;", domain,
            StringComparison.Ordinal);
        Assert.Contains("ReignSavePayloadCodec.Encode", domain, StringComparison.Ordinal);
        Assert.Contains("ReignSavePayloadCodec.Decode", domain, StringComparison.Ordinal);
        Assert.Contains("session.WriteTranscriptJson(session.TranscriptJson);", behavior,
            StringComparison.Ordinal);
        Assert.Contains("_castleSession.ReadTranscriptJson()", viewModel,
            StringComparison.Ordinal);
        Assert.Contains("_castleSession.WriteTranscriptJson(", viewModel,
            StringComparison.Ordinal);
        Assert.DoesNotContain("_castleSession.TranscriptJson = JsonConvert.SerializeObject", viewModel,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ruler_docket_state_uses_chunked_payloads_with_legacy_string_fallback()
    {
        string root = TestOptions.FindWorkspace();
        string docketBehavior = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignRulerDocketCampaignBehavior.cs"));
        string courtBehavior = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignCourtCampaignBehavior.cs"));

        Assert.Contains("private List<string> _rulerDocketStateChunks", docketBehavior,
            StringComparison.Ordinal);
        Assert.Contains("_rulerDocketStateChunks = ReignSavePayloadCodec.Encode(json);", docketBehavior,
            StringComparison.Ordinal);
        Assert.Contains("ReignSavePayloadCodec.Decode(_rulerDocketStateChunks)", docketBehavior,
            StringComparison.Ordinal);
        Assert.Contains(": _rulerDocketStateJson;", docketBehavior, StringComparison.Ordinal);
        Assert.Contains("_reignCourt_rulerDocketStateChunks", courtBehavior, StringComparison.Ordinal);
        Assert.DoesNotContain("_rulerDocketStateJson = JsonConvert.SerializeObject", docketBehavior,
            StringComparison.Ordinal);
    }
}
