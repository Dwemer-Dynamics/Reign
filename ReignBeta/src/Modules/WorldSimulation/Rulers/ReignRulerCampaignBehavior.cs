using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Save;
using ReignBeta.Shared.Rulers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignRulerCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignRulerCampaignBehavior Instance { get; private set; }
        internal RulerState State = new RulerState();
        private List<string> _chunks = new List<string>();
        private List<JObject> _relationOutbox = new List<JObject>();
        private bool _networkBusy, _presenting, _initialized;
        private double _tick;
        private int _epoch;
        internal bool ApplyingNative;
        internal static bool Enabled(string feature) => ReignServerClient.RulerOptionsConfirmed && ReignServerClient.RulerOptions.Enabled(feature);
        internal static Hero HeroById(string id) => Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id) ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(h => h.StringId == id);
        internal static Kingdom Realm(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id);
        internal static Clan ClanById(string id) => Clan.All.FirstOrDefault(c => c.StringId == id);
        internal static Settlement Holding(string id) => Settlement.All.FirstOrDefault(s => s.StringId == id);
        internal static bool Sovereign(Hero hero) => hero != null && hero.IsAlive && hero.Clan?.Kingdom?.Leader == hero;
        internal static double Day => CampaignTime.Now.ToDays;
        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSession);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, Hourly);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, WarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, PeaceMade);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OwnerChanged);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, PrisonerTaken);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, PrisonerReleased);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, HeroKilled);
            CampaignEvents.HeroRelationChanged.AddNonSerializedListener(this, NativeExecutionRelationChanged);
        }
        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving) _chunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(new { state = State, relations = _relationOutbox }));
            dataStore.SyncData("ReignRulerSystemV1", ref _chunks);
            if (!dataStore.IsLoading) return;
            _captureControls.Clear();
            _epoch++; _initialized = _networkBusy = _presenting = false; _tick = 0;
            if (_chunks == null || _chunks.Count == 0) { State = new RulerState(); return; }
            var saved = JObject.Parse(ReignSavePayloadCodec.Decode(_chunks));
            State = saved["state"]?.ToObject<RulerState>() ?? new RulerState();
            if (State.Version != 1) throw new InvalidOperationException("Unsupported ruler save format.");
            _relationOutbox = saved["relations"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        }
        private void OnSession(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("reign_ruler_handover", "hero_main_options", "close_window", "I have captured an enemy ruler. I place their fate in your hands.",
                () => Enabled(RulerOptions.Judgments) && Hero.OneToOneConversationHero == Clan.PlayerClan?.Kingdom?.Leader
                    && Hero.OneToOneConversationHero != Hero.MainHero && HeldPlayerRuler() != null,
                () => HandOver(HeldPlayerRuler(), Hero.OneToOneConversationHero));
            starter.AddPlayerLine("reign_ruler_judgment", "hero_main_options", "close_window", "I will decide your fate as a captive ruler.",
                () => State.Judgments.Any(j => j.PrisonerId == Hero.OneToOneConversationHero?.StringId && j.VictorId == Hero.MainHero?.StringId && j.Stage == RulerJudgmentStage.AwaitingDecision),
                () => _presenting = false);
        }
        internal void OptionsChanged()
        {
            foreach (var j in State.Judgments.Where(j => j.Stage != RulerJudgmentStage.Completed && j.Stage != RulerJudgmentStage.Cancelled && j.Stage != RulerJudgmentStage.Applying))
                if (!Enabled(RulerOptions.Judgments) || (j.CivilWar && !Enabled(RulerOptions.CivilWars)))
                    ReignRulerPolicy.Cancel(j, "feature_disabled");
            if (Enabled(RulerOptions.Mortality) && NativeCampaign.Current != null) CampaignOptions.IsLifeDeathCycleDisabled = false;
        }
        internal void ApplicationTick(float dt)
        {
            _tick += Math.Max(0, dt);
            if (_tick < 5 || NativeCampaign.Current == null || Hero.MainHero == null || ReignCampaignInitializationGate.IsPending || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress) return;
            _tick = 0;
            if (!ReignServerClient.RulerOptionsConfirmed) return;
            OptionsChanged();
            if (!_initialized) { InitializeWorld(); _initialized = true; }
            if (_relationOutbox.Count > 0 && !_networkBusy) SendRelations(_relationOutbox[0]);
            foreach (var j in State.Judgments.ToList())
            {
                if (j.Stage == RulerJudgmentStage.Completed || j.Stage == RulerJudgmentStage.Cancelled) continue;
                if (j.Stage == RulerJudgmentStage.AwaitingDecision && Realm(j.VictorKingdomId)?.Leader?.IsAlive == true)
                    j.VictorId = Realm(j.VictorKingdomId).Leader.StringId;
                Hero prisoner = HeroById(j.PrisonerId);
                if (prisoner == null) { j.Failure = "Prisoner identity is unavailable."; continue; }
                if (!j.NativeDeathObserved && !j.NativeExecutionRequested && (!prisoner.IsPrisoner || prisoner.PartyBelongedToAsPrisoner?.MapFaction != Realm(j.VictorKingdomId)))
                { ReignRulerPolicy.Cancel(j, "escaped_or_rescued"); continue; }
                if (j.Stage == RulerJudgmentStage.AwaitingAuthority)
                {
                    if (Realm(j.VictorKingdomId)?.Leader != Hero.MainHero) continue;
                    j.VictorId = Hero.MainHero.StringId; j.Stage = RulerJudgmentStage.AwaitingDecision;
                }
                if (j.Stage == RulerJudgmentStage.AwaitingDecision && !_networkBusy)
                {
                    if (HeroById(j.VictorId) == Hero.MainHero) { if (!_presenting) PrepareDecision(j, null); }
                    else PrepareDecision(j, null);
                    break;
                }
                if (ReignRulerPolicy.SentenceDue(j, Day) || j.Stage == RulerJudgmentStage.Applying) ApplyJudgment(j);
            }
        }
        private void InitializeWorld()
        {
            if (Enabled(RulerOptions.Capitals)) foreach (var k in Kingdom.All.Where(k => !k.IsEliminated)) Capital(k);
            foreach (var a in Kingdom.All.Where(k => !k.IsEliminated))
                foreach (var b in Kingdom.All.Where(k => !k.IsEliminated && string.CompareOrdinal(k.StringId, a.StringId) > 0 && a.IsAtWarWith(k))) GetWar(a, b, true);
        }
        private void Hourly()
        {
            if (Enabled(RulerOptions.Judgments)) foreach (var j in State.Judgments.Where(j => ReignRulerPolicy.SentenceDue(j, Day)).ToList()) ApplyJudgment(j);
            if (!Enabled(RulerOptions.Capitals)) return;
            foreach (var kingdom in Kingdom.All.Where(k => !k.IsEliminated).ToList())
            {
                var ruler = kingdom.Leader; var capital = Capital(kingdom);
                if (ruler == null || ruler == Hero.MainHero || ruler.IsPrisoner || !ruler.IsAlive || !ruler.IsActive || capital == null) continue;
                // Accepted attendance owns this finite stay. Capital enforcement
                // resumes after its owner releases the ruler; do not bounce them
                // between the venue and capital or abandon their return intent.
                if (ReignNpcLivesBehavior.Instance?.HasOwnedMeetingTeleportDeferral(ruler.StringId) == true
                    || ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance?.IsGovernmentAttendanceReserved(ruler) == true
                    || ReignBeta.Court.ReignCourtCampaignBehavior.Instance?.IsNobleVisitorReserved(ruler) == true) continue;
                MobileParty party = ruler.PartyBelongedTo;
                if (party == null)
                {
                    if (ruler.CurrentSettlement != capital && !capital.IsUnderSiege)
                    { if (ruler.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(ruler); EnterSettlementAction.ApplyForCharacterOnly(ruler, capital); }
                    continue;
                }
                // A visiting ruler can be a passenger. They cannot commandeer
                // the player's party or a different leader's movement orders.
                if (party == MobileParty.MainParty || party.LeaderHero != ruler) continue;
                if (party.MapEvent != null || party.SiegeEvent != null) continue;
                bool commander = party.Army?.LeaderParty == party;
                bool returning = State.Returns.ContainsKey(ruler.StringId);
                if (commander && !returning && ReignRulerPolicy.CanDepart(true, ArmyHealthy(party))) continue;
                if (commander && !returning && !ReignRulerPolicy.ShouldReturn(true, ArmyHealthy(party), false)) continue;
                if (party.CurrentSettlement == capital)
                {
                    State.Returns.Remove(ruler.StringId); party.SetMoveModeHold(); continue;
                }
                State.Returns[ruler.StringId] = capital.StringId;
                if (party.Army != null) DisbandArmyAction.ApplyByNotEnoughParty(party.Army);
                party.SetMoveGoToSettlement(capital, party.NavigationCapability, false);
            }
        }
        internal Settlement Capital(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated) return null;
            var row = State.Capitals.FirstOrDefault(c => c.KingdomId == kingdom.StringId);
            Settlement current = row == null ? null : Holding(row.SettlementId);
            if (current != null && current.OwnerClan?.Kingdom == kingdom) return current;
            var candidate = ReignRulerPolicy.SelectCapital(Settlement.All.Where(s => (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == kingdom && !s.IsUnderSiege)
                .Select(s => new RulerCapitalCandidate { Id = s.StringId, Town = s.IsTown, RulingClan = s.OwnerClan == kingdom.RulingClan,
                    GarrisonStrength = s.Town?.GarrisonParty?.Party.CalculateCurrentStrength() ?? 0, ClanTier = s.OwnerClan?.Tier ?? 0 }));
            if (row == null) { row = new RulerCapital { KingdomId = kingdom.StringId }; State.Capitals.Add(row); }
            row.SettlementId = candidate?.Id ?? ""; row.RulingClanId = kingdom.RulingClan?.StringId ?? ""; row.SelectedDay = Day; row.Manual = false;
            if (kingdom.Leader != null) State.Returns[kingdom.Leader.StringId] = row.SettlementId;
            return Holding(row.SettlementId);
        }
        internal void DesignateCapital(Kingdom kingdom, Settlement town)
        {
            if (kingdom == null || town?.IsTown != true || town.OwnerClan?.Kingdom != kingdom || town.IsUnderSiege) return;
            Capital(kingdom); var row = State.Capitals.Single(c => c.KingdomId == kingdom.StringId);
            row.SettlementId = town.StringId; row.Manual = true; row.SelectedDay = Day;
        }
        internal static int ArmyHealthy(MobileParty party) => party != null && party.Army?.LeaderParty == party
            ? party.Army.Parties.Where(p => p == party || p.AttachedTo == party).Sum(p => p.Party.NumberOfHealthyMembers) : 0;
        internal bool MayMove(MobileParty party, Settlement destination = null)
        {
            var hero = party?.LeaderHero;
            if (!Enabled(RulerOptions.Capitals) || !Sovereign(hero) || hero == Hero.MainHero || ApplyingNative) return true;
            var capital = Capital(hero.Clan.Kingdom);
            if (capital == null) return true;
            // Arrival ends the travel exception immediately, including between hourly ticks.
            ReignRulerPolicy.CompleteReturnIfArrived(State, hero.StringId, party.CurrentSettlement?.StringId);
            if (State.Returns.TryGetValue(hero.StringId, out string target)) return destination?.StringId == target;
            if (destination == capital) return true;
            return party.Army?.LeaderParty == party && (party.CurrentSettlement == capital
                ? ArmyHealthy(party) >= ReignRulerPolicy.DepartureTroops : ArmyHealthy(party) >= ReignRulerPolicy.ReturnTroops);
        }
        internal bool MayLeave(MobileParty party) => MayMove(party)
            || party?.LeaderHero != null && State.Returns.ContainsKey(party.LeaderHero.StringId);
        internal void ObjectiveCompleted(Army army)
        { var ruler = army?.LeaderParty?.LeaderHero; if (Enabled(RulerOptions.Capitals) && Sovereign(ruler) && ruler != Hero.MainHero) State.Returns[ruler.StringId] = Capital(army.Kingdom)?.StringId ?? ""; }
        private static List<string> Holdings(Kingdom k) => Settlement.All.Where(s => (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == k).Select(s => s.StringId).ToList();
        private RulerWar GetWar(Kingdom a, Kingdom b, bool migration = false)
        {
            var prior = State.Wars.LastOrDefault(w => w.Active && ((w.KingdomA == a.StringId && w.KingdomB == b.StringId) || (w.KingdomB == a.StringId && w.KingdomA == b.StringId)));
            if (prior != null) return prior;
            var war = new RulerWar { Id = Guid.NewGuid().ToString("N"), KingdomA = a.StringId, KingdomB = b.StringId, StartDay = Day,
                HomelandA = Holdings(a), HomelandB = Holdings(b), MigrationBaseline = migration };
            State.Wars.Add(war); return war;
        }
        private void WarDeclared(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        { if (first is Kingdom a && second is Kingdom b) GetWar(a, b); }
        private void PeaceMade(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        { foreach (var w in State.Wars.Where(w => (w.KingdomA == first.StringId && w.KingdomB == second.StringId) || (w.KingdomB == first.StringId && w.KingdomA == second.StringId))) w.Active = false; }
        private void OwnerChanged(Settlement settlement, bool open, Hero newOwner, Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            var victor = newOwner?.Clan?.Kingdom; var loser = oldOwner?.Clan?.Kingdom;
            if (victor == null || loser == null || victor == loser || (!settlement.IsTown && !settlement.IsCastle)) return;
            var war = State.Wars.LastOrDefault(w => w.Active && ((w.KingdomA == victor.StringId && w.KingdomB == loser.StringId) || (w.KingdomB == victor.StringId && w.KingdomA == loser.StringId)));
            if (war != null) { var list = war.KingdomA == victor.StringId ? war.CapturesA : war.CapturesB; if (!list.Contains(settlement.StringId)) list.Add(settlement.StringId); }
            foreach (var e in State.Effects.Where(e => e.VictorKingdomId == victor.StringId && e.DefeatedKingdomId == loser.StringId && ReignRulerPolicy.Fade(e.Verdict, e.StartDay, Day) > 0))
                if (!e.Conquered.Contains(settlement.StringId)) e.Conquered.Add(settlement.StringId);
        }
        private Hero HeldPlayerRuler() => State.Judgments.Where(j => j.Stage == RulerJudgmentStage.AwaitingAuthority).Select(j => HeroById(j.PrisonerId))
            .FirstOrDefault(h => h?.IsPrisoner == true && h.PartyBelongedToAsPrisoner == PartyBase.MainParty);
        private void HandOver(Hero prisoner, Hero sovereign)
        {
            if (prisoner == null || sovereign?.Clan?.Kingdom != Clan.PlayerClan?.Kingdom) return;
            PartyBase custody = sovereign.PartyBelongedTo?.Party ?? Capital(sovereign.Clan.Kingdom)?.Party; if (custody == null) return;
            prisoner.PartyBelongedToAsPrisoner?.PrisonRoster.RemoveTroop(prisoner.CharacterObject);
            TakePrisonerAction.Apply(custody, prisoner);
            var j = State.Judgments.LastOrDefault(x => x.PrisonerId == prisoner.StringId && x.Stage == RulerJudgmentStage.AwaitingAuthority);
            if (j != null) j.Stage = RulerJudgmentStage.AwaitingDecision;
        }
        private void PrisonerTaken(PartyBase captor, Hero prisoner)
        {
            if (!Enabled(RulerOptions.Judgments) || ApplyingNative || !Sovereign(prisoner) || !(captor?.MapFaction is Kingdom victor) || victor.Leader == null) return;
            if (State.Judgments.Any(j => j.PrisonerId == prisoner.StringId && j.Stage != RulerJudgmentStage.Completed && j.Stage != RulerJudgmentStage.Cancelled)) return;
            var loser = prisoner.Clan.Kingdom;
            if (victor == loser || !victor.IsAtWarWith(loser)) return;
            var war = GetWar(victor, loser, true);
            var jnew = new RulerJudgment { Id = Guid.NewGuid().ToString("N"), PrisonerId = prisoner.StringId, VictorId = victor.Leader.StringId,
                DefeatedKingdomId = loser.StringId, VictorKingdomId = victor.StringId, CapturedDay = Day, WarId = war.Id,
                Stage = captor == PartyBase.MainParty && victor.Leader != Hero.MainHero ? RulerJudgmentStage.AwaitingAuthority : RulerJudgmentStage.AwaitingDecision };
            ReignRebellionCampaignBehavior.Instance?.DescribeRulerJudgment(jnew);
            if (jnew.CivilWar && !Enabled(RulerOptions.CivilWars)) return;
            State.Judgments.Add(jnew);
        }
        private void PrisonerReleased(Hero prisoner, PartyBase captor, IFaction faction, EndCaptivityDetail detail, bool notification)
        {
            if (ApplyingNative) return;
            foreach (var j in State.Judgments.Where(j => j.PrisonerId == prisoner?.StringId).ToList())
                if (ReignRulerPolicy.Cancel(j, "escaped_or_rescued"))
                {
                    if (prisoner?.IsAlive == true && detail != EndCaptivityDetail.Death)
                        ReignNewsCampaignBehavior.Instance?.Judgment(j, "escaped");
                    if (j.DefeatedLord) CreateExile(j, false, true);
                }
        }
        private void HeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool show)
        {
            foreach (var j in State.Judgments.Where(j => j.PrisonerId == victim?.StringId && j.Stage == RulerJudgmentStage.Applying))
            { j.NativeDeathObserved = victim.IsDead; if (victim.IsDead) j.NativeDeathDay = Day; }
            foreach (var j in State.Judgments.Where(j => j.PrisonerId == victim?.StringId && j.Stage != RulerJudgmentStage.Applying)) ReignRulerPolicy.Cancel(j, "prisoner_died_outside_judgment");
        }
        internal static bool BlocksRelease(Hero hero) => Instance?.State.Judgments.Any(j => j.PrisonerId == hero?.StringId && ReignRulerPolicy.BlocksRelease(j)) == true;
        internal bool AwaitsDecision(Hero hero) => hero?.IsPrisoner == true
            && State.Judgments.Any(j => j.PrisonerId == hero.StringId && j.Stage == RulerJudgmentStage.AwaitingDecision);
        internal static bool BlocksPeace(IFaction a, IFaction b) => Instance?.State.Judgments.Any(j => ReignRulerPolicy.BlocksRelease(j)
            && ((j.VictorKingdomId == a?.StringId && j.DefeatedKingdomId == b?.StringId) || (j.VictorKingdomId == b?.StringId && j.DefeatedKingdomId == a?.StringId))) == true;
        internal static bool HoldsCivilWar(string movement) => Instance?.State.Judgments.Any(j => j.MovementId == movement && j.Stage != RulerJudgmentStage.Completed && j.Stage != RulerJudgmentStage.Cancelled) == true;
        internal RulerModifiers Modifiers(Town town) => !Enabled(RulerOptions.Effects) || town?.Settlement?.OwnerClan?.Kingdom == null ? new RulerModifiers()
            : ReignRulerPolicy.Modifiers(State.Effects, town.Settlement.StringId, town.Settlement.OwnerClan.Kingdom.StringId, Day);
        internal JObject RuntimeStatus() => new JObject { ["schema"] = "reign-ruler-runtime-v1",
            ["available"] = ReignBeta.World.ReignRulerNativePatches.HookCount > 0 && ReignBeta.World.ReignRulerNativePatches.Error.Length == 0,
            ["pending"] = _networkBusy || _relationOutbox.Count > 0 || State.Judgments.Any(j => j.Stage == RulerJudgmentStage.Applying && j.Failure.Length == 0),
            ["failures"] = State.Judgments.Count(j => j.Failure.Length > 0), ["pendingRelationships"] = _relationOutbox.Count,
            ["nativeHooks"] = ReignBeta.World.ReignRulerNativePatches.HookCount, ["nativeHookError"] = ReignBeta.World.ReignRulerNativePatches.Error };
        internal JObject Diagnostics() => JObject.FromObject(new { state = State, pendingNetwork = _networkBusy, pendingRelationships = _relationOutbox.Count,
            failures = State.Judgments.Where(j => j.Failure.Length > 0).Select(j => new { j.Id, j.Failure }), optionsConfirmed = ReignServerClient.RulerOptionsConfirmed,
            options = RulerOptions.Switches.Keys.ToDictionary(key => key, key => ReignServerClient.RulerOptions.Preference(key)),
            lifeDeathCycleDisabled = CampaignOptions.IsLifeDeathCycleDisabled, campaignOptions = ObserveCampaignOptions(),
            news = ReignNewsCampaignBehavior.Instance?.Diagnostics() });
    }
}
