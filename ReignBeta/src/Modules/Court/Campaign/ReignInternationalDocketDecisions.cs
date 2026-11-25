using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using ReignBeta.Shared.Dialogue;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using ReignBeta.Court.Integration;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Court.Campaign
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        public bool TryCounterInternationalTerms(ReignCourtLifeMatter matter, JObject proposedTerms, out string summary)
        {
            summary = string.Empty;
            if (matter == null || matter.Source != ReignDocketSource.International || !matter.IsPending
                || matter.State == ReignCourtLifeMatterState.Deferred || matter.EffectsCommitted || !HasRoyalCommandAccess)
            { summary = "The international audience is not available for revised terms."; return false; }
            if (TryReconcileDirectCourtRansom(matter, null, out _, out summary)) return false;
            if (TryReconcileDirectCourtPayment(matter, null, out _, out summary)) return false;
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
            if (TryReconcileDirectCourtRansom(matter, decision, out bool ransomRecovered, out summary))
                return ransomRecovered;
            if (matter.EffectsCommitted) { summary = matter.DecisionSummary; return true; }
            if (TryReconcileDirectCourtPayment(matter, decision, out bool recovered, out summary)) return recovered;
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
            bool effectCommitted = data.Value<bool?>("nativeEffectCommitted") == true;
            if (effectCommitted && data.Value<string>("nativeOptionId") != option)
            { summary = "The original judgment has already taken effect and must finish recording."; return false; }
            JObject offeredTerms = ParseObject(offered.TermsJson);
            if (option == "refer") return StartInternationalReferral(matter, data, decision, out summary);
            string remedy = data.Value<string>("remedy");
            bool unilateralGoodwill = option == "accept" && remedy == "goodwill" && data.Value<string>("kind") == "incident";
            bool consensus = !unilateralGoodwill && ReignInternationalDocketRules.RequiresForeignAcceptance(
                option, remedy, data.Value<bool?>("ransom") == true);
            // Court prose and older saved interpretation flags cannot establish consensus.
            bool promotedOffer = data.Value<bool?>("replyPromoted") == true && data.Value<bool?>("sovereignAuthorized") == true
                && option == (data.Value<string>("referredResolutionOption") ?? "accept");
            if (!effectCommitted && (consensus || promotedOffer) && !HasExactSovereignOffer(matter, data, option, offeredTerms))
            { summary = "Refer these exact terms for the foreign ruler's decision before confirming the agreement."; return false; }
            JObject requestedTerms = decision["terms"] as JObject;
            if (requestedTerms != null && requestedTerms.Properties().Any(p => offeredTerms[p.Name] == null || !JToken.DeepEquals(offeredTerms[p.Name], p.Value)))
            { summary = "Changed terms must be offered and accepted before they can be executed."; return false; }
            int gold = offeredTerms.Value<int?>("gold") ?? 0;
            if (gold < 0) { summary = "An agreement cannot transfer a negative amount of gold."; return false; }
            Hero paymentPayer = null, paymentRecipient = null;
            if (IsInternationalPaymentOption(data, option) && data.Value<bool?>("nativeEffectCommitted") != true
                && !TryResolveInternationalPayment(data, option, offeredTerms, out paymentPayer, out paymentRecipient, out summary)) return false;
            if (consensus && !effectCommitted && data.Value<bool?>("requiresSovereignReferral") == true)
                return StartInternationalReferral(matter, data, decision, out summary);
            if (!effectCommitted)
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
                ReignWorldActionRecord action = data["normalizedAction"] is JObject encoded ? ReignCourtDialogueClient.ParseCourtWorldAction(encoded)
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
                ReignAICampaignBehavior ai = ReignServices.AI;
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
                return TryExecuteDirectCourtRansom(matter, data, offeredTerms, origin, paymentPayer, paymentRecipient, out summary);
            }
            else if (IsDirectInternationalPayment(data, option))
            {
                if (!TryExecuteDirectInternationalPayment(matter, data, option, offeredTerms, paymentPayer,
                    paymentRecipient, gold, out summary)) return false;
            }
            else if (option == "accept" && data.Value<string>("remedy") != "goodwill")
            { summary = "The sovereign's exact reply has no executable agreement attached yet."; return false; }
            data["nativeEffectCommitted"] = true;
            data["nativeOptionId"] = option;
            matter.PayloadJson = data.ToString(Formatting.None);
            }
            CompleteInternationalCourtSettlement(matter, data, option, gold, out summary,
                data["nativeCourtPaymentAttempt"]?["material"]?["receiptContext"] as JObject);
            ProcessInternationalCourtLife(CampaignTime.Now.ToDays);
            return true;
        }

        private void CompleteInternationalCourtSettlement(ReignCourtLifeMatter matter, JObject data,
            string option, int gold, out string summary, JObject receiptContext = null, bool recoveredPayment = false)
        {
            if (!recoveredPayment) QueueInternationalRelations(matter, data, option);
            JObject presentation = receiptContext ?? data;
            summary = option == "refuse" ? "The foreign request was refused." : option == "favor_domestic" ? "The ruler upheld the domestic noble's position."
                : option == "favor_foreign" ? "The ruler upheld the foreign noble's position." : option == "compromise" ? "The accepted settlement was carried out."
                : option == "compensate" ? gold + (presentation.Value<bool?>("extortion") == true ? " gold was paid to satisfy the foreign crown's demand." : " gold was paid in compensation.") : "The accepted terms were carried out.";
            matter.SelectedOptionId = option;
            matter.ResolutionReceiptId = matter.MatterId + "_resolution";
            matter.EffectsCommitted = true;
            matter.State = ReignCourtLifeMatterState.Resolved;
            matter.DecisionSummary = summary;
            matter.PayloadJson = data.ToString(Formatting.None);
            ReignRulerDocketState state = EnsureRulerDocketState();
            state.InternationalReceiptsJson = state.InternationalReceiptsJson ?? new List<string>();
            if (!state.InternationalReceiptsJson.Any(encoded => ParseObject(encoded).Value<string>("receiptId") == matter.ResolutionReceiptId))
                state.InternationalReceiptsJson.Add(new JObject { ["matterId"] = matter.MatterId, ["receiptId"] = matter.ResolutionReceiptId,
                ["effectsCommitted"] = true, ["outcome"] = option, ["severityRank"] = (int)matter.Severity,
                ["foreign"] = presentation["foreign"].DeepClone(), ["player"] = presentation["player"].DeepClone(), ["channel"] = presentation["channel"],
                ["continuityVersion"] = 1, ["title"] = presentation.Value<string>("title") ?? matter.Title, ["summary"] = summary,
                ["extortion"] = presentation.Value<bool?>("extortion") == true,
                ["nativeReceipt"] = data["nativeReceipt"]?.DeepClone(),
                ["paymentParties"] = receiptContext?["paymentParties"]?.DeepClone() ?? InternationalPaymentParties(matter),
                ["witnessHeroIds"] = receiptContext?["witnessHeroIds"]?.DeepClone()
                    ?? new JArray(matter.Participants.Where(x => !string.IsNullOrWhiteSpace(x.HeroId)).Select(x => x.HeroId).Distinct()) }.ToString(Formatting.None));
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
            ReignWorldActionRecord stored = ReignServices.AI?.Actions
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
                    ? ReignCourtDialogueClient.ParseCourtWorldAction(encoded) : null;
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
            CompleteInternationalCourtSettlement(matter, data, "accept", 0, out string summary);
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

        private static bool IsDirectInternationalPayment(JObject data, string option)
            => option == "compensate" || option == "compromise" || (option == "accept" && DirectPaymentText(data, "remedy") == "gift");

        private const string DirectRansomReceiptType = "reign-direct-court-prisoner-v1";

        private static string DirectRansomKey(ReignCourtLifeMatter matter)
            => ReignNativeRansom.Key("court_matter", matter.CampaignId, matter.TimelineId, matter.MatterId);

        private static bool HasDirectCourtRansomAttempt(ReignCourtLifeMatter matter)
            => matter?.Source == ReignDocketSource.International
                && ReignServices.GoldTransferService?.Ransoms.HasAttempt(DirectRansomKey(matter)) == true;

        private bool TryExecuteDirectCourtRansom(ReignCourtLifeMatter matter, JObject data, JObject terms,
            Kingdom origin, Hero payer, Hero recipient, out string summary)
        {
            var native = new ReignNativeRansom();
            string captiveId = DirectPaymentText(terms, "captiveId");
            bool ransom = data.Value<bool?>("ransom") == true;
            Hero player = Hero.MainHero, foreignRuler = origin?.Leader;
            bool OwnsConfirmation() => OwnsDirectCourtPayment(matter) && HasRoyalCommandAccess
                && matter.CampaignId == Session?.CampaignId && matter.TimelineId == Session?.TimelineId
                && matter.IsPending && !matter.EffectsCommitted
                && Hero.MainHero == player && origin?.IsEliminated == false && origin.Leader == foreignRuler
                && foreignRuler?.IsAlive == true && foreignRuler.StringId == DirectPaymentText(data, "foreignRulerId")
                && JToken.DeepEquals(terms, ParseObject(matter.Options.FirstOrDefault(row => row.OptionId == "accept")?.TermsJson));
            if (!OwnsConfirmation() || !native.OwnsLoadedWorld() || string.IsNullOrWhiteSpace(captiveId)
                || captiveId != DirectPaymentText(data, "captiveId") || terms["ransom"]?.Type != JTokenType.Boolean
                || terms.Value<bool>("ransom") != ransom || (ransom && (payer != foreignRuler || recipient != player)))
            {
                summary = "The exact accepted prisoner terms no longer match this court and its named parties.";
                return false;
            }

            var captive = new ReignNativeRansom.Captive(FindHero(captiveId), "choice", party => party.Owner == player);
            JObject material = FreezeDirectCourtPayment(matter, data, "accept", terms);
            int? amount = ransom ? terms.Value<int?>("gold") : null;
            if (ransom && !amount.HasValue)
            { summary = "The exact accepted prisoner terms have no ransom amount."; return false; }
            NativeRansomAttempt attempt = native.Execute(DirectRansomKey(matter), ReignCourtTerms.Hash(material.ToString(Formatting.None)),
                material, new[] { captive }, OwnsConfirmation, player, payer, recipient, amount);
            if (attempt == null)
            {
                summary = "The named prisoner is no longer in your verified custody or the exact ransom cannot be transferred.";
                return false;
            }
            if (!native.OwnsLoadedWorld() || !OwnsConfirmation())
            {
                summary = "The court context changed during the native prisoner settlement. Do not repeat it.";
                return false;
            }
            return PublishDirectCourtRansom(matter, attempt, fresh: true, out summary);
        }

        private bool TryReconcileDirectCourtRansom(ReignCourtLifeMatter matter, JObject decision, out bool completed, out string summary)
        {
            completed = false;
            summary = "";
            if (!HasDirectCourtRansomAttempt(matter)) return false;
            var native = new ReignNativeRansom();
            if (!OwnsDirectCourtPayment(matter) || !native.OwnsLoadedWorld())
            {
                summary = "This prisoner settlement belongs to a different court or timeline.";
                return true;
            }

            NativeRansomAttempt attempt = native.Journal.Find(DirectRansomKey(matter));
            if (attempt?.State == NativeRansomState.Held && !matter.EffectsCommitted)
                matter.DecisionSummary = "The earlier prisoner settlement is incomplete or cannot be verified. Do not repeat the release or payment.";
            if (!PublishDirectCourtRansom(matter, attempt, fresh: false, out summary)) return true;
            JObject terms = ParseObject(attempt.MaterialJson)["terms"] as JObject;
            completed = decision == null || (DirectPaymentText(decision, "optionId") == "accept"
                && (decision["terms"] == null || JToken.DeepEquals(decision["terms"], terms)));
            if (!completed) summary = "The earlier prisoner settlement was recorded. The requested decision was not executed.";
            return true;
        }

        private bool PublishDirectCourtRansom(ReignCourtLifeMatter matter, NativeRansomAttempt attempt, bool fresh, out string summary)
        {
            summary = "The earlier prisoner settlement is incomplete or cannot be verified. Do not repeat the release or payment.";
            if (attempt?.IsVerified() != true || attempt.Releases.Count != 1 || attempt.Releases[0].Method != "choice"
                || attempt.Binding != ReignCourtTerms.Hash(attempt.MaterialJson)) return false;
            JObject material = ParseObject(attempt.MaterialJson);
            if (DirectPaymentText(material, "campaignId") != matter.CampaignId || DirectPaymentText(material, "timelineId") != matter.TimelineId
                || DirectPaymentText(material, "matterId") != matter.MatterId || DirectPaymentText(material, "reignId") != matter.ReignId
                || DirectPaymentText(material, "optionId") != "accept" || !(material["terms"] is JObject terms)
                || !(material["receiptContext"] is JObject context) || DirectPaymentText(context, "remedy") != "prisoner"
                || !(context["foreign"] is JObject foreign) || !(context["player"] is JObject player)
                || !(context["paymentParties"] is JArray) || !(context["witnessHeroIds"] is JArray)
                || DirectPaymentText(foreign, "kingdomId") != matter.KingdomId
                || DirectPaymentText(terms, "captiveId") != attempt.Releases[0].HeroId || terms["ransom"]?.Type != JTokenType.Boolean)
                return false;

            bool ransom = terms.Value<bool>("ransom");
            NativeRansomPayment payment = attempt.Payment;
            if (ransom != (payment != null) || (ransom &&
                (!JToken.DeepEquals(terms["gold"], new JValue(payment.Amount)) || payment.Amount > 1000000
                    || payment.PayerId != DirectPaymentText(context, "foreignRulerId") || payment.RecipientId != DirectPaymentText(player, "leaderHeroId")
                    || payment.PayerId != DirectPaymentText(terms, "payerHeroId") || payment.RecipientId != DirectPaymentText(terms, "recipientHeroId"))))
                return false;

            string resolutionId = matter.MatterId + "_resolution";
            if (!string.IsNullOrEmpty(attempt.PublicationId))
            {
                if (attempt.PublicationId != resolutionId || !matter.EffectsCommitted || matter.ResolutionReceiptId != resolutionId
                    || matter.SelectedOptionId != "accept") return false;
                summary = matter.DecisionSummary;
                return true;
            }

            JObject data = ParseObject(matter.PayloadJson);
            var receipt = new JObject
            {
                ["schema"] = DirectRansomReceiptType, ["releasedHeroId"] = attempt.Releases[0].HeroId,
                ["originalCaptorId"] = attempt.Releases[0].CaptorId, ["originalRosterCountBefore"] = attempt.Releases[0].RosterCount,
                ["originalRosterCountAfter"] = attempt.Releases[0].Observed.OriginalRosterCount,
                ["isPrisoner"] = false, ["hasCaptor"] = false,
                ["ransom"] = ransom, ["ransomGold"] = payment?.Amount ?? 0
            };
            if (payment != null) receipt["payment"] = ReignNativeRansom.PaymentReceipt(payment);
            if (matter.EffectsCommitted && (matter.ResolutionReceiptId != resolutionId || matter.SelectedOptionId != "accept"
                || !JToken.DeepEquals(receipt, data["nativeReceipt"]))) return false;
            data["nativeReceipt"] = receipt;
            data["nativeEffectCommitted"] = true;
            data["nativeOptionId"] = "accept";
            CompleteInternationalCourtSettlement(matter, data, "accept", payment?.Amount ?? 0,
                out summary, context, recoveredPayment: !fresh || matter.EffectsCommitted);
            var history = EnsureRulerDocketState().History;
            if (!history.Any(row => row.RecordId == resolutionId))
                history.Add(new ReignDocketHistoryRecord { RecordId = resolutionId, ReignId = matter.ReignId,
                    Type = "court_life_international", Outcome = "accept", Day = CurrentDay(),
                    Summary = (DirectPaymentText(context, "title") ?? matter.Title) + ": " + summary,
                    Detail = "Audience " + matter.MatterId + "; template " + matter.TemplateId,
                    PetitionId = matter.MatterId, TranscriptId = matter.TranscriptId, SceneAssetPath = matter.SceneAssetPath });
            attempt.PublicationId = resolutionId;
            StateChanged?.Invoke();
            return true;
        }

        private static string DirectPaymentText(JObject source, string key) => (source?[key] as JValue)?.Value as string;

        private bool OwnsDirectCourtPayment(ReignCourtLifeMatter matter)
        {
            var world = ReignServices.WorldHistory;
            return matter?.Source == ReignDocketSource.International && OwnsCourtLifeMatter(matter)
                && IsPlayerKingdomRuler() && ReignCampaignIdentity.HasActiveCampaign()
                && !ReignCampaignInitializationGate.IsPending && world?.TimelineReady == true
                && !string.IsNullOrWhiteSpace(matter.MatterId) && !string.IsNullOrWhiteSpace(world.TimelineId)
                && matter.CampaignId == ReignCampaignIdentity.CurrentCampaignId()
                && matter.TimelineId == world.TimelineId && matter.ReignId == CurrentReignId();
        }

        private static string DirectCourtPaymentId(ReignCourtLifeMatter matter)
            => "court_payment_" + ReignCourtTerms.Hash(new JArray(matter.CampaignId, matter.TimelineId, matter.MatterId)
                .ToString(Formatting.None));

        private JObject FreezeDirectCourtPayment(ReignCourtLifeMatter matter, JObject data, string option, JObject terms)
        {
            var context = new JObject
            {
                ["foreign"] = data["foreign"]?.DeepClone(), ["player"] = data["player"]?.DeepClone(),
                ["channel"] = data["channel"]?.DeepClone(), ["extortion"] = data["extortion"]?.DeepClone(),
                ["remedy"] = data["remedy"]?.DeepClone(), ["kind"] = data["kind"]?.DeepClone(),
                ["foreignRulerId"] = data["foreignRulerId"]?.DeepClone(),
                ["domesticLordId"] = data["domesticLordId"]?.DeepClone(), ["foreignLordId"] = data["foreignLordId"]?.DeepClone(),
                ["title"] = matter.Title, ["paymentParties"] = InternationalPaymentParties(matter),
                ["witnessHeroIds"] = new JArray(matter.Participants.Where(x => !string.IsNullOrWhiteSpace(x.HeroId))
                    .Select(x => x.HeroId).Distinct())
            };
            return new JObject
            {
                ["campaignId"] = matter.CampaignId, ["timelineId"] = matter.TimelineId, ["matterId"] = matter.MatterId,
                ["reignId"] = matter.ReignId, ["optionId"] = option, ["terms"] = terms.DeepClone(), ["receiptContext"] = context
            };
        }

        private bool TryReconcileDirectCourtPayment(ReignCourtLifeMatter matter, JObject decision,
            out bool completed, out string summary)
        {
            completed = false;
            summary = "";
            if (matter == null || matter.EffectsCommitted) return false;
            JObject data = ParseObject(matter.PayloadJson);
            JToken savedAttempt = data["nativeCourtPaymentAttempt"];
            if (!OwnsDirectCourtPayment(matter))
            {
                summary = "This payment belongs to a different court or timeline.";
                return savedAttempt != null;
            }

            ReignGoldTransferService service = ReignServices.GoldTransferService;
            string id = DirectCourtPaymentId(matter);
            if (savedAttempt == null && service?.HasReceipt(id) != true) return false;
            if (savedAttempt is JObject failed && (DirectPaymentText(failed, "status") == "failed" || DirectPaymentText(failed, "status") == "unknown"))
            {
                summary = DirectPaymentText(failed, "message") ?? "The earlier payment cannot be repeated.";
                return true;
            }

            GoldPaymentReceipt receipt = service?.ReadInternationalCourtReceipt(id);
            if (!(savedAttempt is JObject attempt) || receipt?.Status != "applied"
                || !MatchesDirectCourtPayment(matter, id, attempt, receipt))
            {
                // A receipt alone never authorizes reconstruction using the current proposal.
                summary = "The earlier payment cannot be verified. Do not repeat it before reconciliation.";
                return true;
            }

            JObject material = (JObject)attempt["material"];
            JObject receiptContext = (JObject)material["receiptContext"];
            string option = DirectPaymentText(material, "optionId");
            data["nativeReceipt"] = DirectCourtPaymentReceipt(receipt);
            data["nativeEffectCommitted"] = true;
            data["nativeOptionId"] = option;
            attempt["status"] = "applied";
            // Record the paid decision before judging current eligibility. Applying relationship
            // changes now could direct them to a household a claimant joined after payment.
            CompleteInternationalCourtSettlement(matter, data, option, receipt.Amount, out summary,
                receiptContext, recoveredPayment: true);
            var history = EnsureRulerDocketState().History;
            if (!history.Any(row => row.RecordId == matter.ResolutionReceiptId))
                history.Add(new ReignDocketHistoryRecord { RecordId = matter.ResolutionReceiptId,
                    ReignId = matter.ReignId, Type = "court_life_international", Outcome = option, Day = CurrentDay(),
                    Summary = (receiptContext.Value<string>("title") ?? matter.Title) + ": " + summary,
                    Detail = "Audience " + matter.MatterId + "; template " + matter.TemplateId, PetitionId = matter.MatterId,
                    TranscriptId = matter.TranscriptId, SceneAssetPath = matter.SceneAssetPath });
            StateChanged?.Invoke();
            completed = decision == null || (DirectPaymentText(decision, "optionId") == option
                && (decision["terms"] == null || JToken.DeepEquals(decision["terms"], material["terms"])));
            if (!completed) summary = "The earlier payment was recorded. The requested decision was not executed.";
            return true;
        }

        private static bool MatchesDirectCourtPayment(ReignCourtLifeMatter matter, string id, JObject attempt, GoldPaymentReceipt receipt)
        {
            if (DirectPaymentText(attempt, "id") != id || !(attempt["material"] is JObject material)
                || DirectPaymentText(material, "campaignId") != matter.CampaignId || DirectPaymentText(material, "timelineId") != matter.TimelineId
                || DirectPaymentText(material, "matterId") != matter.MatterId || DirectPaymentText(material, "reignId") != matter.ReignId
                || !(material["terms"] is JObject terms) || !(material["receiptContext"] is JObject context)
                || !(context["foreign"] is JObject) || !(context["player"] is JObject)
                || !(context["paymentParties"] is JArray) || !(context["witnessHeroIds"] is JArray)
                || !IsDirectInternationalPayment(context, DirectPaymentText(material, "optionId"))) return false;
            return receipt.Agreement == ReignCourtTerms.Hash(material.ToString(Formatting.None))
                && receipt.From == DirectPaymentText(terms, "payerHeroId") && receipt.To == DirectPaymentText(terms, "recipientHeroId")
                && JToken.DeepEquals(terms["gold"], new JValue(receipt.Amount))
                && receipt.Amount >= 0 && receipt.Amount <= 1000000 && receipt.FromBefore >= receipt.Amount && receipt.ToBefore >= 0
                && (long)receipt.ToBefore + receipt.Amount <= int.MaxValue;
        }

        private static JObject DirectCourtPaymentReceipt(GoldPaymentReceipt receipt)
            // Applied receipts already verified both exact deltas. Recover the original observation, not current balances.
            => new JObject { ["gold"] = receipt.Amount, ["payerId"] = receipt.From, ["recipientId"] = receipt.To,
                ["payerBefore"] = receipt.FromBefore, ["payerAfter"] = receipt.FromBefore - receipt.Amount,
                ["recipientBefore"] = receipt.ToBefore, ["recipientAfter"] = receipt.ToBefore + receipt.Amount };

        private bool TryExecuteDirectInternationalPayment(ReignCourtLifeMatter matter, JObject data, string option,
            JObject terms, Hero payer, Hero recipient, int gold, out string summary)
        {
            summary = "";
            if (!OwnsDirectCourtPayment(matter) || !HasRoyalCommandAccess
                || matter.CampaignId != Session?.CampaignId || matter.TimelineId != Session?.TimelineId)
            { summary = "This payment does not belong to the current court timeline."; return false; }
            ReignGoldTransferService service = ReignServices.GoldTransferService;
            if (service == null) { summary = "The native payment service is unavailable."; return false; }

            string id = DirectCourtPaymentId(matter);
            if (data["nativeCourtPaymentAttempt"] != null || service.HasReceipt(id))
            { summary = "An earlier payment attempt must be accounted for. Do not repeat it."; return false; }
            if (payer.Gold < gold || recipient.Gold < 0 || (long)recipient.Gold + gold > int.MaxValue)
            { summary = "The promised payment cannot be afforded or received."; return false; }
            JObject material = FreezeDirectCourtPayment(matter, data, option, terms);
            if (!(material["receiptContext"]?["foreign"] is JObject) || !(material["receiptContext"]?["player"] is JObject))
            { summary = "The confirmed court parties could not be recorded."; return false; }
            var attempt = new JObject { ["id"] = id, ["material"] = material, ["status"] = "started" };
            data["nativeCourtPaymentAttempt"] = attempt;
            matter.PayloadJson = data.ToString(Formatting.None);

            GoldPaymentReceipt receipt;
            bool? nativeChangedBalance;
            string materialHash = ReignCourtTerms.Hash(material.ToString(Formatting.None));
            try { receipt = service.PayInternationalCourt(id, payer, recipient, gold, materialHash, out nativeChangedBalance); }
            catch (Exception)
            {
                summary = "The payment result cannot be verified. Do not repeat it before reconciliation.";
                if (OwnsDirectCourtPayment(matter)) RetainDirectPaymentFailure(matter, data, attempt, "unknown", summary);
                return false;
            }
            if (!OwnsDirectCourtPayment(matter))
            { summary = "The court context changed. The payment must be reconciled before it can be reported."; return false; }
            if (receipt.Status != "applied")
            {
                summary = receipt.Status == "unknown"
                    ? "The payment may be incomplete. Do not repeat it before reconciliation."
                    : nativeChangedBalance == false ? "The native payment made no change. This attempt cannot be repeated."
                    : nativeChangedBalance == true ? "The payment did not match the agreement and both balances were restored. This attempt cannot be repeated."
                    : receipt.Message;
                RetainDirectPaymentFailure(matter, data, attempt, receipt.Status, summary);
                return false;
            }

            data["nativeReceipt"] = DirectCourtPaymentReceipt(receipt);
            attempt["status"] = "applied";
            return true;
        }

        private void RetainDirectPaymentFailure(ReignCourtLifeMatter matter, JObject data, JObject attempt,
            string status, string summary)
        {
            attempt["status"] = status;
            attempt["message"] = summary;
            matter.PayloadJson = data.ToString(Formatting.None);
            matter.DecisionSummary = summary;
            StateChanged?.Invoke();
        }

        private bool StartInternationalReferral(ReignCourtLifeMatter matter, JObject data, JObject decision, out string summary)
        {
            if (HasDirectCourtRansomAttempt(matter))
            { summary = "The earlier prisoner settlement must be reconciled before these terms can change."; return false; }
            summary = "The exact terms are being sent to the represented ruler; no agreement has executed.";
            ForeignAmbassadorPosting posting = _foreignAmbassadors.FirstOrDefault(x => x.IsResident && x.OriginKingdomStringId == matter.KingdomId);
            if (posting == null) { summary = "The ambassador must be present before sending a referral."; return false; }
            JObject previous = data["candidate"] as JObject;
            JObject candidate = (JObject)previous?.DeepClone();
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
            string remedy = data.Value<string>("remedy");
            if (candidate == null || (remedy != "proposal" && remedy != "sign_trade_agreement"))
            {
                JObject referralTerms = (JObject)exactTerms.DeepClone();
                if (remedy != "sign_trade_agreement")
                {
                    referralTerms["durationDays"] = 120;
                    referralTerms["isPublic"] = true;
                    JObject previousTerms = candidate?["terms"] as JObject;
                    bool sameTerms = previousTerms != null
                        && exactTerms.Properties().All(p => JToken.DeepEquals(p.Value, previousTerms[p.Name]));
                    referralTerms["promise"] = (sameTerms ? previousTerms.Value<string>("promise") : null)
                        ?? "Accept the exact terms of the proposed court settlement.";
                }
                candidate = new JObject { ["command"] = remedy == "sign_trade_agreement" ? remedy : "record_promise",
                    ["actorKingdomId"] = Clan.PlayerClan.Kingdom.StringId, ["targetKingdomId"] = matter.KingdomId,
                    ["publicReason"] = candidate?.Value<string>("publicReason")
                        ?? "The ruler referred the exact terms of a court matter for a sovereign decision.",
                    ["terms"] = referralTerms };
            }
            else if (!JToken.DeepEquals(candidate["terms"], exactTerms))
            {
                summary = "The saved diplomatic proposal no longer matches the chosen terms.";
                return false;
            }
            candidate["actorKingdomStringId"] = candidate["actorKingdomStringId"] ?? candidate["actorKingdomId"];
            candidate["targetKingdomStringId"] = candidate["targetKingdomStringId"] ?? candidate["targetKingdomId"];
            JObject savedReply = data["reply"] as JObject;
            bool knownReply = MatchesInternationalReferral(matter, data, savedReply);
            // Offer revisions can update candidate. Keep the sent body as the proof of replay.
            JObject sent = data["sentReferralCandidate"] as JObject
                ?? (knownReply ? ParseObject(savedReply.Value<string>("exact_terms_json")) : null);
            bool replayLegacy = sent?.HasValues == true && sent["courtMatterId"] == null
                && JToken.DeepEquals(sent, candidate);
            if (!replayLegacy)
            {
                candidate["courtMatterId"] = matter.MatterId;
                candidate["courtRulerId"] = data.Value<string>("foreignRulerId");
                candidate["courtOptionId"] = intended.OptionId;
            }
            bool terminalReply = data.Value<string>("referralStatus") == "model_output_unusable"
                || (knownReply && savedReply.Value<string>("status") == "model_output_unusable");
            if (terminalReply && JToken.DeepEquals(sent, candidate) && data.Value<bool?>("replyPromoted") == true)
            {
                summary = "The sovereign's decision was unavailable. Review materially revised terms or leave the matter pending.";
                return false;
            }
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
                    if (response.Ok)
                    {
                        data["referralId"] = response.Raw.Value<string>("referralId");
                        data["referralTermsHash"] = response.Raw.Value<string>("termsHash");
                        data["referralDueDay"] = response.Raw.Value<double?>("dueDay");
                        string status = response.Raw.Value<string>("status");
                        bool unavailable = status == "model_output_unusable"
                            && response.Raw.Value<string>("outcome") == "format_failure"
                            && response.Raw.Value<bool?>("technicalFailure") == true;
                        data["referralStatus"] = status;
                        data["replyPromoted"] = unavailable;
                        data["candidate"] = candidate;
                        data["sentReferralCandidate"] = candidate.DeepClone();
                        if (unavailable)
                        {
                            // A terminal reply can refer to an earlier matter. It carries no sovereign assent.
                            data.Remove("reply");
                            data.Remove("normalizedAction");
                            data["replyReady"] = false;
                            data["sovereignAuthorized"] = false;
                            data["requiresSovereignReferral"] = true;
                            matter.SelectedOptionId = "";
                            matter.State = ReignCourtLifeMatterState.Pending;
                            matter.AvailableDay = CampaignTime.Now.ToDays;
                            matter.Summary += SovereignDecisionUnavailable;
                        }
                    }
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

        private void QueueInternationalRelations(ReignCourtLifeMatter matter, JObject data, string option)
        {
            string paidRecipient = (data["nativeReceipt"] as JObject)?.Value<string>("recipientId");
            bool foreignWins = ReignCourtPartyAcceptanceRules.ForeignSideBenefits(option, paidRecipient, data.Value<string>("domesticLordId"));
            int winner = ReignRulerDocketRules.NobleWinnerRelation(matter.Severity);
            string foreignId = data.Value<string>("foreignLordId");
            string domesticId = data.Value<string>("domesticLordId");
            int loser = ReignRulerDocketRules.NobleLoserRelation(matter.Severity);
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
