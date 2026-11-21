using System.Text;
using Newtonsoft.Json.Linq;
using ReignBeta.WorldSimulation.Runtime;

namespace Reign.Mcp.Tests;

public sealed class ReignCalendarStoreTests : IDisposable
{
    private const string Campaign = "campaign_126";
    private const string V1 = """
        {"version":1,"campaignId":"campaign_126","nativeDayAnchor":1000.25,"calendarDayAnchor":1500.5,
         "ageAnchors":{"hero":{"day":1000.25,"age":17.5}}}
        """;
    private readonly string _root = Directory.CreateTempSubdirectory("reign-calendar-").FullName;
    private string Calendar => Path.Combine(_root, "world", "calendar.json");

    [Theory]
    [InlineData(false, false, null, null, true)]  // First Reign launch on a vanilla save.
    [InlineData(true, false, null, false, true)]  // Native save before foundations.
    [InlineData(true, false, null, null, false)]  // Older point without a readiness journal.
    [InlineData(false, false, null, true, false)] // Older journal without a Save Sync point.
    [InlineData(true, true, null, false, false)]
    [InlineData(true, false, "checkpoint", false, false)]
    public void Loaded_save_history_controls_first_use(bool point, bool registered, string? digest, bool? prepared, bool expected) =>
        Assert.Equal(expected, ReignCalendarStore.CanCreateForLoadedSave(point, registered, digest, prepared));

    [Fact]
    public void Only_first_use_can_create_a_missing_calendar()
    {
        Assert.Throws<FileNotFoundException>(() => Open());
        Assert.Throws<FileNotFoundException>(() => Open(firstUse: true, checkpoint: new string('a', 64)));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Calendar)));

        var fresh = ReignCalendarStore.Open(Calendar, Campaign, true, null, () => new ReignCalendarState
        {
            Version = 1, CampaignId = Campaign, NativeDayAnchor = 81.25, CalendarDayAnchor = 123.5,
            AgeAnchors = new Dictionary<string, ReignAgeAnchor>()
        });
        fresh.Publish();
        var loaded = Open(checkpoint: fresh.PublishedSha256);
        Assert.Empty(loaded.State.AgeAnchors);
        Assert.Equal(124.5, loaded.State.CalendarDayAt(82.25));
    }

    [Theory]
    [InlineData("{", "Invalid")]
    [InlineData("""{"Version":73,"calendarOfTheFuture":{}}""", "Unsupported")]
    [InlineData("""{"Version":1,"version":73}""", "Ambiguous")]
    [InlineData("""{"Version":1,"CampaignId":"other_campaign"}""", "different campaign")]
    [InlineData("""{"Version":1,"CampaignId":"campaign_126","AgeAnchors":{}}""", "NativeDayAnchor")]
    [InlineData("""{"Version":1,"CampaignId":"campaign_126","NativeDayAnchor":0,"CalendarDayAnchor":0}""", "age anchors")]
    [InlineData("""{"Version":1,"CampaignId":"campaign_126","NativeDayAnchor":1,"CalendarDayAnchor":2,"AgeAnchors":{"hero":{}}}""", "Age")]
    [InlineData("""{"Version":1,"CampaignId":"campaign_126","NativeDayAnchor":true,"CalendarDayAnchor":0,"AgeAnchors":{}}""", "NativeDayAnchor")]
    public void Existing_rejected_data_never_bootstraps_or_changes_bytes(string input, string expected)
    {
        byte[] bytes = Write(input);
        foreach (bool firstUse in new[] { false, true })
        {
            var failure = Assert.Throws<InvalidDataException>(() => Open(firstUse));
            Assert.Contains(expected, failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(bytes, File.ReadAllBytes(Calendar));
        }
        Assert.Equal(Calendar, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Calendar)!)));
    }

    [Fact]
    public void An_unreadable_existing_path_cannot_be_treated_as_first_use()
    {
        Directory.CreateDirectory(Calendar);
        Assert.Throws<UnauthorizedAccessException>(() => Open(firstUse: true));
        Assert.True(Directory.Exists(Calendar));
    }

    [Theory]
    [InlineData(1e308, -1e308, 1000.25)]
    [InlineData(1000.25, 1e100, 1000.25)]
    [InlineData(1000.25, 1500.5, 1e90)]
    public void Finite_but_unrepresentable_anchors_cannot_enable_adjusted_time(double native, double calendar, double heroDay)
    {
        var input = JObject.Parse(V1);
        input["nativeDayAnchor"] = native;
        input["calendarDayAnchor"] = calendar;
        input["ageAnchors"]!["hero"]!["day"] = heroDay;
        byte[] original = Write(input.ToString());
        var store = Open();
        Assert.Throws<InvalidDataException>(() => store.State.ValidateTimeline(1e9, 1000.25, 126));
        Assert.Equal(original, File.ReadAllBytes(Calendar));
    }

    [Fact]
    public void Restored_v1_preserves_its_dates_and_age_boundary_with_or_without_a_native_digest()
    {
        byte[] original = Write(V1);
        var legacy = Open();
        string checkpoint = Assert.IsType<string>(legacy.PublishedSha256);
        var otherPoint = JObject.Parse(V1);
        otherPoint["nativeDayAnchor"] = 1200;
        byte[] different = Write(otherPoint.ToString());
        Assert.Contains("save point", Assert.Throws<InvalidDataException>(() => Open(checkpoint: checkpoint)).Message);
        Assert.Equal(different, File.ReadAllBytes(Calendar));

        // Save Sync installs the matching snapshot at the active path before native readiness.
        File.WriteAllBytes(Calendar, original);
        var restored = Open(checkpoint: checkpoint);
        Assert.Equal(1563.5, restored.State.CalendarDayAt(1063.25));
        Assert.Equal(1063.25, restored.State.NativeDayAt(1563.5));
        Assert.True(restored.State.AgeAnchors["hero"].AgeAt(1063.24, 126) < 18);
        Assert.Equal(18f, restored.State.AgeAnchors["hero"].AgeAt(1063.25, 126));
        Assert.Equal(original, File.ReadAllBytes(Calendar));
    }

    [Fact]
    public void A_stale_writer_cannot_replace_changed_or_disappeared_evidence()
    {
        byte[] original = Write(V1);
        var store = Open();
        store.State.AgeAnchors["hero"].Age = 23;
        byte[] damaged = Write("{damage");
        Assert.Throws<IOException>(store.Publish);
        Assert.Equal(damaged, File.ReadAllBytes(Calendar));
        File.Delete(Calendar);
        Assert.Throws<FileNotFoundException>(store.Publish);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Calendar)!));
        Directory.Delete(Path.GetDirectoryName(Calendar)!);
        Assert.Throws<DirectoryNotFoundException>(store.Publish);
        Assert.False(Directory.Exists(Path.GetDirectoryName(Calendar)));

        Directory.CreateDirectory(Path.GetDirectoryName(Calendar)!);
        File.WriteAllBytes(Calendar, original);
        store.Publish();
        Assert.Equal(23f, Open(checkpoint: store.PublishedSha256).State.AgeAnchors["hero"].Age);
    }

    [Fact]
    public void Denied_Windows_replacement_keeps_the_previous_checkpoint_and_can_retry()
    {
        if (!OperatingSystem.IsWindows()) return;
        byte[] original = Write(V1);
        var store = Open();
        string previous = Assert.IsType<string>(store.PublishedSha256);
        store.State.AgeAnchors["hero"].Age = 30;
        using (File.Open(Calendar, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(store.Publish);
        Assert.Equal(original, File.ReadAllBytes(Calendar));
        Assert.Equal(previous, store.PublishedSha256);
        Assert.Equal(Calendar, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Calendar)!)));
        store.Publish();
        Assert.Equal(30f, Open(checkpoint: store.PublishedSha256).State.AgeAnchors["hero"].Age);
    }

    [Fact]
    public void Failed_first_publish_preserves_a_competing_file_and_removes_the_scratch()
    {
        var fresh = ReignCalendarStore.Open(Calendar, Campaign, true, null, () => new ReignCalendarState
        {
            Version = 1, CampaignId = Campaign, AgeAnchors = new Dictionary<string, ReignAgeAnchor>()
        });
        byte[] original = Write("arrived before publish");
        Assert.Throws<IOException>(fresh.Publish);
        Assert.Null(fresh.PublishedSha256);
        Assert.Equal(original, File.ReadAllBytes(Calendar));
        Assert.Equal(Calendar, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Calendar)!)));
    }

    private ReignCalendarStore Open(bool firstUse = false, string? checkpoint = null) =>
        ReignCalendarStore.Open(Calendar, Campaign, firstUse, checkpoint,
            () => throw new InvalidOperationException("Unexpected native re-anchoring"));

    private byte[] Write(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Calendar)!);
        var bytes = Encoding.UTF8.GetBytes(text);
        File.WriteAllBytes(Calendar, bytes);
        return bytes;
    }

    public void Dispose() => Directory.Delete(_root, true);
}
