using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Reign.Core.Contracts.Family;
using ReignBeta.Actions;
using ReignBeta.Family;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignFamilyCampaignBehavior
    {
        // Observation only. Never put native schedules in _conceptions: that list owns delivery.
        private List<ReignConceptionRecord> _nativePregnancyContexts = new List<ReignConceptionRecord>();
        private static readonly JsonSerializer PregnancySerializer = JsonSerializer.Create(new JsonSerializerSettings {
            ContractResolver = new CamelCasePropertyNamesContractResolver() });

        internal void ReconcileNativePregnancyContexts(Hero newlyConceived = null)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            try
            {
                var behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<PregnancyCampaignBehavior>();
                var schedules = typeof(PregnancyCampaignBehavior).GetField("_heroPregnancies", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(behavior) as IEnumerable;
                foreach (object schedule in schedules ?? new object[0])
                {
                    Type type = schedule.GetType();
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var mother = type.GetField("Mother", flags)?.GetValue(schedule) as Hero;
                    var father = type.GetField("Father", flags)?.GetValue(schedule) as Hero;
                    object dueValue = type.GetField("DueDate", flags)?.GetValue(schedule);
                    if (mother == null || !mother.IsPregnant || IsManagedPregnancy(mother) || !(dueValue is CampaignTime due)) continue;
                    float dueDay = (float)due.ToDays;
                    string id = "native_pregnancy_" + mother.StringId + "_" + dueDay.ToString("R", CultureInfo.InvariantCulture);
                    var record = _nativePregnancyContexts.FirstOrDefault(x => x.ConceptionId == id
                        || x.MotherId == mother.StringId && x.Status == "active" && x.DueDay == dueDay);
                    if (record == null)
                    {
                        // A flag-only legacy observation can acquire its authoritative schedule later.
                        record = _nativePregnancyContexts.FirstOrDefault(x => x.MotherId == mother.StringId && x.Status == "active" && x.DueDay <= 0);
                        if (record == null) { record = new ReignConceptionRecord { ConceptionId = id, MotherId = mother.StringId }; _nativePregnancyContexts.Add(record); }
                    }
                    float duration = TaleWorlds.CampaignSystem.Campaign.Current.Models.PregnancyModel.PregnancyDurationInDays;
                    record.BiologicalFatherId = father?.StringId ?? "";
                    record.LegalFatherId = father?.StringId ?? "";
                    record.DueDay = dueDay;
                    record.ConceptionDay = duration > 0 ? dueDay - duration : 0;
                    record.ContextSource = "bannerlord_native_schedule";
                    record.Status = "active";
                    // Keep a promoted flag-only identity stable across subsequent captures.
                    // A newer schedule supersedes prior observations, never native delivery state.
                    foreach (var previous in _nativePregnancyContexts.Where(x => x.MotherId == mother.StringId
                        && x.Status == "active" && !ReferenceEquals(x, record))) previous.Status = "ended";
                    if (record.MarriageAtConceptionKnown)
                        record.IsIllegitimate = string.IsNullOrEmpty(record.SpouseAtConceptionId) || record.SpouseAtConceptionId != record.BiologicalFatherId;
                    if (mother == newlyConceived)
                    {
                        record.ConceptionDay = (float)CampaignTime.Now.ToDays;
                        record.MarriageAtConceptionKnown = true;
                        record.SpouseAtConceptionId = mother.Spouse?.StringId ?? "";
                        record.IsIllegitimate = mother.Spouse == null || mother.Spouse != father;
                    }
                }
            }
            catch (Exception ex) { ReignLog.Warn("Native pregnancy schedule unavailable; keeping flag-only self-awareness: " + ex.GetType().Name); }
            foreach (Hero mother in Hero.AllAliveHeroes.Where(h => h.IsPregnant && !IsManagedPregnancy(h)))
                if (!_nativePregnancyContexts.Any(x => x.MotherId == mother.StringId && x.Status == "active"))
                    _nativePregnancyContexts.Add(new ReignConceptionRecord { ConceptionId = "native_pregnancy_" + Guid.NewGuid().ToString("N"),
                        MotherId = mother.StringId, ContextSource = "bannerlord_native_flag",
                        MarriageAtConceptionKnown = mother == newlyConceived,
                        SpouseAtConceptionId = mother == newlyConceived ? mother.Spouse?.StringId ?? "" : "",
                        IsIllegitimate = mother == newlyConceived && mother.Spouse == null });
            foreach (var record in _nativePregnancyContexts.Where(x => x.Status == "active"))
            {
                Hero mother = ReignActionService.HeroById(record.MotherId);
                if (mother == null || !mother.IsPregnant || IsManagedPregnancy(mother)) record.Status = "ended";
            }
        }

        internal IEnumerable<ReignConceptionRecord> PregnancyRecords => _conceptions.Concat(_nativePregnancyContexts)
            .Where(IsActivePregnancyRecord);

        private static bool IsActivePregnancyRecord(ReignConceptionRecord record) => record != null
            && record.Status == "active" && ReignActionService.HeroById(record.MotherId)?.IsPregnant == true;

        internal PregnancyFacts PregnancyFactsFor(ReignConceptionRecord record)
        {
            Hero mother = ReignActionService.HeroById(record.MotherId), father = ReignActionService.HeroById(record.BiologicalFatherId);
            Hero legal = ReignActionService.HeroById(record.LegalFatherId);
            return new PregnancyFacts {
                PregnancyId = record.ConceptionId, MotherId = record.MotherId, MotherName = mother?.Name?.ToString() ?? record.MotherId,
                BiologicalFatherId = record.BiologicalFatherId ?? "", BiologicalFatherName = father?.Name?.ToString() ?? record.BiologicalFatherId ?? "",
                LegalFatherId = record.LegalFatherId ?? "", LegalFatherName = legal?.Name?.ToString() ?? record.LegalFatherId ?? "",
                SpouseId = mother?.Spouse?.StringId ?? "", SpouseName = mother?.Spouse?.Name?.ToString() ?? "",
                SpouseAtConceptionId = record.SpouseAtConceptionId ?? "", MarriageAtConceptionKnown = record.MarriageAtConceptionKnown,
                Active = mother?.IsPregnant == true && record.Status == "active", Illicit = record.IsIllegitimate,
                TimingKnown = record.DueDay > record.ConceptionDay && record.DueDay > 0,
                ConceptionDay = record.ConceptionDay, DueDay = record.DueDay, WorldDay = CampaignTime.Now.ToDays,
                MotherExposed = record.MotherExposed, FatherExposed = record.FatherExposed, Source = record.ContextSource ?? "reign_managed"
            };
        }
        internal JArray CapturePregnancyContexts()
        {
            ReconcileNativePregnancyContexts();
            return new JArray(PregnancyRecords.Select(r => JObject.FromObject(PregnancyFactsFor(r), PregnancySerializer)));
        }
        internal JArray PregnancyContextsFor(Hero hero)
        {
            string heroId = hero?.StringId;
            // Filter identity before resolving native mothers: profile refreshes must not
            // repeat every unrelated pregnancy's hero search for each character.
            return new JArray(_conceptions.Concat(_nativePregnancyContexts)
                .Where(r => r != null && (r.MotherId == heroId || r.BiologicalFatherId == heroId || r.LegalFatherId == heroId))
                .Where(IsActivePregnancyRecord)
                .Select(r => JObject.FromObject(PregnancyFactsFor(r), PregnancySerializer)));
        }

        internal PregnancyKnowledge KnownPregnancy(ReignConceptionRecord record, string observer)
        {
            JObject known;
            try { known = JObject.Parse(record.KnowledgeJson ?? "{}"); } catch (JsonException) { known = new JObject(); }
            return known[observer ?? ""]?.ToObject<PregnancyKnowledge>() ?? new PregnancyKnowledge();
        }
        internal void AcceptPregnancyKnowledge(JArray rows)
        {
            foreach (JObject row in rows?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                var record = PregnancyRecords.FirstOrDefault(r => r.ConceptionId == row.Value<string>("pregnancyId"));
                if (record != null && row["knowledge"] is JObject knowledge) record.KnowledgeJson = knowledge.ToString(Formatting.None);
            }
        }
        internal List<ReignActionOption> PregnancyExposureOptions(string counterpart)
        {
            return PregnancyRecords.Where(r => PregnancyContextPolicy.ExposureError(PregnancyFactsFor(r), KnownPregnancy(r, Hero.MainHero?.StringId),
                    counterpart, counterpart == r.BiologicalFatherId || r.MotherExposed).Length == 0)
                .Select(r => new ReignActionOption { Id = r.ConceptionId, Label = PregnancyFactsFor(r).MotherName + "'s pregnancy" }).ToList();
        }
        internal string CheckPregnancyExposure(string id, string counterpart, bool nameFather)
        {
            var record = PregnancyRecords.FirstOrDefault(r => r.ConceptionId == id);
            return record == null ? "This pregnancy is no longer available." : PregnancyContextPolicy.ExposureError(
                PregnancyFactsFor(record), KnownPregnancy(record, Hero.MainHero?.StringId), counterpart, nameFather);
        }
        internal string PregnancyExposureSentence(string id, bool nameFather)
        {
            var record = PregnancyRecords.FirstOrDefault(r => r.ConceptionId == id);
            if (record == null) return "I expose the concealed pregnancy";
            var facts = PregnancyFactsFor(record);
            return "I expose " + facts.MotherName + "'s illicit pregnancy" + (nameFather ? " and name " + facts.BiologicalFatherName + " as the biological father" : ", without naming the biological father");
        }
        internal void ExposePregnancy(string id, string counterpart, bool nameFather)
        {
            string error = CheckPregnancyExposure(id, counterpart, nameFather);
            if (error.Length > 0) throw new InvalidOperationException(error);
            var record = PregnancyRecords.Single(r => r.ConceptionId == id);
            var mother = ReignActionService.HeroById(record.MotherId);
            var history = ReignWorldHistoryCampaignBehavior.Instance ?? throw new InvalidOperationException("World history is unavailable.");
            bool exposeMother = !record.MotherExposed;
            record.MotherExposed = true;
            record.FatherExposed |= nameFather;
            string summary = PregnancyExposureSentence(id, nameFather) + ".";
            // Public payload deliberately excludes unnamed biological parentage.
            var evidence = new JObject { ["pregnancyId"] = id, ["motherId"] = record.MotherId, ["illicit"] = true,
                ["fatherNamed"] = nameFather, ["biologicalFatherId"] = nameFather ? record.BiologicalFatherId : "" };
            if (exposeMother) history.RecordDynamicSocialReputation(mother, "the_unchaste", true, -50, id + ":exposed:mother", summary,
                "Her publicly exposed illicit pregnancy is a severe court scandal.", new JObject(evidence) { ["directReputationProducer"] = "pregnancy_exposure" });
            if (nameFather)
                history.RecordDynamicSocialReputation(ReignActionService.HeroById(record.BiologicalFatherId), "the_unchaste", true, -5,
                    id + ":exposed:father", summary, "He is the publicly identified biological father of an illicit pregnancy.",
                    new JObject(evidence) { ["directReputationProducer"] = "pregnancy_exposure" });
            history.RecordReignSystemEvent("pregnancy_exposed", "completed", "family", id + ":exposed:" + nameFather,
                summary, "ordinary", mother?.StringId, mother?.Clan?.Kingdom?.StringId, evidence);
        }
    }
}
