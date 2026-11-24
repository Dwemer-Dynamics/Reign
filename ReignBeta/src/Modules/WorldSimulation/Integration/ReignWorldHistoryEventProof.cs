using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReignBeta.WorldSimulation.Integration
{
    // A proof is one immutable native event. Transport success or a stored event ID alone is insufficient.
    internal sealed class ReignWorldHistoryEventProof
    {
        private static readonly (string Native, string Stored)[] EventStrings =
        {
            ("eventId", "event_id"), ("eventType", "event_type"), ("category", "category"),
            ("phase", "phase"), ("correlationId", "correlation_id"), ("locationId", "location_id"),
            ("locationName", "location_name"), ("disseminationClass", "dissemination_class"),
            ("summary", "summary"), ("source", "source")
        };

        private readonly string _campaign;
        private readonly string _timeline;
        private readonly string _generation;
        private readonly JObject _envelope;

        private ReignWorldHistoryEventProof(string origin, string campaign, string timeline, string generation, JObject envelope)
        {
            Origin = origin;
            _campaign = campaign;
            _timeline = timeline;
            _generation = generation;
            _envelope = envelope;
        }

        internal string Origin { get; }

        internal string PathAndQuery => "/world-history/event/" + Uri.EscapeDataString(Text(_envelope, "eventId"))
            + "?eventProofVersion=1&campaignId=" + Uri.EscapeDataString(_campaign)
            + "&timelineId=" + Uri.EscapeDataString(_timeline)
            + "&sharedActivityGeneration=" + Uri.EscapeDataString(_generation);

        internal static bool TryCreate(string origin, string campaign, string timeline, string generation, JObject envelope,
            out ReignWorldHistoryEventProof proof)
        {
            proof = null;
            if (!ExactNonempty(origin) || !ExactNonempty(campaign) || !ExactNonempty(timeline) || generation == null || envelope == null)
                return false;

            envelope = (JObject)envelope.DeepClone();
            if (envelope.Count != EventStrings.Length + 5 || EventStrings.Any(field => Text(envelope, field.Native) == null)
                || Text(envelope, "source") != "reign_system" || Text(envelope, "category") != "justice"
                || Text(envelope, "disseminationClass") != "ordinary" || !Flag(envelope, "isComplete")
                || !Integer(envelope["sequence"], out long sequence) || sequence <= 0 || !Day(envelope["worldDay"], out _)
                || !ExactNonempty(Text(envelope, "eventType")) || !ExactNonempty(Text(envelope, "phase"))
                || string.IsNullOrWhiteSpace(Text(envelope, "summary"))
                || !(envelope["payload"] is JObject material) || !IsExactJson(material)
                || !(envelope["entities"] is JArray entities) || !entities.All(AdmissibleEntity))
                return false;

            string eventId = Text(envelope, "eventId");
            if (!ExactNonempty(eventId) || eventId == "." || eventId == "..") return false;
            proof = new ReignWorldHistoryEventProof(origin, campaign, timeline, generation, envelope);
            return true;
        }

        internal bool Matches(JObject reply)
        {
            if (!Flag(reply, "ok") || Text(reply, "result") != "observed"
                || !Integer(reply["eventProofVersion"], out long version) || version != 1
                || !(reply["ownership"] is JObject owner) || Text(owner, "campaignId") != _campaign
                || Text(owner, "timelineId") != _timeline || Text(owner, "sharedActivityGeneration") != _generation
                || !(reply["event"] is JObject observed) || Text(observed, "campaign_id") != _campaign
                || Text(observed, "timeline_id") != _timeline || !Integer(observed["is_complete"], out long complete) || complete != 1
                || !Integer(observed["sequence"], out long sequence)
                || !Integer(_envelope["sequence"], out long expectedSequence) || sequence != expectedSequence
                || !Day(observed["world_day"], out double day) || !Day(_envelope["worldDay"], out double expectedDay) || day != expectedDay
                || EventStrings.Any(field => Text(observed, field.Stored) != Text(_envelope, field.Native))
                || !StoredMaterialMatches(Text(observed, "payload_json"), _envelope["payload"])
                || !(observed["entities"] is JArray entities))
                return false;

            var expected = (JArray)_envelope["entities"];
            if (entities.Count != expected.Count) return false;
            for (int ordinal = 0; ordinal < expected.Count; ordinal++)
            {
                var entity = (JObject)expected[ordinal];
                if (!(entities[ordinal] is JObject stored) || Text(stored, "event_id") != Text(_envelope, "eventId")
                    || !Integer(stored["ordinal"], out long storedOrdinal) || storedOrdinal != ordinal
                    || Text(stored, "entity_id") != Text(entity, "entityId")
                    || Text(stored, "entity_type") != Text(entity, "entityType") || Text(stored, "role") != Text(entity, "role")
                    || !StoredMaterialMatches(Text(stored, "payload_json"), entity))
                    return false;
            }
            return true;
        }

        private static bool AdmissibleEntity(JToken candidate) => candidate is JObject entity && IsExactJson(entity)
            && ExactNonempty(Text(entity, "entityId")) && ExactNonempty(Text(entity, "entityType"))
            && !string.IsNullOrWhiteSpace(Text(entity, "role"));

        private static bool IsExactJson(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object: return ((JObject)token).Properties().All(property => IsExactJson(property.Value));
                case JTokenType.Array: return token.Children().All(IsExactJson);
                case JTokenType.Integer: return Integer(token, out _);
                case JTokenType.String:
                case JTokenType.Boolean:
                case JTokenType.Null: return true;
                // Justice payloads use integral effects and revisions. A future fractional wire schema needs its own exact comparison.
                default: return false;
            }
        }

        private static bool StoredMaterialMatches(string json, JToken expected)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                {
                    var stored = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    return !reader.Read() && IsExactJson(stored) && JToken.DeepEquals(expected, stored);
                }
            }
            catch (JsonException) { return false; }
        }

        private static bool ExactNonempty(string value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim();

        private static string Text(JObject source, string key) => source?[key]?.Type == JTokenType.String
            ? (string)source[key] : null;

        private static bool Flag(JObject source, string key) => source?[key]?.Type == JTokenType.Boolean && (bool)source[key];

        private static bool Integer(JToken token, out long value)
        {
            value = 0;
            return token?.Type == JTokenType.Integer
                && long.TryParse(token.ToString(Formatting.None), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        private static bool Day(JToken token, out double day)
        {
            day = 0;
            if (token?.Type == JTokenType.Integer)
            {
                // A rounded binary64 conversion of a large integer cannot establish exact equality.
                if (!Integer(token, out long integer) || integer < 0 || integer > 9007199254740992L) return false;
                day = integer;
                return true;
            }
            return token?.Type == JTokenType.Float
                && double.TryParse(token.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out day)
                && day >= 0 && !double.IsInfinity(day);
        }
    }
}
