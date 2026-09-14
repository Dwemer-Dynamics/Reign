using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private JObject CourtLifeCustody(Hero hero)
            => new JObject { ["heroId"] = hero?.StringId, ["exists"] = hero != null,
                ["name"] = hero?.Name?.ToString(), ["age"] = hero?.Age, ["alive"] = hero?.IsAlive,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId, ["gold"] = hero?.Gold,
                ["isPrisoner"] = hero?.IsPrisoner, ["settlementId"] = hero?.CurrentSettlement?.StringId,
                ["partyId"] = hero?.PartyBelongedTo?.StringId,
                ["custodianHeroId"] = hero?.PartyBelongedToAsPrisoner?.Owner?.StringId,
                ["heldByPlayer"] = hero != null && hero.IsPrisoner && hero.PartyBelongedToAsPrisoner?.Owner == Hero.MainHero };

        private bool IsCourtLifeCaptiveFixtureCandidate(Hero hero)
            => CourtLifeCaptiveExclusion(hero) == string.Empty;

        private string CourtLifeCaptiveExclusion(Hero hero)
        {
            Kingdom ours = Clan.PlayerClan?.Kingdom, origin = hero?.Clan?.Kingdom;
            if (ours == null || origin == null || origin == ours) return "not_foreign";
            if (!IsEligibleNobleVisitor(hero, ours)) return "native_status_or_assignment";
            if (!InternationalOriginEligible(origin, ours)) return "origin_or_envoy_ineligible";
            if (FindInternationalCaptive(origin) != null) return "existing_player_captive_use_production_template";
            var state = EnsureRulerDocketState();
            if (state.CourtLifeMatters.Any(m => m.IsPending && m.Participants.Any(p => p.HeroId == hero.StringId)))
                return "participant_in_pending_matter";
            // Capturing the fixture must leave an actual eligible envoy available.
            return _foreignAmbassadors.Any(a => a.IsActive && a.OriginKingdomStringId == origin.StringId)
                || EligibleForeignAmbassadorCandidates(origin).Any(h => h != hero) ? string.Empty : "no_remaining_envoy";
        }

        private JObject CourtLifeCaptiveInventory()
        {
            var inventory = Hero.AllAliveHeroes.Select(h => new { Hero = h, Exclusion = CourtLifeCaptiveExclusion(h) }).ToList();
            var candidates = inventory.Where(x => x.Exclusion == string.Empty).Select(x => x.Hero).OrderBy(h => h.StringId).ToList();
            return new JObject { ["schema"] = "reign-court-life-custody-v1", ["readOnly"] = true,
                ["candidateCount"] = candidates.Count, ["truncated"] = candidates.Count > 64,
                ["candidates"] = new JArray(candidates.Take(64).Select(CourtLifeCustody)),
                ["exclusionCounts"] = new JObject(inventory.Where(x => x.Exclusion != string.Empty).GroupBy(x => x.Exclusion)
                    .OrderBy(x => x.Key).Select(x => new JProperty(x.Key, x.Count()))),
                ["existingPlayerHeldCaptives"] = new JArray(Hero.AllAliveHeroes.Where(h => h.IsPrisoner
                    && h.PartyBelongedToAsPrisoner?.Owner == Hero.MainHero).OrderBy(h => h.StringId).Take(64).Select(CourtLifeCustody)),
                ["mainPartyOwnedByPlayer"] = PartyBase.MainParty?.Owner == Hero.MainHero };
        }

        private ReignCourtLifeMatter PrepareCourtLifeCaptiveMatter(string runId, string heroId, string templateId,
            int slot, string save, string instance, out JObject receipt, out string reason)
        {
            receipt = null; reason = string.Empty;
            ReignInternationalTemplate template = ReignInternationalDocketCatalog.Find(templateId);
            Hero hero = FindHero(heroId);
            if (template?.RequiresCaptive != true || !IsCourtLifeCaptiveFixtureCandidate(hero)
                || PartyBase.MainParty == null || PartyBase.MainParty.Owner != Hero.MainHero)
            { reason = "Exact eligible captive candidate, captive template and player-owned native main party are required."; return null; }
            if (FindRulerDocketMarker(runId) != null)
            { reason = "This fixture ID already has preparation evidence. Inspect it and restore the guarded pre-fixture checkpoint before retrying a partial capture."; return null; }
            Kingdom origin = hero.Clan.Kingdom;
            if (template.Id.Contains("ransom") && origin.Leader.Gold < template.Gold)
            { reason = "The actual foreign ruler cannot fund this ransom."; return null; }
            receipt = new JObject { ["schema"] = "reign-court-life-custody-v1", ["heroId"] = heroId,
                ["before"] = CourtLifeCustody(hero), ["status"] = "capture_started",
                ["requiresCheckpointRollback"] = true, ["saveName"] = save };
            // Record intent before the native action: callback failure must never permit a blind second capture.
            var marker = new JObject { ["runId"] = runId, ["preparedGameInstance"] = instance, ["captiveFixture"] = receipt };
            StoreRulerDocketMarker(marker); StateChanged?.Invoke();
            try
            {
                TakePrisonerAction.Apply(PartyBase.MainParty, hero);
                receipt["after"] = CourtLifeCustody(hero);
                if (FindInternationalCaptive(origin) != hero || !InternationalOriginEligible(origin, Clan.PlayerClan.Kingdom)
                    || !InternationalTemplateEligible(template, origin, Clan.PlayerClan.Kingdom))
                    throw new InvalidOperationException("Native capture did not satisfy the unchanged production template prerequisites.");
                ReignCourtLifeMatter matter = BuildInternationalMatter(template, origin, Session.CampaignId, Session.TimelineId, CurrentDay(), slot, null);
                if (matter == null || ParseObject(matter.PayloadJson).Value<string>("captiveId") != heroId)
                    throw new InvalidOperationException("The production matter did not bind the exact fixture captive.");
                receipt["status"] = "captured_and_bound";
                marker["courtLifeMatterId"] = matter.MatterId;
                StoreRulerDocketMarker(marker); StateChanged?.Invoke();
                return matter;
            }
            catch (Exception ex)
            {
                receipt["after"] = CourtLifeCustody(hero); receipt["status"] = "partial_failure";
                receipt["error"] = ex.Message; StoreRulerDocketMarker(marker); StateChanged?.Invoke();
                reason = "Captive setup failed; inspect its persisted custody receipt and restore the guarded pre-fixture checkpoint before retry: " + ex.Message;
                return null;
            }
        }

        private JObject CourtLifeInternationalNativeState(ReignCourtLifeMatter matter)
        {
            JObject payload = ParseObject(matter.PayloadJson);
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            Kingdom origin = Kingdom.All.FirstOrDefault(k => k.StringId == matter.KingdomId);
            var actions = ReignAICampaignBehavior.Instance?.Actions
                .Where(x => x.ActionId == matter.MatterId + "_native").ToList();
            var action = actions?.FirstOrDefault();
            return new JObject { ["schema"] = "reign-court-life-custody-v1",
                ["captive"] = CourtLifeCustody(FindHero(payload.Value<string>("captiveId"))),
                ["foreignRuler"] = CourtLifeCustody(FindHero(payload.Value<string>("foreignRulerId"))),
                ["playerKingdomId"] = ours?.StringId, ["foreignKingdomId"] = origin?.StringId,
                ["nativeDiplomaticAction"] = new JObject { ["serviceAvailable"] = actions != null,
                    ["count"] = actions?.Count ?? 0, ["actionId"] = action?.ActionId,
                    ["status"] = action?.Status.ToString(), ["type"] = action?.Type.ToString(),
                    ["terms"] = ParseObject(action?.TermsJson),
                    ["executionSnapshot"] = ParseObject(action?.ExecutionSnapshotJson) },
                ["warStateKnown"] = ours != null && origin != null,
                ["atWar"] = ours != null && origin != null ? (bool?)ours.IsAtWarWith(origin) : null };
        }
    }
}
