using System.Text;
using System.Text.Json;
using ReignBeta.Save;

namespace Reign.Mcp.Tests;

public sealed class EconomicReportSaveArchiveTests
{
    [Fact]
    public void Large_realm_snapshot_and_shipment_receipts_survive_signed_short_archive_entries()
    {
        // Synthetic, provider-free data; these receipts are never campaign consent.
        var random = new Random(17380);
        var noise = new byte[512];
        var settlements = Enumerable.Range(0, 200).Select(i =>
        {
            random.NextBytes(noise);
            return new { SettlementStringId = "fixture_town_" + i, SettlementName = "城堡 " + i,
                FoodStocks = 400.25 + i, FoodChange = -2.5, Loyalty = 50.5, Security = 75,
                Notes = Convert.ToBase64String(noise) };
        }).ToArray();
        var shipments = Enumerable.Range(0, 80).Select(i => new { ShipmentId = "fixture_shipment_" + i,
            SourceSettlementStringId = "fixture_town_0", TargetSettlementStringId = "fixture_town_199",
            Status = i % 2 == 0 ? "Delivered" : "InTransit", DeliveredFood = i + .25,
            RequestedFood = i + 1.5, VassalConsentGranted = i % 2 == 0 }).ToArray();
        string json = JsonSerializer.Serialize(new { SnapshotDay = 91107, SnapshotCreatedDay = 91107.5,
            Settlements = settlements, Shipments = shipments });
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
            byte[] bytes = reader.ReadBytes(stringBytes);
            Assert.Equal(stringBytes, bytes.Length);
            restored.Add(Encoding.UTF8.GetString(bytes));
        }
        string decoded = ReignSavePayloadCodec.Decode(restored);
        Assert.Equal(json, decoded);
        using var state = JsonDocument.Parse(decoded);
        Assert.Equal(200, state.RootElement.GetProperty("Settlements").GetArrayLength());
        Assert.Equal(80, state.RootElement.GetProperty("Shipments").GetArrayLength());
        Assert.Equal("InTransit", state.RootElement.GetProperty("Shipments")[79].GetProperty("Status").GetString());
        Assert.False(state.RootElement.GetProperty("Shipments")[79].GetProperty("VassalConsentGranted").GetBoolean());
        Assert.Throws<InvalidDataException>(() => ReignSavePayloadCodec.Decode(["corrupt"]));
    }

    [Fact]
    public void Compressed_legacy_economic_report_recovery_preserves_exact_original_payload()
    {
        string json = JsonSerializer.Serialize(new { SnapshotDay = 91107, SnapshotCreatedDay = 91107.5,
            Settlements = Enumerable.Range(0, 200).Select(i => new { SettlementStringId = "fixture_" + i,
                Notes = new string('x', 500), FoodStocks = 400.25, FoodChange = -2.5 }).ToArray(),
            Shipments = new[] { new { ShipmentId = "completed_fixture", Status = "Delivered", DeliveredFood = 99.5 } } });
        Assert.True(Encoding.UTF8.GetByteCount(json) + sizeof(int) > short.MaxValue);
        string recovered = string.Concat(ReignSavePayloadCodec.Encode(json));
        Assert.InRange(Encoding.UTF8.GetByteCount(recovered) + sizeof(int), 1, short.MaxValue);
        Assert.Equal(json, ReignSavePayloadCodec.DecodeLegacyArchiveEntry(recovered));
    }

    [Fact]
    public void Court_economic_adapter_migrates_legacy_and_chunks_without_discarding_shipments()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignCourtCampaignBehavior.cs"));
        Assert.Contains("_economicReportStateJson = string.Empty;", source);
        Assert.Contains("_economicReportStateChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(", source);
        Assert.Contains("dataStore.SyncData(\"_reignCourt_economicReportState\", ref _economicReportStateJson);", source);
        Assert.Contains("dataStore.SyncData(\"_reignCourt_economicReportStateChunks_v2\", ref _economicReportStateChunks);", source);
        Assert.Contains("ReignSavePayloadCodec.Decode(_economicReportStateChunks)", source);
        Assert.Contains("ReignSavePayloadCodec.DecodeLegacyArchiveEntry(economicReportJson)", source);
        Assert.Contains("JsonConvert.DeserializeObject<ReignEconomicReportState>(economicReportJson)", source);
        Assert.Contains("throw new InvalidOperationException(\"Economic report save state could not be restored safely.\", ex);", source);
        int warning = source.IndexOf("ReignLog.Warn(\"Economic report save state", StringComparison.Ordinal);
        int nextState = source.IndexOf("string spymasterJson", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("_economicReportState = new ReignEconomicReportState();", source[warning..nextState]);
    }
}
