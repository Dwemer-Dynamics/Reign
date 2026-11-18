using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        public bool TryCounterInternationalTerms(ReignCourtLifeMatter matter, JObject proposedTerms, out string summary)
        {
            summary = string.Empty;
            if (matter == null || matter.Source != ReignDocketSource.International || !matter.IsPending
                || matter.State == ReignCourtLifeMatterState.Deferred || matter.EffectsCommitted || !HasRoyalCommandAccess)
            { summary = "The international audience is not available for revised terms."; return false; }
            JObject data = ParseObject(matter.PayloadJson);
            if (data.Value<bool?>("nativeEffectCommitted") == true || data.Value<bool?>("nativeActionStarted") == true)
            { summary = "The original agreement has already taken effect."; return false; }
            if (data.Value<bool?>("courierDelivery") == true)
            { summary = "This messenger carries a fixed written offer and cannot negotiate revised terms."; return false; }
            if (proposedTerms == null || !proposedTerms.Properties().Any()
                || proposedTerms.Properties().Any(p => p.Name == "payerHeroId" || p.Name == "recipientHeroId"
                    ? p.Value.Type != JTokenType.String : (p.Name != "gold" && p.Name != "durationDays") || p.Value.Type != JTokenType.Integer))
            { summary = "A revised offer must specify whole gold or days, or an identified payment party."; return false; }
            long? gold = proposedTerms.Value<long?>("gold"), days = proposedTerms.Value<long?>("durationDays");
            bool partiesChanged = proposedTerms["payerHeroId"] != null || proposedTerms["recipientHeroId"] != null;
            if (!ReignInternationalDocketRules.CounterTermsWithinBounds(gold ?? (partiesChanged ? (long?)0 : null), days))
            { summary = "An offer must contain between 0 and 1,000,000 gold and a duration of 1 to 365 days."; return false; }
            ReignCourtLifeOption prior = matter.Options.FirstOrDefault(x => x.OptionId == "accept")
                ?? matter.Options.FirstOrDefault(x => x.OptionId == "compromise")
                ?? matter.Options.FirstOrDefault(x => x.OptionId == "compensate");
            if (prior == null) { summary = "There is no executable offer to revise in this audience."; return false; }
            string remedy = data.Value<string>("remedy");
            bool financial = prior.OptionId != "accept" || remedy == "gift" || (remedy == "prisoner" && data.Value<bool?>("ransom") == true);
            bool agreement = remedy == "proposal" || remedy == "sign_trade_agreement";
            if ((gold.HasValue && !financial && !agreement) || (days.HasValue && !agreement) || (partiesChanged && !financial))
            { summary = "Those terms do not apply to the action under discussion."; return false; }
            JObject exact = ParseObject(prior.TermsJson);
            if (gold.HasValue && !financial && exact["gold"] == null)
            { summary = "This agreement has no gold transfer to revise."; return false; }
            if (days.HasValue && remedy != "sign_trade_agreement" && exact["durationDays"] == null)
            { summary = "This agreement has no duration to revise."; return false; }
            foreach (JProperty property in proposedTerms.Properties()) exact[property.Name] = property.Value.DeepClone();
            if (financial && !TryResolveInternationalPayment(data, prior.OptionId, exact, out _, out _, out summary)) return false;
            if (JToken.DeepEquals(exact, ParseObject(prior.TermsJson)))
            { summary = "Those are already the current offered terms."; return true; }
            data["counterProposed"] = true;
            data["sovereignAuthorized"] = false;
            data["counterResolutionOption"] = prior.OptionId == "compensate" ? "compromise" : prior.OptionId;
            data["counterTerms"] = exact.DeepClone();
            data["termsSummary"] = DescribeInternationalTerms(exact);
            if (data["candidate"] is JObject candidate)
            {
                candidate["terms"] = exact.DeepClone();
                data.Remove("normalizedAction");
                data["requiresSovereignReferral"] = true;
            }
            string resolution = data.Value<string>("counterResolutionOption");
            SetInternationalOptions(matter, data, new[] { resolution, "refuse", "refer" });
            ReignCourtLifeOption changed = matter.Options.First(x => x.OptionId == resolution);
            changed.TermsJson = exact.ToString(Formatting.None);
            changed.Label = resolution == "compromise" ? "Offer " + exact.Value<int>("gold") + " gold to settle" : "Accept the revised offer";
            changed.Description = data.Value<string>("termsSummary") + ". The foreign representative must respond to these exact terms.";
            matter.PayloadJson = data.ToString(Formatting.None);
            summary = "Revised terms offered: " + DescribeInternationalTerms(exact) + ". They await a fresh response; no agreement has executed.";
            StateChanged?.Invoke();
            return true;
        }

        public bool ApplyInternationalDecision(ReignCourtLifeMatter matter, JObject decision, out string summary)
        {
            summary = string.Empty;
            if (matter == null || matter.Source != ReignDocketSource.International || !HasRoyalCommandAccess)
            { summary = "This decision requires a royal audience."; return false; }
            if (matter.EffectsCommitted) { summary = matter.DecisionSummary; return true; }
            if (RefreshLegacyInternationalPaymentTerms(matter))
            {
                summary = matter.State == ReignCourtLifeMatterState.Invalidated
                    ? matter.DecisionSummary
                    : "Review the named payer and recipient and obtain a fresh response before confirming this payment.";
                return false;
            }
            JObject data = ParseObject(matter.PayloadJson);
            string option = decision?.Value<string>("optionId") ?? string.Empty;
            ReignCourtLifeOption offered = matter.Options.FirstOrDefault(x => x.OptionId == option);
            if (offered == null || !matter.IsPending) { summary = "That option is no longer available."; return false; }
            Kingdom origin = Kingdom.All.FirstOrDefault(x => x?.StringId == matter.KingdomId);
            if (origin?.Leader == null || origin.Leader.StringId != data.Value<string>("foreignRulerId")
                || (Clan.PlayerClan.Kingdom.IsAtWarWith(origin) && data.Value<bool?>("courierDelivery") != true))
            { summary = "The diplomatic parties have changed; the terms require a fresh audience."; return false; }
            if (!InternationalClaimantsRemainEligible(data, origin))
            {
                summary = "A named claimant has died or changed allegiance; this household dispute requires a new audience.";
                matter.State = ReignCourtLifeMatterState.Invalidated;
                matter.DecisionSummary = summary;
                StateChanged?.Invoke();
                return false;
            }
            if (data.Value<bool?>("nativeActionStarted") == true && data.Value<string>("nativeOptionId") != option)
            { summary = "The accepted action is still completing; its terms cannot be replaced."; return false; }
            if (option == "refer") return StartInternationalReferral(matter, data, decision, out summary);
            bool consensus = ReignInternationalDocketRules.RequiresForeignAcceptance(option,
                data.Value<string>("remedy"), data.Value<bool?>("ransom") == true);
            bool sovereignTerms = data.Value<bool?>("sovereignAuthorized") == true
                || (data.Value<string>("kind") == "proposal" && data.Value<bool?>("counterProposed") != true);
            string agreeingHeroId = decision.Value<string>("agreeingHeroId") ?? string.Empty;
            if (consensus && !sovereignTerms && (decision.Value<bool?>("npcAccepted") != true
                || !matter.Participants.Any(x => x.Role == "ambassador" && x.HeroId == agreeingHeroId)))
            { summary = "The exact agreement still requires an explicit response from the foreign representative."; return false; }
            JObject offeredTerms = ParseObject(offered.TermsJson);
            JObject requestedTerms = decision["terms"] as JObject;
            if (requestedTerms != null && requestedTerms.Properties().Any(p => offeredTerms[p.Name] == null || !JToken.DeepEquals(offeredTerms[p.Name], p.Value)))
            { summary = "Changed terms must be offered and accepted before they can be executed."; return false; }
            int gold = offeredTerms.Value<int?>("gold") ?? 0;
            if (gold < 0) { summary = "An agreement cannot transfer a negative amount of gold."; return false; }
            Hero paymentPayer = null, paymentRecipient = null;
            if (IsInternationalPaymentOption(data, option) && data.Value<bool?>("nativeEffectCommitted") != true
                && !TryResolveInternationalPayment(data, option, offeredTerms, out paymentPayer, out paymentRecipient, out summary)) return false;
            if (consensus && data.Value<bool?>("requiresSovereignReferral") == true)
                return StartInternationalReferral(matter, data, decision, out summary);
            if (data.Value<bool?>("nativeEffectCommitted") == true && data.Value<string>("nativeOptionId") != option)
            { summary = "The original judgment has already taken effect and must finish recording."; return false; }
            if (data.Value<bool?>("nativeEffectCommitted") != true)
            {
            if (option == "accept" && ((data["normalizedAction"] is JObject && data.Value<string>("remedy") == "proposal") || data.Value<string>("remedy") == "sign_trade_agreement"))
            {
                if (!(data["normalizedAction"] is JObject))
                {
                    ForeignAmbassadorPosting posting = _foreignAmbassadors.FirstOrDefault(x => x.IsResident && x.OriginKingdomStringId == origin.StringId);
                    bool granted = (ParseObject(posting?.AuthorityCharterJson)["permissions"] as JArray ?? new JArray()).OfType<JObject>()
                        .Any(x => x.Value<string>("actionId") == "trade_agreement" && x.Value<bool?>("granted") == true);
                    if (!granted) return StartInternationalReferral(matter, data, decision, out summary);
                }
                ReignWorldActionRecord action = data["normalizedAction"] is JObject encoded ? ReignServerClient.ParseCourtWorldAction(encoded)
                    : new ReignWorldActionRecord { Type = ReignWorldActionType.DiplomacySignTradeAgreement,
                        ActorKingdomStringId = origin.StringId, TargetKingdomStringId = Clan.PlayerClan.Kingdom.StringId,
                        ActorHeroStringId = origin.Leader.StringId, TargetHeroStringId = Hero.MainHero.StringId,
                        TermsJson = new JObject { ["durationDays"] = offeredTerms.Value<int?>("durationDays") ?? 120 }.ToString(Formatting.None) };
                if (action == null) { summary = "The stored diplomatic package could not be decoded."; return false; }
                action.ActionId = matter.MatterId + "_native";
                action.Source = "court_decision";
                action.Reason = matter.Title;
                action.RequiresAcceptance = false;
                action.ExecuteAfterDay = (float)CampaignTime.Now.ToDays;
                ReignAICampaignBehavior ai = ReignAICampaignBehavior.Instance;
                if (ai == null) { summary = "The diplomatic action service is unavailable."; return false; }
                ReignWorldActionRecord stored = ai.Actions.FirstOrDefault(x => x.ActionId == action.ActionId);
                if (stored?.Status == ReignWorldActionStatus.Completed)
                {
                    if (data.Value<bool?>("nativeActionStarted") != true || stored.Source != "court_decision"
                        || !JToken.DeepEquals(CourtNativeActionIdentity(action), CourtNativeActionIdentity(stored)))
                    { summary = "The completed action does not match this confirmed court proposal."; return false; }
                    data["nativeReceipt"] = CourtNativeCompletionReceipt(stored);
                }
                else
                {
                    if (!ReignActionValidator.Validate(action, out summary)) return false;
                    if (data.Value<bool?>("nativeActionStarted") != true)
                    {
                        data["nativeConfirmedDecision"] = decision.DeepClone();
                        data["nativeConfirmedTerms"] = offeredTerms.DeepClone();
                        data["nativeConfirmedAction"] = CourtNativeActionIdentity(action);
                    }
                    data["nativeActionStarted"] = true;
                    data["nativeOptionId"] = option;
                    matter.PayloadJson = data.ToString(Formatting.None);
                    ai.EnqueueAndExecuteServerActions(new[] { action });
                    ReignActionResult result = ai.GetLastActionResult(action.ActionId);
                    if (result != null) data["nativeReceipt"] = JObject.FromObject(result);
                    // A Government hearing can successfully queue work without completing the treaty.
                    // Preserve its progress receipt, but do not apply the court settlement yet.
                    matter.PayloadJson = data.ToString(Formatting.None);
                    if (result?.Success != true || result.Completed != true)
                    { summary = result?.Message ?? "The action is awaiting authoritative execution."; return false; }
                    data["nativeReceipt"] = CourtNativeCompletionReceipt(action);
                }
            }
            else if (option == "accept" && data.Value<string>("remedy") == "prisoner")
            {
                Hero captive = FindHero(data.Value<string>("captiveId"));
                if (captive == null || !captive.IsPrisoner || captive.PartyBelongedToAsPrisoner?.Owner != Hero.MainHero)
                { summary = "The named prisoner is no longer in your custody."; return false; }
                bool ransom = data.Value<bool?>("ransom") == true;
                if (ransom && paymentPayer.Gold < gold) { summary = "The foreign ruler can no longer afford the offered ransom."; return false; }
                EndCaptivityAction.ApplyByReleasedByChoice(captive, Hero.MainHero);
                if (captive.IsPrisoner) { summary = "The native release action did not complete."; return false; }
                JObject releaseReceipt = new JObject { ["releasedHeroId"] = captive.StringId, ["isPrisoner"] = false, ["ransomGold"] = ransom ? gold : 0 };
                if (ransom) releaseReceipt["payment"] = ExecuteInternationalPayment(paymentPayer, paymentRecipient, gold);
                data["nativeReceipt"] = releaseReceipt;
            }
            else if (option == "compensate" || option == "compromise" || (option == "accept" && data.Value<string>("remedy") == "gift"))
            {
                if (paymentPayer.Gold < gold) { summary = "The promised funds are unavailable."; return false; }
                data["nativeReceipt"] = ExecuteInternationalPayment(paymentPayer, paymentRecipient, gold);
            }
            else if (option == "accept" && data.Value<string>("remedy") != "goodwill")
            { summary = "The sovereign's exact reply has no executable agreement attached yet."; return false; }
            data["nativeEffectCommitted"] = true;
            data["nativeOptionId"] = option;
            matter.PayloadJson = data.ToString(Formatting.None);
            }
            CompleteInternationalCourtSettlement(matter, data, decision, option, gold, out summary);
            ProcessInternationalCourtLife(CampaignTime.Now.ToDays);
            return true;
        }

        private void CompleteInternationalCourtSettlement(ReignCourtLifeMatter matter, JObject data,
            JObject decision, string option, int gold, out string summary)
        {
            QueueInternationalRelations(matter, data, option, decision);
            summary = option == "refuse" ? "The foreign request was refused." : option == "favor_domestic" ? "The ruler upheld the domestic noble's position."
                : option == "favor_foreign" ? "The ruler upheld the foreign noble's position." : option == "compromise" ? "The accepted settlement was carried out."
                : option == "compensate" ? gold + (data.Value<bool?>("extortion") == true ? " gold was paid to satisfy the foreign crown's demand." : " gold was paid in compensation.") : "The accepted terms were carried out.";
            matter.SelectedOptionId = option;
            matter.ResolutionReceiptId = matter.MatterId + "_resolution";
            matter.EffectsCommitted = true;
            matter.State = ReignCourtLifeMatterState.Resolved;
            matter.DecisionSummary = summary;
            matter.PayloadJson = data.ToString(Formatting.None);
            ReignRulerDocketState state = EnsureRulerDocketState();
            state.InternationalReceiptsJson = state.InternationalReceiptsJson ?? new List<string>();
            state.InternationalReceiptsJson.Add(new JObject { ["matterId"] = matter.MatterId, ["receiptId"] = matter.ResolutionReceiptId,
                ["effectsCommitted"] = true, ["outcome"] = option, ["severityRank"] = (int)matter.Severity,
                ["foreign"] = data["foreign"].DeepClone(), ["player"] = data["player"].DeepClone(), ["channel"] = data["channel"],
                ["continuityVersion"] = 1, ["title"] = matter.Title, ["summary"] = summary,
                ["extortion"] = data.Value<bool?>("extortion") == true,
                ["nativeReceipt"] = data["nativeReceipt"]?.DeepClone(),
                ["paymentParties"] = InternationalPaymentParties(matter),
                ["witnessHeroIds"] = new JArray(matter.Participants.Where(x => !string.IsNullOrWhiteSpace(x.HeroId)).Select(x => x.HeroId).Distinct()) }.ToString(Formatting.None));
            TryProcessPendingDocketRelationshipAdjustments();
            _internationalRefreshAfterUtc = DateTime.MinValue;
        }

        private static JObject CourtNativeCompletionReceipt(ReignWorldActionRecord action) => new JObject
        {
            ["actionId"] = action.ActionId, ["status"] = "completed", ["type"] = action.Type.ToString(),
            ["terms"] = ParseObject(action.TermsJson), ["executionSnapshot"] = ParseObject(action.ExecutionSnapshotJson)
        };

        private static JObject CourtNativeActionIdentity(ReignWorldActionRecord action) => new JObject
        {
            ["type"] = action.TypeValue,
            ["actorHero"] = action.ActorHeroStringId ?? "", ["actorClan"] = action.ActorClanStringId ?? "",
            ["actorKingdom"] = action.ActorKingdomStringId ?? "", ["targetHero"] = action.TargetHeroStringId ?? "",
            ["targetClan"] = action.TargetClanStringId ?? "", ["targetKingdom"] = action.TargetKingdomStringId ?? "",
            ["targetSettlement"] = action.TargetSettlementStringId ?? "", ["terms"] = ParseObject(action.TermsJson),
            ["termsHash"] = action.TermsHash ?? "", ["negotiation"] = action.NegotiationId ?? "",
            ["authorization"] = action.AuthorizationMode ?? ""
        };

        private bool TryReconcileCompletedInternationalAction(ReignCourtLifeMatter matter, JObject data)
        {
            if (data.Value<bool?>("nativeActionStarted") != true || matter.EffectsCommitted
                || _courtLifeConversationsInFlight > 0) return false;
            ReignWorldActionRecord stored = ReignAICampaignBehavior.Instance?.Actions
                .FirstOrDefault(x => x.ActionId == matter.MatterId + "_native");
            if (stored == null) return false;
            JObject expected = data["nativeConfirmedAction"] as JObject;
            JObject decision = data["nativeConfirmedDecision"] as JObject;
            JObject terms = data["nativeConfirmedTerms"] as JObject;
            if (expected == null)
            {
                // Older saves retain the original normalized proposal and explicit start marker.
                // Missing or changed evidence is never filled in from the model's narration.
                ReignWorldActionRecord original = data["normalizedAction"] is JObject encoded
                    ? ReignServerClient.ParseCourtWorldAction(encoded) : null;
                if (original == null) return false;
                expected = CourtNativeActionIdentity(original);
                decision = ParseObject(matter.AcceptedDecisionJson);
                terms = decision["acceptedTerms"] as JObject;
            }
            ReignCourtLifeOption offered = matter.Options.FirstOrDefault(x => x.OptionId == "accept");
            bool exactDecision = decision?.Value<string>("optionId") == "accept" && terms != null
                && JToken.DeepEquals(terms, ParseObject(offered?.TermsJson));
            bool sameScope = matter.CampaignId == Session?.CampaignId && matter.TimelineId == Session?.TimelineId
                && data["player"]?.Value<string>("leaderHeroId") == Hero.MainHero?.StringId
                && data["player"]?.Value<string>("kingdomId") == Clan.PlayerClan?.Kingdom?.StringId;
            if (!ReignCourtNativeCompletionRules.CanReconcile(true, matter.EffectsCommitted,
                data.Value<string>("nativeOptionId"), matter.MatterId, stored.ActionId, stored.Status.ToString(),
                stored.Source, exactDecision && JToken.DeepEquals(expected, CourtNativeActionIdentity(stored)), sameScope))
                return false;

            data["nativeEffectCommitted"] = true;
            data["nativeReceipt"] = CourtNativeCompletionReceipt(stored);
            CompleteInternationalCourtSettlement(matter, data, decision, "accept", 0, out string summary);
            List<ReignDocketHistoryRecord> history = EnsureRulerDocketState().History;
            if (!history.Any(x => x.RecordId == matter.ResolutionReceiptId))
                history.Add(new ReignDocketHistoryRecord { RecordId = matter.ResolutionReceiptId,
                    ReignId = matter.ReignId, Type = "court_life_international", Outcome = "accept", Day = CurrentDay(),
                    Summary = matter.Title + ": " + summary,
                    Detail = "Audience " + matter.MatterId + "; template " + matter.TemplateId });
            matter.ConversationLines.Add(new ReignDocketConversationLine {
                Speaker = "Court Clerk", Role = "system", Text = summary });
            StateChanged?.Invoke();
            return true;
        }

        private bool TryResolveInternationalPayment(JObject data, string option, JObject terms,
            out Hero payer, out Hero recipient, out string summary)
        {
            payer = null; recipient = null; summary = string.Empty;
            if (terms["gold"]?.Type != JTokenType.Integer || terms.Value<long>("gold") < 0 || terms.Value<long>("gold") > 1000000
                || terms["payerHeroId"]?.Type != JTokenType.String || terms["recipientHeroId"]?.Type != JTokenType.String)
            { summary = "The payment must specify a valid amount and exact payer and recipient. Please agree to fresh terms."; return false; }
            string payerId = terms.Value<string>("payerHeroId"), recipientId = terms.Value<string>("recipientHeroId");
            if (!ReignInternationalDocketRules.PaymentPartiesAllowed(payerId, recipientId, Hero.MainHero?.StringId,
                data.Value<string>("foreignRulerId"), data.Value<string>("domesticLordId"), data.Value<string>("foreignLordId"),
                option == "accept", data.Value<bool?>("extortion") == true))
            { summary = "These payment parties are outside the agreement's authority. The ruler may offer personal funds to a named claimant, but cannot spend another household's money."; return false; }
            payer = FindHero(payerId); recipient = FindHero(recipientId);
            if (payer?.IsAlive != true || recipient?.IsAlive != true)
            { summary = "The named payer or recipient is unavailable; a fresh agreement is required."; return false; }
            return true;
        }

        private static JObject ExecuteInternationalPayment(Hero payer, Hero recipient, int gold)
        {
            int beforePayer = payer.Gold, beforeRecipient = recipient.Gold;
            GiveGoldAction.ApplyBetweenCharacters(payer, recipient, gold, true);
            return new JObject { ["gold"] = gold, ["payerId"] = payer.StringId, ["recipientId"] = recipient.StringId,
                ["payerBefore"] = beforePayer, ["payerAfter"] = payer.Gold, ["recipientBefore"] = beforeRecipient, ["recipientAfter"] = recipient.Gold };
        }

        private bool StartInternationalReferral(ReignCourtLifeMatter matter, JObject data, JObject decision, out string summary)
        {
            summary = "The exact terms are being sent to the represented ruler; no agreement has executed.";
            ForeignAmbassadorPosting posting = _foreignAmbassadors.FirstOrDefault(x => x.IsResident && x.OriginKingdomStringId == matter.KingdomId);
            if (posting == null) { summary = "The ambassador must be present before sending a referral."; return false; }
            JObject candidate = data["candidate"] as JObject;
            ReignCourtLifeOption intended = matter.Options.FirstOrDefault(x => x.OptionId == (decision.Value<string>("optionId") == "refer"
                ? decision.Value<string>("referralOptionId")
                : decision.Value<string>("optionId")));
            if (intended == null) { summary = "Please identify the exact agreement terms to send. Referring the complaint alone does not authorize a settlement offer."; return false; }
            JObject exactTerms = ParseObject(intended.TermsJson);
            if (decision.Value<string>("optionId") == "refer"
                && (!(decision["referralTerms"] is JObject agreedReferralTerms) || !JToken.DeepEquals(agreedReferralTerms, exactTerms)))
            { summary = "The referral terms have changed or were not agreed. Please name the exact proposal again."; return false; }
            if (IsInternationalPaymentOption(data, intended.OptionId)
                && !TryResolveInternationalPayment(data, intended.OptionId, exactTerms, out _, out _, out summary)) return false;
            data["referredResolutionOption"] = intended.OptionId;
            data["referredResolutionTerms"] = exactTerms.DeepClone();
            if (candidate == null)
            {
                string remedy = data.Value<string>("remedy");
                JObject referralTerms = (JObject)exactTerms.DeepClone();
                if (remedy != "sign_trade_agreement")
                {
                    referralTerms["durationDays"] = 120;
                    referralTerms["isPublic"] = true;
                    referralTerms["promise"] = "Accept this exact settlement of the court matter: " + DescribeInternationalTerms(exactTerms);
                }
                candidate = new JObject { ["command"] = remedy == "sign_trade_agreement" ? remedy : "record_promise",
                    ["actorKingdomId"] = Clan.PlayerClan.Kingdom.StringId, ["targetKingdomId"] = matter.KingdomId,
                    ["publicReason"] = matter.Summary, ["terms"] = referralTerms };
            }
            candidate["actorKingdomStringId"] = candidate["actorKingdomStringId"] ?? candidate["actorKingdomId"];
            candidate["targetKingdomStringId"] = candidate["targetKingdomStringId"] ?? candidate["targetKingdomId"];
            data["referredCommand"] = candidate.Value<string>("command");
            if (!_internationalReferralInFlight.Add(matter.MatterId)) return false;
            matter.State = ReignCourtLifeMatterState.Deferred;
            matter.PayloadJson = data.ToString(Formatting.None);
            decision["deferred"] = true;
            _ = SendInternationalReferralAsync(matter, posting.PostingId, candidate);
            return true;
        }

        private async Task SendInternationalReferralAsync(ReignCourtLifeMatter matter, string postingId, JObject candidate)
        {
            try
            {
                ReignCourtServerResponse response = await ReignCourtServerClient.InternationalCourtReferAsync(matter.MatterId, postingId, candidate).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    JObject data = ParseObject(matter.PayloadJson);
                    if (response.Ok) { data["referralId"] = response.Raw.Value<string>("referralId"); data["referralDueDay"] = response.Raw.Value<double?>("dueDay");
                        data["replyPromoted"] = false; data["candidate"] = candidate; }
                    else { matter.State = ReignCourtLifeMatterState.Pending; data["referralError"] = response.Error; }
                    matter.PayloadJson = data.ToString(Formatting.None);
                    StateChanged?.Invoke();
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() => { matter.State = ReignCourtLifeMatterState.Pending;
                    matter.TechnicalFailure = true; ReignLog.Warn("International referral deferred: " + ex.Message); }).ConfigureAwait(false);
            }
            finally { await ReignMainThread.InvokeAsync(() => _internationalReferralInFlight.Remove(matter.MatterId)).ConfigureAwait(false); }
        }

        private void QueueInternationalRelations(ReignCourtLifeMatter matter, JObject data, string option, JObject decision)
        {
            string paidRecipient = (data["nativeReceipt"] as JObject)?.Value<string>("recipientId");
            bool foreignWins = ReignCourtPartyAcceptanceRules.ForeignSideBenefits(option, paidRecipient, data.Value<string>("domesticLordId"));
            int winner = ReignRulerDocketRules.NobleWinnerRelation(matter.Severity);
            string foreignId = data.Value<string>("foreignLordId");
            string domesticId = data.Value<string>("domesticLordId");
            var acceptances = new Dictionary<string, int>(StringComparer.Ordinal);
            ReignCourtLifeOption chosen = matter.Options.FirstOrDefault(x => x.OptionId == option);
            bool exactAgreement = decision.Value<string>("optionId") == option && chosen != null
                && JToken.DeepEquals(decision["acceptedTerms"], ParseCourtLifeJson(chosen.TermsJson));
            if (exactAgreement && decision["acceptedPartyTiers"] is JObject recorded)
                foreach (JProperty entry in recorded.Properties())
                    if (entry.Value.Type == JTokenType.Integer && matter.Participants.Any(x => x.HeroId == entry.Name))
                        acceptances[entry.Name] = entry.Value.Value<int>();
            string agreeingHeroId = decision.Value<string>("agreeingHeroId");
            if (exactAgreement && decision.Value<bool?>("npcAccepted") == true && !string.IsNullOrWhiteSpace(agreeingHeroId))
                acceptances[agreeingHeroId] = decision.Value<int?>("acceptanceTier") ?? 0;
            int acceptance = ReignCourtPartyAcceptanceRules.LosingPartyTier(foreignWins, domesticId,
                matter.Participants.Where(x => x.Role == "ambassador").Select(x => x.HeroId), acceptances);
            int loser = ReignRulerDocketRules.AcceptedLossRelation(ReignRulerDocketRules.NobleLoserRelation(matter.Severity),
                acceptance);
            QueueInternationalRelation(matter, foreignId, foreignWins ? winner : loser);
            QueueInternationalRelation(matter, domesticId, foreignWins ? loser : winner);
            string rulerId = data.Value<string>("foreignRulerId");
            QueueInternationalRelation(matter, rulerId, ReignInternationalDocketRules.RulerRelation(foreignWins ? winner : loser));
            foreach (string principalId in new[] { domesticId, foreignId }.Where(x => !string.IsNullOrEmpty(x)))
            {
                Hero principal = FindHero(principalId);
                if (principal?.Clan == null) continue;
                int delta = ReignRulerDocketRules.ClanSpilloverRelation(principalId == foreignId ? foreignWins ? winner : loser : foreignWins ? loser : winner);
                foreach (Hero member in principal.Clan.Heroes.Where(x => x.IsAlive && x.StringId != principalId && x.StringId != rulerId && x != Hero.MainHero))
                    QueueInternationalRelation(matter, member.StringId, delta);
            }
        }

        private void QueueInternationalRelation(ReignCourtLifeMatter matter, string heroId, int delta)
        {
            if (string.IsNullOrWhiteSpace(heroId) || delta == 0 || FindHero(heroId)?.IsAlive != true) return;
            string receipt = matter.MatterId + "_relation_" + heroId;
            List<ReignDirectionalRelationAdjustment> rows = EnsureRulerDocketState().PendingRelationAdjustments;
            if (rows.Any(x => x.AdjustmentId == receipt)) return;
            rows.Add(new ReignDirectionalRelationAdjustment { AdjustmentId = receipt, ObserverHeroId = heroId,
                SubjectHeroId = Hero.MainHero.StringId, Delta = delta, Reason = "The ruler's international judgment affected this household." });
        }

        private bool InternationalClaimantsRemainEligible(JObject data, Kingdom origin)
        {
            string domesticId = data.Value<string>("domesticLordId"), foreignId = data.Value<string>("foreignLordId");
            Hero domestic = FindHero(domesticId), foreign = FindHero(foreignId);
            return (string.IsNullOrWhiteSpace(domesticId) || (domestic?.IsAlive == true && domestic.Clan?.Kingdom == Clan.PlayerClan?.Kingdom))
                && (string.IsNullOrWhiteSpace(foreignId) || (foreign?.IsAlive == true && foreign.Clan?.Kingdom == origin));
        }
    }
}
