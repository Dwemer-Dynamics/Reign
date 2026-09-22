$clientPath = 'C:\Users\speed\Desktop\ReignBeta\src\Integration\ReignServerClient.cs'
$behaviorPath = 'C:\Users\speed\Desktop\ReignBeta\src\Campaign\ReignAICampaignBehavior.cs'
$settingsPath = 'C:\Users\speed\Desktop\ReignBeta\src\Settings\ReignBetaSettings.cs'

$client = @'
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Settings;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace ReignBeta.Integration
{
    public static class ReignServerClient
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        public static async Task<bool> IngestDailyWorldSnapshotAsync()
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return false;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["eventType"] = "daily_world_snapshot",
                    ["locationId"] = MobileParty.MainParty?.CurrentSettlement?.StringId ?? string.Empty,
                    ["summary"] = BuildSummary(),
                    ["actorIds"] = new JArray(GetActorIds())
                };

                JObject response = await PostJsonAsync("/events/ingest", payload).ConfigureAwait(false);
                return response.Value<bool?>("ok") == true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Local server ingest failed: " + ex.Message);
                return false;
            }
        }

        public static async Task<List<ReignWorldActionRecord>> FetchActionCommandsAsync()
        {
            List<ReignWorldActionRecord> results = new List<ReignWorldActionRecord>();
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && (!settings.UseLocalServer || !settings.ReceiveServerActionCommands))
                {
                    return results;
                }

                string route = "/actions/next?campaignId=" + Uri.EscapeDataString(GetCampaignId()) + "&limit=10";
                JObject response = await GetJsonAsync(route).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true)
                {
                    return results;
                }

                JArray actions = response["actions"] as JArray;
                if (actions == null)
                {
                    return results;
                }

                foreach (JToken token in actions)
                {
                    JObject obj = token as JObject;
                    ReignWorldActionRecord record = ParseActionRecord(obj);
                    if (record != null && record.Type != ReignWorldActionType.Unknown)
                    {
                        results.Add(record);
                    }
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Local server action poll failed: " + ex.Message);
            }

            return results;
        }

        public static async Task ReportActionAsync(ReignWorldActionRecord action, string status, string message)
        {
            try
            {
                ReignBetaSettings settings = ReignBetaSettings.Instance;
                if (settings != null && !settings.UseLocalServer)
                {
                    return;
                }

                if (action == null)
                {
                    return;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["serverActionId"] = action.ActionId ?? string.Empty,
                    ["actionId"] = action.ActionId ?? string.Empty,
                    ["status"] = status ?? "reported",
                    ["type"] = action.Type.ToString(),
                    ["message"] = message ?? string.Empty
                };

                await PostJsonAsync("/actions/report", payload).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Local server action report failed: " + ex.Message);
            }
        }

        private static async Task<JObject> GetJsonAsync(string route)
        {
            string url = BuildUrl(route);
            using (HttpResponseMessage response = await Client.GetAsync(url).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + body);
                }

                return JObject.Parse(body);
            }
        }

        private static async Task<JObject> PostJsonAsync(string route, JObject payload)
        {
            string url = BuildUrl(route);
            string json = payload.ToString(Formatting.None);
            using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await Client.PostAsync(url, content).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + body);
                }

                return JObject.Parse(body);
            }
        }

        private static string BuildUrl(string route)
        {
            string baseUrl = ReignBetaSettings.Instance?.LocalServerUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://127.0.0.1:5101";
            }

            return baseUrl.TrimEnd('/') + route;
        }

        private static ReignWorldActionRecord ParseActionRecord(JObject obj)
        {
            if (obj == null)
            {
                return null;
            }

            float now = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
            ReignWorldActionRecord action = new ReignWorldActionRecord
            {
                ActionId = ReadString(obj, "actionId", ReadString(obj, "serverActionId", Guid.NewGuid().ToString("N"))),
                Source = ReadString(obj, "source", "server_llm_or_manual"),
                ActorHeroStringId = ReadString(obj, "actorHeroStringId", ReadString(obj, "actorHeroId", "")),
                ActorKingdomStringId = ReadString(obj, "actorKingdomStringId", ReadString(obj, "actorKingdomId", "")),
                TargetHeroStringId = ReadString(obj, "targetHeroStringId", ReadString(obj, "targetHeroId", "")),
                TargetKingdomStringId = ReadString(obj, "targetKingdomStringId", ReadString(obj, "targetKingdomId", "")),
                TargetSettlementStringId = ReadString(obj, "targetSettlementStringId", ReadString(obj, "targetSettlementId", "")),
                Reason = ReadString(obj, "reason", "Server action from ReignBeta."),
                TermsJson = ReadString(obj, "termsJson", ""),
                MinimumTroops = ReadInt(obj, "minimumTroops", 40),
                DesiredStrength = ReadInt(obj, "desiredStrength", 350),
                MaxAttempts = ReadInt(obj, "maxAttempts", 3),
                RequiresAcceptance = ReadBool(obj, "requiresAcceptance", false),
                CreatedDay = now,
                ExecuteAfterDay = now + ReadFloat(obj, "executeAfterDays", 0f)
            };

            action.Type = ParseActionType(ReadString(obj, "type", ReadString(obj, "command", "")), ReadInt(obj, "typeValue", 0));
            if (action.Type == ReignWorldActionType.StrategyCaptureSettlement && action.MaxAttempts <= 3)
            {
                action.MaxAttempts = 24;
            }

            return action;
        }

        private static ReignWorldActionType ParseActionType(string text, int typeValue)
        {
            if (typeValue > 0)
            {
                return (ReignWorldActionType)typeValue;
            }

            string value = (text ?? string.Empty).Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
            switch (value)
            {
                case "diplomacydeclarewar":
                case "diplomacy_declare_war":
                case "declare_war":
                    return ReignWorldActionType.DiplomacyDeclareWar;
                case "diplomacymakepeace":
                case "diplomacy_make_peace":
                case "make_peace":
                    return ReignWorldActionType.DiplomacyMakePeace;
                case "diplomacyoffertributepeace":
                case "offer_tribute_peace":
                    return ReignWorldActionType.DiplomacyOfferTributePeace;
                case "diplomacyrecordpromise":
                case "record_promise":
                    return ReignWorldActionType.DiplomacyRecordPromise;
                case "strategyrecruitandrecover":
                case "recruit_and_recover":
                    return ReignWorldActionType.StrategyRecruitAndRecover;
                case "strategyformarmy":
                case "form_army":
                    return ReignWorldActionType.StrategyFormArmy;
                case "strategyattacksettlement":
                case "attack_settlement":
                    return ReignWorldActionType.StrategyAttackSettlement;
                case "strategycapturesettlement":
                case "capture_settlement":
                case "capture_settlement_plan":
                    return ReignWorldActionType.StrategyCaptureSettlement;
                default:
                    return ReignWorldActionType.Unknown;
            }
        }

        private static string BuildSummary()
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom == null)
            {
                return "The player clan is independent. ReignBeta daily world snapshot recorded.";
            }

            return "The player kingdom is " + playerKingdom.InformalName
                + ", active wars: " + playerKingdom.FactionsAtWarWith.Count
                + ", active armies: " + playerKingdom.Armies.Count
                + ", clans: " + playerKingdom.Clans.Count + ".";
        }

        private static IEnumerable<string> GetActorIds()
        {
            if (Hero.MainHero != null)
            {
                yield return Hero.MainHero.StringId;
            }

            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom?.Leader != null && playerKingdom.Leader != Hero.MainHero)
            {
                yield return playerKingdom.Leader.StringId;
            }
        }

        private static string GetCampaignId()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current?.UniqueGameId ?? "unknown";
        }

        private static string ReadString(JObject obj, string key, string fallback)
        {
            JToken token = obj?[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.ToString();
        }

        private static int ReadInt(JObject obj, string key, int fallback)
        {
            JToken token = obj?[key];
            return token == null || token.Type == JTokenType.Null || !int.TryParse(token.ToString(), out int value) ? fallback : value;
        }

        private static float ReadFloat(JObject obj, string key, float fallback)
        {
            JToken token = obj?[key];
            return token == null || token.Type == JTokenType.Null || !float.TryParse(token.ToString(), out float value) ? fallback : value;
        }

        private static bool ReadBool(JObject obj, string key, bool fallback)
        {
            JToken token = obj?[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            return bool.TryParse(token.ToString(), out bool value) ? value : fallback;
        }
    }
}
'@
[System.IO.File]::WriteAllText($clientPath, $client, [System.Text.UTF8Encoding]::new($false))

$settings = [System.IO.File]::ReadAllText($settingsPath)
if (-not $settings.Contains('private bool _receiveServerActionCommands')) {
    $settings = $settings.Replace(
        '        private bool _useLocalServer = true;' + "`r`n" + '        private string _localServerUrl = "http://127.0.0.1:5101";',
        '        private bool _useLocalServer = true;' + "`r`n" + '        private bool _receiveServerActionCommands = true;' + "`r`n" + '        private string _localServerUrl = "http://127.0.0.1:5101";'
    )

    $insertAfter = @'
        public string LocalServerUrl
        {
            get { return _localServerUrl; }
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:5101" : value.Trim();
                if (next != _localServerUrl)
                {
                    _localServerUrl = next;
                    OnPropertyChanged();
                }
            }
        }
'@

    $receiveProperty = @'
        public string LocalServerUrl
        {
            get { return _localServerUrl; }
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:5101" : value.Trim();
                if (next != _localServerUrl)
                {
                    _localServerUrl = next;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("Receive Server Action Commands", Order = 2, RequireRestart = false, HintText = "When enabled, the local server can send validated diplomacy and strategy commands to the in-game ReignBeta ledger.")]
        [SettingPropertyGroup("Local Server", GroupOrder = 2)]
        public bool ReceiveServerActionCommands
        {
            get { return _receiveServerActionCommands; }
            set
            {
                if (value != _receiveServerActionCommands)
                {
                    _receiveServerActionCommands = value;
                    OnPropertyChanged();
                }
            }
        }
'@
    if (-not $settings.Contains($insertAfter)) {
        throw 'Could not find LocalServerUrl property block.'
    }
    $settings = $settings.Replace($insertAfter, $receiveProperty)
}
[System.IO.File]::WriteAllText($settingsPath, $settings, [System.Text.UTF8Encoding]::new($false))

$behavior = [System.IO.File]::ReadAllText($behaviorPath)
$behavior = $behavior.Replace('using System.Collections.Generic;' + "`r`n" + 'using System.Linq;', 'using System.Collections.Generic;' + "`r`n" + 'using System.Linq;' + "`r`n" + 'using System.Threading.Tasks;')
$behavior = $behavior.Replace(
    '        private List<ReignWorldActionRecord> _actions = new List<ReignWorldActionRecord>();' + "`r`n" + '        private float _lastServerSnapshotDay = -1000f;',
    '        private readonly object _pendingServerActionsLock = new object();' + "`r`n" + '        private List<ReignWorldActionRecord> _actions = new List<ReignWorldActionRecord>();' + "`r`n" + '        private List<ReignWorldActionRecord> _pendingServerActions = new List<ReignWorldActionRecord>();' + "`r`n" + '        private float _lastServerSnapshotDay = -1000f;' + "`r`n" + '        private float _lastServerActionPollDay = -1000f;' + "`r`n" + '        private bool _serverActionPollInFlight;'
)
$behavior = $behavior.Replace(
    '            dataStore.SyncData("_reignBeta_lastServerSnapshotDay", ref _lastServerSnapshotDay);',
    '            dataStore.SyncData("_reignBeta_lastServerSnapshotDay", ref _lastServerSnapshotDay);' + "`r`n" + '            dataStore.SyncData("_reignBeta_lastServerActionPollDay", ref _lastServerActionPollDay);'
)
$behavior = $behavior.Replace(
    '            float now = CurrentDay();' + "`r`n" + '            foreach (ReignWorldActionRecord action in _actions.ToList())',
    '            float now = CurrentDay();' + "`r`n" + '            MaybeStartServerActionPoll(now);' + "`r`n" + '            DrainPendingServerActions();' + "`r`n" + '            foreach (ReignWorldActionRecord action in _actions.ToList())'
)
$marker = @'
        private void ExecuteAction(ReignWorldActionRecord action, float now)
'@
$insert = @'
        private void MaybeStartServerActionPoll(float now)
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null && (!settings.UseLocalServer || !settings.ReceiveServerActionCommands))
            {
                return;
            }

            if (_serverActionPollInFlight || now - _lastServerActionPollDay < 0.25f)
            {
                return;
            }

            _lastServerActionPollDay = now;
            _serverActionPollInFlight = true;
            _ = PollServerActionsAsync();
        }

        private async Task PollServerActionsAsync()
        {
            try
            {
                List<ReignWorldActionRecord> actions = await ReignServerClient.FetchActionCommandsAsync().ConfigureAwait(false);
                if (actions == null || actions.Count == 0)
                {
                    return;
                }

                lock (_pendingServerActionsLock)
                {
                    _pendingServerActions.AddRange(actions);
                }
            }
            finally
            {
                _serverActionPollInFlight = false;
            }
        }

        private void DrainPendingServerActions()
        {
            List<ReignWorldActionRecord> pending;
            lock (_pendingServerActionsLock)
            {
                if (_pendingServerActions.Count == 0)
                {
                    return;
                }

                pending = _pendingServerActions.ToList();
                _pendingServerActions.Clear();
            }

            foreach (ReignWorldActionRecord action in pending)
            {
                if (action == null || _actions.Any(x => x != null && x.ActionId == action.ActionId))
                {
                    continue;
                }

                ReignWorldActionRecord queued = EnqueueAction(action);
                if (queued != null)
                {
                    _ = ReignServerClient.ReportActionAsync(queued, "enqueued", "Queued in Bannerlord action ledger.");
                }
            }
        }

'@
if (-not $behavior.Contains($marker)) {
    throw 'Could not find ExecuteAction marker.'
}
$behavior = $behavior.Replace($marker, $insert + $marker)
$behavior = $behavior.Replace(
    '                ReignLog.Info("Completed action " + action.ActionId + ": " + result.Message);' + "`r`n" + '                ShowDebug("Completed " + action.Type + ".");',
    '                ReignLog.Info("Completed action " + action.ActionId + ": " + result.Message);' + "`r`n" + '                _ = ReignServerClient.ReportActionAsync(action, "completed", result.Message);' + "`r`n" + '                ShowDebug("Completed " + action.Type + ".");'
)
$behavior = $behavior.Replace(
    '                ReignLog.Warn("Failed action " + action.ActionId + ": " + result.Message);' + "`r`n" + '                ShowDebug("Failed " + action.Type + ": " + result.Message);',
    '                ReignLog.Warn("Failed action " + action.ActionId + ": " + result.Message);' + "`r`n" + '                _ = ReignServerClient.ReportActionAsync(action, "failed", result.Message);' + "`r`n" + '                ShowDebug("Failed " + action.Type + ": " + result.Message);'
)
[System.IO.File]::WriteAllText($behaviorPath, $behavior, [System.Text.UTF8Encoding]::new($false))

Select-String -LiteralPath $clientPath,$behaviorPath,$settingsPath -Pattern 'FetchActionCommandsAsync|ReportActionAsync|ReceiveServerActionCommands|MaybeStartServerActionPoll|DrainPendingServerActions' -Context 1,1
