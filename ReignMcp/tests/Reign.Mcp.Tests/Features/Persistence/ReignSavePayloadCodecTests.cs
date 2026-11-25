using System.Reflection;
using System.Text;
using Reign.Core.Contracts.Dialogue;
using ReignBeta.Persistence.Save;

namespace Reign.Mcp.Tests;

public sealed class ReignSavePayloadCodecTests
{
    [Fact]
    public void Native_authorities_load_missing_fields_and_valid_empty_ledgers()
    {
        List<string> ReadOrders(List<string>? chunks, string? legacy) =>
            ReignSavePayloadCodec.DecodeAuthoritativeState<List<string>>(chunks, legacy,
                Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>, () => new List<string>());

        Assert.Empty(ReadOrders(null, null));
        Assert.Empty(ReadOrders(new List<string>(), string.Empty));
        Assert.Empty(ReadOrders(null, "[]"));
        Assert.Empty(ReadOrders(ReignSavePayloadCodec.Encode("[]"), "[\"obsolete order\"]"));
        Assert.Equal(new[] { "retained order" }, ReadOrders(new List<string>(), "[\"retained order\"]"));

        Dictionary<string, string> ReadDocket(List<string>? chunks, string? legacy) =>
            ReignSavePayloadCodec.DecodeAuthoritativeState<Dictionary<string, string>>(chunks, legacy,
                Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>, () => new Dictionary<string, string>());

        Assert.Empty(ReadDocket(null, " "));
        Assert.Empty(ReadDocket(null, "{}"));
        Assert.Empty(ReadDocket(ReignSavePayloadCodec.Encode("{}"), null));
    }

    [Fact]
    public void Native_authority_preserves_unreadable_chunks_and_never_falls_back_to_legacy()
    {
        var chunks = new List<string> { "reign-gzip-v1:", "**bad base64**" };
        string[] originalChunks = chunks.ToArray();
        const string legacy = "[\"older order\"]";
        int deserializeCalls = 0;
        Assert.Throws<FormatException>(() => ReignSavePayloadCodec.DecodeAuthoritativeState(
            chunks, legacy, json =>
            {
                deserializeCalls++;
                return Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json);
            }, () => throw new InvalidOperationException("Present chunked authority cannot be empty.")));

        Assert.Equal(0, deserializeCalls);
        Assert.Equal(originalChunks, chunks);
    }

    [Fact]
    public void Native_authority_rejects_present_json_without_ledger_state()
    {
        List<string> Read(List<string>? chunks, string? legacy) =>
            ReignSavePayloadCodec.DecodeAuthoritativeState<List<string>>(chunks, legacy,
                Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>, () => new List<string>());

        Assert.Throws<InvalidDataException>(() => Read(null, "null"));
        Assert.Throws<InvalidDataException>(() => Read(ReignSavePayloadCodec.Encode(""), "[]"));
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => Read(null, "{broken"));
    }

    [Fact]
    public void Native_authority_loaders_abort_before_registering_replacement_state_or_migrating()
    {
        string root = TestOptions.FindWorkspace();
        string command = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/WorldSimulation/CampaignCommands/ReignCampaignCommandBehavior.cs"));
        int commandStart = command.IndexOf("public override void SyncData(", StringComparison.Ordinal);
        int commandEnd = command.IndexOf("public static bool ValidateControlAction(", StringComparison.Ordinal);
        Assert.True(commandStart >= 0 && commandEnd > commandStart);
        string commandLoad = command[commandStart..commandEnd];
        Assert.Contains("DecodeAuthoritativeState(", commandLoad, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(message, ex);", commandLoad, StringComparison.Ordinal);
        Assert.DoesNotContain("_orders = new List<ReignCampaignOrderRecord>()", commandLoad, StringComparison.Ordinal);
        Assert.Contains("SyncData(\"_reign_campaignCommandState\", ref _legacyState)", commandLoad, StringComparison.Ordinal);
        Assert.Contains("SyncData(\"_reign_campaignCommandStateChunks\", ref _stateChunks)", commandLoad, StringComparison.Ordinal);

        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignRulerDocketCampaignBehavior.cs"));
        int docketStart = docket.IndexOf("private void RestoreRulerDocketAfterLoad()", StringComparison.Ordinal);
        int docketEnd = docket.IndexOf("private void CollapseDuplicatePendingPetitioners(", StringComparison.Ordinal);
        Assert.True(docketStart >= 0 && docketEnd > docketStart);
        string docketLoad = docket[docketStart..docketEnd];
        int decode = docketLoad.IndexOf("DecodeAuthoritativeState(", StringComparison.Ordinal);
        int publish = docketLoad.IndexOf("_rulerDocketState = state;", StringComparison.Ordinal);
        int abort = docketLoad.IndexOf("throw new InvalidOperationException(message, ex);", StringComparison.Ordinal);
        int migrate = docketLoad.IndexOf("MigrateLegacyDocketOnce();", StringComparison.Ordinal);
        Assert.True(decode >= 0 && publish > decode && abort > publish && migrate > abort);
        Assert.DoesNotContain("_rulerDocketState = new ReignRulerDocketState()", docketLoad, StringComparison.Ordinal);

        string court = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignCourtCampaignBehavior.cs"));
        Assert.Contains("SyncData(\"_reignCourt_rulerDocketState\", ref _rulerDocketStateJson)", court, StringComparison.Ordinal);
        Assert.Contains("SyncData(\"_reignCourt_rulerDocketStateChunks\", ref _rulerDocketStateChunks)", court, StringComparison.Ordinal);
    }

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
        var chunks = ReignBeta.Persistence.Save.ReignSavePayloadCodec.Encode(payload);
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
        Assert.Equal(payload, ReignBeta.Persistence.Save.ReignSavePayloadCodec.Decode(restored));
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
    public void Participation_xp_receipts_survive_the_archive_limit_without_reawarding()
    {
        var ledger = new ReignXpLedger();
        for (int i = 0; i < 700; i++)
            Assert.True(ledger.TryClaim("conversation:individual:" + i.ToString("x32")));
        string json = System.Text.Json.JsonSerializer.Serialize(ledger);
        Assert.True(Encoding.UTF8.GetByteCount(json) + 4 > short.MaxValue);

        var chunks = ReignBeta.Persistence.Save.ReignSavePayloadCodec.Encode(json);
        Assert.All(chunks, chunk => Assert.InRange(Encoding.UTF8.GetByteCount(chunk) + 4, 1, short.MaxValue));
        string restored = ReignBeta.Persistence.Save.ReignSavePayloadCodec.Decode(chunks);
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<ReignXpLedger>(restored)!;
        Assert.False(reloaded.TryClaim("conversation:individual:" + 699.ToString("x32")));

        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src",
            "Modules", "Dialogue", "Campaign", "ReignXpCampaignBehavior.cs"));
        Assert.Contains("string json = null;", source);
        Assert.Contains("_reign_participation_xp_chunks_v2", source);
        Assert.Contains("ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_ledger))", source);
        Assert.Contains("ReignSavePayloadCodec.DecodeLegacyArchiveEntry(json)", source);
    }

    [Fact]
    public void Recovered_legacy_xp_envelope_can_exceed_chunk_limit_but_not_archive_limit()
    {
        var random = new Random(46177);
        var bytes = new byte[11_000];
        random.NextBytes(bytes);
        string payload = Convert.ToBase64String(bytes);
        string envelope = string.Concat(ReignBeta.Persistence.Save.ReignSavePayloadCodec.Encode(payload));
        Assert.True(Encoding.UTF8.GetByteCount(envelope) + 4 > 12_004);
        Assert.True(Encoding.UTF8.GetByteCount(envelope) + 4 <= short.MaxValue);
        Assert.Equal(payload, ReignBeta.Persistence.Save.ReignSavePayloadCodec.DecodeLegacyArchiveEntry(envelope));
        Assert.Throws<System.IO.InvalidDataException>(() =>
            ReignBeta.Persistence.Save.ReignSavePayloadCodec.DecodeLegacyArchiveEntry(new string('x', short.MaxValue)));
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
        Assert.Contains("var fence = ReignServerClient.RequestFence.Capture();", source);
        Assert.Contains("IsCurrentCampaign && fence.IsCurrentActionPoll()", source);
        Assert.Contains("var verified = OwnedManualNativeEditorDeliveryReady ? ReignSaveSyncCoordinator.CurrentVerifiedLoad : null;", source);
        Assert.Contains("verified.CampaignId != campaignId || verified.RestoreGeneration != fence.SharedActivityGeneration", source);
        Assert.Contains("!ReignCharacterEditorClient.IsNativeOwnerNonce(verified.LoadSessionId)", source);
        Assert.Contains("(verified == null || ReferenceEquals(verified, ReignSaveSyncCoordinator.CurrentVerifiedLoad))", source);

        int ownerCheck = source.IndexOf("if (!OwnsPoll()) return;", StringComparison.Ordinal);
        int pollStart = source.IndexOf(
            "PollAsync(campaignId, timelineId, fence.SharedActivityGeneration, verified?.LoadSessionId ?? \"\", OwnsPoll)",
            StringComparison.Ordinal);
        int pollRequest = source.IndexOf("timelineId, restoreGeneration, nativeEditorSession, ownsPoll).ConfigureAwait(false)", StringComparison.Ordinal);
        int sessionCheck = source.IndexOf(
            "if (command.IsManualNative && (nativeEditorSession.Length == 0 || command.ManualClaimSessionId != nativeEditorSession))",
            StringComparison.Ordinal);
        int nativeApply = source.IndexOf("ownsPoll() ? ApplyOrReadSavedReceipt(campaignId, timelineId, command) : null", StringComparison.Ordinal);
        int report = source.IndexOf("result.Applied, result.Errors, ownsPoll)", StringComparison.Ordinal);
        Assert.True(ownerCheck >= 0 && pollStart > ownerCheck && pollRequest > pollStart,
            "Check the current owner before polling and carry its captured session and fence into the request.");
        Assert.True(sessionCheck > pollRequest && nativeApply > sessionCheck && report > nativeApply,
            "Check the manual claim session before applying under the same owner and reporting the result.");
        Assert.Contains("retained.Matches(campaignId, timelineId, digest, command)", source);
        Assert.Contains("CampaignId == campaign && TimelineId == timeline && MaterialSha256 == material", source);
        Assert.Contains("(ManualOriginLineageId ?? \"\") == command.ManualOriginLineageId", source);
        Assert.Contains("(ManualOriginMaterialSha256 ?? \"\") == command.ManualOriginMaterialSha256", source);
        Assert.Contains("(AutomaticOriginId ?? \"\") == command.AutomaticOriginId", source);
        Assert.Contains("(AutomaticOriginMaterialSha256 ?? \"\") == command.AutomaticOriginMaterialSha256", source);
        Assert.Contains("_nativeEditorReceipts.Add(command.CommandId, receipt);", source);
        Assert.Contains("dataStore.SyncData(\"_reignCharacterEditor_nativeReceipts_v1\", ref chunks);", source);
        Assert.Contains("result.Applied, result.Errors, ownsPoll)", source);
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

        Type? codecType = Type.GetType(
            "ReignBeta.Persistence.Save.ReignSavePayloadCodec, ReignBeta.Core");
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
            "Court", "Campaign", "ReignCourtDomain.cs"));
        string behavior = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Campaign", "ReignCourtCampaignBehavior.cs"));
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
            "Court", "Campaign", "ReignRulerDocketCampaignBehavior.cs"));
        string courtBehavior = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Campaign", "ReignCourtCampaignBehavior.cs"));

        Assert.Contains("private List<string> _rulerDocketStateChunks", docketBehavior,
            StringComparison.Ordinal);
        Assert.Contains("_rulerDocketStateChunks = ReignSavePayloadCodec.Encode(json);", docketBehavior,
            StringComparison.Ordinal);
        Assert.Contains("ReignSavePayloadCodec.DecodeAuthoritativeState(", docketBehavior,
            StringComparison.Ordinal);
        Assert.Contains("_rulerDocketStateChunks, _rulerDocketStateJson, JsonConvert.DeserializeObject<ReignRulerDocketState>",
            docketBehavior, StringComparison.Ordinal);

        var restored = ReignSavePayloadCodec.DecodeAuthoritativeState<Dictionary<string, int>>(
            new List<string>(), "{\"Version\":3}",
            Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>,
            () => new Dictionary<string, int>());
        Assert.Equal(3, restored["Version"]);
        Assert.Contains("_reignCourt_rulerDocketStateChunks", courtBehavior, StringComparison.Ordinal);
        Assert.DoesNotContain("_rulerDocketStateJson = JsonConvert.SerializeObject", docketBehavior,
            StringComparison.Ordinal);
    }
}
