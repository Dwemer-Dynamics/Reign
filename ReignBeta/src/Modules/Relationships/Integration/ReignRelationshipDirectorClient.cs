using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Family;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        private static readonly object AmbientInputLock = new object();
        private const int AmbientInputUploadBatchSize = 8;

        public static Task<ReignConceptionAttemptResult> PreviewPlayerConceptionAttemptAsync(Hero partner, string actionText, string attemptId)
        {
            return RequestPlayerConceptionDecisionAsync(partner, actionText, attemptId, "preview");
        }

        public static Task<ReignConceptionAttemptResult> ResolvePlayerConceptionAttemptAsync(Hero partner, string actionText, string attemptId)
        {
            return RequestPlayerConceptionDecisionAsync(partner, actionText, attemptId, "proceed");
        }

        public static Task<ReignConceptionAttemptResult> CancelPlayerConceptionAttemptAsync(Hero partner, string actionText, string attemptId)
        {
            return RequestPlayerConceptionDecisionAsync(partner, actionText, attemptId, "pull_out");
        }

        private static async Task<ReignConceptionAttemptResult> RequestPlayerConceptionDecisionAsync(Hero partner, string actionText, string attemptId, string decision)
        {
            ReignConceptionAttemptResult result = new ReignConceptionAttemptResult();
            try
            {
                Hero player = Hero.MainHero;
                if (player == null || partner == null || string.IsNullOrWhiteSpace(attemptId)) return result;
                JObject response = await PostJsonAsync("/family/verify_conception_attempt", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["attemptId"] = attemptId,
                    ["eventId"] = attemptId,
                    ["decision"] = decision ?? "preview",
                    ["playerId"] = player.StringId,
                    ["partnerId"] = partner.StringId,
                    ["player"] = BuildHeroProfile(player),
                    ["partner"] = BuildHeroProfile(partner),
                    ["actionText"] = actionText ?? string.Empty,
                    ["verifiedAct"] = true,
                    ["coLocated"] = true,
                    ["completed"] = true,
                    ["worldDay"] = CurrentDirectorDay()
                }).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Verified = response.Value<bool?>("verified") == true;
                result.Eligible = response.Value<bool?>("eligible") == true;
                result.Success = response.Value<bool?>("success") == true;
                result.Cancelled = response.Value<bool?>("cancelled") == true;
                result.RollPerformed = response.Value<bool?>("rollPerformed") == true;
                result.Decision = response.Value<string>("decision") ?? decision ?? string.Empty;
                result.AttemptId = response.Value<string>("attemptId") ?? attemptId;
                result.Chance = response.Value<double?>("chance") ?? 0d;
                result.Roll = response.Value<double?>("roll") ?? -1d;
                result.ConceptionId = response.Value<string>("conceptionId") ?? string.Empty;
                result.MotherId = response.Value<string>("motherId") ?? string.Empty;
                result.BiologicalFatherId = response.Value<string>("biologicalFatherId") ?? string.Empty;
                result.LegalFatherId = response.Value<string>("legalFatherId") ?? string.Empty;
                result.DueDay = response.Value<float?>("dueDay") ?? 0f;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Player conception decision failed: " + ex.Message);
            }
            return result;
        }

        public static async Task ApplyResolvedPlayerConceptionAsync(ReignConceptionAttemptResult result)
        {
            if (result == null || !result.Success) return;
            await ReignMainThread.InvokeAsync(() =>
            {
                Hero mother = FindLivingHero(result.MotherId);
                Hero bio = FindLivingHero(result.BiologicalFatherId);
                Hero legal = FindLivingHero(result.LegalFatherId) ?? bio;
                string error = string.Empty;
                if (ReignBeta.Campaign.ReignFamilyCampaignBehavior.Instance != null
                    && ReignBeta.Campaign.ReignFamilyCampaignBehavior.Instance.TryStartManagedConception(result.ConceptionId, mother, bio, legal, (float)CurrentDirectorDay(), result.DueDay, legal != bio ? 0.75f : 0f, true, out error))
                {
                    ReignLog.Info("Verified player conception succeeded at chance=" + result.Chance.ToString("P2") + ".");
                }
                else if (!string.IsNullOrWhiteSpace(error))
                {
                    ReignLog.Warn("Verified conception could not start in game: " + error);
                }
            }).ConfigureAwait(false);
        }

        public static async Task ReportConceptionAsync(ReignConceptionRecord record, string status, string childId)
        {
            if (record == null) return;
            try
            {
                await PostJsonAsync("/family/conception/report", new JObject
                {
                    ["campaignId"] = GetCampaignId(),["conceptionId"] = record.ConceptionId,["status"] = status ?? record.Status,
                    ["childId"] = childId ?? string.Empty,["motherId"] = record.MotherId,["biologicalFatherId"] = record.BiologicalFatherId,
                    ["legalFatherId"] = record.LegalFatherId,["worldDay"] = CurrentDirectorDay(),["isIllegitimate"] = record.IsIllegitimate
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { ReignLog.Warn("Conception report failed: " + ex.Message); }
        }

        public static async Task<ReignDirectorRunResult> SubmitNobleRelationshipSnapshotAsync(bool force = false)
        {
            ReignDirectorRunResult result = new ReignDirectorRunResult();
            try
            {
                JObject payload = await ReignMainThread.InvokeAsync(BuildRelationshipDirectorSnapshot).ConfigureAwait(false);
                JObject response = await PostJsonAsync(force ? "/relationships/director/run" : "/relationships/director/snapshot", payload).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Skipped = response.Value<bool?>("skipped") == true;
                result.Reason = response.Value<string>("reason") ?? string.Empty;
                result.RunId = response.Value<string>("runId") ?? string.Empty;
                result.EventCount = (response["events"] as JArray)?.Count ?? 0;
                result.CandidateCount = response.Value<int?>("candidateCount") ?? 0;
            }
            catch (Exception ex) { result.Error = ex.Message; ReignLog.Warn("Relationship director snapshot failed: " + ex.Message); }
            return result;
        }

        public static async Task<ReignAmbientRelationshipRunResult> SubmitAmbientRelationshipSnapshotAsync()
        {
            ReignAmbientRelationshipRunResult result = new ReignAmbientRelationshipRunResult();
            try
            {
                List<string> pendingPaths;
                JArray dailyInputs;
                lock (AmbientInputLock)
                {
                    pendingPaths = PendingAmbientInputPaths()
                        .Take(AmbientInputUploadBatchSize).ToList();
                    dailyInputs = new JArray(pendingPaths.Select(path => JObject.Parse(File.ReadAllText(path, Encoding.UTF8))));
                }
                if (dailyInputs.Count == 0)
                {
                    result.Ok = true;
                    result.Idempotent = true;
                    return result;
                }
                JObject envelope = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                    ["dailyInputs"] = dailyInputs
                };
                JObject response = await PostJsonAsync("/relationships/ambient/snapshot", envelope).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Idempotent = response.Value<bool?>("idempotent") == true;
                result.RunId = response.Value<string>("runId") ?? string.Empty;
                result.ProcessedPairs = response.Value<int?>("processedPairs") ?? 0;
                result.ChangedDirections = response.Value<int?>("changedDirections") ?? 0;
                result.NativeChangesQueued = response.Value<int?>("nativeChangesQueued") ?? 0;
                result.DurationMs = response.Value<long?>("durationMs") ?? 0L;
                JObject plan = response["nativeSyncPlan"] as JObject;
                if (plan != null)
                {
                    result.NativeSyncPlan = new ReignNativeRelationSyncPlan
                    {
                        PlanId = plan.Value<string>("planId") ?? string.Empty,
                        TimelineId = plan.Value<string>("timelineId") ?? string.Empty,
                        WorldDay = plan.Value<double?>("worldDay") ?? 0d,
                        TargetCount = plan.Value<int?>("targetCount") ?? 0
                    };
                    foreach (JObject target in (plan["targets"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        result.NativeSyncPlan.Targets.Add(new ReignNativeRelationSyncTarget
                        {
                            PairKey = target.Value<string>("pairKey") ?? string.Empty,
                            HeroAId = target.Value<string>("heroAId") ?? string.Empty,
                            HeroBId = target.Value<string>("heroBId") ?? string.Empty,
                            TargetRelation = target.Value<int?>("targetRelation") ?? 0,
                            Revision = target.Value<int?>("revision") ?? 1
                        });
                    }
                }
                if (result.Ok)
                {
                    lock (AmbientInputLock)
                    {
                        foreach (string path in pendingPaths)
                        {
                            try { if (File.Exists(path)) File.Delete(path); }
                            catch (Exception ex) { ReignLog.Warn("Could not retire uploaded relationship input: " + ex.Message); }
                        }
                    }
                }
            }
            catch (Exception ex) { result.Error = ex.Message; ReignLog.Warn("Ambient relationship snapshot failed: " + ex.Message); }
            return result;
        }

        public static async Task<ReignAmbientRelationshipRunResult> SubmitCampaignOpeningRelationshipSeedAsync(
            string generationId)
        {
            ReignAmbientRelationshipRunResult result = new ReignAmbientRelationshipRunResult();
            try
            {
                JObject payload = await ReignMainThread.InvokeAsync(() =>
                {
                    List<Hero> allHeroes = Hero.AllAliveHeroes
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                        .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.First())
                        .OrderBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    List<Hero> eligible = allHeroes
                        .Where(IsAmbientRelationshipParticipant).ToList();
                    return new JObject
                    {
                        ["campaignId"] = GetCampaignId(),
                        ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                        ["generationId"] = generationId ?? string.Empty,
                        ["worldDay"] = CurrentDirectorDay(),
                        ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                        ["presenceGroups"] = new JArray(
                            BuildRelationshipPresenceGroups(eligible, false)),
                        ["heroes"] = new JArray(allHeroes.Select(BuildOpeningRelationshipHero))
                    };
                }).ConfigureAwait(false);
                JObject response = await PostJsonAsync(
                    "/relationships/initialization/seed", payload).ConfigureAwait(false);
                PopulateAmbientRelationshipRunResult(result, response);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Campaign-opening relationship seed failed: " + ex.Message);
            }
            return result;
        }

        private static void PopulateAmbientRelationshipRunResult(
            ReignAmbientRelationshipRunResult result, JObject response)
        {
            result.Ok = response?.Value<bool?>("ok") == true;
            result.Idempotent = response?.Value<bool?>("idempotent") == true;
            result.RunId = response?.Value<string>("runId") ?? string.Empty;
            result.ProcessedPairs = response?.Value<int?>("processedPairs") ?? 0;
            result.ChangedDirections = response?.Value<int?>("changedDirections") ?? 0;
            result.NativeChangesQueued = response?.Value<int?>("nativeChangesQueued") ?? 0;
            result.DurationMs = response?.Value<long?>("durationMs") ?? 0L;
            result.Error = response?.Value<string>("error") ?? string.Empty;
            JObject plan = response?["nativeSyncPlan"] as JObject;
            if (plan == null) return;
            result.NativeSyncPlan = new ReignNativeRelationSyncPlan
            {
                PlanId = plan.Value<string>("planId") ?? string.Empty,
                TimelineId = plan.Value<string>("timelineId") ?? string.Empty,
                WorldDay = plan.Value<double?>("worldDay") ?? 0d,
                TargetCount = plan.Value<int?>("targetCount") ?? 0
            };
            foreach (JObject target in (plan["targets"] as JArray ?? new JArray())
                .OfType<JObject>())
            {
                result.NativeSyncPlan.Targets.Add(new ReignNativeRelationSyncTarget
                {
                    PairKey = target.Value<string>("pairKey") ?? string.Empty,
                    HeroAId = target.Value<string>("heroAId") ?? string.Empty,
                    HeroBId = target.Value<string>("heroBId") ?? string.Empty,
                    TargetRelation = target.Value<int?>("targetRelation") ?? 0,
                    Revision = target.Value<int?>("revision") ?? 1
                });
            }
        }

        public static void CaptureAmbientRelationshipDailyInput()
        {
            Stopwatch timer = Stopwatch.StartNew();
            JObject payload = BuildAmbientRelationshipPresenceSnapshot();
            string campaignId = payload.Value<string>("campaignId") ?? GetCampaignId();
            string timelineId = payload.Value<string>("timelineId") ?? "main";
            int day = (int)Math.Floor(payload.Value<double?>("worldDay") ?? CurrentDirectorDay());
            lock (AmbientInputLock)
            {
                payload["dayKey"] = day;
                string directory = AmbientInputDirectory(campaignId, timelineId);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, day.ToString("D8", CultureInfo.InvariantCulture) + ".json");
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, payload.ToString(Newtonsoft.Json.Formatting.None), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            timer.Stop();
            ReignLog.Info("Ambient relationship daily input captured day=" + day
                + " groups=" + ((payload["presenceGroups"] as JArray)?.Count ?? 0)
                + " lifecycleHeroes=" + ((payload["heroes"] as JArray)?.Count ?? 0)
                + " buildMs=" + timer.ElapsedMilliseconds + ".");
        }

        private static IEnumerable<string> PendingAmbientInputPaths()
        {
            string campaignId = GetCampaignId();
            string timelineId = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            string directory = AmbientInputDirectory(campaignId, timelineId);
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                : Enumerable.Empty<string>();
        }

        internal static int PendingAmbientInputCount()
        {
            lock (AmbientInputLock) return PendingAmbientInputPaths().Count();
        }

        private static JObject BuildRelationshipOutboxDiagnostics()
        {
            List<string> paths;
            lock (AmbientInputLock) paths = PendingAmbientInputPaths().ToList();
            List<int> days = paths.Select(path =>
            {
                int.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int value);
                return value;
            }).Where(value => value > 0).ToList();
            return new JObject
            {
                ["clientOutboxDays"] = paths.Count,
                ["oldestClientOutboxDay"] = days.Count == 0 ? -1 : days.Min(),
                ["latestClientOutboxDay"] = days.Count == 0 ? -1 : days.Max(),
                ["uploadInFlight"] = RelationshipOutboxUploadInFlight
            };
        }

        private static string AmbientInputDirectory(string campaignId, string timelineId)
        {
            return Path.Combine(BasePath.Name, "Modules", "ReignBeta", "RelationshipOutbox",
                SafeRelationshipSegment(campaignId, "default"), SafeRelationshipSegment(timelineId, "main"));
        }

        private static string SafeRelationshipSegment(string value, string fallback)
        {
            string safe = new string((value ?? string.Empty)
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
            return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
        }

        public static async Task<bool> ReportNativeRelationSyncProgressAsync(
            ReignNativeRelationSyncProgress progress)
        {
            if (progress == null || string.IsNullOrWhiteSpace(progress.PlanId)) return true;
            try
            {
                JObject response = await PostJsonAsync("/relationships/ambient/native_sync/report", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["timelineId"] = progress.TimelineId ?? "main",
                    ["planId"] = progress.PlanId,
                    ["worldDay"] = CurrentDirectorDay(),
                    ["appliedCount"] = progress.AppliedCount,
                    ["alreadyAlignedCount"] = progress.AlreadyAlignedCount,
                    ["obsoleteCount"] = progress.ObsoleteCount,
                    ["failedCount"] = progress.FailedCount,
                    ["remainingCount"] = progress.RemainingCount,
                    ["durationMs"] = progress.DurationMs,
                    ["lastError"] = progress.LastError ?? string.Empty,
                    ["obsoletePairKeys"] = new JArray(progress.ObsoletePairKeys),
                    ["failedPairs"] = new JArray(progress.FailedPairs.Select(x => new JObject
                    {
                        ["pairKey"] = x.PairKey ?? string.Empty,
                        ["error"] = x.Error ?? string.Empty
                    }))
                }).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Native relationship bulk-sync report failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<List<ReignDirectorAction>> PollRelationshipDirectorActionsAsync()
        {
            List<ReignDirectorAction> actions = new List<ReignDirectorAction>();
            try
            {
                JObject response = await PostJsonAsync("/relationships/director/poll_actions", new JObject { ["campaignId"] = GetCampaignId(), ["worldDay"] = CurrentDirectorDay() }).ConfigureAwait(false);
                foreach (JObject row in (response["actions"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    JObject payload = row["payload"] as JObject ?? JObject.Parse(row.Value<string>("payload_json") ?? "{}");
                    actions.Add(new ReignDirectorAction
                    {
                        DirectorActionId = row.Value<string>("director_action_id") ?? string.Empty,
                        ActionType = row.Value<string>("action_type") ?? string.Empty,
                        ActorId = row.Value<string>("actor_id") ?? string.Empty,
                        TargetId = row.Value<string>("target_id") ?? string.Empty,
                        Payload = payload
                    });
                }
            }
            catch (Exception ex) { ReignLog.Warn("Relationship director action poll failed: " + ex.Message); }
            return actions;
        }

        public static async Task ReportRelationshipDirectorActionAsync(string id, string status, string error = "")
        {
            try
            {
                await PostJsonAsync("/relationships/director/report_action", new JObject { ["campaignId"] = GetCampaignId(), ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main", ["directorActionId"] = id ?? string.Empty, ["status"] = status ?? "completed", ["error"] = error ?? string.Empty, ["worldDay"] = CurrentDirectorDay() }).ConfigureAwait(false);
            }
            catch (Exception ex) { ReignLog.Warn("Relationship director action report failed: " + ex.Message); }
        }

        public static async Task ReportRelationshipDirectorActionsAsync(JArray receipts)
        {
            if (receipts == null || receipts.Count == 0) return;
            try
            {
                JObject response = await PostJsonAsync("/relationships/director/report_actions", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["receipts"] = receipts
                }).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true
                    || (response.Value<int?>("reportedCount") ?? 0) != receipts.Count)
                    throw new InvalidOperationException("The server did not accept every relationship action receipt.");
            }
            catch (Exception ex) { ReignLog.Warn("Relationship director action batch report failed: " + ex.Message); }
        }

        private static JObject BuildAmbientRelationshipPresenceSnapshot()
        {
            List<Hero> eligible = Hero.AllAliveHeroes.Where(IsAmbientRelationshipParticipant).ToList();
            List<JObject> groups = BuildRelationshipPresenceGroups(eligible, false);
            HashSet<string> presentIds = new HashSet<string>(groups
                .SelectMany(x => (x["heroIds"] as JArray ?? new JArray()).Values<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            List<Hero> participants = eligible.Where(x => presentIds.Contains(x.StringId)).ToList();
            return new JObject
            {
                ["campaignId"] = GetCampaignId(),["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["worldDay"] = CurrentDirectorDay(),["playerId"] = Hero.MainHero?.StringId ?? string.Empty,["playerClanId"] = Clan.PlayerClan?.StringId ?? string.Empty,
                ["presenceGroups"] = new JArray(groups),["heroes"] = new JArray(participants.Select(BuildAmbientLifecycleHero))
            };
        }

        private static JObject BuildAmbientLifecycleHero(Hero hero)
        {
            Clan clan = hero?.Clan;
            Kingdom kingdom = clan?.Kingdom;
            ReadNativeMarriageEligibility(hero, out bool nativeCanMarry,
                out bool nativeMarriageClanSuitable);
            JObject row = new JObject
            {
                ["i"] = hero?.StringId ?? string.Empty,
                ["a"] = hero?.Age ?? 0f,
                ["s"] = hero?.Spouse?.StringId ?? string.Empty,
                ["fa"] = hero?.Father?.StringId ?? string.Empty,
                ["mo"] = hero?.Mother?.StringId ?? string.Empty,
                ["c"] = clan?.StringId ?? string.Empty,
                ["k"] = kingdom?.StringId ?? string.Empty,
                ["ch"] = hero?.Children?.Count ?? 0,
                ["an"] = BuildMarriageAncestorIds(hero),
                ["m"] = nativeCanMarry ? 1 : 0,
                ["u"] = nativeMarriageClanSuitable ? 1 : 0
            };
            if (hero?.IsFemale == true) row["f"] = 1;
            if (hero?.IsPregnant == true) row["p"] = 1;
            if (hero?.IsLord == true) row["n"] = 1;
            if (hero?.IsNotable == true) row["o"] = 1;
            if (kingdom?.Leader == hero) row["r"] = 1;
            if (clan?.Leader == hero) row["l"] = 1;
            return row;
        }

        private static JObject BuildOpeningRelationshipHero(Hero hero)
        {
            Clan clan = hero?.Clan;
            Kingdom kingdom = clan?.Kingdom;
            return new JObject
            {
                ["heroStringId"] = hero?.StringId ?? string.Empty,
                ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["age"] = hero?.Age ?? 0f,
                ["isAdult"] = hero != null && !hero.IsChild && hero.Age >= 18f,
                ["isAlive"] = hero?.IsAlive ?? false,
                ["isPlayer"] = hero == Hero.MainHero,
                ["isPrisoner"] = hero?.IsPrisoner ?? false,
                ["isFemale"] = hero?.IsFemale ?? false,
                ["isLord"] = hero?.IsLord ?? false,
                ["isNotable"] = hero?.IsNotable ?? false,
                ["isWanderer"] = hero?.IsWanderer ?? false,
                ["isClanLeader"] = clan?.Leader == hero,
                ["isRuler"] = kingdom?.Leader == hero,
                ["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,
                ["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["clanId"] = clan?.StringId ?? string.Empty,
                ["kingdomId"] = kingdom?.StringId ?? string.Empty
            };
        }

        private static JObject BuildRelationshipDirectorSnapshot()
        {
            List<Hero> participants = Hero.AllAliveHeroes.Where(IsAmbientRelationshipParticipant).ToList();
            List<JObject> groups = BuildRelationshipPresenceGroups(participants, true);
            JArray nobleRows = new JArray(Hero.AllAliveHeroes.Where(x => x != null && x.IsAlive && x.IsLord && !x.IsChild && x.Age >= 18f).Select(BuildDirectorHero));
            JArray clans = new JArray(Clan.All.Where(c => c != null && !c.IsEliminated && !c.IsBanditFaction && !c.IsRebelClan).Select(c => new JObject
            {
                ["clanId"] = c.StringId,
                ["name"] = c.Name?.ToString() ?? c.StringId,
                ["leaderId"] = c.Leader?.StringId ?? string.Empty,
                ["kingdomId"] = c.Kingdom?.StringId ?? string.Empty,
                ["cultureId"] = c.Culture?.StringId ?? string.Empty,
                ["tier"] = c.Tier,
                ["renown"] = c.Renown,
                ["gold"] = c.Gold,
                ["influence"] = c.Influence,
                ["isPlayerClan"] = c == Clan.PlayerClan,
                ["isRulingClan"] = c.Kingdom != null && c.Kingdom.RulingClan == c,
                ["memberIds"] = new JArray(c.AliveLords.Where(h => h != null).Select(h => h.StringId))
            }));
            JArray kingdoms = new JArray(Kingdom.All
                .Where(k => k != null && !k.IsEliminated)
                .Select(k => new JObject
                {
                    ["kingdomId"] = k.StringId,
                    ["name"] = k.Name?.ToString() ?? k.StringId,
                    ["cultureId"] = k.Culture?.StringId ?? string.Empty,
                    ["leaderId"] = k.Leader?.StringId ?? string.Empty,
                    ["cityIds"] = new JArray(Settlement.All
                        .Where(s => s != null && s.IsTown && s.MapFaction == k)
                        .OrderBy(s => s.StringId, StringComparer.OrdinalIgnoreCase)
                        .Select(s => s.StringId)),
                    ["cities"] = new JArray(Settlement.All
                        .Where(s => s != null && s.IsTown && s.MapFaction == k)
                        .OrderBy(s => s.StringId, StringComparer.OrdinalIgnoreCase)
                        .Select(s => new JObject
                        {
                            ["settlementId"] = s.StringId,
                            ["name"] = s.Name?.ToString() ?? s.StringId
                        }))
                }));
            return new JObject
            {
                ["campaignId"] = GetCampaignId(),["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["worldDay"] = CurrentDirectorDay(),["playerId"] = Hero.MainHero?.StringId ?? string.Empty,["playerClanId"] = Clan.PlayerClan?.StringId ?? string.Empty,
                ["presenceGroups"] = new JArray(groups),["heroes"] = new JArray(participants.Select(BuildDirectorHero)),["nobles"] = nobleRows,["clans"] = clans,
                ["kingdoms"] = kingdoms
            };
        }

        private static List<JObject> BuildRelationshipPresenceGroups(List<Hero> participants, bool includeNativeRelations)
        {
            // Every NPC belongs to exactly one daily social pool. This keeps
            // passive relationship work linear and prevents a hero in a party
            // at a settlement from receiving two compatibility encounters.
            var assignments = participants
                .Where(x => x != null && x != Hero.MainHero)
                .Select(hero =>
                {
                    MobileParty party = hero.PartyBelongedTo;
                    Settlement settlement = hero.CurrentSettlement
                        ?? party?.CurrentSettlement;
                    if (party == MobileParty.MainParty)
                        return new { Hero = hero, Kind = "player_party", Id = party.StringId };
                    if (party?.Army != null && party.Army.LeaderParty != null)
                        return new { Hero = hero, Kind = "army", Id = party.Army.LeaderParty.StringId };
                    if (settlement != null && !string.IsNullOrWhiteSpace(settlement.StringId))
                        return new { Hero = hero, Kind = "settlement", Id = settlement.StringId };
                    if (party != null && !string.IsNullOrWhiteSpace(party.StringId))
                        return new { Hero = hero, Kind = "party", Id = party.StringId };
                    return new { Hero = hero, Kind = string.Empty, Id = string.Empty };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Kind)
                    && !string.IsNullOrWhiteSpace(x.Id));
            return assignments
                .GroupBy(x => x.Kind + "|" + x.Id,
                    StringComparer.OrdinalIgnoreCase)
                // Preserve singleton locations for the daily low-judgment fling
                // poll. Ordinary relationship matching safely produces no pair,
                // while a passing singleton remains auditable as unmatched.
                .Where(group => group.Count() >= 1)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    return BuildPresenceGroup(first.Kind, first.Id,
                        group.Select(x => x.Hero).ToList(),
                        includeNativeRelations);
                }).ToList();
        }

        private static JObject BuildPresenceGroup(string kind, string id, List<Hero> members, bool includeNativeRelations)
        {
            JArray relations = new JArray();
            if (includeNativeRelations)
            {
                for (int i = 0; i < members.Count; i++)
                    for (int j = i + 1; j < members.Count; j++)
                        relations.Add(new JObject { ["heroAId"] = members[i].StringId, ["heroBId"] = members[j].StringId, ["value"] = members[i].GetRelation(members[j]) });
            }
            JObject group = new JObject
            {
                ["kind"] = kind,
                ["id"] = id,
                ["heroIds"] = new JArray(members.Select(x => x.StringId))
            };
            if (includeNativeRelations) group["nativeRelations"] = relations;
            return group;
        }

        private static bool IsAmbientRelationshipParticipant(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || hero.IsChild || hero.Age < 18f || hero.IsPrisoner) return false;
            return true;
        }

        private static JObject BuildDirectorHero(Hero hero)
        {
            Clan clan = hero?.Clan;
            Kingdom kingdom = clan?.Kingdom;
            Settlement currentSettlement = hero?.CurrentSettlement ?? hero?.PartyBelongedTo?.CurrentSettlement;
            ReadNativeMarriageEligibility(hero, out bool nativeCanMarry,
                out bool nativeMarriageClanSuitable);
            return new JObject
            {
                ["heroStringId"] = hero?.StringId ?? string.Empty,["name"] = hero?.Name?.ToString() ?? string.Empty,["age"] = hero?.Age ?? 0f,
                ["isFemale"] = hero?.IsFemale ?? false,["isAlive"] = hero?.IsAlive ?? false,["isPregnant"] = hero?.IsPregnant ?? false,
                ["childrenCount"] = hero?.Children?.Count ?? 0,["spouseId"] = hero?.Spouse?.StringId ?? string.Empty,["clanId"] = clan?.StringId ?? string.Empty,
                ["kingdomId"] = kingdom?.StringId ?? string.Empty,["cultureId"] = hero?.Culture?.StringId ?? string.Empty,
                ["fatherId"] = hero?.Father?.StringId ?? string.Empty,["motherId"] = hero?.Mother?.StringId ?? string.Empty,
                ["isClanLeader"] = clan?.Leader == hero,["isRulingClanMember"] = kingdom != null && kingdom.RulingClan == clan,
                ["isLord"] = hero?.IsLord ?? false,["isNotable"] = hero?.IsNotable ?? false,["isWanderer"] = hero?.IsWanderer ?? false,["isPrisoner"] = hero?.IsPrisoner ?? false,
                ["nativeCanMarry"] = nativeCanMarry,["nativeMarriageClanSuitable"] = nativeMarriageClanSuitable,
                ["marriageAncestorIds"] = BuildMarriageAncestorIds(hero),["activeCourtships"] = BuildActiveCourtships(hero),
                ["nativeRelationToLeader"] = clan?.Leader != null && clan.Leader != hero ? hero.GetRelation(clan.Leader) : 0,
                ["currentSettlementId"] = currentSettlement?.StringId ?? string.Empty,
                ["currentSettlementKingdomId"] = currentSettlement?.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                ["clanTier"] = clan?.Tier ?? 0,["clanRenown"] = clan?.Renown ?? 0f,["clanGold"] = clan?.Gold ?? 0,
                ["clanFiefCount"] = clan?.Fiefs?.Count ?? 0,["influence"] = clan?.Influence ?? 0f,
                ["isRuler"] = kingdom?.Leader == hero,["leadership"] = hero?.GetSkillValue(DefaultSkills.Leadership) ?? 0,
                ["traits"] = BuildTraitProfile(hero),["skills"] = BuildSkillProfile(hero)
            };
        }

        private static void ReadNativeMarriageEligibility(Hero hero,
            out bool nativeCanMarry, out bool nativeMarriageClanSuitable)
        {
            nativeCanMarry = false;
            nativeMarriageClanSuitable = false;
            try
            {
                if (hero == null
                    || TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MarriageModel == null)
                    return;
                nativeCanMarry = hero.CanMarry();
                nativeMarriageClanSuitable = TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel
                    .IsClanSuitableForMarriage(hero.Clan);
            }
            catch
            {
                nativeCanMarry = false;
                nativeMarriageClanSuitable = false;
            }
        }

        private static JArray BuildMarriageAncestorIds(Hero hero)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectMarriageAncestors(hero, 3, ids);
            return new JArray(ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        private static void CollectMarriageAncestors(Hero hero, int remainingDepth, HashSet<string> ids)
        {
            if (hero == null || remainingDepth <= 0) return;
            foreach (Hero parent in new[] { hero.Father, hero.Mother })
            {
                if (parent == null || string.IsNullOrWhiteSpace(parent.StringId) || !ids.Add(parent.StringId)) continue;
                CollectMarriageAncestors(parent, remainingDepth - 1, ids);
            }
        }

        private static JArray BuildActiveCourtships(Hero hero)
        {
            JArray rows = new JArray();
            if (hero == null || Romance.RomanticStateList == null) return rows;
            foreach (Romance.RomanticState state in Romance.RomanticStateList
                .Where(x => x != null && x.Level >= Romance.RomanceLevelEnum.MatchMadeByFamily
                    && (x.Person1 == hero || x.Person2 == hero)))
            {
                Hero partner = state.Partner(hero);
                if (partner == null || string.IsNullOrWhiteSpace(partner.StringId)) continue;
				// Direct NPC divorce used to leave Bannerlord's Marriage romance
				// record behind. Never advertise that stale native record as an
				// active courtship after reciprocal spouse state has ended.
				if (state.Level == Romance.RomanceLevelEnum.Marriage
					&& (hero.Spouse != partner || partner.Spouse != hero)) continue;
                rows.Add(new JObject
                {
                    ["heroId"] = partner.StringId,
                    ["clanId"] = partner.Clan?.StringId ?? string.Empty,
                    ["level"] = state.Level.ToString()
                });
            }
            return rows;
        }

        private static Hero FindLivingHero(string id) => Hero.AllAliveHeroes.FirstOrDefault(x => x != null && string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
        private static double CurrentDirectorDay() => TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays;
    }

    public sealed class ReignConceptionAttemptResult
    {
        public bool Ok { get; set; } public bool Verified { get; set; } public bool Eligible { get; set; } public bool Success { get; set; }
        public bool Cancelled { get; set; } public bool RollPerformed { get; set; } public string Decision { get; set; } = string.Empty; public string AttemptId { get; set; } = string.Empty;
        public double Chance { get; set; } public double Roll { get; set; } = -1d; public string ConceptionId { get; set; } = string.Empty;
        public string MotherId { get; set; } = string.Empty; public string BiologicalFatherId { get; set; } = string.Empty; public string LegalFatherId { get; set; } = string.Empty;
        public float DueDay { get; set; } public string Error { get; set; } = string.Empty;
    }

    public sealed class ReignDirectorRunResult
    {
        public bool Ok { get; set; } public bool Skipped { get; set; } public string Reason { get; set; } = string.Empty; public string RunId { get; set; } = string.Empty;
        public int EventCount { get; set; } public int CandidateCount { get; set; } public string Error { get; set; } = string.Empty;
    }

    public sealed class ReignAmbientRelationshipRunResult
    {
        public bool Ok { get; set; }
        public bool Idempotent { get; set; }
        public string RunId { get; set; } = string.Empty;
        public int ProcessedPairs { get; set; }
        public int ChangedDirections { get; set; }
        public int NativeChangesQueued { get; set; }
        public long DurationMs { get; set; }
        public ReignNativeRelationSyncPlan NativeSyncPlan { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public sealed class ReignNativeRelationSyncPlan
    {
        public string PlanId { get; set; } = string.Empty;
        public string TimelineId { get; set; } = string.Empty;
        public double WorldDay { get; set; }
        public int TargetCount { get; set; }
        public List<ReignNativeRelationSyncTarget> Targets { get; } = new List<ReignNativeRelationSyncTarget>();
    }

    public sealed class ReignNativeRelationSyncTarget
    {
        public string PairKey { get; set; } = string.Empty;
        public string HeroAId { get; set; } = string.Empty;
        public string HeroBId { get; set; } = string.Empty;
        public int TargetRelation { get; set; }
        public int Revision { get; set; } = 1;
    }

    public sealed class ReignNativeRelationSyncFailure
    {
        public string PairKey { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    public sealed class ReignNativeRelationSyncProgress
    {
        public string PlanId { get; set; } = string.Empty;
        public string TimelineId { get; set; } = string.Empty;
        public int AppliedCount { get; set; }
        public int AlreadyAlignedCount { get; set; }
        public int ObsoleteCount { get; set; }
        public int FailedCount { get; set; }
        public int RemainingCount { get; set; }
        public long DurationMs { get; set; }
        public string LastError { get; set; } = string.Empty;
        public List<string> ObsoletePairKeys { get; } = new List<string>();
        public List<ReignNativeRelationSyncFailure> FailedPairs { get; } = new List<ReignNativeRelationSyncFailure>();
    }

    public sealed class ReignDirectorAction
    {
        public string DirectorActionId { get; set; } = string.Empty; public string ActionType { get; set; } = string.Empty;
        public string ActorId { get; set; } = string.Empty; public string TargetId { get; set; } = string.Empty; public JObject Payload { get; set; } = new JObject();
    }
}
