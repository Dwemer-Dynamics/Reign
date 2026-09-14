using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Shared;
using ReignBeta.Events;
using ReignBeta.Integration;
using ReignBeta.Settings;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    internal sealed class ReignKingdomEventDefinition
    {
        internal readonly string Id;
        internal readonly string Title;
        internal readonly bool Beneficial;
        internal readonly bool Timed;
        internal readonly float Magnitude;
        internal readonly string Description;

        internal ReignKingdomEventDefinition(
            string id,
            string title,
            bool beneficial,
            bool timed,
            float magnitude,
            string description)
        {
            Id = id;
            Title = title;
            Beneficial = beneficial;
            Timed = timed;
            Magnitude = magnitude;
            Description = description;
        }
    }

    public sealed partial class ReignKingdomEventsCampaignBehavior : CampaignBehaviorBase
    {
        internal const float TimedEventDurationDays = 30f;
        internal const float FoodProductionMagnitude = 0.35f;
        internal const float HearthMagnitude = 1f;
        internal const float SecurityMagnitude = 0.5f;
        internal const float ProsperityMagnitude = 1f;
        internal const float ConstructionMagnitude = 0.25f;
        internal const float LoyaltyMagnitude = 0.5f;
        private const int RetainedCompletedEvents = 128;

        private static readonly IReadOnlyList<ReignKingdomEventDefinition> Definitions =
            new List<ReignKingdomEventDefinition>
            {
                new ReignKingdomEventDefinition("famine", "Famine", false, true, -FoodProductionMagnitude,
                    "Food production from the kingdom's villages is reduced by 35%."),
                new ReignKingdomEventDefinition("pestilence", "Pestilence", false, true, -HearthMagnitude,
                    "Every village loses at least 1 hearth per day, down to the protected hearth floor."),
                new ReignKingdomEventDefinition("crime_wave", "Crime Wave", false, true, -SecurityMagnitude,
                    "Every town and castle suffers -0.5 security per day."),
                new ReignKingdomEventDefinition("trade_collapse", "Trade Collapse", false, true, -ProsperityMagnitude,
                    "Every town and castle suffers -1 prosperity per day."),
                new ReignKingdomEventDefinition("construction_stagnation", "Construction Stagnation", false, true, -ConstructionMagnitude,
                    "Building construction is 25% slower in every town and castle."),
                new ReignKingdomEventDefinition("political_unrest", "Political Unrest", false, true, -LoyaltyMagnitude,
                    "Every town and castle suffers -0.5 loyalty per day."),
                new ReignKingdomEventDefinition("border_crisis", "Border Crisis", false, false, 0f,
                    "The kingdom immediately enters one new war."),

                new ReignKingdomEventDefinition("bountiful_harvest", "Bountiful Harvest", true, true, FoodProductionMagnitude,
                    "Food production from the kingdom's villages is increased by 35%."),
                new ReignKingdomEventDefinition("population_boom", "Population Boom", true, true, HearthMagnitude,
                    "Every village receives +1 daily hearth growth."),
                new ReignKingdomEventDefinition("law_and_order", "Law and Order", true, true, SecurityMagnitude,
                    "Every town and castle receives +0.5 security per day."),
                new ReignKingdomEventDefinition("trade_boom", "Trade Boom", true, true, ProsperityMagnitude,
                    "Every town and castle receives +1 prosperity per day."),
                new ReignKingdomEventDefinition("golden_age", "Golden Age", true, true, ConstructionMagnitude,
                    "Building construction is 25% faster in every town and castle."),
                new ReignKingdomEventDefinition("national_unity", "National Unity", true, true, LoyaltyMagnitude,
                    "Every town and castle receives +0.5 loyalty per day."),
                new ReignKingdomEventDefinition("grand_reconciliation", "Grand Reconciliation", true, false, 0f,
                    "The kingdom immediately ends one ordinary war."),
                new ReignKingdomEventDefinition("orderly_succession", "Orderly Succession", true, false, 0f,
                    "An eligible NPC ruler voluntarily abdicates in favor of the ruling clan's heir.")
            };

        private List<ReignKingdomEventRecord> _events = new List<ReignKingdomEventRecord>();
        private List<ReignKingdomEventTestLedger> _testLedgers = new List<ReignKingdomEventTestLedger>();
        private int _lastRolledDay = -1;
        // Non-serialized, disposable-save-only acceptance seam. It is armed by an exact
        // kingdom_event_test command and consumed before the selected daily tick executes.
        private int _testOrganicTriggerDay = -1;
        private string _testOrganicTriggerRunId = string.Empty;
        private static readonly Queue<string> PendingAnnouncementEventIds =
            new Queue<string>();

        public static ReignKingdomEventsCampaignBehavior Instance { get; private set; }
        public IReadOnlyList<ReignKingdomEventRecord> Events => _events;
        internal static IReadOnlyList<ReignKingdomEventDefinition> Catalog => Definitions;

        public ReignKingdomEventsCampaignBehavior()
        {
            Instance = this;
            PendingAnnouncementEventIds.Clear();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                PruneHistory();
            }
            dataStore.SyncData("_reignKingdomEvents_records", ref _events);
            dataStore.SyncData("_reignKingdomEvents_lastRolledDay", ref _lastRolledDay);
            dataStore.SyncData("_reignKingdomEvents_testLedgers", ref _testLedgers);
            _events = _events ?? new List<ReignKingdomEventRecord>();
            _events.RemoveAll(record => record == null);
            _testLedgers = (_testLedgers ?? new List<ReignKingdomEventTestLedger>())
                .Where(ledger => ledger != null && !string.IsNullOrWhiteSpace(ledger.RunId))
                .GroupBy(ledger => ledger.RunId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last()).Take(64).ToList();
            foreach (ReignKingdomEventTestLedger ledger in _testLedgers)
                ledger.ObservationJson = ledger.ObservationJson ?? new List<string>();
            if (dataStore.IsLoading)
            {
                PruneHistory();
            }
        }

        private void OnDailyTick()
        {
            if (!CanRun())
            {
                return;
            }

            int day = CurrentDayIndex();
            ExpireEvents(day);
            if (_lastRolledDay >= day)
            {
                return;
            }

            // Save the attempt before resolving it. A save taken from an event
            // callback cannot replay the same campaign day.
            _lastRolledDay = day;
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            bool acceleratedTestTrigger = _testOrganicTriggerDay == day
                && !string.IsNullOrWhiteSpace(_testOrganicTriggerRunId);
            if (acceleratedTestTrigger)
            {
                // Consume before selection/effects so failures, callbacks, and saves cannot replay it.
                _testOrganicTriggerDay = -1;
                _testOrganicTriggerRunId = string.Empty;
            }
            if (!acceleratedTestTrigger && !ReignKingdomEventRollerCore.IsDailyTrigger(campaignId, day))
            {
                return;
            }

            bool beneficial = ReignKingdomEventRollerCore.IsBeneficial(campaignId, day);
            if (!TryTriggerPolarity(beneficial, campaignId, day))
            {
                ReignLog.Info("Kingdom event roll succeeded on day " + day
                    + " but no eligible " + (beneficial ? "beneficial" : "harmful")
                    + " archetype had a valid kingdom.");
            }
        }

        internal float GetFoodProductionFactor(Town town)
        {
            return GetTimedMagnitude(town?.OwnerClan?.Kingdom, "famine", "bountiful_harvest");
        }

        internal float GetHearthDelta(Village village)
        {
            Kingdom kingdom = village?.Settlement?.MapFaction as Kingdom;
            return GetTimedMagnitude(kingdom, "pestilence", "population_boom");
        }

        internal float GetSecurityDelta(Town town)
        {
            return GetTimedMagnitude(town?.OwnerClan?.Kingdom, "crime_wave", "law_and_order");
        }

        internal float GetProsperityDelta(Town town)
        {
            return GetTimedMagnitude(town?.OwnerClan?.Kingdom, "trade_collapse", "trade_boom");
        }

        internal float GetConstructionFactor(Town town)
        {
            return GetTimedMagnitude(town?.OwnerClan?.Kingdom, "construction_stagnation", "golden_age");
        }

        internal float GetLoyaltyDelta(Town town)
        {
            return GetTimedMagnitude(town?.OwnerClan?.Kingdom, "political_unrest", "national_unity");
        }

        internal int EligibleCount(string archetypeId)
        {
            ReignKingdomEventDefinition definition = FindDefinition(archetypeId);
            return definition == null ? 0 : EligibleKingdoms(definition).Count;
        }

        public bool ForceEvent(string archetypeId, string kingdomStringId, out string result)
        {
            result = string.Empty;
            if (TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                result = "Load a campaign before forcing a kingdom event.";
                return false;
            }

            ReignKingdomEventDefinition definition = FindDefinition(archetypeId);
            if (definition == null)
            {
                result = "Unknown kingdom event archetype '" + (archetypeId ?? string.Empty) + "'.";
                return false;
            }

            int day = CurrentDayIndex();
            ExpireEvents(day);
            List<Kingdom> eligible = EligibleKingdoms(definition);
            Kingdom selected = string.IsNullOrWhiteSpace(kingdomStringId)
                ? SelectKingdom(eligible, definition, day, true)
                : eligible.FirstOrDefault(kingdom => Same(kingdom.StringId, kingdomStringId));
            if (selected == null)
            {
                result = "No eligible kingdom is available for " + definition.Title + ".";
                return false;
            }

            return ExecuteDefinition(definition, selected, day, true, out _, out result);
        }

        private bool TryTriggerPolarity(bool beneficial, string campaignId, int day)
        {
            List<ReignKingdomEventDefinition> pool = Definitions
                .Where(definition => definition.Beneficial == beneficial)
                .ToList();
            IReadOnlyList<int> order = ReignKingdomEventRollerCore.BuildAttemptOrder(
                pool.Count, campaignId, day, "archetype|" + (beneficial ? "beneficial" : "harmful"));
            foreach (int index in order)
            {
                ReignKingdomEventDefinition definition = pool[index];
                List<Kingdom> eligible = EligibleKingdoms(definition);
                if (eligible.Count == 0)
                {
                    continue;
                }

                Kingdom selected = SelectKingdom(eligible, definition, day, false);
                if (selected != null)
                {
                    return ExecuteDefinition(definition, selected, day, false, out _, out _);
                }
            }
            return false;
        }

        private bool ExecuteDefinition(
            ReignKingdomEventDefinition definition,
            Kingdom kingdom,
            int day,
            bool forced,
            out ReignKingdomEventRecord record,
            out string result)
        {
            record = CreateRecord(definition, kingdom, day, forced);
            result = string.Empty;
            Kingdom secondary = null;
            Hero oldRuler = kingdom?.Leader;
            Hero heir = null;

            if (definition.Timed)
            {
                record.Outcome = definition.Title + " began in " + KingdomName(kingdom) + ".";
                _events.Add(record);
                result = record.Outcome;
            }
            else if (definition.Id == "border_crisis")
            {
                List<Kingdom> partners = EligibleWarPartners(kingdom);
                secondary = SelectPartner(partners, definition, kingdom, day, forced);
                if (secondary == null)
                {
                    return Fail(record, "No treaty-safe war target remained eligible.", out result);
                }
                record.SecondaryKingdomStringId = secondary.StringId;
                ReignAICampaignBehavior ai = ReignAICampaignBehavior.Instance;
                if (ai != null)
                {
                    ai.DeclareWarWithOrigin(kingdom, secondary, "kingdom_event",
                        "A kingdom-wide border crisis escalated into immediate war.", record.EventId, string.Empty, false);
                }
                else
                {
                    DeclareWarAction.ApplyByKingdomDecision(kingdom, secondary);
                }
                if (!kingdom.IsAtWarWith(secondary))
                {
                    return Fail(record, "The native war declaration did not take effect.", out result);
                }
                record.Status = "completed";
                record.Outcome = KingdomName(kingdom) + " declared war on " + KingdomName(secondary) + ".";
                result = record.Outcome;
                _events.Add(record);
            }
            else if (definition.Id == "grand_reconciliation")
            {
                List<Kingdom> partners = EligiblePeacePartners(kingdom);
                secondary = SelectPartner(partners, definition, kingdom, day, forced);
                if (secondary == null)
                {
                    return Fail(record, "No ordinary war remained eligible for peace.", out result);
                }
                record.SecondaryKingdomStringId = secondary.StringId;
                MakePeaceAction.ApplyByKingdomDecision(kingdom, secondary, 0, 0);
                if (kingdom.IsAtWarWith(secondary))
                {
                    return Fail(record, "The native peace action did not take effect.", out result);
                }
                record.Status = "completed";
                record.Outcome = KingdomName(kingdom) + " made immediate peace with " + KingdomName(secondary) + ".";
                result = record.Outcome;
                _events.Add(record);
            }
            else if (definition.Id == "orderly_succession")
            {
                if (!TrySelectHeir(kingdom, out heir))
                {
                    return Fail(record, "No valid living adult NPC heir remained eligible.", out result);
                }
                record.HeirHeroStringId = heir.StringId;
                kingdom.RulingClan.SetLeader(heir);
                CampaignEventDispatcher.Instance.OnClanLeaderChanged(oldRuler, heir);
                if (kingdom.Leader != heir)
                {
                    return Fail(record, "The ruling clan did not recognize the selected heir.", out result);
                }
                record.Status = "completed";
                record.Outcome = (oldRuler?.Name?.ToString() ?? "The ruler") + " abdicated; "
                    + heir.Name + " now rules " + KingdomName(kingdom) + ".";
                result = record.Outcome;
                _events.Add(record);
            }
            else
            {
                return Fail(record, "The selected event has no execution path.", out result);
            }

            RecordHistory(record, kingdom, secondary, oldRuler, heir, "started");
            ShowAnnouncement(record, kingdom, secondary, oldRuler, heir);
            ReignLog.Info("Kingdom event executed: " + record.ArchetypeId + " kingdom="
                + record.KingdomStringId + " secondary=" + record.SecondaryKingdomStringId
                + " forced=" + record.WasForced + ".");
            return true;
        }

        private bool Fail(ReignKingdomEventRecord record, string reason, out string result)
        {
            record.Status = "failed";
            record.Outcome = reason;
            _events.Add(record);
            result = record.Title + " failed: " + reason;
            ReignLog.Warn(result);
            return false;
        }

        private ReignKingdomEventRecord CreateRecord(
            ReignKingdomEventDefinition definition,
            Kingdom kingdom,
            int day,
            bool forced)
        {
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            return new ReignKingdomEventRecord
            {
                ArchetypeId = definition.Id,
                Title = definition.Title,
                Polarity = definition.Beneficial ? "beneficial" : "harmful",
                KingdomStringId = kingdom?.StringId ?? string.Empty,
                RulerHeroStringId = kingdom?.Leader?.StringId ?? string.Empty,
                StartDay = day,
                EndDay = definition.Timed ? day + TimedEventDurationDays : day,
                Status = definition.Timed ? "active" : "pending",
                Magnitude = definition.Magnitude,
                IsTimed = definition.Timed,
                WasForced = forced,
                RollDay = day,
                TriggerRollBasisPoints = forced ? -1 : (int)(ReignKingdomEventRollerCore.StableHash(
                    campaignId, day, "daily_trigger") % ReignKingdomEventRollerCore.BasisPointScale),
                Description = definition.Description
            };
        }

        private List<Kingdom> EligibleKingdoms(ReignKingdomEventDefinition definition)
        {
            IEnumerable<Kingdom> query = Kingdom.All
                .Where(IsEstablishedKingdom)
                .OrderBy(kingdom => kingdom.StringId, StringComparer.Ordinal);

            if (definition.Timed)
            {
                query = query.Where(kingdom => kingdom.Fiefs.Count > 0 && !HasActiveTimedEvent(kingdom));
                if (definition.Id == "famine" || definition.Id == "bountiful_harvest"
                    || definition.Id == "pestilence" || definition.Id == "population_boom")
                {
                    query = query.Where(kingdom => kingdom.Settlements.Any(settlement => settlement?.IsVillage == true));
                }
            }
            else if (definition.Id == "border_crisis")
            {
                query = query.Where(kingdom => EligibleWarPartners(kingdom).Count > 0);
            }
            else if (definition.Id == "grand_reconciliation")
            {
                query = query.Where(kingdom => EligiblePeacePartners(kingdom).Count > 0);
            }
            else if (definition.Id == "orderly_succession")
            {
                query = query.Where(kingdom => IsSuccessionEligible(kingdom));
            }
            return query.ToList();
        }

        private List<Kingdom> EligibleWarPartners(Kingdom actor)
        {
            return Kingdom.All
                .Where(IsEstablishedKingdom)
                .Where(target => target != actor && !actor.IsAtWarWith(target))
                .Where(target => !HasProtectedAgreement(actor, target))
                .OrderBy(target => target.StringId, StringComparer.Ordinal)
                .ToList();
        }

        private List<Kingdom> EligiblePeacePartners(Kingdom actor)
        {
            return Kingdom.All
                .Where(IsEstablishedKingdom)
                .Where(target => target != actor && actor.IsAtWarWith(target))
                .Where(target => !IsRebellionWar(actor, target))
                .OrderBy(target => target.StringId, StringComparer.Ordinal)
                .ToList();
        }

        private static bool HasProtectedAgreement(Kingdom left, Kingdom right)
        {
            float day = CurrentDay();
            return ReignAICampaignBehavior.Instance?.Agreements.Any(agreement => agreement != null
                && agreement.IsActive && !agreement.IsExpired(day)
                && SamePair(agreement.ActorKingdomStringId, agreement.TargetKingdomStringId,
                    left.StringId, right.StringId)) == true;
        }

        private static bool IsRebellionWar(Kingdom left, Kingdom right)
        {
            bool origin = ReignAICampaignBehavior.Instance?.WarOrigins.Any(record => record != null
                && record.IsActive && string.Equals(record.OriginKind, "rebellion", StringComparison.OrdinalIgnoreCase)
                && SamePair(record.AggressorKingdomStringId, record.DefenderKingdomStringId,
                    left.StringId, right.StringId)) == true;
            if (origin)
            {
                return true;
            }

            return ReignRebellionCampaignBehavior.Instance?.Movements.Any(movement => movement != null
                && !movement.ResolutionApplied
                && !string.IsNullOrWhiteSpace(movement.RebelKingdomStringId)
                && SamePair(movement.ParentKingdomStringId, movement.RebelKingdomStringId,
                    left.StringId, right.StringId)) == true;
        }

        private bool IsEstablishedKingdom(Kingdom kingdom)
        {
            return kingdom != null && !kingdom.IsEliminated
                && kingdom.RulingClan != null
                && kingdom.Leader != null && kingdom.Leader.IsAlive
                && ReignRebellionCampaignBehavior.Instance?.IsRebelKingdom(kingdom) != true;
        }

        private static bool IsSuccessionEligible(Kingdom kingdom)
        {
            if (kingdom?.RulingClan == null || kingdom.Leader == null
                || !kingdom.Leader.IsAlive || kingdom.Leader.IsPrisoner
                || kingdom.Leader == Hero.MainHero || kingdom.RulingClan == Clan.PlayerClan)
            {
                return false;
            }
            return EligibleHeirs(kingdom).Count > 0;
        }

        private static bool TrySelectHeir(Kingdom kingdom, out Hero heir)
        {
            heir = EligibleHeirs(kingdom)
                .OrderByDescending(row => row.Value)
                .ThenBy(row => row.Key.StringId, StringComparer.Ordinal)
                .Select(row => row.Key)
                .FirstOrDefault();
            return heir != null;
        }

        private static List<KeyValuePair<Hero, int>> EligibleHeirs(Kingdom kingdom)
        {
            if (kingdom?.RulingClan == null)
            {
                return new List<KeyValuePair<Hero, int>>();
            }
            float adultAge = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            return kingdom.RulingClan.GetHeirApparents()
                .Where(row => row.Key != null && row.Key != Hero.MainHero
                    && row.Key.IsAlive && !row.Key.IsChild && row.Key.Age >= adultAge
                    && !row.Key.IsPrisoner && row.Key.Clan == kingdom.RulingClan)
                .ToList();
        }

        private Kingdom SelectKingdom(
            List<Kingdom> eligible,
            ReignKingdomEventDefinition definition,
            int day,
            bool forced)
        {
            if (eligible == null || eligible.Count == 0)
            {
                return null;
            }
            string salt = definition.Id + "|kingdom" + (forced ? "|forced|" + _events.Count : string.Empty);
            int index = ReignKingdomEventRollerCore.SelectIndex(eligible.Count,
                ReignCampaignIdentity.CurrentCampaignId(), day, salt);
            return index < 0 ? null : eligible[index];
        }

        private Kingdom SelectPartner(
            List<Kingdom> eligible,
            ReignKingdomEventDefinition definition,
            Kingdom actor,
            int day,
            bool forced)
        {
            if (eligible == null || eligible.Count == 0)
            {
                return null;
            }
            string salt = definition.Id + "|partner|" + (actor?.StringId ?? string.Empty)
                + (forced ? "|forced|" + _events.Count : string.Empty);
            int index = ReignKingdomEventRollerCore.SelectIndex(eligible.Count,
                ReignCampaignIdentity.CurrentCampaignId(), day, salt);
            return index < 0 ? null : eligible[index];
        }

        private float GetTimedMagnitude(Kingdom kingdom, params string[] archetypeIds)
        {
            if (kingdom == null)
            {
                return 0f;
            }
            float day = CurrentDay();
            ReignKingdomEventRecord active = _events.LastOrDefault(record => record != null
                && record.IsActiveAt(day)
                && Same(record.KingdomStringId, kingdom.StringId));
            return active != null && archetypeIds.Any(id => Same(id, active.ArchetypeId))
                ? active.Magnitude
                : 0f;
        }

        private bool HasActiveTimedEvent(Kingdom kingdom)
        {
            float day = CurrentDay();
            return kingdom != null && _events.Any(record => record != null
                && record.IsActiveAt(day) && Same(record.KingdomStringId, kingdom.StringId));
        }

        private void ExpireEvents(float day)
        {
            foreach (ReignKingdomEventRecord record in _events.Where(record => record != null
                && record.IsTimed && Same(record.Status, "active")
                && (record.EndDay <= day || FindKingdom(record.KingdomStringId)?.IsEliminated != false)).ToList())
            {
                Kingdom kingdom = FindKingdom(record.KingdomStringId);
                record.Status = kingdom == null || kingdom.IsEliminated ? "cancelled" : "expired";
                record.Outcome = record.Status == "expired"
                    ? record.Title + " ended after " + TimedEventDurationDays.ToString("0") + " days."
                    : record.Title + " ended because its kingdom no longer exists.";
                RecordHistory(record, kingdom, null, kingdom?.Leader, null, "ended");
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] " + record.Outcome,
                    Color.FromUint(0xFFFFD36A)));
            }
        }

        private static void RecordHistory(
            ReignKingdomEventRecord record,
            Kingdom kingdom,
            Kingdom secondary,
            Hero actor,
            Hero heir,
            string phase)
        {
            JObject payload = new JObject
            {
                ["archetypeId"] = record.ArchetypeId,
                ["title"] = record.Title,
                ["polarity"] = record.Polarity,
                ["kingdomId"] = record.KingdomStringId,
                ["secondaryKingdomId"] = record.SecondaryKingdomStringId,
                ["rulerHeroId"] = record.RulerHeroStringId,
                ["heirHeroId"] = record.HeirHeroStringId,
                ["startDay"] = record.StartDay,
                ["endDay"] = record.EndDay,
                ["magnitude"] = record.Magnitude,
                ["timed"] = record.IsTimed,
                ["forced"] = record.WasForced,
                ["status"] = record.Status,
                ["outcome"] = record.Outcome,
                ["description"] = record.Description
            };
            JArray participants = new JArray();
            if (secondary?.Leader != null) participants.Add(secondary.Leader.StringId);
            if (heir != null) participants.Add(heir.StringId);
            string summary = phase == "ended"
                ? record.Outcome
                : (record.IsTimed
                    ? record.Title + " began across " + KingdomName(kingdom) + ". " + record.Description
                    : record.Outcome);
            ReignWorldHistoryCampaignBehavior.Instance?.RecordReignSystemEvent(
                "kingdom_fortune_event", phase, "kingdom_fortune", record.EventId,
                summary, "major_world", actor?.StringId ?? string.Empty,
                kingdom?.StringId ?? record.KingdomStringId, payload, participants);
        }

        private static void ShowAnnouncement(
            ReignKingdomEventRecord record,
            Kingdom kingdom,
            Kingdom secondary,
            Hero oldRuler,
            Hero heir)
        {
            string body = record.IsTimed
                ? KingdomName(kingdom) + " is now experiencing " + record.Title + ".\n\n"
                    + record.Description + "\n\nThe effects last until campaign day "
                    + record.EndDay.ToString("0") + "."
                : record.Outcome;
            if (secondary != null)
            {
                body += "\n\nOther kingdom: " + KingdomName(secondary) + ".";
            }
            if (heir != null)
            {
                body += "\n\nFormer ruler: " + (oldRuler?.Name?.ToString() ?? "Unknown")
                    + "\nNew ruler: " + heir.Name + ".";
            }
            InformationManager.DisplayMessage(new InformationMessage(
                "[Bannerlord Reign] " + record.Title + " — " + KingdomName(kingdom),
                Color.FromUint(record.Polarity == "beneficial" ? 0xFF66FF99u : 0xFFFF7777u)));
            string announcementEventId = record.EventId ?? string.Empty;
            if (announcementEventId.Length > 0
                && !PendingAnnouncementEventIds.Contains(announcementEventId))
            {
                PendingAnnouncementEventIds.Enqueue(announcementEventId);
            }
            InformationManager.ShowInquiry(new InquiryData(
                record.Title + " — " + KingdomName(kingdom), body,
                true, false, "Acknowledge", string.Empty,
                () => ClearActiveAnnouncement(record.EventId), null), true);
        }

        internal static bool TryAcknowledgeAnnouncementForLiveHarness(out string eventId)
        {
            eventId = PendingAnnouncementEventIds.Count > 0
                ? PendingAnnouncementEventIds.Peek()
                : string.Empty;
            if (eventId.Length == 0 || !InformationManager.IsAnyInquiryActive())
                return false;
            InformationManager.HideInquiry();
            PendingAnnouncementEventIds.Dequeue();
            return true;
        }

        private static void ClearActiveAnnouncement(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId)
                || PendingAnnouncementEventIds.Count == 0)
                return;
            Queue<string> retained = new Queue<string>(
                PendingAnnouncementEventIds.Where(candidate =>
                    !string.Equals(candidate, eventId, StringComparison.Ordinal)));
            PendingAnnouncementEventIds.Clear();
            while (retained.Count > 0)
                PendingAnnouncementEventIds.Enqueue(retained.Dequeue());
        }

        private void PruneHistory()
        {
            List<ReignKingdomEventRecord> active = (_events ?? new List<ReignKingdomEventRecord>())
                .Where(record => record != null && Same(record.Status, "active"))
                .ToList();
            List<ReignKingdomEventRecord> completed = (_events ?? new List<ReignKingdomEventRecord>())
                .Where(record => record != null && !Same(record.Status, "active"))
                .OrderByDescending(record => record.StartDay)
                .Take(RetainedCompletedEvents)
                .ToList();
            _events = active.Concat(completed)
                .OrderBy(record => record.StartDay)
                .ThenBy(record => record.EventId, StringComparer.Ordinal)
                .ToList();
        }

        private static bool CanRun()
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null && (!settings.Enabled || !settings.KingdomEventsEnabled))
            {
                return false;
            }
            if (ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || ReignSaveSyncCoordinator.IsAlignmentPending
                || TaleWorlds.CampaignSystem.Campaign.Current == null
                || TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode == CampaignTimeControlMode.Stop)
            {
                return false;
            }
            return ReignCampaignPreparationCampaignBehavior
                .AreAutonomousWorldSystemsUnlocked(CurrentDay());
        }

        private static ReignKingdomEventDefinition FindDefinition(string archetypeId)
        {
            return Definitions.FirstOrDefault(definition => Same(definition.Id, archetypeId));
        }

        private static Kingdom FindKingdom(string kingdomStringId)
        {
            return Kingdom.All.FirstOrDefault(kingdom => kingdom != null && Same(kingdom.StringId, kingdomStringId));
        }

        private static int CurrentDayIndex()
        {
            return (int)Math.Floor(CurrentDay());
        }

        private static float CurrentDay()
        {
            return (float)CampaignTime.Now.ToDays;
        }

        private static string KingdomName(Kingdom kingdom)
        {
            return kingdom?.InformalName?.ToString() ?? kingdom?.Name?.ToString() ?? "an unknown kingdom";
        }

        private static bool Same(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SamePair(string firstLeft, string firstRight, string secondLeft, string secondRight)
        {
            return (Same(firstLeft, secondLeft) && Same(firstRight, secondRight))
                || (Same(firstLeft, secondRight) && Same(firstRight, secondLeft));
        }
    }
}
