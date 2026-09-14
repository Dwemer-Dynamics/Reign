using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ReignBeta.Campaign
{
    internal sealed class ReignPersonalityPreparationResult
    {
        internal bool Ok { get; set; }
        internal int CharacterCount { get; set; }
        internal long DurationMs { get; set; }
    }

    public sealed class ReignCharacterEditorCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignCharacterEditorCampaignBehavior Instance { get; private set; }
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

        public ReignCharacterEditorCampaignBehavior()
        {
            Instance = this;
        }

        public override void RegisterEvents()
        {
            _owningCampaign = TaleWorlds.CampaignSystem.Campaign.Current;
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reignCharacterEditor_lastPollDay", ref _lastPollDay);
            dataStore.SyncData("_reignIdentity_lastSyncDay", ref _lastIdentitySyncDay);
            dataStore.SyncData("_reignNotableBackgroundInitializationComplete", ref _notableBackgroundInitializationComplete);
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
                JObject initialized = await ReignServerClient.InitializeNotableMbtiProfilesAsync(notables).ConfigureAwait(false);
                EnsureActiveGeneration(generationId);
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
                    Dictionary<string, Hero> byId = notables
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                        .ToDictionary(x => x.StringId, x => x, StringComparer.OrdinalIgnoreCase);
                    JArray applied = new JArray();
                    foreach (JObject assignment in assignments.OfType<JObject>())
                    {
                        string heroId = assignment.Value<string>("heroId") ?? string.Empty;
                        if (!byId.TryGetValue(heroId, out Hero hero))
                            throw new InvalidOperationException("Notable '" + heroId + "' no longer exists in the loaded campaign.");
                        if (!hero.IsAlive)
                        {
                            applied.Add(new JObject
                            {
                                ["heroId"] = heroId,
                                ["status"] = "obsolete"
                            });
                            continue;
                        }
                        JObject traits = assignment["nativeTraits"] as JObject;
                        if (traits == null || traits.Count != 5)
                            throw new InvalidOperationException("Notable '" + heroId + "' has an incomplete native trait assignment.");
                        ApplyTraits(hero, traits);
                        applied.Add(new JObject
                        {
                            ["heroId"] = heroId,
                            ["traits"] = ReignServerClient.NativePersonalityTraits(hero)
                        });
                    }
                    return applied;
                }).ConfigureAwait(false);
                ReignCampaignInitializationGate.ReportProgress(
                    "confirming_native_traits",
                    observations.Count,
                    notables.Count);
                progress?.Invoke(observations.Count, notables.Count, "Confirming native personality traits");

                JObject confirmed = await ReignServerClient.ConfirmNotableMbtiProfilesAsync(observations).ConfigureAwait(false);
                EnsureActiveGeneration(generationId);
                if (confirmed?.Value<bool?>("ok") != true
                    || (confirmed?.Value<int?>("confirmedCount") ?? -1) != notables.Count)
                    throw new InvalidOperationException("Native traits could not be confirmed for every notable ("
                        + (confirmed?.Value<int?>("confirmedCount") ?? 0) + " of " + notables.Count + ").");

                await ReignMainThread.InvokeAsync(() =>
                {
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
            float day = (float)CampaignTime.Now.ToDays;
            if (!force && day - _lastPollDay < 0.2f) return;
            _lastPollDay = day; _polling = true; _ = PollAsync();
        }

        private async Task PollAsync()
        {
            try
            {
                List<ReignCharacterEditorCommand> commands = await ReignServerClient.PollCharacterEditorCommandsAsync().ConfigureAwait(false);
                foreach (ReignCharacterEditorCommand command in commands)
                {
                    CharacterEditorApplyResult result = await ReignMainThread.InvokeAsync(() =>
                        IsCurrentCampaign ? Apply(command) : null).ConfigureAwait(false);
                    if (result == null) return; // Never apply old-campaign commands to a new save.
                    await ReignServerClient.ReportCharacterEditorCommandAsync(command.CommandId, result.Errors.Count == 0 ? "completed" : result.Applied.Count > 0 ? "partially_applied" : "failed", result.Applied, result.Errors).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { ReignLog.Warn("Character editor campaign poll failed: " + ex.Message); }
            finally { _polling = false; }
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
                    ReignFamilyCampaignBehavior.Instance?.NotifyPregnancyStateChanged(hero);
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

        private sealed class CharacterEditorApplyResult { public readonly List<string> Applied = new List<string>(); public readonly List<string> Errors = new List<string>(); }
    }
}
