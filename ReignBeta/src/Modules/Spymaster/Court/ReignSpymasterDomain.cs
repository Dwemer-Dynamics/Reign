using System;
using System.Collections.Generic;
using ReignShared.Spymaster;

namespace ReignBeta.Court
{
    public enum ReignSpymasterPage
    {
        Intelligence,
        Subterfuge,
        Reputation,
        Archive
    }

    public enum ReignSpymasterMissionState
    {
        Active,
        Succeeded,
        Failed,
        Exposed,
        Cancelled
    }

    public sealed class ReignSpymasterMission
    {
        public string MissionId = "spy_" + Guid.NewGuid().ToString("N");
        public string SpymasterHeroStringId = string.Empty;
        public string MissionType = string.Empty;
        public string TargetType = string.Empty;
        public string TargetStringId = string.Empty;
        public string TargetName = string.Empty;
        public string SponsorKingdomStringId = string.Empty;
        public int GoldCost;
        public int Roguery;
        public int ClanTier;
        public bool TargetIsRuler;
        public float StartedDay;
        public float DueDay;
        public float ResolvedDay = -1f;
        public float SuccessChance;
        public float DetectionChance;
        public bool Harmful;
        public bool TargetNoticed;
        public bool PlayerIdentified;
        public bool AttributionPending;
        public ReignSpymasterMissionState State;
        public string RequestSummary = string.Empty;
        public string ResultSummary = string.Empty;
        public string ReportJson = "{}";
        public string SocialItemId = string.Empty;
        public string FabricatedText = string.Empty;
        public float OutcomeRoll = -1f;
        public float DetectionRoll = -1f;
        public float AttributionRoll = -1f;
        public float ConsequenceRoll = -1f;
        public float BreakoutChance = -1f;
        public float BreakoutRoll = -1f;
        public List<string> History = new List<string>();
    }

    public sealed class ReignSpymasterSettlementEffect
    {
        public string EffectId = "spy_effect_" + Guid.NewGuid().ToString("N");
        public string SettlementStringId = string.Empty;
        public string EffectType = string.Empty;
        public string SourceKingdomStringId = string.Empty;
        public string AgentHeroStringId = string.Empty;
        public float StartDay;
        public float EndDay;
        public float Magnitude;
    }

    public sealed class ReignForeignAgentRecord
    {
        public string AgentHeroStringId = string.Empty;
        public string SponsorKingdomStringId = string.Empty;
        public string SettlementStringId = string.Empty;
        public bool IsNotable;
        public bool Activated;
        public bool Exposed;
        public float ExposedDay = -1f;
        public float RecruitedDay;
        public float NextActionDay;
    }

    public sealed class ReignForeignAgentActionRecord
    {
        public string ActionId = string.Empty;
        public string AgentHeroStringId = string.Empty;
        public string SponsorKingdomStringId = string.Empty;
        public string SettlementStringId = string.Empty;
        public string EffectType = string.Empty;
        public float WorldDay;
        public float DurationDays;
        public float Magnitude;
        public bool Detected;
        public bool SponsorAttributed;
        public string Summary = string.Empty;
    }

    public sealed class ReignSpymasterOrganicBatchRecord
    {
        public string BatchId = string.Empty;
        public string Label = string.Empty;
        public float CapturedDay;
        public int AgentsBefore;
        public int ActionsBefore;
        public int EffectsBefore;
        public List<string> MissionIds = new List<string>();
        public string PreSnapshotJson = "{}";
    }

    public sealed class ReignSpymasterOrganicTestLedger
    {
        public int SchemaVersion = 1;
        public string RunId = string.Empty;
        public string CampaignId = string.Empty;
        public string SpymasterHeroStringId = string.Empty;
        public float StartedDay;
        public float LastObservedDay;
        public float PreviousAgentRecruitmentDay;
        public int StartingGold;
        public bool DisposableSaveConfirmed;
        public string PlayerKingdomStringId = string.Empty;
        public string PlayerSettlementStringId = string.Empty;
        public string ForeignSettlementStringId = string.Empty;
        public string ForeignHeroStringId = string.Empty;
        public List<string> InitialMissionIds = new List<string>();
        public List<string> InitialAgentHeroIds = new List<string>();
        public List<string> InitialActionIds = new List<string>();
        public List<string> InitialEffectIds = new List<string>();
        public List<string> MissionIds = new List<string>();
        public List<ReignSpymasterOrganicBatchRecord> Batches = new List<ReignSpymasterOrganicBatchRecord>();
        public List<string> ObservationJson = new List<string>();
    }

    public sealed class ReignSpymasterState
    {
        public int Version = 4;
        public float LastAgentRecruitmentDay = -1000f;
        public List<ReignSpymasterMission> Missions = new List<ReignSpymasterMission>();
        public List<ReignSpymasterSettlementEffect> Effects = new List<ReignSpymasterSettlementEffect>();
        public List<ReignForeignAgentRecord> ForeignAgents = new List<ReignForeignAgentRecord>();
        public List<ReignForeignAgentActionRecord> ForeignAgentActions = new List<ReignForeignAgentActionRecord>();
        public List<string> AppliedForeignAgentActionIds = new List<string>();
        public List<ReignSpymasterOrganicTestLedger> OrganicTestLedgers = new List<ReignSpymasterOrganicTestLedger>();
    }

    public sealed class ReignSpymasterMissionQuote
    {
        public int GoldCost;
        public float DurationDays;
        public float SuccessChance;
        public float DetectionChance;
        public bool Harmful;
        public string DifficultyLabel = string.Empty;
    }

    public static class ReignSpymasterBalance
    {
        public const int MissionCapacity = ReignSpymasterCore.MissionCapacity;

        public static float ClanMultiplier(int tier)
        {
            return (float)ReignSpymasterCore.ClanMultiplier(tier);
        }

        public static float DetectionChance(float baseNoticePercent, int roguery)
        {
            return (float)ReignSpymasterCore.DetectionChance(baseNoticePercent, roguery);
        }

        public static float RecruitmentChance(float loyalty)
        {
            return (float)ReignSpymasterCore.RecruitmentChance(loyalty);
        }

        public static ReignSpymasterMissionQuote Quote(string type, int roguery, int clanTier, bool ruler)
        {
            ReignSpymasterCoreQuote quote = ReignSpymasterCore.Quote(type, roguery, clanTier, ruler);
            return new ReignSpymasterMissionQuote
            {
                GoldCost = quote.GoldCost,
                DurationDays = (float)quote.DurationDays,
                SuccessChance = (float)quote.SuccessChance,
                DetectionChance = (float)quote.DetectionChance,
                Harmful = quote.Harmful,
                DifficultyLabel = quote.DifficultyLabel
            };
        }
    }
}
