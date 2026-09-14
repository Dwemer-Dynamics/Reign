using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Government;
using ReignBeta.Settings;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Court
{
    public sealed class ForeignKingdomAmbassadorEligibility
    {
        public Kingdom Kingdom;
        public bool Eligible;
        public string Reason = string.Empty;
        public int RelationWithRuler;
        public int CandidateCount;
    }

    public sealed partial class ReignCourtCampaignBehavior
    {
        private int _lastForeignReferralTickDay = -1;
        private readonly HashSet<string> _presentedAmbassadorReferralIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool DesignateCapitalCondition(MenuCallbackArgs args)
        {
            Settlement settlement = CurrentSettlement();
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            bool eligible = IsCapitalDesignationEligible(
                    ReignBetaSettings.IsCourtSystemAvailable,
                    IsPlayerKingdomRuler(), settlement?.IsTown == true,
                    settlement?.IsUnderSiege == true, kingdom != null
                    && settlement?.OwnerClan?.Kingdom == kingdom)
                && !string.Equals(CurrentCapitalDesignation()?.SettlementStringId, settlement.StringId, StringComparison.OrdinalIgnoreCase);
            args.optionLeaveType = GameMenuOption.LeaveType.Manage;
            args.IsEnabled = eligible;
            return eligible;
        }

        private void DesignateCapitalConsequence(MenuCallbackArgs args)
        {
            Settlement settlement = CurrentSettlement();
            KingdomCapitalDesignation record = CurrentCapitalDesignation();
            if (record?.EverDesignated != true)
            {
                DesignateCapital(settlement);
                return;
            }

            InformationManager.ShowInquiry(new InquiryData(
                "Move the Capital",
                "Designate " + (settlement?.Name?.ToString() ?? "this town")
                    + " as the new capital? Resident ambassadors will relocate immediately. This has no cost or cooldown.",
                true, true, "Designate Capital", "Cancel",
                () => DesignateCapital(settlement), null), true);
        }

        private void DesignateCapital(Settlement settlement)
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (!IsCapitalDesignationEligible(
                    ReignBetaSettings.IsCourtSystemAvailable,
                    IsPlayerKingdomRuler(), settlement?.IsTown == true,
                    settlement?.IsUnderSiege == true, kingdom != null
                    && settlement?.OwnerClan?.Kingdom == kingdom))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] A capital must be a safe town belonging to the kingdom you rule."));
                return;
            }

            KingdomCapitalDesignation record = CurrentCapitalDesignation();
            bool first = record?.EverDesignated != true;
            if (record == null)
            {
                record = new KingdomCapitalDesignation { KingdomStringId = kingdom.StringId };
                _capitalDesignations.Add(record);
            }
            ApplyCapitalDesignation(record, kingdom.StringId, settlement.StringId, CurrentDayFloat());

            if (HasOpenSession)
            {
                _session.Scope = ResolveCourtScope(settlement.StringId, _session.HostSettlementStringId);
                if (_session != null) _session.Revision++;
            }
            RelocateForeignAmbassadors(settlement, false);
            if (HasOpenSession) _ = TickServerSessionAsync();
            StateChanged?.Invoke();
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] "
                + settlement.Name + (first ? " is now your kingdom's first capital. Rule Mode is available." : " is now your kingdom's capital.")));
        }

        private KingdomCapitalDesignation CurrentCapitalDesignation()
        {
            string kingdomId = Clan.PlayerClan?.Kingdom?.StringId;
            return string.IsNullOrWhiteSpace(kingdomId) ? null : _capitalDesignations
                .FirstOrDefault(x => string.Equals(x.KingdomStringId, kingdomId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsCapitalDesignationEligible(bool courtEnabled, bool playerRuler,
            bool isTown, bool underSiege, bool ownedByPlayerKingdom)
        {
            return courtEnabled && playerRuler && isTown && !underSiege && ownedByPlayerKingdom;
        }

        private static void ApplyCapitalDesignation(KingdomCapitalDesignation record,
            string kingdomId, string settlementId, float day)
        {
            if (record == null) return;
            record.KingdomStringId = kingdomId ?? string.Empty;
            record.SettlementStringId = settlementId ?? string.Empty;
            record.EverDesignated = true;
            record.DesignatedDay = day;
            record.ClearedDay = -1f;
            record.Revision++;
        }

        private static void ClearCapitalDesignation(KingdomCapitalDesignation record, float day)
        {
            if (record == null) return;
            record.SettlementStringId = string.Empty;
            record.ClearedDay = day;
            record.Revision++;
        }

        private static ReignCourtScope ResolveCourtScope(string capitalSettlementId, string hostSettlementId)
        {
            return !string.IsNullOrWhiteSpace(capitalSettlementId)
                && string.Equals(capitalSettlementId, hostSettlementId, StringComparison.OrdinalIgnoreCase)
                ? ReignCourtScope.Capital : ReignCourtScope.Local;
        }

        private Settlement FindCurrentCapital()
        {
            KingdomCapitalDesignation record = CurrentCapitalDesignation();
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (record == null || string.IsNullOrWhiteSpace(record.SettlementStringId) || kingdom == null) return null;
            return Settlement.All.FirstOrDefault(x => x?.IsTown == true
                && string.Equals(x.StringId, record.SettlementStringId, StringComparison.OrdinalIgnoreCase)
                && x.OwnerClan?.Kingdom == kingdom);
        }

        public List<ForeignKingdomAmbassadorEligibility> GetForeignAmbassadorKingdoms()
        {
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            return Kingdom.All.Where(x => x != null && x != ours && !x.IsEliminated)
                .OrderBy(x => x.Name?.ToString() ?? x.StringId)
                .Select(x => EvaluateForeignAmbassadorEligibility(x)).ToList();
        }

        private ForeignKingdomAmbassadorEligibility EvaluateForeignAmbassadorEligibility(Kingdom origin, bool eventGenerated = false)
        {
            ForeignKingdomAmbassadorEligibility result = new ForeignKingdomAmbassadorEligibility { Kingdom = origin };
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            Hero ruler = origin?.Leader;
            if (!eventGenerated && !HasRoyalCommandAccess) result.Reason = "Royal ambassador commands are available only in the capital.";
            else if (eventGenerated && (ours?.Leader != Hero.MainHero || CurrentCapital == null)) result.Reason = "An international visit requires a ruling player and a designated capital.";
            else if (ours == null || origin == null || origin == ours || origin.IsEliminated) result.Reason = "That kingdom is not active.";
            else if (ruler == null || !ruler.IsAlive || !ruler.IsActive) result.Reason = "That kingdom has no available ruler.";
            else if (ours.IsAtWarWith(origin)) result.Reason = "Your kingdoms are at war.";
            else if (_foreignAmbassadors.Any(x => x.IsActive && string.Equals(x.OriginKingdomStringId, origin.StringId, StringComparison.OrdinalIgnoreCase)))
                result.Reason = "That kingdom already has an assigned ambassador.";
            else
            {
                result.RelationWithRuler = Hero.MainHero?.GetRelation(ruler) ?? -100;
                if (!eventGenerated && result.RelationWithRuler < -30) result.Reason = "Relation with " + ruler.Name + " must be -30 or better.";
                else
                {
                    result.CandidateCount = EligibleForeignAmbassadorCandidates(origin).Count;
                    if (result.CandidateCount == 0) result.Reason = "No free lord or lady of that kingdom has at least 50 Charm.";
                    else result.Eligible = true;
                }
            }
            return result;
        }

        public async Task<string> EstablishForeignAmbassadorAsync(Kingdom origin, bool eventGenerated = false)
        {
            ForeignKingdomAmbassadorEligibility eligibility = EvaluateForeignAmbassadorEligibility(origin, eventGenerated);
            if (!eligibility.Eligible) return eligibility.Reason;
            Settlement capital = CurrentCapital;
            if (capital == null) return "Designate a current capital before establishing relations.";
            List<Hero> candidates = EligibleForeignAmbassadorCandidates(origin);
            JArray candidatePayload = new JArray(candidates.Select(hero =>
            {
                Settlement source = AmbassadorOriginSettlement(hero, origin);
                int travelDays = AmbassadorTravelDays(source, capital);
                return new JObject
                {
                    ["heroStringId"] = hero.StringId,
                    ["charm"] = hero.GetSkillValue(DefaultSkills.Charm),
                    ["rulerTrust"] = hero.GetRelation(origin.Leader),
                    ["sourceSettlementStringId"] = source?.StringId ?? string.Empty,
                    ["travelDays"] = travelDays,
                    ["isAdult"] = !hero.IsChild,
                    ["isAlive"] = hero.IsAlive,
                    ["isActive"] = hero.IsActive,
                    ["isPrisoner"] = hero.IsPrisoner,
                    ["isLordOrLady"] = hero.IsLord,
                    ["isRuler"] = hero == origin.Leader,
                    ["isGovernor"] = hero.GovernorOf != null,
                    ["isPartyLeader"] = hero.PartyBelongedTo?.LeaderHero == hero,
                    ["isActiveEnvoy"] = _foreignAmbassadors.Any(x => x?.IsActive == true && string.Equals(x.HeroStringId, hero.StringId, StringComparison.OrdinalIgnoreCase))
                };
            }));
            string commandId = "foreign_ambassador_establish_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.EstablishForeignAmbassadorAsync(
                origin, origin.Leader, capital, eligibility.RelationWithRuler, candidatePayload, commandId, eventGenerated).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            string selectedId = response.Raw.Value<string>("heroStringId") ?? string.Empty;
            Hero selected = candidates.FirstOrDefault(x => string.Equals(x.StringId, selectedId, StringComparison.OrdinalIgnoreCase));
            if (selected == null) return "The foreign ruler's selected ambassador is no longer available.";

            await ReignMainThread.InvokeAsync(() =>
            {
                _foreignAmbassadors.Add(new ForeignAmbassadorPosting
                {
                    PostingId = response.Raw.Value<string>("recordId") ?? "foreign_ambassador_" + Guid.NewGuid().ToString("N"),
                    HeroStringId = selected.StringId,
                    OriginKingdomStringId = origin.StringId,
                    OriginRulerHeroStringId = origin.Leader.StringId,
                    HostKingdomStringId = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                    CapitalSettlementStringId = capital.StringId,
                    Status = "traveling",
                    AssignedDay = CurrentDayFloat(),
                    ArrivalDay = (float)(response.Raw.Value<double?>("arrivalDay") ?? CurrentDayFloat() + 3f),
                    Charm = selected.GetSkillValue(DefaultSkills.Charm),
                    RulerTrust = selected.GetRelation(origin.Leader),
                    AuthorityCharterJson = response.Raw["authorityCharter"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}",
                    AuthorityRevision = response.Raw.Value<long?>("authorityRevision") ?? 1,
                    Revision = response.Revision,
                    LastCommandId = commandId
                });
                if (_session != null) _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> DismissForeignAmbassadorAsync(ForeignAmbassadorPosting posting)
        {
            if (!HasRoyalCommandAccess) return "Ambassadors may be dismissed only from the capital.";
            if (posting?.IsActive != true) return "That ambassador posting is no longer active.";
            string commandId = "foreign_ambassador_dismiss_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.DismissForeignAmbassadorAsync(posting, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() => EndForeignAmbassador(posting, "dismissed", true, commandId, response.Revision)).ConfigureAwait(false);
            return string.Empty;
        }

        public void OpenOfficialAmbassadorConversation(ForeignAmbassadorPosting posting)
        {
            if (!TryBuildOfficialAmbassadorContext(posting, out Hero envoy, out JObject context, out _)) return;
            ReignAmbassadorScreenManager.Close(false);
            ReignIndividualChatScreenManager.OpenForHero(envoy, context, () =>
            {
                if (IsRuleModeActive) ReignAmbassadorScreenManager.Open(this);
            });
        }

        internal bool TryBuildOfficialAmbassadorContext(ForeignAmbassadorPosting posting,
            out Hero envoy, out JObject context, out string error)
        {
            envoy = null;
            context = null;
            error = string.Empty;
            if (!HasRoyalCommandAccess) { error = "Official Ambassador Mode is available only in the capital."; return false; }
            if (posting?.IsResident != true) { error = "The selected ambassador is not resident in the capital."; return false; }
            envoy = FindHero(posting.HeroStringId);
            Kingdom origin = Kingdom.All.FirstOrDefault(x => string.Equals(x?.StringId, posting.OriginKingdomStringId, StringComparison.OrdinalIgnoreCase));
            Hero ruler = origin?.Leader;
            if (envoy == null || ruler == null) { error = "The ambassador or represented ruler is unavailable."; return false; }
            context = new JObject
            {
                ["conversationMode"] = "ambassador_official",
                ["postingId"] = posting.PostingId,
                ["representedKingdomId"] = origin.StringId,
                ["representedKingdomName"] = origin.Name?.ToString() ?? origin.StringId,
                ["representedRulerId"] = ruler.StringId,
                ["representedRulerName"] = ruler.Name?.ToString() ?? ruler.StringId,
                ["hostKingdomId"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                ["hostKingdomName"] = Clan.PlayerClan?.Kingdom?.Name?.ToString() ?? string.Empty,
                ["capitalSettlementId"] = CurrentCapital?.StringId ?? string.Empty,
                ["capitalSettlementName"] = CurrentCapital?.Name?.ToString() ?? string.Empty,
                ["authorityRevision"] = posting.AuthorityRevision,
                ["authorityCharter"] = ParseObject(posting.AuthorityCharterJson),
                ["representedGovernment"] = ReignGovernmentCampaignBehavior.Instance?.BuildPublicSnapshot(origin)
                    ?? new JObject { ["available"] = false, ["authoritative"] = true },
                ["governmentNegotiationRule"] = "You know your kingdom's authoritative government position. Clearly distinguish what the ruler would accept from what the government will approve; do not promise blocked authority."
            };
            return true;
        }

        private void ProcessForeignAmbassadorTick()
        {
            int currentDay = (int)Math.Floor(CurrentDayFloat());
            if (_lastForeignReferralTickDay != currentDay)
            {
                _lastForeignReferralTickDay = currentDay;
                _ = TickForeignAmbassadorReferralsAsync(currentDay);
            }
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            Settlement capital = CurrentCapital;
            foreach (ForeignAmbassadorPosting posting in _foreignAmbassadors.Where(x => x?.IsActive == true).ToList())
            {
                Kingdom origin = Kingdom.All.FirstOrDefault(x => string.Equals(x?.StringId, posting.OriginKingdomStringId, StringComparison.OrdinalIgnoreCase));
                Hero hero = FindForeignAmbassadorHero(posting.HeroStringId);
                string terminalReason = ForeignAmbassadorTerminalReason(origin != null, origin?.IsEliminated == true,
                    hero != null, hero?.IsAlive == true, hero?.IsDisabled == true);
                if (!string.IsNullOrWhiteSpace(terminalReason))
                {
                    ReignLog.Warn("Ending foreign ambassador posting " + posting.PostingId + " for "
                        + posting.HeroStringId + ": " + terminalReason + " (heroState="
                        + (hero == null ? "missing" : hero.HeroState.ToString()) + ").");
                    _ = ReignCourtServerClient.EndForeignAmbassadorAsync(posting, terminalReason);
                    EndForeignAmbassador(posting, "ended", false, "", posting.Revision + 1);
                    continue;
                }
                if (ours != null && ours.IsAtWarWith(origin))
                {
                    _ = ReignCourtServerClient.EndForeignAmbassadorAsync(posting, "war_recalled");
                    EndForeignAmbassador(posting, "war_recalled", true, "", posting.Revision + 1);
                    continue;
                }
                if (origin.Leader != null && !string.Equals(posting.OriginRulerHeroStringId, origin.Leader.StringId, StringComparison.OrdinalIgnoreCase))
                {
                    ReignIndividualChatScreenManager.CloseIfOfficialAmbassador(posting.PostingId);
                    posting.OriginRulerHeroStringId = origin.Leader.StringId;
                    posting.RulerTrust = hero.GetRelation(origin.Leader);
                    posting.AuthorityRevision++;
                    _ = RefreshForeignAmbassadorAuthorityAsync(posting, origin.Leader);
                }
                if (capital == null)
                {
                    ShelterForeignAmbassador(posting, hero);
                    continue;
                }
                if (!string.Equals(posting.CapitalSettlementStringId, capital.StringId, StringComparison.OrdinalIgnoreCase))
                {
                    posting.CapitalSettlementStringId = capital.StringId;
                    posting.ShelterSettlementStringId = string.Empty;
                    if (!string.Equals(posting.Status, "traveling", StringComparison.OrdinalIgnoreCase))
                    {
                        posting.Status = "resident";
                        TeleportPartylessHero(hero, capital);
                    }
                    _ = UpdateForeignAmbassadorLocationAsync(posting, "capital_relocated");
                }
                if (string.Equals(posting.Status, "traveling", StringComparison.OrdinalIgnoreCase) && CurrentDayFloat() >= posting.ArrivalDay)
                {
                    posting.Status = "resident";
                    posting.ShelterSettlementStringId = string.Empty;
                    TeleportPartylessHero(hero, capital);
                    _ = UpdateForeignAmbassadorLocationAsync(posting, "arrived");
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + hero.Name
                        + " has arrived in " + capital.Name + " as ambassador from " + origin.Name + "."));
                }
                else if (posting.IsResident && hero.PartyBelongedTo == null && hero.CurrentSettlement != capital)
                {
                    TeleportPartylessHero(hero, capital);
                }
            }
        }

        private async Task TickForeignAmbassadorReferralsAsync(int dayIndex)
        {
            ReignCourtServerResponse response = await ReignCourtServerClient.TickForeignAmbassadorsAsync(dayIndex).ConfigureAwait(false);
            if (!response.Ok) return;
            await ReignMainThread.InvokeAsync(() =>
            {
                foreach (JObject resolved in (response.Raw["resolvedReferrals"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string status = resolved.Value<string>("status") ?? string.Empty;
                    if (status == "refused")
                        InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] A referred ambassador proposal was refused by the foreign ruler."));
                    else if (status == "accepted_pending_execution")
                        InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] A foreign ruler accepted referred terms; the exact action is awaiting authoritative execution."));
                }
                if (!HasRoyalCommandAccess) return;
                JObject counter = (response.Raw["pendingCounteroffers"] as JArray ?? new JArray()).OfType<JObject>()
                    .FirstOrDefault(x => !_presentedAmbassadorReferralIds.Contains(x.Value<string>("referral_id") ?? string.Empty));
                if (counter == null) return;
                string referralId = counter.Value<string>("referral_id") ?? string.Empty;
                string termsHash = counter.Value<string>("counterTermsHash") ?? string.Empty;
                _presentedAmbassadorReferralIds.Add(referralId);
                string terms = (counter["counterTerms"] as JObject ?? new JObject()).ToString(Newtonsoft.Json.Formatting.Indented);
                InformationManager.ShowInquiry(new InquiryData("Ambassador Counteroffer",
                    "The represented ruler has returned a complete counteroffer. Acceptance is explicit and hash-bound.\n\n" + terms,
                    true, true, "Accept exact terms", "Refuse", () => _ = RespondToAmbassadorCounterofferAsync(referralId, termsHash, true),
                    () => _ = RespondToAmbassadorCounterofferAsync(referralId, termsHash, false)), true);
            }).ConfigureAwait(false);
        }

        private async Task RespondToAmbassadorCounterofferAsync(string referralId, string termsHash, bool accept)
        {
            ReignCourtServerResponse response = await ReignCourtServerClient.RespondToForeignAmbassadorCounterofferAsync(referralId, termsHash, accept).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => InformationManager.DisplayMessage(new InformationMessage(response.Ok
                ? accept ? "[Bannerlord Reign] The counteroffer was accepted and queued for authoritative execution." : "[Bannerlord Reign] The counteroffer was refused."
                : "[Bannerlord Reign] The counteroffer decision failed: " + response.Error))).ConfigureAwait(false);
        }

        private async Task RefreshForeignAmbassadorAuthorityAsync(ForeignAmbassadorPosting posting, Hero ruler)
        {
            ReignCourtServerResponse response = await ReignCourtServerClient.RefreshForeignAmbassadorAuthorityAsync(posting, ruler).ConfigureAwait(false);
            if (!response.Ok) return;
            await ReignMainThread.InvokeAsync(() =>
            {
                posting.AuthorityCharterJson = response.Raw["authorityCharter"]?.ToString(Newtonsoft.Json.Formatting.None) ?? posting.AuthorityCharterJson;
                posting.AuthorityRevision = response.Raw.Value<long?>("authorityRevision") ?? posting.AuthorityRevision;
                posting.Revision = response.Revision;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
        }

        private async Task UpdateForeignAmbassadorLocationAsync(ForeignAmbassadorPosting posting, string reason)
        {
            ReignCourtServerResponse response = await ReignCourtServerClient.UpdateForeignAmbassadorLocationAsync(posting, reason).ConfigureAwait(false);
            if (!response.Ok) return;
            await ReignMainThread.InvokeAsync(() =>
            {
                posting.Revision = response.Revision;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
        }

        private void RelocateForeignAmbassadors(Settlement capital, bool captured)
        {
            foreach (ForeignAmbassadorPosting posting in _foreignAmbassadors.Where(x => x?.IsActive == true))
            {
                posting.CapitalSettlementStringId = capital?.StringId ?? string.Empty;
                posting.ShelterSettlementStringId = string.Empty;
                Hero hero = FindHero(posting.HeroStringId);
                if (capital != null && !string.Equals(posting.Status, "traveling", StringComparison.OrdinalIgnoreCase))
                {
                    posting.Status = "resident";
                    TeleportPartylessHero(hero, capital);
                }
                else if (captured)
                {
                    ShelterForeignAmbassador(posting, hero);
                    continue;
                }
                _ = UpdateForeignAmbassadorLocationAsync(posting, "capital_relocated");
            }
            EnforceResidentAdvisorLocations();
        }

        private void ShelterForeignAmbassador(ForeignAmbassadorPosting posting, Hero hero)
        {
            Settlement shelter = ClosestOwnedTown(hero?.CurrentSettlement, Clan.PlayerClan?.Kingdom);
            if (shelter == null)
            {
                _ = ReignCourtServerClient.EndForeignAmbassadorAsync(posting, "no_safe_host_shelter");
                EndForeignAmbassador(posting, "ended", true, "", posting.Revision + 1);
                return;
            }
            if (string.Equals(posting.Status, "sheltered", StringComparison.OrdinalIgnoreCase)
                && string.Equals(posting.ShelterSettlementStringId, shelter.StringId, StringComparison.OrdinalIgnoreCase))
            {
                TeleportPartylessHero(hero, shelter);
                return;
            }
            posting.Status = "sheltered";
            posting.ShelterSettlementStringId = shelter.StringId;
            TeleportPartylessHero(hero, shelter);
            _ = UpdateForeignAmbassadorLocationAsync(posting, "capital_shelter");
        }

        private void EndForeignAmbassador(ForeignAmbassadorPosting posting, string status, bool returnHome, string commandId, long revision)
        {
            if (posting == null) return;
            posting.Status = status;
            posting.EndReason = status;
            posting.EndedDay = CurrentDayFloat();
            posting.LastCommandId = commandId ?? string.Empty;
            posting.Revision = Math.Max(posting.Revision + 1, revision);
            Hero hero = FindHero(posting.HeroStringId);
            Kingdom origin = Kingdom.All.FirstOrDefault(x => string.Equals(x?.StringId, posting.OriginKingdomStringId, StringComparison.OrdinalIgnoreCase));
            if (returnHome) TeleportPartylessHero(hero, ClosestFriendlyFortification(hero?.CurrentSettlement, origin)
                ?? origin?.Leader?.CurrentSettlement ?? hero?.HomeSettlement ?? origin?.InitialHomeSettlement);
            ReignIndividualChatScreenManager.CloseIfOfficialAmbassador(posting.PostingId);
            StateChanged?.Invoke();
        }

        private void OnCapitalSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner,
            Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            KingdomCapitalDesignation record = CurrentCapitalDesignation();
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            if (record == null || ours == null || !string.Equals(record.SettlementStringId, settlement?.StringId, StringComparison.OrdinalIgnoreCase)
                || settlement.OwnerClan?.Kingdom == ours) return;
            HandleCapitalLoss(record, true);
        }

        private void HandleCapitalLoss(KingdomCapitalDesignation record, bool showMessage)
        {
            ClearCapitalDesignation(record, CurrentDayFloat());
            if (HasOpenSession)
            {
                _session.Scope = ReignCourtScope.Local;
                if (_session != null) _session.Revision++;
            }
            ReignAmbassadorScreenManager.Close(false);
            RelocateForeignAmbassadors(null, true);
            if (HasOpenSession) _ = TickServerSessionAsync();
            if (showMessage) InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Your capital has been lost. Royal commands are suspended until a new capital is designated."));
        }

        private void OnAmbassadorWarDeclared(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            Kingdom a = first as Kingdom;
            Kingdom b = second as Kingdom;
            if (ours == null || a == null || b == null || a != ours && b != ours) return;
            Kingdom foreign = a == ours ? b : a;
            foreach (ForeignAmbassadorPosting posting in _foreignAmbassadors.Where(x => x?.IsActive == true
                && string.Equals(x.OriginKingdomStringId, foreign.StringId, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _ = ReignCourtServerClient.EndForeignAmbassadorAsync(posting, "war_recalled");
                EndForeignAmbassador(posting, "war_recalled", true, "", posting.Revision + 1);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] The ambassador from " + foreign.Name + " has departed because war was declared."));
            }
        }

        private void OnCanForeignAmbassadorLeadParty(Hero hero, ref bool result)
        {
            if (result && hero != null && _foreignAmbassadors.Any(x => x?.IsActive == true
                && string.Equals(x.HeroStringId, hero.StringId, StringComparison.OrdinalIgnoreCase))) result = false;
            if (result && IsResidentAdvisor(hero)) result = false;
        }

        private bool IsResidentAdvisor(Hero hero)
        {
            return hero != null && _offices.Any(assignment => assignment?.IsActive == true
                && (assignment.Office == ReignCourtOffice.EconomicAdvisor || assignment.Office == ReignCourtOffice.ForeignAdvisor)
                && string.Equals(assignment.HeroStringId, hero.StringId, StringComparison.OrdinalIgnoreCase));
        }

        private void EnforceResidentAdvisorLocations()
        {
            Settlement destination = CurrentCapital ?? ClosestOwnedTown(null, Clan.PlayerClan?.Kingdom);
            if (destination == null) return;
            foreach (CourtOfficeAssignment assignment in _offices.Where(item => item?.IsActive == true
                && (item.Office == ReignCourtOffice.EconomicAdvisor || item.Office == ReignCourtOffice.ForeignAdvisor)))
            {
                Hero hero = FindHero(assignment.HeroStringId);
                if (hero == null || hero.IsPrisoner || hero.PartyBelongedTo != null || hero.CurrentSettlement == destination) continue;
                TeleportPartylessHero(hero, destination);
            }
        }

        private List<Hero> EligibleForeignAmbassadorCandidates(Kingdom origin)
        {
            Hero ruler = origin?.Leader;
            float adultAge = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            HashSet<string> assigned = new HashSet<string>(_foreignAmbassadors.Where(x => x?.IsActive == true)
                .Select(x => x.HeroStringId), StringComparer.OrdinalIgnoreCase);
            List<Hero> candidates = (origin?.Clans ?? Enumerable.Empty<Clan>()).SelectMany(x => x?.Heroes ?? Enumerable.Empty<Hero>())
                .Where(hero => hero != null && IsForeignAmbassadorCandidateEligible(hero.StringId,
                    hero == ruler, hero.IsLord, hero.IsAlive, hero.IsActive, hero.IsPrisoner,
                    hero.Age, adultAge, hero.Clan?.Kingdom == origin,
                    hero.PartyBelongedTo?.LeaderHero == hero, hero.GovernorOf != null,
                    hero.GetSkillValue(DefaultSkills.Charm), assigned.Contains(hero.StringId)))
                .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase).Select(x => x.First())
                .OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
            if (candidates.Count == 0) return candidates;
            int highestBand = candidates.Max(x => Reign.Core.Contracts.Court.ReignInternationalDocketRules.AmbassadorCharmBand(x.GetSkillValue(DefaultSkills.Charm)));
            return candidates.Where(x => Reign.Core.Contracts.Court.ReignInternationalDocketRules.AmbassadorCharmBand(x.GetSkillValue(DefaultSkills.Charm)) == highestBand).ToList();
        }

        private static Settlement AmbassadorOriginSettlement(Hero hero, Kingdom origin)
        {
            return hero?.CurrentSettlement ?? hero?.HomeSettlement ?? hero?.Clan?.InitialHomeSettlement ?? origin?.InitialHomeSettlement;
        }

        private static int AmbassadorTravelDays(Settlement source, Settlement capital)
        {
            if (source == null || capital == null) return AmbassadorTravelDaysFromDistance(null);
            float distance = (float)Math.Sqrt(source.GetPosition2D.DistanceSquared(capital.GetPosition2D));
            return AmbassadorTravelDaysFromDistance(distance);
        }

        private static int AmbassadorTravelDaysFromDistance(float? distance)
        {
            return !distance.HasValue ? 3 : distance.Value <= 50f ? 1 : distance.Value <= 100f ? 2 : 3;
        }

        private static bool IsForeignAmbassadorCandidateEligible(string heroId, bool isRuler,
            bool isLordOrLady, bool alive, bool active, bool prisoner, float age, float adultAge,
            bool inOriginKingdom, bool partyLeader, bool governor, int charm, bool assigned)
        {
            return !string.IsNullOrWhiteSpace(heroId) && !isRuler && isLordOrLady && alive && active
                && !prisoner && age >= adultAge && inOriginKingdom && !partyLeader && !governor
                && charm >= 50 && !assigned;
        }

        private static Hero FindForeignAmbassadorHero(string id)
        {
            return string.IsNullOrWhiteSpace(id)
                ? null
                : Hero.FindFirst(hero => string.Equals(hero?.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static string ForeignAmbassadorTerminalReason(bool originExists, bool originEliminated,
            bool envoyExists, bool envoyAlive, bool envoyDisabled)
        {
            if (!originExists) return "origin_kingdom_missing";
            if (originEliminated) return "origin_kingdom_eliminated";
            if (!envoyExists) return "envoy_missing";
            if (!envoyAlive) return "envoy_dead";
            if (envoyDisabled) return "envoy_disabled";
            return string.Empty;
        }

        private static Settlement ClosestOwnedTown(Settlement source, Kingdom kingdom)
        {
            IEnumerable<Settlement> candidates = Settlement.All.Where(x => x?.IsTown == true && !x.IsUnderSiege && x.OwnerClan?.Kingdom == kingdom);
            return source == null ? candidates.FirstOrDefault() : candidates.OrderBy(x => x.GetPosition2D.DistanceSquared(source.GetPosition2D)).FirstOrDefault();
        }

        private static Settlement ClosestFriendlyFortification(Settlement source, Kingdom kingdom)
        {
            Settlement town = ClosestOwnedTown(source, kingdom);
            if (town != null) return town;
            IEnumerable<Settlement> candidates = Settlement.All.Where(x => x != null && (x.IsTown || x.IsCastle)
                && !x.IsUnderSiege && x.OwnerClan?.Kingdom == kingdom);
            return source == null ? candidates.FirstOrDefault() : candidates.OrderBy(x => x.GetPosition2D.DistanceSquared(source.GetPosition2D)).FirstOrDefault();
        }

        private static void TeleportPartylessHero(Hero hero, Settlement target)
        {
            if (hero == null || target == null || hero.IsPrisoner || hero.PartyBelongedTo != null || hero.CurrentSettlement == target) return;
            TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, target);
        }

        private static JObject ParseObject(string json)
        {
            try { return JObject.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
            catch { return new JObject(); }
        }
    }
}
