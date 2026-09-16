using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Runtime;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.PartyAgency
{
    public sealed class ReignTemporaryPartyGuestCampaignBehavior : CampaignBehaviorBase
    {
        public const float OpenEndedReviewDays = 5f;
        public const float MaximumFixedTermDays = 30f;
        public const float MinimumFixedTermDays = 1f;
        public const float MinimumReturnHours = 1f;
        public const float MaximumReturnHours = 12f;

        private List<ReignTemporaryPartyGuestRecord> _records = new List<ReignTemporaryPartyGuestRecord>();
        private bool _reviewInquiryOpen;
        private string _reviewHeroStringId = string.Empty;
        private bool _ownsReviewTimeLock;
        private readonly Dictionary<string, ReviewCloseResolution> _reviewCloseResolutions =
            new Dictionary<string, ReviewCloseResolution>(StringComparer.OrdinalIgnoreCase);

        public static ReignTemporaryPartyGuestCampaignBehavior Instance { get; private set; }

        public IReadOnlyList<ReignTemporaryPartyGuestRecord> Records =>
            (_records ?? new List<ReignTemporaryPartyGuestRecord>()).Where(x => x != null).ToList();

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, OnGameLoadFinished);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            CampaignEvents.CanHeroEquipmentBeChangedEvent.AddNonSerializedListener(this, OnCanHeroEquipmentBeChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reign_temporary_party_guests_v1", ref _records);
            if (_records == null) _records = new List<ReignTemporaryPartyGuestRecord>();
        }

        public bool IsProtectedCamp(MobileParty party)
        {
            if (party == null || string.IsNullOrWhiteSpace(party.StringId)) return false;
            return (_records ?? new List<ReignTemporaryPartyGuestRecord>()).Any(record =>
                record != null
                && record.IsLive
                && record.Phase != ReignTemporaryGuestPhase.Completed
                && string.Equals(record.SourcePartyStringId, party.StringId, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsGuestClanLeaderInMainParty(Clan clan)
        {
            Hero leader = clan?.Leader;
            if (leader == null || leader.PartyBelongedTo != MobileParty.MainParty) return false;
            return FindLiveRecord(leader.StringId) != null;
        }

        public bool IsTemporaryGuestEquipmentLocked(Hero hero)
        {
            return hero != null
                && hero.PartyBelongedTo == MobileParty.MainParty
                && FindLiveRecord(hero.StringId) != null;
        }

        private void OnCanHeroEquipmentBeChanged(Hero hero, ref bool canBeChanged)
        {
            if (IsTemporaryGuestEquipmentLocked(hero)) canBeChanged = false;
        }

        public ReignActionResult AcceptGuest(ReignWorldActionRecord action, Hero hero)
        {
            if (!ValidateAcceptAction(action, hero, out string reason))
                return ReignActionResult.ValidationFailed(reason);

            JObject terms = ParseTerms(action.TermsJson);
            float now = CurrentDay();
            ReignTemporaryGuestTermKind termKind = ParseTermKind(terms);
            float fixedDays = termKind == ReignTemporaryGuestTermKind.Fixed
                ? ReadFloat(terms, "durationDays", 0f)
                : 0f;
            string purpose = CleanPurpose(ReadString(terms, "purpose", action.Reason));
            bool armyExitAccepted = ReadBool(terms, "armyExitAccepted", false);
            MobileParty priorParty = hero.PartyBelongedTo;
            Settlement priorSettlement = hero.CurrentSettlement;
            int residentPayment = 0;
            MobileParty sourceParty = priorParty != null && priorParty.LeaderHero == hero
                ? priorParty
                : null;

            ReignTemporaryPartyGuestRecord record = new ReignTemporaryPartyGuestRecord
            {
                HeroStringId = hero.StringId,
                OriginalClanStringId = hero.Clan?.StringId ?? string.Empty,
                OriginalKingdomStringId = hero.Clan?.Kingdom?.StringId ?? string.Empty,
                OriginalCompanionClanStringId = hero.CompanionOf?.StringId ?? string.Empty,
                SourcePartyStringId = sourceParty?.StringId ?? string.Empty,
                Purpose = purpose,
                TermKind = termKind,
                StartedDay = now,
                FixedTermDays = fixedDays,
                ReviewDueDay = now + (termKind == ReignTemporaryGuestTermKind.Fixed ? fixedDays : OpenEndedReviewDays),
                Phase = ReignTemporaryGuestPhase.Active,
                ArmyExitAccepted = armyExitAccepted,
                LastStateChangeDay = now
            };

            try
            {
                if (priorParty?.Army != null)
                {
                    if (priorParty.Army.LeaderParty == priorParty)
                        DisbandArmyAction.ApplyByUnknownReason(priorParty.Army);
                    else
                        priorParty.Army = null;
                }

                if (sourceParty != null)
                {
                    if (sourceParty.CurrentSettlement != null)
                        LeaveSettlementAction.ApplyForParty(sourceParty);
                    CaptureAndProtectCamp(record, sourceParty, hero);
                }

                AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, false);
                if (hero.PartyBelongedTo != MobileParty.MainParty)
                    throw new InvalidOperationException("Bannerlord did not add the consenting noble to the player party.");
                if (!IdentityMatches(record, hero))
                    throw new InvalidOperationException("The native party transfer changed a permanent noble affiliation.");

                _records.Add(record);
                if (ReignEncounteredResidentsCampaignBehavior.Instance?.Find(hero) != null)
                {
                    int payment = terms.Value<int?>("agreedGold") ?? 0;
                    if (payment > 0)
                    {
                        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, hero, payment, true);
                        residentPayment = payment;
                    }
                    ReignEncounteredResidentsCampaignBehavior.Instance.DetachDepartingRole(hero);
                }
                InformationManager.DisplayMessage(new InformationMessage(
                    hero.Name + " has joined your party temporarily for: " + purpose + "."));
                return ReignActionResult.Done(hero.Name + " joined the player party as a temporary guest.")
                    .WithEffect("temporary_party_guest_joined", "hero", hero.StringId, hero.Name.ToString(),
                        "agreementId=" + record.AgreementId + ";reviewDay=" + record.ReviewDueDay)
                    .WithChangedEntity("hero", hero.StringId, hero.Name.ToString(), "party_membership_changed");
            }
            catch (Exception ex)
            {
                _records.Remove(record);
                if (residentPayment > 0)
                    GiveGoldAction.ApplyBetweenCharacters(hero, Hero.MainHero, residentPayment, true);
                if (hero.PartyBelongedTo == MobileParty.MainParty)
                    MobileParty.MainParty.MemberRoster.AddToCounts(hero.CharacterObject, -1);
                if (priorParty != null && priorParty.IsActive && hero.IsAlive && !hero.IsPrisoner)
                {
                    if (sourceParty == priorParty)
                        TeleportHeroAction.ApplyImmediateTeleportToPartyAsPartyLeader(hero, priorParty);
                    else
                        AddHeroToPartyAction.Apply(hero, priorParty, false);
                }
                else if (priorSettlement != null && hero.IsAlive && !hero.IsPrisoner)
                    EnterSettlementAction.ApplyForCharacterOnly(hero, priorSettlement);
                ReleaseCamp(record, false);
                return ReignActionResult.FailTerminal(ex.Message, "temporary_guest_join_failed", "native_party_transfer");
            }
        }

        public bool ValidateAction(ReignWorldActionRecord action, Hero hero, out string reason)
        {
            if (action?.Type == ReignWorldActionType.RegularAcceptTemporaryPartyGuest)
                return ValidateAcceptAction(action, hero, out reason);
            reason = string.Empty;
            ReignTemporaryPartyGuestRecord record = FindLiveRecord(hero?.StringId);
            if (record == null || hero == null)
            {
                reason = "This action requires an active temporary noble guest agreement.";
                return false;
            }
            if (action.Type == ReignWorldActionType.RegularRenewTemporaryPartyGuest)
            {
                JObject terms = ParseTerms(action.TermsJson);
                if (hero.PartyBelongedTo != MobileParty.MainParty)
                {
                    reason = "Only a guest who is still in the player party can renew the agreement.";
                    return false;
                }
                if (!ReadBool(terms, "consentConfirmed", false))
                {
                    reason = "Renewal requires fresh, explicit NPC consent.";
                    return false;
                }
                ReignTemporaryGuestTermKind kind = ParseTermKind(terms);
                float days = ReadFloat(terms, "durationDays", 0f);
                if (kind == ReignTemporaryGuestTermKind.Fixed
                    && (days < MinimumFixedTermDays || days > MaximumFixedTermDays))
                {
                    reason = "A fixed guest agreement must last from 1 through 30 campaign days.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(CleanPurpose(ReadString(terms, "purpose", record.Purpose))))
                {
                    reason = "A temporary guest agreement requires a narrative purpose.";
                    return false;
                }
            }
            if (action.Type == ReignWorldActionType.RegularAcknowledgeOwnFactionCombatRisk)
            {
                if (string.IsNullOrWhiteSpace(record.HostileFactionStringId)
                    || !ReadBool(ParseTerms(action.TermsJson), "consentConfirmed", false))
                {
                    reason = "The guest must explicitly accept an active own-faction combat warning.";
                    return false;
                }
            }
            return true;
        }

        public ReignActionResult RenewGuest(ReignWorldActionRecord action, Hero hero)
        {
            ReignTemporaryPartyGuestRecord record = FindLiveRecord(hero?.StringId);
            if (record == null || hero?.PartyBelongedTo != MobileParty.MainParty)
                return ReignActionResult.ValidationFailed("RenewTemporaryPartyGuest requires an active guest in the player party.");
            JObject terms = ParseTerms(action.TermsJson);
            if (!ReadBool(terms, "consentConfirmed", false))
                return ReignActionResult.ValidationFailed("The noble must explicitly consent to the renewed temporary-party agreement.");
            if (!string.IsNullOrWhiteSpace(record.HostileFactionStringId)
                && !record.HostilityWarningAcknowledged)
                return ReignActionResult.ValidationFailed("The own-faction combat warning must be acknowledged before this guest can remain.");

            ReignTemporaryGuestTermKind kind = ParseTermKind(terms);
            float days = kind == ReignTemporaryGuestTermKind.Fixed
                ? ReadFloat(terms, "durationDays", 0f)
                : 0f;
            if (kind == ReignTemporaryGuestTermKind.Fixed
                && (days < MinimumFixedTermDays || days > MaximumFixedTermDays))
                return ReignActionResult.ValidationFailed("A fixed guest agreement must last from 1 through 30 campaign days.");
            string purpose = CleanPurpose(ReadString(terms, "purpose", record.Purpose));
            if (string.IsNullOrWhiteSpace(purpose))
                return ReignActionResult.ValidationFailed("A temporary guest agreement requires a narrative purpose.");

            float now = CurrentDay();
            record.Purpose = purpose;
            record.TermKind = kind;
            record.FixedTermDays = days;
            record.ReviewDueDay = now + (kind == ReignTemporaryGuestTermKind.Fixed ? days : OpenEndedReviewDays);
            record.Phase = ReignTemporaryGuestPhase.Active;
            record.LastReviewReason = string.Empty;
            record.LastStateChangeDay = now;
            _reviewCloseResolutions.Remove(hero.StringId);
            return ReignActionResult.Done(hero.Name + " renewed the temporary party agreement.")
                .WithEffect("temporary_party_guest_renewed", "hero", hero.StringId, hero.Name.ToString(),
                    "agreementId=" + record.AgreementId + ";reviewDay=" + record.ReviewDueDay);
        }

        public ReignActionResult EndGuest(ReignWorldActionRecord action, Hero hero)
        {
            ReignTemporaryPartyGuestRecord record = FindLiveRecord(hero?.StringId);
            if (record == null)
                return ReignActionResult.ValidationFailed("EndTemporaryPartyGuest requires an active temporary guest.");
            record.Phase = ReignTemporaryGuestPhase.Departing;
            record.LastReviewReason = ReadString(ParseTerms(action.TermsJson), "departureReason", action.Reason);
            record.LastStateChangeDay = CurrentDay();
            if (!ReignPartyChatScreenManager.IsOpen) BeginDeparture(record);
            return ReignActionResult.Done(hero.Name + " ended the temporary party agreement.")
                .WithEffect("temporary_party_guest_departing", "hero", hero.StringId, hero.Name.ToString(),
                    "agreementId=" + record.AgreementId);
        }

        public ReignActionResult AcknowledgeOwnFactionRisk(ReignWorldActionRecord action, Hero hero)
        {
            ReignTemporaryPartyGuestRecord record = FindLiveRecord(hero?.StringId);
            if (record == null || string.IsNullOrWhiteSpace(record.HostileFactionStringId))
                return ReignActionResult.ValidationFailed("There is no active own-faction combat warning for this guest.");
            if (!ReadBool(ParseTerms(action.TermsJson), "consentConfirmed", false))
                return ReignActionResult.ValidationFailed("The guest must explicitly acknowledge the banishment risk.");

            record.HostilityWarningAcknowledged = true;
            record.LastStateChangeDay = CurrentDay();
            if (record.ReviewDueDay > CurrentDay())
            {
                record.Phase = ReignTemporaryGuestPhase.Active;
                record.LastReviewReason = string.Empty;
            }
            return ReignActionResult.Done(hero.Name + " explicitly accepted the own-faction combat risk.")
                .WithEffect("temporary_party_guest_hostility_warning_acknowledged", "hero", hero.StringId,
                    hero.Name.ToString(), "factionId=" + record.HostileFactionStringId);
        }

        public bool ValidateAcceptAction(ReignWorldActionRecord action, Hero hero, out string reason)
        {
            reason = string.Empty;
            bool resident = ReignEncounteredResidentsCampaignBehavior.Instance?.Find(hero) != null;
            if (resident && !ReignEncounteredResidentsCampaignBehavior.Instance.ValidateDeparture(hero, out reason)) return false;
            if (hero == null || hero == Hero.MainHero || (!resident && !hero.IsLord) || hero.CompanionOf != null
                || !ReignConversationEligibility.IsAdultLivingNpc(hero) || hero.IsPrisoner)
            {
                reason = "A temporary noble guest must be a living, adult, free NPC lord or lady.";
                return false;
            }
            if (!resident && (hero.Clan == null || hero.Clan == Clan.PlayerClan))
            {
                reason = "Temporary noble guests must retain membership in a different clan.";
                return false;
            }
            if (hero.GovernorOf != null)
            {
                reason = "A governor must relinquish the governorship before joining as a temporary guest.";
                return false;
            }
            if (FindLiveRecord(hero.StringId) != null)
            {
                reason = "This noble already has an active temporary-party agreement.";
                return false;
            }
            MobileParty party = hero.PartyBelongedTo;
            if (party?.IsCurrentlyUsedByAQuest == true)
            {
                reason = "A noble whose party is required by an active quest cannot become a temporary guest.";
                return false;
            }
            if (party != null && (party.MapEvent != null || party.SiegeEvent != null || party.BesiegerCamp != null))
            {
                reason = "The noble cannot leave a party that is in battle or committed to a siege.";
                return false;
            }
            JObject terms = ParseTerms(action?.TermsJson);
            if (resident && (terms["agreedGold"] != null && (terms["agreedGold"].Type != JTokenType.Integer
                || terms.Value<long>("agreedGold") < 0 || terms.Value<long>("agreedGold") > Hero.MainHero.Gold)))
            {
                reason = "The exact agreed guest payment must be nonnegative and affordable.";
                return false;
            }
            if (!ReadBool(terms, "consentConfirmed", false))
            {
                reason = "The noble must explicitly consent to the temporary-party agreement.";
                return false;
            }
            string purpose = CleanPurpose(ReadString(terms, "purpose", action?.Reason));
            if (string.IsNullOrWhiteSpace(purpose))
            {
                reason = "A temporary-party agreement requires a narrative purpose.";
                return false;
            }
            ReignTemporaryGuestTermKind kind = ParseTermKind(terms);
            float days = ReadFloat(terms, "durationDays", 0f);
            if (kind == ReignTemporaryGuestTermKind.Fixed
                && (days < MinimumFixedTermDays || days > MaximumFixedTermDays))
            {
                reason = "A fixed guest agreement must last from 1 through 30 campaign days.";
                return false;
            }
            if (party?.Army != null && !ReadBool(terms, "armyExitAccepted", false))
            {
                reason = "Leaving an army must be accepted as a separate part of the agreement.";
                return false;
            }
            if (!IsInPerson(hero))
            {
                reason = "Initial temporary-party invitations must be made in person.";
                return false;
            }
            return true;
        }

        public void ApplicationTick()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || MobileParty.MainParty == null) return;
            PromotePastDueReviews();
            ResolveClosedReviews();
            TryShowDueReview();
            RefreshReviewTimeLock();
        }

        private void PromotePastDueReviews()
        {
            float now = CurrentDay();
            foreach (ReignTemporaryPartyGuestRecord record in Records
                .Where(x => x.Phase == ReignTemporaryGuestPhase.Active
                    && now >= x.ReviewDueDay).ToList())
            {
                Hero hero = FindHero(record.HeroStringId);
                if (hero == null || hero.IsDead || hero.IsPrisoner) continue;
                if (hero.PartyBelongedTo != MobileParty.MainParty
                    && !record.TemporarilyExcludedFromBattle) continue;
                record.Phase = ReignTemporaryGuestPhase.ReviewDue;
                record.LastReviewReason = record.TermKind == ReignTemporaryGuestTermKind.Fixed
                    ? "The agreed fixed term has ended."
                    : "The five-day open-ended review is due.";
                record.LastStateChangeDay = now;
            }
        }

        public JObject BuildConversationContext(Hero hero)
        {
            if (hero == null || TaleWorlds.CampaignSystem.Campaign.Current == null) return new JObject();
            ReignTemporaryPartyGuestRecord record = FindLiveRecord(hero.StringId)
                ?? Records.LastOrDefault(x => string.Equals(x.HeroStringId, hero.StringId, StringComparison.OrdinalIgnoreCase));
            if (record == null) return new JObject();
            return new JObject
            {
                ["schema"] = "reign-temporary-guest-dialogue-v1",
                ["enabled"] = true,
                ["heroId"] = hero.StringId,
                ["agreementId"] = record.AgreementId,
                ["purpose"] = record.Purpose,
                ["termKind"] = record.TermKind.ToString(),
                ["phase"] = record.Phase.ToString(),
                ["startedDay"] = record.StartedDay,
                ["reviewDueDay"] = record.ReviewDueDay,
                ["returnDueDay"] = record.ReturnDueDay,
                ["returnSettlementId"] = record.ReturnSettlementStringId,
                ["inMainParty"] = MobileParty.MainParty != null && hero.PartyBelongedTo == MobileParty.MainParty,
                ["temporarilyExcludedFromBattle"] = record.TemporarilyExcludedFromBattle,
                ["departureDeferredForChat"] = record.Phase == ReignTemporaryGuestPhase.Departing && ReignPartyChatScreenManager.IsOpen,
                ["observedWorldDay"] = CampaignTime.Now.ToDays
            };
        }

        public JObject BuildHarnessSnapshot()
        {
            JArray records = new JArray(Records.Select(record => new JObject
            {
                ["agreementId"] = record.AgreementId,
                ["heroId"] = record.HeroStringId,
                ["sourcePartyId"] = record.SourcePartyStringId,
                ["purpose"] = record.Purpose,
                ["termKind"] = record.TermKind.ToString(),
                ["phase"] = record.Phase.ToString(),
                ["reviewDueDay"] = record.ReviewDueDay,
                ["returnDueDay"] = record.ReturnDueDay,
                ["hostilityWarningAcknowledged"] = record.HostilityWarningAcknowledged,
                ["temporarilyExcludedFromBattle"] = record.TemporarilyExcludedFromBattle,
                ["ownFactionCombatPending"] = record.OwnFactionCombatPending,
                ["returnSettlementId"] = record.ReturnSettlementStringId,
                ["originalClanId"] = record.OriginalClanStringId,
                ["originalKingdomId"] = record.OriginalKingdomStringId,
                ["originalCompanionClanId"] = record.OriginalCompanionClanStringId,
                ["identityPreserved"] = IdentityMatches(record, FindHero(record.HeroStringId)),
                ["campProtected"] = string.IsNullOrWhiteSpace(record.SourcePartyStringId)
                    || IsProtectedCamp(FindParty(record.SourcePartyStringId))
            }));
            return new JObject
            {
                ["ok"] = true,
                ["schema"] = "reign-party-agency-harness-v1",
                ["recordCount"] = records.Count,
                ["activeCount"] = Records.Count(record => record.IsLive),
                ["reviewDueCount"] = Records.Count(record => record.Phase == ReignTemporaryGuestPhase.ReviewDue),
                ["returningCount"] = Records.Count(record => record.Phase == ReignTemporaryGuestPhase.Returning),
                ["completedCount"] = Records.Count(record => record.Phase == ReignTemporaryGuestPhase.Completed),
                ["records"] = records,
                ["policy"] = new JObject
                {
                    ["openEndedReviewDays"] = OpenEndedReviewDays,
                    ["fixedTermMinimumDays"] = MinimumFixedTermDays,
                    ["fixedTermMaximumDays"] = MaximumFixedTermDays,
                    ["minimumReturnHours"] = MinimumReturnHours,
                    ["maximumReturnHours"] = MaximumReturnHours
                }
            };
        }

        private void OnGameLoadFinished()
        {
            foreach (ReignTemporaryPartyGuestRecord record in Records.Where(x => x.IsLive))
            {
                MobileParty camp = FindParty(record.SourcePartyStringId);
                if (camp != null && record.Phase != ReignTemporaryGuestPhase.Completed)
                    EnforceCampProtection(record, camp);
            }
        }

        private void OnHourlyTick()
        {
            float now = CurrentDay();
            foreach (ReignTemporaryPartyGuestRecord record in Records.Where(x => x.IsLive).ToList())
            {
                Hero hero = FindHero(record.HeroStringId);
                MobileParty camp = FindParty(record.SourcePartyStringId);
                if (camp != null && record.Phase != ReignTemporaryGuestPhase.Completed)
                    EnforceCampProtection(record, camp);

                if (hero == null || hero.IsDead || hero.IsPrisoner)
                {
                    CompleteUnavailable(record);
                    continue;
                }
                if (record.Phase == ReignTemporaryGuestPhase.Returning && now >= record.ReturnDueDay)
                {
                    CompleteReturn(record, hero);
                    continue;
                }
                if (record.Phase == ReignTemporaryGuestPhase.Departing && !ReignPartyChatScreenManager.IsOpen)
                {
                    BeginDeparture(record);
                    continue;
                }
                if (record.Phase == ReignTemporaryGuestPhase.Active)
                {
                    if (hero.PartyBelongedTo != MobileParty.MainParty && !record.TemporarilyExcludedFromBattle)
                    {
                        CompleteUnavailable(record);
                        continue;
                    }
                    DetectHostilityReview(record, hero);
                    if (record.Phase == ReignTemporaryGuestPhase.Active && now >= record.ReviewDueDay)
                    {
                        record.Phase = ReignTemporaryGuestPhase.ReviewDue;
                        record.LastReviewReason = record.TermKind == ReignTemporaryGuestTermKind.Fixed
                            ? "The agreed fixed term has ended."
                            : "The five-day open-ended review is due.";
                        record.LastStateChangeDay = now;
                    }
                }
            }
        }

        private void DetectHostilityReview(ReignTemporaryPartyGuestRecord record, Hero hero)
        {
            IFaction originalFaction = ResolveOriginalFaction(record, hero);
            IFaction playerFaction = Hero.MainHero?.MapFaction;
            if (originalFaction == null || playerFaction == null
                || !originalFaction.IsAtWarWith(playerFaction)) return;
            if (record.HostilityWarningAcknowledged
                && string.Equals(record.HostileFactionStringId, originalFaction.StringId,
                    StringComparison.OrdinalIgnoreCase)) return;
            record.HostileFactionStringId = originalFaction.StringId;
            record.HostilityWarningAcknowledged = false;
            record.Phase = ReignTemporaryGuestPhase.ReviewDue;
            record.LastReviewReason = "Their faction is now hostile. Remaining requires explicit acceptance of banishment if they fight their own faction.";
            record.LastStateChangeDay = CurrentDay();
        }

        private void OnMapEventStarted(MapEvent mapEvent, PartyBase attacker, PartyBase defender)
        {
            if (mapEvent == null || !mapEvent.IsPlayerMapEvent) return;
            BattleSideEnum opponent = mapEvent.PlayerSide.GetOppositeSide();
            foreach (ReignTemporaryPartyGuestRecord record in Records.Where(x => x.IsLive).ToList())
            {
                Hero hero = FindHero(record.HeroStringId);
                if (hero == null || hero.IsPrisoner || hero.IsWounded) continue;
                if (!OpponentContainsOriginalFaction(mapEvent, opponent, record)) continue;
                if (!record.HostilityWarningAcknowledged)
                {
                    if (hero.PartyBelongedTo == MobileParty.MainParty)
                    {
                        MobileParty.MainParty.MemberRoster.AddToCounts(hero.CharacterObject, -1);
                        record.TemporarilyExcludedFromBattle = true;
                    }
                    continue;
                }
                record.OwnFactionCombatPending = true;
                record.Phase = ReignTemporaryGuestPhase.HostileBanishmentPending;
                record.LastStateChangeDay = CurrentDay();
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            foreach (ReignTemporaryPartyGuestRecord record in Records.Where(x => x.IsLive).ToList())
            {
                Hero hero = FindHero(record.HeroStringId);
                if (record.TemporarilyExcludedFromBattle)
                {
                    record.TemporarilyExcludedFromBattle = false;
                    if (hero != null && hero.IsAlive && !hero.IsPrisoner && hero.PartyBelongedTo == null)
                        AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, false);
                }
                ApplyVerifiedOwnFactionCombatResult(record, hero);
            }
        }

        internal bool ApplyVerifiedOwnFactionCombatResult(
            ReignTemporaryPartyGuestRecord record, Hero hero)
        {
            if (record == null || !record.OwnFactionCombatPending) return false;
            ApplyHostileBanishment(record, hero);
            return record.Phase == ReignTemporaryGuestPhase.Completed
                && hero?.Clan == Clan.PlayerClan;
        }

        private void ApplyHostileBanishment(ReignTemporaryPartyGuestRecord record, Hero hero)
        {
            record.OwnFactionCombatPending = false;
            if (hero == null || hero.IsDead || hero.IsPrisoner)
            {
                CompleteUnavailable(record);
                return;
            }
            Clan oldClan = hero.Clan;
            if (oldClan != null && oldClan != Clan.PlayerClan && oldClan.Leader == hero)
            {
                Hero successor = oldClan.GetHeirApparents()
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key?.StringId)
                    .Select(x => x.Key)
                    .FirstOrDefault(x => x != null && x != hero && x.IsAlive);
                if (successor == null)
                    successor = oldClan.Heroes.FirstOrDefault(x => x != null && x != hero && x.IsAlive && x.IsLord);
                if (successor != null)
                {
                    oldClan.SetLeader(successor);
                    CampaignEventDispatcher.Instance.OnClanLeaderChanged(hero, successor);
                }
            }
            if (oldClan?.Leader == hero)
            {
                CompleteUnavailable(record);
                InformationManager.DisplayMessage(new InformationMessage(
                    hero.Name + " cannot be transferred because their former clan has no valid successor."));
                return;
            }
            hero.Clan = Clan.PlayerClan;
            if (hero.PartyBelongedTo != MobileParty.MainParty)
                AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, false);
            record.Phase = ReignTemporaryGuestPhase.Completed;
            record.LastStateChangeDay = CurrentDay();
            ReleaseCamp(record, true);
            InformationManager.DisplayMessage(new InformationMessage(
                hero.Name + " has been banished by their former faction and is now a member of your clan."));
        }

        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            if (party == null) return;
            foreach (ReignTemporaryPartyGuestRecord record in Records.Where(x => x.IsLive
                && string.Equals(x.SourcePartyStringId, party.StringId, StringComparison.OrdinalIgnoreCase)))
            {
                record.SourcePartyStringId = string.Empty;
            }
        }

        private void CaptureAndProtectCamp(ReignTemporaryPartyGuestRecord record, MobileParty party, Hero hero)
        {
            record.CampX = party.Position.X;
            record.CampY = party.Position.Y;
            record.CampIsOnLand = party.Position.IsOnLand;
            record.ProtectedCampMorale = party.Morale;
            record.ProtectedCampMoraleCaptured = true;
            record.SourcePartyHadCustomName = !TextObject.IsNullOrEmpty(party.Party.CustomName);
            record.SourcePartyOriginalName = party.Party.CustomName?.ToString() ?? string.Empty;
            EnforceCampProtection(record, party);
            party.Party.SetCustomName(new TextObject(hero.Name + "'s Waiting Camp"));
        }

        private void EnforceCampProtection(ReignTemporaryPartyGuestRecord record, MobileParty party)
        {
            if (party == null || !party.IsActive) return;
            if (!record.ProtectedCampMoraleCaptured)
            {
                record.ProtectedCampMorale = party.Morale;
                record.ProtectedCampMoraleCaptured = true;
            }
            party.IsVisible = true;
            party.IsDisbanding = false;
            party.SetPartyUsedByQuest(true);
            party.Ai.SetDoNotMakeNewDecisions(true);
            party.SetMoveModeHold();
            party.IgnoreByOtherPartiesTill(CampaignTime.Never);
            float moraleDelta = record.ProtectedCampMorale - party.Morale;
            if (Math.Abs(moraleDelta) >= 0.001f)
                party.RecentEventsMorale += moraleDelta;
            CampaignVec2 fixedPosition = new CampaignVec2(new Vec2(record.CampX, record.CampY), record.CampIsOnLand);
            if (party.Position != fixedPosition) party.Position = fixedPosition;
        }

        private void ReleaseCamp(ReignTemporaryPartyGuestRecord record, bool useNativeLeaderlessHandling)
        {
            MobileParty party = FindParty(record?.SourcePartyStringId);
            if (party == null) return;
            party.IgnoreByOtherPartiesTill(CampaignTime.Now);
            party.SetPartyUsedByQuest(false);
            party.Ai.SetDoNotMakeNewDecisions(false);
            party.IsDisbanding = false;
            if (record.SourcePartyHadCustomName && !string.IsNullOrWhiteSpace(record.SourcePartyOriginalName))
                party.Party.SetCustomName(new TextObject(record.SourcePartyOriginalName));
            else
                party.Party.SetCustomName(TextObject.GetEmpty());
            if (useNativeLeaderlessHandling && party.LeaderHero == null)
                DisbandPartyAction.StartDisband(party);
        }

        private void BeginDeparture(ReignTemporaryPartyGuestRecord record)
        {
            Hero hero = FindHero(record.HeroStringId);
            if (hero == null || hero.IsDead || hero.IsPrisoner)
            {
                CompleteUnavailable(record);
                return;
            }
            MobileParty destinationParty = FindParty(record.SourcePartyStringId);
            Settlement destinationSettlement = destinationParty == null ? FindSafeReturnSettlement(hero) : null;
            record.ReturnSettlementStringId = destinationSettlement?.StringId ?? string.Empty;
            float hours = CalculateReturnHours(MobileParty.MainParty?.Position,
                destinationParty?.Position ?? destinationSettlement?.GatePosition);
            record.ReturnDueDay = CurrentDay() + hours / CampaignTime.HoursInDay;
            record.Phase = ReignTemporaryGuestPhase.Returning;
            record.LastStateChangeDay = CurrentDay();
            if (hero.PartyBelongedTo == MobileParty.MainParty)
                MobileParty.MainParty.MemberRoster.AddToCounts(hero.CharacterObject, -1);
            if (hero.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(hero);
            hero.ChangeState(Hero.CharacterStates.Traveling);
            InformationManager.DisplayMessage(new InformationMessage(
                hero.Name + " has left your party and will arrive in about " + Math.Ceiling(hours) + " hours."));
        }

        private void CompleteReturn(ReignTemporaryPartyGuestRecord record, Hero hero)
        {
            MobileParty party = FindParty(record.SourcePartyStringId);
            if (party != null && party.IsActive)
            {
                TeleportHeroAction.ApplyImmediateTeleportToPartyAsPartyLeader(hero, party);
                ReleaseCamp(record, false);
            }
            else
            {
                Settlement settlement = FindSettlement(record.ReturnSettlementStringId) ?? FindSafeReturnSettlement(hero);
                if (settlement != null)
                    TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, settlement);
                else
                    hero.ChangeState(Hero.CharacterStates.Active);
            }
            record.Phase = ReignTemporaryGuestPhase.Completed;
            record.LastStateChangeDay = CurrentDay();
        }

        private void CompleteUnavailable(ReignTemporaryPartyGuestRecord record)
        {
            record.Phase = ReignTemporaryGuestPhase.Completed;
            record.LastStateChangeDay = CurrentDay();
            ReleaseCamp(record, true);
            _reviewCloseResolutions.Remove(record.HeroStringId ?? string.Empty);
        }

        private void TryShowDueReview()
        {
            if (_reviewInquiryOpen || ReignPartyChatScreenManager.IsOpen || !CanPresentReview()) return;
            ReignTemporaryPartyGuestRecord record = Records
                .Where(x => x.Phase == ReignTemporaryGuestPhase.ReviewDue)
                .OrderBy(x => x.LastStateChangeDay)
                .ThenBy(x => x.HeroStringId)
                .FirstOrDefault();
            Hero hero = FindHero(record?.HeroStringId);
            if (record == null || hero == null) return;
            AcquireReviewTimeLock();
            _reviewInquiryOpen = true;
            _reviewHeroStringId = hero.StringId;
            string term = record.TermKind == ReignTemporaryGuestTermKind.Fixed
                ? "Fixed term: " + record.FixedTermDays.ToString("0.#") + " days"
                : "Open-ended; reviewed every five days";
            string body = hero.Name + " wants to review the journey.\n\nPurpose: " + record.Purpose
                + "\nElapsed: " + Math.Max(0f, CurrentDay() - record.StartedDay).ToString("0.0") + " days"
                + "\n" + term + "\n\n" + record.LastReviewReason;
            InformationManager.ShowInquiry(new InquiryData(
                "Temporary Party Guest Review", body, true, true,
                "Speak Now", "Let Them Depart",
                () => OpenReviewConversation(record, hero),
                () =>
                {
                    _reviewInquiryOpen = false;
                    _reviewHeroStringId = string.Empty;
                    BeginDeparture(record);
                }), true);
        }

        private void OpenReviewConversation(ReignTemporaryPartyGuestRecord record, Hero hero)
        {
            _reviewInquiryOpen = false;
            _reviewHeroStringId = string.Empty;
            string context = "This is a mandatory temporary-party review. " + hero.Name
                + " must discuss whether the journey is still needed and explicitly consent to any renewal. Purpose: "
                + record.Purpose + ". " + record.LastReviewReason;
            ReignPartyChatScreenManager.OpenTargetedReview(hero, context,
                providerFailed => OnReviewChatClosed(record.HeroStringId, providerFailed));
        }

        private void OnReviewChatClosed(string heroId, bool providerFailed)
        {
            _reviewCloseResolutions[heroId] = new ReviewCloseResolution
            {
                ResolveAfterUtc = DateTime.UtcNow.AddSeconds(3),
                ProviderFailed = providerFailed
            };
        }

        internal bool NotifyDisposableLiveTestReviewProviderFailure(string heroId)
        {
            ReignTemporaryPartyGuestRecord record = FindLiveRecord(heroId);
            if (record == null || record.Phase != ReignTemporaryGuestPhase.ReviewDue)
                return false;
            InformationManager.HideInquiry();
            _reviewInquiryOpen = false;
            _reviewHeroStringId = string.Empty;
            OnReviewChatClosed(heroId, true);
            return true;
        }

        internal bool LetDisposableLiveTestReviewGuestDepart(string heroId)
        {
            ReignTemporaryPartyGuestRecord record = FindLiveRecord(heroId);
            if (record == null || record.Phase != ReignTemporaryGuestPhase.ReviewDue)
                return false;
            InformationManager.HideInquiry();
            _reviewInquiryOpen = false;
            _reviewHeroStringId = string.Empty;
            _reviewCloseResolutions.Remove(heroId ?? string.Empty);
            BeginDeparture(record);
            return record.Phase == ReignTemporaryGuestPhase.Returning
                || record.Phase == ReignTemporaryGuestPhase.Completed;
        }

        internal MobileParty ReleaseDisposableLiveTestSourceParty(string heroId)
        {
            ReignTemporaryPartyGuestRecord record = FindLiveRecord(heroId);
            if (record == null) return null;
            MobileParty source = FindParty(record.SourcePartyStringId);
            if (source != null) ReleaseCamp(record, false);
            return source;
        }

        internal void RunDisposableLiveTestHourlyTick()
        {
            OnHourlyTick();
            ApplicationTick();
        }

        internal void RunDisposableLiveTestHourlyStateOnly()
        {
            OnHourlyTick();
        }

        private void ResolveClosedReviews()
        {
            foreach (KeyValuePair<string, ReviewCloseResolution> pending in _reviewCloseResolutions
                .Where(x => DateTime.UtcNow >= x.Value.ResolveAfterUtc).ToList())
            {
                _reviewCloseResolutions.Remove(pending.Key);
                ReignTemporaryPartyGuestRecord record = FindLiveRecord(pending.Key);
                if (record == null || record.Phase != ReignTemporaryGuestPhase.ReviewDue) continue;
                if (pending.Value.ProviderFailed)
                {
                    record.LastReviewReason = "The conversation provider did not complete the review. Retry or let the guest depart.";
                    continue;
                }
                BeginDeparture(record);
            }
        }

        private void AcquireReviewTimeLock()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            if (!TaleWorlds.CampaignSystem.Campaign.Current.TimeControlModeLock)
            {
                TaleWorlds.CampaignSystem.Campaign.Current.SetTimeControlModeLock(true);
                _ownsReviewTimeLock = true;
            }
        }

        private void RefreshReviewTimeLock()
        {
            bool hasDueReview = Records.Any(x => x.Phase == ReignTemporaryGuestPhase.ReviewDue);
            if (hasDueReview || _reviewInquiryOpen)
            {
                AcquireReviewTimeLock();
                return;
            }
            if (_ownsReviewTimeLock && TaleWorlds.CampaignSystem.Campaign.Current != null)
            {
                TaleWorlds.CampaignSystem.Campaign.Current.SetTimeControlModeLock(false);
                _ownsReviewTimeLock = false;
            }
        }

        private static bool CanPresentReview()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || MobileParty.MainParty == null
                || Mission.Current != null || CharacterObject.OneToOneConversationCharacter != null)
                return false;
            if (MobileParty.MainParty.MapEvent != null || MapEvent.PlayerMapEvent != null
                || MobileParty.MainParty.SiegeEvent != null || TaleWorlds.CampaignSystem.Campaign.Current.CurrentMenuContext != null)
                return false;
            MapState state = Game.Current?.GameStateManager?.ActiveState as MapState;
            return state != null && !state.AtMenu && !state.MapConversationActive && !state.IsSimulationActive;
        }

        private static bool IsInPerson(Hero hero)
        {
            if (hero == null) return false;
            if (CharacterObject.OneToOneConversationCharacter?.HeroObject == hero) return true;
            if (ReignLiveInteractionTestHost
                .IsDisposablePartyAgencyFixtureConversationActive(hero)) return true;
            Settlement playerSettlement = Settlement.CurrentSettlement
                ?? Hero.MainHero?.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement;
            if (playerSettlement != null && hero.CurrentSettlement == playerSettlement) return true;
            MobileParty party = hero.PartyBelongedTo;
            return party != null && PlayerEncounter.EncounteredParty == party.Party;
        }

        private static float CalculateReturnHours(CampaignVec2? from, CampaignVec2? to)
        {
            if (!from.HasValue || !to.HasValue) return MinimumReturnHours;
            float distance = from.Value.ToVec2().Distance(to.Value.ToVec2());
            return Math.Max(MinimumReturnHours, Math.Min(MaximumReturnHours, 1f + distance / 12f));
        }

        private static Settlement FindSafeReturnSettlement(Hero hero)
        {
            var resident = ReignEncounteredResidentsCampaignBehavior.Instance?.Find(hero);
            Settlement residentHome = resident == null ? null : FindSettlement(resident.HomeId);
            if (residentHome != null && !residentHome.IsUnderSiege) return residentHome;
            IEnumerable<Settlement> candidates = Settlement.All.Where(settlement => settlement != null
                && settlement.IsFortification && !settlement.IsUnderSiege
                && hero?.MapFaction != null && !hero.MapFaction.IsAtWarWith(settlement.MapFaction));
            Settlement home = hero?.HomeSettlement;
            if (home != null && candidates.Contains(home)) return home;
            Vec2 origin = MobileParty.MainParty?.Position.ToVec2() ?? Vec2.Zero;
            return candidates.OrderBy(x => x.GatePosition.ToVec2().Distance(origin)).FirstOrDefault();
        }

        private static bool OpponentContainsOriginalFaction(MapEvent mapEvent, BattleSideEnum side,
            ReignTemporaryPartyGuestRecord record)
        {
            return mapEvent.PartiesOnSide(side).Any(entry =>
            {
                PartyBase party = entry?.Party;
                if (party == null) return false;
                if (!string.IsNullOrWhiteSpace(record.OriginalKingdomStringId)
                    && string.Equals(party.MapFaction?.StringId, record.OriginalKingdomStringId,
                        StringComparison.OrdinalIgnoreCase)) return true;
                return string.IsNullOrWhiteSpace(record.OriginalKingdomStringId)
                    && !string.IsNullOrWhiteSpace(record.OriginalClanStringId)
                    && string.Equals(party.MobileParty?.ActualClan?.StringId, record.OriginalClanStringId,
                        StringComparison.OrdinalIgnoreCase);
            });
        }

        private static IFaction ResolveOriginalFaction(ReignTemporaryPartyGuestRecord record, Hero hero)
        {
            if (!string.IsNullOrWhiteSpace(record?.OriginalKingdomStringId))
                return Kingdom.All.FirstOrDefault(x => string.Equals(x.StringId,
                    record.OriginalKingdomStringId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(record?.OriginalClanStringId))
                return Clan.All.FirstOrDefault(x => string.Equals(x.StringId,
                    record.OriginalClanStringId, StringComparison.OrdinalIgnoreCase));
            return ReignEncounteredResidentsCampaignBehavior.Instance?.Find(hero) != null ? null : hero?.MapFaction;
        }

        private ReignTemporaryPartyGuestRecord FindLiveRecord(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId)) return null;
            return (_records ?? new List<ReignTemporaryPartyGuestRecord>()).FirstOrDefault(x =>
                x != null && x.IsLive && string.Equals(x.HeroStringId, heroId, StringComparison.OrdinalIgnoreCase));
        }

        internal void CompleteResidentRecruitment(Hero hero)
        {
            if (ReignEncounteredResidentsCampaignBehavior.Instance?.Find(hero) == null) return;
            var record = FindLiveRecord(hero.StringId);
            if (record == null) return;
            record.Phase = ReignTemporaryGuestPhase.Completed;
            record.LastStateChangeDay = CurrentDay();
            _reviewCloseResolutions.Remove(hero.StringId);
            RefreshReviewTimeLock();
        }

        private static bool IdentityMatches(ReignTemporaryPartyGuestRecord record, Hero hero)
        {
            if (record == null || hero == null) return false;
            return string.Equals(record.OriginalClanStringId, hero.Clan?.StringId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(record.OriginalKingdomStringId, hero.Clan?.Kingdom?.StringId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(record.OriginalCompanionClanStringId, hero.CompanionOf?.StringId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static Hero FindHero(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : Hero.FindFirst(x =>
                string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static MobileParty FindParty(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : MobileParty.All.FirstOrDefault(x => x != null
                && string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static Settlement FindSettlement(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : Settlement.All.FirstOrDefault(x => x != null
                && string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static float CurrentDay()
        {
            return (float)CampaignTime.Now.ToDays;
        }

        private static JObject ParseTerms(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JObject();
            try { return JObject.Parse(json); }
            catch { return new JObject(); }
        }

        private static ReignTemporaryGuestTermKind ParseTermKind(JObject terms)
        {
            string value = ReadString(terms, "termKind", "open_ended")
                .Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            if (value == "fixed" || value == "fixed_term")
                return ReignTemporaryGuestTermKind.Fixed;
            if (value == "open_ended" || value == "openended" || value == "open")
                return ReignTemporaryGuestTermKind.OpenEnded;
            float duration = ReadFloat(terms, "durationDays", 0f);
            return duration >= MinimumFixedTermDays && duration <= MaximumFixedTermDays
                ? ReignTemporaryGuestTermKind.Fixed
                : ReignTemporaryGuestTermKind.OpenEnded;
        }

        private static string CleanPurpose(string purpose)
        {
            string clean = (purpose ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length <= 240 ? clean : clean.Substring(0, 240);
        }

        private static string ReadString(JObject obj, string key, string fallback)
        {
            JToken token = obj?[key];
            return token == null ? fallback ?? string.Empty : token.ToString();
        }

        private static float ReadFloat(JObject obj, string key, float fallback)
        {
            return obj?[key]?.Value<float?>() ?? fallback;
        }

        private static bool ReadBool(JObject obj, string key, bool fallback)
        {
            return obj?[key]?.Value<bool?>() ?? fallback;
        }

        private sealed class ReviewCloseResolution
        {
            public DateTime ResolveAfterUtc;
            public bool ProviderFailed;
        }
    }
}
