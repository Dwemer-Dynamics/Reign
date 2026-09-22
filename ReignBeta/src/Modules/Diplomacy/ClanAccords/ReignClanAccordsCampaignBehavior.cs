using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.ClanAccords;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.ClanAccords
{
    /// <summary>Native-save authority. Server projections own memory and relationship consequences.</summary>
    public sealed class ReignClanAccordsCampaignBehavior : CampaignBehaviorBase
    {
        public sealed class SavedState
        {
            public ClanAccordLedger Ledger = new ClanAccordLedger();
            public List<JObject> Outbox = new List<JObject>();
            public int LastSeason = -1;
        }

        private SavedState _state = new SavedState();
        private bool _inFlight;
        private bool _dirty = true;
        private string _lastSyncError = string.Empty;
        private long _revision;
        private long _syncRequestId;
        private long _nextSyncAttempt;
        private float _pumpElapsed;
        public static ReignClanAccordsCampaignBehavior Instance { get; private set; }
        public IReadOnlyList<ClanAccordRecord> Records => _state.Ledger.Records.ToList();
        public bool HasPendingWork => _inFlight || _dirty || _state.Outbox.Count > 0;
        public static bool IsPlayerRuler => Hero.MainHero != null && Clan.PlayerClan?.Kingdom?.Leader == Hero.MainHero;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, OnLoaded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, clan => Reconcile());
            CampaignEvents.OnClanDefectedEvent.AddNonSerializedListener(this, (clan, oldKingdom, newKingdom) => Reconcile());
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this,
                (clan, oldKingdom, newKingdom, detail, showNotification) => Reconcile());
        }

        public override void SyncData(IDataStore dataStore)
        {
            string json = dataStore.IsSaving ? JsonConvert.SerializeObject(_state) : string.Empty;
            dataStore.SyncData("_reign_clan_accords_v1", ref json);
            if (dataStore.IsLoading)
            {
                // Malformed saved state is a load error, never silently erase an accord ledger.
                _state = string.IsNullOrEmpty(json) ? new SavedState()
                    : JsonConvert.DeserializeObject<SavedState>(json) ?? throw new InvalidOperationException("Clan Accords save is empty.");
                if (_state.Ledger == null || _state.Outbox == null || _state.Ledger.Records == null
                    || _state.Ledger.SeasonalReceipts == null || _state.Ledger.Records.Any(x => x == null)
                    || _state.Ledger.SeasonalReceipts.Any(x => x == null) || _state.Outbox.Any(x => x == null))
                    throw new InvalidOperationException("Clan Accords save is incomplete.");
                // Invalidate acknowledgments from a pre-load state, even on the same timeline.
                _syncRequestId++;
                _inFlight = false;
                _nextSyncAttempt = 0;
                _pumpElapsed = 0f;
                _dirty = true;
                _revision++;
            }
        }

        private void OnLoaded()
        {
            Instance = this;
            if (_state.LastSeason < 0) _state.LastSeason = SeasonIndex();
            _dirty = true;
        }

        public bool ValidateAction(ReignWorldActionRecord action, out string error)
        {
            error = string.Empty;
            if (action == null || string.IsNullOrWhiteSpace(action.ActionId))
                error = "An identified Clan Accords action is required.";
            else if (!IsPlayerRuler) error = "Clan Accords are currently available only to player rulers.";
            else if (ReignCampaignInitializationGate.IsPending) error = "Campaign initialization is still in progress.";
            else if (action.Type != ReignWorldActionType.RegularCreateClanAccord && action.Type != ReignWorldActionType.RegularCancelClanAccord)
                error = "Unsupported Clan Accords action.";
            if (error.Length > 0) return false;
            Reconcile();
            JObject terms;
            try { terms = JObject.Parse(action.TermsJson ?? "{}"); }
            catch (JsonException) { error = "Invalid Clan Accords terms."; return false; }
            if (terms.Value<bool?>("playerConfirmed") != true)
            { error = "The player must explicitly confirm this agreement or cancellation."; return false; }
            bool fromUi = action.Source == "clan_accords_ui";
            if (!fromUi && !ReignActionValidator.IsDialogueActionSource(action.Source))
            { error = "Clan Accords must originate from a player conversation or the management screen."; return false; }
            if (action.Type == ReignWorldActionType.RegularCancelClanAccord)
            {
                ClanAccordRecord record = Records.FirstOrDefault(x => x.Id == terms.Value<string>("accordId"));
                if (record == null || record.PlayerClanId != Clan.PlayerClan.StringId)
                { error = "The selected agreement does not belong to the player clan."; return false; }
                if (fromUi && action.ActorHeroStringId != Hero.MainHero.StringId)
                { error = "Only the player may use the Clan Accords cancellation control."; return false; }
                if (!fromUi && ReignObjectResolver.FindHero(action.ActorHeroStringId)?.Clan?.StringId != record.PartnerClanId)
                { error = "This conversation does not concern the selected partner clan."; return false; }
                return true;
            }
            Hero npc = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            if (fromUi) error = "New accords must be arranged in conversation.";
            else if (npc == null || npc == Hero.MainHero || !npc.IsAlive || npc.IsChild || !npc.IsLord || npc.Clan == null || npc.Clan.IsEliminated)
                error = "An eligible adult noble must negotiate for their clan.";
            else if (npc.Clan == Clan.PlayerClan) error = "An accord requires a different clan.";
            else if (Hostile(npc.Clan)) error = "Accords cannot be signed with an opposing clan during war.";
            else if (action.TargetHeroStringId != Hero.MainHero.StringId) error = "The player must be the other negotiator.";
            else if (terms.Value<string>("partnerClanId") != npc.Clan.StringId) error = "The negotiator's clan has changed or is unresolved.";
            else if (terms.Value<bool?>("consentConfirmed") != true) error = "The NPC must explicitly accept the accord.";
            else if (!Enum.TryParse(terms.Value<string>("accordType"), true, out ClanAccordType type) || !Enum.IsDefined(typeof(ClanAccordType), type))
                error = "Choose a supported Clan Accord type.";
            else if (!Records.Any(x => x.ActionId == action.ActionId)
                && (_state.Ledger.ActiveCount(Clan.PlayerClan.StringId, type) >= Clan.PlayerClan.Tier
                    || Records.Any(x => x.IsActive && x.PlayerClanId == Clan.PlayerClan.StringId && x.PartnerClanId == npc.Clan.StringId && x.Type == type)))
                error = "This accord type is already active with that clan or all player slots are occupied.";
            return error.Length == 0;
        }

        public ReignActionResult Execute(ReignWorldActionRecord action)
        {
            if (!ValidateAction(action, out string error)) return ReignActionResult.ValidationFailed(error);
            JObject terms = JObject.Parse(action.TermsJson ?? "{}");
            ClanAccordResult result;
            if (action.Type == ReignWorldActionType.RegularCancelClanAccord)
                result = _state.Ledger.Cancel(terms.Value<string>("accordId"), action.ActionId,
                    Hero.MainHero.StringId, Hero.MainHero.Name.ToString(), Day(), true);
            else
            {
                Hero npc = ReignObjectResolver.FindHero(action.ActorHeroStringId);
                result = _state.Ledger.Create(new ClanAccordCreateRequest
                {
                    ActionId = action.ActionId, Type = (ClanAccordType)Enum.Parse(typeof(ClanAccordType), terms.Value<string>("accordType"), true),
                    PlayerClanId = Clan.PlayerClan.StringId, PlayerClanName = Clan.PlayerClan.Name.ToString(),
                    PartnerClanId = npc.Clan.StringId, PartnerClanName = npc.Clan.Name.ToString(),
                    PlayerArrangerId = Hero.MainHero.StringId, PlayerArrangerName = Hero.MainHero.Name.ToString(),
                    NpcArrangerId = npc.StringId, NpcArrangerName = npc.Name.ToString(), CampaignDay = Day(),
                    PlayerClanTier = Clan.PlayerClan.Tier, PlayerAccepted = true, NpcAccepted = true, IsHostile = false
                });
            }
            if (!result.Success) return ReignActionResult.ValidationFailed(result.Error);
            if (result.Changed)
            {
                QueueEvent(result.Record.IsActive ? "formed" : "ended", result.Record);
                OnHourlyTick();
            }
            string verb = result.Record.IsActive ? "established" : "ended";
            return ReignActionResult.Done("Clan accord " + verb + ": " + result.Record.Type + " with " + result.Record.PartnerClanName + ".")
                .WithEffect("clan_accord_" + verb, "clan", result.Record.PartnerClanId, result.Record.PartnerClanName, "accordId=" + result.Record.Id);
        }

        public string CancelFromUi(string accordId)
        {
            var action = new ReignWorldActionRecord
            {
                ActionId = "clan_accord_cancel_" + Guid.NewGuid().ToString("N"),
                Type = ReignWorldActionType.RegularCancelClanAccord, Source = "clan_accords_ui",
                ActorHeroStringId = Hero.MainHero?.StringId ?? string.Empty,
                TargetHeroStringId = Hero.MainHero?.StringId ?? string.Empty,
                TermsJson = new JObject { ["accordId"] = accordId, ["playerConfirmed"] = true }.ToString(Formatting.None)
            };
            if (!ValidateAction(action, out string error)) return error;
            // Same lifecycle service and native validation as the conversation action.
            ReignActionResult result = Execute(action);
            return result.Success ? string.Empty : result.Message;
        }

        public ClanAccordBonuses GetBonuses(Clan clan)
        {
            if (clan == null || clan.IsEliminated || Clan.PlayerClan?.IsEliminated != false) return new ClanAccordBonuses();
            // Observation must never mutate lifecycle; hostility immediately suppresses benefits.
            // Filter to the queried clan before any linear native clan resolution.
            var live = new ClanAccordLedger { Records = _state.Ledger.Records.Where(x => x.IsActive
                && x.PlayerClanId == Clan.PlayerClan.StringId
                && (x.PlayerClanId == clan.StringId || x.PartnerClanId == clan.StringId)
                && !Hostile(x.PartnerClanId == clan.StringId ? clan : ReignObjectResolver.FindClan(x.PartnerClanId))).ToList() };
            return live.GetBonuses(clan.StringId);
        }

        public JObject Snapshot() => new JObject
        {
            ["schema"] = "reign-clan-accords-v1", ["playerClanId"] = Clan.PlayerClan?.StringId ?? string.Empty,
            ["playerClanTier"] = Clan.PlayerClan?.Tier ?? 0, ["ledger"] = JObject.FromObject(_state.Ledger),
            ["bonuses"] = JObject.FromObject(GetBonuses(Clan.PlayerClan)), ["pendingEvents"] = _state.Outbox.Count,
            ["syncInFlight"] = _inFlight, ["syncDirty"] = _dirty, ["lastSyncError"] = _lastSyncError
        };

        public JObject ConversationContext(Hero npc)
        {
            if (npc?.Clan == null) return new JObject();
            return new JObject
            {
                ["playerRuler"] = IsPlayerRuler, ["playerClanId"] = Clan.PlayerClan?.StringId ?? "",
                ["playerClanTier"] = Clan.PlayerClan?.Tier ?? 0, ["partnerClanId"] = npc.Clan.StringId,
                ["eligible"] = IsPlayerRuler && npc.IsAlive && !npc.IsChild && npc.IsLord && npc.Clan != Clan.PlayerClan && !Hostile(npc.Clan),
                ["records"] = JArray.FromObject(Records.Where(x => x.PartnerClanId == npc.Clan.StringId)
                    .OrderByDescending(x => x.IsActive).ThenByDescending(x => x.StartDay).Take(30)),
                ["used"] = JObject.FromObject(Enum.GetValues(typeof(ClanAccordType)).Cast<ClanAccordType>()
                    .ToDictionary(x => x.ToString(), x => _state.Ledger.ActiveCount(Clan.PlayerClan?.StringId ?? "", x)))
            };
        }

        private void OnWarDeclared(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail) => Reconcile();
        private void Reconcile()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            foreach (string id in Records.Where(x => x.IsActive).Select(x => x.PartnerClanId).Distinct().ToList())
            {
                Clan partner = ReignObjectResolver.FindClan(id);
                ClanAccordEndReason reason = partner == null || partner.IsEliminated || Clan.PlayerClan?.IsEliminated != false
                    ? ClanAccordEndReason.ClanEliminated : Hostile(partner) ? ClanAccordEndReason.War : ClanAccordEndReason.None;
                if (reason == ClanAccordEndReason.None) continue;
                foreach (ClanAccordRecord record in _state.Ledger.EndForPartner(id, reason, Day())) QueueEvent("ended", record);
            }
        }

        private void OnDailyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            Reconcile();
            int season = SeasonIndex();
            if (_state.LastSeason >= 0 && season > _state.LastSeason)
                foreach (var record in Records.Where(x => x.IsActive).GroupBy(x => x.PartnerClanId).Select(x => x.First()))
                    QueueEvent("season", record);
            _state.LastSeason = season;
            _dirty = true;
            OnHourlyTick();
        }

        private void QueueEvent(string kind, ClanAccordRecord record)
        {
            Clan partner = ReignObjectResolver.FindClan(record.PartnerClanId);
            var memberIds = AdultMembers(partner).Select(x => x.StringId).ToList();
            string id = kind == "season" ? "season:" + record.PlayerClanId + ":" + record.PartnerClanId + ":" + SeasonIndex()
                : kind + ":" + record.Id;
            if (_state.Outbox.Any(x => x.Value<string>("eventId") == id)) return;
            _state.Outbox.Add(new JObject
            {
                ["eventId"] = id, ["kind"] = kind, ["worldDay"] = Day(),
                ["record"] = kind == "season" ? null : JObject.FromObject(record),
                ["partnerClanId"] = record.PartnerClanId, ["playerHeroId"] = Hero.MainHero?.StringId ?? record.PlayerArrangerId,
                ["year"] = CampaignTime.Now.GetYear, ["season"] = (int)CampaignTime.Now.GetSeasonOfYear,
                ["endReason"] = record.EndReason.ToString(), ["partnerMemberIds"] = new JArray(memberIds),
                ["knownHeroIds"] = new JArray(memberIds.Concat(AdultMembers(Clan.PlayerClan).Select(x => x.StringId)).Distinct())
            });
            _dirty = true;
            _revision++;
        }

        private void OnHourlyTick()
        {
            if (Hero.MainHero == null || TaleWorlds.CampaignSystem.Campaign.Current == null || ReignCampaignInitializationGate.IsPending
                || ReignWorldHistoryCampaignBehavior.Instance?.TimelineReady != true) return;
            Reconcile();
            if (_inFlight || !_dirty || System.Diagnostics.Stopwatch.GetTimestamp() < _nextSyncAttempt) return;
            JObject payload = Snapshot();
            payload["playerHeroId"] = Hero.MainHero.StringId;
            payload["events"] = new JArray(_state.Outbox.Take(64).Select(x => x.DeepClone()));
            string campaign = ReignServerClient.GetCampaignId();
            string timeline = ReignWorldHistoryCampaignBehavior.Instance.TimelineId;
            payload["campaignId"] = campaign;
            payload["timelineId"] = timeline;
            _inFlight = true;
            _nextSyncAttempt = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency;
            _ = SyncAsync(payload, campaign, timeline, _revision, _state, ++_syncRequestId);
        }

        /// <summary>Call from SubModule.OnApplicationTick on the main thread; paused native time must still drain saves.</summary>
        public void ApplicationTick(float dt)
        {
            if (Instance != this || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            _pumpElapsed += dt;
            if (_pumpElapsed < 1f) return;
            _pumpElapsed = 0f;
            OnHourlyTick();
        }

        private async Task SyncAsync(JObject payload, string campaign, string timeline, long revision,
            SavedState sentState, long requestId)
        {
            JObject result = null;
            string error = null;
            try { result = await ReignServerClient.SyncClanAccordsAsync(payload).ConfigureAwait(false); }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (Instance != this || requestId != _syncRequestId || !ReferenceEquals(_state, sentState)) return;
                    try
                    {
                        if (campaign != ReignServerClient.GetCampaignId()
                            || timeline != ReignWorldHistoryCampaignBehavior.Instance?.TimelineId) return;
                        if (error != null || result?.Value<bool?>("ok") != true)
                            throw new InvalidOperationException(error ?? result?.Value<string>("error") ?? "Accord synchronization failed.");
                        var acknowledged = new HashSet<string>((result["acknowledgedEventIds"] as JArray ?? new JArray()).Values<string>());
                        // Only acknowledge IDs actually sent in this bounded batch.
                        var sentIds = new HashSet<string>(((JArray)payload["events"]).Select(x => x.Value<string>("eventId")));
                        _state.Outbox.RemoveAll(x => acknowledged.Contains(x.Value<string>("eventId")) && sentIds.Contains(x.Value<string>("eventId")));
                        _dirty = revision != _revision || _state.Outbox.Count > 0;
                        _lastSyncError = string.Empty;
                    }
                    catch (Exception ex)
                    {
                        _dirty = true;
                        _lastSyncError = ex.Message;
                        _nextSyncAttempt = System.Diagnostics.Stopwatch.GetTimestamp() + 5 * System.Diagnostics.Stopwatch.Frequency;
                        ReignLog.Warn("Clan Accords synchronization retained for retry: " + ex.Message);
                    }
                    finally { _inFlight = false; }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // An unavailable main-thread dispatcher generally means campaign shutdown.
                // Do not mutate native state from this worker or clear a newer request's flag.
                ReignLog.Warn("Clan Accords completion awaits campaign recovery: " + ex.Message);
            }
        }

        private static IEnumerable<Hero> AdultMembers(Clan clan) => clan?.Heroes.Where(x => x.IsAlive && !x.IsChild) ?? Enumerable.Empty<Hero>();
        private static bool Hostile(Clan clan) => clan == null || clan.IsEliminated || (Clan.PlayerClan != null && clan.IsAtWarWith(Clan.PlayerClan));
        private static double Day() => CampaignTime.Now.ToDays;
        private static int SeasonIndex() => CampaignTime.Now.GetYear * CampaignTime.SeasonsInYear + (int)CampaignTime.Now.GetSeasonOfYear;
    }
}
