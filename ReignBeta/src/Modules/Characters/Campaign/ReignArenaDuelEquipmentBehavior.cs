using System;
using System.Collections.Generic;
using ReignBeta.Integration;
using SandBox.Missions.MissionLogics.Arena;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    // Native arena duels use personal battle equipment, which civilians may lack.
    // Borrow a native arena weapon on the spawned agent only; never alter the hero.
    internal sealed class ReignArenaDuelEquipmentBehavior : MissionLogic
    {
        private readonly CharacterObject _opponent;
        private readonly EquipmentElement _practiceWeapon;
        private bool _finished;
        private float _waitSeconds;
        private readonly List<AgentRemovalObservation> _removals = new List<AgentRemovalObservation>();

        internal IReadOnlyList<AgentRemovalObservation> Removals => _removals;

        internal sealed class AgentRemovalObservation
        {
            internal string HeroId { get; set; }
            internal int AgentIndex { get; set; }
            internal string State { get; set; }
            internal float Health { get; set; }
            internal string AffectorHeroId { get; set; }
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow killingBlow)
        {
            if (_removals.Count >= 2 || affectedAgent?.Character == null
                || (affectedAgent.Character != CharacterObject.PlayerCharacter && affectedAgent.Character != _opponent))
                return;

            _removals.Add(new AgentRemovalObservation {
                HeroId = affectedAgent.Character.StringId, AgentIndex = affectedAgent.Index,
                State = agentState.ToString(), Health = affectedAgent.Health,
                AffectorHeroId = affectorAgent?.Character?.StringId ?? string.Empty });
            ReignLog.Info("Native arena duel agent removed hero=" + affectedAgent.Character.StringId
                + " state=" + agentState + " health=" + affectedAgent.Health
                + " affector=" + (affectorAgent?.Character?.StringId ?? string.Empty));
        }

        internal ReignArenaDuelEquipmentBehavior(CharacterObject opponent, IEnumerable<Equipment> presets)
        {
            _opponent = opponent;
            if (presets == null)
            {
                return;
            }

            foreach (Equipment preset in presets)
            {
                for (int slot = 0; slot < 4; slot++)
                {
                    EquipmentElement element = preset[(EquipmentIndex)slot];
                    if (element.Item?.PrimaryWeapon?.IsMeleeWeapon == true)
                    {
                        _practiceWeapon = element;
                        return;
                    }
                }
            }
        }

        public override void OnMissionTick(float dt)
        {
            if (_finished)
            {
                return;
            }

            _waitSeconds += dt;
            if (Mission.GetMissionBehavior<ArenaDuelMissionController>() == null || _waitSeconds > 30f)
            {
                _finished = true;
                return;
            }

            Agent player = null;
            Agent opponent = null;
            foreach (Agent agent in Mission.Agents)
            {
                if (!agent.IsActive())
                {
                    continue;
                }

                if (agent.Character == CharacterObject.PlayerCharacter)
                {
                    player = agent;
                }
                else if (agent.Character == _opponent)
                {
                    opponent = agent;
                }
            }

            if (player == null || opponent == null)
            {
                return;
            }

            _finished = true;
            try
            {
                SupplyMissingMeleeWeapon(player);
                SupplyMissingMeleeWeapon(opponent);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Native arena duel practice weapon setup failed: " + ex);
            }
        }

        private void SupplyMissingMeleeWeapon(Agent agent)
        {
            Equipment source = agent.SpawnEquipment;
            for (int slot = 0; slot < 4; slot++)
            {
                if (source[(EquipmentIndex)slot].Item?.PrimaryWeapon?.IsMeleeWeapon == true)
                {
                    return;
                }
            }

            if (_practiceWeapon.IsEmpty)
            {
                ReignLog.Warn("Native arena duel has no cultural melee practice weapon for " + agent.Character.StringId);
                return;
            }

            var equipment = new Equipment();
            equipment.FillFrom(source);
            EquipmentIndex targetSlot = EquipmentIndex.Weapon0;
            for (int slot = 0; slot < 4; slot++)
            {
                if (equipment[(EquipmentIndex)slot].IsEmpty)
                {
                    targetSlot = (EquipmentIndex)slot;
                    break;
                }
            }

            equipment[targetSlot] = _practiceWeapon;
            agent.UpdateSpawnEquipmentAndRefreshVisuals(equipment);
            agent.WieldInitialWeapons();
            ReignLog.Info("Native arena duel borrowed practice weapon hero=" + agent.Character.StringId + " item=" + _practiceWeapon.Item.StringId);
        }
    }
}
