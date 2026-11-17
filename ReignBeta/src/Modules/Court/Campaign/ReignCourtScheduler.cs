using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Court
{
    public static class ReignCourtTerms
    {
        public static string Canonicalize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "{}";
            try { return CanonicalizeToken(JToken.Parse(json)).ToString(Formatting.None); }
            catch { return json.Trim(); }
        }

        public static string Hash(string json)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Canonicalize(json))))
                    .Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static JToken CanonicalizeToken(JToken token)
        {
            if (token is JObject obj)
            {
                JObject ordered = new JObject();
                foreach (JProperty property in obj.Properties().OrderBy(x => x.Name, StringComparer.Ordinal))
                    ordered.Add(property.Name, CanonicalizeToken(property.Value));
                return ordered;
            }
            if (token is JArray array) return new JArray(array.Select(CanonicalizeToken));
            return token.DeepClone();
        }
    }

    public static class ReignCourtScheduler
    {
        public const int MaximumDailyMatters = 6;

        public static List<CourtAgendaItem> BuildAgenda(List<CourtMatter> matters, int agendaDay)
        {
            matters = matters ?? new List<CourtMatter>();
            ExpireOverdueMatters(matters, agendaDay);

            List<CourtMatter> candidates = matters
                .Where(x => x != null && !x.IsTerminal)
                .GroupBy(x => x.SourceKey ?? x.MatterId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.IsCritical)
                    .ThenBy(x => x.DueDay < 0f ? float.MaxValue : x.DueDay)
                    .ThenByDescending(x => x.Priority)
                    .ThenBy(x => x.CreatedDay).First())
                .OrderByDescending(x => x.IsCritical)
                .ThenBy(x => DeadlineRank(x, agendaDay))
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => StableWeight(x, agendaDay))
                .ToList();

            List<CourtMatter> selected = new List<CourtMatter>();
            bool majorSelected = false;
            bool ambientSelected = false;
            foreach (CourtMatter matter in candidates)
            {
                if (selected.Count >= MaximumDailyMatters) break;
                if (matter.IsMajorRealmEvent && majorSelected) continue;
                if (matter.IsAmbientRoleplay && ambientSelected) continue;
                selected.Add(matter);
                majorSelected |= matter.IsMajorRealmEvent;
                ambientSelected |= matter.IsAmbientRoleplay;
            }

            List<CourtAgendaItem> agenda = new List<CourtAgendaItem>();
            for (int i = 0; i < selected.Count; i++)
            {
                CourtMatter matter = selected[i];
                matter.State = ReignCourtMatterState.Scheduled;
                matter.ScheduledDay = agendaDay;
                matter.Revision++;
                agenda.Add(new CourtAgendaItem
                {
                    MatterId = matter.MatterId,
                    AgendaDay = agendaDay,
                    Slot = i,
                    IsUrgentInterruption = false,
                    Status = "scheduled"
                });
            }
            return agenda;
        }

        public static bool TryQueueUnique(List<CourtMatter> matters, CourtMatter candidate)
        {
            if (matters == null || candidate == null || string.IsNullOrWhiteSpace(candidate.SourceKey)) return false;
            if (matters.Any(x => x != null && !x.IsTerminal && string.Equals(x.SourceKey, candidate.SourceKey, StringComparison.OrdinalIgnoreCase))) return false;
            matters.Add(candidate);
            return true;
        }

        public static CourtAgendaItem QueueUrgentInterruption(List<CourtMatter> matters, List<CourtAgendaItem> agenda, CourtMatter matter, int day)
        {
            if (!TryQueueUnique(matters, matter)) return null;
            matter.IsCritical = true;
            matter.State = ReignCourtMatterState.Scheduled;
            matter.ScheduledDay = day;
            matter.Revision++;
            CourtAgendaItem item = new CourtAgendaItem
            {
                MatterId = matter.MatterId,
                AgendaDay = day,
                Slot = -1,
                IsUrgentInterruption = true,
                Status = "urgent"
            };
            agenda?.Add(item);
            return item;
        }

        private static void ExpireOverdueMatters(IEnumerable<CourtMatter> matters, int day)
        {
            foreach (CourtMatter matter in matters.Where(x => x != null && !x.IsTerminal && x.DueDay >= 0f && x.DueDay < day))
            {
                matter.State = ReignCourtMatterState.Expired;
                matter.ResolvedDay = day;
                matter.SelectedOptionId = "deadline_expired";
                matter.ResolutionReceiptJson = string.IsNullOrWhiteSpace(matter.DefaultOutcomeJson) ? "{}" : matter.DefaultOutcomeJson;
                matter.Revision++;
            }
        }

        private static int DeadlineRank(CourtMatter matter, int day)
        {
            if (matter.DueDay < 0f) return int.MaxValue;
            return Math.Max(0, (int)Math.Floor(matter.DueDay - day));
        }

        private static int StableWeight(CourtMatter matter, int day)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in (matter.SourceKey ?? matter.MatterId ?? string.Empty)) hash = hash * 31 + c;
                return (hash ^ day) & int.MaxValue;
            }
        }
    }
}
