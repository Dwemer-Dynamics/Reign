using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.Government;
using ReignBeta.World;
using ReignPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Government.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Runtime;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.News;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
#endif

namespace ReignBeta.Platform.Integration
{
    public static partial class ReignServerClient
    {
        public static async Task<bool> UpsertHeroAsync(Hero hero)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return false;
                }

                if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                {
                    return false;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["hero"] = BuildHeroProfile(hero)
                };
                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/characters/upsert", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /characters/upsert hero=" + hero.StringId + " clientMs=" + timer.ElapsedMilliseconds + " ok=" + (response.Value<bool?>("ok") == true));
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Hero upsert failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<ReignHeroGenerationResult> UpsertAllHeroesAsync()
        {
            ReignHeroGenerationResult result = new ReignHeroGenerationResult();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    result.Error = "Local server is disabled.";
                    return result;
                }

                JArray heroes = new JArray();
                foreach (Hero hero in Hero.AllAliveHeroes.Where(IsFixedNobleHero))
                {
                    if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                    {
                        continue;
                    }

                    heroes.Add(BuildHeroProfile(hero));
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["heroes"] = heroes
                };
                JObject response = await PostJsonAsync("/characters/upsert-batch", payload).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Created = response.Value<int?>("created") ?? 0;
                result.Updated = response.Value<int?>("updated") ?? 0;
                result.Failed = response.Value<int?>("failed") ?? 0;
                result.Total = response.Value<int?>("total") ?? heroes.Count;
                result.Error = response.Value<string>("error") ?? string.Empty;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Generate all hero folders failed: " + ex.Message);
                return result;
            }
        }

        public static async Task<JObject> SynchronizeIdentityNetworkAsync(
            string initializationGenerationId = "")
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return new JObject { ["ok"] = false, ["error"] = "Local server is disabled." };
                }

                JArray heroes = new JArray();
                JArray financeSnapshots = new JArray();
                JArray portraitCharacters = new JArray();
                double observedWorldDay = TaleWorlds.CampaignSystem.Campaign.Current == null
                    ? 0d
                    : CampaignTime.Now.ToDays;
                foreach (Hero hero in Hero.AllAliveHeroes
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                    .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .OrderBy(x => x.StringId, StringComparer.OrdinalIgnoreCase))
                {
                    heroes.Add(BuildIdentityRosterEntry(hero));
                    if (IsFinanceSnapshotEligible(hero))
                    {
                        financeSnapshots.Add(BuildFinanceSnapshotEntry(hero));
                    }
                    if (IsPortraitRosterEligible(hero))
                    {
                        portraitCharacters.Add(BuildPortraitRosterEntry(hero));
                    }
                }

                string campaignId = GetCampaignId();
                string rosterFingerprint = BuildPortraitRosterFingerprint(portraitCharacters);
                JObject response = await PostJsonAsync("/identity/synchronize", new JObject
                {
                    ["campaignId"] = campaignId,
                    ["correlationId"] = NewCorrelationId("identity_sync", Hero.MainHero?.StringId),
                    ["worldDay"] = observedWorldDay,
                    ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                    ["deferRelationshipProjection"] = !string.IsNullOrWhiteSpace(initializationGenerationId),
                    ["initializationGenerationId"] = initializationGenerationId ?? string.Empty,
                    ["portraitRosterFingerprint"] = rosterFingerprint,
                    ["heroes"] = heroes,
                    ["financeSnapshots"] = financeSnapshots
                }).ConfigureAwait(false);
                try
                {
                    bool alreadySynchronized;
                    lock (PortraitRosterSyncLock)
                    {
                        alreadySynchronized = SyncedPortraitRosterFingerprints.TryGetValue(campaignId, out string previousFingerprint)
                            && string.Equals(previousFingerprint, rosterFingerprint, StringComparison.Ordinal)
                            && response.Value<bool?>("portraitRosterSyncRequired") == false;
                    }

                    if (alreadySynchronized)
                    {
                        response["portraitRosterOk"] = true;
                        response["portraitRosterUnchanged"] = true;
                        response["portraitCharacterCount"] = portraitCharacters.Count;
                    }
                    else
                    {
                        JObject portraitRosterResponse = await PostJsonAsync("/portrait-roster/upsert-batch", new JObject
                        {
                            ["campaignId"] = campaignId,
                            ["campaignLabel"] = ReignCampaignIdentity.CurrentCampaignLabel(),
                            ["rosterFingerprint"] = rosterFingerprint,
                            ["characters"] = portraitCharacters
                        }).ConfigureAwait(false);
                        bool portraitRosterOk = portraitRosterResponse.Value<bool?>("ok") == true;
                        response["portraitRosterOk"] = portraitRosterOk;
                        response["portraitRosterUnchanged"] = portraitRosterResponse.Value<bool?>("unchanged") == true;
                        response["portraitCharacterCount"] = portraitRosterResponse.Value<int?>("characterCount") ?? portraitCharacters.Count;
                        if (portraitRosterOk)
                        {
                            lock (PortraitRosterSyncLock)
                            {
                                SyncedPortraitRosterFingerprints[campaignId] = rosterFingerprint;
                            }
                        }
                    }
                }
                catch (Exception portraitException)
                {
                    response["portraitRosterOk"] = false;
                    response["portraitRosterError"] = portraitException.Message;
                    ReignLog.Warn("Portrait roster synchronization failed: " + portraitException.Message);
                }
                ReignLog.Info("Identity synchronization ok=" + (response.Value<bool?>("ok") == true)
                    + " heroes=" + heroes.Count
                    + " finances=" + financeSnapshots.Count
                    + " changed=" + (response.Value<bool?>("rosterChanged") == true)
                    + " seeded=" + (response.Value<int?>("seeded") ?? 0)
                    + " upgraded=" + (response.Value<int?>("upgraded") ?? 0));
                return response;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Identity synchronization failed: " + ex.Message);
                return new JObject { ["ok"] = false, ["error"] = ex.Message };
            }
        }

        public static async Task<JObject> MakePlayerKnowEveryoneAsync()
        {
            try
            {
                Hero player = Hero.MainHero;
                if (player == null || string.IsNullOrWhiteSpace(player.StringId))
                {
                    return new JObject { ["ok"] = false, ["error"] = "The player hero is unavailable." };
                }

                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return new JObject { ["ok"] = false, ["error"] = "Local server is disabled." };
                }

                JArray heroes = new JArray();
                foreach (Hero hero in Hero.AllAliveHeroes
                    .Where(x => x != null && x != player && !string.IsNullOrWhiteSpace(x.StringId))
                    .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .OrderBy(x => x.StringId, StringComparer.OrdinalIgnoreCase))
                {
                    heroes.Add(BuildIdentityRosterEntry(hero));
                }

                return await PostJsonAsync("/identity/debug/know-everyone", new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["correlationId"] = NewCorrelationId("identity_debug_know_everyone", player.StringId),
                    ["worldDay"] = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0d : CampaignTime.Now.ToDays,
                    ["testMode"] = true,
                    ["observer"] = BuildIdentityRosterEntry(player),
                    ["heroes"] = heroes
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Make player know everyone failed: " + ex.Message);
                return new JObject { ["ok"] = false, ["error"] = ex.Message };
            }
        }

        public static async Task<JObject> QueryIdentityAsync(Hero observer, Hero subject)
        {
            if (observer == null || subject == null) return new JObject { ["ok"] = false, ["error"] = "Observer and subject are required." };
            return await PostJsonAsync("/identity/query", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["observerHeroStringId"] = observer.StringId,
                ["subjectHeroStringId"] = subject.StringId,
                ["limit"] = 10
            }).ConfigureAwait(false);
        }

        public static async Task<JObject> ResetIdentityAsync(Hero observer, Hero subject)
        {
            if (observer == null || subject == null) return new JObject { ["ok"] = false, ["error"] = "Observer and subject are required." };
            return await PostJsonAsync("/identity/reset", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["correlationId"] = NewCorrelationId("identity_reset", observer.StringId),
                ["observerHeroStringId"] = observer.StringId,
                ["subjectHeroStringId"] = subject.StringId,
                ["testMode"] = true
            }).ConfigureAwait(false);
        }

        public static async Task<ReignCharacterConstructionResult> ConstructAllHeroesAsync(bool force = false)
        {
            ReignCharacterConstructionResult result = new ReignCharacterConstructionResult();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    result.Error = "Local server is disabled.";
                    return result;
                }

                JArray heroes = new JArray();
                foreach (Hero hero in Hero.AllAliveHeroes.Where(IsFixedNobleHero))
                {
                    if (hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                    {
                        continue;
                    }

                    heroes.Add(BuildHeroProfile(hero));
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["heroes"] = heroes,
                    ["force"] = force,
                    ["useLlm"] = true
                };
                JObject response = await PostJsonAsync("/characters/construct-batch", payload).ConfigureAwait(false);
                result.Ok = response.Value<bool?>("ok") == true;
                result.Constructed = response.Value<int?>("constructed") ?? 0;
                result.Failed = response.Value<int?>("failed") ?? 0;
                result.Total = response.Value<int?>("total") ?? heroes.Count;
                result.LlmUsed = response.Value<int?>("llmUsed") ?? 0;
                result.Error = response.Value<string>("error") ?? string.Empty;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                ReignLog.Warn("Generate full living characters failed: " + ex.Message);
                return result;
            }
        }

        public static async Task<JObject> RetryFailedCharactersAsync(Hero hero = null)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "Local server is disabled."
                    };
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId()
                };
                if (hero != null && !string.IsNullOrWhiteSpace(hero.StringId))
                {
                    payload["heroStringId"] = hero.StringId;
                    payload["hero"] = BuildHeroProfile(hero);
                }

                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/characters/retry-failed", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /characters/retry-failed hero=" + (hero?.StringId ?? "<all>")
                    + " clientMs=" + timer.ElapsedMilliseconds
                    + " ok=" + (response.Value<bool?>("ok") == true));
                return response;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Retry failed character generation call failed: " + ex.Message);
                return new JObject
                {
                    ["ok"] = false,
                    ["error"] = ex.Message
                };
            }
        }

        public static async Task<JObject> DiagnosePortraitAsync(Hero hero, string cacheKey, string appearanceKey)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "Local server is disabled."
                    };
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["heroStringId"] = hero?.StringId ?? string.Empty,
                    ["characterObjectId"] = hero?.CharacterObject?.StringId ?? string.Empty,
                    ["cacheKey"] = cacheKey ?? string.Empty,
                    ["appearanceKey"] = appearanceKey ?? string.Empty,
                    ["textureFactoryHas"] = AIPortraits.TextureFactory.Has(cacheKey),
                    ["activeWidgetIds"] = new JArray(
                        "ReignChatPlayerPortrait",
                        "ReignChatNpcPortrait",
                        "ReignChatZoomPortrait",
                        "AIPortraitsNpcPortrait",
                        "AIPortraitsPlayerPortrait",
                        "AIPortraitsZoomPortrait")
                };

                if (hero != null)
                {
                    payload["hero"] = BuildHeroProfile(hero);
                }

                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostJsonAsync("/portraits/diagnose", payload).ConfigureAwait(false);
                timer.Stop();
                ReignLog.Info("HTTP /portraits/diagnose hero=" + (hero?.StringId ?? "")
                    + " cacheKey=" + ShortLog(cacheKey)
                    + " clientMs=" + timer.ElapsedMilliseconds
                    + " ok=" + (response.Value<bool?>("ok") == true));
                return response;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Portrait diagnosis server call failed: " + ex.Message);
                return new JObject
                {
                    ["ok"] = false,
                    ["error"] = ex.Message
                };
            }
        }

        public static async Task<bool> RegisterPortraitFileAsync(string cacheKey, string filePath, string source)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(filePath))
                {
                    return false;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["cacheKey"] = cacheKey,
                    ["filePath"] = filePath,
                    ["source"] = source ?? "game",
                    ["characterObjectId"] = ExtractCharacterObjectId(cacheKey)
                };

                JObject response = await PostJsonAsync("/portraits/register", payload).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Portrait registration failed: " + ex.Message);
                return false;
            }
        }

        public static Task<ReignPortraitGenerationResult> GeneratePortraitAsync(string cacheKey, string heroStringId, string prompt,
            byte[] sourceImagePng, string outputSizeOverride = null, AIPortraits.PortraitPromptContext promptContext = null,
            string promptPurpose = "portrait", string generationOperationId = null, string portraitRefreshMode = "")
            => GeneratePortraitAsync(cacheKey, heroStringId, prompt, sourceImagePng, outputSizeOverride, promptContext,
                promptPurpose, generationOperationId, portraitRefreshMode, null, null);

        internal static async Task<ReignPortraitGenerationResult> GeneratePortraitAsync(string cacheKey, string heroStringId,
            string prompt, byte[] sourceImagePng, string outputSizeOverride, AIPortraits.PortraitPromptContext promptContext,
            string promptPurpose, string generationOperationId, string portraitRefreshMode, Func<bool> requestOwner,
            ReignMainThread.GameScope requestScope)
        {
            ReignPortraitGenerationResult result = new ReignPortraitGenerationResult();
            bool portraitProduct = string.IsNullOrWhiteSpace(promptPurpose)
                || string.Equals(promptPurpose, "portrait", StringComparison.OrdinalIgnoreCase);
            if (portraitProduct && string.IsNullOrWhiteSpace(generationOperationId))
                generationOperationId = Guid.NewGuid().ToString("N");
            result.GenerationOperationId = generationOperationId ?? string.Empty;
            try
            {
				byte[] compatibleSource = portraitProduct ? (portraitRefreshMode == "reflect_years" ? sourceImagePng : null) : AIPortraits.PngReencode.NormalizeImageEditSource(sourceImagePng);
				if (!portraitProduct && (compatibleSource == null || compatibleSource.Length == 0))
				{
					result.Ok = false;
					result.Error = "Portrait source could not be normalized to an opaque PNG within the 384-5000 pixel and 10 MB image-edit limits.";
					return result;
				}
				sourceImagePng = compatibleSource;
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    result.Error = "Local Bannerlord Reign server is disabled.";
                    return result;
                }

                if (!portraitProduct && (sourceImagePng == null || sourceImagePng.Length == 0))
                {
                    result.Error = "No source image was captured.";
                    return result;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["cacheKey"] = cacheKey ?? string.Empty,
                    ["heroStringId"] = heroStringId ?? string.Empty,
                    ["characterObjectId"] = ExtractCharacterObjectId(cacheKey),
                    ["prompt"] = prompt ?? string.Empty,
                    ["portraitRefreshMode"] = portraitRefreshMode,
                    ["promptPurpose"] = string.IsNullOrWhiteSpace(promptPurpose) ? "portrait" : promptPurpose,
                    ["sourceImageBase64"] = sourceImagePng == null ? null : Convert.ToBase64String(sourceImagePng)
                };
                if (portraitProduct)
                {
                    payload["nativeCharacterSnapshot"] = PortraitRequestScope.Current?.Snapshot.DeepClone();
                    payload["generationOperationId"] = generationOperationId;
                    payload["generationStartedUtc"] = DateTime.UtcNow.ToString("o");
                }
                if (promptContext != null)
                {
                    payload["characterName"] = promptContext.CharacterName ?? string.Empty;
                    payload["ageYears"] = promptContext.AgeYears.HasValue ? (JToken)promptContext.AgeYears.Value : JValue.CreateNull();
                    payload["cultureId"] = promptContext.CultureId ?? string.Empty;
                    payload["cultureName"] = promptContext.CultureName ?? string.Empty;
                    payload["gender"] = promptContext.Gender ?? string.Empty;
					payload["clanTier"] = promptContext.ClanTier.HasValue ? (JToken)promptContext.ClanTier.Value : JValue.CreateNull();
					payload["socialStation"] = promptContext.SocialStation ?? string.Empty;
					payload["occupation"] = promptContext.Occupation ?? string.Empty;
					payload["isNotable"] = promptContext.IsNotable;
                }
                if (!string.IsNullOrWhiteSpace(outputSizeOverride))
                {
                    payload["outputSize"] = outputSizeOverride;
                }

                string url = BuildUrl("/portraits/generate");
                int requestChars = payload.ToString(Formatting.None).Length;
                ReignLog.Info("HTTP /portraits/generate starting url=" + url
                    + " cacheKey=" + ShortLog(cacheKey)
                    + " hero=" + (heroStringId ?? "")
                    + " sourceBytes=" + (sourceImagePng?.Length ?? 0)
                    + " requestChars=" + requestChars);

                Stopwatch timer = Stopwatch.StartNew();
                JObject response = await PostPortraitRequestAsync("/portraits/generate", payload, requestOwner, requestScope).ConfigureAwait(false);
                timer.Stop();
                PopulatePortraitGenerationResult(result, response);
                result.ClientTotalMs = timer.ElapsedMilliseconds;

                ReignLog.Info("HTTP /portraits/generate cacheKey=" + ShortLog(cacheKey)
                    + " hero=" + (heroStringId ?? "")
                    + " provider=" + result.Provider
                    + " model=" + result.Model
                    + " adapter=" + result.Adapter
                    + " clientMs=" + result.ClientTotalMs
                    + " serverMs=" + result.ServerDurationMs
                    + " ok=" + result.Ok
                    + " timing=" + result.TimingSummary);
                return result;
            }
            catch (ReignRequestOwnerAbandonedException ex)
            {
                result.Error = ex.Message;
                return result;
            }
            catch (Exception ex)
            {
                bool unknownOutcome = ex is ReignHttpTransport.UnknownMutationOutcomeException;
                if (portraitProduct && (ex is OperationCanceledException || unknownOutcome)
                    && !string.IsNullOrWhiteSpace(generationOperationId))
                {
                    ReignPortraitGenerationResult recovered = await RecoverPortraitGenerationAsync(
                        cacheKey, heroStringId, generationOperationId, 180000, requestOwner, requestScope).ConfigureAwait(false);
                    if (recovered != null && recovered.Ok && recovered.ProductAccepted)
                    {
                        ReignLog.Info("Recovered completed portrait operation after the generation response was unavailable operation="
                            + generationOperationId + " hero=" + (heroStringId ?? ""));
                        return recovered;
                    }
                    // A fast transport failure can precede registration on the
                    // server. Keep the exact operation for the next status check.
                    if (recovered != null && (recovered.GenerationMayStillComplete
                        || (unknownOutcome && string.Equals(recovered.GenerationStatus, "not_found", StringComparison.OrdinalIgnoreCase))))
                    {
                        result.GenerationStatus = recovered.GenerationStatus;
                        result.GenerationMayStillComplete = true;
                    }
                }
                string error = unknownOutcome && result.GenerationMayStillComplete
                    ? "The portrait request's outcome is unknown. Reign will check this operation before requesting another portrait."
                    : BuildPortraitServerError(ex);
                result.Error = error;
                ReignLog.Warn("Portrait generation server call failed url=" + BuildUrl("/portraits/generate")
                    + " cacheKey=" + ShortLog(cacheKey)
                    + " hero=" + (heroStringId ?? "")
                    + " sourceBytes=" + (sourceImagePng == null ? 0 : sourceImagePng.Length)
                    + " error=" + DescribeExceptionChain(ex));
                return result;
            }
        }

        private static Task<JObject> PostPortraitRequestAsync(string route, JObject payload, Func<bool> requestOwner,
            ReignMainThread.GameScope requestScope)
            => requestOwner == null ? PostJsonAsync(route, payload) : PostOwnedJsonAsync(route, payload, requestOwner, requestScope);

        public static Task<ReignPortraitGenerationResult> RecoverPortraitGenerationAsync(
            string cacheKey, string heroStringId, string generationOperationId, int maxWaitMs = 0)
            => RecoverPortraitGenerationAsync(cacheKey, heroStringId, generationOperationId, maxWaitMs, null, null);

        internal static async Task<ReignPortraitGenerationResult> RecoverPortraitGenerationAsync(string cacheKey,
            string heroStringId, string generationOperationId, int maxWaitMs, Func<bool> requestOwner, ReignMainThread.GameScope requestScope)
        {
            ReignPortraitGenerationResult result = new ReignPortraitGenerationResult
            {
                GenerationOperationId = generationOperationId ?? string.Empty
            };
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                do
                {
                    JObject response = await PostPortraitRequestAsync("/portraits/generate/status", new JObject
                    {
                        ["campaignId"] = GetCampaignId(),
                        ["cacheKey"] = cacheKey ?? string.Empty,
                        ["heroStringId"] = heroStringId ?? string.Empty,
                        ["generationOperationId"] = generationOperationId ?? string.Empty
                    }, requestOwner, requestScope).ConfigureAwait(false);
                    PopulatePortraitGenerationResult(result, response);
                    result.ClientTotalMs = timer.ElapsedMilliseconds;
                    if (string.Equals(result.GenerationStatus, "completed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(result.GenerationStatus, "failed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(result.GenerationStatus, "not_found", StringComparison.OrdinalIgnoreCase)
                        || maxWaitMs <= 0)
                    {
                        return result;
                    }
                    result.GenerationMayStillComplete = true;
                    await Task.Delay(2500).ConfigureAwait(false);
                }
                while (timer.ElapsedMilliseconds < maxWaitMs);

                result.GenerationStatus = "pending";
                result.GenerationMayStillComplete = true;
                result.Error = "Reign is still waiting for the portrait provider. The completed portrait will be recovered instead of generating it again.";
                return result;
            }
            catch (ReignRequestOwnerAbandonedException ex)
            {
                result.GenerationStatus = "owner_closed";
                result.GenerationMayStillComplete = true;
                result.Error = ex.Message;
                return result;
            }
            catch (Exception ex)
            {
                result.GenerationStatus = "unavailable";
                result.GenerationMayStillComplete = true;
                result.Error = "Reign could not check the portrait operation yet: " + DescribeExceptionChain(ex);
                return result;
            }
        }

        private static void PopulatePortraitGenerationResult(ReignPortraitGenerationResult result, JObject response)
        {
            response = response ?? new JObject();
            result.Ok = response.Value<bool?>("ok") == true;
            result.Error = response.Value<string>("error") ?? string.Empty;
            result.Provider = response.Value<string>("provider") ?? string.Empty;
            result.Model = response.Value<string>("model") ?? string.Empty;
            result.Adapter = response.Value<string>("adapter") ?? string.Empty;
            result.ServerDurationMs = response.Value<long?>("durationMs") ?? 0;
            result.TimingSummary = FormatTimingSummary(response["timing"] as JObject);
            result.GenerationOperationId = response.Value<string>("generationOperationId")
                ?? result.GenerationOperationId ?? string.Empty;
            string WireString(string name) => response[name]?.Type == JTokenType.String
                ? response[name].Value<string>() : string.Empty;
            result.WireOperationId = WireString("generationOperationId");
            result.WireCampaignId = WireString("campaignId");
            result.WireHeroId = WireString("heroStringId");
            result.WireCacheKey = WireString("cacheKey");
            result.GenerationStatus = response.Value<string>("generationStatus") ?? string.Empty;
            result.Recovered = response.Value<bool?>("recovered") == true;
            result.GenerationMayStillComplete = string.Equals(result.GenerationStatus, "pending", StringComparison.OrdinalIgnoreCase);
            string imageBase64 = response.Value<string>("imageBase64") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(imageBase64)) result.ImageBytes = Convert.FromBase64String(imageBase64);
            result.EffectivePrompt = response.Value<string>("effectivePrompt") ?? string.Empty;
            string sourceBase64 = response.Value<string>("sourceImageBase64") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sourceBase64)) result.SourceImageBytes = Convert.FromBase64String(sourceBase64);

            JObject receipt = response["portraitProductReceipt"] as JObject;
            JObject portraitInput = response["portraitInput"] as JObject;
            result.PortraitProductReceiptJson = receipt?.ToString(Formatting.Indented) ?? string.Empty;
            result.PortraitInputJson = portraitInput?.ToString(Formatting.Indented) ?? string.Empty;
            result.ProductSchema = receipt?.Value<string>("schema") ?? string.Empty;
            result.ProductVersion = receipt?.Value<int?>("version") ?? 0;
            result.ProductAccepted = receipt?.Value<bool?>("accepted") == true;
            JObject master = receipt?["master"] as JObject;
            JObject source = receipt?["source"] as JObject;
            JObject promptReceipt = receipt?["prompt"] as JObject;
            JObject focus = receipt?["focus"] as JObject;
            result.PortraitWidth = master?.Value<int?>("width") ?? 0;
            result.PortraitHeight = master?.Value<int?>("height") ?? 0;
            result.PortraitSha256 = master?.Value<string>("sha256") ?? string.Empty;
            result.SourceWidth = source?.Value<int?>("width") ?? 0;
            result.SourceHeight = source?.Value<int?>("height") ?? 0;
            result.SourceSha256 = source?.Value<string>("sha256") ?? string.Empty;
            result.PromptSha256 = promptReceipt?.Value<string>("sha256") ?? string.Empty;
            if (focus != null)
            {
                result.FaceFocus = new PortraitFaceFocus
                {
                    Method = focus.Value<string>("method") ?? string.Empty,
                    Model = focus.Value<string>("model") ?? string.Empty,
                    Confidence = focus.Value<double?>("confidence") ?? 0d,
                    CandidateCount = focus.Value<int?>("candidateCount") ?? 0,
                    X = focus.Value<double?>("x") ?? 0d,
                    Y = focus.Value<double?>("y") ?? 0d,
                    Width = focus.Value<double?>("width") ?? 0d,
                    Height = focus.Value<double?>("height") ?? 0d
                };
            }
        }

        private static string BuildPortraitServerError(Exception ex)
        {
            string details = DescribeExceptionChain(ex);
            string baseUrl = ReignBetaSettings.Instance?.LocalServerUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://127.0.0.1:5101";
            }

            if (details.IndexOf("actively refused", StringComparison.OrdinalIgnoreCase) >= 0
                || details.IndexOf("10061", StringComparison.OrdinalIgnoreCase) >= 0
                || details.IndexOf("No connection could be made", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Local Bannerlord Reign server is not running at " + baseUrl.TrimEnd('/') + ". Start the server, then try Look at them again.";
            }

            if (ex is OperationCanceledException)
            {
                return "The game stopped waiting for the portrait response. Reign will retain and recover the completed provider result instead of charging for another generation.";
            }

            if (details.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                || details.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Local Bannerlord Reign server did not answer in time at " + baseUrl.TrimEnd('/') + ". Check the server window/logs, then try again.";
            }

            return string.IsNullOrWhiteSpace(details)
                ? "Portrait generation could not reach the local Bannerlord Reign server."
                : "Portrait generation server call failed: " + details;
        }

        private static string DescribeExceptionChain(Exception ex)
        {
            if (ex == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            Exception current = ex;
            while (current != null)
            {
                string message = current.GetType().Name + ": " + current.Message;
                if (!parts.Contains(message))
                {
                    parts.Add(message);
                }
                current = current.InnerException;
            }

            return string.Join(" | ", parts);
        }

        public static Task<JObject> PostCastleSceneAsync(JObject payload)
        {
            return PostJsonAsync("/castle-chat/scene/generate", payload ?? new JObject());
        }

        public static Task<JObject> PostFamilyChambersSceneAsync(JObject payload)
        {
            return PostJsonAsync("/family-chambers/scene/generate", payload ?? new JObject());
        }

        public static async Task<bool> ReconcileChildhoodMaturityAsync(Hero hero)
        {
            if (hero == null || hero.IsChild || string.IsNullOrWhiteSpace(hero.StringId)) return false;
            JObject response = await PostJsonAsync("/family-chambers/childhood/mature", new JObject
            {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["hero"] = BuildHeroProfile(hero), ["worldDay"] = CampaignTime.Now.ToDays
            }).ConfigureAwait(false);
            return response.Value<bool?>("ok") == true;
        }

        internal static JObject BuildFamilyChambersHeroProfile(Hero hero)
        {
            JObject profile = BuildHeroProfile(hero);
            int? familyRelation = ReignServices.Court?.GetFamilyDirectionalRelation(hero);
            if (familyRelation.HasValue) profile["relationToPlayer"] = familyRelation.Value;
            if (hero?.CharacterObject == null) return profile;

            FaceGenerationParams face = FaceGenerationParams.Create();
            MBBodyProperties.GetParamsFromKey(ref face, hero.BodyProperties, earsAreHidden: false, mouthHidden: false);
            profile["familyVisualAppearance"] = new JObject
            {
                ["ancestry"] = FamilyCultureAncestry(hero.Culture?.StringId),
                ["nativeRace"] = hero.CharacterObject.Race == 0 ? "human" : "human of a native Calradian ancestry",
                ["hairColor"] = FamilyHairColor(face.CurrentHairColorOffset),
                ["skinTone"] = FamilySkinTone(face.CurrentSkinColorOffset),
                ["eyeColor"] = FamilyEyeColor(face.CurrentEyeColorOffset)
            };
            return profile;
        }

        private static string FamilyCultureAncestry(string cultureId)
        {
            switch ((cultureId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "sturgia": return "Sturgian";
                case "vlandia": return "Vlandian";
                case "battania": return "Battanian";
                case "aserai": return "Aserai";
                case "khuzait": return "Khuzait";
                case "empire": return "Imperial Calradian";
                default: return "Calradian";
            }
        }

        private static string FamilyHairColor(float value)
        {
            if (value < 0.2f) return "black";
            if (value < 0.4f) return "dark brown";
            if (value < 0.6f) return "chestnut brown";
            if (value < 0.8f) return "light brown";
            return "fair blond";
        }

        private static string FamilySkinTone(float value)
        {
            if (value < 0.2f) return "deep brown";
            if (value < 0.4f) return "warm brown";
            if (value < 0.6f) return "olive";
            if (value < 0.8f) return "light olive";
            return "fair";
        }

        private static string FamilyEyeColor(float value)
        {
            if (value < 0.25f) return "dark brown";
            if (value < 0.5f) return "hazel";
            if (value < 0.75f) return "gray";
            return "blue";
        }

        public static Task<JObject> GenerateSharedPortraitAsync(string cacheKey)
        {
            return PostJsonAsync("/portraits/shared-generation/generate-one", new JObject
            {
                ["cacheKey"] = cacheKey ?? string.Empty,
                ["force"] = false
            });
        }
    }
}
