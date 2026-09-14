using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using ReignBeta.Government;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Integration
{
    public sealed class ReignCourtServerResponse
    {
        public bool Ok;
        public string Error = string.Empty;
        public long Revision;
        public JObject Raw = new JObject();
        public JArray NativeActions = new JArray();
    }

    public static partial class ReignCourtServerClient
    {
        private static readonly HttpClient Client = new HttpClient(new Reign.Core.Contracts.Platform.ReignProtocolHandler(new HttpClientHandler { UseProxy = false })) { Timeout = TimeSpan.FromSeconds(90) };

        public static Task<ReignCourtServerResponse> OpenSessionAsync(CourtSession session, long expectedRevision, string commandId, JObject nativeSnapshot)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["sessionId"] = session?.SessionId ?? string.Empty;
            payload["authority"] = (session?.Authority ?? ReignCourtAuthority.Royal).ToString().ToLowerInvariant();
            payload["scope"] = (session?.Scope ?? ReignCourtScope.Local).ToString().ToLowerInvariant();
            payload["capitalSettlementStringId"] = ReignCourtCampaignBehavior.Instance?.CurrentCapital?.StringId ?? string.Empty;
            payload["playerIsKingdomRuler"] = ReignCourtCampaignBehavior.IsPlayerKingdomRuler();
            payload["hostSettlementStringId"] = session?.HostSettlementStringId ?? string.Empty;
            payload["lastAgendaDay"] = session?.LastAgendaDay ?? -1;
            payload["nativeSnapshot"] = nativeSnapshot ?? new JObject();
            return PostAsync("/court/session/open", payload);
        }

        public static Task<ReignCourtServerResponse> TickSessionAsync(CourtSession session, long expectedRevision, string commandId, JObject nativeSnapshot)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["sessionId"] = session?.SessionId ?? string.Empty;
            payload["state"] = SessionStateName(session?.State ?? ReignCourtSessionState.Inactive);
            payload["activeMatterId"] = session?.ActiveMatterId ?? string.Empty;
            payload["timeMode"] = session?.TimeMode ?? 0;
            payload["interruptionReason"] = session?.InterruptionReason ?? string.Empty;
            payload["nativeSnapshot"] = nativeSnapshot ?? new JObject();
            return PostAsync("/court/session/tick", payload);
        }

        public static Task<ReignCourtServerResponse> CloseSessionAsync(CourtSession session, long expectedRevision, string commandId, string reason)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["sessionId"] = session?.SessionId ?? string.Empty;
            payload["reason"] = reason ?? string.Empty;
            return PostAsync("/court/session/close", payload);
        }

        public static Task<ReignCourtServerResponse> BuildAgendaAsync(CourtSession session, long expectedRevision, string commandId, JArray candidateMatters)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["sessionId"] = session?.SessionId ?? string.Empty;
            payload["agendaDay"] = session?.LastAgendaDay ?? (int)Math.Floor(CampaignTime.Now.ToDays);
            payload["candidateMatters"] = candidateMatters ?? new JArray();
            return PostAsync("/court/agenda/build", payload);
        }

        public static Task<ReignCourtServerResponse> GetHomeAsync()
        {
            return PostAsync("/court/home", BasePayload(string.Empty, 0));
        }

        public static Task<ReignCourtServerResponse> ResolveCastleChatPromptsAsync(
            string culture,
            string room,
            string ownershipMode,
            string settlementName,
            string settlementOwnerName,
            string realmRulerName,
            string playerName)
        {
            JObject payload = BasePayload(string.Empty, 0);
            payload["culture"] = culture ?? "generic";
            payload["room"] = room ?? "main_hall";
            payload["ownershipMode"] = ownershipMode ?? "player_ruled";
            payload["settlementName"] = settlementName ?? string.Empty;
            payload["settlementOwnerName"] = settlementOwnerName ?? string.Empty;
            payload["realmRulerName"] = realmRulerName ?? string.Empty;
            payload["playerName"] = playerName ?? string.Empty;
            return PostAsync("/api/castle-chat/prompts", payload);
        }

        public static Task<ReignCourtServerResponse> GetTabAsync(string tab)
        {
            JObject payload = BasePayload(string.Empty, 0);
            payload["tab"] = tab ?? "court";
            return PostAsync("/court/view", payload);
        }

        public static Task<ReignCourtServerResponse> StartMatterAsync(string matterId, long expectedRevision, string commandId)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["matterId"] = matterId ?? string.Empty;
            return PostAsync("/court/matter/start", payload);
        }

        public static Task<ReignCourtServerResponse> ResolveMatterAsync(string matterId, string optionId, string termsHash, long expectedRevision, string commandId, string executionStatus = null, JObject nativeReceipt = null)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["matterId"] = matterId ?? string.Empty;
            payload["optionId"] = optionId ?? string.Empty;
            payload["termsHash"] = termsHash ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(executionStatus)) payload["executionStatus"] = executionStatus;
            if (nativeReceipt != null) payload["nativeReceipt"] = nativeReceipt;
            return PostAsync("/court/matter/resolve", payload);
        }

        public static Task<ReignCourtServerResponse> AssignOfficeAsync(ReignCourtOffice office, Hero hero, long expectedRevision, string commandId, JObject eligibility)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["office"] = office.ToString().ToLowerInvariant();
            payload["heroStringId"] = hero?.StringId ?? string.Empty;
            payload["eligibility"] = eligibility ?? new JObject();
            return PostAsync("/court/offices/assign", payload);
        }

        public static Task<ReignCourtServerResponse> DismissOfficeAsync(ReignCourtOffice office, long expectedRevision, string commandId, string reason)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["office"] = office.ToString().ToLowerInvariant();
            payload["reason"] = reason ?? "dismissed_by_ruler";
            return PostAsync("/court/offices/dismiss", payload);
        }

        public static Task<ReignCourtServerResponse> RequestRoyalCouncilAdviceAsync(string councilSessionId, string turnId,
            string role, Hero speaker, string playerText, bool isOpening, bool expand, JObject domainPacket,
            JArray transcript, JArray presentAdvisorIds)
        {
            JObject payload = BasePayload(string.Empty, 0);
            payload["councilSessionId"] = councilSessionId ?? string.Empty;
            payload["turnId"] = turnId ?? string.Empty;
            payload["role"] = role ?? string.Empty;
            payload["playerText"] = playerText ?? string.Empty;
            payload["isOpening"] = isOpening;
            payload["expand"] = expand;
            payload["domainPacket"] = domainPacket ?? new JObject();
            payload["transcript"] = transcript ?? new JArray();
            payload["presentAdvisorIds"] = presentAdvisorIds ?? new JArray();
            payload["speaker"] = new JObject
            {
                ["characterId"] = speaker?.StringId ?? string.Empty,
                ["name"] = speaker?.Name?.ToString() ?? string.Empty,
                ["cultureId"] = speaker?.Culture?.StringId ?? string.Empty,
                ["clanId"] = speaker?.Clan?.StringId ?? string.Empty
            };
            return PostAsync("/court/royal-council/respond", payload);
        }

        public static Task<ReignCourtServerResponse> RequestRulerPetitionReactionAsync(
            ReignDocketPetition petition, string phase, string outcome, string requestTerms,
            string turnId, string playerText, JArray transcript)
        {
            JObject payload = BasePayload(string.Empty, 0);
            payload["petitionId"] = petition?.PetitionId ?? string.Empty;
            payload["transcriptId"] = petition?.TranscriptId ?? string.Empty;
            payload["turnId"] = turnId ?? string.Empty;
            payload["phase"] = phase ?? "opening";
            payload["outcome"] = outcome ?? string.Empty;
            payload["playerText"] = playerText ?? string.Empty;
            payload["transcript"] = transcript ?? new JArray();
            payload["petitionerHeroId"] = petition?.PetitionerHeroId ?? string.Empty;
            payload["petitionerName"] = petition?.PetitionerName ?? string.Empty;
            payload["kind"] = petition?.Kind.ToString() ?? string.Empty;
            payload["severity"] = petition?.Severity.ToString() ?? string.Empty;
            payload["problem"] = petition?.ProblemSummary ?? string.Empty;
            payload["targetSettlementName"] = petition?.TargetSettlementName ?? string.Empty;
            payload["requestTerms"] = requestTerms ?? string.Empty;
            payload["danger"] = petition?.DangerLabel ?? string.Empty;
            return PostAsync("/court/ruler-petition/respond", payload);
        }

        public static Task<ReignCourtServerResponse> AssignRegentAsync(Hero hero, long expectedRevision, string commandId, JObject eligibility)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["heroStringId"] = hero?.StringId ?? string.Empty;
            payload["eligibility"] = eligibility ?? new JObject();
            return PostAsync("/court/regent/assign", payload);
        }

        public static Task<ReignCourtServerResponse> DismissRegentAsync(long expectedRevision, string commandId, string reason)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["reason"] = reason ?? "dismissed_by_ruler";
            return PostAsync("/court/regent/dismiss", payload);
        }

        public static Task<ReignCourtServerResponse> AssignAmbassadorAsync(Hero hero, Kingdom target, string missionType, float travelDays, long expectedRevision, string commandId, JObject eligibility)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["heroStringId"] = hero?.StringId ?? string.Empty;
            payload["targetKingdomStringId"] = target?.StringId ?? string.Empty;
            payload["missionType"] = missionType ?? "public_information";
            payload["travelDays"] = travelDays;
            payload["eligibility"] = eligibility ?? new JObject();
            return PostAsync("/court/ambassadors/assign", payload);
        }

        public static Task<ReignCourtServerResponse> SetAmbassadorMissionAsync(AmbassadorPosting posting, string missionType, JObject missionTerms, string termsHash, long expectedRevision, string commandId)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["postingId"] = posting?.PostingId ?? string.Empty;
            payload["missionType"] = missionType ?? "public_information";
            payload["missionTerms"] = missionTerms ?? new JObject();
            payload["termsHash"] = termsHash ?? string.Empty;
            return PostAsync("/court/ambassadors/mission", payload);
        }

        public static Task<ReignCourtServerResponse> RecallAmbassadorAsync(AmbassadorPosting posting, long expectedRevision, string commandId)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["postingId"] = posting?.PostingId ?? string.Empty;
            return PostAsync("/court/ambassadors/recall", payload);
        }

        public static Task<ReignCourtServerResponse> EstablishForeignAmbassadorAsync(
            Kingdom origin, Hero ruler, Settlement capital, int relationWithRuler, JArray candidates, string commandId, bool eventGenerated = false)
        {
            JObject payload = BasePayload(commandId, 0);
            payload["originKingdomStringId"] = origin?.StringId ?? string.Empty;
            payload["originKingdomName"] = origin?.Name?.ToString() ?? string.Empty;
            payload["originRulerHeroStringId"] = ruler?.StringId ?? string.Empty;
            payload["originRulerName"] = ruler?.Name?.ToString() ?? string.Empty;
            payload["relationWithRuler"] = relationWithRuler;
            payload["eventGenerated"] = eventGenerated;
            payload["atWar"] = origin != null && Clan.PlayerClan?.Kingdom != null
                && FactionManager.IsAtWarAgainstFaction(Clan.PlayerClan.Kingdom, origin);
            payload["capitalSettlementStringId"] = capital?.StringId ?? string.Empty;
            payload["capitalSettlementName"] = capital?.Name?.ToString() ?? string.Empty;
            payload["candidates"] = candidates ?? new JArray();
            payload["originGovernment"] = ReignGovernmentCampaignBehavior.Instance?.BuildPublicSnapshot(origin)
                ?? new JObject { ["available"] = false, ["authoritative"] = true };
            return PostAsync("/court/foreign-ambassadors/establish", payload);
        }

        public static Task<ReignCourtServerResponse> DismissForeignAmbassadorAsync(ForeignAmbassadorPosting posting, string commandId)
        {
            JObject payload = BasePayload(commandId, posting?.Revision ?? 0);
            payload["postingId"] = posting?.PostingId ?? string.Empty;
            payload["reason"] = "dismissed";
            return PostAsync("/court/foreign-ambassadors/dismiss", payload);
        }

        public static Task<ReignCourtServerResponse> EndForeignAmbassadorAsync(ForeignAmbassadorPosting posting, string reason)
        {
            JObject payload = BasePayload("foreign_ambassador_end_" + Guid.NewGuid().ToString("N"), posting?.Revision ?? 0);
            payload["postingId"] = posting?.PostingId ?? string.Empty;
            payload["reason"] = reason ?? "ended";
            return PostAsync("/court/foreign-ambassadors/end", payload);
        }

        public static Task<ReignCourtServerResponse> UpdateForeignAmbassadorLocationAsync(ForeignAmbassadorPosting posting, string reason)
        {
            JObject payload = BasePayload("foreign_ambassador_location_" + Guid.NewGuid().ToString("N"), posting?.Revision ?? 0);
            payload["postingId"] = posting?.PostingId ?? string.Empty;
            payload["status"] = posting?.Status ?? string.Empty;
            payload["capitalSettlementStringId"] = posting?.CapitalSettlementStringId ?? string.Empty;
            payload["shelterSettlementStringId"] = posting?.ShelterSettlementStringId ?? string.Empty;
            payload["reason"] = reason ?? string.Empty;
            return PostAsync("/court/foreign-ambassadors/location", payload);
        }

        public static Task<ReignCourtServerResponse> RefreshForeignAmbassadorAuthorityAsync(ForeignAmbassadorPosting posting, Hero ruler)
        {
            JObject payload = BasePayload("foreign_ambassador_authority_" + Guid.NewGuid().ToString("N"), posting?.Revision ?? 0);
            payload["postingId"] = posting?.PostingId ?? string.Empty;
            payload["originRulerHeroStringId"] = ruler?.StringId ?? string.Empty;
            payload["originRulerName"] = ruler?.Name?.ToString() ?? string.Empty;
            payload["charm"] = posting?.Charm ?? 0;
            payload["rulerTrust"] = posting?.RulerTrust ?? 0;
            payload["originGovernment"] = ReignGovernmentCampaignBehavior.Instance?.BuildPublicSnapshot(ruler?.Clan?.Kingdom)
                ?? new JObject { ["available"] = false, ["authoritative"] = true };
            return PostAsync("/court/foreign-ambassadors/authority", payload);
        }

        public static Task<ReignCourtServerResponse> TickForeignAmbassadorsAsync(int dayIndex)
        {
            JObject payload = BasePayload("foreign_ambassador_tick_" + dayIndex, 0);
            payload["worldSnapshot"] = ReignServerClient.BuildDiplomacyWorldSnapshot();
            return PostAsync("/court/foreign-ambassadors/tick", payload);
        }

        public static Task<ReignCourtServerResponse> RespondToForeignAmbassadorCounterofferAsync(string referralId, string termsHash, bool accept)
        {
            JObject payload = BasePayload("foreign_ambassador_counter_" + referralId + "_" + (accept ? "accept" : "refuse"), 0);
            payload["referralId"] = referralId ?? string.Empty;
            payload["termsHash"] = termsHash ?? string.Empty;
            payload["accept"] = accept;
            payload["worldSnapshot"] = ReignServerClient.BuildDiplomacyWorldSnapshot();
            return PostAsync("/court/foreign-ambassadors/referrals/respond", payload);
        }

        public static Task<ReignCourtServerResponse> RunCapitalAmbassadorServerMatrixAsync(string runId)
        {
            JObject payload = BasePayload("capital_ambassador_server_matrix_" + (runId ?? string.Empty), 0);
            payload["runId"] = runId ?? string.Empty;
            payload["confirmation"] = "run Reign capital ambassador server matrix";
            return PostAsync("/court/foreign-ambassadors/test", payload);
        }

        public static Task<ReignCourtServerResponse> StartIntelligenceAsync(string type, string targetType, string targetId, int goldCost, float influenceCost, float duration, float risk, float reportReliability, string commandId)
        {
            JObject payload = BasePayload(commandId, 0);
            payload["operationType"] = type ?? string.Empty;
            payload["targetType"] = targetType ?? string.Empty;
            payload["targetStringId"] = targetId ?? string.Empty;
            payload["goldCost"] = goldCost;
            payload["influenceCost"] = influenceCost;
            payload["durationDays"] = duration;
            payload["risk"] = risk;
            payload["officerReportReliability"] = reportReliability;
            return PostAsync("/court/intelligence/start", payload);
        }

        public static Task<ReignCourtServerResponse> CancelIntelligenceAsync(IntelligenceOperation operation, long expectedRevision, string commandId)
        {
            JObject payload = BasePayload(commandId, expectedRevision);
            payload["operationId"] = operation?.OperationId ?? string.Empty;
            return PostAsync("/court/intelligence/cancel", payload);
        }

        public static Task<ReignCourtServerResponse> UpsertObligationAsync(string obligationId, string obligationType, Hero owedBy, Hero owedTo,
            string description, float dueDay, JObject terms, JObject breachRule, string visibility, long expectedRevision, string commandId)
        {
            JObject payload=BasePayload(commandId,expectedRevision);
            payload["obligationId"]=obligationId??string.Empty;payload["obligationType"]=obligationType??"promise";
            payload["owedBy"]=owedBy?.StringId??string.Empty;payload["owedTo"]=owedTo?.StringId??string.Empty;payload["description"]=description??string.Empty;
            payload["dueDay"]=dueDay;payload["terms"]=terms??new JObject();payload["breachRule"]=breachRule??new JObject();payload["visibility"]=visibility??"private";
            payload["knownBy"]=new JArray(new[]{owedBy?.StringId??string.Empty,owedTo?.StringId??string.Empty}.Where(x=>!string.IsNullOrWhiteSpace(x)));
            return PostAsync("/court/obligations/upsert",payload);
        }

        public static Task<ReignCourtServerResponse> ResolveObligationAsync(CourtObligation obligation, string status, JObject resolution, string commandId)
        {
            JObject payload=BasePayload(commandId,obligation?.Revision??0);
            payload["obligationId"]=obligation?.ObligationId??string.Empty;payload["termsHash"]=obligation?.TermsHash??string.Empty;
            payload["status"]=status??"resolved";payload["resolution"]=resolution??new JObject();
            return PostAsync("/court/obligations/resolve",payload);
        }

        public static Task<ReignCourtServerResponse> UpsertPlotAsync(string plotId, string plotType, Hero director, string targetId, float dueDay,
            JObject terms, string visibility, long expectedRevision, string commandId)
        {
            JObject payload=BasePayload(commandId,expectedRevision);
            payload["plotId"]=plotId??string.Empty;payload["plotType"]=plotType??"scheme";payload["directorHeroStringId"]=director?.StringId??string.Empty;
            payload["targetStringId"]=targetId??string.Empty;payload["dueDay"]=dueDay;payload["terms"]=terms??new JObject();payload["visibility"]=visibility??"private";
            payload["knownBy"]=new JArray(new[]{director?.StringId??string.Empty,Hero.MainHero?.StringId??string.Empty}.Where(x=>!string.IsNullOrWhiteSpace(x)));
            return PostAsync("/court/plots/upsert",payload);
        }

        public static Task<ReignCourtServerResponse> ValidateWorldActionAsync(ReignWorldActionRecord action, string commandId)
        {
            JObject encoded = action == null ? new JObject() : JObject.FromObject(action);
            JObject payload = BasePayload(commandId, 0);
            payload["worldAction"] = encoded;
            payload["termsHash"] = ReignCourtTerms.Hash(encoded.ToString(Formatting.None));
            return PostAsync("/court/world-action/validate", payload);
        }

        public static Task<ReignCourtServerResponse> ProposeDiplomaticActionAsync(ReignWorldActionRecord action, string commandId)
        {
            JObject encoded=action==null?new JObject():JObject.FromObject(action);
            encoded["command"]=DiplomacyCommand(action?.Type??ReignWorldActionType.Unknown);
            try{encoded["terms"]=JObject.Parse(string.IsNullOrWhiteSpace(action?.TermsJson)?"{}":action.TermsJson);}catch{encoded["terms"]=new JObject();}
            JObject payload=BasePayload(commandId,0);
            payload["worldAction"]=encoded;
            payload["worldSnapshot"]=ReignServerClient.BuildDiplomacyWorldSnapshot();
            payload["termsHash"]=ReignCourtTerms.Hash(encoded.ToString(Formatting.None));
            return PostAsync("/court/diplomacy/propose",payload);
        }

        private static string DiplomacyCommand(ReignWorldActionType type)
        {
            switch(type)
            {
                case ReignWorldActionType.DiplomacyDeclareWar:return "declare_war";
                case ReignWorldActionType.DiplomacyMakePeace:return "make_peace";
                case ReignWorldActionType.DiplomacyOfferTributePeace:return "offer_tribute_peace";
                case ReignWorldActionType.DiplomacyDemandReparationsPeace:return "demand_reparations_peace";
                case ReignWorldActionType.DiplomacyDemandSettlementPeace:return "demand_settlement_peace";
                case ReignWorldActionType.DiplomacyDemandSurrenderPeace:return "demand_surrender_peace";
                case ReignWorldActionType.DiplomacySignTradeAgreement:return "sign_trade_agreement";
                case ReignWorldActionType.DiplomacySignNonAggressionPact:return "sign_non_aggression_pact";
                case ReignWorldActionType.DiplomacySignAlliance:return "sign_alliance";
                case ReignWorldActionType.DiplomacySignDefensivePact:return "sign_defensive_pact";
                case ReignWorldActionType.DiplomacyExchangePrisoners:return "exchange_prisoners";
                case ReignWorldActionType.DiplomacyRansomPackage:return "ransom_package";
                case ReignWorldActionType.DiplomacyCaravanProtectionAgreement:return "caravan_protection_agreement";
                case ReignWorldActionType.DiplomacySupplyAgreement:return "supply_agreement";
                case ReignWorldActionType.DiplomacyLoanOrSubsidy:return "loan_or_subsidy";
                case ReignWorldActionType.DiplomacyGuaranteeIndependence:return "guarantee_independence";
                case ReignWorldActionType.DiplomacyPackage:return "diplomatic_package";
                default:return type.ToString();
            }
        }

        public static Task<ReignCourtServerResponse> ValidateSupplyTransferAsync(ReignSupplyTransferRequest request)
        {
            JObject transfer = new JObject
            {
                ["sourceSettlementStringId"] = request?.SourceSettlementStringId ?? string.Empty,
                ["targetSettlementStringId"] = request?.TargetSettlementStringId ?? string.Empty,
                ["itemStringId"] = request?.ItemStringId ?? string.Empty,
                ["amount"] = request?.ItemAmount ?? 0,
                ["goldAmount"] = request?.GoldAmount ?? 0,
                ["goldPayerKind"] = request?.GoldPayerKind ?? string.Empty,
                ["goldPayerStringId"] = request?.GoldPayerStringId ?? string.Empty,
                ["goldRecipientKind"] = request?.GoldRecipientKind ?? string.Empty,
                ["goldRecipientStringId"] = request?.GoldRecipientStringId ?? string.Empty,
                ["vassalConsentGranted"] = request?.VassalConsentGranted ?? false
            };
            JObject payload = BasePayload(request?.CommandId ?? string.Empty, 0);
            payload["transfer"] = transfer;
            payload["termsHash"] = ReignCourtTerms.Hash(transfer.ToString(Formatting.None));
            return PostAsync("/court/supply/validate", payload);
        }

        public static Task<ReignCourtServerResponse> ValidateNativeActionAsync(JObject action, string commandId)
        {
            JObject nativeAction = action == null ? new JObject() : (JObject)action.DeepClone();
            JObject payload = BasePayload(commandId, 0);
            payload["nativeAction"] = nativeAction;
            payload["termsHash"] = ReignCourtTerms.Hash(nativeAction.ToString(Formatting.None));
            return PostAsync("/court/native/validate", payload);
        }

        private static JObject BasePayload(string commandId, long expectedRevision)
        {
            return new JObject
            {
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["commandId"] = commandId ?? string.Empty,
                ["expectedRevision"] = expectedRevision,
                ["worldDay"] = CampaignTime.Now.ToDays,
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["playerClanStringId"] = Clan.PlayerClan?.StringId ?? string.Empty,
                ["playerKingdomStringId"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                ["playerIsKingdomRuler"] = ReignCourtCampaignBehavior.IsPlayerKingdomRuler()
                , ["sessionId"] = ReignCourtCampaignBehavior.Instance?.Session?.SessionId ?? string.Empty
                , ["courtScope"] = (ReignCourtCampaignBehavior.Instance?.Session?.Scope ?? ReignCourtScope.Local).ToString().ToLowerInvariant()
                , ["hostSettlementStringId"] = ReignCourtCampaignBehavior.Instance?.Session?.HostSettlementStringId ?? string.Empty
                , ["capitalSettlementStringId"] = ReignCourtCampaignBehavior.Instance?.CurrentCapital?.StringId ?? string.Empty
            };
        }

        private static async Task<ReignCourtServerResponse> PostAsync(string route, JObject payload)
        {
            try { await ReignSaveSyncCoordinator.WaitForReadyAsync(route).ConfigureAwait(false); }
            catch (Exception ex) { return new ReignCourtServerResponse { Ok = false, Error = ex.Message }; }
            if (!ReignServerEndpoint.TryBeginRequest(out string unavailable))
            {
                return new ReignCourtServerResponse { Ok = false, Error = unavailable };
            }

            try
            {
                string json = (payload ?? new JObject()).ToString(Formatting.None);
                Exception lastTransportFailure = null;
                foreach (string baseUrl in ReignServerEndpoint.CandidateBaseUrls())
                {
                    string url = baseUrl.TrimEnd('/') + (route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route);
                    Stopwatch timer = Stopwatch.StartNew();
                    try
                    {
                        using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                        using (HttpResponseMessage response = await Client.PostAsync(url, content).ConfigureAwait(false))
                        {
                            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            timer.Stop();
                            ReignServerEndpoint.ReportSuccess(baseUrl);
                            if (!response.IsSuccessStatusCode) return new ReignCourtServerResponse { Ok = false, Error = "HTTP " + (int)response.StatusCode + ": " + body };
                            JObject result = JObject.Parse(body);
                            return new ReignCourtServerResponse
                            {
                                Ok = result.Value<bool?>("ok") == true,
                                Error = result.Value<string>("error") ?? string.Empty,
                                Revision = ReadNumericRevision(result),
                                NativeActions = result["nativeActions"] as JArray ?? new JArray(),
                                Raw = result
                            };
                        }
                    }
                    catch (Exception ex) when (ReignServerEndpoint.IsTransportFailure(ex))
                    {
                        lastTransportFailure = ex;
                    }
                }

                ReignServerEndpoint.ReportTransportFailure();
                return new ReignCourtServerResponse { Ok = false, Error = lastTransportFailure?.Message ?? "Local Bannerlord Reign server is unavailable." };
            }
            catch (Exception ex)
            {
                return new ReignCourtServerResponse { Ok = false, Error = ex.Message };
            }
        }

        private static long ReadNumericRevision(JObject result)
        {
            if (result == null) return 0;
            foreach (string name in new[] { "revision", "currentRevision" })
            {
                JToken token = result[name];
                if (token == null || token.Type == JTokenType.Null) continue;
                if (token.Type == JTokenType.Integer) return token.Value<long>();
                if (long.TryParse(token.ToString(), out long revision)) return revision;
            }
            return 0;
        }

        private static string SessionStateName(ReignCourtSessionState state)
        {
            switch (state)
            {
                case ReignCourtSessionState.PausedForMatter: return "paused_for_matter";
                case ReignCourtSessionState.InConversation: return "in_conversation";
                default: return state.ToString().ToLowerInvariant();
            }
        }
    }
}
