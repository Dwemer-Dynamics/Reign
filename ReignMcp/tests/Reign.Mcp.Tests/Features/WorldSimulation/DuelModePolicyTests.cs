using ReignBeta.Campaign;

namespace Reign.Mcp.Tests;

public sealed class DuelModePolicyTests
{
    [Theory]
    [InlineData("player", "companion", true)]
    [InlineData("companion", "player", true)]
    [InlineData("player", "bystander", false)]
    [InlineData("bystander", "player", false)]
    [InlineData("companion", "bystander", false)]
    [InlineData("player", "player", false)]
    [InlineData("companion", "companion", false)]
    [InlineData(null, "player", false)]
    [InlineData("companion", null, false)]
    public void DuelExperienceBelongsOnlyToHitsBetweenBothParticipants(string? attacker, string? target, bool expected)
    {
        Assert.Equal(expected, ReignDuelModePolicy.IsExperiencePair("player", "companion", attacker!, target!));
        Assert.False(ReignDuelModePolicy.IsExperiencePair("player", "player", attacker!, target!));
        Assert.False(ReignDuelModePolicy.IsExperiencePair("player", "", attacker!, target!));
        Assert.False(ReignDuelModePolicy.IsExperiencePair(null!, "companion", attacker!, target!));
    }

    [Fact]
    public void EveryDuelLaunchEnablesNativeFullCombatExperienceWithoutASecondBattleAward()
    {
        string root = TestOptions.FindWorkspace();
        string mission = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignDuelMissionBehavior.cs"));
        string launch = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Dialogue/Campaign/ReignDuelLaunchPromptService.cs"));
        string service = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignDuelService.cs"));
        Assert.Contains("ReignDuelMissionBehavior.EnableCombatExperience(duelMission, intent);", launch);
        Assert.Equal(2, mission.Split("EnableCombatExperience(Mission, intent);").Length - 1);
        string hit = mission.Substring(mission.IndexOf("public override void OnScoreHit", StringComparison.Ordinal));
        hit = hit.Substring(0, hit.IndexOf("public static bool TryStartFromCurrentMission", StringComparison.Ordinal));
        Assert.Contains("ReignDuelService.IsActiveDuel(_experienceIntent.ActionId)", hit);
        Assert.Contains("Mission.GetMissionBehavior<BattleAgentLogic>() != null", hit);
        Assert.Contains("isBlocked || blow.InflictedDamage <= 0", hit);
        Assert.Contains("ReignDuelModePolicy.IsExperiencePair(", hit);
        Assert.Contains("CombatXpModel.MissionTypeEnum.Battle", hit);
        Assert.Equal(1, hit.Split("SkillLevelingManager.OnCombatHit(").Length - 1);
        Assert.DoesNotContain("MissionTypeEnum.PracticeFight", hit);
        Assert.DoesNotContain("AddSkillXp(", hit);
        Assert.DoesNotContain("IsPractice(", hit);
        Assert.Contains("Active.Contains(actionId) && Pending.ContainsKey(actionId)", service);
    }

    [Theory]
    [InlineData("practice", false)]
    [InlineData("practice", true)]
    [InlineData(" PRACTICE ", true)]
    public void PracticeCannotEscalateThroughLegacyDeathTerms(string requested, bool staleLethal)
    {
        string mode = ReignDuelModePolicy.Resolve(requested, staleLethal);
        Assert.Equal("practice", mode);
        Assert.True(ReignDuelModePolicy.IsPractice(mode));
        Assert.False(ReignDuelModePolicy.IsLethal(mode));
        Assert.Equal("practice duel", ReignDuelModePolicy.DisplayName(mode));
    }

    [Fact]
    public void PracticeIsTheThirdSelectableMode()
    {
        var mode = Assert.Single(Reign.Core.Contracts.Actions.ReignActionCatalog.Find("duel_player")!.Parameters);
        Assert.Equal("terms.duelMode", mode.Key);
        Assert.Equal(new[] { "practice", "honor", "lethal" }, mode.Choices);
    }

    [Fact]
    public void BothMissionCompletionPathsExcludePracticeFromSocialHistory()
    {
        string root = TestOptions.FindWorkspace();
        string service = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignDuelService.cs"));
        string mission = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignDuelMissionBehavior.cs"));
        string social = service.Substring(service.IndexOf("internal static void RecordDuelSocialOutcome", StringComparison.Ordinal));
        social = social.Substring(0, social.IndexOf("public static bool HasPendingForOpponent", StringComparison.Ordinal));
        Assert.Contains("if (intent == null || ReignDuelModePolicy.IsPractice(intent.Mode)) return;", social);
        Assert.True(social.IndexOf("IsPractice(intent.Mode)", StringComparison.Ordinal) < social.IndexOf("RecordSocialOutcome(", StringComparison.Ordinal));
        Assert.Contains("RecordDuelSocialOutcome(intent, resolvedWinner, loserHero, finishReason)", service);
        Assert.Contains("RecordDuelSocialOutcome(finished, winnerHero, loserHero, finishReason)", mission);
        Assert.Contains("? \"practice\" : ResolvePurpose", service);
        foreach (string consumer in new[] { service, mission })
        {
            Assert.Contains("\"practice_duel_completed\"", consumer);
            Assert.Contains("in a private practice duel.", consumer);
        }
    }

    [Theory]
    [InlineData("honor", false)]
    [InlineData("honor", true)]
    [InlineData("honour", false)]
    [InlineData(" HONOUR ", true)]
    public void AgreedHonorNeverEscalatesToDeath(string requested, bool staleLethal)
    {
        string mode = ReignDuelModePolicy.Resolve(requested, staleLethal);
        Assert.Equal("honor", mode);
        Assert.False(ReignDuelModePolicy.IsLethal(mode));
        Assert.Equal("duel of honor", ReignDuelModePolicy.DisplayName(mode));
    }

    [Theory]
    [InlineData("lethal", true)]
    [InlineData("lethal", false)]
    [InlineData("death", false)]
    [InlineData("to_the_death", false)]
    [InlineData("TO THE DEATH", false)]
    [InlineData("to-the-death", false)]
    public void DeathModeCannotBypassViolenceGateWithFalseLegacyFlag(string requested, bool legacy)
    {
        string mode = ReignDuelModePolicy.Resolve(requested, legacy);
        Assert.Equal("lethal", mode);
        Assert.True(ReignDuelModePolicy.IsLethal(mode));
        Assert.Equal("duel to the death", ReignDuelModePolicy.DisplayName(mode));
    }

    [Theory]
    [InlineData(null, false, "training")]
    [InlineData("", false, "training")]
    [InlineData("unknown", false, "training")]
    [InlineData(null, true, "lethal")]
    [InlineData("", true, "lethal")]
    [InlineData("training", true, "training")]
    public void LegacyDefaultsPreserveExplicitNonlethalTerms(string? requested, bool legacy, string expected)
    {
        Assert.Equal(expected, ReignDuelModePolicy.Resolve(requested!, legacy));
    }

    [Fact]
    public void CivilianArenaEquipmentFallbackIsLimitedToLiveDuelAgents()
    {
        string root = TestOptions.FindWorkspace();
        string launch = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Dialogue/Campaign/ReignDuelLaunchPromptService.cs"));
        string equipment = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignArenaDuelEquipmentBehavior.cs"));
        Assert.Contains("Mission duelMission = (Mission)CampaignMission.OpenArenaDuelMission(", launch);
        Assert.Contains("duelMission.AddMissionBehavior(new ReignArenaDuelEquipmentBehavior(", launch);
        Assert.Contains("DuelPresetEquipmentRoster?.AllEquipments", launch);
        Assert.Contains("Mission.GetMissionBehavior<ArenaDuelMissionController>()", equipment);
        Assert.Contains("agent.Character == _opponent", equipment);
        Assert.Contains("source[(EquipmentIndex)slot].Item?.PrimaryWeapon?.IsMeleeWeapon == true", equipment);
        Assert.Contains("equipment.FillFrom(source)", equipment);
        Assert.Contains("agent.UpdateSpawnEquipmentAndRefreshVisuals(equipment)", equipment);
        foreach (string forbidden in new[] { "BattleEquipment.FillFrom", "CivilianEquipment.FillFrom", ".Health =", "SetMissionMode(", "CompleteDuel" })
        {
            Assert.DoesNotContain(forbidden, equipment);
        }
    }

    [Fact]
    public void ArenaRemovalEvidenceObservesOnlyTheTwoDuelistsWithoutCompletingOrDamagingThem()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/Characters/Campaign/ReignArenaDuelEquipmentBehavior.cs"));
        string observer = source.Substring(source.IndexOf("public override void OnAgentRemoved", StringComparison.Ordinal));
        observer = observer.Substring(0, observer.IndexOf("internal ReignArenaDuelEquipmentBehavior", StringComparison.Ordinal));
        Assert.Contains("_removals.Count >= 2", observer);
        Assert.Contains("affectedAgent.Character != CharacterObject.PlayerCharacter && affectedAgent.Character != _opponent", observer);
        Assert.Contains("State = agentState.ToString()", observer);
        Assert.Contains("Health = affectedAgent.Health", observer);
        Assert.Contains("affectorAgent?.Character?.StringId", observer);
        foreach (string forbidden in new[] { ".Health =", "RegisterBlow(", "CompleteDuel", "EndMission(", "SetMissionMode(" })
            Assert.DoesNotContain(forbidden, observer);
    }

    [Fact]
    public void CompletedArenaExitKeepsNativeVetoAndExactDisposableIdentityGates()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/Characters/Campaign/ReignEncounteredResidentNativeTestHost.cs"));
        Assert.Contains("leave && missionKind == \"completed_arena_duel\"", source);
        Assert.Contains("Unknown native scene operation kind", source);
        Assert.Contains("leave completed arena duel on enrolled disposable save", source);
        Assert.Contains("(!arenaExit && (Agent.Main == null || !Agent.Main.IsActive()))", source);
        Assert.Contains("GetMissionBehavior<SandBox.Missions.MissionLogics.Arena.ArenaDuelMissionController>() == null", source);
        Assert.Contains("GetMissionBehavior<ReignArenaDuelEquipmentBehavior>() == null", source);
        Assert.Contains("logic.OnEndMissionRequest(out bool canLeave)", source);
        Assert.Contains("if (!canLeave || inquiry != null)", source);
        Assert.True(source.IndexOf("if (!canLeave || inquiry != null)", StringComparison.Ordinal)
            < source.IndexOf("Mission.Current.EndMission()", StringComparison.Ordinal));
        foreach (string gate in new[] { "_armed", "expectedGameInstanceId", "IsReadyForCampaign", "IsAlignmentPending", "save != baseline", "campaign.TimeControlMode != CampaignTimeControlMode.Stop", "IsConversationInProgress", "MapEvent != null" })
            Assert.Contains(gate, source);
        Assert.DoesNotContain("CompleteDuel", source);
        Assert.DoesNotContain("RegisterBlow", source);
    }

    [Fact]
    public void ProposalAndExecutionUseTheSamePolicyAndKeepNonlethalDeathRiskZero()
    {
        string root = TestOptions.FindWorkspace();
        string service = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignDuelService.cs"));
        string validator = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/World/ReignActionValidator.cs"));
        foreach (string consumer in new[] { service, validator })
        {
            Assert.Contains("ReignDuelModePolicy.Resolve(", consumer);
            Assert.Contains("ReignDuelModePolicy.IsLethal(mode)", consumer);
            Assert.DoesNotContain("mode == \"honor\" || mode == \"honour\"", consumer);
        }
        Assert.Contains("AllowPlayerDeath = lethal &&", service);
        Assert.Contains("TargetDeathRiskPercent = lethal ?", service);
        Assert.Contains("PlayerDeathRiskPercent = lethal ?", service);
        Assert.Contains("public const int LethalDuelPlayerRelationThreshold = -80;", validator);
    }
}
