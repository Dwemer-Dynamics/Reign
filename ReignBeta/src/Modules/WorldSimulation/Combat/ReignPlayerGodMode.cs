using System;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.World
{
    // Mission-local only: never persisted to a hero, campaign, save or RBM setting.
    internal sealed class ReignPlayerGodMode : MissionLogic
    {
        private Agent _protectedAgent;
        private Agent.MortalityState _previousMortality;

        internal static bool Attach(Mission mission)
        {
            if (mission == null || mission.GetMissionBehavior<ReignPlayerGodMode>() != null) return false;
            mission.AddMissionBehavior(new ReignPlayerGodMode());
            return true;
        }

        public override void OnMissionTick(float dt)
        {
            Agent player = Mission?.MainAgent;
            if (GameNetwork.IsMultiplayer || !ReferenceEquals(Mission, TaleWorlds.MountAndBlade.Mission.Current)
                || !IsCombatMode(Mission?.Mode) || player == null || !player.IsHuman
                || !player.IsPlayerControlled || !player.IsActive()
                || !(player.Health > 0f) || float.IsInfinity(player.Health))
            {
                ReleaseWithNotice();
                return;
            }

            // A replacement player agent must explicitly opt in again. Do not transfer
            // protection to a mount, spectator target or another controlled soldier.
            if (_protectedAgent != null && !ReferenceEquals(_protectedAgent, player)) ReleaseWithNotice();
            if (_protectedAgent != null && player.CurrentMortalityState != Agent.MortalityState.Invulnerable)
                ReleaseWithNotice(); // Respect another system taking ownership of mortality.

            bool alt = Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt);
            if (!alt || !Input.IsKeyPressed(InputKey.Equals)
                || Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)
                || Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift)) return;

            if (_protectedAgent == null)
            {
                if (!ReignPlayerGodModeHealthPatch.TryApply())
                {
                    InformationManager.DisplayMessage(new InformationMessage("Reign player god mode could not be enabled. See the Reign log."));
                    return;
                }
                _previousMortality = player.CurrentMortalityState;
                _protectedAgent = player;
                player.SetMortalityState(Agent.MortalityState.Invulnerable);
                ReignPlayerGodModeHealthPatch.Activate(this);
                InformationManager.DisplayMessage(new InformationMessage("Reign player god mode: ON (Alt+= to turn off)."));
            }
            else
            {
                Release();
                InformationManager.DisplayMessage(new InformationMessage("Reign player god mode: OFF."));
            }
        }

        private static bool IsCombatMode(MissionMode? mode)
            => mode == MissionMode.Battle || mode == MissionMode.Duel
                || mode == MissionMode.Tournament || mode == MissionMode.Stealth;

        internal bool Protects(Agent agent)
            => _protectedAgent != null && ReferenceEquals(agent, _protectedAgent)
                && !GameNetwork.IsMultiplayer && ReferenceEquals(Mission?.MainAgent, agent)
                && ReferenceEquals(Mission, TaleWorlds.MountAndBlade.Mission.Current)
                && IsCombatMode(Mission?.Mode) && agent.IsHuman && agent.IsPlayerControlled
                && agent.CurrentMortalityState == Agent.MortalityState.Invulnerable;

        private void ReleaseWithNotice()
        {
            if (_protectedAgent == null) return;
            Release();
            InformationManager.DisplayMessage(new InformationMessage(
                "Reign player god mode: OFF (the scene or player state changed)."));
        }

        internal void Release()
        {
            ReignPlayerGodModeHealthPatch.Deactivate(this);
            Agent previous = _protectedAgent;
            _protectedAgent = null;
            if (previous != null && previous.CurrentMortalityState == Agent.MortalityState.Invulnerable)
                previous.SetMortalityState(_previousMortality);
        }

        protected override void OnEndMission()
        {
            Release();
            base.OnEndMission();
        }

        public override void OnRemoveBehavior()
        {
            Release();
            base.OnRemoveBehavior();
        }
    }

    // Some combat mods write Health directly (for example RBM weapon self-damage),
    // bypassing the engine's collision-side invulnerability checks.
    internal static class ReignPlayerGodModeHealthPatch
    {
        private const string Owner = "com.bannerlordreign.player-god-mode.v1";
        private static bool _installed;
        private static ReignPlayerGodMode _active;

        internal static void Activate(ReignPlayerGodMode behavior) { _active = behavior; }
        internal static void Deactivate(ReignPlayerGodMode behavior)
        {
            if (ReferenceEquals(_active, behavior)) _active = null;
        }

        internal static bool TryApply()
        {
            if (_installed) return true;
            var harmony = new Harmony(Owner);
            try
            {
                // Query by name without touching ActionIndexCache or Agent.
                // An early caller must not permanently cache -1 animation IDs.
                if (MBAnimation.GetActionCodeWithName("act_inventory_idle_start") < 0) return false;
                var setter = AccessTools.PropertySetter(typeof(Agent), nameof(Agent.Health));
                if (setter == null) throw new MissingMethodException(typeof(Agent).FullName, "set_Health");
                harmony.Patch(setter, prefix: new HarmonyMethod(
                    typeof(ReignPlayerGodModeHealthPatch), nameof(GuardHealth)) { priority = Priority.Last });
                _installed = true;
                return true;
            }
            catch (Exception ex)
            {
                try { harmony.UnpatchAll(Owner); }
                catch (Exception cleanup) { ReignLog.Exception("Player god-mode hook cleanup failed", cleanup); }
                ReignLog.Exception("Player god-mode health guard could not be installed", ex);
                return false;
            }
        }

        internal static void Unapply()
        {
            _active?.Release();
            if (!_installed) return;
            new Harmony(Owner).UnpatchAll(Owner);
            _installed = false;
        }

        internal static void GuardHealth(Agent __instance, ref float value)
        {
            // One null/identity fast path while idle; no per-agent behavior scans.
            if (__instance == null || _active?.Protects(__instance) != true) return;
            float current = __instance.Health;
            if (value < current || float.IsNaN(value)) value = current;
        }
    }
}
