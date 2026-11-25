using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Family;
using ReignBeta.World;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using ReignBeta.Characters.Family;
using ReignBeta.Platform.Integration;
using ReignBeta.Persistence.Integration;
using ReignBeta.Relationships.Integration;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Characters.Campaign
{
    public sealed partial class ReignFamilyCampaignBehavior : CampaignBehaviorBase
    {
        private List<ReignConceptionRecord> _conceptions = new List<ReignConceptionRecord>();
        private List<ReignParentageRecord> _parentage = new List<ReignParentageRecord>();
        private bool _reconcilingPregnancyRestrictions;
        private int _managedBirthReportOffset;
        private readonly ConcurrentDictionary<ReignConceptionRecord, byte> _managedBirthReportsInFlight =
            new ConcurrentDictionary<ReignConceptionRecord, byte>();

        public static ReignFamilyCampaignBehavior Instance => ReignServices.Family;

        public override void RegisterEvents()
        {
            ReignServices.Family = this;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnChildConceivedEvent.AddNonSerializedListener(this, OnChildConceived);
            CampaignEvents.OnGivenBirthEvent.AddNonSerializedListener(this, OnGivenBirth);
            CampaignEvents.OnHeroJoinedPartyEvent.AddNonSerializedListener(this, OnHeroJoinedParty);
            CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, OnMobilePartyCreated);
            CampaignEvents.OnPartyLeaderChangedEvent.AddNonSerializedListener(this, OnPartyLeaderChanged);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.CanHeroLeadPartyEvent.AddNonSerializedListener(this, OnCanHeroLeadParty);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving) PruneConceptionHistory();
            dataStore.SyncData("_reign_conceptions", ref _conceptions);
            dataStore.SyncData("_reign_parentage", ref _parentage);
            dataStore.SyncData("_reign_native_pregnancy_contexts", ref _nativePregnancyContexts);
            _nativePregnancyContexts = _nativePregnancyContexts ?? new List<ReignConceptionRecord>();
            _conceptions = _conceptions ?? new List<ReignConceptionRecord>();
            _parentage = _parentage ?? new List<ReignParentageRecord>();
            if (dataStore.IsLoading) PruneConceptionHistory();
        }

        private void PruneConceptionHistory()
        {
            _conceptions = (_conceptions ?? new List<ReignConceptionRecord>())
                .Where(x => x != null && (string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase) || HasPendingManagedBirth(x)))
                .Concat((_conceptions ?? new List<ReignConceptionRecord>())
                    .Where(x => x != null && !string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase) && !HasPendingManagedBirth(x))
                    .OrderByDescending(x => x.ConceptionDay)
                    .Take(128))
                .Distinct()
                .OrderBy(x => x.ConceptionDay)
                .ToList();
        }

        public bool IsManagedPregnancy(Hero mother)
        {
            return mother != null && _conceptions.Any(x => x != null && x.Status == "active" && x.MotherId == mother.StringId);
        }

        internal JObject ObserveManagedBirth(string motherId, string conceptionId)
        {
            var matches = _conceptions.Where(candidate => candidate != null && candidate.MotherId == motherId
                && (conceptionId == null ? candidate.Status == "active" : candidate.ConceptionId == conceptionId))
                .Take(2).ToArray();
            if (matches.Length != 1 || string.IsNullOrEmpty(matches[0].ConceptionId)
                || (matches[0].Status != "active" && matches[0].Status != "born"))
                throw new InvalidOperationException("Select one existing active or born managed conception for the exact mother.");
            var record = matches[0];
            if (_conceptions.Any(candidate => candidate != null && !ReferenceEquals(candidate, record)
                && candidate.ConceptionId == record.ConceptionId))
                throw new InvalidOperationException("The managed conception identity is ambiguous.");
            Hero mother = ManagedBirthObservationHero(motherId);
            Hero biological = ManagedBirthObservationHero(record.BiologicalFatherId);
            Hero legal = ManagedBirthObservationHero(record.LegalFatherId);
            string intendedLegalId = string.IsNullOrEmpty(record.IntendedLegalFatherId)
                ? record.LegalFatherId : record.IntendedLegalFatherId;
            Hero intendedLegal = ManagedBirthObservationHero(intendedLegalId);
            Hero child = string.IsNullOrEmpty(record.ChildId) ? null : ManagedBirthObservationHero(record.ChildId);
            var forConception = _parentage.Where(row => row != null && row.ConceptionId == record.ConceptionId).ToArray();
            string pendingHash = string.Empty;
            if (!string.IsNullOrWhiteSpace(record.PendingManagedBirthReportJson))
            {
                using (var sha = SHA256.Create())
                    pendingHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(record.PendingManagedBirthReportJson)))
                        .Replace("-", "").ToLowerInvariant();
            }
            return new JObject
            {
                ["schema"] = "reign-managed-birth-native-observation-v1",
                ["conceptionId"] = record.ConceptionId, ["status"] = record.Status, ["dueDay"] = record.DueDay,
                ["motherId"] = motherId, ["motherPregnant"] = mother.IsPregnant,
                ["biologicalFatherId"] = biological.StringId, ["legalFatherId"] = legal.StringId,
                ["intendedLegalFatherId"] = intendedLegalId,
                ["childId"] = record.ChildId ?? "", ["nativeChildMotherId"] = child?.Mother?.StringId ?? "",
                ["nativeChildFatherId"] = child?.Father?.StringId ?? "",
                ["motherChildIds"] = ManagedBirthObservationChildren(mother),
                ["biologicalFatherChildIds"] = ManagedBirthObservationChildren(biological),
                ["legalFatherChildIds"] = ManagedBirthObservationChildren(legal),
                ["intendedLegalFatherChildIds"] = ManagedBirthObservationChildren(intendedLegal),
                ["parentageForConceptionCount"] = forConception.Length,
                ["parentageForChildCount"] = child == null ? 0
                    : _parentage.Count(row => row != null && row.ChildId == child.StringId),
                ["matchingParentageCount"] = child == null ? 0 : forConception.Count(row => row.ChildId == child.StringId
                    && row.MotherId == motherId && row.BiologicalFatherId == biological.StringId
                    && row.LegalFatherId == legal.StringId),
                ["pendingReportSha256"] = pendingHash,
                ["reportInFlight"] = _managedBirthReportsInFlight.ContainsKey(record)
            };
        }

        private static Hero ManagedBirthObservationHero(string id)
        {
            var hero = string.IsNullOrEmpty(id) ? null : ReignObjectResolver.FindHero(id);
            if (hero == null || hero.StringId != id)
                throw new InvalidOperationException("The managed conception lacks an exact native hero identity.");
            return hero;
        }

        private static JArray ManagedBirthObservationChildren(Hero parent)
        {
            var children = (parent.Children ?? new List<Hero>()).Where(child => child != null).Take(129).ToArray();
            if (children.Length > 128 || children.Any(child => string.IsNullOrEmpty(child.StringId)))
                throw new InvalidOperationException("The native child list cannot be measured within the observation bound.");
            return new JArray(children.Select(child => child.StringId).OrderBy(id => id, StringComparer.Ordinal));
        }

        public IReadOnlyList<Hero> LivingPubliclyAcknowledgedChildren(Hero parent)
        {
            if (parent == null) return new List<Hero>();
            return (parent.Children ?? new List<Hero>())
                .Where(child => child != null && child.IsAlive
                    && (child.Mother == parent || child.Father == parent))
                .GroupBy(child => child.StringId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        public IReadOnlyList<Hero> LivingPubliclyKnownBastardChildren(Hero parent)
        {
            if (parent == null) return new List<Hero>();
            return (_parentage ?? new List<ReignParentageRecord>())
                .Where(record => record != null && record.IsIllegitimate
                    && (string.Equals(record.MotherId, parent.StringId, StringComparison.OrdinalIgnoreCase) && (record.Revealed || record.MotherExposed)
                        || string.Equals(record.BiologicalFatherId, parent.StringId, StringComparison.OrdinalIgnoreCase) && (record.Revealed || record.FatherExposed)))
                .Select(record => ReignObjectResolver.FindHero(record.ChildId))
                .Where(child => child != null && child.IsAlive)
                .GroupBy(child => child.StringId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        public bool TryCreateSocialBalanceChild(
            Hero mother,
            Hero biologicalFather,
            Hero legalFather,
            bool publiclyKnown,
            bool female,
            out Hero child,
            out string error)
        {
            child = null;
            error = string.Empty;
            legalFather = legalFather ?? biologicalFather;
            if (mother == null || biologicalFather == null || legalFather == null
                || !mother.IsAlive || !biologicalFather.IsAlive || !legalFather.IsAlive
                || !mother.IsFemale || biologicalFather.IsFemale || legalFather.IsFemale)
            {
                error = "A living female mother and living male biological/legal fathers are required.";
                return false;
            }
            try
            {
                child = HeroCreator.DeliverOffSpring(mother, legalFather, female);
                if (child == null)
                {
                    error = "Bannerlord did not create the requested child.";
                    return false;
                }
                mother.IsPregnant = false;
                bool illegitimate = mother.Spouse == null || legalFather != biologicalFather;
                ReignParentageRecord parentage = new ReignParentageRecord
                {
                    ChildId = child.StringId,
                    MotherId = mother.StringId,
                    BiologicalFatherId = biologicalFather.StringId,
                    LegalFatherId = legalFather.StringId,
                    IsIllegitimate = illegitimate,
                    Revealed = publiclyKnown && illegitimate,
                    BastardSurname = illegitimate ? "Baseborn" : string.Empty,
                    ConceptionId = "social_balance_" + Guid.NewGuid().ToString("N")
                };
                _parentage.Add(parentage);
                if (parentage.Revealed && child.Father != biologicalFather)
                {
                    RemoveChildReference(child.Father, child);
                    child.Father = biologicalFather;
                }
                if (parentage.Revealed) ApplyBastardName(child, parentage.BastardSurname);
                CampaignEventDispatcher.Instance.OnGivenBirth(mother, new List<Hero> { child }, 0);
                return true;
            }
            catch (Exception ex)
            {
                error = "Social-balance child creation failed: " + ex.Message;
                return false;
            }
        }

        public bool TryStartManagedConception(string conceptionId, Hero mother, Hero biologicalFather, Hero legalFather,
            float conceptionDay, float dueDay, float secrecy, bool playerInvolved, out string error)
            => TryStartManagedConception(conceptionId, mother, biologicalFather, legalFather,
                conceptionDay, dueDay, secrecy, playerInvolved, out error, null);

        internal bool TryStartManagedConception(string conceptionId, Hero mother, Hero biologicalFather, Hero legalFather,
            float conceptionDay, float dueDay, float secrecy, bool playerInvolved, out string error, string intendedLegalFatherId)
        {
            error = string.Empty;
            if (mother == null || biologicalFather == null || !mother.IsFemale || biologicalFather.IsFemale || mother.Age < 18f || mother.Age > 45f || !mother.IsAlive || !biologicalFather.IsAlive)
            {
                error = "Invalid mother, father, sex, age, or life state.";
                return false;
            }
            if (_conceptions.Any(x => x != null && x.ConceptionId == conceptionId)) return true;
            if (mother.IsPregnant || IsManagedPregnancy(mother))
            {
                error = "Mother is already pregnant.";
                return false;
            }
            ReignConceptionRecord record = new ReignConceptionRecord
            {
                ConceptionId = string.IsNullOrWhiteSpace(conceptionId) ? "conception_" + Guid.NewGuid().ToString("N") : conceptionId,
                MotherId = mother.StringId,
                BiologicalFatherId = biologicalFather.StringId,
                LegalFatherId = (legalFather ?? biologicalFather).StringId,
                IntendedLegalFatherId = string.IsNullOrWhiteSpace(intendedLegalFatherId)
                    ? (legalFather ?? biologicalFather).StringId : intendedLegalFatherId,
                ConceptionDay = conceptionDay,
                DueDay = dueDay > conceptionDay ? dueDay : conceptionDay + 36f,
                Secrecy = secrecy,
                PlayerInvolved = playerInvolved,
                IsIllegitimate = mother.Spouse == null || (legalFather ?? biologicalFather) != biologicalFather,
                SpouseAtConceptionId = mother.Spouse?.StringId ?? string.Empty,
                MarriageAtConceptionKnown = true,
                ContextSource = "reign_managed",
                Status = "active"
            };
            _conceptions.Add(record);
            mother.IsPregnant = true;
            NotifyPregnancyStateChanged(mother);
            _ = ReignRelationshipDirectorClient.ReportConceptionAsync(record, "active", string.Empty);
            ReignLog.Info("Managed conception started id=" + record.ConceptionId + " mother=" + mother.StringId + " bioFather=" + biologicalFather.StringId + " legalFather=" + record.LegalFatherId + ".");
            return true;
        }

        public void NotifyPregnancyStateChanged(Hero hero)
        {
            if (ReignPregnancyRestrictionPolicy.IsRestrictedNpc(hero))
            {
                TryWithdrawPregnantNpc(hero, "pregnancy_state_changed");
            }
        }

        public void OnPregnancyRestrictionSettingsChanged()
        {
            if (ReignPregnancyRestrictionPolicy.IsEnabled)
            {
                ReconcilePregnancyRestrictions("settings_enabled");
            }
        }

        public void ReconcilePregnancyRestrictions(string source = "reconcile")
        {
            if (!ReignPregnancyRestrictionPolicy.IsEnabled
                || TaleWorlds.CampaignSystem.Campaign.Current == null
                || _reconcilingPregnancyRestrictions)
            {
                return;
            }

            _reconcilingPregnancyRestrictions = true;
            try
            {
                foreach (Hero hero in Hero.AllAliveHeroes.ToList())
                {
                    if (ReignPregnancyRestrictionPolicy.IsRestrictedNpc(hero))
                    {
                        TryWithdrawPregnantNpc(hero, source);
                    }
                }
            }
            finally
            {
                _reconcilingPregnancyRestrictions = false;
            }
        }

        public bool RevealBiologicalFather(string childId, out string error)
        {
            error = string.Empty;
            ReignParentageRecord record = _parentage.FirstOrDefault(x => x != null && x.ChildId == childId);
            Hero child = ReignObjectResolver.FindHero(childId);
            Hero biologicalFather = ReignObjectResolver.FindHero(record?.BiologicalFatherId);
            if (record == null || child == null || biologicalFather == null)
            {
                error = "Parentage record, child, or biological father was not found.";
                return false;
            }
            if (record.Revealed) return true;

            Hero oldFather = child.Father;
            RemoveChildReference(oldFather, child);
            child.Father = biologicalFather;
            record.Revealed = true;
            record.IsIllegitimate = true;
            record.BastardSurname = string.IsNullOrWhiteSpace(record.BastardSurname) ? "Baseborn" : record.BastardSurname;
            ApplyBastardName(child, record.BastardSurname);
            ReignLog.Info("Biological parentage revealed child=" + childId + " father=" + biologicalFather.StringId + ".");
            return true;
        }

        private void OnDailyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            if (TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            float day = (float)CampaignTime.Now.ToDays;
            foreach (ReignConceptionRecord record in _conceptions.Where(x => x != null && x.Status == "active" && x.DueDay <= day).ToList())
            {
                Deliver(record);
            }
        }

        private void OnHourlyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            ReconcilePregnancyRestrictions("hourly");
            var pending = _conceptions.Where(HasPendingManagedBirth).ToList();
            if (pending.Count == 0) return;
            int start = _managedBirthReportOffset % pending.Count;
            int batch = Math.Min(3, pending.Count);
            _managedBirthReportOffset = (start + batch) % pending.Count;
            for (int index = 0; index < batch; index++)
                _ = SendManagedBirthReportAsync(pending[(start + index) % pending.Count]);
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            ReconcileNativePregnancyContexts();
            // A sealed campaign skips PrepareInitialState on subsequent loads.
            // Repair legacy placement before native daily settlement events can run.
            RestoreDisplacedPregnantRuralNotables();
        }

        internal void PrepareInitialState()
        {
            ReconcileNativePregnancyContexts();
            RestoreDisplacedPregnantRuralNotables();
            ReconcilePregnancyRestrictions("campaign_preparation");
        }

        private void RestoreDisplacedPregnantRuralNotables()
        {
            // Older pregnancy withdrawal moved village residents into fortifications.
            // Native recruitment dereferences CurrentSettlement.Village for rural notables.
            foreach (string motherId in _conceptions.Where(record => record != null && record.Status == "active")
                .Select(record => record.MotherId).Distinct().ToList())
            {
                Hero mother = ReignObjectResolver.FindHero(motherId);
                if (mother == null || mother == Hero.MainHero || !mother.IsAlive || !mother.IsActive
                    || !mother.IsPregnant || !mother.IsRuralNotable || mother.IsPrisoner
                    || mother.PartyBelongedTo != null || mother.PartyBelongedToAsPrisoner != null
                    || mother.CurrentSettlement?.IsFortification != true
                    || mother.BornSettlement?.IsVillage != true) continue;

                string previousSettlementId = mother.CurrentSettlement.StringId;
                TeleportHeroAction.ApplyImmediateTeleportToSettlement(mother, mother.BornSettlement);
                mother.UpdateHomeSettlement();
                ReignLog.Info("Restored pregnant rural notable " + mother.StringId + " from "
                    + previousSettlementId + " to native village " + mother.BornSettlement.StringId + ".");
            }
        }

        private void OnChildConceived(Hero mother)
        {
            ReconcileNativePregnancyContexts(mother);
            NotifyPregnancyStateChanged(mother);
        }

        private void OnGivenBirth(Hero mother, List<Hero> children, int stillborn)
        {
            var native = _nativePregnancyContexts.FirstOrDefault(r => r.MotherId == mother?.StringId && r.Status == "active");
            if (native != null)
            {
                native.Status = "born";
                native.ChildId = children?.FirstOrDefault()?.StringId ?? "";
                foreach (Hero child in children ?? new List<Hero>())
                    _parentage.Add(new ReignParentageRecord { ChildId = child.StringId, MotherId = native.MotherId,
                        BiologicalFatherId = native.BiologicalFatherId, LegalFatherId = native.LegalFatherId,
                        IsIllegitimate = native.IsIllegitimate, Revealed = native.FatherExposed,
                        MotherExposed = native.MotherExposed, FatherExposed = native.FatherExposed, ConceptionId = native.ConceptionId });
                _ = ReignRelationshipDirectorClient.ReportConceptionAsync(native, "born", native.ChildId);
            }
            // Birth clears Hero.IsPregnant in both native and Reign-managed flows.
            // Eligibility is intentionally returned to Bannerlord without creating a new party.
        }

        private void OnHeroJoinedParty(Hero hero, MobileParty party)
        {
            if (ReignPregnancyRestrictionPolicy.IsRestrictedNpc(hero))
            {
                TryWithdrawPregnantNpc(hero, "hero_joined_party");
            }
        }

        private void OnMobilePartyCreated(MobileParty party)
        {
            if (ReignPregnancyRestrictionPolicy.IsRestrictedNpc(party?.LeaderHero))
            {
                TryWithdrawPregnantNpc(party.LeaderHero, "mobile_party_created");
            }
        }

        private void OnPartyLeaderChanged(MobileParty party, Hero oldLeader)
        {
            if (ReignPregnancyRestrictionPolicy.IsRestrictedNpc(party?.LeaderHero))
            {
                TryWithdrawPregnantNpc(party.LeaderHero, "party_leader_changed");
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            ReconcilePregnancyRestrictions("map_event_ended");
        }

        private void OnCanHeroLeadParty(Hero hero, ref bool result)
        {
            if (ReignPregnancyRestrictionPolicy.IsRestrictedNpc(hero))
            {
                result = false;
            }
        }

        private void TryWithdrawPregnantNpc(Hero hero, string source)
        {
            if (!ReignPregnancyRestrictionPolicy.IsRestrictedNpc(hero)
                || hero.IsPrisoner
                || hero.PartyBelongedToAsPrisoner != null)
            {
                return;
            }

            MobileParty party = hero.PartyBelongedTo;
            // Settlement notables are already outside party/combat duty. Their native
            // occupation and recruitment depend on remaining in their own settlement.
            if (party == null && hero.IsNotable) return;
            if (IsInsideBesiegedTown(hero, party))
            {
                return;
            }
            if (party == null && IsSafeFortification(hero, hero.CurrentSettlement))
            {
                return;
            }

            if (party != null
                && (!party.IsActive
                    || party.MapEvent != null
                    || party.SiegeEvent != null
                    || party.BesiegedSettlement != null))
            {
                ReignLog.Info("Pregnancy withdrawal deferred for " + hero.StringId + " until active combat or siege ends.");
                return;
            }

            Settlement safeSettlement = FindSafeFortification(hero, party);
            if (safeSettlement == null)
            {
                ReignLog.Warn("Pregnancy withdrawal could not find a safe fortification for " + hero.StringId + ".");
                return;
            }

            try
            {
                if (party != null && party.LeaderHero == hero && party != MobileParty.MainParty)
                {
                    RelinquishPregnantLeader(hero, party);
                }

                if (hero.GovernorOf != null && hero.GovernorOf.Settlement != safeSettlement)
                {
                    ChangeGovernorAction.RemoveGovernorOf(hero);
                }
                TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, safeSettlement);
                if (hero.PartyBelongedTo == null && hero.CurrentSettlement == safeSettlement)
                {
                    ReignLog.Info("Pregnancy restriction withdrew " + hero.StringId + " to " + safeSettlement.StringId + " from " + source + ".");
                }
                else
                {
                    ReignLog.Warn("Pregnancy withdrawal remains pending for " + hero.StringId + " after " + source + ".");
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Pregnancy withdrawal failed for " + hero.StringId + ": " + ex.Message);
            }
        }

        private static void RelinquishPregnantLeader(Hero hero, MobileParty party)
        {
            if (party.Army != null)
            {
                if (party.Army.LeaderParty == party)
                {
                    DisbandArmyAction.ApplyByUnknownReason(party.Army);
                }
                else
                {
                    party.Army = null;
                }
            }

            Hero replacement = FindReplacementLeader(hero);
            if (replacement != null)
            {
                if (replacement.GovernorOf != null)
                {
                    ChangeGovernorAction.RemoveGovernorOf(replacement);
                }
                TeleportHeroAction.ApplyImmediateTeleportToPartyAsPartyLeader(replacement, party);
                ReignLog.Info("Pregnancy restriction transferred party " + party.StringId + " from " + hero.StringId + " to " + replacement.StringId + ".");
                return;
            }

            party.RemovePartyLeader();
            party.Ai.SetDoNotMakeNewDecisions(true);
            party.SetMoveModeHold();
            party.IsDisbanding = true;
            ReignLog.Info("Pregnancy restriction started disbanding " + party.StringId + " because " + hero.StringId + " had no eligible replacement.");
        }

        private static Hero FindReplacementLeader(Hero pregnantLeader)
        {
            if (pregnantLeader?.Clan == null || TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                return null;
            }

            float adulthood = TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge;
            return pregnantLeader.Clan.Heroes
                .Where(candidate => candidate != null
                    && candidate != pregnantLeader
                    && candidate != Hero.MainHero
                    && candidate.IsAlive
                    && candidate.IsActive
                    && !candidate.IsPregnant
                    && !candidate.IsPrisoner
                    && candidate.PartyBelongedTo == null
                    && candidate.PartyBelongedToAsPrisoner == null
                    && candidate.Age > adulthood
                    && candidate.CanLeadParty())
                .OrderByDescending(candidate => candidate.GovernorOf == null)
                .ThenByDescending(candidate => candidate == pregnantLeader.Clan.Leader)
                .ThenByDescending(candidate => candidate.Age)
                .ThenBy(candidate => candidate.StringId)
                .FirstOrDefault();
        }

        private static Settlement FindSafeFortification(Hero hero, MobileParty party)
        {
            Func<Settlement, bool> sameFaction = settlement => IsSafeFortification(hero, settlement)
                && settlement.MapFaction == hero.MapFaction;
            Func<Settlement, bool> nonHostile = settlement => IsSafeFortification(hero, settlement);

            if (party != null)
            {
                if (sameFaction(party.CurrentSettlement))
                {
                    return party.CurrentSettlement;
                }

                Settlement nearest = SettlementHelper.FindNearestFortificationToMobileParty(party, party.NavigationCapability, sameFaction)
                    ?? SettlementHelper.FindNearestFortificationToMobileParty(party, party.NavigationCapability, nonHostile);
                if (nearest != null)
                {
                    return nearest;
                }
            }

            if (sameFaction(hero.HomeSettlement))
            {
                return hero.HomeSettlement;
            }
            if (sameFaction(hero.Clan?.InitialHomeSettlement))
            {
                return hero.Clan.InitialHomeSettlement;
            }
            if (hero.LastKnownClosestSettlement != null)
            {
                Settlement nearest = SettlementHelper.FindNearestFortificationToSettlement(hero.LastKnownClosestSettlement, MobileParty.NavigationType.All, sameFaction)
                    ?? SettlementHelper.FindNearestFortificationToSettlement(hero.LastKnownClosestSettlement, MobileParty.NavigationType.All, nonHostile);
                if (nearest != null)
                {
                    return nearest;
                }
            }

            return Settlement.All.FirstOrDefault(sameFaction) ?? Settlement.All.FirstOrDefault(nonHostile);
        }

        private static bool IsInsideBesiegedTown(Hero hero, MobileParty party)
        {
            Settlement settlement = hero?.CurrentSettlement ?? party?.CurrentSettlement;
            return settlement != null
                && settlement.IsTown
                && settlement.IsUnderSiege
                && (hero.CurrentSettlement == settlement || party?.CurrentSettlement == settlement);
        }

        private static bool IsSafeFortification(Hero hero, Settlement settlement)
        {
            if (hero == null
                || settlement == null
                || !settlement.IsFortification
                || settlement.MapFaction == null
                || settlement.IsUnderSiege
                || settlement.IsUnderRaid
                || settlement.Party?.MapEvent != null)
            {
                return false;
            }

            return hero.MapFaction == null
                || !FactionManager.IsAtWarAgainstFaction(settlement.MapFaction, hero.MapFaction);
        }

        private void Deliver(ReignConceptionRecord record)
        {
            if (record.Status != "active") return;
            Hero mother = ReignObjectResolver.FindHero(record.MotherId);
            Hero bioFather = ReignObjectResolver.FindHero(record.BiologicalFatherId);
            Hero legalFather = ReignObjectResolver.FindHero(record.LegalFatherId) ?? bioFather;
            if (mother == null || bioFather == null || legalFather == null || !mother.IsAlive)
            {
                FailManagedDelivery(record, mother);
                return;
            }

            Hero child;
            try { child = HeroCreator.DeliverOffSpring(mother, legalFather, MBRandom.RandomFloat <= 0.51f); }
            catch (Exception ex)
            {
                ReignLog.Warn("Managed delivery returned no observed child: " + ex.Message);
                FailManagedDelivery(record, mother);
                return;
            }
            if (child == null)
            {
                ReignLog.Warn("Managed delivery returned no child for " + record.ConceptionId + ".");
                FailManagedDelivery(record, mother);
                return;
            }

            // A returned child is an irreversible native outcome. Record it before callbacks.
            record.Status = "born";
            record.ChildId = child.StringId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(record.ChildId))
            {
                RunManagedBirthFollowUp(record, "clear pregnancy", () => mother.IsPregnant = false);
                ReignLog.Warn("Managed delivery returned a child without an ID for " + record.ConceptionId + ".");
                return;
            }

            ReignParentageRecord parentage = new ReignParentageRecord
            {
                ChildId = record.ChildId,
                MotherId = mother.StringId,
                BiologicalFatherId = bioFather.StringId,
                LegalFatherId = legalFather.StringId,
                IsIllegitimate = record.IsIllegitimate,
                Revealed = record.FatherExposed || !record.MotherExposed && legalFather == bioFather,
                MotherExposed = record.MotherExposed,
                FatherExposed = record.FatherExposed,
                BastardSurname = record.IsIllegitimate && mother.Spouse == null ? "Baseborn" : string.Empty,
                ConceptionId = record.ConceptionId
            };
            _parentage.Add(parentage);
            RunManagedBirthFollowUp(record, "capture birth report", () =>
            {
                JObject observed = ReignRelationshipDirectorClient.CaptureConceptionReport(record, "born", record.ChildId);
                if (!string.IsNullOrWhiteSpace(record.IntendedLegalFatherId))
                    observed["intendedLegalFatherId"] = record.IntendedLegalFatherId;
                observed["legalFatherId"] = parentage.LegalFatherId;
                observed["reportOrigin"] = ManagedBirthReportOrigin((string)observed["campaignId"]);
                record.PendingManagedBirthReportJson = observed.ToString(Formatting.None);
            });

            RunManagedBirthFollowUp(record, "clear pregnancy", () => mother.IsPregnant = false);
            if (parentage.BastardSurname.Length > 0)
                RunManagedBirthFollowUp(record, "name child", () => ApplyBastardName(child, parentage.BastardSurname));
            RunManagedBirthFollowUp(record, "dispatch birth event",
                () => CampaignEventDispatcher.Instance.OnGivenBirth(mother, new List<Hero> { child }, 0));
            if (HasPendingManagedBirth(record)) _ = SendManagedBirthReportAsync(record);
            if (record.PlayerInvolved)
                RunManagedBirthFollowUp(record, "display birth notice",
                    () => InformationManager.DisplayMessage(new InformationMessage(mother.Name + " has given birth.")));
        }

        private static bool HasPendingManagedBirth(ReignConceptionRecord record)
            => record?.Status == "born" && !string.IsNullOrWhiteSpace(record.PendingManagedBirthReportJson);

        private static string ManagedBirthReportOrigin(string campaign)
        {
            var aligned = ReignSaveSyncCoordinator.CurrentVerifiedLoad;
            return aligned != null && aligned.CampaignId == campaign
                ? aligned.Origin
                : ReignHttpTransport.PinnableOrigin(ReignServerEndpoint.ConfiguredBaseUrl());
        }

        private async System.Threading.Tasks.Task SendManagedBirthReportAsync(ReignConceptionRecord record)
        {
            if (!HasPendingManagedBirth(record) || !_managedBirthReportsInFlight.TryAdd(record, 0)) return;
            try
            {
                string retainedPayload = record.PendingManagedBirthReportJson;
                JObject payload = JObject.Parse(retainedPayload);
                string campaign = (string)payload["campaignId"];
                string timeline = (string)payload["timelineId"];
                string conception = (string)payload["conceptionId"];
                string child = (string)payload["childId"];
                string origin = (string)payload["reportOrigin"];
                payload.Remove("reportOrigin");
                string configuredServer = ReignServerEndpoint.ConfiguredBaseUrl();
                var fence = ReignServerClient.RequestFence.Capture();
                bool OwnsReport() => ReferenceEquals(Instance, this) && fence.IsCurrentActionPoll()
                    && configuredServer == ReignServerEndpoint.ConfiguredBaseUrl()
                    && origin == ManagedBirthReportOrigin(campaign) && ReignHttpTransport.IsAllowedPinnedOrigin(origin)
                    && HasPendingManagedBirth(record) && _conceptions.Contains(record)
                    && record.PendingManagedBirthReportJson == retainedPayload
                    && record.ConceptionId == conception && record.ChildId == child
                    && campaign == ReignServerClient.GetCampaignId()
                    && timeline == (ReignServices.WorldHistory?.TimelineId ?? "main");
                if (string.IsNullOrWhiteSpace(conception) || string.IsNullOrWhiteSpace(child)
                    || (string)payload["status"] != "born") return;
                if (campaign != ReignServerClient.GetCampaignId() || timeline != (ReignServices.WorldHistory?.TimelineId ?? "main")
                    || origin != ManagedBirthReportOrigin(campaign))
                {
                    ReignLog.Warn("Managed birth report is retained for its original campaign, timeline, and server.");
                    return;
                }
                if (!OwnsReport()) return;
                payload["sharedActivityGeneration"] = fence.SharedActivityGeneration;
                if (await ReignRelationshipDirectorClient.AcknowledgeManagedBirthAsync(origin, payload, OwnsReport).ConfigureAwait(false))
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (OwnsReport()) record.PendingManagedBirthReportJson = string.Empty;
                    }).ConfigureAwait(false);
            }
            catch (ReignRequestOwnerAbandonedException) { }
            catch (Exception ex) { ReignLog.Warn("Managed birth report remains pending: " + ex.Message); }
            finally { _managedBirthReportsInFlight.TryRemove(record, out _); }
        }

        private static void FailManagedDelivery(ReignConceptionRecord record, Hero mother)
        {
            record.Status = "failed";
            if (mother != null)
                RunManagedBirthFollowUp(record, "clear pregnancy", () => mother.IsPregnant = false);
            _ = ReignRelationshipDirectorClient.ReportConceptionAsync(record, "failed", string.Empty);
        }

        private static void RunManagedBirthFollowUp(ReignConceptionRecord record, string step, Action followUp)
        {
            try { followUp(); }
            catch (Exception ex) { ReignLog.Warn("Managed delivery " + record.ConceptionId + " could not " + step + ": " + ex.Message); }
        }

        private static void ApplyBastardName(Hero child, string surname)
        {
            if (child == null || string.IsNullOrWhiteSpace(surname)) return;
            string first = child.FirstName?.ToString() ?? child.Name?.ToString() ?? "Child";
            child.SetName(new TextObject(first + " " + surname), new TextObject(first));
        }

        private static void RemoveChildReference(Hero father, Hero child)
        {
            if (father == null || child == null) return;
            try
            {
                FieldInfo field = typeof(Hero).GetField("_children", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(father) is IList<Hero> children) children.Remove(child);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Could not repair presumed father's child list: " + ex.Message);
            }
        }
    }
}
