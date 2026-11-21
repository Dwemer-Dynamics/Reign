using Newtonsoft.Json.Linq;
using ReignBeta.Rebellion.Campaign;

namespace Reign.Mcp.Tests;

public sealed class WeeklyRebellionTests
{
    [Fact]
    public void A_sparse_realm_keeps_its_week_while_other_realms_advance_across_a_reload()
    {
        var next = new Dictionary<string, int>();
        WeeklyRebellionSchedule.Enroll(next, ["sparse", "complete"], 11, 14);
        Assert.Equal(12, next["sparse"]);
        Assert.True(WeeklyRebellionSchedule.Finish(next, "complete", 12, 14));
        Assert.True(WeeklyRebellionSchedule.Finish(next, "complete", 13, 14));
        Assert.False(WeeklyRebellionSchedule.Finish(next, "complete", 14, 14));
        Assert.False(WeeklyRebellionSchedule.Finish(next, "complete", 12, 14));
        var restored = new Dictionary<string, int>(next);
        WeeklyRebellionSchedule.Enroll(restored, ["sparse", "complete", "new"], 11, 15);
        Assert.Equal(12, restored["sparse"]);
        Assert.Equal(15, restored["complete"]);
        Assert.Equal(15, restored["new"]);
        Assert.Equal(11, WeeklyRebellionSchedule.CompletedWeek(restored, 15));
        WeeklyRebellionSchedule.Enroll(restored, ["complete", "new"], 11, 15);
        Assert.Equal(14, WeeklyRebellionSchedule.CompletedWeek(restored, 15));
    }

    private static WeeklyRebellionClan Clan(string id, bool player = false, bool ruler = false) => new()
    {
        Id = id, Leader = id, HasLivingLeader = true, IsPlayer = player, IsRuling = ruler, EligibleLeader = true
    };

    private static WeeklyRebellionRealm Realm(bool playerRuler)
    {
        string ruler = playerRuler ? "player" : "npc_ruler";
        return new WeeklyRebellionRealm
        {
            Id = "realm", Ruler = ruler, Exclusion = "",
            Clans = [Clan(ruler, playerRuler, true), Clan("claimant"), Clan("voter"),
                Clan(playerRuler ? "other" : "player", !playerRuler)]
        };
    }

    private static JObject Response(JObject request, Func<string, string, int?> relation) => new()
    {
        ["ok"] = true, ["campaignId"] = "campaign", ["timelineId"] = "timeline",
        ["attitudes"] = new JArray(((JArray)request["pairs"]!).Select(pair =>
        {
            string observer = pair.Value<string>("observerId")!, target = pair.Value<string>("targetId")!;
            int? value = relation(observer, target);
            return new JObject
            {
                ["observerId"] = observer, ["targetId"] = target, ["hasPair"] = value.HasValue,
                ["effectiveAttitude"] = value ?? 0, ["personalAffinity"] = value ?? 0,
                ["targetPublicStanding"] = 0, ["publicStandingRevision"] = 1
            };
        }))
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_vassal_rolls_against_either_ruler_and_only_vassals_vote(bool playerRuler)
    {
        var realm = Realm(playerRuler);
        var attitudes = new WeeklyRebellionAttitudes();
        var grievances = realm.Grievances().ToArray();
        Assert.Contains(("claimant", realm.Ruler), grievances);
        Assert.All(grievances, pair => { Assert.NotEqual("player", pair.Item1); Assert.Equal(realm.Ruler, pair.Item2); });
        await attitudes.Fetch(grievances, "campaign", "timeline",
            p => Task.FromResult(Response(p, (observer, _) => observer == "claimant" ? -35 : -34)));
        int eligibleRolls = 0;
        Assert.True(realm.RecordRolls(attitudes, (clan, _, exclusion) =>
        {
            Assert.True(clan.IsVassal);
            if (exclusion == "relationship_threshold") return 0;
            Assert.Empty(exclusion);
            eligibleRolls++;
            return 5;
        }));
        Assert.Equal(1, eligibleRolls);
        var winner = realm.Winner()!;
        Assert.Equal("claimant", winner.Id);
        await attitudes.Fetch(realm.JoinPairs(winner), "campaign", "timeline", p =>
        {
            Assert.All((JArray)p["pairs"]!, pair => Assert.Equal("claimant", (string?)pair["targetId"]));
            return Task.FromResult(Response(p, (observer, _) => observer == "voter" ? 0 : -34));
        });
        Assert.True(realm.TryPoll(winner, attitudes, out var sides));
        Assert.True(sides.Single(x => x.ClanId == "voter").Rebel);
        Assert.False(sides.Single(x => x.ClanId == "player").Rebel);
        Assert.False(sides.Single(x => x.ClanId == (playerRuler ? "other" : "npc_ruler")).Rebel);
    }

    [Fact]
    public async Task Missing_grievance_and_missing_supporter_defer_without_rerolling_or_applying_a_partial_poll()
    {
        var realm = Realm(false);
        var initial = new WeeklyRebellionAttitudes();
        await initial.Fetch(realm.Grievances(), "campaign", "timeline",
            p => Task.FromResult(Response(p, (observer, _) => observer == "claimant" ? -35 : null)));
        var saved = new List<(string Clan, int Roll)>();
        int Save(WeeklyRebellionClan clan, JObject? _, string reason)
        {
            int roll = reason.Length == 0 ? 5 : 0;
            saved.Add((clan.Id, roll));
            return roll;
        }
        Assert.False(realm.RecordRolls(initial, Save));
        Assert.Equal([("claimant", 5)], saved);
        var retry = new WeeklyRebellionAttitudes();
        Assert.Equal(("voter", realm.Ruler), Assert.Single(realm.Grievances()));
        await retry.Fetch(realm.Grievances(), "campaign", "timeline", p => Task.FromResult(Response(p, (_, _) => 0)));
        Assert.True(realm.RecordRolls(retry, Save));
        var winner = realm.Winner()!;
        await retry.Fetch(realm.JoinPairs(winner), "campaign", "timeline", p =>
            Task.FromResult(Response(p, (observer, _) => observer == "voter" ? null : 0)));
        Assert.False(realm.TryPoll(winner, retry, out var incomplete));
        Assert.Empty(incomplete);
        // A new attempt after restoring the same saved outcomes needs no RNG or grievance request.
        var replay = Realm(false);
        foreach (var clan in replay.Clans.Where(c => c.IsVassal))
        {
            var original = realm.Clans.Single(c => c.Id == clan.Id);
            clan.HasRoll = original.HasRoll; clan.Triggered = original.Triggered; clan.RulerRelation = original.RulerRelation;
        }
        Assert.Empty(replay.Grievances());
        var recovered = new WeeklyRebellionAttitudes();
        Assert.True(replay.RecordRolls(recovered, (_, _, _) => throw new Exception("Rerolled a saved clan")));
        var savedWinner = replay.Winner()!;
        await recovered.Fetch(replay.JoinPairs(savedWinner), "campaign", "timeline", p => Task.FromResult(Response(p, (_, _) => 0)));
        Assert.True(replay.TryPoll(savedWinner, recovered, out var sides));
        Assert.False(sides.Single(x => x.ClanId == "voter").Rebel);
        Assert.Equal([("claimant", 5), ("voter", 0)], saved);
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(5001)]
    public async Task Bounded_pages_cover_every_pair_including_the_tail(int count)
    {
        var pairs = Enumerable.Range(0, count).Select(i => ("vassal_" + i, "ruler_" + (i % 3))).ToArray();
        var attitudes = new WeeklyRebellionAttitudes();
        int sent = 0;
        await attitudes.Fetch(pairs.Concat(pairs), "campaign", "timeline", p =>
        {
            int size = ((JArray)p["pairs"]!).Count;
            Assert.InRange(size, 1, WeeklyRebellionAttitudes.PageSize);
            sent += size;
            var response = Response(p, (_, _) => 0);
            response["attitudes"] = new JArray(((JArray)response["attitudes"]!).Reverse());
            return Task.FromResult(response);
        });
        Assert.Equal(count, sent);
        Assert.All(pairs, pair => Assert.Equal(0, (int?)attitudes.Find(pair.Item1, pair.Item2)?["effectiveAttitude"]));
    }

    [Theory]
    [InlineData("omit")]
    [InlineData("duplicate")]
    [InlineData("reverse")]
    [InlineData("missing_value")]
    [InlineData("campaign")]
    [InlineData("timeline")]
    public async Task Incomplete_or_mismatched_late_page_exposes_no_partial_result(string damage)
    {
        var attitudes = new WeeklyRebellionAttitudes();
        int page = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => attitudes.Fetch(
            Enumerable.Range(0, WeeklyRebellionAttitudes.PageSize + 2).Select(i => ("v" + i, "r")),
            "campaign", "timeline", p =>
            {
                var response = Response(p, (_, _) => 0);
                if (++page == 2)
                {
                    var rows = (JArray)response["attitudes"]!;
                    switch (damage)
                    {
                        case "omit": rows.RemoveAt(1); break;
                        case "duplicate": rows[1] = rows[0].DeepClone(); break;
                        case "reverse":
                            string observer = rows[0].Value<string>("observerId")!;
                            rows[0]["observerId"] = rows[0].Value<string>("targetId");
                            rows[0]["targetId"] = observer;
                            break;
                        case "missing_value": ((JObject)rows[0]).Remove("effectiveAttitude"); break;
                        case "campaign": response["campaignId"] = "another"; break;
                        case "timeline": response["timelineId"] = "another"; break;
                    }
                }
                return Task.FromResult(response);
            }));
        Assert.Equal(2, page);
        Assert.Null(attitudes.Find("v0", "r"));
    }

    [Fact]
    public async Task A_save_change_cancelling_dispatch_on_a_late_page_discards_the_earlier_page()
    {
        var attitudes = new WeeklyRebellionAttitudes();
        int dispatches = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => attitudes.Fetch(
            Enumerable.Range(0, WeeklyRebellionAttitudes.PageSize + 1).Select(i => ("v" + i, "r")),
            "campaign", "timeline", request => ++dispatches == 1
                ? Task.FromResult(Response(request, (_, _) => -35))
                : throw new OperationCanceledException("Restored the same campaign and timeline.")));
        Assert.Equal(2, dispatches);
        Assert.Null(attitudes.Find("v0", "r"));
    }

    [Fact]
    public async Task Eligible_leaders_keep_the_five_percent_boundary_and_saved_tie_order()
    {
        var realm = Realm(false);
        var attitudes = new WeeklyRebellionAttitudes();
        await attitudes.Fetch(realm.Grievances(), "campaign", "timeline", p => Task.FromResult(Response(p, (_, _) => -36)));
        for (int roll = 1; roll <= 100; roll++)
        {
            var trial = Realm(false);
            Assert.True(trial.RecordRolls(attitudes, (_, _, _) => roll));
            Assert.Equal(roll <= 5 ? "claimant" : null, trial.Winner()?.Id);
        }
        Assert.True(realm.SameRoster(Realm(false)));
        Assert.False(realm.SameRoster(Realm(true)));
        var changed = Realm(false);
        changed.Clans[1].Leader = "heir";
        Assert.False(realm.SameRoster(changed));
        Assert.False(WeeklyRebellionClan.MatchesSavedHero("claimant", "heir"));
        Assert.True(WeeklyRebellionClan.MatchesSavedHero("", "legacy_leader"));
    }
}
