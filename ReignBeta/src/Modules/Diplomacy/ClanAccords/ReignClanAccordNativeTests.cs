using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.ClanAccords;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace ReignBeta.ClanAccords
{
    /// <summary>Pure fixtures using real native math; never reads or changes the live accord ledger.</summary>
    internal static class ReignClanAccordNativeTests
    {
        internal static JObject Run(string runId, string profile)
        {
            var assertions = new JArray();
            if (!string.Equals(profile, "contracts", StringComparison.OrdinalIgnoreCase))
                return new JObject { ["ok"] = false, ["schema"] = "reign-clan-accord-native-contract-v1",
                    ["runId"] = runId, ["profile"] = profile, ["error"] = "Only the non-mutating contracts profile is supported." };
            Action<string, Func<JObject>> check = (id, test) =>
            {
                try
                {
                    JObject evidence = test();
                    assertions.Add(new JObject { ["id"] = "clan_accords.native." + id,
                        ["passed"] = evidence.Value<bool>("passed"), ["evidence"] = evidence });
                }
                catch (Exception ex)
                {
                    assertions.Add(new JObject { ["id"] = "clan_accords.native." + id,
                        ["passed"] = false, ["error"] = ex.Message });
                }
            };

            float[] factors = { 0f, 0.5f, -0.5f, -1f };
            float[] deltas = { 50f, 0.1f, 0.2f };
            for (int factorIndex = 0; factorIndex < factors.Length; factorIndex++)
            for (int deltaIndex = 0; deltaIndex < deltas.Length; deltaIndex++)
            foreach (bool descriptions in new[] { false, true })
            {
                float factor = factors[factorIndex];
                float delta = deltas[deltaIndex];
                string id = "fixed_" + factorIndex + "_" + deltaIndex + "_" + descriptions;
                check(id, () =>
                {
                    var number = new ExplainedNumber(10f, descriptions, new TextObject("{=!}Native base"));
                    number.AddFactor(factor, new TextObject("{=!}Native factor"));
                    float before = number.ResultNumber;
                    ReignClanAccordModelMath.AddFinalDelta(ref number, delta, new TextObject("{=!}Accord bonus"));
                    bool lines = !descriptions || (number.GetLines().Any(x => x.name == "Accord bonus" && Near(x.number, delta))
                        && Near(number.GetLines().Sum(x => x.number), number.ResultNumber));
                    return new JObject { ["passed"] = Near(number.ResultNumber, before + delta) && lines,
                        ["factor"] = factor, ["delta"] = delta, ["before"] = before,
                        ["after"] = number.ResultNumber, ["descriptions"] = descriptions, ["explanationSumMatches"] = lines };
                });
            }

            for (int factorIndex = 0; factorIndex < factors.Length; factorIndex++)
            foreach (double reduction in new[] { .02d, .08d, 1.2d })
            {
                float factor = factors[factorIndex];
                check("wage_" + factorIndex + "_" + reduction.ToString(System.Globalization.CultureInfo.InvariantCulture), () =>
                {
                    var number = new ExplainedNumber(100f, true, new TextObject("{=!}Native wages"));
                    number.AddFactor(factor, new TextObject("{=!}Native wage factor"));
                    number.LimitMin(0f);
                    float before = number.ResultNumber;
                    ReignClanAccordModelMath.ApplyWageReduction(ref number, reduction);
                    float expected = before * (1f - (float)Math.Min(1d, reduction));
                    return new JObject { ["passed"] = Near(number.ResultNumber, expected)
                        && Near(number.GetLines().Sum(x => x.number), number.ResultNumber),
                        ["factor"] = factor, ["reduction"] = reduction, ["before"] = before,
                        ["after"] = number.ResultNumber, ["expected"] = expected };
                });
            }

            check("native_limits", () =>
            {
                var floor = new ExplainedNumber(-20f, true); floor.LimitMin(0f);
                ReignClanAccordModelMath.ApplyWageReduction(ref floor, .08d);
                var ceiling = new ExplainedNumber(10f, true); ceiling.LimitMax(10f);
                ReignClanAccordModelMath.AddFinalDelta(ref ceiling, .2f, new TextObject("{=!}Accord bonus"));
                var negative = new ExplainedNumber(-2f, false); negative.AddFactor(.5f);
                ReignClanAccordModelMath.AddFinalDelta(ref negative, .2f, new TextObject("{=!}Accord bonus"));
                return new JObject { ["passed"] = Near(floor.ResultNumber, 0f) && Near(ceiling.ResultNumber, 10f)
                    && Near(negative.ResultNumber, -2.8f), ["floor"] = floor.ResultNumber,
                    ["ceiling"] = ceiling.ResultNumber, ["negativeGrowthWithBenefit"] = negative.ResultNumber };
            });

            check("save_json_nonempty", () =>
            {
                var state = new ReignClanAccordsCampaignBehavior.SavedState { LastSeason = 4336 };
                foreach (ClanAccordType type in Enum.GetValues(typeof(ClanAccordType)))
                {
                    ClanAccordResult created = state.Ledger.Create(new ClanAccordCreateRequest
                    {
                        ActionId = "native-contract-" + type, Type = type,
                        PlayerClanId = "fixture-player-clan", PlayerClanName = "Fixture Player Clan",
                        PartnerClanId = "fixture-partner-clan", PartnerClanName = "Fixture Partner Clan",
                        PlayerArrangerId = "fixture-player", PlayerArrangerName = "Fixture Player",
                        NpcArrangerId = "fixture-lesser-member", NpcArrangerName = "Fixture Lesser Member",
                        PlayerClanTier = 1, PlayerAccepted = true, NpcAccepted = true, CampaignDay = 10d
                    });
                    if (!created.Success) throw new InvalidOperationException(created.Error);
                }
                state.Ledger.ClaimSeasonalGoodwill("fixture-player-clan", "fixture-partner-clan", "fixture-lesser-member", 1084, 0, 18);
                var ended = state.Ledger.Cancel(state.Ledger.Records.First(x => x.Type == ClanAccordType.Artisan).Id,
                    "native-contract-cancel", "fixture-successor", "Fixture Successor", 11d, true);
                state.Outbox.Add(new JObject { ["eventId"] = "ended:" + ended.Record.Id,
                    ["kind"] = "ended", ["record"] = JObject.FromObject(ended.Record),
                    ["partnerMemberIds"] = new JArray("fixture-lesser-member"), ["endReason"] = "Cancelled" });
                string json = JsonConvert.SerializeObject(state);
                var restored = JsonConvert.DeserializeObject<ReignClanAccordsCampaignBehavior.SavedState>(json);
                bool exact = JToken.DeepEquals(JObject.Parse(json), JObject.FromObject(restored));
                int duplicate = restored.Ledger.ClaimSeasonalGoodwill("fixture-player-clan", "fixture-partner-clan", "fixture-lesser-member", 1084, 0, 0);
                ClanAccordRecord history = restored.Ledger.Records.Single(x => !x.IsActive);
                bool separate = !ReferenceEquals(state.Ledger, restored.Ledger) && !ReferenceEquals(state.Outbox[0], restored.Outbox[0]);
                return new JObject { ["passed"] = exact && separate && restored.LastSeason == 4336
                    && restored.Ledger.Records.Count == 5 && restored.Ledger.Records.Count(x => x.IsActive) == 4
                    && restored.Ledger.GetBonuses("fixture-player-clan").TradeIncome == 50
                    && restored.Ledger.GetBonuses("fixture-player-clan").Prosperity == 0d
                    && history.NpcArrangerId == "fixture-lesser-member" && history.EndActorId == "fixture-successor"
                    && restored.Outbox.Count == 1 && duplicate == 0 && restored.Ledger.SeasonalReceipts.Count == 1,
                    ["serializedRoundtripEqual"] = exact, ["independentObjects"] = separate,
                    ["records"] = restored.Ledger.Records.Count, ["pendingEvents"] = restored.Outbox.Count,
                    ["seasonalReceipts"] = restored.Ledger.SeasonalReceipts.Count,
                    ["nativeSaveReloadProven"] = false };
            });

            return new JObject { ["ok"] = assertions.Count == 38 && assertions.All(x => x.Value<bool>("passed")),
                ["schema"] = "reign-clan-accord-native-contract-v1", ["runId"] = runId, ["profile"] = "contracts",
                ["assertions"] = assertions, ["assertionCount"] = assertions.Count, ["providerCalls"] = 0,
                ["campaignMutations"] = 0, ["coverage"] = "Native ExplainedNumber and isolated saved-state JSON only.",
                ["remainingCoverage"] = new JArray("Installed model selection and real daily campaign effects",
                    "Actual SyncData save/checkpoint and fresh-process reload", "Paused outbox retry and timeline isolation",
                    "Natural dialogue acceptance, seasonal relationships, memory, cancellation, and war") };
        }

        private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < .0001f;
    }
}

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> ExecuteClanAccordTestAsync(JObject command)
        {
            string runId = command.Value<string>("fixtureRunId") ?? command.Value<string>("runId") ?? Guid.NewGuid().ToString("N");
            string profile = command.Value<string>("profile") ?? "contracts";
            JObject report = await ReignMainThread.InvokeAsync(() =>
                ReignBeta.ClanAccords.ReignClanAccordNativeTests.Run(runId, profile)).ConfigureAwait(false);
            return report.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("Clan Accords native contracts passed without campaign mutation.", report)
                : LiveCommandResult.Failed("Clan Accords native contracts failed.", report);
        }
    }
}
