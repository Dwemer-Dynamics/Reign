using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.UI;
using ReignBeta.World;
using ReignBeta.Shared;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Campaign
{
    public sealed class ReignPlayerApproachCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignPlayerApproachCampaignBehavior Instance { get; private set; }
        private List<JObject> _requests = new List<JObject>();
        private string _journal = "", _keepVisit = "";
        private JObject _pursuit;
        private bool _evaluating, _deciding;
        private float _retryDelay;
        private CampaignTimeControlMode _priorTime;
        private int _epoch;
        private List<string> _passedContacts = new List<string>();
        private JObject _shownRequest;
        private ReignPlayerApproachPopup _popup;
        private static bool Enabled(JObject request) => ReignServerClient.ApproachKindEnabled(request?.Value<string>("kind"));
        internal void ApplyApproachOptions()
        {
            if (!ReignServerClient.ApproachKindEnabled("patrol"))
            { CancelPursuit(true); _passedContacts.Clear(); }
            foreach (JObject request in _requests.Where(r => !Enabled(r)).ToList())
                if (string.IsNullOrWhiteSpace(request.Value<string>("pendingChoice"))) QueueCancel(request);
            if (_shownRequest != null && !Enabled(_shownRequest))
            {
                _popup?.CancelIfTop();
                if (!ReignPlayerApproachPopup.IsOpen) { ReleaseTime(_shownRequest); _shownRequest = null; _popup = null; }
            }
            // Drain cancellation even when native time is paused or another screen is up.
            JObject cancelled = _requests.FirstOrDefault(r => r.Value<string>("pendingChoice") == "cancel");
            if (!_deciding && cancelled != null && Ready()) _ = ResolveDecisionAsync(cancelled, "cancel");
        }
        private bool CurrentOwner(NativeCampaign owner, int epoch) => ReferenceEquals(owner, NativeCampaign.Current) && Instance == this && epoch == _epoch;
        private readonly Dictionary<string, PatrolAnchor> _scanAnchors = new Dictionary<string, PatrolAnchor>();
        private sealed class PatrolAnchor
        {
            internal float X, Y;
            internal string SettlementId = "", OrderId = "";
            internal int Revision;
        }
        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourly);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }
        public override void SyncData(IDataStore store)
        {
            if (store.IsSaving) _journal = JsonConvert.SerializeObject(new JObject
                { ["requests"] = new JArray(_requests), ["pursuit"] = _pursuit, ["priorTime"] = (int)_priorTime, ["passedContacts"] = new JArray(_passedContacts) });
            store.SyncData("ReignPlayerApproachesV1", ref _journal);
            if (store.IsLoading)
            {
                _epoch++;
                try
                {
                    var saved = string.IsNullOrWhiteSpace(_journal) ? new JObject() : JObject.Parse(_journal);
                    _requests = (saved["requests"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                    _pursuit = saved["pursuit"] as JObject;
                    _priorTime = (CampaignTimeControlMode)(saved.Value<int?>("priorTime") ?? 0);
                    _passedContacts = (saved["passedContacts"] as JArray ?? new JArray()).Values<string>().ToList();
                }
                catch { _requests.Clear(); _pursuit = null; }
                _evaluating = _deciding = false; _keepVisit = "";
            }
        }
        private void OnHourly()
        {
            if (_evaluating || !Ready() || (!ReignServerClient.ApproachKindEnabled("shared") && !ReignServerClient.ApproachKindEnabled("patrol"))) return;
            _ = EvaluateSharedAndPatrolAsync();
        }
        private static bool Ready() => !ReignCampaignInitializationGate.IsPending
            && NativeCampaign.Current != null && MobileParty.MainParty != null
            && Settings.ReignBetaSettings.Instance?.UseLocalServer != false
            && Hero.MainHero?.IsAlive == true && !Hero.MainHero.IsPrisoner && MobileParty.MainParty?.MapEvent == null;

        private async Task EvaluateSharedAndPatrolAsync()
        {
            NativeCampaign owner = NativeCampaign.Current; int epoch = _epoch;
            _evaluating = true;
            try
            {
                var presence = new HashSet<string>(ReignServerClient.BuildPlayerAttentionPresence().OfType<JObject>().Select(x => x.Value<string>("heroId")));
                var shared = new JArray(Hero.AllAliveHeroes.Where(h => h != Hero.MainHero && presence.Contains(h.StringId))
                    .Select(h => ReignServerClient.BuildApproachCandidate(h)));
                var patrols = CapturePatrolCandidates();
                int healthy = MobileParty.MainParty.MemberRoster.GetTroopRoster().Sum(t => Math.Max(0, t.Number - t.WoundedNumber));
                JObject response = ReignServerClient.ApproachKindEnabled("shared")
                    ? await ReignServerClient.EvaluateApproachesAsync("shared", shared).ConfigureAwait(false) : new JObject();
                await ReignMainThread.InvokeAsync(() => { if (CurrentOwner(owner, epoch)) Enqueue(response); }).ConfigureAwait(false);
                if (patrols.Count > 0)
                {
                    JObject patrol = await ReignMainThread.InvokeAsync(() => CurrentOwner(owner, epoch)
                        ? ReignServerClient.EvaluateApproachesAsync("patrol", patrols, healthy) : Task.FromResult(new JObject())).Unwrap().ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() => { if (CurrentOwner(owner, epoch)) BeginPatrol(patrol); }).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { ReignLog.Warn("Approach evaluation failed: " + ex.Message); }
            finally { if (CurrentOwner(owner, epoch)) _evaluating = false; }
        }
        private JArray CapturePatrolCandidates()
        {
            _scanAnchors.Clear(); var rows = new JArray();
            if (!ReignServerClient.ApproachKindEnabled("patrol") || _pursuit != null || MobileParty.MainParty.CurrentSettlement != null) return rows;
            foreach (MobileParty party in MobileParty.All.Where(p => p != MobileParty.MainParty && p.LeaderHero?.IsLord == true))
            {
                if (!EligibleParty(party)) continue;
                var order = ReignCampaignCommandBehavior.Instance?.ActiveOrderFor(party.LeaderHero.StringId);
                bool managedPatrol = order?.Objective == "patrol" && !order.AwaitingGuidance;
                if (!managedPatrol && (order != null || party.DefaultBehavior != AiBehavior.PatrolAroundPoint)) continue;
                CampaignVec2 center = party.TargetSettlement?.Position ?? party.TargetPosition;
                if (managedPatrol)
                {
                    JObject terms = JObject.Parse(order.TermsJson ?? "{}");
                    // Point orders use the command owner's exact bounded objective;
                    // native waypoint state is used only when no explicit point exists.
                    if (terms.Value<float?>("positionX") is float x && terms.Value<float?>("positionY") is float y)
                        center = new CampaignVec2(new TaleWorlds.Library.Vec2(x, y), party.Position.IsOnLand);
                    else if (!string.IsNullOrWhiteSpace(order.TargetSettlementStringId))
                        center = Settlement.Find(order.TargetSettlementStringId)?.Position ?? center;
                }
                if (party.Position.Distance(MobileParty.MainParty.Position) > PlayerApproachPolicy.PatrolAreaRadius
                    || center.Distance(MobileParty.MainParty.Position) > PlayerApproachPolicy.PatrolAreaRadius) continue;
                var row = ReignServerClient.BuildApproachCandidate(party.LeaderHero);
                row["patrolling"] = true; row["hostile"] = false; row["protected"] = false;
                row["distance"] = party.Position.Distance(MobileParty.MainParty.Position);
                rows.Add(row);
                _scanAnchors[party.StringId] = new PatrolAnchor { X = center.ToVec2().X, Y = center.ToVec2().Y,
                    SettlementId = party.TargetSettlement?.StringId ?? "", OrderId = order?.OrderId ?? "", Revision = order?.Revision ?? 0 };
            }
            return rows;
        }
        private static bool EligibleParty(MobileParty party) => MobileParty.MainParty != null && party != null && party.IsActive && party.LeaderHero?.IsAlive == true
            && !party.LeaderHero.IsPrisoner && party.Army == null && party.MapEvent == null && party.SiegeEvent == null
            && party.CurrentSettlement == null && !FactionManager.IsAtWarAgainstFaction(party.MapFaction, MobileParty.MainParty.MapFaction)
            && PartyAgency.ReignTemporaryPartyGuestCampaignBehavior.Instance?.IsProtectedCamp(party) != true;

        private void BeginPatrol(JObject response)
        {
            JObject request = (response?["requests"] as JArray)?.OfType<JObject>().FirstOrDefault();
            if (request == null || _pursuit != null) return;
            if (!Enabled(request)) { QueueCancel(request); return; }
            string partyId = request["heroes"]?[0]?.Value<string>("partyId") ?? "";
            MobileParty party = MobileParty.All.FirstOrDefault(p => p.StringId == partyId);
            if (!EligibleParty(party) || !_scanAnchors.TryGetValue(partyId, out PatrolAnchor anchor))
            { QueueCancel(request); return; }
            var currentOrder = ReignCampaignCommandBehavior.Instance?.ActiveOrderFor(party.LeaderHero.StringId);
            var currentCenter = new CampaignVec2(new TaleWorlds.Library.Vec2(anchor.X, anchor.Y), party.Position.IsOnLand);
            if ((currentOrder?.OrderId ?? "") != anchor.OrderId || (currentOrder?.Revision ?? 0) != anchor.Revision
                || (currentOrder == null && party.DefaultBehavior != AiBehavior.PatrolAroundPoint)
                || (currentOrder != null && (currentOrder.Objective != "patrol" || currentOrder.AwaitingGuidance))
                || currentCenter.Distance(MobileParty.MainParty.Position) > PlayerApproachPolicy.PatrolAreaRadius
                || party.Position.Distance(MobileParty.MainParty.Position) > PlayerApproachPolicy.PatrolAreaRadius)
            { QueueCancel(request); return; }
            request["anchorX"] = anchor.X; request["anchorY"] = anchor.Y; request["anchorSettlementId"] = anchor.SettlementId;
            request["orderId"] = anchor.OrderId; request["orderRevision"] = anchor.Revision; request["contact"] = false;
            _pursuit = request;
            SetPartyAiAction.GetActionForEscortingParty(party, MobileParty.MainParty,
                party.NavigationCapability, party.IsCurrentlyAtSea, MobileParty.MainParty.IsCurrentlyAtSea);
        }
        internal bool YieldPatrolOrder(MobileParty party, string orderId, int revision, string objective)
        {
            if (_pursuit == null || PursuitParty() != party) return false;
            if (_pursuit.Value<string>("orderId") == orderId && _pursuit.Value<int>("orderRevision") == revision && objective == "patrol") return true;
            CancelPursuit(false); return false;
        }
        private MobileParty PursuitParty() => MobileParty.All.FirstOrDefault(p => p.StringId == _pursuit?["heroes"]?[0]?.Value<string>("partyId"));
        private void CancelPursuit(bool restore)
        {
            if (_pursuit == null) return;
            JObject request = _pursuit;
            if (restore) RestorePatrol();
            _pursuit = null;
            QueueCancel(request);
        }
        private void QueueCancel(JObject request)
        {
            if (string.IsNullOrWhiteSpace(request.Value<string>("pendingChoice"))) request["pendingChoice"] = "cancel";
            if (!_requests.Any(r => r.Value<string>("requestId") == request.Value<string>("requestId"))) _requests.Add(request);
        }
        private void RestorePatrol()
        {
            if (_pursuit == null) return;
            MobileParty party = PursuitParty();
            if (!EligibleParty(party) || party.DefaultBehavior != AiBehavior.EscortParty || party.TargetParty != MobileParty.MainParty) return;
            var order = ReignCampaignCommandBehavior.Instance?.ActiveOrderFor(party.LeaderHero.StringId);
            if ((order?.OrderId ?? "") != _pursuit.Value<string>("orderId") || (order?.Revision ?? 0) != _pursuit.Value<int>("orderRevision")) return;
            Settlement settlement = Settlement.Find(_pursuit.Value<string>("anchorSettlementId") ?? "");
            if (settlement != null) SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, settlement, party.NavigationCapability, party.IsCurrentlyAtSea, settlement.HasPort);
            else party.SetMovePatrolAroundPoint(new CampaignVec2(new TaleWorlds.Library.Vec2(_pursuit.Value<float>("anchorX"), _pursuit.Value<float>("anchorY")), party.Position.IsOnLand), party.NavigationCapability);
        }
        internal bool ConsumeFriendlyEncounter(PartyBase a, PartyBase b)
        {
            if (a == null || b == null) return false;
            if (!ReignServerClient.ApproachKindEnabled("patrol")) { CancelPursuit(true); return false; }
            // Grace applies only to a noble initiating another collision before
            // departing. A deliberate player encounter keeps its normal route.
            if (b == PartyBase.MainParty && a.MobileParty != null && _passedContacts.Contains(a.MobileParty.StringId)
                && EligibleParty(a.MobileParty) && a.MobileParty.Position.Distance(MobileParty.MainParty.Position) <= PlayerApproachPolicy.PatrolContactRadius) return true;
            if (_pursuit == null) return false;
            MobileParty party = PursuitParty();
            if (!EligibleParty(party) || party.DefaultBehavior != AiBehavior.EscortParty || party.TargetParty != MobileParty.MainParty
                || !((a == party.Party && b == PartyBase.MainParty) || (b == party.Party && a == PartyBase.MainParty))) return false;
            if (party.Position.Distance(MobileParty.MainParty.Position) > PlayerApproachPolicy.PatrolContactRadius) return false;
            ReachPatrolContact(); return true;
        }
        private void ReachPatrolContact()
        {
            if (_pursuit == null || _pursuit.Value<bool>("contact")) return;
            _pursuit["contact"] = true; _requests.Add(_pursuit);
        }
        private void Enqueue(JObject response)
        {
            if (response?.Value<bool?>("ok") != true) return;
            foreach (JObject request in (response["requests"] as JArray ?? new JArray()).OfType<JObject>())
                if (!Enabled(request)) QueueCancel(request);
                else if (!_requests.Any(r => r.Value<string>("requestId") == request.Value<string>("requestId"))) _requests.Add(request);
        }
        private void OnTick(float dt)
        {
            if (!Ready()) { if (_pursuit != null) CancelPursuit(true); return; }
            _passedContacts.RemoveAll(id => { var party = MobileParty.All.FirstOrDefault(p => p.StringId == id);
                return !EligibleParty(party) || party.Position.Distance(MobileParty.MainParty.Position) > PlayerApproachPolicy.PatrolContactRadius * 2; });
            if (_pursuit != null && !_pursuit.Value<bool>("contact"))
            {
                MobileParty party = PursuitParty();
                CampaignVec2 center = new CampaignVec2(new TaleWorlds.Library.Vec2(_pursuit.Value<float>("anchorX"), _pursuit.Value<float>("anchorY")), party?.Position.IsOnLand ?? true);
                if (!EligibleParty(party) || party.DefaultBehavior != AiBehavior.EscortParty || party.TargetParty != MobileParty.MainParty
                    || CampaignTime.Now.ToDays - _pursuit.Value<double>("worldDay") > PlayerApproachPolicy.PatrolPursuitDays
                    || center.Distance(MobileParty.MainParty.Position) > PlayerApproachPolicy.PatrolAreaRadius)
                    CancelPursuit(true);
                else if (party.Position.Distance(MobileParty.MainParty.Position) <= PlayerApproachPolicy.PatrolContactRadius) ReachPatrolContact();
            }
            _retryDelay = Math.Max(0, _retryDelay - dt);
            if (!ReignPlayerApproachPopup.CanPresentOnMap()) return;
            if (_deciding || ReignPlayerApproachPopup.IsOpen || ReignIndividualChatScreenManager.IsOpen || ReignPartyChatScreenManager.IsOpenOrPending
                || Mission.Current != null || _retryDelay > 0) return;
            string menu = TaleWorlds.CampaignSystem.Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId ?? "";
            // Native castles use the castle menu as their keep landing page.
            string keep = (menu == "town_keep" || menu == "castle_keep"
                || (menu == "castle" && Settlement.CurrentSettlement?.IsCastle == true)) ? (Settlement.CurrentSettlement?.StringId ?? "") : "";
            if (ReignServerClient.ApproachKindEnabled("keep") && keep.Length > 0 && keep != _keepVisit && !_evaluating)
            {
                _keepVisit = keep;
                var nobles = Runtime.ReignPartyChatSession.GetAvailableConversationHeroes().Where(EligibleKeepHero).ToList();
                _ = EvaluateKeepAsync(new JArray(nobles.Select(h => ReignServerClient.BuildApproachCandidate(h))));
            }
            else if (keep.Length == 0 && menu != "") _keepVisit = "";
            if (_requests.Count == 0) return;
            JObject first = _requests[0];
            string choice = first.Value<string>("pendingChoice");
            if (!string.IsNullOrWhiteSpace(choice)) { _ = ResolveDecisionAsync(first, choice); return; }
            if (!Enabled(first) || !StillPresent(first)) { QueueCancel(first); return; }
            ShowRequest(first);
        }
        private async Task EvaluateKeepAsync(JArray nobles)
        {
            NativeCampaign owner = NativeCampaign.Current; int epoch = _epoch;
            _evaluating = true;
            try { var response = await ReignServerClient.EvaluateApproachesAsync("keep", nobles).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() => { if (CurrentOwner(owner, epoch)) Enqueue(response); }).ConfigureAwait(false); }
            catch (Exception ex) { if (CurrentOwner(owner, epoch)) { _keepVisit = ""; _retryDelay = 10; ReignLog.Warn("Keep approach failed: " + ex.Message); } }
            finally { if (CurrentOwner(owner, epoch)) _evaluating = false; }
        }
        private static bool EligibleKeepHero(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || !hero.IsActive || hero.IsPrisoner
                || hero.IsChild || hero.Age < 18 || Settlement.CurrentSettlement == null || MobileParty.MainParty == null) return false;
            // Temporary noble guests can still be lords. Check both native party
            // ownership and the actual roster rather than treating lord status as external presence.
            return PlayerApproachPolicy.EligibleKeepNoble(hero.IsLord, hero.CurrentSettlement == Settlement.CurrentSettlement,
                hero.PartyBelongedTo == MobileParty.MainParty, MobileParty.MainParty.MemberRoster.Contains(hero.CharacterObject));
        }
        private bool StillPresent(JObject request)
        {
            var presence = new HashSet<string>(ReignServerClient.BuildPlayerAttentionPresence().OfType<JObject>().Select(p => p.Value<string>("heroId")));
            return (request["heroes"] as JArray ?? new JArray()).OfType<JObject>().All(r =>
            {
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == r.Value<string>("heroId"));
                if (hero == null || !hero.IsAlive || hero.IsPrisoner) return false;
                if (request.Value<string>("kind") == "keep") return EligibleKeepHero(hero);
                return request.Value<string>("kind") == "patrol" ? EligibleParty(hero.PartyBelongedTo)
                    && hero.PartyBelongedTo.Position.Distance(MobileParty.MainParty.Position) <= PlayerApproachPolicy.PatrolContactRadius : presence.Contains(hero.StringId);
            });
        }
        private void ShowRequest(JObject request)
        {
            // Recheck immediately before pausing time or replacing native focus.
            // An unready map leaves the invitation queued without a decision.
            ReignPlayerApproachPopup.TryPresentOnMap(() => ShowReadyRequest(request));
        }
        private void ShowReadyRequest(JObject request)
        {
            if (!Enabled(request) || !StillPresent(request)) { QueueCancel(request); return; }
            string kind = request.Value<string>("kind"), names = string.Join(", ", (request["heroes"] as JArray).OfType<JObject>().Select(h => h.Value<string>("name")));
            string detail = kind == "shared" ? names + " is asking to speak with you." : kind == "patrol" ? names + "'s party is approaching you." : names + " wish to speak with you.";
            NativeCampaign owner = NativeCampaign.Current; int epoch = _epoch;
            if (request.Value<bool?>("timeHeld") != true) { _priorTime = owner.TimeControlMode; request["timeHeld"] = true; }
            TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            var popup = new ReignPlayerApproachPopup(kind == "patrol" ? "A NOBLE'S PARTY" : "A REQUEST TO SPEAK", detail,
                kind == "shared" ? "Talk to them" : kind == "keep" ? "See what they want" : "Stop and see what they want",
                kind == "shared" ? "Tell them another time" : kind == "keep" ? "Avoid them" : "Nod and pass", accept =>
                { if (!CurrentOwner(owner, epoch)) return; string choice = Enabled(request) ? (accept ? "accept" : "refuse") : "cancel";
                    request["pendingChoice"] = choice; _ = ResolveDecisionAsync(request, choice); },
                () => { if (CurrentOwner(owner, epoch)) { QueueCancel(request); ReleaseTime(request); } });
            _shownRequest = request; _popup = popup;
            ScreenManager.PushScreen(popup);
            if (!popup.Initialized) { ScreenManager.PopScreen(); _retryDelay = 10; ReleaseTime(request); }
        }
        private void ReleaseTime(JObject request)
        {
            if (request.Value<bool?>("timeHeld") != true) return;
            if (NativeCampaign.Current != null && !NativeCampaign.Current.TimeControlModeLock) NativeCampaign.Current.TimeControlMode = _priorTime;
            request["timeHeld"] = false;
        }
        private async Task ResolveDecisionAsync(JObject request, string choice)
        {
            if (_deciding) return; _deciding = true;
            if (request.Value<string>("kind") == "keep" && !StillPresent(request)) choice = "cancel";
            NativeCampaign owner = NativeCampaign.Current; int epoch = _epoch;
            try
            {
                JObject response = await ReignServerClient.DecideApproachAsync(request.Value<string>("requestId"), choice).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true) throw new InvalidOperationException(response.Value<string>("error") ?? "Approach decision unavailable.");
                JArray observations = await ReignMainThread.InvokeAsync(() => CurrentOwner(owner, epoch)
                    ? ReignServerClient.ApplyApproachReceipts(response) : throw new InvalidOperationException("The approach campaign changed.")).ConfigureAwait(false);
                if (observations.Count > 0)
                {
                    JObject acknowledged = await ReignMainThread.InvokeAsync(() => CurrentOwner(owner, epoch)
                        ? ReignServerClient.AcknowledgeApproachAsync(request.Value<string>("requestId"), observations)
                        : Task.FromResult(new JObject())).Unwrap().ConfigureAwait(false);
                    if (acknowledged.Value<bool?>("ok") != true) throw new InvalidOperationException("Native approach observation is awaiting acknowledgment.");
                }
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!CurrentOwner(owner, epoch)) return;
                    if (request.Value<string>("kind") == "patrol")
                    {
                        string partyId = request["heroes"]?[0]?.Value<string>("partyId") ?? "";
                        if (response["request"]?.Value<string>("status") != "cancelled" && partyId.Length > 0 && !_passedContacts.Contains(partyId)) _passedContacts.Add(partyId);
                        if (_pursuit?.Value<string>("requestId") == request.Value<string>("requestId"))
                        { RestorePatrol(); _pursuit = null; }
                    }
                    ReleaseTime(request);
                    string accepted = response["request"]?.Value<string>("status");
                    if (accepted == "accepted" && Enabled(request) && StillPresent(request))
                    {
                        var heroes = (request["heroes"] as JArray).OfType<JObject>().Select(r => Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == r.Value<string>("heroId"))).Where(h => h != null).ToList();
                        if (request.Value<string>("kind") == "keep") ReignPartyChatScreenManager.OpenApproach(heroes, request);
                        else if (heroes.Count > 0) ReignIndividualChatScreenManager.OpenForHero(heroes[0], new JObject
                            { ["approachRequestId"] = request.Value<string>("requestId"), ["approachOpening"] = true }, null);
                        if (!ReignPartyChatScreenManager.IsOpenOrPending && !ReignIndividualChatScreenManager.IsOpen)
                            throw new InvalidOperationException("The accepted conversation could not open; its invitation is retained.");
                    }
                    _requests.Remove(request);
                    if (_shownRequest == request) { _shownRequest = null; _popup = null; }
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { if (CurrentOwner(owner, epoch)) { _retryDelay = 10; ReignLog.Warn("Approach decision retained for retry: " + ex.Message); } }
            finally { if (CurrentOwner(owner, epoch)) _deciding = false; }
        }
    }
}
