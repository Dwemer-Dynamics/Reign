using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Integration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        internal JObject RunGovernmentTestProfile(string runId, string profile,
            JObject options, string gameInstanceId)
        {
            string id = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
            string phase = (profile ?? "preflight").Trim().ToLowerInvariant();
            options = options ?? new JObject();
            if (!TryRequireGovernmentTestSave(options, out string expectedSave,
                out string activeSave, out string saveError))
                return GovernmentTestFailure(id, phase, saveError, expectedSave, activeSave);

            switch (phase)
            {
                case "preflight": return GovernmentPreflight(id, expectedSave, activeSave);
                case "contracts": return GovernmentContracts(id);
                case "snapshot": return GovernmentSnapshot(id);
                case "hearing_compose":
                    bool composed = ReignBeta.UI.ReignGovernmentScreenManager.TryComposeHearing(
                        options.Value<string>("businessId"), options.Value<string>("playerMessage"));
                    return new JObject { ["ok"] = composed, ["profile"] = phase,
                        ["businessId"] = options.Value<string>("businessId"), ["submitted"] = false,
                        ["message"] = composed ? "The unsent composer is ready. Use the native Speak control to submit."
                            : "Open the exact interactive hearing and provide 1-1200 characters. No text was changed." };
                case "authority": return GovernmentAuthorityContracts(id);
                case "resolutions": return GovernmentResolutionContracts(id);
                case "voting": return GovernmentVotingContracts(id);
                case "pressure": return GovernmentPressureContracts(id);
                case "save_prepare": return GovernmentSavePrepare(id, options, gameInstanceId);
                case "save_verify": return GovernmentSaveVerify(id, options, gameInstanceId);
                case "case_prepare": return GovernmentCertificationCasePrepare(id, options);
                case "case_observe": return GovernmentCertificationCaseObserve(id, options);
                case "case_execute": return GovernmentCertificationCaseExecute(id, options);
                case "soak_prepare": return GovernmentCertificationSoakPrepare(id);
                case "soak_verify": return GovernmentCertificationSoakVerify(id, options);
                case "cleanup": return GovernmentCertificationCleanup(id);
                default: return GovernmentTestFailure(id, phase,
                    "Unsupported government test profile.", expectedSave, activeSave);
            }
        }

        private JObject GovernmentPreflight(string runId, string expectedSave, string activeSave)
        {
            JArray assertions = new JArray();
            List<Kingdom> kingdoms = Kingdom.All.Where(IsEligibleKingdom)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
            foreach (Kingdom kingdom in kingdoms) EnsureGovernment(kingdom);
            bool allInitialized = kingdoms.Count > 0 && kingdoms.All(kingdom =>
            {
                ReignGovernmentStateRecord state = GetGovernment(kingdom);
                List<ReignGovernmentPartyRecord> parties = GetParties(kingdom.StringId).ToList();
                List<ReignGovernmentSeatRecord> seats = GetSeats(kingdom.StringId).ToList();
                if (state == null || state.Level < 1 || state.Level > 5) return false;
                if (IsCharterPending(kingdom.StringId))
                    return parties.Count == 0 && seats.Count == 0
                        && string.IsNullOrEmpty(state.DominantPartyId) && string.IsNullOrEmpty(state.CoalitionPartyIdsCsv);
                int coalitionSeats = parties.Where(party => party.IsGoverningCoalition).Sum(party => party.SeatCount);
                return parties.Count >= 2 && parties.Count <= 4
                    && parties.All(party => ParsePlanks(party.PlanksCsv).Count >= 2
                        && ParsePlanks(party.PlanksCsv).Count <= 4)
                    && parties.Where(party => party.SeatCount > 0)
                        .All(party => !string.IsNullOrWhiteSpace(party.SpeakerHeroStringId))
                    && seats.All(seat => string.IsNullOrWhiteSpace(seat.PartyId)
                        || parties.Any(party => party.PartyId == seat.PartyId))
                    && parties.All(party => party.SeatCount == seats.Count(seat => seat.PartyId == party.PartyId))
                    && parties.Sum(party => party.SeatCount)
                        + seats.Count(seat => string.IsNullOrWhiteSpace(seat.PartyId)) == seats.Count
                    && (seats.Count == 0
                        ? parties.All(party => !party.IsDominant && !party.IsGoverningCoalition)
                            && string.IsNullOrEmpty(state.DominantPartyId) && string.IsNullOrEmpty(state.CoalitionPartyIdsCsv)
                        : parties.Count(party => party.IsDominant) == 1
                            && parties.Count(party => party.IsGoverningCoalition) >= 1
                            && coalitionSeats * 2 > seats.Count);
            });
            AddGovernmentAssertion(assertions, "government_exact_disposable_save",
                string.Equals(expectedSave, activeSave, StringComparison.OrdinalIgnoreCase),
                "The client independently matched Bannerlord's active save to the exact MCP-authorized campaign-test Current.",
                new JObject { ["expectedSaveName"] = expectedSave, ["activeSaveName"] = activeSave });
            AddGovernmentAssertion(assertions, "government_campaign_loaded",
                TaleWorlds.CampaignSystem.Campaign.Current != null,
                "A Bannerlord campaign is loaded.", null);
            AddGovernmentAssertion(assertions, "government_restored_calendar_ready",
                ReignBeta.Runtime.ReignCalendarService.IsReady
                    && kingdoms.All(kingdom => GetGovernment(kingdom).SessionProtocolVersion == GovernmentSessionRules.ProtocolVersion
                        && FormatGovernmentDate(GetGovernment(kingdom).NextMeetingDay) != "Awaiting calendar"),
                "The restored calendar supplies anchored session dates before campaign time advances.", null);
            AddGovernmentAssertion(assertions, "government_native_interception_ready", ReignGovernmentPatches.VerifyReady(),
                "Every required Government Harmony hook is installed exactly once under the Government owner.", ReignGovernmentPatches.BuildDiagnostics());
            AddGovernmentAssertion(assertions, "government_restored_constituencies_ready",
                kingdoms.Where(kingdom => !IsCharterPending(kingdom.StringId)).All(kingdom =>
                    GetSeats(kingdom.StringId).All(seat => _participation.Constituencies.Any(row =>
                        row.KingdomId == kingdom.StringId && row.Status == "active" && row.IncumbentId == seat.HeroStringId
                        && (int)row.Source == seat.SeatSourceValue
                        && (row.Source == ReignGovernmentSeatSource.LandholdingClanLeader
                            ? row.ClanId == seat.ClanStringId : row.SettlementId == seat.SettlementStringId)))),
                "Every restored occupied seat has its active constituency before any campaign time advances; new territory remains subject to accreditation.", null);
            AddGovernmentAssertion(assertions, "government_all_kingdoms_initialized", allInitialized,
                "Every eligible kingdom has a valid saved level and institution. Pending charters have no invented parties or seats; confirmed empty chambers have no invented dominant party or coalition. Occupied chambers have two to four valid parties, matching seat totals, a strict-majority coalition and speakers for occupied parties.",
                new JObject { ["eligibleKingdomCount"] = kingdoms.Count, ["governmentCount"] = _governments.Count });
            AddGovernmentAssertion(assertions, "government_resolution_catalog_104",
                ReignGovernmentResolutionCatalog.Templates.Count == 104,
                "The authoritative catalog contains exactly 104 tangible two-route resolutions.", null);
            AddGovernmentAssertion(assertions, "government_npc_meetings_provider_free",
                _meetings.Where(meeting => !meeting.PlayerAttended).All(meeting => meeting.ProviderCallCount == 0),
                "NPC seasonal meetings remain deterministic and make zero provider calls.", null);
            JObject result = GovernmentTestResult(runId, "preflight", assertions);
            result["kingdoms"] = new JArray(kingdoms.Select(kingdom => GovernmentKingdomEvidence(kingdom)));
            return result;
        }

        private static JObject GovernmentContracts(string runId)
        {
            JArray assertions = new JArray();
            int[] levelCounts = new int[6];
            int[] partyCounts = new int[5];
            var observedBlueprints = new Dictionary<string, ReignGovernmentPartyBlueprint>(StringComparer.OrdinalIgnoreCase);
            const int samples = 10000;
            for (int index = 0; index < samples; index++)
            {
                string id = "government-contract-" + index;
                levelCounts[ReignGovernmentRules.SelectStartingLevel(id)]++;
                int count = ReignGovernmentRules.SelectPartyCount(id, 12);
                partyCounts[count]++;
                foreach (ReignGovernmentPartyBlueprint party in ReignGovernmentRules.SelectParties(id, 12))
                    observedBlueprints[party.Id] = party;
            }
            int[] expectedLevels = { 0, 1000, 2500, 3000, 2500, 1000 };
            bool weightedLevels = Enumerable.Range(1, 5)
                .All(level => Math.Abs(levelCounts[level] - expectedLevels[level]) <= 220);
            AddGovernmentAssertion(assertions, "government_weighted_starting_levels", weightedLevels,
                "A 10,000-key deterministic sample remains within 2.2 percentage points of 10/25/30/25/10, so levels 2-4 are materially more likely than 1 or 5.",
                new JObject { ["level1"] = levelCounts[1], ["level2"] = levelCounts[2],
                    ["level3"] = levelCounts[3], ["level4"] = levelCounts[4], ["level5"] = levelCounts[5] });
            bool weightedParties = Math.Abs(partyCounts[2] - 4500) <= 220
                && Math.Abs(partyCounts[3] - 4500) <= 220 && Math.Abs(partyCounts[4] - 1000) <= 220;
            AddGovernmentAssertion(assertions, "government_weighted_party_counts", weightedParties,
                "Party count follows 45% two, 45% three, and 10% four only at twelve or more occupied seats.",
                new JObject { ["two"] = partyCounts[2], ["three"] = partyCounts[3], ["four"] = partyCounts[4] });
            AddGovernmentAssertion(assertions, "government_party_planks_multi_tone",
                observedBlueprints.Count >= 16 && observedBlueprints.Values.All(party => party.Planks.Count >= 2 && party.Planks.Count <= 4),
                "Every selectable party combines two to four compatible historical tones rather than one single issue.",
                new JObject { ["observedBlueprintCount"] = observedBlueprints.Count });

            var cultures = new Dictionary<string, ReignGovernmentInstitutionKind>
            {
                ["empire"] = ReignGovernmentInstitutionKind.Senate,
                ["vlandia"] = ReignGovernmentInstitutionKind.CouncilOfPeers,
                ["sturgia"] = ReignGovernmentInstitutionKind.Veche,
                ["battania"] = ReignGovernmentInstitutionKind.Oenach,
                ["aserai"] = ReignGovernmentInstitutionKind.Majlis,
                ["khuzait"] = ReignGovernmentInstitutionKind.Kurultai,
                ["nord"] = ReignGovernmentInstitutionKind.Thing
            };
            AddGovernmentAssertion(assertions, "government_culture_institutions",
                cultures.All(pair => ReignGovernmentRules.InstitutionForCulture(pair.Key).Kind == pair.Value),
                "Every supported culture maps to its approved institution kind, with Council of Estates as the fallback.", null);
            var prisonerTemplate = ReignGovernmentResolutionCatalog.Find("R092");
            var targetlessPrisoner = new ReignGovernmentResolutionRecord();
            var missingPrisoner = new ReignGovernmentResolutionRecord { TargetHeroStringId = "government_missing_prisoner_target" };
            AddGovernmentAssertion(assertions, "government_prisoner_remedies_require_recorded_target",
                RequiresRecordedPrisoner(prisonerTemplate)
                    && !HasRecordedSeasonalPrisonerTarget(targetlessPrisoner, prisonerTemplate)
                    && !HasRecordedSeasonalPrisonerTarget(missingPrisoner, prisonerTemplate)
                    && HasRecordedSeasonalPrisonerTarget(targetlessPrisoner, ReignGovernmentResolutionCatalog.Find("R006"))
                    && string.IsNullOrEmpty(targetlessPrisoner.TargetHeroStringId)
                    && missingPrisoner.TargetHeroStringId == "government_missing_prisoner_target",
                "Prisoner remedies reject absent or missing recorded people without rebinding the target; unrelated seasonal remedies remain eligible.", null);
            var sponsored = new GovernmentPetition { Revision = 2, Consents = new List<GovernmentPetitionConsent> {
                new GovernmentPetitionConsent { ActorId = "expired", Operation = "sponsor", Revision = 2, ExpiresInterval = 6 },
                new GovernmentPetitionConsent { ActorId = "wrong_revision", Operation = "sponsor", Revision = 1, ExpiresInterval = 8 },
                new GovernmentPetitionConsent { ActorId = "outside_roll", Operation = "sponsor", Revision = 2, ExpiresInterval = 8 },
                new GovernmentPetitionConsent { ActorId = "renewed", Operation = "sponsor", Revision = 2, ExpiresInterval = 8 } } };
            var sponsorRoll = new[] { "expired", "wrong_revision", "renewed" };
            string originalConsents = JsonConvert.SerializeObject(sponsored.Consents);
            AddGovernmentAssertion(assertions, "government_sponsor_exact_revision_and_interval",
                PersonalPetitionSponsor(sponsored, 6, sponsorRoll)?.ActorId == "renewed"
                    && PersonalPetitionSponsor(sponsored, 8, sponsorRoll) == null
                    && originalConsents == JsonConvert.SerializeObject(sponsored.Consents),
                "Sponsor selection skips expired, wrong-revision and nonmember consents without rewriting their history; expiry is exclusive.", null);
            AddGovernmentAssertion(assertions, "government_local_renewal_receipt_scope",
                LocalPetitionReceiptId("member:petition:2", 6) == LocalPetitionReceiptId("member:petition:2", 6)
                    && LocalPetitionReceiptId("member:petition:2", 6) != LocalPetitionReceiptId("member:petition:2", 7)
                    && LocalPetitionReceiptId("member:petition:2", 6) != LocalPetitionReceiptId("speaker:petition:2", 6),
                "Free local renewals have stable within-interval receipts and distinct later-interval/action identities; paid agreement IDs are unchanged.", null);
            var localRenewal = new GovernmentPetition { Id = "local_renewal", AuthorId = "self", Revision = 2, Status = "submitted" };
            GovernmentParticipationRules.AddConsent(localRenewal, "self", "sponsor", 2, LocalPetitionReceiptId("member:local_renewal:2", 6), 6, "party", out _);
            bool coversInitialSelection = LocalConsentCoversNextSelection(localRenewal, "self", "sponsor", 6);
            bool needsEarlyRenewal = GovernmentParticipationRules.HasConsent(localRenewal, "self", "sponsor", 7)
                && !LocalConsentCoversNextSelection(localRenewal, "self", "sponsor", 7);
            string warning = DescribePetitionConsent(localRenewal, localRenewal.Consents.Single(), 7, "next session");
            GovernmentParticipationRules.AddConsent(localRenewal, "self", "sponsor", 2, LocalPetitionReceiptId("member:local_renewal:2", 7), 7, "party", out _);
            string renewedConsent = JsonConvert.SerializeObject(localRenewal.Consents);
            GovernmentParticipationRules.AddConsent(localRenewal, "self", "sponsor", 2, LocalPetitionReceiptId("member:local_renewal:2", 7), 7, "party", out _);
            AddGovernmentAssertion(assertions, "government_local_renewal_covers_next_selection",
                coversInitialSelection && needsEarlyRenewal && warning.Contains("Renew sponsorship")
                    && LocalConsentCoversNextSelection(localRenewal, "self", "sponsor", 7)
                    && GovernmentParticipationRules.HasConsent(localRenewal, "self", "sponsor", 8)
                    && !GovernmentParticipationRules.HasConsent(localRenewal, "self", "sponsor", 9)
                    && localRenewal.Consents.Count == 1 && renewedConsent == JsonConvert.SerializeObject(localRenewal.Consents)
                    && DescribePetitionConsent(localRenewal, localRenewal.Consents.Single(), 9, "later session").StartsWith("Consent expired"),
                "Explicit free renewal bridges an otherwise expired-at-selection sponsorship; same-interval repetition is unchanged, expiry remains exclusive and its current/expiring/expired status is visible.", null);
            var sourceParticipants = new JArray(
                new JObject { ["heroId"] = "speaker", ["role"] = "petitioner" },
                new JObject { ["heroId"] = "author", ["role"] = "beneficiary", ["settlementId"] = "town" },
                new JObject { ["heroId"] = "lord", ["role"] = "affected_lord" },
                new JObject { ["heroId"] = "speaker", ["role"] = "filing_speaker" });
            string participantHistory = sourceParticipants.ToString(Formatting.None);
            var normalized = PetitionParticipants(sourceParticipants, "author", "sponsor", "speaker");
            AddGovernmentAssertion(assertions, "government_petition_roles_preserve_affected_people",
                normalized.Count == 5 && normalized[0].Value<string>("heroId") == "author"
                    && normalized.Count(x => x.Value<string>("role") == "petitioner") == 1
                    && normalized.Any(x => x.Value<string>("heroId") == "speaker" && x.Value<string>("role") == "filing_speaker")
                    && normalized.Any(x => x.Value<string>("heroId") == "lord" && x.Value<string>("role") == "affected_lord")
                    && normalized.Any(x => x.Value<string>("role") == "beneficiary" && x.Value<string>("settlementId") == "town")
                    && JToken.DeepEquals(normalized, PetitionParticipants(normalized, "author", "sponsor", "speaker"))
                    && participantHistory == sourceParticipants.ToString(Formatting.None)
                    && !PetitionParticipants(sourceParticipants, "author", "sponsor", null).Any(x => x.Value<string>("role") == "filing_speaker"),
                "Canonical petition roles are ordered and idempotent; affected people and extra role metadata survive. A missing historical filer is never invented.", null);
            var news = new GovernmentNewsLedger();
            news.Record(1, "day", "realm", "Realm", "Senate", new GovernmentNewsItem { ReceiptId = "business:presented", Text = "Old attribution" });
            bool refreshedPending = news.Record(1, "day", "realm", "Realm", "Senate", new GovernmentNewsItem { ReceiptId = "business:presented", Text = "Correct attribution" });
            news.PublishDue(2);
            AddGovernmentAssertion(assertions, "government_pending_news_correction_preserves_archive",
                refreshedPending && news.Archive.Count == 1 && news.Receipts.Count == 1
                    && !news.Record(1, "day", "realm", "Realm", "Senate", new GovernmentNewsItem { ReceiptId = "business:presented", Text = "Later change" })
                    && news.Archive[0].Kingdoms[0].Items.Single().Text == "Correct attribution",
                "An existing pending presentation can refresh once without duplicate news; publication makes the archived report immutable.", null);
            var narrowShare = new ReignBeta.UI.ViewModels.ReignGovernmentShareVM("A very long proposal title", 2.27, 0);
            var middleShare = new ReignBeta.UI.ViewModels.ReignGovernmentShareVM("A very long proposal title", 12.5, 0);
            var longOption = new ReignBeta.UI.ViewModels.ReignGovernmentOptionVM("accept", "Approve", new string('x', 500), false, false, _ => { }, 12.5);
            string fullTerms = "Workshop benefit, tariff cost and an explicit expiry remain in the full proposal.";
            var readingOptions = new JArray(new JObject { ["label"] = "Approve", ["description"] = fullTerms });
            string completeTerms = ReignBeta.UI.ViewModels.ReignGovernmentScreenVM.CompleteProposalText("Proposal", readingOptions);
            AddGovernmentAssertion(assertions, "government_compact_support_remains_readable",
                Math.Abs(narrowShare.Width - 18.9772f) < .001 && narrowShare.Text == ""
                    && middleShare.Text == "12.5%" && longOption.Detail == "12.5% declared support · Terms below."
                    && longOption.ShareText == "12.5% declared support"
                    && completeTerms.Contains(fullTerms)
                    && ReignBeta.UI.ViewModels.ReignGovernmentScreenVM.CompleteProposalText(completeTerms, readingOptions) == completeTerms,
                "Narrow bars keep their true shares without miniature sentences; long outcome descriptions point to the complete proposal reading field.", null);
            var closedUnvoted = new ReignGovernmentBusinessRecord { Status = "invalidated", VotesJson = "[]" };
            var recorded = new ReignGovernmentBusinessRecord { Status = "decided", VotesJson = "[{\"memberHeroId\":\"member\",\"optionId\":\"accept\",\"weight\":10}]" };
            var openUnvoted = new ReignGovernmentBusinessRecord { Status = "presented", VotesJson = "[]" };
            var finalOption = new ReignBeta.UI.ViewModels.ReignGovernmentOptionVM("accept", "Approve", "", false, false, _ => { }, 30, true);
            var unvotedOption = new ReignBeta.UI.ViewModels.ReignGovernmentOptionVM("accept", "Approve", "", false, false, _ => { }, 30, false, true);
            AddGovernmentAssertion(assertions, "government_closed_vote_presentation_is_truthful",
                ReignBeta.UI.ViewModels.ReignGovernmentScreenVM.ClosedWithoutRecordedVote(closedUnvoted)
                    && !ReignBeta.UI.ViewModels.ReignGovernmentScreenVM.ClosedWithoutRecordedVote(recorded)
                    && !ReignBeta.UI.ViewModels.ReignGovernmentScreenVM.ClosedWithoutRecordedVote(openUnvoted)
                    && !ReignBeta.UI.ViewModels.ReignGovernmentScreenVM.ClosedWithoutRecordedVote(null)
                    && finalOption.ShareText == "30% recorded vote" && finalOption.Detail == finalOption.ShareText
                    && unvotedOption.ShareText == "No vote recorded" && unvotedOption.Detail == unvotedOption.ShareText,
                "A closed matter without ballots cannot present old declarations as a final vote. Recorded outcomes identify actual vote shares; open matters retain declared support.", null);
            var diagnosticSource = new JObject {
                ["business"] = new JArray(new JObject { ["id"] = "unchanged" }),
                ["participation"] = new JObject { ["Profiles"] = new JArray(
                    new JObject { ["privateBelief"] = "private-sentinel-before" }) } };
            string diagnosticOriginal = diagnosticSource.ToString(Formatting.None);
            var diagnosticBefore = GovernmentFingerprintDiagnostics(diagnosticSource);
            var diagnosticChanged = (JObject)diagnosticSource.DeepClone();
            diagnosticChanged["participation"]["Profiles"][0]["privateBelief"] = "private-sentinel-after";
            var diagnosticAfter = GovernmentFingerprintDiagnostics(diagnosticChanged);
            AddGovernmentAssertion(assertions, "government_persistence_diagnostics_private_and_observational",
                JToken.DeepEquals(diagnosticBefore, GovernmentFingerprintDiagnostics(diagnosticSource))
                    && diagnosticBefore.Value<string>("featureFingerprint") == "8efb470c56faa9b30efd82de66949b8da4a3bf44a11a026aa3be0db6bad586cf"
                    && !JToken.DeepEquals(diagnosticBefore["featureFingerprint"], diagnosticAfter["featureFingerprint"])
                    && !JToken.DeepEquals(diagnosticBefore["components"]["participation"], diagnosticAfter["components"]["participation"])
                    && JToken.DeepEquals(diagnosticBefore["components"]["business"], diagnosticAfter["components"]["business"])
                    && !diagnosticBefore.ToString().Contains("private-sentinel")
                    && !diagnosticAfter.ToString().Contains("private-sentinel")
                    && diagnosticOriginal == diagnosticSource.ToString(Formatting.None),
                "Read-only component digests locate a private-state change without revealing its values or mutating the source; identical inputs retain identical full-v2 digests.", null);
            var meetingHistory = Enumerable.Range(0, 70).Select(index => new ReignGovernmentMeetingRecord {
                MeetingId = "retention_" + index, KingdomStringId = index % 2 == 0 ? "first" : "second",
                MeetingDay = index / 2 }).ToList();
            string originalMeetingHistory = JsonConvert.SerializeObject(meetingHistory);
            var retainedMeetings = RetainGovernmentMeetingHistory(meetingHistory);
            AddGovernmentAssertion(assertions, "government_meeting_history_retention_is_stable_before_save",
                retainedMeetings.Count == 64 && meetingHistory.Count == 70
                    && retainedMeetings.All(x => x.MeetingDay >= 3)
                    && retainedMeetings[0].MeetingId == "retention_68"
                    && retainedMeetings[1].MeetingId == "retention_69"
                    && retainedMeetings.Count(x => x.KingdomStringId == "first") == 32
                    && retainedMeetings.Count(x => x.KingdomStringId == "second") == 32
                    && JToken.DeepEquals(JArray.FromObject(retainedMeetings),
                        JArray.FromObject(RetainGovernmentMeetingHistory(retainedMeetings)))
                    && originalMeetingHistory == JsonConvert.SerializeObject(meetingHistory),
                "Meeting retention keeps the newest64 with stable same-day order and equal realm coverage in this fixture, is idempotent, and leaves the input untouched; live session recording uses this rule before any save.", null);
            var crimeTowns = new[] {
                new KeyValuePair<string, float>("healthy", 40f),
                new KeyValuePair<string, float>("borderline", 39.99f),
                new KeyValuePair<string, float>("z_town", 12f),
                new KeyValuePair<string, float>("a_town", 12f),
                new KeyValuePair<string, float>("", 0f),
                new KeyValuePair<string, float>("invalid_nan", float.NaN),
                new KeyValuePair<string, float>("invalid_infinity", float.NegativeInfinity) };
            string originalCrimeTowns = JsonConvert.SerializeObject(crimeTowns);
            AddGovernmentAssertion(assertions, "government_crime_target_matches_town_security_evidence",
                SelectTownCrimeTargetId(crimeTowns) == "a_town"
                    && SelectTownCrimeTargetId(Enumerable.Reverse(crimeTowns)) == "a_town"
                    && SelectTownCrimeTargetId(new[] { crimeTowns[0] }) == null
                    && SelectTownCrimeTargetId(new[] { crimeTowns[1] }) == "borderline"
                    && SelectTownCrimeTargetId(crimeTowns.Skip(4)) == null
                    && SelectTownCrimeTargetId(null) == null
                    && originalCrimeTowns == JsonConvert.SerializeObject(crimeTowns),
                "Town crime targets use actual security below40, choose the worst affected town with a stable identity tie-break, reject missing/nonfinite evidence, and leave observations untouched. Realm projection excludes castles and foreign settlements; filed targets remain frozen.", null);
            return GovernmentTestResult(runId, "contracts", assertions);
        }

        private JObject GovernmentSnapshot(string runId)
        {
            JArray assertions = new JArray();
            List<Kingdom> kingdoms = Kingdom.All.Where(IsEligibleKingdom)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
            var snapshots = new JArray();
            bool privacy = true;
            bool pressureChannels = true;
            bool coalitions = true;
            foreach (Kingdom kingdom in kingdoms)
            {
                JObject snapshot = BuildPublicSnapshot(kingdom);
                snapshots.Add(snapshot);
                privacy &= snapshot["members"] == null && snapshot["outstandingDeals"] == null
                    && !snapshot.Descendants().OfType<JProperty>().Any(property =>
                        new[] { "trust", "rulerRelation", "relation", "partyLoyalty", "governmentLoyalty",
                            "lobbyingShift", "supportPercent", "stanceTowardRuler" }
                            .Contains(property.Name, StringComparer.OrdinalIgnoreCase));
                pressureChannels &= snapshot.SelectToken("peoplePressure.additionalToNoblePoliticalPressure")?.Value<bool>() == true;
                bool occupiedChamber = GetSeats(kingdom.StringId).Count > 0;
                coalitions &= snapshot["governingCoalitionPartyIds"] is JArray coalition
                    && snapshot["parties"] is JArray snapshotParties
                    && (occupiedChamber
                        ? coalition.Count > 0 && snapshotParties.Children<JObject>().Any(party => party.Value<bool?>("isGoverningCoalition") == true)
                        : coalition.Count == 0 && string.IsNullOrEmpty(snapshot.Value<string>("dominantPartyId"))
                            && !snapshotParties.Children<JObject>().Any(party => party.Value<bool?>("isGoverningCoalition") == true));
            }
            AddGovernmentAssertion(assertions, "government_public_snapshot_privacy", privacy,
                "Public ruler and ambassador knowledge exposes institutions and observable political records without live relationships, hidden loyalty, Trust, score predictions, bribes or private deals.", null);
            AddGovernmentAssertion(assertions, "government_additional_people_pressure", pressureChannels,
                "Every public snapshot explicitly reports Government as people-side pressure additional to noble Political Pressure, with separate settlement and civil-war input routes.", null);
            AddGovernmentAssertion(assertions, "government_public_governing_coalition", coalitions,
                "Occupied chambers identify their saved governing coalition; an empty or pending chamber exposes no fictitious dominant party or coalition.", null);
            AddGovernmentAssertion(assertions, "government_authoritative_ambassador_knowledge",
                snapshots.All(value => value.Value<bool?>("authoritative") == true
                    && !string.IsNullOrWhiteSpace(value.Value<string>("authorityMeaning"))
                    && value.Value<string>("decisionRule")?.IndexOf("equal frozen occupied-seat shares", StringComparison.OrdinalIgnoreCase) >= 0
                    && value.Value<string>("decisionRule")?.IndexOf("no later veto or runoff", StringComparison.OrdinalIgnoreCase) >= 0),
                "Rulers and ambassadors receive authoritative disagreement-aware government knowledge.", null);
            JObject models = ObserveNativeEdictModels();
            JObject crimeTargets = ObserveTownCrimeTargets(kingdoms);
            if (models.Value<bool>("available"))
                AddGovernmentAssertion(assertions, "government_native_edict_model_contributions", models.Value<bool>("ok"),
                    "Real tariff and bounded workshop calculations reach the edict postfix exactly once and preserve balances/ledger. Nonzero exercised flags, not configured percentages alone, establish positive modifier coverage.", null);
            AddGovernmentAssertion(assertions, "government_native_crime_targets_match_evidence", crimeTargets.Value<bool>("ok"),
                "Owned-town crime evidence, selected targets and unfiled factory candidates agree; existing resolutions and business remain unchanged. Candidate count identifies positive coverage.", null);
            JObject result = GovernmentTestResult(runId, "snapshot", assertions);
            result["governments"] = snapshots;
            result["nativeEdictModels"] = models;
            result["townCrimeTargets"] = crimeTargets;
            return result;
        }

        private JObject ObserveNativeEdictModels()
        {
            Settlement settlement = Hero.MainHero?.CurrentSettlement;
            if (settlement?.IsTown != true || settlement.OwnerClan != Hero.MainHero?.Clan)
                return new JObject { ["available"] = false, ["reason"] = "Enter a player-owned town; no model call was made." };
            string edictsBefore = JsonConvert.SerializeObject(_edicts);
            int goldBefore = Hero.MainHero.Gold, tariffsBefore = settlement.Town.TradeTaxAccumulated;
            var effects = EdictEffects(settlement);
            var observations = new JArray();
            observations.Add(ReignGovernmentEdictModelPatch.ObserveNativeModel("CalculateTownIncomeFromTariffs", settlement,
                () => TaleWorlds.CampaignSystem.Campaign.Current.Models.ClanFinanceModel.CalculateTownIncomeFromTariffs(settlement.OwnerClan, settlement.Town, false)));
            foreach (var workshop in settlement.Town.Workshops.Where(x => x != null).Take(3))
                observations.Add(ReignGovernmentEdictModelPatch.ObserveNativeModel("GetEffectiveConversionSpeedOfProduction", settlement,
                    () => TaleWorlds.CampaignSystem.Campaign.Current.Models.WorkshopModel.GetEffectiveConversionSpeedOfProduction(workshop, 1f, true)));
            bool correct = observations.All(row =>
            {
                double expectedFactor = row.Value<string>("method") == "CalculateTownIncomeFromTariffs"
                    ? effects.Tariffs : effects.WorkshopProduction;
                double before = row.Value<double?>("beforeEdicts") ?? double.NaN;
                double expected = Math.Min(row.Value<double?>("limitMax") ?? double.NaN,
                    Math.Max(row.Value<double?>("limitMin") ?? double.NaN, before + Math.Max(0, before) * expectedFactor));
                double tolerance = Math.Max(.002, Math.Abs(expected) * .00001);
                return row.Value<int>("calls") == 1 && row.Value<double?>("flatDelta") == 0
                    && Math.Abs((row.Value<double?>("factor") ?? double.NaN) - expectedFactor) < .000001
                    && Math.Abs((row.Value<double?>("afterEdicts") ?? double.NaN) - expected) <= tolerance;
            });
            bool conserved = goldBefore == Hero.MainHero.Gold && tariffsBefore == settlement.Town.TradeTaxAccumulated
                && edictsBefore == JsonConvert.SerializeObject(_edicts);
            return new JObject { ["schema"] = "reign-government-native-edict-model-observation-v1", ["available"] = true,
                ["settlementId"] = settlement.StringId, ["worldDay"] = CampaignTime.Now.ToDays,
                ["expectedTariffFactor"] = effects.Tariffs, ["expectedWorkshopFactor"] = effects.WorkshopProduction,
                ["nonzeroTariffExercised"] = effects.Tariffs != 0 && observations.Any(x =>
                    x.Value<string>("method") == "CalculateTownIncomeFromTariffs"
                    && Math.Abs((x.Value<double?>("afterEdicts") ?? 0) - (x.Value<double?>("beforeEdicts") ?? 0)) > .000001),
                ["nonzeroWorkshopExercised"] = effects.WorkshopProduction != 0 && observations.Any(x =>
                    x.Value<string>("method") == "GetEffectiveConversionSpeedOfProduction"
                    && Math.Abs((x.Value<double?>("afterEdicts") ?? 0) - (x.Value<double?>("beforeEdicts") ?? 0)) > .000001),
                ["applyWithdrawals"] = false, ["nativeCalculationsMatch"] = correct, ["balancesAndEdictsUnchanged"] = conserved,
                ["ok"] = correct && conserved, ["observations"] = observations };
        }

        private JObject ObserveTownCrimeTargets(IEnumerable<Kingdom> kingdoms)
        {
            string historyBefore = JsonConvert.SerializeObject(_resolutions);
            string businessBefore = JsonConvert.SerializeObject(_business);
            var observations = new JArray();
            foreach (Kingdom kingdom in kingdoms.Take(32))
            {
                var eligible = kingdom.Fiefs.Where(x => x?.Settlement?.IsTown == true
                    && !float.IsNaN(x.Security) && !float.IsInfinity(x.Security) && x.Security < 40f).ToList();
                Settlement selected = SelectTargetSettlement(kingdom, "criminal_activity");
                bool evidence = CollectEvidenceTags(kingdom).Contains("criminal_activity");
                bool correct = eligible.Count == 0 ? selected == null && !evidence
                    : evidence && selected?.IsTown == true && selected.MapFaction == kingdom
                        && eligible.Any(x => x.Settlement == selected)
                        && selected.Town.Security == eligible.Min(x => x.Security);
                ReignGovernmentResolutionRecord candidate = null;
                var state = GetGovernment(kingdom);
                var party = GetParties(kingdom.StringId).FirstOrDefault();
                if (selected != null && state != null && party != null)
                    candidate = CreateResolution(kingdom, state, party, ReignGovernmentResolutionCatalog.Find("R029"), CurrentDay());
                if (candidate != null) correct &= candidate.TargetSettlementStringId == selected.StringId;
                observations.Add(new JObject { ["kingdomId"] = kingdom.StringId, ["eligibleTownCount"] = eligible.Count,
                    ["crimeEvidencePresent"] = evidence, ["selectedSettlementId"] = selected?.StringId ?? "",
                    ["selectedIsTown"] = selected?.IsTown == true, ["selectedIsOwned"] = selected?.MapFaction == kingdom,
                    ["unfiledCandidateCreated"] = candidate != null, ["factoryTargetMatches"] = candidate != null && candidate.TargetSettlementStringId == selected?.StringId,
                    ["ok"] = correct });
            }
            bool unchanged = historyBefore == JsonConvert.SerializeObject(_resolutions) && businessBefore == JsonConvert.SerializeObject(_business);
            return new JObject { ["schema"] = "reign-government-town-crime-target-observation-v1",
                ["candidateOnly"] = true, ["filedHistoryUnchanged"] = unchanged,
                ["kingdomTotal"] = kingdoms.Count(), ["truncated"] = kingdoms.Count() > observations.Count,
                ["candidateCount"] = observations.Count(x => x.Value<bool>("unfiledCandidateCreated")),
                ["ok"] = unchanged && observations.All(x => x.Value<bool>("ok")), ["observations"] = observations };
        }

        private JObject GovernmentAuthorityContracts(string runId)
        {
            JArray assertions = new JArray();
            bool increasesAttractive = Enumerable.Range(1, 4).All(level =>
            {
                ReignGovernmentAuthorityTransition value = ReignGovernmentRules.IncreaseTransition(level);
                return value.Loyalty > 0 && value.Prosperity > 0 && value.Hearth > 0
                    && value.DurationDays == 30 && value.Trust == 0 && value.SupportiveRelation > 0;
            });
            AddGovernmentAssertion(assertions, "government_power_increase_trap", increasesAttractive,
                "Every authority increase grants immediate loyalty, prosperity, hearth, relationship and thirty-day benefits without changing retired Government Trust.", null);
            ReignGovernmentReductionPenalty[] forcedScale = Enumerable.Range(2, 4)
                .Select(ReignGovernmentRules.ForcedReductionPenalty).ToArray();
            ReignGovernmentReductionPenalty forced = forcedScale[3];
            AddGovernmentAssertion(assertions, "government_forced_five_to_four_exact",
                forcedScale.Select(x => x.LandholdingClanLeaderRelation)
                    .SequenceEqual(new[] { -20, -35, -50, -70 })
                && forcedScale.Select(x => x.NonLandholdingDissenterRelation)
                    .SequenceEqual(new[] { -10, -20, -30, -40 })
                && forced.SettlementLoyalty == -70 && forced.Trust == 0,
                "Forced reductions use the exact capped clan-leader -20/-35/-50/-70 and nonlandholder -10/-20/-30/-40 ladders while 5-to-4 retains -70 settlement loyalty and trust zero.", null);
            ReignGovernmentReductionPenalty approved = ReignGovernmentRules.ApprovedReductionPenalty(5);
            AddGovernmentAssertion(assertions, "government_voted_five_to_four_small_cost",
                approved.SettlementLoyalty == -5 && approved.LandholdingClanLeaderRelation == 0
                && approved.NonLandholdingDissenterRelation == -5 && approved.Trust == 0,
                "A ratified 5-to-4 reduction carries only its small fixed consented costs.", null);
            bool monotonic = Enumerable.Range(1, 4).All(level =>
            {
                ReignGovernmentDailyEffects lower = ReignGovernmentRules.DailyEffects(level);
                ReignGovernmentDailyEffects upper = ReignGovernmentRules.DailyEffects(level + 1);
                return upper.Loyalty >= lower.Loyalty && upper.Prosperity >= lower.Prosperity && upper.Hearth >= lower.Hearth;
            });
            AddGovernmentAssertion(assertions, "government_daily_benefits_monotonic", monotonic,
                "Standing people-side loyalty, prosperity, and hearth benefits rise monotonically with government authority.", null);

            Clan consentClan = Kingdom.All.Where(IsEligibleKingdom).SelectMany(x => x.Clans)
                .Where(x => x != null && x != Clan.PlayerClan && !x.IsEliminated && x.Leader != null)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            Hero individual = _seats.Select(x => FindHero(x.HeroStringId))
                .Where(x => x != null && x.Clan != Clan.PlayerClan
                    && (consentClan == null || x.Clan != consentClan))
                .OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            var consentState = new ReignGovernmentStateRecord
            {
                Level = 5,
                ReductionConsentFromLevel = 5,
                ReductionConsentHeroIdsCsv = individual?.StringId ?? string.Empty,
                ReductionConsentClanIdsCsv = consentClan?.StringId ?? string.Empty
            };
            bool individualScoped = individual == null
                || IsReductionPenaltyExempt(consentState, individual, 5);
            bool clanScoped = consentClan == null || consentClan.Heroes
                .Where(x => x != null && x.IsAlive).All(x => IsReductionPenaltyExempt(consentState, x, 5));
            bool playerClanExempt = Clan.PlayerClan == null || Clan.PlayerClan.Heroes
                .Where(x => x != null && x.IsAlive).All(x => IsReductionPenaltyExempt(
                    new ReignGovernmentStateRecord { Level = 5 }, x, 5));
            bool staleConsentRejected = (individual == null
                    || !IsReductionPenaltyExempt(consentState, individual, 4))
                && (consentClan?.Leader == null
                    || !IsReductionPenaltyExempt(consentState, consentClan.Leader, 4));
            AddGovernmentAssertion(assertions, "government_reduction_consent_scopes",
                individualScoped && clanScoped && playerClanExempt && staleConsentRejected,
                "Individual consent exempts only its saved NPC, clan-leader consent exempts the whole saved clan, the player clan is always exempt, and consent does not carry to another authority level.",
                new JObject { ["individualHeroId"] = individual?.StringId ?? string.Empty,
                    ["consentingClanId"] = consentClan?.StringId ?? string.Empty });
            return GovernmentTestResult(runId, "authority", assertions);
        }

        private JObject GovernmentResolutionContracts(string runId)
        {
            JArray assertions = new JArray();
            IReadOnlyList<ReignGovernmentResolutionTemplate> templates = ReignGovernmentResolutionCatalog.Templates;
            bool tangible = templates.Count == 104 && templates.All(template => template.FirstRoute.Target > 0
                && template.SecondRoute.Target > 0 && template.FirstRoute.DeadlineDays > 0
                && template.SecondRoute.DeadlineDays > 0
                && (template.FirstRoute.Action != template.SecondRoute.Action
                    || !string.Equals(template.FirstRoute.Description, template.SecondRoute.Description, StringComparison.OrdinalIgnoreCase))
                && new[] { template.FirstRoute, template.SecondRoute }.All(route =>
                {
                    string terms = SeasonalRequirementDescription(route, template.Scale);
                    string amount = route.Target.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
                    if (!terms.Contains(route.DeadlineDays + " days after acceptance")) return false;
                    if (IsPrivateRelationProgress(route.Action))
                        return terms == "Completion is verified privately; deadline: " + route.DeadlineDays + " days after acceptance.";
                    if (!terms.Contains(amount)) return false;
                    if (UsesImmediateGold(route.Action) && (route.Unit != ReignGovernmentResolutionUnit.Gold
                        || route.Target < 10000
                        || !terms.Contains("The ruler pays " + amount + " denars immediately on acceptance."))) return false;
                    if (route.Action == ReignGovernmentResolutionAction.GarrisonMinimum && !terms.Contains("at least " + amount + " troops")) return false;
                    if (route.Action == ReignGovernmentResolutionAction.GarrisonMaximum && !terms.Contains("at most " + amount + " troops")) return false;
                    if ((route.Action == ReignGovernmentResolutionAction.LoyaltyTarget
                        || route.Action == ReignGovernmentResolutionAction.ProsperityTarget
                        || route.Action == ReignGovernmentResolutionAction.HearthTarget
                        || route.Action == ReignGovernmentResolutionAction.SecurityTarget
                        || route.Action == ReignGovernmentResolutionAction.CollectTaxMinimum)
                        && !terms.Contains("from its value at acceptance")) return false;
                    int dailyCost = DailyCommitmentGoldCost(route.Action, template.Scale);
                    return dailyCost == 0 || terms.Contains("The ruler pays "
                        + dailyCost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                        + " denars for each honored commitment day; an unfunded day earns no progress.");
                }));
            AddGovernmentAssertion(assertions, "government_104_tangible_two_route_resolutions", tangible,
                "All 104 templates expose two tangible deadline-bound routes with public units, exact payer/cost terms and private relationship verification.", null);
            var memorial = ReignGovernmentResolutionCatalog.Find("R094");
            string sharedTerms = SeasonalRequirementDescription(memorial.FirstRoute, memorial.Scale);
            string memorialText = ReignBeta.UI.ViewModels.ReignGovernmentScreenVM.CompleteProposalText("Choose a remedy.",
                new JArray(new JObject { ["label"] = memorial.FirstRoute.Description, ["description"] = sharedTerms },
                    new JObject { ["label"] = memorial.SecondRoute.Description, ["description"] = sharedTerms }));
            AddGovernmentAssertion(assertions, "government_equal_cost_remedies_keep_named_terms",
                memorialText.Contains(memorial.FirstRoute.Description + ": " + sharedTerms)
                    && memorialText.Contains(memorial.SecondRoute.Description + ": " + sharedTerms),
                "Two differently named remedies retain their complete terms even when their budgets, target and deadline are identical.", null);
            ReignGovernmentResolutionAction[] actions = (ReignGovernmentResolutionAction[])Enum.GetValues(typeof(ReignGovernmentResolutionAction));
            AddGovernmentAssertion(assertions, "government_resolution_action_catalog_complete",
                actions.All(action => templates.Any(template => template.FirstRoute.Action == action || template.SecondRoute.Action == action)),
                "Every resolution action type is used by at least one production template.",
                new JObject { ["actionCount"] = actions.Length });

            Kingdom kingdom = Kingdom.All.Where(IsEligibleKingdom).OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            Kingdom target = Kingdom.All.Where(x => x != null && x != kingdom && !x.IsEliminated)
                .OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            bool routed = false;
            if (kingdom != null && target != null)
            {
                var matching = new ReignGovernmentResolutionRecord
                {
                    KingdomStringId = kingdom.StringId, TemplateId = "R019", Status = "active",
                    RouteActionValue = (int)ReignGovernmentResolutionAction.SignTradeAgreement,
                    TargetKingdomStringId = target.StringId, RequiredAmount = 3, CurrentValue = 0f
                };
                var other = new ReignGovernmentResolutionRecord
                {
                    KingdomStringId = kingdom.StringId, TemplateId = "R019", Status = "active",
                    RouteActionValue = (int)ReignGovernmentResolutionAction.SignTradeAgreement,
                    TargetKingdomStringId = "government_test_non_target", RequiredAmount = 3, CurrentValue = 0f
                };
                _resolutions.Add(matching);
                _resolutions.Add(other);
                try
                {
                    int updated = RecordResolutionProgress(kingdom.StringId,
                        ReignGovernmentResolutionAction.SignTradeAgreement, target.StringId, 1,
                        "Government harness target-routing receipt.");
                    routed = updated == 1 && Math.Abs(matching.CurrentValue - 1f) < 0.001f
                        && Math.Abs(other.CurrentValue) < 0.001f && Same(matching.Status, "active");
                }
                finally
                {
                    _resolutions.Remove(matching);
                    _resolutions.Remove(other);
                }
            }
            AddGovernmentAssertion(assertions, "government_resolution_target_routing", routed,
                "A verified action advances only the active resolution with the matching kingdom/action/target tuple, and the harness restores its synthetic records.", null);
            int trackableRoutes = 0;
            const string syntheticKingdomId = "government_test_synthetic_kingdom";
            foreach (ReignGovernmentResolutionTemplate template in templates)
            {
                ReignGovernmentResolutionRoute[] routes = { template.FirstRoute, template.SecondRoute };
                for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
                {
                    ReignGovernmentResolutionRoute route = routes[routeIndex];
                    string targetId = "government_test_target_" + template.Id + "_" + routeIndex;
                    var record = new ReignGovernmentResolutionRecord
                    {
                        ResolutionId = "government_test_route_" + template.Id + "_" + routeIndex,
                        KingdomStringId = syntheticKingdomId,
                        TemplateId = template.Id,
                        Status = "active",
                        RouteActionValue = (int)route.Action,
                        TargetSettlementStringId = targetId,
                        TargetKingdomStringId = targetId,
                        TargetClanStringId = targetId,
                        TargetHeroStringId = targetId,
                        TargetPolicyStringId = targetId,
                        RequiredAmount = Math.Max(2, route.Target),
                        CurrentValue = 0f
                    };
                    _resolutions.Add(record);
                    try
                    {
                        int updated = RecordResolutionProgress(syntheticKingdomId, route.Action,
                            targetId, 1, "Government 208-route tracking contract.");
                        if (updated == 1 && Math.Abs(record.CurrentValue - 1f) < 0.001f
                            && Same(record.Status, "active")
                            && record.EvidenceJson.IndexOf("208-route", StringComparison.OrdinalIgnoreCase) >= 0)
                            trackableRoutes++;
                    }
                    finally
                    {
                        _resolutions.Remove(record);
                    }
                }
            }
            AddGovernmentAssertion(assertions, "government_all_208_resolution_routes_trackable",
                trackableRoutes == 208,
                "Every route in all 104 two-route templates accepts correctly targeted evidence through its production action type; representative native cases separately prove completion rewards.",
                new JObject { ["trackableRoutes"] = trackableRoutes, ["expectedRoutes"] = 208 });
            return GovernmentTestResult(runId, "resolutions", assertions);
        }

        private static JObject GovernmentVotingContracts(string runId)
        {
            JArray assertions = new JArray();
            var baseReduction = new ReignGovernmentVoteInput
            {
                PrimaryPlank = ReignGovernmentPlank.RoyalAuthority,
                SecondaryPlanks = new[] { ReignGovernmentPlank.Security }, CurrentLevel = 5,
                PartySeatShare = 0.25d, PartyLoyalty = 75, SpeakerSupportsReduction = true,
                RulerRelation = -100
            };
            int hostile = ReignGovernmentRules.EvaluateLevelReductionVote(baseReduction).Score;
            baseReduction.RulerRelation = 100;
            int friendly = ReignGovernmentRules.EvaluateLevelReductionVote(baseReduction).Score;
            AddGovernmentAssertion(assertions, "government_reduction_vote_every_member_relation", friendly - hostile == 100,
                "Each member's relationship with the ruler contributes directly to that member's authority-reduction vote.",
                new JObject { ["hostileScore"] = hostile, ["friendlyScore"] = friendly });
            baseReduction.PartySeatShare = 0.05d;
            int smallParty = ReignGovernmentRules.EvaluateLevelReductionVote(baseReduction).Score;
            baseReduction.PartySeatShare = 0.95d;
            int largeParty = ReignGovernmentRules.EvaluateLevelReductionVote(baseReduction).Score;
            AddGovernmentAssertion(assertions, "government_party_power_self_interest", largeParty < smallParty,
                "Members become harder to persuade when a reduction would cost their own party more institutional power.", null);

            var vote = new ReignGovernmentResolutionVoteInput
            {
                IsProposingPartyMember = false, SharedPlankCount = 0, GovernmentLoyalty = 35,
                PartyLoyalty = 20, OwnSpeakerSupports = false, ProposingSpeakerCharm = 20,
                OwnSpeakerCharm = 160, ProposingSpeakerRelation = -40, LobbyingShift = 0
            };
            int entrenched = ReignGovernmentRules.EvaluateResolutionVote(vote).Score;
            vote.ProposingSpeakerCharm = 300;
            vote.OwnSpeakerCharm = 20;
            vote.ProposingSpeakerRelation = 80;
            vote.LobbyingShift = 25;
            int persuaded = ReignGovernmentRules.EvaluateResolutionVote(vote).Score;
            AddGovernmentAssertion(assertions, "government_charm_loyalty_defection", persuaded > entrenched,
                "Opposition-speaker charm, personal relations, and difficult bounded lobbying can move an individual vote despite party alignment.",
                new JObject { ["entrenchedScore"] = entrenched, ["persuadedScore"] = persuaded });
            AddGovernmentAssertion(assertions, "government_two_thirds_reduction_vote",
                !ReignGovernmentRules.ReductionRatified(5, 8) && ReignGovernmentRules.ReductionRatified(6, 9),
                "Authority reductions require at least two thirds of all occupied seats.", null);
            return GovernmentTestResult(runId, "voting", assertions);
        }

        private JObject GovernmentPressureContracts(string runId)
        {
            JArray assertions = new JArray();
            ReignGovernmentActionDisposition[] expected =
            {
                ReignGovernmentActionDisposition.Advisory,
                ReignGovernmentActionDisposition.FormalPressure,
                ReignGovernmentActionDisposition.ReconsiderationDelay,
                ReignGovernmentActionDisposition.ApprovalOrOverride,
                ReignGovernmentActionDisposition.BlockedPendingApprovalOrLevelReduction
            };
            bool levels = Enumerable.Range(1, 5).All(level =>
                ReignGovernmentRules.ActionDisposition(level, ReignGovernmentActionKind.War) == expected[level - 1]);
            AddGovernmentAssertion(assertions, "government_pressure_levels_one_to_five", levels,
                "Major actions progress from advisory, formal pressure, seven-day reconsideration, approval-or-override, to level-five block.", null);
            AddGovernmentAssertion(assertions, "government_level_three_delay_exact",
                ReignGovernmentRules.ReconsiderationDelayDays(3) == 7
                && ReignGovernmentRules.ReconsiderationDelayDays(2) == 0,
                "Only level 3 applies the exact seven-day reconsideration delay.", null);
            ReignWorldActionType[] coveredPoliticalActions =
            {
                ReignWorldActionType.DiplomacyDeclareWar, ReignWorldActionType.DiplomacyMakePeace,
                ReignWorldActionType.DiplomacySignTradeAgreement, ReignWorldActionType.DiplomacyExchangePrisoners,
                ReignWorldActionType.DiplomacyRansomPackage, ReignWorldActionType.DiplomacyBackRebellion,
                ReignWorldActionType.RegularDismissPlayerVassal, ReignWorldActionType.RegularConfirmArrest
            };
            ReignWorldActionType[] excludedPersonalAndTacticalActions =
            {
                ReignWorldActionType.StrategyFormArmy, ReignWorldActionType.StrategyCaptureSettlement,
                ReignWorldActionType.PoliticsStartRulingClanRebellion, ReignWorldActionType.PoliticsInstallRulingClan,
                ReignWorldActionType.PoliticsMarriageAlliance, ReignWorldActionType.PoliticsResolveRebellionPledge,
                ReignWorldActionType.PoliticsResolveRebellionSummons, ReignWorldActionType.RegularRaidVillage,
                ReignWorldActionType.RegularOfferPlayerVassalage, ReignWorldActionType.RegularTransferWorkshop,
                ReignWorldActionType.RegularIssueCampaignOrder, ReignWorldActionType.RegularCancelCampaignOrder
            };
            AddGovernmentAssertion(assertions, "government_reign_political_action_coverage",
                coveredPoliticalActions.All(action => MapActionKind(action) != ReignGovernmentActionKind.Advice)
                && excludedPersonalAndTacticalActions.All(action => MapActionKind(action) == ReignGovernmentActionKind.Advice),
                "The real native action-enum adapter gates constitutional diplomacy, state spending and major justice while leaving tactical orders, personal dealings and internal rebellion lifecycle outside government hearings.",
                new JObject { ["included"] = new JArray(coveredPoliticalActions.Select(x => x.ToString())),
                    ["excluded"] = new JArray(excludedPersonalAndTacticalActions.Select(x => x.ToString())) });
            AddGovernmentAssertion(assertions, "government_reduction_consent_not_self_pressured",
                MapActionKind(ReignWorldActionType.PoliticsConsentGovernmentReduction)
                    == ReignGovernmentActionKind.Advice,
                "Recording an NPC's accepted reduction consent does not itself create a government approval pressure cycle.", null);
            bool separated = Kingdom.All.Where(IsEligibleKingdom).Select(BuildPublicSnapshot)
                .All(snapshot => snapshot.SelectToken("peoplePressure.additionalToNoblePoliticalPressure")?.Value<bool>() == true
                    && snapshot.SelectToken("peoplePressure.probabilityRule")?.Value<string>()
                        ?.IndexOf("does not directly rewrite", StringComparison.OrdinalIgnoreCase) >= 0);
            AddGovernmentAssertion(assertions, "government_people_and_noble_pressure_separate", separated,
                "Government pressure is additive and leaves Public Standing and rebellion probability formulas untouched while feeding loyalty and relationship inputs.", null);
            return GovernmentTestResult(runId, "pressure", assertions);
        }

        private JObject GovernmentSavePrepare(string runId, JObject options, string gameInstanceId)
        {
            JArray assertions = new JArray();
            string caseId = (options?.Value<string>("caseId") ?? string.Empty).Trim();
            if (caseId == "GOV-NATIVE-049") GovernmentFiefSavePrepare(assertions);
            if (caseId == "GOV-NATIVE-052") GovernmentFiefSaveAssert(assertions, true);
            if (caseId.Equals("GOV-NATIVE-022", StringComparison.OrdinalIgnoreCase))
            {
                Kingdom kingdom = Clan.PlayerClan?.Kingdom;
                ReignGovernmentStateRecord state = GetGovernment(kingdom);
                List<Hero> realmMembers = kingdom?.Clans
                    .Where(clan => clan != null && clan != Clan.PlayerClan && !clan.IsEliminated)
                    .SelectMany(clan => clan.Heroes)
                    .Where(hero => IsEligibleMember(hero) && hero != Hero.MainHero)
                    .OrderBy(hero => hero.StringId, StringComparer.Ordinal)
                    .ToList() ?? new List<Hero>();
                Hero clanLeader = realmMembers.FirstOrDefault(hero => hero.Clan?.Leader == hero);
                Hero individual = realmMembers.FirstOrDefault(hero => hero.Clan?.Leader != hero
                    && hero.Clan != clanLeader?.Clan);
                bool seeded = state != null && kingdom?.Leader == Hero.MainHero
                    && clanLeader != null && individual != null;
                string clanResult = string.Empty;
                string individualResult = string.Empty;
                if (seeded)
                {
                    state.Level = 5;
                    state.ReductionConsentFromLevel = 0;
                    state.ReductionConsentHeroIdsCsv = string.Empty;
                    state.ReductionConsentClanIdsCsv = string.Empty;
                    state.Revision++;
                    seeded = TryRecordReductionConsent(kingdom.Leader, clanLeader, out clanResult)
                        && TryRecordReductionConsent(kingdom.Leader, individual, out individualResult);
                }
                bool individualTagPresent = state != null
                    && ParseIds(state.ReductionConsentHeroIdsCsv).Count > 0;
                bool clanTagPresent = state != null
                    && ParseIds(state.ReductionConsentClanIdsCsv).Count > 0;
                AddGovernmentAssertion(assertions, "government_save_prepare_consent_tags",
                    seeded && state?.Level == 5 && state.ReductionConsentFromLevel == 5
                    && individualTagPresent && clanTagPresent,
                    "The persistence checkpoint used the production consent recorder to create level-scoped tags from one real individual and one real clan leader.",
                    new JObject
                    {
                        ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                        ["authorityLevel"] = state?.Level ?? 0,
                        ["consentFromLevel"] = state?.ReductionConsentFromLevel ?? 0,
                        ["individualConsentIds"] = state?.ReductionConsentHeroIdsCsv ?? string.Empty,
                        ["clanConsentIds"] = state?.ReductionConsentClanIdsCsv ?? string.Empty,
                        ["individualHeroId"] = individual?.StringId ?? string.Empty,
                        ["clanLeaderHeroId"] = clanLeader?.StringId ?? string.Empty,
                        ["individualConsentResult"] = individualResult,
                        ["clanConsentResult"] = clanResult
                    });
            }
            string fingerprint = GovernmentFeatureFingerprint();
            AddGovernmentAssertion(assertions, "government_save_prepare_fingerprint", fingerprint.Length == 64,
                "A stable save-backed fingerprint was captured before the external checkpoint/restart boundary.", null);
            AddGovernmentAssertion(assertions, "government_save_prepare_game_instance",
                !string.IsNullOrWhiteSpace(gameInstanceId),
                "The live bridge supplied the current native game-instance marker.", null);
            JObject result = GovernmentTestResult(runId, "save_prepare", assertions);
            result["featureFingerprint"] = fingerprint;
            result["featureFingerprintSchema"] = "reign-government-persistence-v2";
            result["persistenceDiagnostics"] = GovernmentPersistenceDiagnostics();
            result["constitution"] = GovernmentSavedConstitutionEvidence();
            result["gameInstanceId"] = gameInstanceId ?? string.Empty;
            return result;
        }

        private JObject GovernmentSaveVerify(string runId, JObject options, string gameInstanceId)
        {
            JArray assertions = new JArray();
            string expected = (options.Value<string>("expectedFeatureFingerprint") ?? string.Empty).Trim();
            string previous = (options.Value<string>("previousGameInstanceId") ?? string.Empty).Trim();
            string actual = GovernmentFeatureFingerprint();
            AddGovernmentAssertion(assertions, "government_save_verify_different_game_instance",
                previous.Length > 0 && !string.IsNullOrWhiteSpace(gameInstanceId)
                && !string.Equals(previous, gameInstanceId, StringComparison.OrdinalIgnoreCase),
                "Persistence verification ran in a different native game instance from preparation.",
                new JObject { ["previousGameInstanceId"] = previous, ["currentGameInstanceId"] = gameInstanceId ?? string.Empty });
            AddGovernmentAssertion(assertions, "government_save_verify_fingerprint",
                expected.Length == 64 && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase),
                "Government, party, member, resolution, pressure, lobbying, meeting, hearing, and the complete saved participation state survived reload byte-stably.",
                new JObject { ["expected"] = expected, ["actual"] = actual });
            string caseId = (options?.Value<string>("caseId") ?? string.Empty).Trim();
            if (caseId == "GOV-NATIVE-050" || caseId == "GOV-NATIVE-053")
                GovernmentFiefSaveAssert(assertions, caseId == "GOV-NATIVE-053");
            if (caseId.Equals("GOV-NATIVE-035", StringComparison.OrdinalIgnoreCase))
            {
                Kingdom kingdom = Clan.PlayerClan?.Kingdom;
                ReignGovernmentStateRecord state = GetGovernment(kingdom);
                AddGovernmentAssertion(assertions, "government_save_verify_consent_tags",
                    state?.Level == 5 && state.ReductionConsentFromLevel == 5
                    && ParseIds(state.ReductionConsentHeroIdsCsv).Count > 0
                    && ParseIds(state.ReductionConsentClanIdsCsv).Count > 0,
                    "The reloaded native game instance retained the exact level-scoped individual and clan consent tags.",
                    new JObject
                    {
                        ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                        ["authorityLevel"] = state?.Level ?? 0,
                        ["consentFromLevel"] = state?.ReductionConsentFromLevel ?? 0,
                        ["individualConsentIds"] = state?.ReductionConsentHeroIdsCsv ?? string.Empty,
                        ["clanConsentIds"] = state?.ReductionConsentClanIdsCsv ?? string.Empty
                    });
            }
            JObject result = GovernmentTestResult(runId, "save_verify", assertions);
            result["featureFingerprint"] = actual;
            result["featureFingerprintSchema"] = "reign-government-persistence-v2";
            result["persistenceDiagnostics"] = GovernmentPersistenceDiagnostics();
            result["constitution"] = GovernmentSavedConstitutionEvidence();
            result["gameInstanceId"] = gameInstanceId ?? string.Empty;
            return result;
        }

        private JObject GovernmentKingdomEvidence(Kingdom kingdom)
        {
            ReignGovernmentStateRecord state = GetGovernment(kingdom);
            var memberIds = new HashSet<string>(GetSeats(kingdom.StringId).Select(x => x.HeroStringId), StringComparer.Ordinal);
            var attendance = _governmentAttendance.Select(x => new {
                    Lease = x, Business = _business.FirstOrDefault(b => Same(b.BusinessId, x.BusinessId)) })
                .Where(x => x.Business?.KingdomStringId == kingdom.StringId || memberIds.Contains(x.Lease.HeroStringId))
                .GroupBy(x => x.Lease.HeroStringId, StringComparer.Ordinal)
                .OrderByDescending(x => x.Any(row => !row.Lease.Released))
                .ThenByDescending(x => x.Max(row => row.Business?.CreatedDay ?? -1))
                .ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            return new JObject
            {
                ["kingdomId"] = kingdom.StringId,
                ["cultureId"] = kingdom.Culture?.StringId ?? string.Empty,
                ["institution"] = state?.InstitutionName ?? string.Empty,
                ["authorityLevel"] = state?.Level ?? 0,
                ["partyCount"] = GetParties(kingdom.StringId).Count,
                ["seatCount"] = GetSeats(kingdom.StringId).Count,
                ["participation"] = GovernmentParticipationEvidence(kingdom.StringId),
                ["constituencyTotal"] = _participation.Constituencies.Count(row => row.KingdomId == kingdom.StringId),
                ["constituenciesTruncated"] = _participation.Constituencies.Count(row => row.KingdomId == kingdom.StringId) > 64,
                ["constituencies"] = new JArray(_participation.Constituencies.Where(row => row.KingdomId == kingdom.StringId)
                    .OrderBy(row => row.Id, StringComparer.Ordinal).Take(64).Select(row => new JObject {
                        ["id"] = row.Id, ["status"] = row.Status, ["incumbentId"] = row.IncumbentId,
                        ["settlementId"] = row.SettlementId, ["clanId"] = row.ClanId, ["source"] = row.Source.ToString(),
                        ["eligibleSinceDay"] = row.EligibleSinceDay, ["accreditedDay"] = row.AccreditedDay })),
                ["attendanceHeroTotal"] = attendance.Count,
                ["attendanceUnreleasedHeroTotal"] = attendance.Count(x => x.Any(row => !row.Lease.Released)),
                ["attendanceHeroesTruncated"] = attendance.Count > 64,
                ["attendanceResidence"] = new JArray(attendance.Take(64).Select(group => {
                    var hero = FindHero(group.Key);
                    return new JObject { ["heroId"] = group.Key,
                        ["currentSettlementId"] = hero?.CurrentSettlement?.StringId ?? "",
                        ["nativeHomeSettlementId"] = hero?.HomeSettlement?.StringId ?? "",
                        ["isAlive"] = hero?.IsAlive == true, ["isActive"] = hero?.IsActive == true,
                        ["isDisabled"] = hero?.IsDisabled == true,
                        ["leaseTotal"] = group.Count(), ["leasesTruncated"] = group.Count() > 8,
                        ["leases"] = new JArray(group.OrderBy(x => x.Lease.Released)
                            .ThenByDescending(x => x.Business?.CreatedDay ?? -1)
                            .ThenBy(x => x.Lease.BusinessId, StringComparer.Ordinal).Take(8).Select(x => new JObject {
                                ["businessId"] = x.Lease.BusinessId, ["businessKingdomId"] = x.Business?.KingdomStringId ?? "",
                                ["businessStatus"] = x.Business?.Status ?? "missing",
                                ["hostSettlementId"] = x.Lease.HostSettlementStringId,
                                ["originalSettlementId"] = x.Lease.OriginalSettlementStringId,
                                ["homeSettlementId"] = x.Lease.HomeSettlementStringId,
                                ["constituencySettlementId"] = x.Lease.ConstituencySettlementStringId,
                                ["status"] = x.Lease.Status, ["moved"] = x.Lease.Moved, ["returning"] = x.Lease.Returning,
                                ["released"] = x.Lease.Released,
                                ["reason"] = (x.Lease.Reason ?? "").Substring(0, Math.Min(256, (x.Lease.Reason ?? "").Length)),
                                ["reasonTruncated"] = (x.Lease.Reason ?? "").Length > 256 })) };
                })),
                ["stanceTowardRuler"] = state?.StanceTowardRuler ?? 0,
                ["coalitionPartyIds"] = state?.CoalitionPartyIdsCsv ?? string.Empty
            };
        }

        // Test-only observation. Public snapshots expose final ballots after
        // decision, rather than pending absentee instructions.
        private JObject GovernmentParticipationEvidence(string kingdomId)
        {
            var businessIds = new HashSet<string>(_business.Where(x => x.KingdomStringId == kingdomId)
                .Select(x => x.BusinessId), StringComparer.Ordinal);
            var ballots = _participation.Ballots.Where(x => businessIds.Contains(x.BusinessId))
                .OrderByDescending(x => x.SubmittedDay).ThenBy(x => x.BusinessId, StringComparer.Ordinal)
                .ThenBy(x => x.HeroId, StringComparer.Ordinal).ToList();
            var caucuses = _participation.Caucuses.Where(x => x.KingdomId == kingdomId)
                .OrderByDescending(x => x.TargetInterval).ThenBy(x => x.PartyId, StringComparer.Ordinal).ToList();
            var petitions = _participation.Petitions.Where(x => x.KingdomId == kingdomId)
                .OrderByDescending(x => x.AuthorId == Hero.MainHero?.StringId)
                .ThenByDescending(x => x.CreatedInterval).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            return new JObject {
                ["readOnly"] = true, ["memberBallotTotal"] = ballots.Count,
                ["memberBallotsTruncated"] = ballots.Count > 64,
                ["memberBallots"] = new JArray(ballots.Take(64).Select(x => new JObject {
                    ["businessId"] = x.BusinessId, ["heroId"] = x.HeroId, ["optionId"] = x.OptionId,
                    ["abstain"] = x.Abstain, ["source"] = x.Source, ["submittedDay"] = x.SubmittedDay })),
                ["caucusTotal"] = caucuses.Count, ["caucusesTruncated"] = caucuses.Count > 8,
                ["caucuses"] = new JArray(caucuses.Take(8).Select(x => new JObject {
                    ["partyId"] = x.PartyId, ["caucusId"] = GovernmentParticipationRules.CaucusId(x), ["targetInterval"] = x.TargetInterval, ["closed"] = x.Closed,
                    ["adoptedPetitionId"] = x.AdoptedPetitionId, ["rollTotal"] = x.Roll.Count,
                    ["rollTruncated"] = x.Roll.Count > 64, ["roll"] = new JArray(x.Roll.Take(64)),
                    ["nominationTotal"] = x.Nominations.Count, ["nominationsTruncated"] = x.Nominations.Count > 16,
                    ["nominations"] = new JArray(x.Nominations.Take(16)), ["endorsementTotal"] = x.Endorsements.Count,
                    ["endorsementsTruncated"] = x.Endorsements.Count > 64,
                    ["endorsements"] = new JArray(x.Endorsements.OrderBy(e => e.Key, StringComparer.Ordinal).Take(64)
                        .Select(e => new JObject { ["heroId"] = e.Key, ["petitionId"] = e.Value,
                            ["petitionRevision"] = x.EndorsementRevisions.TryGetValue(e.Key, out long revision) ? revision : 0 })),
                    ["selectionBallotTotal"] = x.SelectionBallots.Count, ["selectionBallotsTruncated"] = x.SelectionBallots.Count > 64,
                    ["selectionBallots"] = new JArray(x.SelectionBallots.Take(64).Select(b => new JObject {
                        ["heroId"] = b.ActorId, ["petitionId"] = b.PetitionId, ["petitionRevision"] = b.PetitionRevision, ["eligible"] = b.Eligible })) })),
                ["petitionTotal"] = petitions.Count, ["petitionsTruncated"] = petitions.Count > 16,
                ["petitions"] = new JArray(petitions.Take(16).Select(x => new JObject {
                    ["petitionId"] = x.Id, ["revision"] = x.Revision, ["authorId"] = x.AuthorId,
                    ["proposalId"] = x.ProposalId, ["status"] = x.Status, ["businessId"] = x.BusinessId,
                    ["adoptedPartyId"] = x.AdoptedPartyId, ["createdInterval"] = x.CreatedInterval,
                    ["submittedInterval"] = x.SubmittedInterval, ["resolvedInterval"] = x.ResolvedInterval })) };
        }

        private JObject GovernmentSavedConstitutionEvidence()
        {
            string kingdomId = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty;
            var charter = _participation.Constitutions.FirstOrDefault(x => Same(x.KingdomId, kingdomId));
            return new JObject
            {
                ["available"] = charter != null, ["kingdomId"] = kingdomId,
                ["institutionId"] = charter?.InstitutionId ?? string.Empty,
                ["draftAuthority"] = charter?.DraftAuthority ?? 0,
                ["confirmed"] = charter?.Confirmed ?? false,
                ["source"] = charter?.Source ?? string.Empty, ["revision"] = charter?.Revision ?? 0
            };
        }

        private string GovernmentFeatureFingerprint()
        {
            return GovernmentFingerprintDigest(BuildGovernmentFeatureFingerprintState());
        }

        private JObject BuildGovernmentFeatureFingerprintState()
        {
            return new JObject
            {
                ["governments"] = new JArray(_governments.OrderBy(x => x.KingdomStringId, StringComparer.Ordinal)
                    .Select(x => new JObject { ["kingdom"] = x.KingdomStringId, ["culture"] = x.CultureStringId,
                        ["institution"] = x.InstitutionName, ["kind"] = x.InstitutionKindValue, ["level"] = x.Level,
                        ["trust"] = x.Trust, ["lastMeeting"] = x.LastMeetingDay, ["nextMeeting"] = x.NextMeetingDay,
                        ["dominantParty"] = x.DominantPartyId, ["ruler"] = x.RulerHeroStringId,
                        ["coalition"] = x.CoalitionPartyIdsCsv, ["stance"] = x.StanceTowardRuler,
                        ["reductionConsentFromLevel"] = x.ReductionConsentFromLevel,
                        ["reductionConsentHeroes"] = x.ReductionConsentHeroIdsCsv,
                        ["reductionConsentClans"] = x.ReductionConsentClanIdsCsv,
                        ["revision"] = x.Revision })),
                ["parties"] = new JArray(_parties.OrderBy(x => x.KingdomStringId, StringComparer.Ordinal)
                    .ThenBy(x => x.PartyId, StringComparer.Ordinal).Select(x => JObject.FromObject(x))),
                ["seats"] = new JArray(_seats.OrderBy(x => x.KingdomStringId, StringComparer.Ordinal)
                    .ThenBy(x => x.HeroStringId, StringComparer.Ordinal).Select(x => JObject.FromObject(x))),
                ["resolutions"] = new JArray(_resolutions.OrderBy(x => x.ResolutionId, StringComparer.Ordinal)
                    .Select(x => JObject.FromObject(x))),
                ["pressures"] = new JArray(_pressures.OrderBy(x => x.PressureId, StringComparer.Ordinal)
                    .Select(x => JObject.FromObject(x))),
                ["lobbyRecords"] = new JArray(_lobbyRecords.OrderBy(x => x.LobbyId, StringComparer.Ordinal)
                    .Select(x => JObject.FromObject(x))),
                ["meetings"] = new JArray(_meetings.OrderBy(x => x.MeetingId, StringComparer.Ordinal)
                    .Select(x => JObject.FromObject(x))),
                ["business"] = new JArray(_business.OrderBy(x => x.BusinessId, StringComparer.Ordinal)
                    .Select(x => JObject.FromObject(x))),
                ["ownedNativeDecisionTypes"] = new JArray(_ownedNativeDecisions
                    .Select(x => x?.GetType().FullName ?? string.Empty)),
                ["hearing"] = BuildGovernmentHearingPersistenceSnapshot(),
                // Hash every saved participation field, including private political profiles.
                // Diagnostics expose only digests/counts, never these private values.
                ["participation"] = JObject.FromObject(_participation)
            };
        }

        private static string GovernmentFingerprintDigest(JToken value)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                    value.ToString(Formatting.None)))).Replace("-", string.Empty).ToLowerInvariant();
        }

        // Process-local observations only. They are not saved, do not normalize state,
        // and cannot turn a failed strict-v2 comparison into a passing comparison.
        private readonly JArray _governmentLoadFingerprintPhases = new JArray();

        private static JObject GovernmentFingerprintDiagnostics(JObject value)
        {
            JObject Describe(JToken token) => new JObject {
                ["sha256"] = GovernmentFingerprintDigest(token),
                ["count"] = token is JContainer container ? container.Count : 1 };
            var components = new JObject();
            foreach (var property in value.Properties().Take(16))
                components[property.Name] = Describe(property.Value);
            var participation = new JObject();
            if (value["participation"] is JObject savedParticipation)
                foreach (var property in savedParticipation.Properties().Take(48))
                    participation[property.Name] = Describe(property.Value);
            return new JObject {
                ["schema"] = "reign-government-persistence-diagnostics-v1",
                ["featureFingerprint"] = GovernmentFingerprintDigest(value),
                ["components"] = components,
                ["componentsTruncated"] = value.Count > 16,
                ["participationComponents"] = participation,
                ["participationComponentsTruncated"] = (value["participation"] as JObject)?.Count > 48 };
        }

        private void CaptureGovernmentLoadFingerprintPhase(string phase, bool reset = false)
        {
            if (reset) _governmentLoadFingerprintPhases.Clear();
            if (_governmentLoadFingerprintPhases.Count >= 4) return;
            try
            {
                var diagnostic = GovernmentFingerprintDiagnostics(BuildGovernmentFeatureFingerprintState());
                diagnostic["phase"] = phase;
                _governmentLoadFingerprintPhases.Add(diagnostic);
            }
            catch (Exception error)
            {
                // Optional observations must not interrupt a production load or publish values.
                _governmentLoadFingerprintPhases.Add(new JObject {
                    ["phase"] = phase, ["available"] = false, ["errorType"] = error.GetType().Name });
            }
        }

        private JObject GovernmentPersistenceDiagnostics()
        {
            var diagnostic = GovernmentFingerprintDiagnostics(BuildGovernmentFeatureFingerprintState());
            diagnostic["loadPhases"] = _governmentLoadFingerprintPhases.DeepClone();
            return diagnostic;
        }

        private static JObject GovernmentTestResult(string runId, string profile, JArray assertions)
        {
            bool ok = assertions.Children<JObject>().All(row => row.Value<bool?>("passed") == true);
            return new JObject
            {
                ["ok"] = ok, ["schema"] = "reign-government-test-report-v1",
                ["runId"] = runId ?? string.Empty, ["profile"] = profile ?? string.Empty,
                ["nativeInterception"] = ReignGovernmentPatches.BuildDiagnostics(),
                ["assertions"] = assertions, ["failedAssertionCount"] = assertions.Children<JObject>()
                    .Count(row => row.Value<bool?>("passed") != true),
                ["capturedUtc"] = DateTime.UtcNow.ToString("o")
            };
        }

        private static void AddGovernmentAssertion(JArray assertions, string id, bool passed,
            string statement, JObject evidence)
        {
            assertions.Add(new JObject
            {
                ["id"] = id ?? string.Empty, ["passed"] = passed,
                ["statement"] = statement ?? string.Empty,
                ["evidence"] = evidence ?? new JObject()
            });
        }

        private static bool TryRequireGovernmentTestSave(JObject options,
            out string expected, out string active, out string error)
        {
            expected = (options?.Value<string>("expectedSaveName") ?? string.Empty).Trim();
            active = ReignServerClient.ActiveNativeSaveName().Trim();
            error = string.Empty;
            if (expected.Length == 0) error = "The exact MCP-authorized campaign-test Current save name is required.";
            else if (active.Length == 0) error = "Bannerlord did not expose an active loaded save identity.";
            else if (!string.Equals(expected, active, StringComparison.OrdinalIgnoreCase))
                error = "The active Bannerlord save does not match the exact MCP-authorized campaign-test Current.";
            return error.Length == 0;
        }

        private static JObject GovernmentTestFailure(string runId, string profile, string error,
            string expected, string active)
        {
            return new JObject
            {
                ["ok"] = false, ["schema"] = "reign-government-test-report-v1",
                ["runId"] = runId ?? string.Empty, ["profile"] = profile ?? string.Empty,
                ["error"] = error ?? string.Empty, ["expectedSaveName"] = expected ?? string.Empty,
                ["activeSaveName"] = active ?? string.Empty
            };
        }
    }
}
