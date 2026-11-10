using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    internal static class ReignDuelCombatPatches
    {
        private const string Owner = "com.bannerlordreign.duel.training.v1";
        internal static bool Ready { get; private set; }
        [ThreadStatic] private static ExperienceScope _experienceScope;
        private static readonly AccessTools.StructFieldRef<AttackCollisionData, bool> ShieldBlock
            = AccessTools.StructFieldRefAccess<AttackCollisionData, bool>("_attackBlockedWithShield");
        private static readonly AccessTools.StructFieldRef<AttackCollisionData, bool> WeaponBlock
            = AccessTools.StructFieldRefAccess<AttackCollisionData, bool>("_missileBlockedWithWeapon");
        private static readonly AccessTools.StructFieldRef<AttackCollisionData, bool> ShieldOnBack
            = AccessTools.StructFieldRefAccess<AttackCollisionData, bool>("_collidedWithShieldOnBack");
        private static readonly AccessTools.StructFieldRef<AttackCollisionData, bool> ColliderAgent
            = AccessTools.StructFieldRefAccess<AttackCollisionData, bool>("_isColliderAgent");
        private static readonly AccessTools.StructFieldRef<AttackCollisionData, int> CollisionResult
            = AccessTools.StructFieldRefAccess<AttackCollisionData, int>("_collisionResult");
        private static readonly AccessTools.FieldRef<Mission, Dictionary<int, Mission.Missile>> Missiles
            = AccessTools.FieldRefAccess<Mission, Dictionary<int, Mission.Missile>>("_missilesDictionary");

        internal static void Apply()
        {
            if (Ready) return;
            var harmony = new Harmony(Owner);
            try
            {
                harmony.Patch(Required(typeof(Agent), "HandleBlow"),
                    transpiler: new HarmonyMethod(typeof(ReignDuelCombatPatches), nameof(TranspileDamage)));
                harmony.Patch(Required(typeof(Hero), nameof(Hero.AddSkillXp)),
                    prefix: new HarmonyMethod(typeof(ReignDuelCombatPatches), nameof(ScaleSkillXp)));
                harmony.Patch(Required(typeof(BattleAgentLogic), nameof(BattleAgentLogic.OnScoreHit)),
                    prefix: new HarmonyMethod(typeof(ReignDuelCombatPatches), nameof(NativeBattleHit)));
                harmony.Patch(Required(typeof(Mission), "PrintAttackCollisionResults"),
                    prefix: new HarmonyMethod(typeof(ReignDuelCombatPatches), nameof(CombatNotice)));
                Ready = true;
            }
            catch
            {
                harmony.UnpatchAll(Owner);
                throw;
            }
        }

        internal static void Unapply()
        {
            new Harmony(Owner).UnpatchAll(Owner);
            Ready = false;
            _experienceScope = null;
        }

        private static MethodInfo Required(Type type, string name)
            => AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);

        private static IEnumerable<CodeInstruction> TranspileDamage(IEnumerable<CodeInstruction> instructions)
        {
            var source = instructions.ToList();
            MethodInfo setter = AccessTools.PropertySetter(typeof(Agent), nameof(Agent.Health));
            if (source.Count(i => i.Calls(setter)) != 1)
                throw new InvalidOperationException("Duel training requires exactly one native HandleBlow health assignment.");
            foreach (CodeInstruction original in source)
            {
                var copy = new CodeInstruction(original);
                if (original.Calls(setter))
                {
                    var blowArgument = new CodeInstruction(OpCodes.Ldarg_1);
                    blowArgument.labels.AddRange(copy.labels);
                    blowArgument.blocks.AddRange(copy.blocks);
                    copy.labels.Clear(); copy.blocks.Clear();
                    yield return blowArgument;
                    copy.opcode = OpCodes.Call;
                    copy.operand = Required(typeof(ReignDuelCombatPatches), nameof(ApplyHitHealth));
                }
                yield return copy;
            }
        }

        private static ReignDuelMissionBehavior ForHit(Agent target, Agent attacker)
        {
            var behavior = target?.Mission?.GetMissionBehavior<ReignDuelMissionBehavior>();
            return behavior?.OwnsCombatHit(target, attacker) == true ? behavior : null;
        }

        private static void ApplyHitHealth(Agent target, float nativeNextHealth, ref Blow blow)
        {
            var behavior = target.Mission?.GetMissionBehavior<ReignDuelMissionBehavior>();
            if (behavior?.HasActiveCombatDuel != true)
            {
                target.Health = nativeNextHealth;
                return;
            }
            Agent attacker = blow.OwnerId < 0 ? null : target.Mission.FindAgentWithIndex(blow.OwnerId);
            target.Health = !behavior.OwnsCombatHit(target, attacker) ? nativeNextHealth
                : ReignDuelModePolicy.HealthAfterHit(target.Health, nativeNextHealth, blow.InflictedDamage);
        }

        private static bool NativeBattleHit(Agent affectedAgent, Agent affectorAgent)
            => ForHit(affectedAgent, affectorAgent) == null;

        private static void CombatNotice(Agent attackerAgent, Agent victimAgent,
            ref AttackCollisionData attackCollisionData, ref CombatLogData combatLog)
        {
            if (ForHit(victimAgent, attackerAgent) == null || attackCollisionData.AttackBlockedWithShield) return;
            combatLog.InflictedDamage = TaleWorlds.Library.MathF.Round(combatLog.InflictedDamage * ReignDuelModePolicy.DamageMultiplier);
            combatLog.ModifiedDamage = TaleWorlds.Library.MathF.Round(combatLog.ModifiedDamage * ReignDuelModePolicy.DamageMultiplier);
            combatLog.IsFatalDamage = victimAgent.Health - attackCollisionData.InflictedDamage * ReignDuelModePolicy.DamageMultiplier < 1f;
        }

        private static void ScaleSkillXp(Hero __instance, ref float xpAmount)
        {
            ExperienceScope scope = _experienceScope;
            if (scope != null && ReferenceEquals(scope.Hero, __instance))
                xpAmount = ReignDuelModePolicy.ScaleExperience(xpAmount, scope.Blocked);
        }

        internal sealed class ExperienceScope : IDisposable
        {
            private readonly ExperienceScope _previous;
            internal readonly Hero Hero;
            internal readonly bool Blocked;
            internal ExperienceScope(Hero hero, bool blocked)
            { Hero = hero; Blocked = blocked; _previous = _experienceScope; _experienceScope = this; }
            public void Dispose() { _experienceScope = _previous; }
        }

        internal static AttackCollisionData ReferenceCollision(AttackCollisionData collision)
        {
            AttackCollisionData reference = collision;
            ShieldBlock(ref reference) = false; WeaponBlock(ref reference) = false;
            ShieldOnBack(ref reference) = false; ColliderAgent(ref reference) = true;
            CollisionResult(ref reference) = (int)CombatCollisionResult.StrikeAgent;
            return reference;
        }

        internal static float BlockedReferenceDamage(Agent attacker, Agent target, in AttackCollisionData collision,
            out float movementSpeedDamageModifier)
        {
            // Recalculate a copy as an unblocked body hit using the native attack
            // physics, armor and damage models. Never apply this hypothetical hit.
            AttackCollisionData reference = ReferenceCollision(collision);
            int slot = collision.AffectorWeaponSlotOrMissileIndex;
            MissionWeapon weapon = collision.IsMissile
                ? Missiles(attacker.Mission)[slot].Weapon
                : slot >= 0 && slot < 4 ? attacker.Equipment[slot] : MissionWeapon.Invalid;
            var information = new AttackInformation(attacker, target, WeakGameEntity.Invalid, in reference, in weapon);
            MissionCombatMechanicsHelper.GetAttackCollisionResults(in information, false, 1f, false,
                ref reference, out CombatLogData _, out int _);
            movementSpeedDamageModifier = reference.MovementSpeedDamageModifier;
            return Math.Max(0, TaleWorlds.Library.MathF.Round(
                MissionGameModels.Current.AgentApplyDamageModel.CalculateDamage(in information, in reference, reference.InflictedDamage)));
        }
    }
}
