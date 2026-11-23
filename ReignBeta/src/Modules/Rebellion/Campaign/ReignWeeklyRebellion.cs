#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Rebellion.Campaign
{
    internal static class WeeklyRebellionSchedule
    {
        internal static void Enroll(Dictionary<string, int> next, IEnumerable<string> realmIds, int completedWeek, int currentWeek)
        {
            var live = new HashSet<string>(realmIds);
            // On first load inherit the previous global cadence. A newly formed
            // kingdom does not inherit older unresolved weeks from another realm.
            int firstWeek = next.Count == 0 && completedWeek >= 0 ? completedWeek + 1 : currentWeek;
            foreach (string id in next.Keys.Where(id => !live.Contains(id)).ToList()) next.Remove(id);
            foreach (string id in live)
                if (!next.ContainsKey(id)) next.Add(id, firstWeek);
        }

        internal static int CompletedWeek(Dictionary<string, int> next, int currentWeek) =>
            next.Count == 0 ? currentWeek : next.Values.Min() - 1;

        internal static bool Finish(Dictionary<string, int> next, string realmId, int week, int currentWeek)
        {
            if (next[realmId] != week) return false;
            next[realmId] = week + 1;
            return week < currentWeek;
        }
    }

    internal sealed class WeeklyRebellionClan
    {
        internal string Id = string.Empty, Leader = string.Empty;
        internal bool IsPlayer, IsRuling, HasLivingLeader, EligibleLeader;
        internal bool HasRoll, Triggered;
        internal int RulerRelation;

        internal bool IsVassal => !IsPlayer && !IsRuling;
        internal bool CanVote => IsVassal && EligibleLeader;

        // Older roll records have no hero ids. Preserve their original clan/week identity.
        internal static bool MatchesSavedHero(string? saved, string? current) => string.IsNullOrEmpty(saved)
            || string.Equals(saved, current, StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class WeeklyRebellionRealm
    {
        private const int RelationshipThreshold = -35;
        private const int WeeklyOutbreakPercent = 5;
        internal string Id = string.Empty, Ruler = string.Empty, Exclusion = string.Empty;
        internal List<WeeklyRebellionClan> Clans = new List<WeeklyRebellionClan>();

        private static bool AtRebellionThreshold(int relation) => relation <= RelationshipThreshold;
        internal static bool WonRoll(bool eligible, int roll) => eligible && roll >= 1 && roll <= WeeklyOutbreakPercent;

        internal bool SameRoster(WeeklyRebellionRealm? other) =>
            other != null && Ruler == other.Ruler && Exclusion == other.Exclusion
            && Clans.Select(Identity).SequenceEqual(other.Clans.Select(Identity));

        private static (string, string, bool, bool, bool, bool) Identity(WeeklyRebellionClan clan) =>
            (clan.Id, clan.Leader, clan.IsPlayer, clan.IsRuling, clan.HasLivingLeader, clan.EligibleLeader);

        internal IEnumerable<(string, string)> Grievances() => Clans
            .Where(clan => Exclusion.Length == 0 && clan.CanVote && !clan.HasRoll)
            .Select(clan => (clan.Leader, Ruler));

        // Persist each available roll before asking for supporters. A missing
        // grievance leaves that clan unrolled and the winner undecided.
        internal bool RecordRolls(WeeklyRebellionAttitudes attitudes,
            Func<WeeklyRebellionClan, JObject?, string, int> saveRoll)
        {
            bool complete = true;
            foreach (var clan in Clans.Where(clan => clan.IsVassal && !clan.HasRoll))
            {
                string exclusion = Exclusion;
                JObject? attitude = null;
                if (exclusion.Length == 0)
                {
                    if (!clan.EligibleLeader) exclusion = "ineligible_leader";
                    else if ((attitude = attitudes.Find(clan.Leader, Ruler)) == null)
                    {
                        complete = false;
                        continue;
                    }
                    else if (!AtRebellionThreshold(attitude.Value<int>("effectiveAttitude")))
                        exclusion = "relationship_threshold";
                }
                clan.RulerRelation = attitude?.Value<int>("effectiveAttitude") ?? 0;
                int roll = saveRoll(clan, attitude, exclusion);
                clan.HasRoll = true;
                clan.Triggered = WonRoll(exclusion.Length == 0, roll);
            }
            return complete;
        }

        internal WeeklyRebellionClan? Winner() => Exclusion.Length == 0
            ? Clans.Where(clan => clan.CanVote && clan.Triggered)
                .OrderBy(clan => clan.RulerRelation).ThenBy(clan => clan.Id).FirstOrDefault()
            : null;

        internal IEnumerable<(string, string)> JoinPairs(WeeklyRebellionClan winner)
        {
            // Fixed NPC members also record their relations at joining. Self and
            // player-clan observers have never been part of the supporter poll.
            foreach (var clan in Clans.Where(clan => clan.HasLivingLeader && !clan.IsPlayer))
            {
                if (clan.Leader != winner.Leader) yield return (clan.Leader, winner.Leader);
                if (clan.Leader != Ruler && clan != winner) yield return (clan.Leader, Ruler);
            }
        }

        internal bool TryPoll(WeeklyRebellionClan winner, WeeklyRebellionAttitudes attitudes,
            out List<(string ClanId, bool Rebel, int ToRebel, int ToRuler)> memberships)
        {
            memberships = new List<(string, bool, int, int)>();
            foreach (var clan in Clans)
            {
                int? rebel = attitudes.Find(clan.Leader, winner.Leader)?.Value<int>("effectiveAttitude");
                int? ruler = clan == winner ? winner.RulerRelation
                    : attitudes.Find(clan.Leader, Ruler)?.Value<int>("effectiveAttitude");
                bool joins = clan == winner;
                if (!joins && clan.CanVote)
                {
                    if (!rebel.HasValue || !ruler.HasValue)
                    {
                        memberships.Clear();
                        return false;
                    }
                    joins = rebel.Value > ruler.Value;
                }
                memberships.Add((clan.Id, joins, rebel ?? 0, ruler ?? 0));
            }
            return true;
        }
    }

    // Scoped to one realm and one attempt. The response must account for each
    // directed identity, including explicit hasPair=false rows.
    internal sealed class WeeklyRebellionAttitudes
    {
        internal const int PageSize = 128;
        private readonly Dictionary<(string, string), JObject> rows = new Dictionary<(string, string), JObject>();

        internal JObject? Find(string observer, string target) =>
            rows.TryGetValue((observer, target), out var row) && row.Value<bool>("hasPair") ? row : null;

        internal async Task Fetch(IEnumerable<(string Observer, string Target)> pairs, string campaign,
            string timeline, Func<JObject, Task<JObject>> post)
        {
            var needed = pairs.Distinct().Where(pair => !rows.ContainsKey(pair)).ToList();
            var received = new Dictionary<(string, string), JObject>();
            for (int offset = 0; offset < needed.Count; offset += PageSize)
            {
                var page = needed.GetRange(offset, Math.Min(PageSize, needed.Count - offset));
                var remaining = new HashSet<(string, string)>(page);
                var response = await post(new JObject
                {
                    ["campaignId"] = campaign, ["timelineId"] = timeline,
                    ["pairs"] = new JArray(page.Select(pair => new JObject
                    {
                        ["observerId"] = pair.Observer, ["targetId"] = pair.Target, ["context"] = "weekly_rebellion"
                    }))
                }).ConfigureAwait(false);
                if (response == null || response.Value<bool?>("ok") != true || response.Value<string>("campaignId") != campaign
                    || response.Value<string>("timelineId") != timeline || !(response["attitudes"] is JArray data))
                    throw new InvalidDataException(response?.Value<string>("error") ?? "Invalid effective-attitude response.");
                foreach (var item in data)
                {
                    if (!(item is JObject row) || row["observerId"]?.Type != JTokenType.String
                        || row["targetId"]?.Type != JTokenType.String || row["hasPair"]?.Type != JTokenType.Boolean)
                        throw new InvalidDataException("Malformed effective-attitude row.");
                    var key = (row.Value<string>("observerId")!, row.Value<string>("targetId")!);
                    if (!remaining.Remove(key))
                        throw new InvalidDataException("Unexpected or repeated effective-attitude pair " + key + ".");
                    if (row.Value<bool>("hasPair") && (row["effectiveAttitude"]?.Type != JTokenType.Integer
                        || row["personalAffinity"]?.Type != JTokenType.Integer || row["targetPublicStanding"]?.Type != JTokenType.Integer
                        || row["publicStandingRevision"]?.Type != JTokenType.Integer))
                        throw new InvalidDataException("Incomplete effective-attitude values for " + key + ".");
                    received.Add(key, row);
                }
                if (remaining.Count != 0)
                    throw new InvalidDataException("Missing effective-attitude pair " + remaining.First() + ".");
            }
            foreach (var entry in received) rows.Add(entry.Key, entry.Value);
        }
    }
}
