using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.UI;
using SandBox;
using SandBox.Missions.AgentBehaviors;
using SandBox.Objects.Usables;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.World
{
    // Scene orders own one NPC's movement behavior, never the player's controls.
    // A scene Follow completes after its native behavior binds to the actual
    // target; guidance still requires observed arrival at its destination.
    public sealed class ReignSceneActionMissionBehavior : MissionBehavior
    {
        // A distinct type lets Stop remove only the behavior added by this order.
        public sealed class AcceptedFollowBehavior : FollowAgentBehavior
        {
            public AcceptedFollowBehavior(AgentBehaviorGroup group) : base(group) { }
        }

        private sealed class Order
        {
            internal string ActionId;
            internal Agent Actor, Target;
            internal Hero ActorHero, TargetHero;
            internal DailyBehaviorGroup FollowGroup;
            internal AcceptedFollowBehavior FollowBehavior;
            internal Vec3 Destination, StartedAt, LastFollowTargetPosition;
            internal bool Guide, Reported, Bound, ResumeLogged, PlayerResolvedLogged, BindingStartedLogged, MovementApplied;
            internal float Elapsed, UpdateIn;
        }
        private readonly Dictionary<string, Order> _orders = new Dictionary<string, Order>();
        private readonly List<Order> _pendingRestores = new List<Order>();
        private JArray _cachedDestinations = new JArray();
        private bool _destinationsCached;
        private static ReignSceneActionMissionBehavior _lastActive;
        public override MissionBehaviorType BehaviorType => (MissionBehaviorType)1;
        public static ReignSceneActionMissionBehavior Current
        {
            get
            {
                ReignSceneActionMissionBehavior current = Mission.Current?.GetMissionBehavior<ReignSceneActionMissionBehavior>();
                return current ?? _lastActive;
            }
        }
        private Mission ActiveMission => Mission ?? TaleWorlds.MountAndBlade.Mission.Current;

        private static bool ConversationOpen(Mission mission) => ReignIndividualChatScreenManager.IsOpen
            || ReignPartyChatScreenManager.IsOpen || mission?.Mode == MissionMode.Conversation
            || TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.IsConversationInProgress == true;

        private static Agent ResolvePlayerAgent(Mission mission)
        {
            try
            {
                // Restrict this transition-sensitive lookup to managed mission
                // membership. Reading Character or Controller on an outgoing
                // native conversation wrapper can access an agent that Bannerlord
                // has already begun replacing.
                Agent player = mission?.MainAgent;
                if (player != null && mission?.Agents?.Contains(player) == true) return player;
                player = Agent.Main;
                return player != null && mission?.Agents?.Contains(player) == true ? player : null;
            }
            catch (NullReferenceException)
            {
                return null;
            }
        }

        private Agent FindAgent(Hero hero)
        {
            Mission mission = ActiveMission;
            if (hero == null || mission?.Agents == null) return null;
            foreach (Agent agent in mission.Agents)
                if (AgentRepresentsHero(agent, hero)) return agent;
            return null;
        }

        private static bool AgentRepresentsHero(Agent agent, Hero hero)
        {
            if (agent == null || hero == null) return false;
            try
            {
                // Individual Chat temporarily deactivates the native scene agent.
                // The mission still owns that same agent and the resident registry
                // remains authoritative, so identity lookup must survive the overlay.
                // Encountered residents retain a managed agent-to-hero registration
                // while Individual Chat replaces its native conversation wrapper.
                // Resolve that registration first so we do not dereference the stale
                // native Character pointer for these promoted residents.
                return ReignEncounteredResidentsCampaignBehavior.Instance?.ResolveAgent(agent) == hero
                    || agent.Character == hero.CharacterObject;
            }
            catch (NullReferenceException)
            {
                // Native scene collections can briefly retain a partially torn-down
                // agent. It cannot satisfy a present-agent action requirement.
                return false;
            }
        }

        public static JObject Context()
        {
            var behavior = Current;
            Mission mission = behavior?.ActiveMission;
            if (behavior != null && (ReignIndividualChatScreenManager.IsOpen
                || ReignPartyChatScreenManager.IsOpen))
                return new JObject { ["available"] = true,
                    ["destinations"] = new JArray(behavior._cachedDestinations.Select(x => x.DeepClone())),
                    ["followAgreements"] = ReignFollowCampaignBehavior.RuntimeSnapshot() };
            if (ResolvePlayerAgent(mission) == null) return new JObject { ["available"] = false,
                ["followAgreements"] = ReignFollowCampaignBehavior.RuntimeSnapshot() };
            behavior.CacheDestinations(mission);
            return new JObject
            {
                ["available"] = true,
                ["destinations"] = new JArray(behavior._cachedDestinations.Select(x => x.DeepClone())),
                ["followAgreements"] = ReignFollowCampaignBehavior.RuntimeSnapshot()
            };
        }

        private void CacheDestinations(Mission mission)
        {
            if (mission == null || _destinationsCached) return;
            _cachedDestinations = new JArray(mission.ActiveMissionObjects.OfType<Passage>()
                .Where(x => x.ToLocation != null).Select(x => new JObject
                { ["locationId"] = x.ToLocation.StringId, ["name"] = x.ToLocation.Name.ToString() }));
            _destinationsCached = true;
        }

        public bool IsFollowing(Hero hero) => hero != null && _orders.Values.Any(x => x.ActorHero == hero
            || AgentRepresentsHero(x.Actor, hero));

        public bool CanFollow(Hero actor, Hero target = null)
        {
            Mission mission = ActiveMission;
            if (mission == null) return false;
            Hero requestedTarget = target ?? Hero.MainHero;
            bool deferredChatBinding = ReignIndividualChatScreenManager.IsOpen
                && ReignIndividualChatScreenManager.ActiveHero == actor
                && requestedTarget == Hero.MainHero;
            if (deferredChatBinding) return true;
            Agent npc = FindAgent(actor);
            Agent player = ResolvePlayerAgent(mission);
            Agent followTarget = requestedTarget == Hero.MainHero ? player : FindAgent(requestedTarget);
            try
            {
                // Individual Chat may temporarily take the native agent out of AI
                // control. The order begins after that screen closes, so presence,
                // humanity and peacefulness are the durable eligibility facts.
                return deferredChatBinding || (npc != null && player != null && npc != player && npc.IsHuman
                    && followTarget != null && followTarget != npc
                    && !ReignSceneTeamSafety.AreHostile(npc.Team, player.Team));
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        public ReignActionResult Begin(ReignWorldActionRecord action, Hero actor, Hero target)
        {
            if (_orders.TryGetValue(action.ActionId, out Order prior))
                return prior.Reported ? ReignActionResult.NoOp("This scene follow is already active.")
                    : ReignActionResult.Progress("Waiting for the NPC's scene movement.").WithResultCode("scene_movement_pending");
            Mission mission = ActiveMission;
            bool guide = action.Type == ReignWorldActionType.RegularShowTheWay;
            bool deferredChatBinding = !guide && ReignIndividualChatScreenManager.IsOpen
                && ReignIndividualChatScreenManager.ActiveHero == actor
                && (target == null || target == Hero.MainHero);
            // During Individual Chat, retain only stable Hero identities. Native Agent
            // wrappers can point at the outgoing conversation representation and must not
            // be read or retained until the mission has settled after the overlay closes.
            Agent npc = deferredChatBinding ? null : FindAgent(actor);
            Agent player = deferredChatBinding ? null : ResolvePlayerAgent(mission);
            Agent followTarget = deferredChatBinding ? null : guide
                ? FindAgent(target == Hero.MainHero ? null : target)
                : (target == null || target == Hero.MainHero ? player : FindAgent(target));
            if (!deferredChatBinding && (npc == null || player == null || npc == player || !npc.IsHuman
                || ReignSceneTeamSafety.AreHostile(npc.Team, player.Team)))
                return ReignActionResult.ValidationFailed("Scene movement requires a present, peaceful NPC and the player in the same scene.");
            Vec3 destination = default;
            if (guide)
            {
                if (followTarget != null && followTarget != npc) destination = followTarget.Position;
                else
                {
                    JObject terms = string.IsNullOrWhiteSpace(action.TermsJson) ? new JObject() : JObject.Parse(action.TermsJson);
                    string locationId = terms.Value<string>("destinationLocationId") ?? "";
                    var passages = mission.ActiveMissionObjects.OfType<Passage>()
                        .Where(x => x.ToLocation != null && x.ToLocation.StringId == locationId).ToList();
                    if (locationId.Length == 0 || passages.Count != 1)
                        return ReignActionResult.ValidationFailed("Guidance needs one resolved present hero or a unique destination from native scene hints.");
                    destination = passages[0].GameEntity.GlobalPosition;
                }
            }
            else if (!deferredChatBinding && (followTarget == null || followTarget == npc))
                return ReignActionResult.ValidationFailed("Scene following needs a different active target agent.");

            // Replace only this mod's existing movement order for the same actor.
            if (!deferredChatBinding)
                Stop(actor, "A new accepted scene instruction replaced the previous one.");
            // Follow binding always happens on a later mission tick. Apart from avoiding
            // conversation teardown, this lets Bannerlord finish restoring the resident's
            // CampaignAgentComponent and AgentNavigator before Reign changes AI behavior.
            bool bound = guide && npc?.IsActive() == true;
            _orders[action.ActionId] = new Order { ActionId = action.ActionId, Actor = npc, Target = followTarget,
                ActorHero = actor, TargetHero = target ?? Hero.MainHero, Guide = guide, Destination = destination,
                Bound = bound, StartedAt = bound ? npc.Position : default };
            return ReignActionResult.Progress(guide ? actor.Name + " is preparing to guide you." : actor.Name + " is preparing to follow.")
                .WithResultCode("scene_movement_pending").WithNextAttemptDelay(0.25f);
        }

        public ReignActionResult Stop(Hero hero, string reason = "The NPC stopped following in this scene.")
        {
            Agent agent = FindAgent(hero);
            foreach (var order in _orders.Values.Where(x => x.ActorHero == hero
                || (agent != null && x.Actor == agent)).ToList())
            {
                if (ConversationOpen(ActiveMission)) _pendingRestores.Add(order);
                else Restore(order);
                if (!order.Reported) Complete(order, ReignActionResult.Obsolete(reason, "scene_order_cancelled"));
                _orders.Remove(order.ActionId);
            }
            return ReignActionResult.Done(reason).WithEffect("scene_follow_stopped", "hero", hero?.StringId ?? "",
                hero?.Name?.ToString() ?? "", "movement=stopped");
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            Mission mission = ActiveMission;
            if (mission == null) return;
            // Individual Chat is a Gauntlet overlay over the live town mission.
            // Mission.Current can be empty while the overlay processes an action,
            // so retain the behavior most recently ticked by the active mission.
            _lastActive = this;
            // Closing Reign's overlay can reveal a native conversation underneath.
            // Same-scene native dialogue does not always change Mission.Mode.
            // Wait for the campaign conversation too before taking movement ownership
            // or counting the movement timeout.
            if (ConversationOpen(mission)) return;
            foreach (Order stopped in _pendingRestores) Restore(stopped);
            _pendingRestores.Clear();
            EnsurePersistentFollowers();
            foreach (Order order in _orders.Values) order.Elapsed += dt;
            if (_orders.Values.Any(x => !x.Bound && x.Elapsed < 2f)) return;
            CacheDestinations(mission);
            Order resuming = _orders.Values.FirstOrDefault(x => !x.ResumeLogged);
            if (resuming != null)
            {
                resuming.ResumeLogged = true;
                ReignLog.Info("Scene order resume action=" + resuming.ActionId + " before player resolution.");
            }
            Agent player = ResolvePlayerAgent(mission);
            foreach (Order order in _orders.Values.Where(x => x.ResumeLogged && !x.PlayerResolvedLogged).ToList())
            {
                order.PlayerResolvedLogged = true;
                ReignLog.Info("Scene order resume action=" + order.ActionId + " playerResolved=" + (player != null));
            }
            foreach (Order order in _orders.Values.ToList())
            {
                if (!_orders.ContainsKey(order.ActionId)) continue;
                if (!order.Bound)
                {
                    // The first mission ticks after a Gauntlet/native conversation
                    // closes are still inside Bannerlord's agent reactivation
                    // transition. Even read-only Agent native calls can dereference
                    // the outgoing conversation representation during that window.
                    // Let the owning mission settle before resolving fresh agents.
                    if (!order.BindingStartedLogged)
                    {
                        order.BindingStartedLogged = true;
                        ReignLog.Info("Scene order binding action=" + order.ActionId + " after settleSeconds=" + order.Elapsed.ToString("F2"));
                    }
                    foreach (Order prior in _orders.Values.Where(x => x.ActionId != order.ActionId
                        && x.ActorHero == order.ActorHero).ToList())
                    {
                        Restore(prior);
                        if (!prior.Reported) Complete(prior, ReignActionResult.Obsolete(
                            "A new accepted scene instruction replaced the previous one.", "scene_order_cancelled"));
                        _orders.Remove(prior.ActionId);
                    }
                    order.Actor = FindAgent(order.ActorHero);
                    ReignLog.Info("Scene order binding action=" + order.ActionId
                        + " actorResolved=" + (order.Actor != null));
                    order.Target = order.TargetHero == null || order.TargetHero == Hero.MainHero
                        ? player : FindAgent(order.TargetHero);
                    ReignLog.Info("Scene order binding action=" + order.ActionId
                        + " targetResolved=" + (order.Target != null));
                    if (order.Actor?.IsActive() == true && order.Target?.IsActive() == true
                        && order.Actor != order.Target && order.Actor.IsAIControlled && order.Actor.IsHuman)
                    {
                        // Use the native navigator's Follow behavior so daily routines
                        // cannot overwrite a separately applied scripted position.
                        Vec3 startedAt = order.Actor.Position;
                        if (order.Guide || TryBindNativeFollow(order))
                        {
                            order.Bound = true;
                            order.StartedAt = startedAt;
                            if (!order.Guide) order.LastFollowTargetPosition = order.Target.Position;
                            continue;
                        }
                    }
                    if (order.Elapsed >= 10f)
                    {
                        Finish(order, false, "The scene participants did not return after the conversation closed.", "scene_actor_unavailable");
                        continue;
                    }
                    else continue;
                }
                if (order.Actor == null || !order.Actor.IsActive() || player == null
                    || (!order.Guide && (order.Target == null || !order.Target.IsActive()))
                    || ReignSceneTeamSafety.AreHostile(order.Actor.Team, player.Team))
                { Finish(order, false, "The scene participant became unavailable or hostile.", "scene_actor_unavailable"); continue; }
                if ((order.UpdateIn -= dt) > 0) continue;
                order.UpdateIn = 0.25f;
                Vec3 destination = order.Guide ? order.Target?.Position ?? order.Destination : order.Target.Position;
                float distance = order.Actor.Position.Distance(destination);
                float playerDistance = order.Actor.Position.Distance(player.Position);
                // Native Follow updates its destination from target velocity. A
                // scene transition or guarded player relocation can move the
                // target while its velocity is zero, leaving a valid stale path.
                bool targetJumped = !order.Guide && destination.Distance(order.LastFollowTargetPosition) > 8f;
                if (!order.Guide) order.LastFollowTargetPosition = destination;
                // Native dialogue may release the scripted behavior while the
                // lasting agreement still owns this order. Retake only an empty
                // slot; never replace another scene activity.
                if (!order.Guide && order.FollowBehavior != null
                    && order.FollowGroup?.ScriptedBehavior == null)
                {
                    order.FollowGroup.SetScriptedBehavior<AcceptedFollowBehavior>();
                    order.FollowBehavior.SetTargetAgent(order.Target);
                    ReignLog.Info("Scene follow rebound after native dialogue action=" + order.ActionId);
                }
                if (targetJumped && distance > 4.5f && order.FollowBehavior?.IsActive == true
                    && ReferenceEquals(order.FollowGroup?.ScriptedBehavior, order.FollowBehavior))
                {
                    // Native FollowAgentBehavior retains its private idle distance
                    // across SetTargetAgent. After a discontinuous move it can
                    // record the entire gap as that distance and never resume.
                    // Recreate only our own scripted behavior to reset that state.
                    order.FollowGroup.RemoveBehavior<AcceptedFollowBehavior>();
                    order.FollowBehavior = order.FollowGroup.AddBehavior<AcceptedFollowBehavior>();
                    order.FollowBehavior.SetTargetAgent(order.Target);
                    order.FollowGroup.SetScriptedBehavior<AcceptedFollowBehavior>();
                    ReignLog.Info("Scene follow reset discontinuous target action=" + order.ActionId);
                }
                // A conversation or a discontinuous target move can leave native Follow
                // active without a destination. Its normal update depends on target
                // velocity, so a stationary player cannot restart that path. Reissue
                // only this order's native target when pursuit has no destination.
                if (!order.Guide && order.FollowBehavior?.IsActive == true && distance > 4.5f
                    && order.FollowGroup?.Navigator != null
                    && !order.FollowGroup.Navigator.TargetPosition.IsValid)
                    order.FollowBehavior.SetTargetAgent(order.Target);
                if (order.Guide && distance <= 2f && playerDistance <= 5f)
                { Finish(order, true, "The guide and player reached the requested destination.", "scene_guidance_completed"); continue; }
                if (!order.Guide && !order.Reported && order.FollowBehavior?.IsActive == true)
                {
                    order.Reported = true;
                    Complete(order, ReignActionResult.Done("The NPC is following the selected scene participant.")
                        .WithResultCode("scene_follow_active").WithEffect("scene_follow_started", "action", order.ActionId, "Scene follow", "native_follow=active;target=bound"));
                }
                if ((!order.Reported && order.Elapsed >= (order.Guide ? 180f : 20f)))
                { Finish(order, false, "The NPC could not complete the scene movement in time.", "scene_path_timeout"); continue; }
                // Native FollowAgentBehavior owns pursuit, stopping distance and speed.
                // Guidance continues to use an explicit destination below.
                if (!order.Guide) continue;
                float stopDistance = order.Guide ? 2f : 2.5f;
                if (distance <= stopDistance || (order.Guide && playerDistance > 12f))
                {
                    if (order.MovementApplied)
                    {
                        order.Actor.DisableScriptedMovement();
                        order.MovementApplied = false;
                    }
                }
                else
                {
                    WorldPosition position = new WorldPosition(mission.Scene, UIntPtr.Zero, destination, false);
                    order.Actor.SetScriptedPosition(ref position, false, Agent.AIScriptedFrameFlags.DoNotRun);
                    order.MovementApplied = true;
                }
            }
        }

        private void EnsurePersistentFollowers()
        {
            if (ReignFollowCampaignBehavior.Instance == null) return;
            foreach (Tuple<Hero, Hero> pair in ReignFollowCampaignBehavior.Instance.ActiveSceneFollowers)
            {
                Hero actor = pair.Item1;
                Hero target = pair.Item2;
                if (IsFollowing(actor) || !CanFollow(actor, target)) continue;
                Agent npc = FindAgent(actor);
                Agent followTarget = target == Hero.MainHero ? ResolvePlayerAgent(ActiveMission) : FindAgent(target);
                if (npc == null || followTarget == null || npc == followTarget) continue;
                string id = "persistent_follow_" + actor.StringId;
                _orders[id] = new Order
                {
                    ActionId = id,
                    Actor = npc,
                    Target = followTarget,
                    ActorHero = actor,
                    TargetHero = target,
                    Reported = true
                };
                ReignLog.Info("Persistent scene follow queued actor=" + actor.StringId
                    + " target=" + target.StringId);
            }
        }

        protected override void OnEndMission()
        {
            foreach (Order stopped in _pendingRestores) Restore(stopped);
            _pendingRestores.Clear();
            foreach (Order order in _orders.Values.ToList())
                Finish(order, false, "The scene ended before the movement completed.", "scene_ended");
            if (ReferenceEquals(_lastActive, this)) _lastActive = null;
            base.OnEndMission();
        }

        private static void Restore(Order order)
        {
            try
            {
                if (order.Actor?.IsActive() != true) return;
                if (order.FollowBehavior != null
                    && ReferenceEquals(order.FollowGroup?.GetBehavior<AcceptedFollowBehavior>(), order.FollowBehavior))
                    order.FollowGroup.RemoveBehavior<AcceptedFollowBehavior>();
                if (order.MovementApplied) order.Actor.DisableScriptedMovement();
            }
            catch (NullReferenceException)
            {
                // Mission teardown can invalidate the owning native agent before the
                // behavior callback runs. Bannerlord then owns disposal of its group.
            }
        }

        private static bool TryBindNativeFollow(Order order)
        {
            AgentNavigator navigator = order.Actor.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
            DailyBehaviorGroup group = navigator?.GetBehaviorGroup<DailyBehaviorGroup>();
            // A quest or another native activity retains its scripted behavior.
            if (group == null || group.ScriptedBehavior != null) return false;
            AcceptedFollowBehavior follow = group.AddBehavior<AcceptedFollowBehavior>();
            order.FollowGroup = group;
            order.FollowBehavior = follow;
            follow.SetTargetAgent(order.Target);
            group.SetScriptedBehavior<AcceptedFollowBehavior>();
            ReignLog.Info("Scene follow native bind action=" + order.ActionId + " actor=" + order.Actor.Index
                + " target=" + order.Target.Index + " groupActive=" + group.IsActive);
            return true;
        }
        private static void Complete(Order order, ReignActionResult result)
            => ReignAICampaignBehavior.Instance?.CompleteActionFromMissionHook(order.ActionId, result);
        private void Finish(Order order, bool success, string message, string code)
        {
            Restore(order);
            _orders.Remove(order.ActionId);
            if (!order.Reported) Complete(order, success
                ? ReignActionResult.Done(message).WithResultCode(code).WithEffect("scene_guidance_completed", "action", order.ActionId, "Scene guidance", "arrival=observed")
                : ReignActionResult.FailTerminal(message, code, "scene_movement"));
        }
    }
}
