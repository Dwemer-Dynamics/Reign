using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Campaign;
using ReignBeta.Government;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using ReignBeta.World;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Actions
{
    public static class ReignActionService
    {
        public static string Arg(ActionClause clause, string key, string fallback = "") => clause != null && clause.Arguments.TryGetValue(key, out var value) ? value : fallback;
        public static Hero HeroById(string id) => Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);
        public static double Now => CampaignTime.Now.ToDays;
        public static ActionAgreement Copy(ActionAgreement agreement) => JsonConvert.DeserializeObject<ActionAgreement>(JsonConvert.SerializeObject(agreement));

        // Read-only menu eligibility. Parameter-dependent checks still run at
        // preview and commit; browsing the composer never reserves or executes.
        public static bool CanBuild(ActionDefinition definition, ActionAgreement agreement, bool playerActs)
        {
            if (!ReignActionConversationPolicy.Allows(definition, playerActs, agreement.Clauses.Count)) return false;
            Hero actor = HeroById(playerActs ? agreement.PlayerId : agreement.NpcId);
            Hero counterpart = HeroById(playerActs ? agreement.NpcId : agreement.PlayerId);
            if (actor == null || counterpart == null || actor == counterpart || actor.IsDead || counterpart.IsDead || actor.IsChild || counterpart.IsChild) return false;
            if (definition.Id == ReignSpouseSeparation.ActionId) return ReignSpouseSeparationAction.Available(actor);
            if (definition.Adapter.StartsWith("npc_life:", StringComparison.Ordinal))
                return ReignNpcLivesBehavior.Instance?.CanBuildLifeAction(definition.Id, actor, counterpart) == true;
            if (definition.Id == "expose_concealed_pregnancy")
                return ReignFamilyCampaignBehavior.Instance?.PregnancyExposureOptions(counterpart.StringId).Count > 0;
            if (definition.Adapter.StartsWith("asset:", StringComparison.Ordinal))
                return definition.Id == "give.gold" ? actor.Gold > 0
                    : ReignActionEntities.Find(definition.Id.Substring(5), actor.StringId, clause: new ActionClause { ActionId = definition.Id, ActorId = actor.StringId, RecipientId = counterpart.StringId }).Any(r => r.Available);
            if (definition.Adapter.StartsWith("government:", StringComparison.Ordinal))
                return ReignGovernmentCampaignBehavior.Instance != null && (actor.Clan?.Kingdom != null
                    || ReignGovernmentCampaignBehavior.Instance.Seats.Any(s => s.HeroStringId == actor.StringId)
                    || definition.Id == "government.endorse_petition" && actor.IsNotable);
            if (definition.Family == "court")
            {
#if !REIGN_EXCLUDE_COURT
                var court = ReignCourtCampaignBehavior.Instance;
                if (court == null || !actor.IsActive || actor.IsPrisoner) return false;
                if (definition.Id.StartsWith("spymaster.", StringComparison.Ordinal))
                    return court.IsRuleModeActive && court.ActiveSpymaster == actor
                        && court.ActiveSpymasterMissionCount < court.SpymasterMissionCapacity;
                var courtClause = new ActionClause { ActionId = definition.Id, ActorId = actor.StringId, RecipientId = counterpart.StringId };
                // Use the same native eligibility as preview, including actual office
                // holders and court scope. Test only required selector alternatives;
                // these checks do not appoint, dismiss, dispatch or reserve anything.
                if (definition.Id == "court.serve_office")
                    return definition.Parameters.First(p => p.Key == "office").Choices.Any(office =>
                    { courtClause.Arguments["office"] = office; return court.CheckCourtAgreement(courtClause).Count == 0; });
                if (definition.Id == "court.serve_ambassador")
                    return Kingdom.All.Where(k => !k.IsEliminated && k != Clan.PlayerClan?.Kingdom).Any(kingdom =>
                    { courtClause.Arguments["targetKingdomId"] = kingdom.StringId; return court.CheckCourtAgreement(courtClause).Count == 0; });
                return court.CheckCourtAgreement(courtClause).Count == 0;
#else
                return false;
#endif
            }
            if (definition.Family == "diplomacy")
            {
                if (actor.Clan?.Kingdom?.Leader != actor) return false;
                if (!new[] { "declare_war", "break_treaty", "record_promise", "back_rebellion" }.Contains(definition.Id)
                    && counterpart.Clan?.Kingdom?.Leader != counterpart) return false;
            }
            if (definition.NativeObjective.Length > 0)
            {
                if (actor.IsPrisoner) return false;
                var party = actor.PartyBelongedTo;
                if (definition.NativeObjective == "establish_party") return party == null;
                if (party == null || !party.IsActive || party.LeaderHero != actor) return false;
            }
            if (definition.Id == "start_ruling_clan_rebellion")
                return actor.Clan?.Leader == actor && actor.Clan?.Kingdom?.Leader == counterpart && actor != counterpart;
            if (definition.Id == "pledge_rebellion_support")
                return actor.Clan?.Leader == actor && counterpart.Clan?.Leader == counterpart
                    && actor.Clan != counterpart.Clan && counterpart.Clan?.Kingdom != null
                    && counterpart.Clan.Kingdom.Leader != counterpart && actor.Clan?.Kingdom == counterpart.Clan.Kingdom;
            if (definition.Id == "recruit_encountered_resident")
            {
                var recruit = new ActionClause { ActionId = definition.Id, ActorId = actor.StringId, RecipientId = counterpart.StringId,
                    Arguments = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["terms.agreedGold"] = "0" } };
                return ReignActionValidator.ValidateProposal(WorldRecord(agreement, recruit), out _);
            }
            if (new[] { "renew_temporary_party_guest", "end_temporary_party_guest", "acknowledge_own_faction_combat_risk" }.Contains(definition.Id))
            {
                var guest = ReignBeta.PartyAgency.ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records.FirstOrDefault(record =>
                    record.IsLive && string.Equals(record.HeroStringId, actor.StringId, StringComparison.OrdinalIgnoreCase));
                if (guest == null) return false;
                if (definition.Id == "renew_temporary_party_guest") return actor.PartyBelongedTo == TaleWorlds.CampaignSystem.Party.MobileParty.MainParty;
                if (definition.Id == "acknowledge_own_faction_combat_risk") return !string.IsNullOrWhiteSpace(guest.HostileFactionStringId);
                return true;
            }
            return true;
        }

        public static bool CanSelect(ActionClause clause, ActionParameter parameter, string value)
        {
            if (clause.ActionId == "expose_concealed_pregnancy" && parameter.Key == "disclosure")
                return ReignFamilyCampaignBehavior.Instance?.CheckPregnancyExposure(Arg(clause, "pregnancyId"), clause.RecipientId, value == "name_father") == "";
            Hero actor = HeroById(clause.ActorId), counterpart = HeroById(clause.RecipientId);
            if (clause.ActionId == "npc_life.summon_meeting")
            {
                if (parameter.Key == "targetHeroId") return ReignNpcLivesBehavior.CanSummon(actor, HeroById(value));
                if (parameter.Key == "destinationId") return ReignNpcLivesBehavior.Instance?.MeetingPlaceOptions(clause).Any(o => o.Id == value && o.Available) == true;
            }
            if (!ReignActionConversationPolicy.AllowsParticipantSelection(clause.ActionId, parameter.Key, value, clause.ActorId, clause.RecipientId)) return false;
            if (parameter.Kind == "hero" && parameter.Key != "terms.wageRecipientHeroStringId")
            {
                if (clause.ActionId == "kill_character" && value == Hero.MainHero?.StringId) return false;
                if (clause.ActionId.StartsWith("spymaster.mitigate_own", StringComparison.Ordinal) && value != Hero.MainHero?.StringId) return false;
                if (clause.ActionId == "spymaster.assassinate_person")
                {
                    var victim = HeroById(value);
                    if (victim == null || victim == Hero.MainHero || victim.IsChild || victim.IsNotable && victim.Issue != null) return false;
                }
            }
            if (parameter.Kind == "party" && (parameter.Key == "targetPartyId" || parameter.Key == "terms.targetPartyId"))
                if (actor?.PartyBelongedTo?.StringId == value) return false;
            if (parameter.Kind == "settlement")
            {
                var settlement = TaleWorlds.CampaignSystem.Settlements.Settlement.Find(value);
                string objective = ReignActionCatalog.Find(clause.ActionId)?.NativeObjective ?? "";
                if (objective == "raid" && settlement?.IsVillage != true) return false;
                if (objective == "besiege_capture" && clause.ActionId != "attack_settlement" && settlement?.IsFortification != true) return false;
            }
            if (parameter.Key == "actorKingdomId" && actor?.Clan?.Kingdom?.StringId != value) return false;
            if (parameter.Key == "actorClanId" && new[] { "join_kingdom", "leave_kingdom", "hire_mercenary_clan", "join_rebellion", "recruit_lord_to_rebellion" }.Contains(clause.ActionId)
                && actor?.Clan?.StringId != value) return false;
            if (parameter.Key == "targetKingdomId" && new[] { "join_kingdom", "hire_mercenary_clan", "hire_player_as_mercenary", "offer_player_vassalage" }.Contains(clause.ActionId))
            {
                var ruler = Kingdom.All.FirstOrDefault(k => k.StringId == value)?.Leader;
                if (ruler != actor && ruler != counterpart) return false;
            }
            if (parameter.Key == "targetKingdomId" && (ReignActionCatalog.Find(clause.ActionId)?.Family == "diplomacy"
                || clause.ActionId == "court.serve_ambassador") && actor?.Clan?.Kingdom?.StringId == value) return false;
            if (parameter.Key == "targetClanId" && clause.ActionId == "join_clan"
                && (counterpart?.Clan?.Leader != counterpart || counterpart?.Clan?.StringId != value || actor?.Clan?.StringId == value)) return false;
            return true;
        }

        public static List<string> Preview(ActionAgreement agreement)
        {
            ReignNpcLivesBehavior.Instance?.BindMeetingArrival(agreement);
            var errors = AgreementRules.Validate(agreement, ReignActionCatalog.Find, Now);
            if (errors.Count > 0) return errors;
            try { BindGuestReviews(agreement); }
            catch (ArgumentException ex) { return new List<string> { ex.Message }; }
            if (HeroById(agreement.PlayerId) == null || HeroById(agreement.NpcId) == null) errors.Add("Both participants must be alive and available.");
            foreach (var c in agreement.Clauses)
            {
                var d = ReignActionCatalog.Find(c.ActionId); if (d == null) continue;
                if (c.ActionId == ReignSpouseSeparation.ActionId) errors.AddRange(ReignSpouseSeparationAction.Check(agreement, c));
                else if (c.ActionId == "expose_concealed_pregnancy")
                {
                    string error = ReignFamilyCampaignBehavior.Instance?.CheckPregnancyExposure(Arg(c, "pregnancyId"), c.RecipientId, Arg(c, "disclosure") == "name_father") ?? "Family services are unavailable.";
                    if (error.Length > 0) errors.Add(error);
                }
                else if (d.Adapter.StartsWith("npc_life:", StringComparison.Ordinal))
                {
                    if (ReignNpcLivesBehavior.Instance == null) errors.Add("NPC Lives is unavailable.");
                    else errors.AddRange(ReignNpcLivesBehavior.Instance.CheckLifeAction(agreement, c));
                }
                else if (d.Adapter.StartsWith("asset:", StringComparison.Ordinal))
                {
                    try { ReignAssetTransfer.Check(agreement, c); } catch (Exception ex) { errors.Add(ex.Message); }
                }
                else if (d.Adapter.StartsWith("government:", StringComparison.Ordinal))
                {
                    if (ReignGovernmentCampaignBehavior.Instance == null) errors.Add("Government is unavailable.");
                    else errors.AddRange(ReignGovernmentCampaignBehavior.Instance.CheckAgreementClause(c));
                }
                else if (d.Adapter.StartsWith("court:", StringComparison.Ordinal))
                {
#if !REIGN_EXCLUDE_COURT
                    if (ReignCourtCampaignBehavior.Instance == null) errors.Add("Court services are unavailable.");
                    else errors.AddRange(ReignCourtCampaignBehavior.Instance.CheckCourtAgreement(c));
#else
                    errors.Add("This build does not include court services.");
#endif
                }
                else
                {
                    var record = WorldRecord(agreement, c);
                    if (!ReignActionValidator.ValidateProposal(record, out var reason)) errors.Add(reason);
                }
            }
            errors.AddRange(ReignAssetTransfer.CheckAggregate(agreement));
#if !REIGN_EXCLUDE_COURT
            if (ReignCourtCampaignBehavior.Instance != null) errors.AddRange(ReignCourtCampaignBehavior.Instance.CheckCourtAgreementPackage(agreement));
#endif
            if (ReignAgreementBehavior.Instance != null) errors.AddRange(ReignAgreementBehavior.Instance.CheckAssetReservations(agreement));
            if (errors.Count == 0 && agreement.Clauses.Any(c => c.ActionId == "government.support_nomination" || c.ActionId == "government.support_vote"))
            {
                if (ReignAgreementBehavior.Instance == null || ReignGovernmentCampaignBehavior.Instance == null)
                    errors.Add("The vote bargain's delivery timing is unavailable.");
                else
                    errors.AddRange(ReignGovernmentCampaignBehavior.Instance.CheckVoteAgreementTiming(agreement,
                        ReignAgreementBehavior.Instance.DeliveryDays(agreement)));
            }
            return errors.Distinct().ToList();
        }

        internal static void BindGuestReviews(ActionAgreement agreement)
        {
            if (agreement?.Clauses == null) return;
            foreach (var clause in agreement.Clauses.Where(c => c != null
                && (c.ActionId == "renew_temporary_party_guest" || c.ActionId == "end_temporary_party_guest")))
            {
                var context = ReignBeta.PartyAgency.ReignTemporaryPartyGuestCampaignBehavior.Instance
                    ?.BuildConversationContext(HeroById(clause.ActorId));
                AgreementRules.BindGuestReview(agreement, clause, context?.Value<string>("agreementId") ?? "",
                    context?.Value<double?>("reviewDueDay") ?? double.NaN,
                    context?.Value<int?>("agreementRevision") ?? -1);
            }
        }

        internal static ReignWorldActionRecord WorldRecord(ActionAgreement agreement, ActionClause c, int installment = 0)
        {
            var definition = ReignActionCatalog.Find(c.ActionId) ?? throw new InvalidOperationException("Unknown action.");
            if (!Enum.TryParse(definition.NativeObjective.Length > 0 ? "RegularIssueCampaignOrder" : definition.Adapter, out ReignWorldActionType type)) throw new InvalidOperationException("This action has no world adapter.");
            var actor = HeroById(c.ActorId); var target = HeroById(c.RecipientId);
            // The sentence's actor is the consenting lord. The existing preparation
            // adapter records that lord's decision under the player's conspiracy.
            bool rebellionPledge = c.ActionId == "pledge_rebellion_support";
            if (rebellionPledge) { actor = HeroById(agreement.PlayerId); target = HeroById(agreement.NpcId); }
            var terms = new JObject();
            foreach (var kv in AgreementRules.WorldTerms(definition, c.Arguments))
                terms[kv.Key] = JToken.FromObject(kv.Value);
            if (definition.NativeObjective.Length > 0) terms["objective"] = ReignActionCatalog.ObjectiveFor(definition,
                TaleWorlds.CampaignSystem.Settlements.Settlement.Find(Arg(c, "targetSettlementId", Arg(c, "TargetSettlement")))?.IsVillage == true);
            if (type == ReignWorldActionType.PoliticsResolveCivilWar)
            {
                terms["politicalResult"] = "recognized_independence";
                terms["rebelKingdomId"] = Arg(c, "actorKingdomId", actor?.Clan?.Kingdom?.StringId ?? "");
                terms["parentKingdomId"] = Arg(c, "targetKingdomId", target?.Clan?.Kingdom?.StringId ?? "");
            }
            if (c.DurationDays > 0 && definition.DurationParameter.Length > 0)
                terms[definition.DurationParameter.Replace("terms.", "")] = c.DurationDays * (definition.DurationParameter.EndsWith("Hours", StringComparison.Ordinal) ? 24 : 1);
            if (rebellionPledge) { terms["decision"] = "pledge"; terms["requestChannel"] = "structured_action"; }
            terms["fromHeroStringId"] = actor?.StringId ?? c.ActorId; terms["toHeroStringId"] = target?.StringId ?? c.RecipientId;
            terms["consentConfirmed"] = agreement.HasBothConsents; terms["playerConfirmed"] = agreement.HasBothConsents;
            if (type == ReignWorldActionType.RegularReleaseResidentFromDuty) terms["releaseConfirmed"] = agreement.HasBothConsents;
            if (type == ReignWorldActionType.RegularRecruitEncounteredResident) terms["agreementKind"] = "permanent";
            if (type == ReignWorldActionType.RegularCreateClanAccord) terms["partnerClanId"] = actor?.Clan?.StringId ?? "";
            // The native mercenary adapter selects TargetClan first. Bind it to the
            // explicitly joining clan, not the ruler's default counterpart clan.
            string actorClanId = Arg(c, "actorClanId", actor?.Clan?.StringId ?? "");
            string targetClanId = type == ReignWorldActionType.RegularHireMercenaryClan ? actorClanId
                : Arg(c, "targetClanId", Arg(c, "TargetClan", target?.Clan?.StringId ?? ""));
            if (type == ReignWorldActionType.PoliticsMarriageAlliance && agreement.Origin != ActionOrigin.Console)
            { terms["marriageHero1StringId"] = c.ActorId; terms["marriageHero2StringId"] = c.RecipientId; }
            return new ReignWorldActionRecord
            {
                ActionId = WorldActionExecutionIdentity.Create(agreement.Id, c.Id, installment),
                Type = type, Source = agreement.Origin == ActionOrigin.Console ? "reign_console" : "structured_agreement",
                AuthorizationMode = agreement.Origin == ActionOrigin.Console ? "operator" : "explicit_agreement",
                NegotiationId = agreement.Id, TermsHash = agreement.TermsHash(), NegotiatedCommand = c.ActionId,
                ActorHeroStringId = actor?.StringId ?? c.ActorId, TargetHeroStringId = Arg(c, "TargetHero", Arg(c, "targetHeroId", target?.StringId ?? c.RecipientId)),
                ActorClanStringId = actorClanId, TargetClanStringId = targetClanId,
                ActorKingdomStringId = Arg(c, "actorKingdomId", actor?.Clan?.Kingdom?.StringId ?? ""), TargetKingdomStringId = Arg(c, "targetKingdomId", Arg(c, "TargetKingdom", target?.Clan?.Kingdom?.StringId ?? "")),
                TargetSettlementStringId = Arg(c, "targetSettlementId", Arg(c, "TargetSettlement")), Reason = c.DisplayText,
                TermsJson = terms.ToString(Formatting.None), RequiresAcceptance = false,
                AcceptedByHeroStringId = agreement.HasBothConsents ? agreement.NpcId : "", AcceptedDay = (float)Now,
                ExecuteAfterDay = (float)Math.Max(Now, c.DueDay), CreatedDay = (float)Now,
                MinimumTroops = int.TryParse(Arg(c, "minimumTroops"), out int troops) ? troops : 0,
                DesiredStrength = int.TryParse(Arg(c, "desiredStrength"), out int strength) ? strength : 0
            };
        }

        public static ActionReceipt RunOperator(ActionAgreement agreement)
        {
            if (ReignAgreementBehavior.Instance == null) throw new InvalidOperationException("Load a campaign first.");
            agreement = Copy(agreement); agreement.Origin = ActionOrigin.Console; agreement.State = AgreementState.Accepted;
            agreement.PlayerAcceptedHash = ""; agreement.NpcAcceptedHash = "";
            BindGuestReviews(agreement);
            return ReignAgreementBehavior.Instance.Commit(agreement, true);
        }

        public static ActionReceipt AcceptNpcDecision(ActionAgreement offered, string decision, string reviewedHash)
        {
            var agreement = Copy(offered);
            if (decision != "accept") throw new InvalidOperationException("The NPC did not accept this proposal.");
            if (agreement.Origin != ActionOrigin.Conversation || !AgreementRules.Accept(agreement, agreement.NpcId, reviewedHash))
                throw new InvalidOperationException("The NPC accepted a different revision.");
            return ReignAgreementBehavior.Instance.Commit(agreement, false);
        }
    }
}
