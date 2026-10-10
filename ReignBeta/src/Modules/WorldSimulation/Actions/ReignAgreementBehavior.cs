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
using ReignBeta.Save;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Actions
{
    public sealed class ReignAgreementBehavior : CampaignBehaviorBase, IAgreementBackend
    {
        private Dictionary<string, AgreementJournal> _agreements = new Dictionary<string, AgreementJournal>(StringComparer.Ordinal);
        private HashSet<string> _applied = new HashSet<string>(StringComparer.Ordinal);
        private Dictionary<string, ActionAgreement> _offers = new Dictionary<string, ActionAgreement>(StringComparer.Ordinal);
        private Dictionary<string, string> _decisions = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, JObject> _nativeOperations = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private List<string> _chunks = new List<string>();
        public static ReignAgreementBehavior Instance { get; private set; }
        public IReadOnlyCollection<AgreementJournal> Agreements => _agreements.Values;
        public IReadOnlyCollection<ActionAgreement> Offers => _offers.Values;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, Advance);
        }

        public override void SyncData(IDataStore store)
        {
            if (store.IsSaving) _chunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(new { version = 1, agreements = _agreements, applied = _applied, offers = _offers, decisions = _decisions, nativeOperations = _nativeOperations }));
            store.SyncData("_reign_explicit_agreements_v1", ref _chunks);
            if (store.IsLoading && _chunks != null && _chunks.Count > 0)
            {
                var saved = JObject.Parse(ReignSavePayloadCodec.Decode(_chunks));
                if (saved.Value<int>("version") != 1) throw new InvalidOperationException("Unsupported Reign agreement save version.");
                _agreements = saved["agreements"].ToObject<Dictionary<string, AgreementJournal>>();
                _applied = saved["applied"].ToObject<HashSet<string>>();
                _offers = saved["offers"]?.ToObject<Dictionary<string, ActionAgreement>>() ?? new Dictionary<string, ActionAgreement>(StringComparer.Ordinal);
                _decisions = saved["decisions"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>(StringComparer.Ordinal);
                _nativeOperations = saved["nativeOperations"]?.ToObject<Dictionary<string, JObject>>() ?? new Dictionary<string, JObject>(StringComparer.Ordinal);
                foreach (var j in _agreements.Values)
                    if (j.CommittedHash.Length > 0 && j.CommittedHash != j.Agreement.TermsHash()) throw new InvalidOperationException("A saved agreement no longer matches its approved terms.");
            }
        }

        internal JObject NativeOperation(string id) => _nativeOperations.TryGetValue(id, out var receipt) ? (JObject)receipt.DeepClone() : null;
        internal void SaveNativeOperation(string id, JObject receipt) => _nativeOperations[id] = (JObject)receipt.DeepClone();
        internal bool IsActiveAgreement(string id, string hash) => _agreements.TryGetValue(id, out var journal)
            && journal.CommittedHash == hash && (journal.Agreement.State == AgreementState.Waiting || journal.Agreement.State == AgreementState.InTransit);
        internal void RecheckAgreement(string id)
        { if (_agreements.TryGetValue(id, out var journal)) ReignAgreementEngine.Advance(journal, this, ReignActionService.Now); }
        internal void RecheckPendingAgreements() => Advance();

        public ActionAgreement PrepareOffer(ActionAgreement draft, string playerId, string npcId, string conversationId, out ActionReceipt receipt)
        {
            receipt = null;
            if (draft == null) return null;
            var a = ReignActionService.Copy(draft);
            if (a.Origin != ActionOrigin.Conversation || a.PlayerId != playerId || a.NpcId != npcId)
                throw new InvalidOperationException("This action belongs to a different conversation participant.");
            if (a.NpcAcceptedHash.Length == 0) { a.ConversationId = conversationId; a.ProposerId = playerId; }
            if (_agreements.TryGetValue(a.Id, out var committed))
            {
                if (committed.CommittedHash != a.TermsHash()) throw new InvalidOperationException("The operation was already committed with different terms.");
                receipt = Status(a.Id); return null;
            }
            if (a.NpcAcceptedHash.Length > 0)
            {
                if (!_offers.TryGetValue(a.Id, out var pending) || pending.State != AgreementState.Counteroffered
                    || pending.TermsHash() != a.TermsHash() || pending.NpcAcceptedHash != a.TermsHash()
                    || a.ConversationId != conversationId)
                    throw new InvalidOperationException("The NPC's offer has changed or is no longer available.");
                a.State = AgreementState.Counteroffered;
                if (!AgreementRules.Accept(a, playerId, a.TermsHash())) throw new InvalidOperationException("This offer cannot be accepted.");
                receipt = Commit(a, false); _offers.Remove(a.Id); return null;
            }
            a.ConversationId = conversationId; a.ProposerId = playerId;
            ReignActionService.BindGuestReviews(a);
            a.State = AgreementState.Offered; a.PlayerAcceptedHash = a.TermsHash(); a.NpcAcceptedHash = "";
            var errors = ReignActionService.Preview(a);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            if (_offers.TryGetValue(a.Id, out var old) && (a.Revision < old.Revision
                || a.Revision == old.Revision && a.TermsHash() != old.TermsHash()))
                throw new InvalidOperationException("The proposal revision is stale.");
            if (AgreementRules.IsPlayerDeclaration(a))
            {
                a.State = AgreementState.Accepted;
                receipt = Commit(a, false);
                return null;
            }
            _offers[a.Id] = ReignActionService.Copy(a);
            return a;
        }

        public string ReceiveDecision(ActionAgreement offered, ActionDecision decision, string player, string npc, string conversation, string responseId)
        {
            if (string.IsNullOrWhiteSpace(responseId)) throw new InvalidOperationException("The response has no durable identity.");
            string key = conversation + ":" + responseId;
            string content = JsonConvert.SerializeObject(decision);
            if (_decisions.TryGetValue(key, out var prior))
            {
                var receipt = JObject.Parse(prior);
                if (receipt.Value<string>("decision") != content) throw new InvalidOperationException("This response identity already belongs to different terms.");
                return receipt.Value<string>("message") ?? "";
            }
            string message = ReceiveDecisionCore(offered, decision, player, npc, conversation);
            _decisions[key] = new JObject { ["decision"] = content, ["message"] = message }.ToString(Formatting.None);
            return message;
        }

        private string ReceiveDecisionCore(ActionAgreement offered, ActionDecision decision, string player, string npc, string conversation)
        {
            var errors = ActionNegotiation.ValidateDecision(offered, decision, player, npc, conversation, ReignActionService.Now);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            if (offered != null)
            {
                if (_agreements.TryGetValue(offered.Id, out var done) && done.CommittedHash == offered.TermsHash())
                    return Status(offered.Id).Message;
                if (!_offers.TryGetValue(offered.Id, out var saved) || saved.State != AgreementState.Offered || saved.TermsHash() != offered.TermsHash())
                    throw new InvalidOperationException("This response no longer matches the saved pending offer.");
            }
            switch (decision.Kind)
            {
                case "none": return offered == null ? "" : "The NPC has not accepted the proposed action.";
                case "reject": _offers[offered.Id].State = AgreementState.Rejected; return "Action declined. " + decision.Reason;
                case "accept":
                    var receipt = ReignActionService.AcceptNpcDecision(offered, "accept", decision.TermsHash);
                    _offers.Remove(offered.Id); return receipt.Message;
                case "counter":
                case "propose":
                    var proposal = ActionNegotiation.CreateProposal(offered, decision, player, npc, conversation, ReignActionService.Now);
                    errors = ReignActionService.Preview(proposal);
                    errors.AddRange(ReignActionQuotes.ValidateNpcQuote(proposal));
                    if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
                    ReignActionWording.Render(proposal);
                    proposal.NpcAcceptedHash = proposal.TermsHash();
                    _offers[proposal.Id] = proposal;
                    return "Action offer: " + proposal.Sentence + " Open Alt+A to review, accept or counter.";
                default: throw new InvalidOperationException("Unknown action decision.");
            }
        }

        public void DeclineOffer(string id, string reviewedHash)
        {
            if (_offers.TryGetValue(id, out var a) && a.State == AgreementState.Counteroffered && a.TermsHash() == reviewedHash)
                a.State = AgreementState.Rejected;
        }

        public ActionReceipt Commit(ActionAgreement agreement, bool operatorAuthorized)
        {
            if (_agreements.TryGetValue(agreement.Id, out var prior))
            {
                if (prior.CommittedHash != agreement.TermsHash()) throw new InvalidOperationException("This operation ID has different saved terms.");
                return Status(agreement.Id);
            }
            var journal = new AgreementJournal { Agreement = ReignActionService.Copy(agreement) };
            _agreements.Add(agreement.Id, journal);
            try { return ReignAgreementEngine.Commit(journal, this, ReignActionService.Now, operatorAuthorized); }
            catch { if (journal.CommittedHash.Length == 0) _agreements.Remove(agreement.Id); throw; }
        }

        public bool Authorizes(ReignWorldActionRecord record)
        {
            if (record == null || !_agreements.TryGetValue(record.NegotiationId ?? "", out var j)
                || j.CommittedHash != record.TermsHash || j.CommittedHash != j.Agreement.TermsHash()
                || !(j.Agreement.HasRequiredConsents || j.OperatorAuthorized && j.Agreement.Origin == ActionOrigin.Console)
                || (j.Agreement.State != AgreementState.Waiting && j.Agreement.State != AgreementState.InTransit)) return false;
            foreach (var step in j.Steps.Where(s => s.Reserved && !s.Returned && !s.Completed))
            {
                var clause = j.Agreement.Clauses.Single(c => c.Id == step.ClauseId);
                if (clause.ActionId.StartsWith("give.") || clause.ActionId.StartsWith("government.")) continue;
                var expected = CapturedRecord(j.Agreement, clause, step.Snapshot, step.Installment);
                if (JToken.DeepEquals(AuthorityFields(expected), AuthorityFields(record))) return true;
            }
            return false;
        }

        internal bool MatchesObservedCompletedOrder(ReignWorldActionRecord record, string nativeOrderStatus)
            => MatchesObservedCampaignOrder(record, nativeOrderStatus, false);

        internal bool MatchesObservedPendingOrder(ReignWorldActionRecord record, string nativeOrderStatus)
            => MatchesObservedCampaignOrder(record, nativeOrderStatus, true);

        private bool MatchesObservedCampaignOrder(ReignWorldActionRecord record, string nativeOrderStatus, bool restoreInProgress)
        {
            if (record == null || record.Type != ReignWorldActionType.RegularIssueCampaignOrder
                || !_agreements.TryGetValue(record.NegotiationId ?? "", out var journal)) return false;
            foreach (var step in journal.Steps)
            {
                var clause = journal.Agreement.Clauses.SingleOrDefault(c => c.Id == step.ClauseId);
                if (clause == null) continue;
                string prefix = "sa_" + journal.Agreement.Id + "_"
                    + clause.Id.Substring(0, Math.Min(16, clause.Id.Length)) + "_";
                if (!(record.ActionId ?? "").StartsWith(prefix, StringComparison.Ordinal)
                    || !int.TryParse(record.ActionId.Substring(prefix.Length),
                        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                        out int installment) || installment < 0 || installment >= clause.Installments) continue;
                ReignWorldActionRecord expected;
                try { expected = CapturedRecord(journal.Agreement, clause, step.Snapshot, installment); }
                catch (JsonException) { continue; }
                catch (InvalidOperationException) { continue; }
                bool capturedMatches = JToken.DeepEquals(AuthorityFields(expected), AuthorityFields(record));
                bool applied = _applied.Contains(Key(journal.Agreement, clause, installment));
                if (restoreInProgress
                    ? ReignCompletedOrderPolicy.CanRestoreInProgress(journal, step, installment, capturedMatches,
                        applied, nativeOrderStatus, record.Status.ToString())
                    : ReignCompletedOrderPolicy.CanReconcile(journal, step, installment, capturedMatches,
                        applied, nativeOrderStatus, record.Status.ToString(), record.FailureReason)) return true;
            }
            return false;
        }

        private static JObject AuthorityFields(ReignWorldActionRecord r) => JObject.FromObject(new
        {
            r.ActionId, r.TypeValue, r.Source, r.AuthorizationMode, r.NegotiationId, r.TermsHash, r.NegotiatedCommand,
            r.ActorHeroStringId, r.TargetHeroStringId, r.ActorClanStringId, r.TargetClanStringId,
            r.ActorKingdomStringId, r.TargetKingdomStringId, r.TargetSettlementStringId, r.SupporterClanIdsCsv,
            r.TermsJson, r.Reason, r.RequiresAcceptance, r.AcceptedByHeroStringId, r.MinimumTroops, r.DesiredStrength
        });

        private static ReignWorldActionRecord CapturedRecord(ActionAgreement a, ActionClause c, string snapshot, int installment)
        {
            var record = JObject.Parse(snapshot)["worldAction"]?.ToObject<ReignWorldActionRecord>()
                ?? throw new InvalidOperationException("The native action snapshot is missing.");
            record.ActionId = "sa_" + a.Id + "_" + c.Id.Substring(0, Math.Min(16, c.Id.Length)) + "_" + installment;
            return record;
        }
        public ActionReceipt Status(string id)
        {
            if (!_agreements.TryGetValue(id, out var j)) throw new InvalidOperationException("Unknown operation ID.");
            return new ActionReceipt { OperationId = id, TermsHash = j.CommittedHash, State = j.Agreement.State,
                Day = ReignActionService.Now, Message = j.Agreement.Failure.Length > 0 ? j.Agreement.Failure : j.Receipts.LastOrDefault()?.Message ?? j.Agreement.State.ToString(),
                ObservedEffects = j.Steps.Where(s => s.Completed).Select(s => s.ClauseId).ToList() };
        }
        public ActionReceipt Cancel(string id) => _agreements.TryGetValue(id, out var j)
            ? ReignAgreementEngine.Cancel(j, this, ReignActionService.Now) : throw new InvalidOperationException("Unknown operation ID.");
        internal bool ReconcileImpossibleTreaty(ReignWorldActionRecord record, bool treatyRecorded)
        {
            if (record == null || record.Source != "structured_agreement" || record.AuthorizationMode != "explicit_agreement"
                || !_agreements.TryGetValue(record.NegotiationId ?? "", out var journal)
                || journal.Steps == null || journal.Steps.Count != 1 || journal.Agreement.Clauses.Count != 1) return false;
            var step = journal.Steps[0];
            var clause = journal.Agreement.Clauses[0];
            ReignWorldActionRecord expected;
            try { expected = CapturedRecord(journal.Agreement, clause, step.Snapshot, step.Installment); }
            catch (JsonException) { return false; }
            catch (InvalidOperationException) { return false; }
            var actorKingdom = ReignObjectResolver.FindKingdom(record.ActorKingdomStringId);
            var targetKingdom = ReignObjectResolver.FindKingdom(record.TargetKingdomStringId);
            // Missing objects alone do not prove a dissolution. Require an actual native eliminated realm.
            bool eliminated = actorKingdom?.IsEliminated == true || targetKingdom?.IsEliminated == true;
            return ReignUnfulfilledTreatyPolicy.Retire(journal,
                JToken.DeepEquals(AuthorityFields(expected), AuthorityFields(record)),
                _applied.Contains(Key(journal.Agreement, clause, step.Installment)),
                record.Status.ToString(), record.FailureReason, eliminated, treatyRecorded, ReignActionService.Now);
        }
        internal bool ReconcileObservedCampaignCancellation(ReignWorldActionRecord record)
        {
            if (record == null || record.Type != ReignWorldActionType.RegularIssueCampaignOrder
                || !_agreements.TryGetValue(record.NegotiationId ?? "", out var parent)) return false;
            foreach (var step in parent.Steps.Where(s => s.Started && !s.Completed && !s.Returned))
            {
                var clause = parent.Agreement.Clauses.Single(c => c.Id == step.ClauseId);
                if (clause.ActionId.StartsWith("give.", StringComparison.Ordinal)) continue;
                ReignWorldActionRecord captured;
                JObject terms;
                try
                {
                    captured = CapturedRecord(parent.Agreement, clause, step.Snapshot, step.Installment);
                    terms = JObject.Parse(captured.TermsJson ?? "{}");
                }
                catch (JsonException) { continue; }
                catch (InvalidOperationException) { continue; }
                string orderId = terms.Value<string>("orderId");
                if (string.IsNullOrWhiteSpace(orderId)) orderId = captured.ActionId;
                var order = ReignCampaignCommandBehavior.Instance?.Orders.FirstOrDefault(o => o.OrderId == orderId);
                if (order == null || order.CommanderHeroStringId != captured.ActorHeroStringId
                    || order.IssuerHeroStringId != captured.TargetHeroStringId || order.CorrelationId != parent.Agreement.Id
                    || !ReignCompletedOrderPolicy.CanReconcileCancellation(parent, step,
                        JToken.DeepEquals(AuthorityFields(captured), AuthorityFields(record)),
                        _applied.Contains(Key(parent.Agreement, clause, step.Installment)), order.Status, record.Status.ToString())) continue;
                var cancellation = _agreements.Values.FirstOrDefault(j => j.Agreement.State == AgreementState.Completed
                    && j.Agreement.HasBothConsents && j.CommittedHash == j.Agreement.TermsHash()
                    && j.Agreement.PlayerId == parent.Agreement.PlayerId && j.Agreement.NpcId == parent.Agreement.NpcId
                    && j.Steps.Any(s => CompletedCancellationMatches(j, s, clause, orderId)));
                if (cancellation == null) return false;
                var receipt = ReignAgreementEngine.CancelObservedOrder(parent, cancellation, clause.Id,
                    orderId, this, ReignActionService.Now);
                if (receipt.State != AgreementState.Cancelled) return false;
                return ReignAICampaignBehavior.Instance?.RecordObservedCampaignCancellation(record, orderId, cancellation.Agreement.Id) == true;
            }
            return false;
        }

        private bool CompletedCancellationMatches(AgreementJournal journal, AgreementStep step,
            ActionClause original, string orderId)
        {
            var clause = journal.Agreement.Clauses.Single(c => c.Id == step.ClauseId);
            if (!step.Completed || step.FulfilledInstallments <= 0 || !step.Started || step.Returned
                || clause.ActorId != original.ActorId || clause.RecipientId != original.RecipientId
                || !(clause.ActionId == "cancel_campaign_order" || clause.ActionId == "respond_to_order_report"
                    && ReignActionService.Arg(clause, "terms.response") == "cancel")
                || ReignActionService.Arg(clause, "terms.orderId") != orderId
                || !_applied.Contains(Key(journal.Agreement, clause, step.Installment))) return false;
            ReignWorldActionRecord captured;
            try { captured = CapturedRecord(journal.Agreement, clause, step.Snapshot, step.Installment); }
            catch (JsonException) { return false; }
            catch (InvalidOperationException) { return false; }
            var actual = ReignAICampaignBehavior.Instance?.Actions.FirstOrDefault(x => x.ActionId == captured.ActionId);
            return actual?.Status == ReignWorldActionStatus.Completed
                && JToken.DeepEquals(AuthorityFields(captured), AuthorityFields(actual));
        }

        private void Advance()
        {
            foreach (var j in _agreements.Values.Where(j => j.Agreement.State == AgreementState.Waiting || j.Agreement.State == AgreementState.InTransit).ToList())
            {
                var cancelled = ReignAICampaignBehavior.Instance?.Actions.FirstOrDefault(a =>
                    a.Type == ReignWorldActionType.RegularIssueCampaignOrder && a.NegotiationId == j.Agreement.Id
                    && ReconcileObservedCampaignCancellation(a));
                if (cancelled != null || j.Agreement.State == AgreementState.RecoveryRequired) continue;
                ReignAgreementEngine.Advance(j, this, ReignActionService.Now);
            }
        }
        public IReadOnlyList<string> Preflight(ActionAgreement a) => ReignActionService.Preview(a);
        internal IEnumerable<string> CheckAssetReservations(ActionAgreement a)
        {
            foreach (var clause in a.Clauses.Where(c => new[] { "give.settlement", "give.workshop", "give.ship" }.Contains(c.ActionId)
                || c.ActionId == "give.prisoner" && Hero.AllAliveHeroes.Any(h => h.CharacterObject.StringId == ReignActionService.Arg(c, "assetId"))))
                if (_agreements.Values.Any(j => j.Agreement.Id != a.Id
                    && (j.Agreement.State == AgreementState.Reserved || j.Agreement.State == AgreementState.Waiting
                        || j.Agreement.State == AgreementState.InTransit || j.Agreement.State == AgreementState.RecoveryRequired)
                    && j.Agreement.Clauses.Any(c => c.ActionId == clause.ActionId && c.ActorId == clause.ActorId
                        && ReignActionService.Arg(c, "assetId") == ReignActionService.Arg(clause, "assetId")
                        && j.Steps.Any(s => s.ClauseId == c.Id && s.Reserved && !s.Returned && !s.Completed))))
                    yield return "This owned asset or prisoner is already reserved by another agreement.";
        }
        public string Capture(ActionAgreement a, ActionClause c) => c.ActionId.StartsWith("give.", StringComparison.Ordinal)
            ? ReignAssetTransfer.Capture(a, c).ToString(Formatting.None)
            : c.ActionId.StartsWith("government.", StringComparison.Ordinal) ? ReignGovernmentCampaignBehavior.Instance.CaptureGovernmentAgreement(c).ToString(Formatting.None)
#if !REIGN_EXCLUDE_COURT
            : ReignActionCatalog.Find(c.ActionId).Family == "court" ? ReignCourtCampaignBehavior.Instance.CaptureCourtAgreement(c).ToString(Formatting.None)
#endif
            : new JObject { ["worldAction"] = JObject.FromObject(ReignActionService.WorldRecord(a, c)) }.ToString(Formatting.None);
        public void Reserve(ActionAgreement a, ActionClause c, string snapshot) { if (c.ActionId.StartsWith("give.", StringComparison.Ordinal)) ReignAssetTransfer.Reserve(c, JObject.Parse(snapshot)); }
        public void Return(ActionAgreement a, ActionClause c, string snapshot, int fulfilledInstallments) { if (c.ActionId.StartsWith("give.", StringComparison.Ordinal)) ReignAssetTransfer.Return(c, JObject.Parse(snapshot), fulfilledInstallments); }
        private static string Key(ActionAgreement a, ActionClause c, int installment) => a.Id + ":" + c.Id + ":" + installment;
        public void ApplyAssetsAtomically(ActionAgreement a, IReadOnlyList<AgreementStep> steps)
        {
            var pending = steps.Where(s => !_applied.Contains(Key(a, a.Clauses.Single(c => c.Id == s.ClauseId), s.Installment))).ToList();
            ReignAssetTransfer.DeliverBatch(a, pending);
            foreach (var step in pending) _applied.Add(Key(a, a.Clauses.Single(c => c.Id == step.ClauseId), step.Installment));
        }
        public void Apply(ActionAgreement a, ActionClause c, string snapshot, int installment)
        {
            string key = Key(a, c, installment); if (_applied.Contains(key)) return;
            if (c.ActionId.StartsWith("give.", StringComparison.Ordinal)) ReignAssetTransfer.Deliver(c, JObject.Parse(snapshot));
            else if (c.ActionId.StartsWith("government.", StringComparison.Ordinal)) ReignGovernmentCampaignBehavior.Instance.ApplyAgreementClause(a.Id, c,
                _agreements[a.Id].Steps.Single(s => s.ClauseId == c.Id));
#if !REIGN_EXCLUDE_COURT
            else if (ReignActionCatalog.Find(c.ActionId).Family == "court") ReignCourtCampaignBehavior.Instance.ApplyCourtAgreement(a, c,
                _agreements[a.Id].Steps.Single(s => s.ClauseId == c.Id));
#endif
            else
            {
                var expected = CapturedRecord(a, c, snapshot, installment);
                var behavior = ReignAICampaignBehavior.Instance ?? throw new InvalidOperationException("Native action execution is unavailable.");
                behavior.EnqueueAndExecuteServerActions(new[] { expected });
                var queued = behavior.Actions.FirstOrDefault(x => x.ActionId == expected.ActionId);
                if (queued == null || !JToken.DeepEquals(AuthorityFields(expected), AuthorityFields(queued)))
                    throw new InvalidOperationException("The native action queue did not retain the exact agreement step.");
            }
            _applied.Add(key);
        }
        public bool Observe(ActionAgreement a, ActionClause c, string snapshot, int installment)
        {
            if (!_applied.Contains(Key(a, c, installment))) return false;
            if (c.ActionId.StartsWith("give.", StringComparison.Ordinal)) return true;
            if (c.ActionId.StartsWith("government.", StringComparison.Ordinal))
                return ReignGovernmentCampaignBehavior.Instance.ObserveAgreementClause(c, _agreements[a.Id].Steps.Single(s => s.ClauseId == c.Id));
#if !REIGN_EXCLUDE_COURT
            if (ReignActionCatalog.Find(c.ActionId).Family == "court")
                return ReignCourtCampaignBehavior.Instance.ObserveCourtAgreement(c, _agreements[a.Id].Steps.Single(s => s.ClauseId == c.Id));
#endif
            var expected = CapturedRecord(a, c, snapshot, installment);
            if (expected.Type == ReignWorldActionType.RegularIssueCampaignOrder)
            {
                var order = ReignCampaignCommandBehavior.Instance?.Orders.FirstOrDefault(o => o.OrderId == expected.ActionId);
                if (order == null) throw new InvalidOperationException("The supervised order was not retained.");
                if (order.IsTerminal && order.Status != "completed") throw new InvalidOperationException(order.LastFailure);
                if (order.Status != "completed") return false;
                var ai = ReignAICampaignBehavior.Instance;
                var queued = ai?.Actions.FirstOrDefault(x => x.ActionId == expected.ActionId);
                if (queued == null) throw new InvalidOperationException("The supervised order's world action was not retained.");
                if (queued.Status != ReignWorldActionStatus.Completed && !ai.TryCompleteObservedCampaignOrder(queued))
                    throw new InvalidOperationException("The completed native order no longer matches its captured agreement step.");
                return true;
            }
            var record = ReignAICampaignBehavior.Instance.Actions.FirstOrDefault(x => x.ActionId == expected.ActionId);
            if (record?.IsTerminal == true && record.Status != ReignWorldActionStatus.Completed) throw new InvalidOperationException(record.FailureReason);
            return record?.Status == ReignWorldActionStatus.Completed;
        }
        public bool ConditionSatisfied(ActionClause c, double now)
        {
            if (c.ConditionKind == "" || c.ConditionKind == "day") return now >= c.DueDay;
            var actor = ReignActionService.HeroById(c.ActorId);
            var target = ReignActionService.HeroById(c.ConditionTargetId);
            if (actor == null) throw new InvalidOperationException("The agreed actor is no longer alive.");
            switch (c.ConditionKind)
            {
                case "arrival": return actor.CurrentSettlement?.StringId == c.ConditionTargetId;
                case "release": return target != null && !target.IsPrisoner;
                case "ownership": return Settlement.All.Any(s => s.StringId == c.ConditionTargetId && s.OwnerClan == actor.Clan);
                case "peace":
                case "war":
                    var kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == c.ConditionTargetId);
                    return kingdom != null && actor.MapFaction != null && FactionManager.IsAtWarAgainstFaction(actor.MapFaction, kingdom) == (c.ConditionKind == "war");
                default: throw new InvalidOperationException("Unknown condition.");
            }
        }
        public double DeliveryDays(ActionAgreement a)
        {
            var first = ReignActionService.HeroById(a.PlayerId); var second = ReignActionService.HeroById(a.NpcId);
            if (first?.PartyBelongedTo != null && first.PartyBelongedTo == second?.PartyBelongedTo || first?.CurrentSettlement != null && first.CurrentSettlement == second?.CurrentSettlement) return 0;
            if (!a.Clauses.Any(c => c.ActionId.StartsWith("give.", StringComparison.Ordinal))) return 0;
            return ReignBeta.Integration.ReignServerClient.ActionDeliveryDays(first, second, ReignActionService.Now);
        }
        public void SaveJournal(AgreementJournal journal) { _agreements[journal.Agreement.Id] = journal; }
    }
}
