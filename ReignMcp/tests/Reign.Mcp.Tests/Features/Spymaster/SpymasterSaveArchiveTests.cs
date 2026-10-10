using System.Text;
using System.Text.Json;
using ReignBeta.Save;

namespace Reign.Mcp.Tests;

public sealed class SpymasterSaveArchiveTests
{
    [Fact]
    public void Mission_effect_and_control_receipts_survive_signed_short_archive_entries()
    {
        var random = new Random(19222);
        var reports = new byte[1200];
        var missions = Enumerable.Range(0, 80).Select(index =>
        {
            random.NextBytes(reports);
            return new { MissionId = "signed_mission_" + index, State = 1, AgreementEffectState = "completed",
                ReportJson = Convert.ToBase64String(reports) };
        }).ToArray();
        string json = JsonSerializer.Serialize(new { Version = 4, Missions = missions,
            Effects = new[] { new { EffectId = "retained_effect", MissionId = "signed_mission_79" } },
            AppliedForeignAgentActionIds = new[] { "already_applied" },
            OrganicTestLedgers = new[] { new { RunId = "signed_control", MissionIds = new[] { "signed_mission_79" } } } });
        Assert.True(Encoding.UTF8.GetByteCount(json) + sizeof(int) > short.MaxValue);
        var chunks = ReignSavePayloadCodec.Encode(json);
        Assert.True(chunks.Count > 1);
        using var archive = new MemoryStream();
        using (var writer = new BinaryWriter(archive, Encoding.UTF8, true))
            foreach (string chunk in chunks)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(chunk);
                Assert.InRange(bytes.Length + sizeof(int), 1, 12004);
                writer.Write(checked((short)(bytes.Length + sizeof(int))));
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        archive.Position = 0;
        var restored = new List<string>();
        using var reader = new BinaryReader(archive, Encoding.UTF8, true);
        while (archive.Position < archive.Length)
        {
            int entryBytes = reader.ReadInt16(), stringBytes = reader.ReadInt32();
            Assert.InRange(entryBytes, 1, short.MaxValue);
            Assert.Equal(entryBytes - sizeof(int), stringBytes);
            restored.Add(Encoding.UTF8.GetString(reader.ReadBytes(stringBytes)));
        }
        Assert.Equal(json, ReignSavePayloadCodec.Decode(restored));
        using var state = JsonDocument.Parse(ReignSavePayloadCodec.Decode(restored));
        Assert.Equal(80, state.RootElement.GetProperty("Missions").GetArrayLength());
        Assert.Equal("already_applied", state.RootElement.GetProperty("AppliedForeignAgentActionIds")[0].GetString());
        Assert.Throws<System.IO.InvalidDataException>(() => ReignSavePayloadCodec.Decode(["corrupt"]));
    }

    [Fact]
    public void Compressed_legacy_recovery_preserves_the_complete_original_state()
    {
        string json = JsonSerializer.Serialize(new { Version = 4,
            Missions = Enumerable.Range(0, 90).Select(i => new { MissionId = "legacy_" + i,
                ReportJson = new string('x', 600), AgreementEffectState = "completed" }).ToArray(),
            AppliedForeignAgentActionIds = new[] { "no_replay" } });
        Assert.True(Encoding.UTF8.GetByteCount(json) + sizeof(int) > short.MaxValue);
        string recovered = string.Concat(ReignSavePayloadCodec.Encode(json));
        Assert.InRange(Encoding.UTF8.GetByteCount(recovered) + sizeof(int), 1, short.MaxValue);
        Assert.Equal(json, ReignSavePayloadCodec.DecodeLegacyArchiveEntry(recovered));
    }

    [Fact]
    public void Court_adapter_migrates_legacy_and_recovered_state_without_dropping_receipts()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignCourtCampaignBehavior.cs"));
        Assert.Contains("_spymasterStateJson = string.Empty;", source);
        Assert.Contains("_spymasterStateChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(", source);
        Assert.Contains("dataStore.SyncData(\"_reignCourt_spymasterState\", ref _spymasterStateJson);", source);
        Assert.Contains("dataStore.SyncData(\"_reignCourt_spymasterStateChunks_v2\", ref _spymasterStateChunks);", source);
        Assert.Contains("ReignSavePayloadCodec.Decode(_spymasterStateChunks)", source);
        Assert.Contains("ReignSavePayloadCodec.DecodeLegacyArchiveEntry(spymasterJson)", source);
        Assert.Contains("JsonConvert.DeserializeObject<ReignSpymasterState>(spymasterJson)", source);
        Assert.Contains("throw new InvalidOperationException(\"Spymaster save state could not be restored safely.\", ex);", source);
        int warning = source.IndexOf("ReignLog.Warn(\"Spymaster save state", StringComparison.Ordinal);
        int restore = source.IndexOf("RestoreRulerDocketAfterLoad();", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("_spymasterState = new ReignSpymasterState();", source[warning..restore]);
    }
}
