using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using ReignBeta.Characters.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Persistence.Save;
using ReignBeta.Persistence.Integration;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Characters.Campaign
{
    internal sealed class ReignPersonalityPreparationResult
    {
        internal bool Ok { get; set; }
        internal int CharacterCount { get; set; }
        internal long DurationMs { get; set; }
    }

    public sealed partial class ReignCharacterEditorCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignCharacterEditorCampaignBehavior Instance => ReignServices.CharacterEditor;
        public bool NotableBackgroundInitializationComplete => _notableBackgroundInitializationComplete;
        public bool NotableBackgroundInitializationInFlight => _notableBackgroundInitializationInFlight;

        private bool _polling;
        private TaleWorlds.CampaignSystem.Campaign _owningCampaign;
        private bool IsCurrentCampaign => _owningCampaign != null
            && ReferenceEquals(_owningCampaign, TaleWorlds.CampaignSystem.Campaign.Current)
            && ReferenceEquals(Instance, this);
        private bool _identitySyncing;
        private bool _notableBackgroundInitializationComplete;
        private bool _notableBackgroundInitializationInFlight;
        private float _lastPollDay = -1000f;
        private float _lastIdentitySyncDay = -1000f;
        // Coordinated with the fully qualified server browser and native dispatcher.
        private static bool OwnedManualNativeEditorDeliveryReady => false;
        private bool _nativeEditorReceiptStorageReady = true;
        private List<string> _unreadableNativeEditorReceipts;
        private Dictionary<string, NativeEditorReceipt> _nativeEditorReceipts = new Dictionary<string, NativeEditorReceipt>(StringComparer.Ordinal);
        private readonly HashSet<string> _restoredNativeEditorReceiptIds = new HashSet<string>(StringComparer.Ordinal);

        public ReignCharacterEditorCampaignBehavior()
        {
            ReignServices.CharacterEditor = this;
        }

        public override void RegisterEvents()
        {
            _owningCampaign = TaleWorlds.CampaignSystem.Campaign.Current;
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnProfileTownEntered);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnProfileTownLeft);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reignCharacterEditor_lastPollDay", ref _lastPollDay);
            dataStore.SyncData("_reignIdentity_lastSyncDay", ref _lastIdentitySyncDay);
            dataStore.SyncData("_reignNotableBackgroundInitializationComplete", ref _notableBackgroundInitializationComplete);
            List<string> chunks = null;
            if (dataStore.IsSaving)
                chunks = _nativeEditorReceiptStorageReady
                    ? ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_nativeEditorReceipts))
                    : _unreadableNativeEditorReceipts;
            dataStore.SyncData("_reignCharacterEditor_nativeReceipts_v1", ref chunks);
            if (!dataStore.IsLoading) return;
            _restoredNativeEditorReceiptIds.Clear();

            try
            {
                var loaded = ReignSavePayloadCodec.DecodeAuthoritativeState(chunks, null,
                    json => JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error })
                        .ToObject<Dictionary<string, NativeEditorReceipt>>(),
                    () => new Dictionary<string, NativeEditorReceipt>(StringComparer.Ordinal));
                if (loaded.Any(pair => string.IsNullOrEmpty(pair.Key) || pair.Value == null || !pair.Value.IsValid()))
                    throw new InvalidOperationException("A native editor receipt is incomplete.");
                _nativeEditorReceipts = loaded;
                _restoredNativeEditorReceiptIds.UnionWith(loaded.Keys);
                _nativeEditorReceiptStorageReady = true;
                _unreadableNativeEditorReceipts = null;
            }
            catch (Exception ex) when (ex is JsonException || ex is System.IO.InvalidDataException
                || ex is FormatException || ex is InvalidOperationException)
            {
                _nativeEditorReceiptStorageReady = false;
                _unreadableNativeEditorReceipts = chunks;
                ReignLog.Warn("Character editor native receipts could not be loaded. The saved receipts were retained and new native edits are paused.");
            }
        }
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            ReignLog.Info(_notableBackgroundInitializationComplete
                ? "Permanent notable personalities are already prepared."
                : "Permanent notable personality preparation is pending in the campaign readiness coordinator.");
        }
        private void OnHourlyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            StartPoll(false);
            StartIdentitySync(false);
        }

        public void ApplicationTick(float deltaSeconds)
        {
            if (!IsCurrentCampaign) return;
            TryPrewarmTownProfiles();
            if (!ReignCampaignInitializationGate.IsPending)
            {
                StartPoll(false);
                StartIdentitySync(false);
            }
        }

        internal async Task<ReignPersonalityPreparationResult> EnsureInitialPersonalitiesAsync(
            string generationId,
            Action<int, int, string> progress)
        {
            if (_notableBackgroundInitializationComplete)
            {
                return new ReignPersonalityPreparationResult { Ok = true };
            }
            if (_notableBackgroundInitializationInFlight)
                throw new InvalidOperationException("Notable personality preparation is already running.");
            if (TaleWorlds.CampaignSystem.Campaign.Current == null)
                throw new InvalidOperationException("No campaign is loaded.");

            List<Hero> notables = await ReignMainThread.InvokeAsync(() =>
                Hero.AllAliveHeroes
                    .Where(x => x != null && x.IsAlive && x.IsNotable)
                    .GroupBy(x => x.StringId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .ToList()).ConfigureAwait(false);
            _notableBackgroundInitializationInFlight = true;
            ReignCampaignInitializationGate.BeginProfileGeneration(notables.Count);
            progress?.Invoke(0, notables.Count, "Generating permanent personalities");
            return await InitializeNotableBackgroundCharactersAsync(
                generationId,
                notables,
                progress).ConfigureAwait(false);
        }

        private async Task<ReignPersonalityPreparationResult> InitializeNotableBackgroundCharactersAsync(
            string generationId,
            List<Hero> notables,
            Action<int, int, string> progress)
        {
            DateTime startedUtc = DateTime.UtcNow;
            try
            {
                bool typedStartup = OwnedManualNativeEditorDeliveryReady;
                ReignServerClient.RequestFence startupFence = null;
                string startupCampaign = null;
                JObject startupOwner = null;
                bool OwnsStartup() => IsCurrentCampaign && startupFence?.IsCurrentActionPoll() == true
                    && ReignCampaignInitializationGate.IsActiveGeneration(generationId);
                Task<JObject> initialize;
                if (typedStartup)
                {
                    initialize = await ReignMainThread.InvokeAsync(() =>
                    {
                        startupFence = ReignServerClient.RequestFence.Capture();
                        if (!OwnsStartup())
                            throw new InvalidOperationException("The notable startup belongs to a different native preparation.");
                        startupCampaign = ReignServerClient.GetCampaignId();
                        startupOwner = new JObject
                        {
                            ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                            ["restoreGeneration"] = startupFence.SharedActivityGeneration,
                            ["preparationId"] = generationId
                        };
                        return ReignCharacterEditorClient.InitializeNotableMbtiProfilesAsync(notables, startupCampaign,
                            startupOwner, OwnsStartup);
                    }).ConfigureAwait(false);
                }
                else initialize = ReignCharacterEditorClient.InitializeNotableMbtiProfilesAsync(notables);
                JObject initialized = await initialize.ConfigureAwait(false);
                EnsureActiveGeneration(generationId);
                if (typedStartup && (initialized?.Value<int?>("typedNotableStartupVersion") != 1
                    || !JToken.DeepEquals(initialized["startupNativeOwner"], startupOwner)))
                    throw new InvalidOperationException("The server did not acknowledge the exact native startup owner.");
                if (initialized?.Value<bool?>("ok") != true)
                    throw new InvalidOperationException("The server did not generate every notable personality ("
                        + (initialized?.Value<int?>("assignmentCount") ?? 0) + " of "
                        + (initialized?.Value<int?>("requestedCount") ?? notables.Count) + ").");
                JArray assignments = initialized["assignments"] as JArray ?? new JArray();
                if (assignments.Count != notables.Count)
                    throw new InvalidOperationException("The server returned an incomplete notable assignment set.");
                ReignLog.Info("Background profile assignment completed for " + assignments.Count
                    + " characters"
                    + " (server database " + (initialized.Value<long?>("databaseMs") ?? -1)
                    + " ms, materialization " + (initialized.Value<long?>("materializationMs") ?? -1) + " ms).");
                ReignCampaignInitializationGate.ReportProgress(
                    "applying_native_traits",
                    0,
                    notables.Count);
                progress?.Invoke(0, notables.Count, "Applying native personality traits");

                JArray observations = await ReignMainThread.InvokeAsync(() =>
                {
                    EnsureActiveGeneration(generationId);
                    if (typedStartup && !OwnsStartup())
                        throw new InvalidOperationException("The notable startup no longer owns the loaded campaign.");
                    Dictionary<string, Hero> byId = notables
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                        .ToDictionary(x => x.StringId, x => x, StringComparer.OrdinalIgnoreCase);
                    JArray applied = new JArray();
                    foreach (JObject assignment in assignments.OfType<JObject>())
                    {
                        string heroId = assignment.Value<string>("heroId") ?? string.Empty;
                        if (!byId.TryGetValue(heroId, out Hero hero))
                            throw new InvalidOperationException("Notable '" + heroId + "' no longer exists in the loaded campaign.");
                        if (assignment["deferNativeTraitsToQueue"]?.Type == JTokenType.Boolean
                            && assignment.Value<bool>("deferNativeTraitsToQueue"))
                        {
                            applied.Add(new JObject { ["heroId"] = heroId, ["status"] = "native_delivery_deferred" });
                            continue;
                        }
                        string startupSession = assignment.Value<string>("startupNativeSessionId") ?? "";
                        if (typedStartup && (assignment["startupNativeSessionId"]?.Type != JTokenType.String
                            || !ReignCharacterEditorClient.IsNativeOwnerNonce(startupSession)))
                            throw new InvalidOperationException("Notable '" + heroId + "' has no current startup reservation.");
                        if (!hero.IsAlive)
                        {
                            var retired = new JObject { ["heroId"] = heroId, ["status"] = "obsolete" };
                            if (typedStartup) retired["startupNativeSessionId"] = startupSession;
                            applied.Add(retired);
                            continue;
                        }
                        JObject traits = assignment["nativeTraits"] as JObject;
                        if (traits == null || traits.Count != 5)
                            throw new InvalidOperationException("Notable '" + heroId + "' has an incomplete native trait assignment.");
                        if (!typedStartup || !JToken.DeepEquals(ReignCharacterEditorClient.NativePersonalityTraits(hero), traits))
                            ApplyTraits(hero, traits);
                        var measured = new JObject
                        {
                            ["heroId"] = heroId,
                            ["traits"] = ReignCharacterEditorClient.NativePersonalityTraits(hero)
                        };
                        if (typedStartup) measured["startupNativeSessionId"] = startupSession;
                        applied.Add(measured);
                    }
                    return applied;
                }).ConfigureAwait(false);
                ReignCampaignInitializationGate.ReportProgress(
                    "confirming_native_traits",
                    observations.Count,
                    notables.Count);
                progress?.Invoke(observations.Count, notables.Count, "Confirming native personality traits");

                var confirm = typedStartup
                    ? await ReignMainThread.InvokeAsync(() => ReignCharacterEditorClient.ConfirmNotableMbtiProfilesAsync(
                        observations, startupCampaign, startupOwner, OwnsStartup)).ConfigureAwait(false)
                    : ReignCharacterEditorClient.ConfirmNotableMbtiProfilesAsync(observations);
                JObject confirmed = await confirm.ConfigureAwait(false);
                EnsureActiveGeneration(generationId);
                int confirmedCount = confirmed?.Value<int?>("confirmedCount") ?? -1;
                int expectedDeferredCount = observations.OfType<JObject>()
                    .Count(item => item.Value<string>("status") == "native_delivery_deferred");
                JToken deferred = confirmed?["deferredCount"];
                int deferredCount = deferred == null ? 0 : deferred.Type == JTokenType.Integer ? deferred.Value<int>() : -1;
                if (confirmed?.Value<bool?>("ok") != true || deferredCount != expectedDeferredCount
                    || confirmedCount + deferredCount != notables.Count)
                    throw new InvalidOperationException("The server did not acknowledge every notable startup observation ("
                        + Math.Max(confirmedCount, 0) + " confirmed, " + deferredCount + " deferred).");
                if (deferredCount > 0)
                    ReignLog.Info(deferredCount + " notable assignments retained their existing native delivery owners.");

                await ReignMainThread.InvokeAsync(() =>
                {
                    if (typedStartup && !OwnsStartup())
                        throw new InvalidOperationException("The notable startup no longer owns the loaded campaign.");
                    _notableBackgroundInitializationComplete = true;
                    _notableBackgroundInitializationInFlight = false;
                }).ConfigureAwait(false);
                long durationMs = Math.Max(0L, (long)(DateTime.UtcNow - startedUtc).TotalMilliseconds);
                ReignLog.Info("Background character generation completed for " + notables.Count
                    + " characters in " + durationMs + " ms"
                    + " (server confirmation " + (confirmed.Value<long?>("totalMs") ?? -1) + " ms).");
                return new ReignPersonalityPreparationResult
                {
                    Ok = true,
                    CharacterCount = notables.Count,
                    DurationMs = durationMs
                };
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Background character initialization failed: " + ex);
                ReignCampaignInitializationGate.ReportProgress(
                    "failed_waiting_for_retry",
                    0,
                    notables?.Count ?? 0);
                await ReignMainThread.InvokeAsync(() =>
                {
                    _notableBackgroundInitializationInFlight = false;
                }).ConfigureAwait(false);
                throw;
            }
        }

        private static void EnsureActiveGeneration(string generationId)
        {
            if (!ReignCampaignInitializationGate.IsActiveGeneration(generationId))
                throw new InvalidOperationException(
                    "The notable personality result belongs to a stale campaign generation.");
        }

        private void StartPoll(bool force)
        {
            if (!IsCurrentCampaign || _polling || Hero.MainHero == null) return;
            if (!_nativeEditorReceiptStorageReady) return;
            float day = (float)CampaignTime.Now.ToDays;
            if (!force && day - _lastPollDay < 0.2f) return;
            string campaignId = ReignServerClient.GetCampaignId();
            var fence = ReignServerClient.RequestFence.Capture();
            string timelineId = ReignServices.WorldHistory?.TimelineId ?? "main";
            var verified = OwnedManualNativeEditorDeliveryReady ? ReignSaveSyncCoordinator.CurrentVerifiedLoad : null;
            if (verified != null && (verified.CampaignId != campaignId || verified.RestoreGeneration != fence.SharedActivityGeneration
                || !ReignCharacterEditorClient.IsNativeOwnerNonce(verified.LoadSessionId)))
                verified = null;
            bool OwnsPoll() => IsCurrentCampaign && fence.IsCurrentActionPoll()
                && !ReignCampaignInitializationGate.IsPending
                && (verified == null || ReferenceEquals(verified, ReignSaveSyncCoordinator.CurrentVerifiedLoad));
            if (!OwnsPoll()) return;
            _lastPollDay = day;
            _polling = true;
            _ = PollAsync(campaignId, timelineId, fence.SharedActivityGeneration, verified?.LoadSessionId ?? "", OwnsPoll);
        }

        private async Task PollAsync(string campaignId, string timelineId, string restoreGeneration, string nativeEditorSession,
            Func<bool> ownsPoll)
        {
            try
            {
                var commands = await ReignCharacterEditorClient.PollCharacterEditorCommandsAsync(campaignId,
                    timelineId, restoreGeneration, nativeEditorSession, ownsPoll).ConfigureAwait(false);
                foreach (ReignCharacterEditorCommand command in commands)
                {
                    if (command.IsAutomaticNative && !OwnedManualNativeEditorDeliveryReady) continue;
                    if (command.IsManualNative && (nativeEditorSession.Length == 0 || command.ManualClaimSessionId != nativeEditorSession))
                        continue;
                    NativeEditorReceipt result = await ReignMainThread.InvokeAsync(() =>
                        ownsPoll() ? ApplyOrReadSavedReceipt(campaignId, timelineId, command) : null).ConfigureAwait(false);
                    if (result == null)
                    {
                        if (!await ReignMainThread.InvokeAsync(ownsPoll).ConfigureAwait(false)) return;
                        continue;
                    }
                    await ReignCharacterEditorClient.ReportCharacterEditorCommandAsync(campaignId, timelineId,
                        restoreGeneration, command, result.Status, result.Applied, result.Errors, ownsPoll).ConfigureAwait(false);
                }
            }
            catch (ReignRequestOwnerAbandonedException) { }
            catch (Exception ex) { ReignLog.Warn("Character editor campaign poll failed: " + ex.Message); }
            finally { _polling = false; }
        }

        private NativeEditorReceipt ApplyOrReadSavedReceipt(string campaignId, string timelineId, ReignCharacterEditorCommand command)
        {
            if (string.IsNullOrEmpty(command.CommandId) || command.ReceiptOnly
                && !_restoredNativeEditorReceiptIds.Contains(command.CommandId))
                return null;
            // Property order is part of the native edit because some fields depend on earlier changes.
            var material = new JObject { ["hero"] = command.HeroId, ["dangerous"] = command.Dangerous,
                ["confirmation"] = command.Confirmation, ["changes"] = command.Changes };
            using var sha = SHA256.Create();
            string digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(material.ToString(Formatting.None)))).Replace("-", "");
            if (_nativeEditorReceipts.TryGetValue(command.CommandId, out var retained))
            {
                // The same saved outcome can answer a newly issued claim for the exact command.
                if (retained.Matches(campaignId, timelineId, digest, command))
                    return retained;
                ReignLog.Warn("The saved Character Editor receipt does not match the current command or campaign scope.");
                return null;
            }

            // A reissued or restored claim can only read the native receipt saved with this campaign.
            if (command.ReceiptOnly) return null;

            // The native effect and its receipt are recorded in the same campaign-thread callback.
            var result = Apply(command);
            var receipt = new NativeEditorReceipt
            {
                CampaignId = campaignId, TimelineId = timelineId, MaterialSha256 = digest,
                ManualOriginLineageId = command.IsManualNative ? command.ManualOriginLineageId : null,
                ManualOriginMaterialSha256 = command.IsManualNative ? command.ManualOriginMaterialSha256 : null,
                AutomaticOriginId = command.IsAutomaticNative ? command.AutomaticOriginId : null,
                AutomaticOriginMaterialSha256 = command.IsAutomaticNative ? command.AutomaticOriginMaterialSha256 : null,
                Applied = result.Applied.ToArray(), Errors = result.Errors.ToArray()
            };
            _nativeEditorReceipts.Add(command.CommandId, receipt);
            return receipt;
        }

        private void StartIdentitySync(bool force)
        {
            if (!IsCurrentCampaign || _identitySyncing || Hero.MainHero == null) return;
            float day = (float)CampaignTime.Now.ToDays;
            if (!force && day - _lastIdentitySyncDay < 1f) return;
            _lastIdentitySyncDay = day;
            _identitySyncing = true;
            _ = SynchronizeIdentityAsync();
        }

        private async Task SynchronizeIdentityAsync()
        {
            try
            {
                await ReignServerClient.SynchronizeIdentityNetworkAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { ReignLog.Warn("Campaign identity synchronization failed: " + ex.Message); }
            finally { _identitySyncing = false; }
        }

        internal void MarkIdentitySynchronizedForCurrentDay()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            _lastIdentitySyncDay = (float)CampaignTime.Now.ToDays;
        }

        private static CharacterEditorApplyResult Apply(ReignCharacterEditorCommand command)
        {
            CharacterEditorApplyResult result = new CharacterEditorApplyResult();
            Hero hero = ReignObjectResolver.FindHero(command?.HeroId);
            if (hero == null) { result.Errors.Add("Hero was not found in this savegame."); return result; }
            if (command.IsAutomaticNative) return ApplyTypedAutomaticNotableTraits(hero, command, result);
            if (!hero.IsAlive
                && (command?.CommandId ?? string.Empty).StartsWith(
                    "notable_mbti_native_", StringComparison.OrdinalIgnoreCase))
            {
                // Notables can die between server assignment and the next native
                // command poll. Their permanent MBTI remains valid, but there is
                // no living native trait container left to update.
                result.Applied.Add("traits");
                return result;
            }
            foreach (JProperty property in (command.Changes ?? new JObject()).Properties())
            {
                try
                {
                    if (ApplyField(hero, property.Name, property.Value, result)) result.Applied.Add(property.Name);
                    else result.Errors.Add(property.Name + ": this native field is not supported by the current Bannerlord runtime.");
                }
                catch (Exception ex) { result.Errors.Add(property.Name + ": " + ex.Message); }
            }
            return result;
        }

        private static CharacterEditorApplyResult ApplyTypedAutomaticNotableTraits(Hero hero, ReignCharacterEditorCommand command,
            CharacterEditorApplyResult result)
        {
            if (!hero.IsAlive)
            {
                result.Errors.Add("The notable is no longer living.");
                return result;
            }
            try
            {
                var names = new[] { "valor", "generosity", "honor", "mercy", "calculating" };
                var traits = command.Changes?["traits"] as JObject;
                if (command.Changes?.Count != 1 || traits?.Count != names.Length
                    || names.Any(key => traits[key]?.Type != JTokenType.Integer || traits.Value<long>(key) < -2 || traits.Value<long>(key) > 2))
                    throw new InvalidOperationException("Typed notable traits are outside the supported native range or shape.");
                ApplyTraits(hero, traits);
                var observed = ReignCharacterEditorClient.NativePersonalityTraits(hero);
                if (names.Any(key => observed.Value<long>(key) != traits.Value<long>(key)))
                    throw new InvalidOperationException("The measurable native traits did not match the requested values.");
                result.Applied.Add("traits");
            }
            catch (Exception ex) { result.Errors.Add("traits: " + ex.Message); }
            return result;
        }

        private static bool ApplyField(Hero hero, string key, JToken value, CharacterEditorApplyResult result)
        {
            switch ((key ?? string.Empty).ToLowerInvariant())
            {
                case "name":
                case "title":
                    string name = value?.ToString() ?? string.Empty; if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Name cannot be empty.");
                    hero.SetName(new TextObject(name), new TextObject(name.Split(' ')[0])); return true;
                case "age":
                    float age = value.Value<float>(); if (age < 0f || age > 128f) throw new InvalidOperationException("Age must be between 0 and 128.");
                    hero.SetBirthDay(CampaignTime.YearsFromNow(-age)); return true;
                case "gold":
                    int targetGold = value.Value<int>(); hero.ChangeHeroGold(targetGold - hero.Gold); return true;
                case "wealth":
                    JObject wealth = value as JObject; if (wealth == null) return false; int wealthGold = wealth.Value<int?>("gold") ?? hero.Gold; hero.ChangeHeroGold(wealthGold - hero.Gold); return true;
                case "ispregnant":
                    hero.IsPregnant = value.Value<bool>();
                    ReignServices.Family?.NotifyPregnancyStateChanged(hero);
                    return true;
                case "spouseid": SetSpouse(hero, ReignObjectResolver.FindHero(value?.ToString())); return true;
                case "fatherid": SetParent(hero, ReignObjectResolver.FindHero(value?.ToString()), true); return true;
                case "motherid": SetParent(hero, ReignObjectResolver.FindHero(value?.ToString()), false); return true;
                case "clanid":
                    Clan clan = ReignObjectResolver.FindClan(value?.ToString()); if (clan == null) throw new InvalidOperationException("Clan was not found."); hero.Clan = clan; return true;
                case "kingdomid":
                    Kingdom kingdom = ReignObjectResolver.FindKingdom(value?.ToString()); if (hero.Clan == null) throw new InvalidOperationException("Hero has no clan to move between kingdoms.");
                    if (kingdom == null) ChangeKingdomAction.ApplyByLeaveKingdom(hero.Clan, false); else if (hero.Clan.Kingdom != kingdom) ChangeKingdomAction.ApplyByJoinToKingdom(hero.Clan, kingdom, default(CampaignTime), false); return true;
                case "isalive":
                    bool alive = value.Value<bool>(); if (alive == hero.IsAlive) return true; if (alive) throw new InvalidOperationException("Bannerlord cannot safely resurrect an initialized dead hero."); KillCharacterAction.ApplyByRemove(hero, false, true); return !hero.IsAlive;
                case "skills": ApplySkills(hero, value as JObject); return true;
                case "traits": ApplyTraits(hero, value as JObject); return true;
                default: return TrySetSimpleHeroProperty(hero, key, value);
            }
        }

        private static void ApplySkills(Hero hero, JObject values)
        {
            Dictionary<string, SkillObject> map = new Dictionary<string, SkillObject>(StringComparer.OrdinalIgnoreCase)
            {
                ["oneHanded"]=DefaultSkills.OneHanded,["twoHanded"]=DefaultSkills.TwoHanded,["polearm"]=DefaultSkills.Polearm,["bow"]=DefaultSkills.Bow,
                ["crossbow"]=DefaultSkills.Crossbow,["throwing"]=DefaultSkills.Throwing,["riding"]=DefaultSkills.Riding,["athletics"]=DefaultSkills.Athletics,
                ["smithing"]=DefaultSkills.Crafting,["scouting"]=DefaultSkills.Scouting,["tactics"]=DefaultSkills.Tactics,["roguery"]=DefaultSkills.Roguery,
                ["charm"]=DefaultSkills.Charm,["leadership"]=DefaultSkills.Leadership,["trade"]=DefaultSkills.Trade,["steward"]=DefaultSkills.Steward,
                ["medicine"]=DefaultSkills.Medicine,["engineering"]=DefaultSkills.Engineering
            };
            foreach (JProperty item in values?.Properties() ?? Enumerable.Empty<JProperty>()) if (map.TryGetValue(item.Name, out SkillObject skill)) hero.SetSkillValue(skill, Math.Max(0, Math.Min(1023, item.Value.Value<int>())));
        }

        private static void ApplyTraits(Hero hero, JObject values)
        {
            if (hero == null) throw new InvalidOperationException("Hero was not found.");
            if (!hero.IsAlive) return;
            if (values == null) throw new InvalidOperationException("Trait values were missing.");
            Dictionary<string, TraitObject> map = new Dictionary<string, TraitObject>(StringComparer.OrdinalIgnoreCase) { ["valor"]=DefaultTraits.Valor,["generosity"]=DefaultTraits.Generosity,["honor"]=DefaultTraits.Honor,["mercy"]=DefaultTraits.Mercy,["calculating"]=DefaultTraits.Calculating };
            foreach (JProperty item in values.Properties())
            {
                if (!map.TryGetValue(item.Name, out TraitObject trait)) continue;
                if (trait == null)
                    throw new InvalidOperationException("Bannerlord trait '" + item.Name + "' is unavailable.");
                int target = Math.Max(-2, Math.Min(2, item.Value.Value<int>()));
                hero.SetTraitLevel(trait, target);
                if (hero.GetTraitLevel(trait) != target)
                    throw new InvalidOperationException("Bannerlord did not retain trait '" + item.Name + "'.");
            }
        }

        private static void SetSpouse(Hero hero, Hero spouse)
        {
            Hero old = hero.Spouse; if (old != null && old.Spouse == hero) old.Spouse = null; hero.Spouse = spouse; if (spouse != null) { if (spouse.Spouse != null && spouse.Spouse != hero) spouse.Spouse.Spouse = null; spouse.Spouse = hero; }
        }

        private static void SetParent(Hero child, Hero parent, bool father)
        {
            if (parent == child) throw new InvalidOperationException("A hero cannot be their own parent."); if (father) child.Father = parent; else child.Mother = parent;
            if (parent == null) return; FieldInfo field = typeof(Hero).GetField("_children", BindingFlags.Instance | BindingFlags.NonPublic); if (field?.GetValue(parent) is IList<Hero> children && !children.Contains(child)) children.Add(child);
        }

        private static bool TrySetSimpleHeroProperty(Hero hero, string key, JToken value)
        {
            PropertyInfo property = typeof(Hero).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(x => x.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && x.CanWrite);
            if (property == null || value == null || value.Type == JTokenType.Object || value.Type == JTokenType.Array) return false;
            object converted = Convert.ChangeType(value.ToString(), property.PropertyType, System.Globalization.CultureInfo.InvariantCulture); property.SetValue(hero, converted, null); return true;
        }

        private sealed class NativeEditorReceipt
        {
            public string CampaignId { get; set; }
            public string TimelineId { get; set; }
            public string MaterialSha256 { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string ManualOriginLineageId { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string ManualOriginMaterialSha256 { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string AutomaticOriginId { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string AutomaticOriginMaterialSha256 { get; set; }
            public string[] Applied { get; set; }
            public string[] Errors { get; set; }

            [JsonIgnore]
            public string Status => Errors.Length == 0 ? "completed" : Applied.Length > 0 ? "partially_applied" : "failed";

            public bool Matches(string campaign, string timeline, string material, ReignCharacterEditorCommand command) =>
                CampaignId == campaign && TimelineId == timeline && MaterialSha256 == material
                && (ManualOriginLineageId ?? "") == command.ManualOriginLineageId
                && (ManualOriginMaterialSha256 ?? "") == command.ManualOriginMaterialSha256
                && (AutomaticOriginId ?? "") == command.AutomaticOriginId
                && (AutomaticOriginMaterialSha256 ?? "") == command.AutomaticOriginMaterialSha256;

            public bool IsValid() => !string.IsNullOrEmpty(CampaignId) && !string.IsNullOrEmpty(TimelineId)
                && MaterialSha256?.Length == 64 && MaterialSha256.All(Uri.IsHexDigit)
                && (string.IsNullOrEmpty(ManualOriginLineageId) && string.IsNullOrEmpty(ManualOriginMaterialSha256)
                    || ReignCharacterEditorClient.IsNativeOwnerNonce(ManualOriginLineageId)
                        && ReignCharacterEditorClient.IsNativeOriginDigest(ManualOriginMaterialSha256))
                && (string.IsNullOrEmpty(AutomaticOriginId) && string.IsNullOrEmpty(AutomaticOriginMaterialSha256)
                    || string.IsNullOrEmpty(ManualOriginLineageId) && ReignCharacterEditorClient.IsNativeOwnerNonce(AutomaticOriginId)
                        && ReignCharacterEditorClient.IsNativeOriginDigest(AutomaticOriginMaterialSha256))
                && Applied != null && Errors != null && Applied.All(value => value != null) && Errors.All(value => value != null);
        }

        private sealed class CharacterEditorApplyResult { public readonly List<string> Applied = new List<string>(); public readonly List<string> Errors = new List<string>(); }
    }
}
