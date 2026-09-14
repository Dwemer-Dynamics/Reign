using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Government
{
    public sealed class ReignGovernmentAttendanceRecord
    {
        public string BusinessId { get; set; } = "";
        public string HeroStringId { get; set; } = "";
        public string Status { get; set; } = "absent";
        public string Reason { get; set; } = "";
        public string HostSettlementStringId { get; set; } = "";
        public string OriginalSettlementStringId { get; set; } = "";
        public string HomeSettlementStringId { get; set; } = "";
        public string RulerHeroStringId { get; set; } = "";
        public bool Moved { get; set; }
        public bool Released { get; set; }
        public bool Returning { get; set; }
    }

    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private List<ReignGovernmentAttendanceRecord> _governmentAttendance = new List<ReignGovernmentAttendanceRecord>();
        private List<string> _governmentHearingChunks = new List<string>();
        public long GovernmentAttendanceRevision { get; private set; }
        public bool IsGovernmentAttendanceReserved(Hero hero) => hero != null && _governmentAttendance.Any(x => !x.Released && !x.Returning
            && Same(x.HeroStringId, hero.StringId) && Same(x.HostSettlementStringId, hero.CurrentSettlement?.StringId));

        public IReadOnlyList<Hero> GetAttendingGovernmentHeroes(Settlement settlement)
        {
            if (settlement == null) return new List<Hero>();
            var ids = _governmentAttendance.Where(x => !x.Released && !x.Returning && x.HostSettlementStringId == settlement.StringId)
                .Select(x => x.HeroStringId);
            Kingdom kingdom = settlement.OwnerClan?.Kingdom;
            if (GovernmentCapital(kingdom) == settlement && _business.Any(x => Same(x.KingdomStringId, kingdom?.StringId) && !GovernmentBusinessRules.IsClosed(x.Status)))
                ids = ids.Concat(GetSeats(kingdom.StringId).Select(x => x.HeroStringId));
            return ids.Distinct(StringComparer.OrdinalIgnoreCase).Select(FindHero)
                .Where(x => x != null && x.CurrentSettlement == settlement && GovernmentAttendanceBlock(x, false).Length == 0).ToList();
        }

        private void RegisterGovernmentAttendanceEvents()
        {
            BusinessChanged += OnAttendanceBusinessChanged;
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, TickGovernmentAttendance);
            CampaignEvents.CanMoveToSettlementEvent.AddNonSerializedListener(this, OnGovernmentCanMoveToSettlement);
        }

        private void SyncGovernmentHearingData(IDataStore store)
        {
            if (store.IsSaving) _governmentHearingChunks = ReignSavePayloadCodec.Encode(BuildGovernmentHearingPersistenceSnapshot().ToString(Formatting.None));
            store.SyncData("_reignGovernment_hearingChunks_v1", ref _governmentHearingChunks);
            if (!store.IsLoading) return;
            JObject state = _governmentHearingChunks == null || _governmentHearingChunks.Count == 0 ? new JObject()
                : JObject.Parse(ReignSavePayloadCodec.Decode(_governmentHearingChunks));
            _governmentAttendance = state["attendance"]?.ToObject<List<ReignGovernmentAttendanceRecord>>() ?? new List<ReignGovernmentAttendanceRecord>();
            _governmentCommitments = state["commitments"]?.ToObject<List<ReignGovernmentCommitmentRecord>>() ?? new List<ReignGovernmentCommitmentRecord>();
            _governmentConversationTurns.Clear();
            GovernmentAttendanceRevision++;
        }

        public JObject BuildGovernmentHearingPersistenceSnapshot() => new JObject
        {
            ["attendance"] = JArray.FromObject(_governmentAttendance),
            ["commitments"] = JArray.FromObject(_governmentCommitments)
        };

        private Settlement GovernmentCapital(Kingdom kingdom)
        {
            if (kingdom == null) return null;
#if !REIGN_EXCLUDE_COURT
            var court = ReignBeta.Court.ReignCourtCampaignBehavior.Instance;
            var designation = court?.CapitalDesignations.FirstOrDefault(x => Same(x.KingdomStringId, kingdom.StringId));
            if (designation != null)
            {
                var capital = Settlement.Find(designation.SettlementStringId);
                return capital?.IsTown == true && !capital.IsUnderSiege && capital.OwnerClan?.Kingdom == kingdom ? capital : null;
            }
#endif
            // Do not invent a different player capital when the royal capital is not designated.
            if (kingdom == Clan.PlayerClan?.Kingdom) return null;
            return Settlement.All.Where(x => x.IsTown && !x.IsUnderSiege && x.OwnerClan?.Kingdom == kingdom)
                .OrderByDescending(x => x.OwnerClan == kingdom.RulingClan).ThenBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
        }

        private string GovernmentAttendanceBlock(Hero hero, bool requiresRelocation = true)
        {
            if (hero == null || !hero.IsAlive || !hero.IsActive || hero.IsDisabled) return "Unavailable";
            if (hero.IsPrisoner) return "Captive";
            if (hero.PartyBelongedTo?.Army != null) return "Serving with an army";
            if (hero.IsTraveling || hero.PartyBelongedTo?.MapEvent != null) return "On campaign or travelling";
            if (requiresRelocation && hero.PartyBelongedTo != null) return "On campaign or travelling";
            if (!requiresRelocation && (hero.CurrentSettlement == null || hero.CurrentSettlement.IsUnderSiege)) return "Away or occupied by a siege";
            if (requiresRelocation && hero.GovernorOf != null) return "Serving as governor";
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (hero.Issue != null || campaign?.QuestManager?.IsQuestGiver(hero) == true
                || campaign?.QuestManager?.TrackedObjects?.ContainsKey(hero) == true
                || campaign?.IssueManager?.IssueSolvingCompanionList?.Contains(hero) == true) return "Occupied with another duty";
#if !REIGN_EXCLUDE_COURT
            var court = ReignBeta.Court.ReignCourtCampaignBehavior.Instance;
            if (court != null && (court.IsNobleVisitorReserved(hero) || court.HasConflictingGovernmentAttendanceDuty(hero, requiresRelocation)
                || requiresRelocation && (court.Offices.Any(x => x.IsActive && Same(x.HeroStringId, hero.StringId))
                || court.Regents.Any(x => x.IsActive && Same(x.HeroStringId, hero.StringId))
                || court.Ambassadors.Any(x => x.Status != "recalled" && Same(x.HeroStringId, hero.StringId))
                || court.ForeignAmbassadors.Any(x => x.IsActive && Same(x.HeroStringId, hero.StringId))))) return "Assigned to another court duty";
#endif
            return "";
        }

        public IReadOnlyList<ReignGovernmentAttendanceRecord> GetBusinessAttendance(string businessId)
        {
            var record = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            if (record == null) return new List<ReignGovernmentAttendanceRecord>();
            Settlement capital = GovernmentCapital(FindKingdom(record.KingdomStringId));
            return GetSeats(record.KingdomStringId).Select(seat =>
            {
                Hero hero = FindHero(seat.HeroStringId);
                string reason = GovernmentAttendanceBlock(hero, false);
                bool present = reason.Length == 0 && capital != null && hero.CurrentSettlement == capital;
                return new ReignGovernmentAttendanceRecord { BusinessId = businessId, HeroStringId = seat.HeroStringId,
                    Status = present ? "present" : "absent", HostSettlementStringId = capital?.StringId ?? "",
                    Reason = present ? "At the capital" : reason.Length > 0 ? reason : capital == null ? "No safe designated capital" : "Away from the capital" };
            }).ToList();
        }

        public bool CanDiscussGovernmentBusinessPrivately(string businessId, string heroId, out string reason)
        {
            var record = _business.FirstOrDefault(x => Same(x.BusinessId, businessId));
            Hero hero = FindHero(heroId);
            reason = "The member is not available for a private conversation about this matter.";
            if (record == null) return false;
            string blocked = GovernmentAttendanceBlock(hero, false);
            bool eligible = GovernmentPrivateConversationEligibility.CanDiscuss(
                !GovernmentBusinessRules.IsClosed(record.Status),
                !string.IsNullOrEmpty(record.VotesJson) && record.VotesJson != "[]",
                FindKingdom(record.KingdomStringId)?.Leader == Hero.MainHero,
                GetSeats(record.KingdomStringId).Any(x => Same(x.HeroStringId, heroId)),
                hero?.CurrentSettlement != null && hero.CurrentSettlement == Hero.MainHero?.CurrentSettlement,
                blocked.Length > 0);
            if (!eligible) { if (blocked.Length > 0) reason = blocked; return false; }
            reason = ""; return true;
        }

        private void OnAttendanceBusinessChanged(ReignGovernmentBusinessRecord record)
        {
            if (GovernmentBusinessRules.IsClosed(record.Status)) { NotifyGovernmentBusinessClosed(record); return; }
            if (record.Status != "postponed") return;
            Kingdom kingdom = FindKingdom(record.KingdomStringId);
            Settlement capital = GovernmentCapital(kingdom);
            if (capital == null) return;
            foreach (var seat in GetSeats(record.KingdomStringId))
            {
                if (_governmentAttendance.Any(x => Same(x.BusinessId, record.BusinessId) && Same(x.HeroStringId, seat.HeroStringId))) continue;
                Hero hero = FindHero(seat.HeroStringId);
                string blocked = GovernmentAttendanceBlock(hero);
                if (blocked.Length > 0 || hero == Hero.MainHero) continue;
                var shared = _governmentAttendance.FirstOrDefault(x => !x.Released && x.Moved && Same(x.HeroStringId, seat.HeroStringId));
                var lease = new ReignGovernmentAttendanceRecord { BusinessId = record.BusinessId, HeroStringId = hero.StringId,
                    HostSettlementStringId = capital.StringId, OriginalSettlementStringId = shared?.OriginalSettlementStringId ?? hero.CurrentSettlement?.StringId ?? "",
                    HomeSettlementStringId = shared?.HomeSettlementStringId ?? hero.HomeSettlement?.StringId ?? "", RulerHeroStringId = kingdom.Leader?.StringId ?? "" };
                _governmentAttendance.Add(lease);
                GovernmentAttendanceRevision++;
                try
                {
                    bool requiresMove = hero.CurrentSettlement != capital;
                    if (requiresMove) TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, capital);
                    lease.Moved = shared?.Moved == true || requiresMove && hero.CurrentSettlement == capital;
                    lease.Status = hero.CurrentSettlement == capital ? "present" : "absent";
                    lease.Reason = lease.Status == "present" ? "Attending during the recess" : "Native arrival could not be confirmed";
                }
                catch (Exception ex) { lease.Moved = hero.CurrentSettlement == capital; lease.Reason = "Arrival failed: " + ex.Message; }
            }
        }

        private void OnGovernmentCanMoveToSettlement(Hero hero, ref bool result)
        {
            if (result && _governmentAttendance.Any(x => !x.Released && !x.Returning && Same(x.HeroStringId, hero?.StringId)
                && Same(x.HostSettlementStringId, hero?.CurrentSettlement?.StringId))) result = false;
        }

        private void TickGovernmentAttendance()
        {
            foreach (var lease in _governmentAttendance.Where(x => !x.Released).ToList())
            {
                var record = _business.FirstOrDefault(x => Same(x.BusinessId, lease.BusinessId));
                var kingdom = FindKingdom(record?.KingdomStringId);
                if (lease.Returning || record == null || GovernmentBusinessRules.IsClosed(record.Status)
                    || GovernmentCapital(kingdom)?.StringId != lease.HostSettlementStringId
                    || kingdom?.Leader?.StringId != lease.RulerHeroStringId
                    || GovernmentAttendanceBlock(FindHero(lease.HeroStringId)).Length > 0) ReturnGovernmentAttendee(lease);
            }
        }

        private void NotifyGovernmentBusinessClosed(ReignGovernmentBusinessRecord record)
        {
            foreach (var lease in _governmentAttendance.Where(x => !x.Released && Same(x.BusinessId, record.BusinessId)).ToList()) ReturnGovernmentAttendee(lease);
            foreach (var commitment in _governmentCommitments.Where(x => Same(x.BusinessId, record.BusinessId) && x.Status == "active")) commitment.Status = "concluded";
        }

        private void ReturnGovernmentAttendee(ReignGovernmentAttendanceRecord lease)
        {
            GovernmentAttendanceRevision++;
            lease.Returning = true;
            Hero hero = FindHero(lease.HeroStringId);
            if (!lease.Moved || hero == null || GovernmentAttendanceBlock(hero).Length > 0 || hero.CurrentSettlement?.StringId != lease.HostSettlementStringId)
            { lease.Released = true; lease.Reason = "Released to current duties"; return; }
            if (_governmentAttendance.Any(x => x != lease && !x.Released && !x.Returning && Same(x.HeroStringId, lease.HeroStringId)
                && !GovernmentBusinessRules.IsClosed(_business.FirstOrDefault(b => Same(b.BusinessId, x.BusinessId))?.Status ?? "cancelled")))
            { lease.Released = true; lease.Reason = "Remaining for another hearing"; return; }
            var kingdom = hero.Clan?.Kingdom ?? hero.HomeSettlement?.OwnerClan?.Kingdom;
            Func<Settlement, bool> safe = s => s != null && !s.IsUnderSiege && s.OwnerClan?.Kingdom != null && kingdom != null && !kingdom.IsAtWarWith(s.OwnerClan.Kingdom);
            Settlement target = Settlement.Find(lease.OriginalSettlementStringId);
            if (!safe(target)) target = Settlement.Find(lease.HomeSettlementStringId);
            if (!safe(target)) target = Settlement.All.Where(x => (x.IsTown || x.IsCastle) && safe(x)).OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            if (target == null) { lease.Released = true; lease.Reason = "No safe return; released to native movement"; return; }
            try
            {
                TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, target);
                lease.Released = hero.CurrentSettlement == target;
                lease.Reason = lease.Released ? "Returned after the hearing" : "Return pending";
            }
            catch (Exception ex) { lease.Reason = "Return pending: " + ex.Message; }
        }
    }
}
