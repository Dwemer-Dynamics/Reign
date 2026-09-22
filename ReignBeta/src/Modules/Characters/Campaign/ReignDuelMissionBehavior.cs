using System;
using System.Collections.Generic;
using System.Reflection;
using ReignBeta.Integration;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    internal sealed class ReignDuelMissionBehavior : MissionBehavior
    {
        private readonly List<Agent> _pausedAgents = new List<Agent>();
        private ReignDuelIntent _intent;
        private Agent _playerAgent;
        private Agent _opponentAgent;
        private Formation _opponentFormation;
        private float _playerOriginalHealth;
        private float _playerOriginalHealthLimit;
        private float _opponentOriginalHealth;
        private float _opponentOriginalHealthLimit;
        private float _pauseTimer;
        private bool _active;

        public override MissionBehaviorType BehaviorType
        {
            get { return (MissionBehaviorType)1; }
        }

        public static bool TryStartFromCurrentMission(string source)
        {
            try
            {
                Mission mission = Mission.Current;
                if (mission == null)
                {
                    if (ReignDuelService.HasPendingDuel)
                    {
                        ReignLog.Warn("Conversation duel start requested from " + source + " but Mission.Current was null.");
                    }

                    return false;
                }

                ReignDuelMissionBehavior behavior = mission.GetMissionBehavior<ReignDuelMissionBehavior>();
                if (behavior == null)
                {
                    if (ReignDuelService.HasPendingDuel)
                    {
                        ReignLog.Warn("Conversation duel start requested from " + source + " but ReignDuelMissionBehavior was not attached.");
                    }

                    return false;
                }

                return behavior.TryStartPendingDuel(source);
            }
            catch (System.Exception ex)
            {
                ReignLog.Warn("Conversation duel start request failed from " + source + ": " + ex.Message);
                return false;
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            if (!_active)
            {
                return;
            }

            if (_playerAgent == null || _opponentAgent == null)
            {
                FinishDuel(null, null, "duel_agent_missing");
                return;
            }

            if (!_playerAgent.IsActive() || !_opponentAgent.IsActive())
            {
                Agent winner = _playerAgent.IsActive() ? _playerAgent : _opponentAgent.IsActive() ? _opponentAgent : null;
                Agent loser = winner == _playerAgent ? _opponentAgent : _playerAgent;
                FinishDuel(winner, loser, "duel_agent_inactive");
                return;
            }

            _opponentAgent.SetTargetAgent(_playerAgent);

            if (!_intent.Lethal && (_playerAgent.Health <= _intent.TrainingStopHealth || _opponentAgent.Health <= _intent.TrainingStopHealth))
            {
                Agent winner = _playerAgent.Health > _opponentAgent.Health ? _playerAgent : _opponentAgent;
                Agent loser = winner == _playerAgent ? _opponentAgent : _playerAgent;
                FinishDuel(winner, loser, "training_threshold");
                return;
            }

            if (_intent.PauseNearbyAgents)
            {
                _pauseTimer += dt;
                if (_pauseTimer >= 0.75f)
                {
                    _pauseTimer = 0f;
                    PauseNearbyAgents();
                }
            }
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow killingBlow)
        {
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, killingBlow);
            if (!_active || affectedAgent == null)
            {
                return;
            }

            if (affectedAgent != _playerAgent && affectedAgent != _opponentAgent)
            {
                return;
            }

            Agent winner = affectedAgent == _playerAgent ? _opponentAgent : _playerAgent;
            FinishDuel(winner, affectedAgent, "agent_removed");
        }

        private bool TryStartPendingDuel(string source)
        {
            Hero currentConversationHero = CharacterObject.OneToOneConversationCharacter?.HeroObject;
            if (ReignIndividualChatScreenManager.IsOpen)
            {
                return false;
            }

            if (!ReignDuelService.TryGetStartableIntent(Mission, out ReignDuelIntent intent, out Agent player, out Agent opponent))
            {
                if (ReignDuelService.HasPendingDuel)
                {
                    ReignLog.Warn("Conversation duel start requested from " + source + " but agents were not startable: " + ReignDuelService.DescribePendingState(Mission));
                }
                return false;
            }

            TryEndConversationCompat(currentConversationHero, source);

            if (player == null || opponent == null || !player.IsActive() || !opponent.IsActive())
            {
                ReignLog.Warn("Conversation duel agents became inactive before setup from " + source + ": " + ReignDuelService.DescribePendingState(Mission));
                return false;
            }

            if (TryStartNativeDuel(intent, player, opponent, source, out string nativeError))
            {
                return true;
            }

            ReignLog.Warn("Conversation duel native fight unavailable from " + source + ": " + nativeError + ". Falling back to Reign scene control.");
            _intent = intent;
            _playerAgent = player;
            _opponentAgent = opponent;
            _opponentFormation = opponent.Formation;
            _playerOriginalHealth = player.Health;
            _playerOriginalHealthLimit = player.HealthLimit;
            _opponentOriginalHealth = opponent.Health;
            _opponentOriginalHealthLimit = opponent.HealthLimit;
            _pauseTimer = 0f;
            _active = true;
            ReignDuelService.MarkActive(intent.ActionId);

            ApplyDuelSetup(player, opponent);
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + intent.OpponentName + " steps into a " + (intent.Lethal ? "duel of honor." : "training duel."), Color.FromUint(0xFFFFD36A)));
            ReignLog.Info("Conversation duel started fallback action=" + intent.ActionId + " opponent=" + intent.OpponentHeroStringId + " mode=" + intent.Mode + " source=" + source);
            return true;
        }

        private void TryEndConversationCompat(Hero currentConversationHero, string source)
        {
            try
            {
                TaleWorlds.CampaignSystem.Conversation.ConversationManager manager = TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager;
                if (manager != null && manager.IsConversationInProgress)
                {
                    ReignLog.Info("Conversation duel ending native conversation flow from " + source + " for " + (currentConversationHero?.StringId ?? "unknown") + ".");
                    manager.EndConversation();
                }
            }
            catch (System.Exception ex)
            {
                ReignLog.Warn("Conversation duel could not end native conversation flow from " + source + ": " + ex.Message);
            }
        }

        private bool TryStartNativeDuel(ReignDuelIntent intent, Agent player, Agent opponent, string source, out string error)
        {
            error = null;
            object fightHandler = GetMissionFightHandler();
            if (fightHandler == null)
            {
                error = "MissionFightHandler was not available in this scene.";
                return false;
            }

            if (Agent.Main == null)
            {
                error = "Agent.Main was null.";
                return false;
            }

            TryStopUsingGameObjectCompat(player);
            TryStopUsingGameObjectCompat(opponent);

            List<Agent> playerSide = new List<Agent> { Agent.Main };
            List<Agent> opponentSide = new List<Agent> { opponent };
            MethodInfo[] methods = fightHandler.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (MethodInfo method in methods)
            {
                if (!string.Equals(method.Name, "StartCustomFight", System.StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 5 || parameters.Length > 6)
                {
                    continue;
                }

                if (!parameters[0].ParameterType.IsAssignableFrom(playerSide.GetType())
                    || !parameters[1].ParameterType.IsAssignableFrom(opponentSide.GetType())
                    || parameters[2].ParameterType != typeof(bool)
                    || parameters[3].ParameterType != typeof(bool))
                {
                    continue;
                }

                Delegate callback = CreateNativeFightEndDelegate(parameters[4].ParameterType, intent, Agent.Main, opponent);
                if (callback == null)
                {
                    continue;
                }

                object[] args = parameters.Length == 6
                    ? new object[] { playerSide, opponentSide, false, false, callback, 0f }
                    : new object[] { playerSide, opponentSide, false, false, callback };

                try
                {
                    ReignDuelService.MarkActive(intent.ActionId);
                    method.Invoke(fightHandler, args);
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + intent.OpponentName + " steps into a " + (intent.Lethal ? "duel of honor." : "training duel."), Color.FromUint(0xFFFFD36A)));
                    ReignLog.Info("Conversation duel started native action=" + intent.ActionId + " opponent=" + intent.OpponentHeroStringId + " mode=" + intent.Mode + " source=" + source);
                    return true;
                }
                catch (System.Exception ex)
                {
                    ReignDuelService.MarkInactive(intent.ActionId);
                    error = "StartCustomFight threw: " + (ex.InnerException?.Message ?? ex.Message);
                    return false;
                }
            }

            error = "No compatible StartCustomFight method found on MissionFightHandler.";
            return false;
        }

        private object GetMissionFightHandler()
        {
            try
            {
                Type fightHandlerType = Type.GetType("SandBox.Missions.MissionLogics.MissionFightHandler, SandBox");
                if (fightHandlerType == null)
                {
                    return null;
                }

                foreach (MethodInfo method in typeof(Mission).GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!method.IsGenericMethodDefinition
                        || method.GetParameters().Length != 0
                        || !string.Equals(method.Name, "GetMissionBehavior", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    MethodInfo generic = method.MakeGenericMethod(fightHandlerType);
                    return generic.Invoke(Mission, null);
                }
            }
            catch (System.Exception ex)
            {
                ReignLog.Warn("Conversation duel failed to resolve MissionFightHandler: " + ex.Message);
            }

            return null;
        }

        private Delegate CreateNativeFightEndDelegate(Type delegateType, ReignDuelIntent intent, Agent player, Agent opponent)
        {
            if (delegateType == null || !typeof(Delegate).IsAssignableFrom(delegateType))
            {
                return null;
            }

            try
            {
                NativeFightEndForwarder forwarder = new NativeFightEndForwarder(this, intent, player, opponent);
                MethodInfo invoke = typeof(NativeFightEndForwarder).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
                return Delegate.CreateDelegate(delegateType, forwarder, invoke, false);
            }
            catch (System.Exception ex)
            {
                ReignLog.Warn("Conversation duel failed to create native fight callback: " + ex.Message);
                return null;
            }
        }

        private void FinishNativeDuel(ReignDuelIntent finished, Agent playerAgent, Agent opponentAgent, bool playerWon)
        {
            Agent winner = playerWon ? playerAgent : opponentAgent;
            Agent loser = playerWon ? opponentAgent : playerAgent;
            CompleteDuel(finished, winner, loser, "native_fight_completed", false);
        }

        private static void TryStopUsingGameObjectCompat(Agent agent)
        {
            try
            {
                if (agent == null)
                {
                    return;
                }

                MethodInfo[] methods = agent.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (MethodInfo method in methods)
                {
                    if (!string.Equals(method.Name, "StopUsingGameObject", System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 0)
                    {
                        method.Invoke(agent, null);
                        return;
                    }

                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                    {
                        method.Invoke(agent, new object[] { true });
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private sealed class NativeFightEndForwarder
        {
            private readonly ReignDuelMissionBehavior _owner;
            private readonly ReignDuelIntent _intent;
            private readonly Agent _player;
            private readonly Agent _opponent;

            public NativeFightEndForwarder(ReignDuelMissionBehavior owner, ReignDuelIntent intent, Agent player, Agent opponent)
            {
                _owner = owner;
                _intent = intent;
                _player = player;
                _opponent = opponent;
            }

            public void Invoke(bool playerWon)
            {
                _owner.FinishNativeDuel(_intent, _player, _opponent, playerWon);
            }
        }

        private void ApplyDuelSetup(Agent player, Agent opponent)
        {
            if (player == null || opponent == null)
            {
                return;
            }

            player.HealthLimit = _intent.PlayerHealth;
            player.Health = _intent.PlayerHealth;
            opponent.HealthLimit = _intent.OpponentHealth;
            opponent.Health = _intent.OpponentHealth;

            opponent.SetAutomaticTargetSelection(false);
            opponent.SetDetachableFromFormation(true);
            opponent.Formation = null;
            opponent.SetTargetAgent(player);

            PauseNearbyAgents();
        }

        private void PauseNearbyAgents()
        {
            if (_intent == null || _playerAgent == null)
            {
                return;
            }

            foreach (Agent agent in Mission.AllAgents)
            {
                if (agent == null || agent == _playerAgent || agent == _opponentAgent || !agent.IsActive())
                {
                    continue;
                }

                if (!(agent.IsHuman || agent.IsMount))
                {
                    continue;
                }

                float dx = agent.Position.x - _playerAgent.Position.x;
                float dy = agent.Position.y - _playerAgent.Position.y;
                if (dx * dx + dy * dy > _intent.PauseRadius * _intent.PauseRadius)
                {
                    continue;
                }

                agent.SetIsAIPaused(true);
                if (!_pausedAgents.Contains(agent))
                {
                    _pausedAgents.Add(agent);
                }
            }
        }

        private void FinishDuel(Agent winnerAgent, Agent loserAgent, string finishReason)
        {
            if (!_active || _intent == null)
            {
                return;
            }

            ReignDuelIntent finished = _intent;
            CompleteDuel(finished, winnerAgent, loserAgent, finishReason, true);
        }

        private void CompleteDuel(ReignDuelIntent finished, Agent winnerAgent, Agent loserAgent, string finishReason, bool restoreManualState)
        {
            if (finished == null)
            {
                return;
            }

            Hero winnerHero = ReignDuelService.HeroForAgent(winnerAgent);
            Hero loserHero = ReignDuelService.HeroForAgent(loserAgent);
            bool playerWon = winnerAgent == _playerAgent;
            if (!restoreManualState)
            {
                playerWon = winnerAgent == Agent.Main || winnerHero == Hero.MainHero;
            }
            bool opponentWon = winnerAgent == _opponentAgent;
            bool killed = ApplyLethalOutcome(finished, winnerHero, loserHero);

            if (restoreManualState)
            {
                RestoreAgents();
            }

            string winnerName = winnerHero?.Name?.ToString() ?? (playerWon ? Hero.MainHero?.Name?.ToString() : "unknown");
            string loserName = loserHero?.Name?.ToString() ?? (opponentWon ? Hero.MainHero?.Name?.ToString() : "unknown");
            string message = BuildResultMessage(finished, winnerName, loserName, killed, finishReason);
            bool arrestDuel = string.Equals(finished.Purpose, "arrest_capture",
                StringComparison.OrdinalIgnoreCase);
            if (arrestDuel)
                ReignArrestCampaignBehavior.Instance?.CompleteArrestDuel(
                    finished.ArrestCaseId, finished.OpponentHero, playerWon);
            ReignActionResult result = ReignActionResult.Done(message)
                .WithResultCode(arrestDuel
                    ? playerWon ? "arrest_duel_capture_completed" : "arrest_duel_escape_completed"
                    : finished.Lethal ? "lethal_duel_completed" : "training_duel_completed")
                .WithEffect("duel_completed", "hero", finished.OpponentHeroStringId, finished.OpponentName, "mode=" + finished.Mode + ";winner=" + winnerName + ";loser=" + loserName + ";reason=" + finishReason)
                .WithChangedEntity("hero", finished.OpponentHeroStringId, finished.OpponentName, playerWon ? "lost_duel_to_player" : "won_duel_against_player")
                .WithDiagnostic("finishReason", finishReason);
            ReignDuelService.AddOutcomeDiagnostics(result, finished, playerWon, winnerHero, loserHero, killed);
            ReignDuelService.RecordDuelSocialOutcome(finished, winnerHero, loserHero, finishReason);

            if (killed && loserHero != null)
            {
                result.WithEffect("hero_killed", "hero", loserHero.StringId, loserHero.Name.ToString(), "duelActionId=" + finished.ActionId);
                result.WithChangedEntity("hero", loserHero.StringId, loserHero.Name.ToString(), "killed_in_duel");
            }

            ReignDuelService.Complete(finished.ActionId);
            ReignAICampaignBehavior.Instance?.CompleteActionFromMissionHook(finished.ActionId, result);
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(0xFFFFD36A)));
            ReignLog.Info("Conversation duel completed action=" + finished.ActionId + " result=" + result.ResultCode + " killed=" + killed);
        }

        private bool ApplyLethalOutcome(ReignDuelIntent intent, Hero winnerHero, Hero loserHero)
        {
            if (!intent.Lethal || winnerHero == null || loserHero == null || loserHero.IsDead)
            {
                return false;
            }

            bool loserIsPlayer = loserHero == Hero.MainHero;
            if (loserIsPlayer && !intent.AllowPlayerDeath)
            {
                return false;
            }

            int risk = loserIsPlayer ? intent.PlayerDeathRiskPercent : intent.TargetDeathRiskPercent;
            if (risk <= 0 || MBRandom.RandomFloat * 100f > risk)
            {
                return false;
            }

            KillCharacterAction.ApplyByBattle(loserHero, winnerHero, true);
            return true;
        }

        private void RestoreAgents()
        {
            foreach (Agent paused in _pausedAgents)
            {
                if (paused != null && paused.IsActive())
                {
                    paused.SetIsAIPaused(false);
                }
            }
            _pausedAgents.Clear();

            if (_opponentAgent != null)
            {
                if (_opponentAgent.IsActive())
                {
                    _opponentAgent.SetAutomaticTargetSelection(true);
                    _opponentAgent.SetDetachableFromFormation(false);
                    _opponentAgent.Formation = _opponentFormation;
                    _opponentAgent.HealthLimit = _opponentOriginalHealthLimit;
                    _opponentAgent.Health = _intent.Lethal ? _opponentAgent.Health : _opponentOriginalHealth;
                }
            }

            if (_playerAgent != null && _playerAgent.IsActive())
            {
                _playerAgent.HealthLimit = _playerOriginalHealthLimit;
                _playerAgent.Health = _intent.Lethal ? _playerAgent.Health : _playerOriginalHealth;
            }

            _active = false;
            _intent = null;
            _playerAgent = null;
            _opponentAgent = null;
            _opponentFormation = null;
        }

        private static string BuildResultMessage(ReignDuelIntent intent, string winnerName, string loserName, bool killed, string finishReason)
        {
            if (intent.Lethal)
            {
                return winnerName + " defeated " + loserName + " in a duel of honor" + (killed ? ", and the matter ended in death." : ".");
            }

            if (string.Equals(intent.Purpose, "hostile", StringComparison.OrdinalIgnoreCase))
            {
                return winnerName + " bested " + loserName + " in an angry challenge.";
            }

            return winnerName + " bested " + loserName + " in a training duel.";
        }
    }
}
