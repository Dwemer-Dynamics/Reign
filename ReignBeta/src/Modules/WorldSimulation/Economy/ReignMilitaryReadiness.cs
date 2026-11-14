using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Shared;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Economy
{
    // Standing is strategic intent, not a new party-size limit or a grant of soldiers.
    internal static class ReignMilitaryReadiness
    {
        internal static bool AtWar(Kingdom realm) => realm != null && Kingdom.All.Any(other => other != realm
            && !other.IsEliminated && realm.IsAtWarWith(other));
        private static bool CurrentOrder(Kingdom realm, out EconomyStandingOrder order)
        {
            order = null;
            return realm != null && ReignWarEconomyTransactions.State?.MilitaryStandings.TryGetValue(realm.StringId, out order) == true
                && ReignWarEconomyPolicy.StandingOrderApplies(order?.Ruler, realm.Leader?.StringId);
        }
        internal static string OrderedMode(Kingdom realm) => CurrentOrder(realm, out var order) ? order.Mode : "automatic";
        internal static int? PeacePercent(Kingdom realm) => CurrentOrder(realm, out var order) ? order.PeacePercent : null;
        internal static string EffectiveMode(Kingdom realm)
        {
            if (realm == null) return "peace";
            bool player = realm.Leader == Hero.MainHero;
            if (!player && AtWar(realm)) return "war";
            if (CurrentOrder(realm, out var order))
                return !player && order.Automatic ? "peace" : order.Mode;
            // Preserve the player's previously unrestricted forces until the sovereign makes a choice.
            return player ? "war" : "peace";
        }

        internal static bool AcceptPressure(JObject snapshot, string campaign, string timeline, double observedDay)
        {
            var state = ReignWarEconomyTransactions.State;
            if (state == null || campaign != ReignServerClient.GetCampaignId()
                || timeline != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                || !ReignWarEconomyPolicy.PressureFresh(observedDay, CampaignTime.Now.ToDays)
                || !TryParsePressure(snapshot, campaign, timeline, out var pairs)) return false;
            state.MilitaryPressure = pairs;
            state.MilitaryPressureCampaign = campaign; state.MilitaryPressureTimeline = timeline;
            state.MilitaryPressureDay = observedDay;
            return true;
        }

        // Validate the whole snapshot before replacing the current cache. JSON integers can exceed Int32.
        internal static bool TryParsePressure(JObject snapshot, string campaign, string timeline,
            out List<EconomyPressurePair> pairs)
        {
            pairs = null;
            if (string.IsNullOrEmpty(campaign) || string.IsNullOrEmpty(timeline)
                || snapshot?["version"]?.Type != JTokenType.Integer || snapshot["version"].ToString() != "1"
                || snapshot["complete"]?.Type != JTokenType.Boolean || !snapshot["complete"].Value<bool>()
                || snapshot["campaignId"]?.Type != JTokenType.String || (string)snapshot["campaignId"] != campaign
                || snapshot["timelineId"]?.Type != JTokenType.String || (string)snapshot["timelineId"] != timeline
                || !(snapshot["pairs"] is JArray rows) || rows.Count > 1024) return false;
            var parsed = new List<EconomyPressurePair>(rows.Count);
            var seen = new HashSet<Tuple<string, string>>();
            foreach (var token in rows)
            {
                if (!(token is JObject row) || row["actor_kingdom_id"]?.Type != JTokenType.String
                    || row["target_kingdom_id"]?.Type != JTokenType.String
                    || row["pressure_value"]?.Type != JTokenType.Integer
                    || !int.TryParse(row["pressure_value"].ToString(Newtonsoft.Json.Formatting.None), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int value) || value < -100 || value > 100
                    || (row["dominant_channel"] != null && row["dominant_channel"].Type != JTokenType.Null
                        && row["dominant_channel"].Type != JTokenType.String)) return false;
                string actor = (string)row["actor_kingdom_id"], target = (string)row["target_kingdom_id"];
                if (string.IsNullOrEmpty(actor) || string.IsNullOrEmpty(target) || actor == target
                    || !seen.Add(Tuple.Create(actor, target))) return false;
                parsed.Add(new EconomyPressurePair { Actor = actor, Target = target, Value = value,
                    Channel = (string)row["dominant_channel"] ?? "" });
            }
            pairs = parsed;
            return true;
        }

        private static bool Fresh => ReignWarEconomyTransactions.State != null
            && ReignWarEconomyTransactions.State.MilitaryPressureCampaign == ReignServerClient.GetCampaignId()
            && ReignWarEconomyTransactions.State.MilitaryPressureTimeline == (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
            && ReignWarEconomyPolicy.PressureFresh(ReignWarEconomyTransactions.State.MilitaryPressureDay, CampaignTime.Now.ToDays);

        private static bool NearFrontier(MobileParty party, Kingdom other)
        {
            // A directed threat affects its actual frontier; hostile third-party pairs cannot raise this party's target.
            var home = party.CurrentSettlement?.IsFortification == true && party.CurrentSettlement.OwnerClan?.Kingdom == party.ActualClan.Kingdom
                ? party.CurrentSettlement
                : party.ActualClan.Fiefs.Select(x => x.Settlement).OrderBy(x => x.GetPosition2D.DistanceSquared(party.GetPosition2D)).FirstOrDefault();
            return home != null && other.Fiefs.Any(x => x.Settlement.GetPosition2D.DistanceSquared(home.GetPosition2D) <= 90f * 90f);
        }

        internal static double TargetFraction(MobileParty party)
        {
            var realm = party?.ActualClan?.Kingdom;
            if (realm == null || party.IsGarrison || !party.IsLordParty || party.IsMainParty) return 1;
            bool player = realm.Leader == Hero.MainHero, orderedWar = EffectiveMode(realm) == "war";
            int? percent = PeacePercent(realm);
            double fraction = ReignWarEconomyPolicy.FieldReadiness(AtWar(realm), orderedWar, 0, false, false, player, percent);
            if (fraction >= 1 || percent.HasValue) return fraction;
            if (Fresh)
                foreach (var pair in ReignWarEconomyTransactions.State.MilitaryPressure.Where(x => x.Value > 0
                    && (x.Actor == realm.StringId || x.Target == realm.StringId)))
                {
                    var other = Kingdom.All.FirstOrDefault(x => !x.IsEliminated && x.StringId == (pair.Actor == realm.StringId ? pair.Target : pair.Actor));
                    if (other != null) fraction = Math.Max(fraction, ReignWarEconomyPolicy.FieldReadiness(false, false, pair.Value, NearFrontier(party, other), true));
                }
            return fraction;
        }

        internal static int RecruitmentRoom(MobileParty party)
        {
            if (party?.IsLordParty != true || party.IsGarrison || party.IsMainParty || party.ActualClan?.Kingdom == null)
                return int.MaxValue;
            // Explicit player force requests keep their exact target and the ordinary resource-waiting contract.
            if (HasPlayerForceOrder(party)) return int.MaxValue;
            int cap = Math.Max(0, party.Party.PartySizeLimit);
            int target = Math.Min(cap, Math.Max(15, (int)Math.Floor(cap * TargetFraction(party))));
            return Math.Max(0, target - party.MemberRoster.TotalManCount);
        }

        private static bool HasPlayerForceOrder(MobileParty party)
        {
            var order = ReignCampaignCommandBehavior.Instance?.ActiveOrderFor(party?.LeaderHero?.StringId);
            return order?.Objective == "recruit_resupply" && !order.IsTerminal && order.IssuerHeroStringId == Hero.MainHero?.StringId;
        }

        internal static bool SafeForDrawdown(MobileParty party) => party?.IsActive == true && party.IsLordParty
            && !party.IsMainParty && !party.IsGarrison && party.ActualClan?.Kingdom != null
            && party.LeaderHero?.IsAlive == true && !party.LeaderHero.IsPrisoner && party.MapEvent == null
            && party.Army == null && !party.IsCurrentlyAtSea
            && party.CurrentSettlement?.IsUnderSiege != true && party.CurrentSettlement?.IsUnderRaid != true
            && !HasPlayerForceOrder(party) && TargetFraction(party) < 1;

        internal static Dictionary<int, int> HealthyByTier(MobileParty party) => party.MemberRoster.GetTroopRoster()
            .Where(x => x.Character != null && !x.Character.IsHero && x.Number > x.WoundedNumber)
            .GroupBy(x => x.Character.Tier).OrderBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Sum(t => t.Number - t.WoundedNumber));

        internal static JObject DrawdownParty(MobileParty party)
        {
            var state = ReignWarEconomyTransactions.State;
            var result = new JObject { ["removed"] = 0, ["reason"] = "not eligible for safe peace drawdown" };
            if (state == null || !SafeForDrawdown(party)) return result;
            int day = (int)Math.Floor(CampaignTime.Now.ToDays);
            string generation = ReignWarEconomyTransactions.Generation(party), key = party.StringId + ":" + generation;
            if (state.DrawdownPending.Any(x => x.Party == party.StringId && x.Generation == generation))
            { result["reason"] = "an interrupted drawdown requires inspection; no replay"; return result; }
            if (state.DrawdownDays.TryGetValue(key, out int previous) && previous == day)
            { result["reason"] = "already processed this party generation today"; return result; }
            if ((!state.DrawdownDays.ContainsKey(key) && state.DrawdownDays.Count >= ReignWarEconomyPolicy.DrawdownPartyLimit)
                || state.DrawdownPending.Count >= 128)
            { result["reason"] = "drawdown receipt capacity unavailable"; return result; }
            int capacity = Math.Max(0, party.Party.PartySizeLimit);
            int target = Math.Min(capacity, Math.Max(15, (int)Math.Floor(capacity * TargetFraction(party))));
            int batch = ReignWarEconomyPolicy.DrawdownBatch(party.MemberRoster.TotalManCount, target, capacity, true);
            if (batch == 0)
            {
                if (party.MemberRoster.TotalManCount <= target) state.DrawdownCarry.Remove(key);
                result["reason"] = "at target or no healthy surplus regulars"; return result;
            }
            var realm = party.ActualClan.Kingdom;
            state.DrawdownCarry.TryGetValue(key, out var carry);
            var plan = ReignWarEconomyPolicy.SelectDischarges(party.MemberRoster.GetTroopRoster()
                .Where(x => x.Character != null && !x.Character.IsHero).Select(x => new ReignWarEconomyPolicy.DischargeCandidate {
                    Id = x.Character.StringId, Tier = x.Character.Tier, Healthy = Math.Max(0, x.Number - x.WoundedNumber),
                    Experience = (double)x.Xp / Math.Max(1, x.Number) }), batch,
                carry?.Kingdom == realm.StringId ? carry.Tiers : null,
                ReignKingdomEventRollerCore.StableHash(key, day, "peace-drawdown-tier-tie"), out var nextCarry);
            if (plan.Count == 0) { result["reason"] = "at target or no healthy surplus regulars"; return result; }
            var receipt = new EconomyDrawdownReceipt { Id = ++state.DrawdownSequence, Party = party.StringId,
                Generation = generation, Kingdom = realm.StringId, Day = day,
                Before = party.MemberRoster.TotalManCount, Planned = plan.Sum(x => x.Healthy), Status = "pending",
                AllocationVersion = ReignWarEconomyPolicy.DrawdownAllocationVersion, HealthyByTierBefore = HealthyByTier(party),
                Troops = plan.ToDictionary(x => x.Id, x => x.Healthy, StringComparer.Ordinal) };
            state.DrawdownPending.Add(receipt);
            state.DrawdownDays[key] = day; // Reserve before native mutation; duplicate/reloaded callbacks cannot repeat it.
            try
            {
                foreach (var selected in plan)
                {
                    var troop = party.MemberRoster.GetTroopRoster().First(x => x.Character.StringId == selected.Id).Character;
                    int before = party.MemberRoster.GetTroopCount(troop);
                    var element = party.MemberRoster.GetTroopRoster().First(x => x.Character == troop);
                    int experience = (int)((long)Math.Max(0, element.Xp) * selected.Healthy / Math.Max(1, element.Number));
                    party.MemberRoster.AddToCounts(troop, -selected.Healthy, false, 0, -experience);
                    receipt.Observed[selected.Id] = before - party.MemberRoster.GetTroopCount(troop);
                    receipt.ExperienceRemoved[selected.Id] = element.Xp - party.MemberRoster.GetTroopRoster().FirstOrDefault(x => x.Character == troop).Xp;
                }
                receipt.After = party.MemberRoster.TotalManCount;
                receipt.Removed = receipt.Before - receipt.After;
                receipt.HealthyByTierAfter = HealthyByTier(party);
                if (receipt.Removed != receipt.Planned || receipt.Observed.Any(x => x.Value != receipt.Troops[x.Key]))
                    throw new InvalidOperationException("Peace drawdown roster conservation failed.");
                receipt.HearthReturned = ReturnDischarged(party.ActualClan.Kingdom, receipt.Removed, receipt.HearthCredits);
                receipt.HearthUnallocated = receipt.Removed * ReignWarEconomyPolicy.HearthReturnPerDischarge - receipt.HearthReturned;
                if (receipt.HearthReturned < 0 || receipt.HearthUnallocated < -.000001)
                    throw new InvalidOperationException("Demobilization hearth return exceeded its actual soldier budget.");
                receipt.Status = "completed";
                if (receipt.After <= target || nextCarry.Values.All(x => x == 0m)) state.DrawdownCarry.Remove(key);
                else state.DrawdownCarry[key] = new EconomyDrawdownCarry { Kingdom = realm.StringId, Tiers = nextCarry };
                state.DemobilizedRegulars += receipt.Removed;
                state.DemobilizationHearthReturned += receipt.HearthReturned;
                state.DrawdownPending.Remove(receipt);
                state.DrawdownRecent.Add(receipt);
                if (state.DrawdownRecent.Count > 128) state.DrawdownRecent.RemoveRange(0, state.DrawdownRecent.Count - 128);
                result = JObject.FromObject(receipt); result["removed"] = receipt.Removed;
                result["target"] = target; result["reason"] = "healthy surplus regulars discharged proportionally by tier";
            }
            catch (Exception ex)
            {
                receipt.Status = "uncertain";
                state.LastError = "Peace drawdown requires inspection: " + ex.Message;
                state.UnsupportedEvents++;
                ReignLog.Exception("Peace drawdown " + party.StringId, ex);
                throw;
            }
            return result;
        }

        // Shared by healthy peace drawdown and medical discharge; callers own the removal journal.
        internal static double ReturnDischarged(Kingdom realm, int removed, Dictionary<string, double> credits)
        {
            if (removed < 0 || credits.Count != 0) throw new InvalidOperationException("Invalid hearth return budget");
            if (realm == null || removed == 0) return 0;
            var villages = Settlement.All.Where(x => realm != null && x.Village?.VillageState == Village.VillageStates.Normal
                && x.OwnerClan?.Kingdom == realm).Select(x => x.Village).ToDictionary(x => x.StringId, StringComparer.Ordinal);
            var plan = ReignWarEconomyPolicy.PlanHearthReturn(removed, villages.Values.Select(x =>
                new ReignWarEconomyPolicy.HearthRecipient { Id = x.StringId, Hearth = x.Hearth }));
            double total = 0;
            foreach (var credit in plan)
            {
                var village = villages[credit.Key]; float before = village.Hearth; village.Hearth = credit.Value;
                double actual = (double)village.Hearth - before;
                credits.Add(credit.Key, actual); total += actual;
                var state = ReignWarEconomyTransactions.State;
                state.DemobilizationHearthByVillage.TryGetValue(credit.Key, out double previous);
                state.DemobilizationHearthByVillage[credit.Key] = previous + actual;
            }
            if (total < 0 || total > removed * ReignWarEconomyPolicy.HearthReturnPerDischarge + .000001)
                throw new InvalidOperationException("Hearth return exceeded soldier budget");
            return total;
        }

        internal static void DrawdownDaily()
        {
            var state = ReignWarEconomyTransactions.State;
            if (state == null) return;
            if (state.DrawdownDays.Count >= ReignWarEconomyPolicy.DrawdownPartyLimit)
            {
                var live = new System.Collections.Generic.HashSet<string>(MobileParty.AllLordParties.Select(ReignWarEconomyTransactions.GenerationKey), StringComparer.Ordinal);
                foreach (string key in state.DrawdownDays.Keys.Where(x => !live.Contains(x))
                    .Take(Math.Max(1, state.DrawdownDays.Count - ReignWarEconomyPolicy.DrawdownPartyLimit + 1)).ToList())
                {
                    state.DrawdownDays.Remove(key);
                    state.DrawdownCarry.Remove(key);
                }
            }
            foreach (var party in MobileParty.AllLordParties.OrderBy(x => x.StringId, StringComparer.Ordinal).ToList())
                try { DrawdownParty(party); }
                catch { /* The pending receipt retains uncertainty; other parties can still process. */ }
        }

        internal static bool Order(Hero ruler, string mode, out string result, int? peacePercent = null, bool retainPercent = true, bool automatic = false)
        {
            var realm = ruler?.Clan?.Kingdom;
            var state = ReignWarEconomyTransactions.State;
            result = "Only the current sovereign may order a kingdom's military standing.";
            if (realm == null || realm.Leader != ruler || state == null || (mode != "peace" && mode != "war")
                || (peacePercent.HasValue && (peacePercent < 40 || peacePercent > 90 || peacePercent % 5 != 0))) return false;
            state.MilitaryStandings.TryGetValue(realm.StringId, out var previous);
            int? selectedPercent = retainPercent ? previous?.PeacePercent : peacePercent;
            if (previous?.Mode == mode && previous.PeacePercent == selectedPercent && previous.Automatic == automatic && previous.Ruler == ruler.StringId)
            { result = "That standing is already ordered."; return true; }
            // Same constitutional route as existing recruitment/recovery strategy; never declares war or spends a realm treasury.
            var action = new ReignWorldActionRecord { Type = ReignWorldActionType.StrategyRecruitAndRecover,
                ActorHeroStringId = ruler.StringId, ActorKingdomStringId = realm.StringId,
                ActionId = "military-standing:" + realm.StringId + ":" + mode + ":" + ((previous?.Revision ?? 0) + 1),
                Reason = "Order " + mode + " military standing", TermsJson = new JObject { ["militaryStanding"] = mode,
                    ["peacePercent"] = selectedPercent.HasValue ? new JValue(selectedPercent.Value) : JValue.CreateNull() }.ToString() };
            if (!ReignBeta.Government.ReignGovernmentActionGate.TryAuthorize(action, out var blocked))
            { result = blocked?.Message ?? "Government authorization is pending."; return false; }
            state.MilitaryStandings[realm.StringId] = new EconomyStandingOrder { Mode = mode, Ruler = ruler.StringId,
                Reason = ruler == Hero.MainHero ? "Sovereign order" : "NPC sovereign order", Day = CampaignTime.Now.ToDays,
                Revision = (previous?.Revision ?? 0) + 1, PeacePercent = selectedPercent, Automatic = automatic };
            if (previous != null && previous.Mode != mode && ruler != Hero.MainHero)
                ReignBeta.Campaign.ReignNewsCampaignBehavior.Instance?.MilitaryStanding(realm, mode,
                    state.MilitaryStandings[realm.StringId].Revision);
            result = mode == "war" ? "War standing ordered. Mobilization still requires recruits, food and funding."
                : "Peace standing ordered. Your chosen field target remains in force during war; garrison targets stay full.";
            return true;
        }

        internal static void UpdateNpcStandings()
        {
            foreach (var realm in Kingdom.All.Where(x => !x.IsEliminated && x.Leader != null && x.Leader != Hero.MainHero))
            {
                var state = ReignWarEconomyTransactions.State;
                if (!AtWar(realm) && state?.MilitaryStandings.TryGetValue(realm.StringId, out var standing) == true
                    && !standing.Automatic && standing.Mode == "war" && standing.Ruler == realm.Leader.StringId) continue;
                Order(realm.Leader, AtWar(realm) ? "war" : "peace", out _, null, false, true);
            }
        }

        internal static JObject Observation(Kingdom realm) => new JObject {
            ["version"] = 1, ["kingdomId"] = realm?.StringId, ["atWar"] = AtWar(realm),
            ["orderedMode"] = OrderedMode(realm),
            ["effectiveMode"] = EffectiveMode(realm), ["peacePercent"] = PeacePercent(realm),
            ["pressureFresh"] = Fresh, ["pressureObservedDay"] = ReignWarEconomyTransactions.State?.MilitaryPressureDay ?? -1,
            ["garrisonTargetFactor"] = 1, ["garrisonWageFactor"] = ReignWarEconomyPolicy.GarrisonWageFactor,
            ["garrisonMenPerFood"] = ReignWarEconomyPolicy.GarrisonMenPerFood,
            ["parties"] = new JArray(MobileParty.AllLordParties.Where(x => x?.ActualClan?.Kingdom == realm && !x.IsMainParty)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).Select(x => new JObject { ["partyId"] = x.StringId,
                    ["targetFraction"] = TargetFraction(x), ["members"] = x.MemberRoster.TotalManCount,
                    ["nativeCapacity"] = x.Party.PartySizeLimit, ["recruitmentRoom"] = RecruitmentRoom(x) })) };
    }
}
