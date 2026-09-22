using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Save;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace ReignBeta.Court
{
    public sealed class FamilyChambersSessionRecord
    {
        [SaveableField(1)] public string SessionId;
        [SaveableField(2)] public string CampaignId;
        [SaveableField(3)] public string TimelineId;
        [SaveableField(4)] public string SettlementStringId;
        [SaveableField(5)] public float CreatedDay;
        [SaveableField(6)] public string AdultHeroIdsCsv;
        [SaveableField(7)] public string ChildHeroIdsCsv;
        [SaveableField(8)] public CastleRoomSessionRecord ChatRecord;
        [SaveableField(9)] public long Revision;

        public FamilyChambersSessionRecord()
        {
            SessionId = "family_chambers_" + Guid.NewGuid().ToString("N");
            CampaignId = string.Empty;
            TimelineId = "main";
            SettlementStringId = string.Empty;
            AdultHeroIdsCsv = string.Empty;
            ChildHeroIdsCsv = string.Empty;
            ChatRecord = new CastleRoomSessionRecord();
        }
    }

    public sealed class ReignFamilyChambersCampaignBehavior : CampaignBehaviorBase
    {
        private List<FamilyChambersSessionRecord> _sessions = new List<FamilyChambersSessionRecord>();
        private List<string> _maturedChildHeroIds = new List<string>();

        public static ReignFamilyChambersCampaignBehavior Instance { get; private set; }

        public ReignFamilyChambersCampaignBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickHeroEvent.AddNonSerializedListener(this, OnDailyTickHero);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reign_family_chambers_sessions", ref _sessions);
            dataStore.SyncData("_reign_family_chambers_matured_children", ref _maturedChildHeroIds);
            _sessions = _sessions ?? new List<FamilyChambersSessionRecord>();
            _maturedChildHeroIds = _maturedChildHeroIds ?? new List<string>();
        }


        public IReadOnlyList<Hero> GetPresentAdults()
        {
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            if (settlement == null) return Array.Empty<Hero>();
            return ReignPartyChatSession.GetAvailableConversationHeroes()
                .Concat(ReignPartyChatSession.GetMainPartyHeroes())
                .Where(hero => hero != null && hero != Hero.MainHero && hero.IsLord
                    && hero.IsAlive && hero.IsActive && !hero.IsChild && !hero.IsPrisoner)
                .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(hero => hero.Name?.ToString() ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<Hero> GetQualifiedChildren(IEnumerable<Hero> adults)
        {
            float adultAge = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            List<Hero> presentAdults = (adults ?? Enumerable.Empty<Hero>()).Where(parent => parent != null).ToList();
            var presentAdultIds = new HashSet<string>(presentAdults.Select(parent => parent.StringId), StringComparer.OrdinalIgnoreCase);
            IEnumerable<Hero> linkedChildren = presentAdults.SelectMany(parent => parent.Children ?? new List<Hero>())
                .Concat(Hero.AllAliveHeroes.Where(child => child != null
                    && (presentAdultIds.Contains(child.Father?.StringId ?? string.Empty)
                        || presentAdultIds.Contains(child.Mother?.StringId ?? string.Empty))));
            return linkedChildren
                .Where(child => child != null && child.IsAlive && child.Age < adultAge)
                .GroupBy(child => child.StringId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(child => child.Age)
                .ThenBy(child => child.Name?.ToString() ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public FamilyChambersSessionRecord CreateSession(IEnumerable<Hero> selected)
        {
            List<Hero> participants = (selected ?? Enumerable.Empty<Hero>())
                .Where(hero => hero != null)
                .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).Take(4).ToList();
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            float day = (float)(TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays);
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string timelineId = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            string childIds = string.Join(",", participants.Where(hero => hero.IsChild).Select(hero => hero.StringId));
            string adultIds = string.Join(",", participants.Where(hero => !hero.IsChild).Select(hero => hero.StringId));
            string stable = campaignId + "|" + timelineId + "|" + (settlement?.StringId ?? "") + "|" + day.ToString("0.000")
                + "|" + string.Join(",", participants.Select(hero => hero.StringId));
            string sessionId = "family_chambers_" + CastleChat.CastleScheduleEngine.StableHash(
                campaignId + "|" + timelineId, (int)Math.Floor(day), CastleChat.CastleScheduleEngine.GetTimeBlock(CampaignTime.Now.ToHours % 24d),
                settlement?.StringId ?? string.Empty, stable).ToString("x8");
            // Reopening the same audience must preserve its transcript and turn receipts.
            // Older saves may contain duplicate records; the latest is the last audience shown.
            var existing = _sessions.LastOrDefault(item => item != null && item.ChatRecord != null
                && item.SessionId == sessionId && item.CampaignId == campaignId && item.TimelineId == timelineId
                && item.SettlementStringId == (settlement?.StringId ?? string.Empty)
                && item.AdultHeroIdsCsv == adultIds && item.ChildHeroIdsCsv == childIds);
            if (existing != null) return existing;
            var chat = new CastleRoomSessionRecord
            {
                SessionKey = sessionId,
                CampaignId = campaignId,
                TimelineId = timelineId,
                SettlementStringId = settlement?.StringId ?? string.Empty,
                CultureId = ReignCourtCampaignBehavior.NormalizeCastleCulture(settlement?.Culture?.StringId),
                CampaignDay = (int)Math.Floor(day),
                TimeBlock = (int)CastleChat.CastleScheduleEngine.GetTimeBlock(CampaignTime.Now.ToHours % 24d),
                Room = (int)CastleChat.CastleRoom.NobleSolar,
                OccupantHeroIdsCsv = string.Join(",", participants.Select(hero => hero.StringId)),
                ChildHeroIdsCsv = childIds,
                InteractionMode = "family_chambers",
                DisplayName = "Family Chambers",
                ServerConversationSessionId = sessionId,
                DialoguePromptSnapshot = ReignFamilyChambersPrompt.BuildDialogueContract(participants),
                PromptRevision = "family_chambers_v1"
            };
            var record = new FamilyChambersSessionRecord
            {
                SessionId = sessionId,
                CampaignId = campaignId,
                TimelineId = timelineId,
                SettlementStringId = settlement?.StringId ?? string.Empty,
                CreatedDay = day,
                AdultHeroIdsCsv = adultIds,
                ChildHeroIdsCsv = childIds,
                ChatRecord = chat
            };
            _sessions.Add(record);
            _sessions.RemoveAll(item => item == null || item.CreatedDay < day - 30f);
            return record;
        }

        private async void OnDailyTickHero(Hero hero)
        {
            if (hero == null || hero.IsChild || !hero.IsAlive || string.IsNullOrWhiteSpace(hero.StringId)) return;
            bool participated = _sessions.Any(session => ReignCourtCampaignBehavior.SplitIds(session?.ChildHeroIdsCsv)
                .Contains(hero.StringId, StringComparer.OrdinalIgnoreCase))
                || ReignCourtCampaignBehavior.Instance?.HasUnmigratedChildAttention(hero) == true;
            if (!participated || _maturedChildHeroIds.Contains(hero.StringId, StringComparer.OrdinalIgnoreCase)) return;
            try
            {
                bool ok = await ReignServerClient.ReconcileChildhoodMaturityAsync(hero).ConfigureAwait(false);
                if (ok) _maturedChildHeroIds.Add(hero.StringId);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Family Chambers coming-of-age reconciliation failed for " + hero.StringId + ": " + ex.Message);
            }
        }
    }
}
