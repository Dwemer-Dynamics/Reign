using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.UI;
using SandBox.Objects.Usables;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.World
{
    // Scene orders own one NPC's scripted movement, never the player's controls.
    // Completion is observed from movement/proximity; a queued intention alone
    // is not a successful follow or guide receipt.
    public sealed class ReignSceneActionMissionBehavior : MissionBehavior
    {
        private sealed class Order
        {
            internal string ActionId;
            internal Agent Actor, Target;
            internal Vec3 Destination, StartedAt;
            internal bool Guide, Reported;
            internal float Elapsed, UpdateIn;
            internal Agent.AIScriptedFrameFlags OriginalFlags;
        }
        private readonly Dictionary<string, Order> _orders = new Dictionary<string, Order>();
        public override MissionBehaviorType BehaviorType => (MissionBehaviorType)1;
        public static ReignSceneActionMissionBehavior Current => Mission.Current?.GetMissionBehavior<ReignSceneActionMissionBehavior>();

        private Agent FindAgent(Hero hero) => hero == null ? null : Mission.Agents.FirstOrDefault(agent =>
            agent.IsActive() && (agent.Character == hero.CharacterObject
                || ReignEncounteredResidentsCampaignBehavior.Instance?.ResolveAgent(agent) == hero));

        public static JObject Context()
        {
            var behavior = Current;
            if (behavior == null || behavior.Mission.MainAgent == null) return new JObject { ["available"] = false };
            return new JObject
            {
                ["available"] = true,
                ["destinations"] = new JArray(behavior.Mission.ActiveMissionObjects.OfType<Passage>()
                    .Where(x => x.ToLocation != null).Select(x => new JObject
                    { ["locationId"] = x.ToLocation.StringId, ["name"] = x.ToLocation.Name.ToString() }))
            };
        }

        public bool IsFollowing(Hero hero) => hero != null && _orders.Values.Any(x => x.Actor != null
            && (x.Actor.Character == hero.CharacterObject || ReignEncounteredResidentsCampaignBehavior.Instance?.ResolveAgent(x.Actor) == hero));

        public bool CanFollow(Hero actor, Hero target = null)
        {
            Agent npc = FindAgent(actor);
            Agent player = Mission.MainAgent;
            Agent followTarget = FindAgent(target ?? Hero.MainHero);
            return npc != null && player != null && npc != player && npc.IsAIControlled && npc.IsHuman
                && player.IsActive() && followTarget != null && followTarget != npc
                && (npc.Team == null || player.Team == null || !npc.Team.IsEnemyOf(player.Team));
        }

        public ReignActionResult Begin(ReignWorldActionRecord action, Hero actor, Hero target)
        {
            if (_orders.TryGetValue(action.ActionId, out Order prior))
                return prior.Reported ? ReignActionResult.NoOp("This scene follow is already active.")
                    : ReignActionResult.Progress("Waiting for the NPC's scene movement.").WithResultCode("scene_movement_pending");
            Agent npc = FindAgent(actor);
            Agent player = Mission.MainAgent;
            if (npc == null || player == null || npc == player || !npc.IsAIControlled || !npc.IsHuman
                || !player.IsActive() || (npc.Team != null && player.Team != null && npc.Team.IsEnemyOf(player.Team)))
                return ReignActionResult.ValidationFailed("Scene movement requires a present, peaceful NPC and the player in the same scene.");
            bool guide = action.Type == ReignWorldActionType.RegularShowTheWay;
            Agent followTarget = guide ? FindAgent(target == Hero.MainHero ? null : target) : FindAgent(target ?? Hero.MainHero);
            Vec3 destination = default;
            if (guide)
            {
                if (followTarget != null && followTarget != npc) destination = followTarget.Position;
                else
                {
                    JObject terms = string.IsNullOrWhiteSpace(action.TermsJson) ? new JObject() : JObject.Parse(action.TermsJson);
                    string locationId = terms.Value<string>("destinationLocationId") ?? "";
                    var passages = Mission.ActiveMissionObjects.OfType<Passage>()
                        .Where(x => x.ToLocation != null && x.ToLocation.StringId == locationId).ToList();
                    if (locationId.Length == 0 || passages.Count != 1)
                        return ReignActionResult.ValidationFailed("Guidance needs one resolved present hero or a unique destination from native scene hints.");
                    destination = passages[0].GameEntity.GlobalPosition;
                }
            }
            else if (followTarget == null || followTarget == npc)
                return ReignActionResult.ValidationFailed("Scene following needs a different active target agent.");

            // Replace only this mod's existing movement order for the same actor.
            Stop(actor, "A new accepted scene instruction replaced the previous one.");
            _orders[action.ActionId] = new Order { ActionId = action.ActionId, Actor = npc, Target = followTarget,
                Guide = guide, Destination = destination, StartedAt = npc.Position, OriginalFlags = npc.GetScriptedFlags() };
            return ReignActionResult.Progress(guide ? actor.Name + " is preparing to guide you." : actor.Name + " is preparing to follow.")
                .WithResultCode("scene_movement_pending").WithNextAttemptDelay(0.25f);
        }

        public ReignActionResult Stop(Hero hero, string reason = "The NPC stopped following in this scene.")
        {
            Agent agent = FindAgent(hero);
            foreach (var order in _orders.Values.Where(x => x.Actor == agent).ToList())
            {
                Restore(order);
                if (!order.Reported) Complete(order, ReignActionResult.Obsolete(reason, "scene_order_cancelled"));
                _orders.Remove(order.ActionId);
            }
            return ReignActionResult.Done(reason).WithEffect("scene_follow_stopped", "hero", hero?.StringId ?? "",
                hero?.Name?.ToString() ?? "", "movement=stopped");
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (ReignIndividualChatScreenManager.IsOpen || ReignPartyChatScreenManager.IsOpen
                || TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager.IsConversationInProgress == true) return;
            foreach (Order order in _orders.Values.ToList())
            {
                if (order.Actor == null || !order.Actor.IsActive() || Mission.MainAgent == null
                    || (!order.Guide && (order.Target == null || !order.Target.IsActive()))
                    || (order.Actor.Team != null && Mission.MainAgent.Team != null && order.Actor.Team.IsEnemyOf(Mission.MainAgent.Team)))
                { Finish(order, false, "The scene participant became unavailable or hostile.", "scene_actor_unavailable"); continue; }
                order.Elapsed += dt;
                if ((order.UpdateIn -= dt) > 0) continue;
                order.UpdateIn = 0.25f;
                Vec3 destination = order.Guide ? order.Target?.Position ?? order.Destination : order.Target.Position;
                float distance = order.Actor.Position.Distance(destination);
                float playerDistance = order.Actor.Position.Distance(Mission.MainAgent.Position);
                if (order.Guide && distance <= 2f && playerDistance <= 5f)
                { Finish(order, true, "The guide and player reached the requested destination.", "scene_guidance_completed"); continue; }
                if (!order.Guide && !order.Reported && (distance <= 3f || order.Actor.Position.Distance(order.StartedAt) >= 0.5f))
                {
                    order.Reported = true;
                    Complete(order, ReignActionResult.Done("The NPC is following the selected scene participant.")
                        .WithResultCode("scene_follow_active").WithEffect("scene_follow_started", "action", order.ActionId, "Scene follow", "movement=observed"));
                }
                if ((!order.Reported && order.Elapsed >= (order.Guide ? 180f : 20f)))
                { Finish(order, false, "The NPC could not complete the scene movement in time.", "scene_path_timeout"); continue; }
                if (distance <= (order.Guide ? 2f : 2.5f) || (order.Guide && playerDistance > 12f))
                    order.Actor.DisableScriptedMovement();
                else
                {
                    WorldPosition position = new WorldPosition(Mission.Scene, UIntPtr.Zero, destination, false);
                    order.Actor.SetScriptedPosition(ref position, false, Agent.AIScriptedFrameFlags.DoNotRun);
                }
            }
        }

        protected override void OnEndMission()
        {
            foreach (Order order in _orders.Values.ToList())
                Finish(order, false, "The scene ended before the movement completed.", "scene_ended");
            base.OnEndMission();
        }

        private static void Restore(Order order)
        {
            if (order.Actor?.IsActive() == true)
            {
                order.Actor.DisableScriptedMovement();
                order.Actor.SetScriptedFlags(order.OriginalFlags);
            }
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
