using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.WorldSimulation.Integration;

namespace Reign.Mcp.Tests;

public sealed class ReignWorldHistoryEventProofTests
{
    [Fact]
    public void Proof_binds_the_immutable_envelope_and_compares_structured_json()
    {
        JObject envelope = Event();
        var proof = Create(envelope);
        envelope["summary"] = "later native text";
        envelope["payload"]!["caseId"] = "later_case";
        envelope["entities"]![0]!["role"] = "later role";

        JObject stored = Observed();
        stored["event"]!["payload_json"] =
            """{"detail":{"dayLabel":"2026-11-24T00:00:00Z","revisions":[-7,5]},"caseId":"case-a"}""";
        Assert.True(proof.Matches(stored));
        Assert.Equal("http://127.0.0.1:8089", proof.Origin);
        Assert.Equal("/world-history/event/event%2B1?eventProofVersion=1&campaignId=campaign%2Bone&timelineId=main&sharedActivityGeneration=gen%2B1",
            proof.PathAndQuery);
    }

    [Theory]
    [InlineData("event_id", "EVENT+1")]
    [InlineData("campaign_id", "Campaign+one")]
    [InlineData("timeline_id", "Main")]
    [InlineData("world_day", "15.25")]
    [InlineData("location_id", "another_town")]
    [InlineData("location_name", "A different town")]
    [InlineData("event_type", "arrest_effect_settled")]
    [InlineData("correlation_id", "case-b")]
    [InlineData("summary", "Different wording")]
    [InlineData("sequence", "44")]
    [InlineData("is_complete", "1")]
    public void Proof_refuses_changed_identity_or_material_and_does_not_coerce_token_types(string field, string value)
    {
        JObject reply = Observed();
        reply["event"]![field] = value;
        Assert.False(Create(Event()).Matches(reply));
    }

    [Fact]
    public void Proof_refuses_a_close_but_different_world_day()
    {
        JObject reply = Observed();
        reply["event"]!["world_day"] = Math.BitIncrement(15.25d);
        Assert.False(Create(Event()).Matches(reply));
    }

    [Theory]
    [InlineData("""{"caseId":"case-b","detail":{"revisions":[-7,5],"dayLabel":"2026-11-24T00:00:00Z"}}""")]
    [InlineData("""{"caseId":"case-a","detail":{"revisions":[5,-7],"dayLabel":"2026-11-24T00:00:00Z"}}""")]
    [InlineData("""{"caseId":"wrong","caseId":"case-a","detail":{"revisions":[-7,5],"dayLabel":"2026-11-24T00:00:00Z"}}""")]
    [InlineData("""{"caseId":"case-a","detail":{"revisions":[-7.0,5],"dayLabel":"2026-11-24T00:00:00Z"}}""")]
    public void Proof_refuses_a_changed_ordered_payload_duplicate_key_or_unsupported_fractional_token(string material)
    {
        JObject reply = Observed();
        reply["event"]!["payload_json"] = material;
        Assert.False(Create(Event()).Matches(reply));
    }

    [Fact]
    public void Proof_refuses_a_reordered_or_inconsistent_entity_projection()
    {
        JObject reordered = Observed();
        var items = (JArray)reordered["event"]!["entities"]!;
        reordered["event"]!["entities"] = new JArray(items[1].DeepClone(), items[0].DeepClone());
        Assert.False(Create(Event()).Matches(reordered));

        JObject wrongProjection = Observed();
        wrongProjection["event"]!["entities"]![0]!["role"] = "changed";
        Assert.False(Create(Event()).Matches(wrongProjection));

        JObject wrongJson = Observed();
        wrongJson["event"]!["entities"]![0]!["payload_json"] =
            """{"entityId":"hero_a","entityType":"hero","role":"accused","isPlayer":true}""";
        Assert.False(Create(Event()).Matches(wrongJson));
    }

    [Fact]
    public void Aggregate_legacy_absent_and_stale_responses_never_confirm_an_event()
    {
        var proof = Create(Event());
        Assert.False(proof.Matches(JObject.Parse("""{"ok":true,"accepted":1,"duplicates":0}""")));
        Assert.False(proof.Matches(JObject.Parse("""{"ok":true,"event":{"event_id":"event+1"}}""")));
        Assert.False(proof.Matches(JObject.Parse("""{"ok":false,"eventProofVersion":1,"result":"not_found"}""")));
        Assert.False(proof.Matches(JObject.Parse("""{"ok":false,"eventProofVersion":1,"result":"owner_mismatch"}""")));

        JObject stale = Observed();
        stale["ownership"]!["sharedActivityGeneration"] = "previous";
        Assert.False(proof.Matches(stale));
    }

    [Fact]
    public void Creation_is_scoped_and_does_not_infer_legacy_owner_or_unrepresented_envelope_fields()
    {
        JObject envelope = Event();
        Assert.False(ReignWorldHistoryEventProof.TryCreate(null, "campaign", "main", "generation", envelope, out _));
        Assert.False(ReignWorldHistoryEventProof.TryCreate("http://127.0.0.1:8089", "campaign", "main", null, envelope, out _));
        Assert.True(ReignWorldHistoryEventProof.TryCreate("http://127.0.0.1:8089", "campaign", "main", "", envelope, out _));
        envelope["eventId"] = "..";
        Assert.False(ReignWorldHistoryEventProof.TryCreate("http://127.0.0.1:8089", "campaign", "main", "generation", envelope, out _));
        envelope = Event();
        envelope["knowledgeRules"] = new JArray();
        Assert.False(ReignWorldHistoryEventProof.TryCreate("http://127.0.0.1:8089", "campaign", "main", "generation", envelope, out _));
        envelope = Event();
        envelope["disseminationClass"] = "major_world";
        Assert.False(ReignWorldHistoryEventProof.TryCreate("http://127.0.0.1:8089", "campaign", "main", "generation", envelope, out _));
    }

    private static ReignWorldHistoryEventProof Create(JObject envelope)
    {
        Assert.True(ReignWorldHistoryEventProof.TryCreate("http://127.0.0.1:8089", "campaign+one", "main", "gen+1", envelope, out var proof));
        return proof;
    }

    private static JObject Event()
    {
        using var reader = new JsonTextReader(new StringReader("""
        {
            "eventId":"event+1","eventType":"arrest_evidence_reclassified","category":"justice",
            "phase":"evidence_established","correlationId":"case-a","locationId":"town_a","locationName":"A town",
            "disseminationClass":"ordinary","summary":"Evidence established","source":"reign_system",
            "sequence":44,"worldDay":15.25,"isComplete":true,
            "payload":{"caseId":"case-a","detail":{"revisions":[-7,5],"dayLabel":"2026-11-24T00:00:00Z"}},
            "entities":[
                {"entityId":"hero_a","entityType":"hero","role":"accused","isPlayer":false},
                {"entityId":"hero_b","entityType":"hero","role":"witness","isPlayer":true}
            ]
        }
        """)) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    private static JObject Observed() => JObject.Parse("""
        {
            "ok":true,"eventProofVersion":1,"result":"observed",
            "ownership":{"campaignId":"campaign+one","timelineId":"main","sharedActivityGeneration":"gen+1"},
            "event":{
                "event_id":"event+1","campaign_id":"campaign+one","timeline_id":"main",
                "event_type":"arrest_evidence_reclassified","category":"justice","phase":"evidence_established",
                "correlation_id":"case-a","location_id":"town_a","location_name":"A town","dissemination_class":"ordinary",
                "summary":"Evidence established","source":"reign_system","sequence":44,"world_day":15.25,"is_complete":1,
                "payload_json":"{\"caseId\":\"case-a\",\"detail\":{\"revisions\":[-7,5],\"dayLabel\":\"2026-11-24T00:00:00Z\"}}",
                "entities":[
                    {"event_id":"event+1","ordinal":0,"entity_id":"hero_a","entity_type":"hero","role":"accused",
                     "payload_json":"{\"entityId\":\"hero_a\",\"entityType\":\"hero\",\"role\":\"accused\",\"isPlayer\":false}"},
                    {"event_id":"event+1","ordinal":1,"entity_id":"hero_b","entity_type":"hero","role":"witness",
                     "payload_json":"{\"entityId\":\"hero_b\",\"entityType\":\"hero\",\"role\":\"witness\",\"isPlayer\":true}"}
                ]
            }
        }
        """);
}
