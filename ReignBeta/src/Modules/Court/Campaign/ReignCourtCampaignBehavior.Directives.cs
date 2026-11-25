using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.Library;
using ReignBeta.Characters.Integration;
using ReignBeta.Characters.World;
using ReignBeta.Court.FamilyChambers;
using ReignBeta.Court.Integration;
using ReignBeta.Court.UI;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.Spymaster.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Court.Campaign
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        public bool TryHandleRegentLetter(ReignLetter letter, Action onClosed)
        {
            if (letter == null || !string.Equals(letter.Source, "court_regent", StringComparison.OrdinalIgnoreCase)) return false;
            const string prefix = "court_regent:";
            if (string.IsNullOrWhiteSpace(letter.Reason) || !letter.Reason.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string matterId = letter.Reason.Substring(prefix.Length);
            CourtMatter matter = _matters.FirstOrDefault(x => x.MatterId == matterId && !x.IsTerminal);
            if (matter == null) return false;
            JArray options;
            try { options = JArray.Parse(string.IsNullOrWhiteSpace(matter.DecisionOptionsJson) ? "[]" : matter.DecisionOptionsJson); }
            catch { options = new JArray(); }
            List<InquiryElement> elements = options.OfType<JObject>().Select(x => new InquiryElement(
                x.Value<string>("optionId") ?? string.Empty,
                (x.Value<string>("label") ?? "Reply") + (string.IsNullOrWhiteSpace(x.Value<string>("description")) ? string.Empty : " â€” " + x.Value<string>("description")),
                null)).ToList();
            if (elements.Count == 0) return false;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "A Major Matter from Your Regent",
                (letter.Body ?? matter.Summary) + "\n\nYour reply directs the outcome, but answering by letter does not count as personal presence at court.",
                elements,true,1,1,"Send Reply","Leave Unanswered",
                selected =>
                {
                    InquiryElement chosen = selected?.FirstOrDefault();
                    JObject option = options.OfType<JObject>().FirstOrDefault(x => string.Equals(x.Value<string>("optionId"), chosen?.Identifier as string, StringComparison.OrdinalIgnoreCase));
                    if (option != null)
                    {
                        _ = ResolveRegentReplyAsync(matter, option);
                    }
                    onClosed?.Invoke();
                },
                selected => onClosed?.Invoke(),string.Empty,false),true,false);
            return true;
        }

        private async Task ResolveRegentReplyAsync(CourtMatter matter, JObject option)
        {
            string error = await ResolveDecisionAsync(matter.MatterId, option.Value<string>("optionId") ?? string.Empty,
                option.Value<string>("termsHash") ?? string.Empty, true, "regent_mail").ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => InformationManager.DisplayMessage(new InformationMessage(string.IsNullOrWhiteSpace(error)
                ? "[Bannerlord Reign] Your coded reply was sent to the Regent. Presence credit was not granted."
                : "[Bannerlord Reign] The Regent's matter remains pending: " + error))).ConfigureAwait(false);
        }

        public async Task<string> AssignOfficeAsync(ReignCourtOffice office, Hero hero, string agreementOperationId = null)
        {
            var agreementContinuation = CourtAgreementContinuation(agreementOperationId);
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            CourtOfficeAssignment active = _offices.FirstOrDefault(x => x.IsActive && x.Office == office);
            if (office == ReignCourtOffice.Spymaster && active != null
                && !string.Equals(active.HeroStringId, hero?.StringId, StringComparison.OrdinalIgnoreCase)
                && !CanChangeSpymasterOffice(out string activeMissionError)) return activeMissionError;
            if (!ReignCourtOfficeService.IsEligible(hero, _session.Authority, _offices.Where(x => x != active), out string eligibilityError)) return eligibilityError;
            long expectedRevision = active?.Revision ?? 0;
            string commandId = agreementOperationId ?? "court_office_assign_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.AssignOfficeAsync(office, hero, expectedRevision, commandId,
                NativeHeroEligibility(hero)).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            return await ReignMainThread.InvokeAsync(() =>
            {
                agreementContinuation();
                if (IsNobleVisitorReserved(hero)) return "This noble is committed to a court visit. Assign an office after the visit ends.";
                if (!ReignCourtOfficeService.TryAssign(_offices, office, hero, _session.Authority, commandId, expectedRevision, CurrentDayFloat(), out CourtOfficeAssignment assignment, out string error)) return error;
                assignment.AssignmentId = response.Raw.Value<string>("recordId") ?? assignment.AssignmentId;
                assignment.Revision = response.Revision;
                _session.Revision++;
                StateChanged?.Invoke();
                return string.Empty;
            }).ConfigureAwait(false);
        }

        public async Task<string> DismissOfficeAsync(CourtOfficeAssignment assignment, string agreementOperationId = null)
        {
            var agreementContinuation = CourtAgreementContinuation(agreementOperationId);
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (assignment == null || !assignment.IsActive) return "That office is already vacant.";
            if (assignment.Office == ReignCourtOffice.Spymaster && !CanChangeSpymasterOffice(out string activeMissionError))
                return activeMissionError;
            string commandId = agreementOperationId ?? "court_office_dismiss_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.DismissOfficeAsync(assignment.Office, assignment.Revision, commandId, "dismissed_by_ruler").ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            return await ReignMainThread.InvokeAsync(() =>
            {
                agreementContinuation();
                if (!ReignCourtOfficeService.TryDismiss(_offices, assignment.Office, commandId, assignment.Revision, CurrentDayFloat(), "dismissed_by_ruler", out string error)) return error;
                assignment.Revision = response.Revision;
                _session.Revision++;
                StateChanged?.Invoke();
                return string.Empty;
            }).ConfigureAwait(false);
        }

        public async Task<string> AssignRegentAsync(Hero hero, string agreementOperationId = null)
        {
            var agreementContinuation = CourtAgreementContinuation(agreementOperationId);
            if (_session?.Authority != ReignCourtAuthority.Royal) return "A Regent may be appointed only by a kingdom ruler.";
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            JObject eligibility = NativeHeroEligibility(hero);
            if (eligibility.Value<bool?>("isAdult") != true || eligibility.Value<bool?>("isFree") != true || eligibility.Value<bool?>("inCourtScope") != true)
                return "The Regent must be an adult, free, active member of the royal court scope.";
            CourtRegentAssignment active = ActiveRegent;
            long expectedRevision = active?.Revision ?? 0;
            string commandId = agreementOperationId ?? "court_regent_assign_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.AssignRegentAsync(hero, expectedRevision, commandId, eligibility).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                agreementContinuation();
                if (active != null)
                {
                    active.IsActive = false;
                    active.DismissedDay = CurrentDayFloat();
                    active.DismissalReason = "replaced";
                    active.Revision++;
                }
                _regents.Add(new CourtRegentAssignment
                {
                    AssignmentId = response.Raw.Value<string>("recordId") ?? "regent_" + Guid.NewGuid().ToString("N"),
                    HeroStringId = hero.StringId,AssignedDay = CurrentDayFloat(),Revision = response.Revision,LastCommandId = commandId
                });
                _session.Revision++; StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> DismissRegentAsync(string agreementOperationId = null)
        {
            var agreementContinuation = CourtAgreementContinuation(agreementOperationId);
            CourtRegentAssignment active = ActiveRegent;
            if (active == null) return "No Regent is assigned.";
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            string commandId = agreementOperationId ?? "court_regent_dismiss_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.DismissRegentAsync(active.Revision, commandId, "dismissed_by_ruler").ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                agreementContinuation();
                active.IsActive = false;active.DismissedDay = CurrentDayFloat();active.DismissalReason = "dismissed_by_ruler";
                active.Revision = response.Revision;active.LastCommandId = commandId;_session.Revision++;StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> AssignAmbassadorAsync(Hero hero, Kingdom target, string missionType, string agreementOperationId = null)
        {
            var agreementContinuation = CourtAgreementContinuation(agreementOperationId);
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (_session.Authority != ReignCourtAuthority.Royal) return "Ambassador postings require royal court authority.";
            if (_ambassadors.Count(x => x.Status != "recalled" && x.Status != "replaced") >= 3) return "All three ambassador postings are occupied.";
            JObject eligibility=NativeHeroEligibility(hero);
            if(eligibility.Value<bool?>("isAdult")!=true||eligibility.Value<bool?>("isFree")!=true||eligibility.Value<bool?>("inCourtScope")!=true)return "The ambassador must be an adult, free, active hero within royal court scope.";
            if (_ambassadors.Any(x => x.HeroStringId == hero.StringId && x.Status != "recalled" && x.Status != "replaced")) return "That hero already holds an ambassador posting.";
            string commandId = agreementOperationId ?? "court_ambassador_assign_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.AssignAmbassadorAsync(hero, target, missionType, 3f, 0, commandId, eligibility).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                agreementContinuation();
                _ambassadors.Add(new AmbassadorPosting
                {
                    PostingId = response.Raw.Value<string>("recordId") ?? "ambassador_" + Guid.NewGuid().ToString("N"),
                    HeroStringId = hero.StringId,
                    TargetKingdomStringId = target.StringId,
                    MissionType = missionType ?? "public_information",
                    Status = "traveling",
                    AssignedDay = CurrentDayFloat(),
                    ArrivalDay = CurrentDayFloat() + 3f,
                    Revision = response.Revision,
                    LastCommandId = commandId
                });
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> SetAmbassadorMissionAsync(AmbassadorPosting posting, string missionType, JObject terms, string agreementOperationId = null)
        {
            var agreementContinuation = CourtAgreementContinuation(agreementOperationId);
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (posting == null || posting.Status == "recalled" || posting.Status == "replaced") return "That posting is no longer active.";
            terms = terms ?? new JObject();
            string hash = ReignCourtTerms.Hash(terms.ToString(Newtonsoft.Json.Formatting.None));
            string commandId = agreementOperationId ?? "court_ambassador_mission_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.SetAmbassadorMissionAsync(posting, missionType, terms, hash, posting.Revision, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                agreementContinuation();
                posting.MissionType = missionType;
                posting.MissionTermsJson = terms.ToString(Newtonsoft.Json.Formatting.None);
                posting.Status = "posted";
                posting.Revision = response.Revision;
                posting.LastCommandId = commandId;
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> RecallAmbassadorAsync(AmbassadorPosting posting, string agreementOperationId = null)
        {
            var agreementContinuation = CourtAgreementContinuation(agreementOperationId);
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (posting == null || posting.Status == "recalled") return "That ambassador has already been recalled.";
            string commandId = agreementOperationId ?? "court_ambassador_recall_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.RecallAmbassadorAsync(posting, posting.Revision, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                agreementContinuation();
                posting.Status = "recalled";
                posting.RecalledDay = CurrentDayFloat();
                posting.Revision = response.Revision;
                posting.LastCommandId = commandId;
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> DeliverAmbassadorProposalAsync(AmbassadorPosting posting,ReignWorldActionType type,JObject terms)
        {
            if(posting==null)return "An active ambassador posting is required.";
            Kingdom ours=Clan.PlayerClan?.Kingdom,target=Kingdom.All.FirstOrDefault(x=>x.StringId==posting.TargetKingdomStringId);
            if(ours?.Leader==null||target?.Leader==null)return "Both realms must have current rulers.";
            string missionError=await SetAmbassadorMissionAsync(posting,"deliver_proposal",new JObject{{"actionType",type.ToString()},{"terms",terms??new JObject()}}).ConfigureAwait(false);
            if(!string.IsNullOrWhiteSpace(missionError))return missionError;
            return await SubmitDiplomaticProposalAsync(new ReignWorldActionRecord
            {
                Type=type,ActorHeroStringId=ours.Leader.StringId,ActorKingdomStringId=ours.StringId,TargetHeroStringId=target.Leader.StringId,TargetKingdomStringId=target.StringId,
                Reason="The posted ambassador delivers a package authorized by the royal court.",TermsJson=(terms??new JObject()).ToString(Newtonsoft.Json.Formatting.None)
            }).ConfigureAwait(false);
        }

        public async Task<string> StartIntelligenceOperationAsync(string operationType, string targetType, string targetId, int goldCost, float influenceCost, float duration, float risk)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            CourtOfficeAssignment spymasterAssignment=_offices.FirstOrDefault(x=>x.IsActive&&x.Office==ReignCourtOffice.Spymaster);
            if (spymasterAssignment==null) return "Appoint a Spymaster before starting advanced operations.";
            ReignCourtIntelligenceQuote quote=GetIntelligenceQuote(goldCost,influenceCost,duration,risk);
            goldCost=quote.GoldCost;influenceCost=quote.InfluenceCost;duration=quote.DurationDays;risk=quote.Risk;
            if ((Hero.MainHero?.Gold ?? 0) < goldCost) return "The player clan cannot afford this operation.";
            if ((Clan.PlayerClan?.Influence ?? 0f) < influenceCost) return "The player clan lacks the required influence.";
            string commandId = "court_intelligence_start_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.StartIntelligenceAsync(operationType, targetType, targetId, goldCost, influenceCost, duration, risk, quote.ReportReliability, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (goldCost > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, goldCost, true);
                if (influenceCost > 0f) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -influenceCost);
                _operations.Add(new IntelligenceOperation
                {
                    OperationId = response.Raw.Value<string>("recordId") ?? "intel_" + Guid.NewGuid().ToString("N"),
                    OperationType = operationType,
                    TargetType = targetType,
                    TargetStringId = targetId,
                    State = ReignIntelligenceOperationState.Active,
                    GoldCost = goldCost,
                    InfluenceCost = influenceCost,
                    StartedDay = CurrentDayFloat(),
                    DueDay = CurrentDayFloat() + duration,
                    Risk = risk,
                    Revision = response.Revision,
                    LastCommandId = commandId
                });
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public ReignCourtIntelligenceQuote GetIntelligenceQuote(int goldCost,float influenceCost,float duration,float risk)
        {
            CourtOfficeAssignment assignment=_offices.FirstOrDefault(x=>x.IsActive&&x.Office==ReignCourtOffice.Spymaster);Hero hero=FindHero(assignment?.HeroStringId);
            return ReignCourtOfficePerformanceService.QuoteIntelligence(ReignCourtOfficePerformanceService.Evaluate(ReignCourtOffice.Spymaster,hero,_session),goldCost,influenceCost,duration,risk);
        }

        public async Task<string> CancelIntelligenceOperationAsync(IntelligenceOperation operation)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (operation == null || operation.State != ReignIntelligenceOperationState.Active) return "Only an active operation may be cancelled.";
            string commandId = "court_intelligence_cancel_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.CancelIntelligenceAsync(operation, operation.Revision, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                operation.State = ReignIntelligenceOperationState.Cancelled;
                operation.Revision = response.Revision;
                operation.LastCommandId = commandId;
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> SubmitWorldActionAsync(ReignWorldActionRecord action)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (action == null) return "No court plan was supplied.";
            if(action.TypeValue>0&&action.TypeValue<100)return "Diplomatic packages must be sent through the foreign-ruler proposal path.";
            action.Source = "court_player_plan";
            action.RequiresAcceptance = false;
            if (!ReignActionValidator.Validate(action, out string validationError)) return validationError;
            string commandId = "court_world_action_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.ValidateWorldActionAsync(action, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            NativeCourtExecution execution = await ExecuteNativeActionsAsync(response.NativeActions).ConfigureAwait(false);
            if (!execution.Success) return execution.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> SubmitDiplomaticProposalAsync(ReignWorldActionRecord action)
        {
            if(!_serverAvailable||!_serverSessionOpened)return MutationUnavailable();
            if(_session.Authority!=ReignCourtAuthority.Royal)return "Diplomatic proposals require royal court authority.";
            if(action==null||action.TypeValue<=0||action.TypeValue>=100)return "A supported diplomatic package is required.";
            action.Source="court_player_diplomacy";action.RequiresAcceptance=true;
            if(!ReignActionValidator.Validate(action,out string validationError))return validationError;
            string commandId="court_diplomacy_"+Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response=await ReignCourtServerClient.ProposeDiplomaticActionAsync(action,commandId).ConfigureAwait(false);
            if(!response.Ok)return response.Error;
            string responseKind=response.Raw.Value<string>("responseKind")??"refuse",reason=response.Raw.Value<string>("publicReason")??string.Empty;
            if(response.NativeActions.Count>0)
            {
                NativeCourtExecution execution=await ExecuteNativeActionsAsync(response.NativeActions).ConfigureAwait(false);
                if(!execution.Success)return execution.Error;
                await ReignMainThread.InvokeAsync(()=>{_session.Revision++;StateChanged?.Invoke();}).ConfigureAwait(false);
                return string.Empty;
            }
            await SyncServerHomeAsync().ConfigureAwait(false);
            return responseKind=="counter"?"A counteroffer was added to the court agenda: "+reason:"The foreign ruler refused: "+reason;
        }

        public async Task<string> TransferSupplyAsync(ReignSupplyTransferRequest request)
        {
            if (!ReignMainThread.IsMainThread) return CourtOwnedActionUnavailable;
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();

            CourtSession session = _session;
            if (session == null) return CourtOwnedActionUnavailable;
            var fence = ReignServerClient.RequestFence.Capture();
            var saveSync = ReignServices.SaveSync;
            string savePoint = saveSync?.CurrentSavePointId;
            string sessionId = session.SessionId;
            ReignCourtAuthority authority = session.Authority;
            bool paidSupply = request?.GoldAmount > 0;
            Hero paymentAuthor = paidSupply ? Hero.MainHero : null;
            object sitting = _courtSittingOwner;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            string hostId = session.HostSettlementStringId;
            Settlement host = string.IsNullOrEmpty(hostId) ? null : Settlement.Find(hostId);
            KingdomCapitalDesignation ReadDesignation() => _capitalDesignations.FirstOrDefault(record =>
                record != null && string.Equals(record.KingdomStringId, kingdom?.StringId, StringComparison.OrdinalIgnoreCase));
            KingdomCapitalDesignation designation = ReadDesignation();
            long capitalRevision = designation?.Revision ?? -1L;

            bool OwnsTransfer() => ReferenceEquals(this, ReignServices.Court) && _serverSessionOpened && HasRoyalCommandAccess
                && (!paidSupply || (paymentAuthor != null && ReferenceEquals(paymentAuthor, Hero.MainHero)))
                && ReferenceEquals(sitting, _courtSittingOwner)
                && ReferenceEquals(session, _session) && session.SessionId == sessionId && session.Authority == authority
                && ReferenceEquals(kingdom, Clan.PlayerClan?.Kingdom) && host?.IsTown == true && host.OwnerClan?.Kingdom == kingdom
                && session.HostSettlementStringId == hostId
                && designation?.EverDesignated == true && ReferenceEquals(designation, ReadDesignation())
                && designation.Revision == capitalRevision && designation.SettlementStringId == hostId
                && !ReignCampaignInitializationGate.IsPending && fence.IsCurrentActionPoll()
                && !ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress && !ReignSaveSyncCoordinator.IsSaveFinalizationPending
                && ReferenceEquals(saveSync, ReignServices.SaveSync)
                && string.Equals(savePoint, saveSync?.CurrentSavePointId, StringComparison.Ordinal);

            if (!OwnsTransfer()) return CourtOwnedActionUnavailable;
            if (paidSupply && (!string.Equals(request.GoldPayerKind, "hero", StringComparison.Ordinal)
                || !string.Equals(request.GoldPayerStringId, paymentAuthor.StringId, StringComparison.Ordinal)))
                return "A paid supply transfer can spend only the player's own wallet.";
            if (paidSupply)
            {
                ReignSupplyTransferResult saved = ReignCourtSupplyService.FindSavedPaidTransfer(request, _paidSupplyJournal,
                    ReignCampaignIdentity.CurrentCampaignId(), ReignServices.WorldHistory?.TimelineId ?? "main");
                if (saved != null) return saved.Success ? string.Empty : saved.Error;
                if (_appliedNativeCommandIds.Contains(request.CommandId))
                    return "A previously used native command does not have an exact paid supply receipt.";
            }
            ReignCourtServerResponse response = await ReignCourtServerClient.ValidateSupplyTransferAsync(
                request, _serverRevision, fence.SharedActivityGeneration, OwnsTransfer).ConfigureAwait(false);
            if (!response.Ok) return response.Error;

            NativeCourtExecution execution = await ExecuteNativeActionsAsync(response.NativeActions,
                requestOwner: OwnsTransfer, onSuccess: () => session.Revision++, ownedPaidSupply: paidSupply).ConfigureAwait(false);
            if (!execution.Success) return execution.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (OwnsTransfer()) StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> SubmitNativeActionAsync(JObject action)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (action == null) return "No native court action was supplied.";
            string commandId = "court_native_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.ValidateNativeActionAsync(action, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            NativeCourtExecution execution = await ExecuteNativeActionsAsync(response.NativeActions).ConfigureAwait(false);
            if (!execution.Success) return execution.Error;
            await ReignMainThread.InvokeAsync(() => { _session.Revision++; StateChanged?.Invoke(); }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> RecordGoldPromiseAsync(Hero recipient,int amount,float dueInDays)
        {
            if(!_serverAvailable||!_serverSessionOpened)return MutationUnavailable();
            if(recipient==null||recipient==Hero.MainHero||amount<=0)return "A promise requires another current hero and a positive amount.";
            string id="obligation_"+Guid.NewGuid().ToString("N"),commandId="court_obligation_"+Guid.NewGuid().ToString("N");
            JObject terms=new JObject{{"goldAmount",amount},{"currency","denars"}};
            JObject breach=new JObject{{"effect","obligation_breached"},{"reason","failed_gold_promise"}};
            ReignCourtServerResponse response=await ReignCourtServerClient.UpsertObligationAsync(id,"promise",Hero.MainHero,recipient,
                "Pay "+amount+" denars to "+recipient.Name+".",CurrentDayFloat()+Math.Max(1f,dueInDays),terms,breach,"private",0,commandId).ConfigureAwait(false);
            if(!response.Ok)return response.Error;
            await SyncServerHomeAsync().ConfigureAwait(false);
            return string.Empty;
        }

        public async Task RefreshServerTabAsync(string tab)
        {
            if(!_serverAvailable||!_serverSessionOpened)return;
            ReignCourtServerResponse response=await ReignCourtServerClient.GetTabAsync(tab).ConfigureAwait(false);if(!response.Ok)return;
            await ReignMainThread.InvokeAsync(()=>
            {
                foreach(JObject row in (response.Raw["obligations"] as JArray??new JArray()).OfType<JObject>())UpsertServerObligation(row);
                foreach(JObject row in (response.Raw["plots"] as JArray??new JArray()).OfType<JObject>())UpsertServerPlot(row);
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
        }

        public bool TrySelectDecision(string matterId, string optionId, string commandId, long expectedRevision, string termsHash, bool confirmed, out string error)
        {
            error = "Court decisions require the asynchronous server-validated command and native-receipt path.";
            return false;
        }

        public bool TryAssignOffice(ReignCourtOffice office, Hero hero, string commandId, long expectedRevision, out string error)
        {
            error = "Court office mutations require the asynchronous server-validated command path.";
            return false;
        }
    }
}
