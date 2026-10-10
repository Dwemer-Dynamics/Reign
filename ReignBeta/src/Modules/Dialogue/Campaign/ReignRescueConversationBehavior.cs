using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;
using Helpers;
using ReignBeta.Shared;
using SandBox.CampaignBehaviors;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Campaign
{
    public sealed class ReignRescueConversationBehavior : CampaignBehaviorBase
    {
        internal static ReignRescueConversationBehavior Instance { get; private set; }
        private List<JObject> _rescues = new List<JObject>();
        private readonly Dictionary<int, JObject> _joinedBattles = new Dictionary<int, JObject>();
        private List<JObject> _loadedBattles = new List<JObject>();
        private string _saved = "";
        private static ConversationSentence _listen, _return;
        private static int _mainOptionsToken;
        private float _delay;
        internal void ApplyApproachOptions()
        {
            if (!ReignServerClient.ApproachKindEnabled("battle_rescue")) { _joinedBattles.Clear(); _loadedBattles.Clear(); }
            // Keep native release evidence intact; hiding this optional line must
            // never execute or suppress the ordinary native release consequences.
            foreach (JObject rescue in _rescues)
                if (!ReignServerClient.ApproachKindEnabled(rescue.Value<string>("kind")) && !rescue.Value<bool>("listened")) rescue["dismissed"] = true;
        }
        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSession);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnBattleEnded);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }
        public override void SyncData(IDataStore store)
        {
            if (store.IsSaving) _saved = JsonConvert.SerializeObject(new JObject { ["rescues"] = new JArray(_rescues),
                ["battles"] = new JArray(_joinedBattles.Values.Concat(_loadedBattles)) });
            store.SyncData("ReignRescueConversationsV1", ref _saved);
            if (store.IsLoading)
            {
                try { var saved = JObject.Parse(string.IsNullOrWhiteSpace(_saved) ? "{}" : _saved);
                    _rescues = (saved["rescues"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                    _loadedBattles = (saved["battles"] as JArray ?? new JArray()).OfType<JObject>().ToList(); }
                catch { _rescues.Clear(); _loadedBattles.Clear(); }
                // Rebind only frozen pre-join evidence to its exact restored battle.
                // A later strength snapshot can never recreate the prediction.
                _joinedBattles.Clear();
            }
        }
        internal static void Install()
        {
            try
            {
                var harmony = new Harmony("com.bannerlordreign.rescue-conversations");
                var methods = typeof(MapEventSide).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Where(m => m.Name == "AddPartyInternal" && m.GetParameters().Any(p => p.ParameterType == typeof(PartyBase))).ToList();
                if (methods.Count == 0) throw new MissingMethodException("MapEventSide.AddPartyInternal(PartyBase) is unavailable.");
                foreach (var method in methods) harmony.Patch(method, prefix: new HarmonyMethod(typeof(ReignRescueConversationBehavior), nameof(BeforePartyJoin)));
            }
            catch (Exception ex) { ReignLog.Warn("Battle rescue join hook unavailable: " + ex.Message); }
            try
            {
                var harmony = new Harmony("com.bannerlordreign.prison-rescue-conversations");
                var owner = typeof(PrisonBreakCampaignBehavior);
                var success = AccessTools.Method(owner, "settlement_prison_break_success_continue_on_consequence");
                var release = AccessTools.Method(owner, "prison_break_end_with_success_on_consequence");
                var companion = AccessTools.Method(owner, "prison_break_end_with_success_companion");
                if (success == null || release == null || companion == null || AccessTools.Field(owner, "_prisonerHero") == null)
                    throw new MissingMethodException("Native successful prison-break continuation is unavailable.");
                harmony.Patch(success, prefix: new HarmonyMethod(typeof(ReignRescueConversationBehavior), nameof(BeforePrisonSuccessConversation)));
                foreach (var method in new[] { release, companion }) harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(ReignRescueConversationBehavior), nameof(BeforeNativePrisonRelease)),
                    postfix: new HarmonyMethod(typeof(ReignRescueConversationBehavior), nameof(AfterNativePrisonRelease)));
            }
            catch (Exception ex) { ReignLog.Warn("Prison rescue conversation hook unavailable: " + ex.Message); }
        }
        private static Hero NativePrisoner(object owner) => owner == null ? null : AccessTools.Field(typeof(PrisonBreakCampaignBehavior), "_prisonerHero")?.GetValue(owner) as Hero;
        private static void BeforePrisonSuccessConversation(object __instance)
        {
            if (!ReignServerClient.ApproachKindEnabled("prison_rescue")) return;
            Hero hero = NativePrisoner(__instance);
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || !hero.IsPrisoner || Hero.MainHero == null) return;
            var settlement = hero.PartyBelongedToAsPrisoner?.Settlement ?? Hero.MainHero.CurrentSettlement;
            var rescue = new JObject { ["eventId"] = "prison_rescue_" + Guid.NewGuid().ToString("N"), ["kind"] = "prison_rescue",
                ["heroId"] = hero.StringId, ["playerId"] = Hero.MainHero.StringId, ["completedDay"] = CampaignTime.Now.ToDays,
                ["settlementId"] = settlement?.StringId ?? "", ["nativeReleasePending"] = true,
                ["description"] = "The player brought you out of the prison at " + (settlement?.Name?.ToString() ?? "the settlement") + " during a successful native prison break." };
            Instance?.AddRescue(rescue);
            // The native continuation opens this very conversation. Do not open a second window.
            rescue["opened"] = true;
        }
        private static void BeforeNativePrisonRelease(object __instance, out Hero __state) => __state = NativePrisoner(__instance);
        private static void AfterNativePrisonRelease(Hero __state)
        {
            JObject rescue = Instance?.Pending(__state);
            if (__state == null || __state.IsPrisoner || rescue?.Value<string>("kind") != "prison_rescue") return;
            rescue["nativeReleasePending"] = false;
            rescue["completedDay"] = CampaignTime.Now.ToDays;
        }
        private static void BeforePartyJoin(MapEventSide __instance, object[] __args)
        {
            var party = __args.OfType<PartyBase>().FirstOrDefault();
            if (party == PartyBase.MainParty) Instance?.CaptureJoin(__instance.MapEvent, __instance.MissionSide);
        }
        private static double FightingPower(MapEventSide side) => side == null ? 0 : side.Parties
            .Where(p => p.Party != PartyBase.MainParty).Sum(p => p.Party.MemberRoster.GetTroopRoster()
                .Sum(t => PlayerApproachPolicy.FightingUnitPower(t.Number - t.WoundedNumber, t.Character?.Tier ?? 0)));
        private void CaptureJoin(MapEvent battle, BattleSideEnum side)
        {
            if (!ReignServerClient.ApproachKindEnabled("battle_rescue")) return;
            if (Hero.MainHero == null || battle == null || !battle.IsFieldBattle || _joinedBattles.ContainsKey(battle.GetHashCode())) return;
            MapEventSide friends = side == BattleSideEnum.Attacker ? battle.AttackerSide : battle.DefenderSide;
            MapEventSide enemies = side == BattleSideEnum.Attacker ? battle.DefenderSide : battle.AttackerSide;
            double own = FightingPower(friends), hostile = FightingPower(enemies);
            if (!PlayerApproachPolicy.OutmatchedBeforeJoining(own, hostile) || friends.Parties.Any(p => p.Party == PartyBase.MainParty)) return;
            var beneficiaries = friends.Parties.Select(p => p.Party.LeaderHero).Where(h => h != null && h != Hero.MainHero && h.IsAlive)
                .Select(h => h.StringId).Distinct().ToList();
            if (beneficiaries.Count == 0) return;
            _joinedBattles[battle.GetHashCode()] = new JObject
            {
                ["eventId"] = "battle_rescue_" + Guid.NewGuid().ToString("N"), ["kind"] = "battle_rescue", ["beneficiaries"] = new JArray(beneficiaries),
                ["side"] = (int)side, ["friendlyPowerBeforePlayer"] = own, ["enemyPowerBeforePlayer"] = hostile,
                ["estimatedFriendlyShare"] = own / (own + hostile), ["playerId"] = Hero.MainHero.StringId,
                ["joinedDay"] = CampaignTime.Now.ToDays, ["description"] = "The player joined your existing battle when your side was outmatched."
            };
            JObject captured = _joinedBattles[battle.GetHashCode()];
            captured["battleStartedDay"] = battle.BattleStartTime.ToDays;
            captured["friendlyParties"] = new JArray(friends.Parties.Select(p => p.Party.Id.ToString()).OrderBy(x => x, StringComparer.Ordinal));
            captured["enemyParties"] = new JArray(enemies.Parties.Select(p => p.Party.Id.ToString()).OrderBy(x => x, StringComparer.Ordinal));
        }
        private void RebindLoadedBattle()
        {
            if (_loadedBattles.Count == 0 || ReignCampaignInitializationGate.IsPending) return;
            MapEvent battle = MobileParty.MainParty?.MapEvent;
            if (battle != null) foreach (JObject evidence in _loadedBattles)
            {
                var side = (BattleSideEnum)evidence.Value<int>("side");
                MapEventSide friends = side == BattleSideEnum.Attacker ? battle.AttackerSide : battle.DefenderSide;
                MapEventSide enemies = side == BattleSideEnum.Attacker ? battle.DefenderSide : battle.AttackerSide;
                if (evidence.Value<string>("playerId") != Hero.MainHero?.StringId || evidence["battleStartedDay"] == null
                    || Math.Abs(evidence.Value<double>("battleStartedDay") - battle.BattleStartTime.ToDays) > 1e-8
                    || !friends.Parties.Any(p => p.Party == PartyBase.MainParty)) continue;
                var actualFriends = friends.Parties.Where(p => p.Party != PartyBase.MainParty).Select(p => p.Party.Id.ToString()).OrderBy(x => x, StringComparer.Ordinal);
                var actualEnemies = enemies.Parties.Select(p => p.Party.Id.ToString()).OrderBy(x => x, StringComparer.Ordinal);
                if (actualFriends.SequenceEqual(evidence["friendlyParties"].Values<string>()) && actualEnemies.SequenceEqual(evidence["enemyParties"].Values<string>()))
                    _joinedBattles[battle.GetHashCode()] = evidence;
            }
            _loadedBattles.Clear();
        }
        private void OnBattleEnded(MapEvent battle)
        {
            if (battle == null || !_joinedBattles.TryGetValue(battle.GetHashCode(), out JObject evidence)) return;
            _joinedBattles.Remove(battle.GetHashCode());
            if (!ReignServerClient.ApproachKindEnabled("battle_rescue")) return;
            var player = battle.Winner?.Parties.FirstOrDefault(p => p.Party == PartyBase.MainParty);
            if ((int)battle.WinningSide != evidence.Value<int>("side") || battle.EndedByRetreat || player == null || player.ContributionToBattle <= 0) return;
            foreach (string id in evidence["beneficiaries"].Values<string>())
            {
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);
                if (hero == null || hero.IsPrisoner || !hero.IsAlive) continue;
                var rescued = (JObject)evidence.DeepClone(); rescued["heroId"] = id; rescued["completedDay"] = CampaignTime.Now.ToDays;
                rescued["description"] = "The player joined your existing battle while your side was outmatched, contributed to the fighting, and helped your side win. This was a prediction from strength at arrival, not proof that defeat was inevitable or that the player won alone.";
                AddRescue(rescued);
            }
        }
        private void AddRescue(JObject rescue)
        {
            if (!ReignServerClient.ApproachKindEnabled(rescue.Value<string>("kind"))) return;
            if (_rescues.Any(r => r.Value<string>("heroId") == rescue.Value<string>("heroId") && r.Value<string>("eventId") == rescue.Value<string>("eventId"))) return;
            rescue["opened"] = false; rescue["listened"] = false; _rescues.Add(rescue); _delay = 1;
        }
        private void OnSession(CampaignGameStarter starter)
        {
            _listen = starter.AddPlayerLine("reign_listen_to_rescued_hero", "hero_main_options", "reign_rescue_listen_done", "Listen to them.", CanListen, Listen, 111);
            _mainOptionsToken = _listen.InputToken;
            _return = starter.AddDialogLine("reign_rescue_listen_return", "reign_rescue_listen_done", "hero_main_options", "{=*}...", null, null);
        }
        private static Hero ConversationHero => CharacterObject.OneToOneConversationCharacter?.HeroObject;
        private static bool CanListen()
        {
            JObject rescue = Instance?.Pending(ConversationHero);
            if (rescue == null || ReignCampaignInitializationGate.IsPending || Settings.ReignBetaSettings.Instance?.UseLocalServer == false) return false;
            return !rescue.Value<bool>("nativeReleasePending") || NativePrisoner(NativeCampaign.Current?.GetCampaignBehavior<PrisonBreakCampaignBehavior>()) == ConversationHero;
        }
        private JObject Pending(Hero hero) => hero == null ? null : _rescues.LastOrDefault(r => r.Value<string>("heroId") == hero.StringId
            && r.Value<string>("playerId") == Hero.MainHero?.StringId && !r.Value<bool>("listened") && !r.Value<bool>("dismissed")
            && ReignServerClient.ApproachKindEnabled(r.Value<string>("kind")));
        internal static void PrepareListenOption(int token)
        {
            if (_listen == null || !CanListen()) return;
            AccessTools.Property(typeof(ConversationSentence), "InputToken").SetValue(_listen, token);
            AccessTools.Property(typeof(ConversationSentence), "OutputToken").SetValue(_return, token);
        }
        private static void Listen()
        {
            Hero hero = ConversationHero; JObject rescue = Instance?.Pending(hero);
            if (rescue == null) return;
            if (rescue.Value<bool>("nativeReleasePending"))
            {
                var owner = NativeCampaign.Current?.GetCampaignBehavior<PrisonBreakCampaignBehavior>();
                if (owner == null || NativePrisoner(owner) != hero) return;
                try
                {
                    string consequence = hero.CompanionOf == Clan.PlayerClan ? "prison_break_end_with_success_companion" : "prison_break_end_with_success_on_consequence";
                    AccessTools.Method(typeof(PrisonBreakCampaignBehavior), consequence).Invoke(owner, null);
                    if (hero.IsPrisoner || rescue.Value<bool>("nativeReleasePending")) return;
                    // The native release invalidates its old success tokens. Return to ordinary options.
                    AccessTools.Property(typeof(ConversationSentence), "OutputToken").SetValue(_return, _mainOptionsToken);
                }
                catch (Exception ex) { ReignLog.Warn("Native prison rescue completion failed: " + ex.Message); return; }
            }
            ReignIndividualChatScreenManager.OpenForHero(hero, new JObject
            {
                ["approachOpening"] = true, ["rescueOpening"] = true, ["rescueContext"] = rescue.DeepClone(),
                ["sceneContextDirective"] = "The NPC asked to speak following a verified rescue. The player chose to listen and has not said anything. The NPC must speak first about the supplied rescue."
            }, null);
        }
        internal void HeardRescue(string eventId, Hero hero)
        {
            JObject rescue = _rescues.FirstOrDefault(r => r.Value<string>("eventId") == eventId && r.Value<string>("heroId") == hero?.StringId);
            if (rescue != null) rescue["listened"] = true;
        }
        private void OnTick(float dt)
        {
            RebindLoadedBattle();
            _delay = Math.Max(0, _delay - dt);
            if (ReignCampaignInitializationGate.IsPending || Settings.ReignBetaSettings.Instance?.UseLocalServer == false
                || _delay > 0 || Hero.MainHero == null || Hero.MainHero.IsPrisoner || Mission.Current != null || PartyBase.MainParty?.MapEvent != null
                || ReignIndividualChatScreenManager.IsOpen || ReignPartyChatScreenManager.IsOpenOrPending || ReignPlayerApproachPopup.IsOpen) return;
            var next = _rescues.FirstOrDefault(r => r.Value<string>("playerId") == Hero.MainHero.StringId
                && !r.Value<bool>("opened") && !r.Value<bool>("listened") && !r.Value<bool>("dismissed")
                && ReignServerClient.ApproachKindEnabled(r.Value<string>("kind")));
            if (next == null) return;
            string screen = ScreenManager.TopScreen?.GetType().Name ?? "";
            if (screen.IndexOf("MapScreen", StringComparison.OrdinalIgnoreCase) < 0 || PlayerEncounter.Current != null) return;
            Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == next.Value<string>("heroId"));
            if (hero == null || hero.IsPrisoner || !hero.IsAlive) { next["opened"] = true; return; }
            if (!ReignServerClient.BuildPlayerAttentionPresence(hero).OfType<JObject>().Any(r => r.Value<string>("heroId") == hero.StringId)) return;
            next["opened"] = true;
            try
            {
                var player = new ConversationCharacterData(CharacterObject.PlayerCharacter, PartyBase.MainParty, true, true, false, true, true, true);
                var partner = new ConversationCharacterData(hero.CharacterObject, hero.PartyBelongedTo?.Party, true, true, false, true, true, true);
                CampaignMapConversation.OpenConversation(player, partner);
            }
            catch (Exception ex) { next["opened"] = false; _delay = 10; ReignLog.Warn("Rescue native conversation failed: " + ex.Message); }
        }
    }
}
