using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    internal static class ReignDuelService
    {
        private static readonly Dictionary<string, ReignDuelIntent> Pending = new Dictionary<string, ReignDuelIntent>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static ReignActionResult QueueConversationDuel(ReignWorldActionRecord action, Hero actorHero, Hero targetHero)
        {
            if (action != null && Pending.TryGetValue(action.ActionId, out ReignDuelIntent existing))
            {
                return ReignActionResult.Progress("Conversation duel is waiting for the mission hook against " + existing.OpponentName + ".")
                    .WithResultCode(Active.Contains(action.ActionId) ? "conversation_duel_active" : "conversation_duel_waiting_for_mission")
                    .WithNextAttemptDelay(0.25f)
                    .WithEffect("conversation_duel_intent", "hero", existing.OpponentHeroStringId, existing.OpponentName, "mode=" + existing.Mode + ";waiting=true");
            }

            Hero conversationHero = CharacterObject.OneToOneConversationCharacter?.HeroObject;
            Hero opponent = ResolveOpponent(action, actorHero, targetHero, conversationHero);
            bool testSource = IsTestSource(action);

            if (opponent == null)
            {
                return ReignActionResult.ValidationFailed("DuelPlayer requires the current conversation hero as the opponent.")
                    .WithResultCode("duel_missing_conversation_opponent");
            }

            if (conversationHero == null || conversationHero != opponent)
            {
                if (testSource)
                {
                    return ReignActionResult.Progress("Conversation-scoped duel intent recorded for " + opponent.Name + "; the mission hook requires an active one-on-one conversation before it can begin.")
                        .WithResultCode("duel_requires_conversation_hook")
                        .WithEffect("conversation_duel_intent", "hero", opponent.StringId, opponent.Name.ToString(), "requiresActiveOneToOneConversation=true")
                        .WithChangedEntity("action", action.ActionId, action.Type.ToString(), "pending_conversation_hook");
                }

                return ReignActionResult.ValidationFailed("DuelPlayer can only be initiated while speaking one-on-one with the target hero.")
                    .WithResultCode("duel_requires_active_one_to_one_conversation")
                    .WithDiagnostic("resolvedOpponent", opponent.StringId)
                    .WithDiagnostic("currentConversationHero", conversationHero?.StringId ?? string.Empty);
            }

            if (!ReignConversationEligibility.IsAdultLivingNpc(opponent))
            {
                return ReignActionResult.ValidationFailed("DuelPlayer requires a living adult non-player conversation opponent.")
                    .WithResultCode("duel_invalid_opponent");
            }

            ReignDuelIntent intent = BuildIntent(action, opponent);
            intent.CapturedOpponentAgent = FindConversationHeroAgent(opponent.StringId) ?? FindHeroAgent(Mission.Current, opponent.StringId);
            Pending[action.ActionId] = intent;
            if (string.Equals(intent.Purpose, "arrest_capture", StringComparison.OrdinalIgnoreCase))
            {
                ReignArrestCampaignBehavior.Instance?.MarkDuelPending(intent.ArrestCaseId,
                    action.ActionId);
            }

            string modeText = intent.Lethal ? "honor duel" : "training duel";
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + opponent.Name + " is ready for a " + modeText + ".", Color.FromUint(0xFFFFD36A)));
            ReignLog.Info("Conversation duel armed action=" + action.ActionId + " opponent=" + opponent.StringId + " mode=" + intent.Mode + " capturedAgent=" + (intent.CapturedOpponentAgent != null) + " state=" + DescribePendingState(Mission.Current));

            return ReignActionResult.Progress("Conversation duel armed with " + opponent.Name + ".")
                .WithResultCode("conversation_duel_armed")
                .WithNextAttemptDelay(0.25f)
                .WithEffect("conversation_duel_intent", "hero", opponent.StringId, opponent.Name.ToString(), "mode=" + intent.Mode + ";lethal=" + intent.Lethal)
                .WithChangedEntity("action", action.ActionId, action.Type.ToString(), "mission_hook_armed")
                .WithDiagnostic("opponentHeroId", opponent.StringId)
                .WithDiagnostic("duelMode", intent.Mode);
        }

        public static bool TryGetStartableIntent(Mission mission, out ReignDuelIntent intent, out Agent playerAgent, out Agent opponentAgent)
        {
            intent = null;
            playerAgent = mission?.MainAgent ?? Agent.Main;
            opponentAgent = null;

            if (mission == null || playerAgent == null || !playerAgent.IsActive())
            {
                return false;
            }

            foreach (ReignDuelIntent candidate in Pending.Values)
            {
                if (candidate == null || Active.Contains(candidate.ActionId))
                {
                    continue;
                }

                opponentAgent = IsUsableAgent(candidate.CapturedOpponentAgent, candidate.OpponentHeroStringId)
                    ? candidate.CapturedOpponentAgent
                    : FindHeroAgent(mission, candidate.OpponentHeroStringId) ?? FindConversationHeroAgent(candidate.OpponentHeroStringId);
                if (opponentAgent == null || !opponentAgent.IsActive())
                {
                    continue;
                }

                intent = candidate;
                return true;
            }

            return false;
        }

        public static string DescribePendingState(Mission mission)
        {
            StringBuilder builder = new StringBuilder();
            int missionAgentCount = 0;
            if (mission != null)
            {
                foreach (Agent ignored in mission.AllAgents)
                {
                    missionAgentCount++;
                }
            }

            Hero conversationHero = CharacterObject.OneToOneConversationCharacter?.HeroObject;
            builder.Append("pending=").Append(Pending.Count)
                .Append("; active=").Append(Active.Count)
                .Append("; missionAgents=").Append(missionAgentCount)
                .Append("; playerAgent=").Append((mission?.MainAgent ?? Agent.Main) != null)
                .Append("; conversationHero=").Append(conversationHero?.StringId ?? "none");

            foreach (ReignDuelIntent candidate in Pending.Values)
            {
                if (candidate == null || Active.Contains(candidate.ActionId))
                {
                    continue;
                }

                Agent missionAgent = FindHeroAgent(mission, candidate.OpponentHeroStringId);
                Agent conversationAgent = FindConversationHeroAgent(candidate.OpponentHeroStringId);
                builder.Append("; candidate=").Append(candidate.ActionId)
                    .Append("/").Append(candidate.OpponentHeroStringId)
                    .Append(" capturedAgent=").Append(candidate.CapturedOpponentAgent != null)
                    .Append(" capturedActive=").Append(candidate.CapturedOpponentAgent != null && candidate.CapturedOpponentAgent.IsActive())
                    .Append(" missionAgent=").Append(missionAgent != null)
                    .Append(" missionActive=").Append(missionAgent != null && missionAgent.IsActive())
                    .Append(" conversationAgent=").Append(conversationAgent != null)
                    .Append(" conversationActive=").Append(conversationAgent != null && conversationAgent.IsActive());
            }

            return builder.ToString();
        }

        public static void MarkActive(string actionId)
        {
            if (!string.IsNullOrWhiteSpace(actionId))
            {
                Active.Add(actionId);
            }
        }

        public static void MarkInactive(string actionId)
        {
            if (!string.IsNullOrWhiteSpace(actionId))
            {
                Active.Remove(actionId);
            }
        }

        public static void Complete(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return;
            }

            Pending.Remove(actionId);
            Active.Remove(actionId);
        }

        public static bool TryGetFirstPendingDuel(out ReignDuelIntent intent)
        {
            foreach (ReignDuelIntent candidate in Pending.Values)
            {
                if (candidate != null && !Active.Contains(candidate.ActionId))
                {
                    intent = candidate;
                    return true;
                }
            }

            intent = null;
            return false;
        }

        public static bool TryGetPendingDuel(string actionId, out ReignDuelIntent intent)
        {
            if (!string.IsNullOrWhiteSpace(actionId)
                && Pending.TryGetValue(actionId, out ReignDuelIntent candidate)
                && candidate != null
                && !Active.Contains(candidate.ActionId))
            {
                intent = candidate;
                return true;
            }

            intent = null;
            return false;
        }

        public static void CancelPendingDuel(string actionId)
        {
            if (!TryGetPendingDuel(actionId, out ReignDuelIntent intent))
            {
                return;
            }

            ReignActionResult result = ReignActionResult.NoOp("Duel with " + intent.OpponentName + " was cancelled.")
                .WithResultCode("duel_cancelled")
                .WithEffect("duel_cancelled", "hero", intent.OpponentHeroStringId, intent.OpponentName, "mode=" + intent.Mode)
                .WithChangedEntity("action", intent.ActionId, "RegularDuelPlayer", "cancelled_by_player");

            Complete(intent.ActionId);
            ReignAICampaignBehavior.Instance?.CompleteActionFromMissionHook(intent.ActionId, result);
            ReignLog.Info("Conversation duel cancelled action=" + intent.ActionId + " opponent=" + intent.OpponentHeroStringId);
        }

        public static void FailPendingDuel(ReignDuelIntent intent, string message, string resultCode)
        {
            if (intent == null)
            {
                return;
            }

            ReignActionResult result = ReignActionResult.FailTerminal(message, resultCode ?? "duel_launch_failed", "mission_launch")
                .WithEffect("duel_failed", "hero", intent.OpponentHeroStringId, intent.OpponentName, "mode=" + intent.Mode)
                .WithChangedEntity("action", intent.ActionId, "RegularDuelPlayer", "duel_launch_failed")
                .WithDiagnostic("duelMode", intent.Mode);

            Complete(intent.ActionId);
            ReignAICampaignBehavior.Instance?.CompleteActionFromMissionHook(intent.ActionId, result);
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(0xFFFF6600)));
            ReignLog.Warn("Conversation duel failed action=" + intent.ActionId + " opponent=" + intent.OpponentHeroStringId + " reason=" + message);
        }

        public static void CompleteDuelFromMapMission(ReignDuelIntent intent, Hero winnerHero, string finishReason)
        {
            if (intent == null)
            {
                return;
            }

            Hero opponent = intent.OpponentHero;
            bool playerWon = winnerHero == null || winnerHero == Hero.MainHero || (winnerHero.StringId == Hero.MainHero?.StringId);
            Hero loserHero = playerWon ? opponent : Hero.MainHero;
            Hero resolvedWinner = playerWon ? Hero.MainHero : opponent;
            bool killed = ApplyLethalOutcome(intent, resolvedWinner, loserHero);

            string winnerName = resolvedWinner?.Name?.ToString() ?? (playerWon ? "the player" : intent.OpponentName);
            string loserName = loserHero?.Name?.ToString() ?? (playerWon ? intent.OpponentName : "the player");
            string message = BuildResultMessage(intent, winnerName, loserName, killed);

            bool arrestDuel = string.Equals(intent.Purpose, "arrest_capture",
                StringComparison.OrdinalIgnoreCase);
            if (arrestDuel)
                ReignArrestCampaignBehavior.Instance?.CompleteArrestDuel(
                    intent.ArrestCaseId, opponent, playerWon);
            ReignActionResult result = ReignActionResult.Done(message)
                .WithResultCode(arrestDuel
                    ? playerWon ? "arrest_duel_capture_completed" : "arrest_duel_escape_completed"
                    : intent.Lethal ? "lethal_duel_completed" : "training_duel_completed")
                .WithEffect("duel_completed", "hero", intent.OpponentHeroStringId, intent.OpponentName, "mode=" + intent.Mode + ";winner=" + winnerName + ";loser=" + loserName + ";reason=" + finishReason)
                .WithChangedEntity("hero", intent.OpponentHeroStringId, intent.OpponentName, playerWon ? "lost_duel_to_player" : "won_duel_against_player")
                .WithDiagnostic("finishReason", finishReason ?? string.Empty);
            AddOutcomeDiagnostics(result, intent, playerWon, resolvedWinner, loserHero, killed);
            RecordDuelSocialOutcome(intent, resolvedWinner, loserHero, finishReason);

            if (killed && loserHero != null)
            {
                result.WithEffect("hero_killed", "hero", loserHero.StringId, loserHero.Name.ToString(), "duelActionId=" + intent.ActionId);
                result.WithChangedEntity("hero", loserHero.StringId, loserHero.Name.ToString(), "killed_in_duel");
            }

            Complete(intent.ActionId);
            ReignAICampaignBehavior.Instance?.CompleteActionFromMissionHook(intent.ActionId, result);
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(0xFFFFD36A)));
            ReignLog.Info("Conversation duel completed from map mission action=" + intent.ActionId + " result=" + result.ResultCode + " killed=" + killed + " callbackWinner=" + (winnerHero?.StringId ?? "null"));
        }

        internal static void RecordDuelSocialOutcome(ReignDuelIntent intent, Hero winnerHero, Hero loserHero, string finishReason)
        {
            if (intent == null) return;
            string source = "duel|" + intent.ActionId;
            JObject evidence = new JObject
            {
                ["duelMode"] = intent.Mode ?? string.Empty,
                ["duelPurpose"] = intent.Purpose ?? string.Empty,
                ["arrestCaseId"] = intent.ArrestCaseId ?? string.Empty,
                ["lethal"] = intent.Lethal,
                ["finishReason"] = finishReason ?? string.Empty,
                ["winnerHeroId"] = winnerHero?.StringId ?? string.Empty,
                ["loserHeroId"] = loserHero?.StringId ?? string.Empty
            };
            if (winnerHero != null)
                ReignWorldHistoryCampaignBehavior.Instance?.RecordSocialOutcome(
                    "duelist", winnerHero, "winner", source + "|winner",
                    winnerHero.Name + " prevailed in a duel.", evidence);
            if (loserHero != null)
                ReignWorldHistoryCampaignBehavior.Instance?.RecordSocialOutcome(
                    "fallen_challenger", loserHero, "loser", source + "|loser",
                    loserHero.Name + " was defeated in a duel.", evidence);
        }

        public static bool HasPendingForOpponent(string heroStringId)
        {
            if (string.IsNullOrWhiteSpace(heroStringId))
            {
                return false;
            }

            foreach (ReignDuelIntent intent in Pending.Values)
            {
                if (intent != null
                    && !Active.Contains(intent.ActionId)
                    && string.Equals(intent.OpponentHeroStringId, heroStringId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasPendingDuel
        {
            get
            {
                foreach (ReignDuelIntent intent in Pending.Values)
                {
                    if (intent != null && !Active.Contains(intent.ActionId))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private static Hero ResolveOpponent(ReignWorldActionRecord action, Hero actorHero, Hero targetHero, Hero conversationHero)
        {
            if (conversationHero != null && conversationHero != Hero.MainHero)
            {
                if (SameHero(conversationHero, actorHero) || SameHero(conversationHero, targetHero))
                {
                    return conversationHero;
                }

                if (actorHero == null && targetHero == null)
                {
                    return conversationHero;
                }
            }

            if (targetHero != null && targetHero != Hero.MainHero)
            {
                return targetHero;
            }

            if (actorHero != null && actorHero != Hero.MainHero)
            {
                return actorHero;
            }

            return null;
        }

        private static bool SameHero(Hero left, Hero right)
        {
            return left != null && right != null && string.Equals(left.StringId, right.StringId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTestSource(ReignWorldActionRecord action)
        {
            string source = action?.Source ?? string.Empty;
            return source.IndexOf("gauntlet", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("dry", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ReignDuelIntent BuildIntent(ReignWorldActionRecord action, Hero opponent)
        {
            JObject terms = ParseTerms(action.TermsJson);
            string mode = ReadString(terms, "duelMode", ReadString(terms, "mode", "training")).Trim().ToLowerInvariant();
            bool lethal = ReadBool(terms, "lethal",
                mode == "lethal" || mode == "death" || mode == "to_the_death" || mode == "honor" || mode == "honour");
            if (lethal)
            {
                mode = "lethal";
            }
            else
            {
                mode = "training";
            }
            string purpose = ResolvePurpose(terms, action, opponent, lethal);

            return new ReignDuelIntent
            {
                ActionId = action.ActionId,
                OpponentHeroStringId = opponent.StringId,
                OpponentName = opponent.Name?.ToString() ?? opponent.StringId,
                Mode = mode,
                Purpose = purpose,
                ArrestCaseId = ReadString(terms, "caseId", string.Empty),
                Reason = action.Reason ?? string.Empty,
                Lethal = lethal,
                AllowPlayerDeath = ReadBool(terms, "allowPlayerDeath", false),
                TargetDeathRiskPercent = Clamp(ReadInt(terms, "targetDeathRiskPercent", lethal ? 100 : 0), 0, 100),
                PlayerDeathRiskPercent = Clamp(ReadInt(terms, "playerDeathRiskPercent", 0), 0, 100),
                PauseNearbyAgents = ReadBool(terms, "pauseNearbyAgents", true),
                PauseRadius = Clamp(ReadFloat(terms, "pauseRadius", 18f), 8f, 45f),
                PlayerHealth = Clamp(ReadFloat(terms, "playerHealth", 100f), 20f, 500f),
                OpponentHealth = Clamp(ReadFloat(terms, "opponentHealth", 100f), 20f, 500f),
                TrainingStopHealth = Clamp(ReadFloat(terms, "trainingStopHealth", 8f), 1f, 30f),
                CreatedDay = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays
            };
        }

        private static Agent FindHeroAgent(Mission mission, string heroStringId)
        {
            if (mission == null || string.IsNullOrWhiteSpace(heroStringId))
            {
                return null;
            }

            foreach (Agent agent in mission.AllAgents)
            {
                if (agent == null || !agent.IsHuman)
                {
                    continue;
                }

                Hero hero = HeroForAgent(agent);
                if (hero != null && string.Equals(hero.StringId, heroStringId, StringComparison.OrdinalIgnoreCase))
                {
                    return agent;
                }
            }

            return null;
        }

        private static Agent FindConversationHeroAgent(string heroStringId)
        {
            if (string.IsNullOrWhiteSpace(heroStringId))
            {
                return null;
            }

            Agent oneToOne = TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.OneToOneConversationAgent as Agent;
            if (MatchesHero(oneToOne, heroStringId))
            {
                return oneToOne;
            }

            IReadOnlyList<IAgent> conversationAgents = TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.ConversationAgents;
            if (conversationAgents == null)
            {
                return null;
            }

            foreach (IAgent conversationAgent in conversationAgents)
            {
                Agent agent = conversationAgent as Agent;
                if (MatchesHero(agent, heroStringId))
                {
                    return agent;
                }
            }

            return null;
        }

        private static bool MatchesHero(Agent agent, string heroStringId)
        {
            Hero hero = HeroForAgent(agent);
            return hero != null && string.Equals(hero.StringId, heroStringId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUsableAgent(Agent agent, string heroStringId)
        {
            return agent != null
                && agent.IsActive()
                && MatchesHero(agent, heroStringId);
        }

        internal static Hero HeroForAgent(Agent agent)
        {
            CharacterObject character = agent?.Character as CharacterObject;
            return character?.HeroObject;
        }

        private static bool ApplyLethalOutcome(ReignDuelIntent intent, Hero winnerHero, Hero loserHero)
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

        private static string BuildResultMessage(ReignDuelIntent intent, string winnerName, string loserName, bool killed)
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

        internal static ReignActionResult AddOutcomeDiagnostics(
            ReignActionResult result,
            ReignDuelIntent intent,
            bool playerWon,
            Hero winnerHero,
            Hero loserHero,
            bool killed)
        {
            if (result == null || intent == null)
            {
                return result;
            }

            return result
                .WithDiagnostic("lethal", intent.Lethal.ToString())
                .WithDiagnostic("duelPurpose", string.IsNullOrWhiteSpace(intent.Purpose) ? (intent.Lethal ? "lethal" : "friendly") : intent.Purpose)
                .WithDiagnostic("playerWon", playerWon.ToString())
                .WithDiagnostic("winnerHeroId", winnerHero?.StringId ?? (playerWon ? Hero.MainHero?.StringId : intent.OpponentHeroStringId) ?? string.Empty)
                .WithDiagnostic("loserHeroId", loserHero?.StringId ?? (playerWon ? intent.OpponentHeroStringId : Hero.MainHero?.StringId) ?? string.Empty)
                .WithDiagnostic("killed", killed.ToString());
        }

        private static string ResolvePurpose(JObject terms, ReignWorldActionRecord action, Hero opponent, bool lethal)
        {
            if (lethal)
            {
                return "lethal";
            }

            string declared = FirstNonEmpty(
                ReadString(terms, "duelPurpose", string.Empty),
                ReadString(terms, "challengeTone", string.Empty),
                ReadString(terms, "challengeKind", string.Empty));
            string normalized = declared.Trim().ToLowerInvariant();
            if (normalized == "arrest_capture" || normalized == "arrest capture")
            {
                return "arrest_capture";
            }
            if (ContainsAny(normalized, "hostile", "angry", "anger", "adversarial", "vengeance", "retribution"))
            {
                return "hostile";
            }
            if (ContainsAny(normalized, "friendly", "training", "spar", "practice"))
            {
                return "friendly";
            }

            string reason = (action?.Reason ?? string.Empty).ToLowerInvariant();
            if (ContainsAny(reason, "angry", "anger", "hostile", "insult", "offense", "offence", "vengeance", "retribution"))
            {
                return "hostile";
            }

            int relation = opponent == null || Hero.MainHero == null ? 0 : opponent.GetRelation(Hero.MainHero);
            return relation <= World.ReignActionValidator.NpcAttackPlayerRelationThreshold ? "hostile" : "friendly";
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            return !string.IsNullOrWhiteSpace(value)
                && terms != null
                && terms.Any(term => !string.IsNullOrWhiteSpace(term)
                    && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values == null
                ? string.Empty
                : values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static JObject ParseTerms(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JObject();
            }

            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return new JObject();
            }
        }

        private static string ReadString(JObject obj, string key, string fallback)
        {
            JToken token = obj?[key];
            return token == null ? fallback : token.ToString();
        }

        private static bool ReadBool(JObject obj, string key, bool fallback)
        {
            JToken token = obj?[key];
            if (token == null)
            {
                return fallback;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            return bool.TryParse(token.ToString(), out bool parsed) ? parsed : fallback;
        }

        private static int ReadInt(JObject obj, string key, int fallback)
        {
            JToken token = obj?[key];
            return token != null && int.TryParse(token.ToString(), out int parsed) ? parsed : fallback;
        }

        private static float ReadFloat(JObject obj, string key, float fallback)
        {
            JToken token = obj?[key];
            return token != null && float.TryParse(token.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
