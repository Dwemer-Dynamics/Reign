using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Shared.Rulers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignRulerCampaignBehavior
    {
        private Hero _executedHero;
        private string _executionId = "";
        private readonly Dictionary<string, JObject> _nativeExecutionRelations = new Dictionary<string, JObject>();
        private JArray _unauthorizedExecutionRows;
        internal void BeginExecutionRelations(Hero victim, Hero executor)
        {
            if (_executedHero != null || (!Sovereign(victim) && !State.Judgments.Any(j => j.PrisonerId == victim?.StringId && j.Stage == RulerJudgmentStage.Applying))
                || (!Enabled(RulerOptions.Judgments) && !Enabled(RulerOptions.Reactions))) return;
            _executedHero = victim; _executionId = State.Judgments.LastOrDefault(j => j.PrisonerId == victim.StringId && j.Stage == RulerJudgmentStage.Applying)?.Id
                ?? ("native_execution_" + victim.StringId);
            _nativeExecutionRelations.Clear(); _unauthorizedExecutionRows = null;
            if (!ApplyingNative && executor == Hero.MainHero && !Sovereign(executor) && Enabled(RulerOptions.Reactions))
            {
                var own = Clan.PlayerClan?.Kingdom; var defeated = victim.Clan.Kingdom;
                _unauthorizedExecutionRows = new JArray(Hero.AllAliveHeroes.Where(h => h.IsLord && !h.IsChild && h != executor && h != victim
                    && ((own != null && h.Clan?.Kingdom == own) || h.Clan?.Kingdom == defeated))
                    .Select(h => new JObject { ["subjectId"] = h.StringId, ["targetId"] = executor.StringId,
                        ["delta"] = own?.Leader == h ? -60 : -30, ["reason"] = "unauthorized_execution" }));
            }
        }
        private void NativeExecutionRelationChanged(Hero first, Hero second, int delta, bool notification, ChangeRelationAction.ChangeRelationDetail detail, Hero originalFirst, Hero originalSecond)
        {
            if (_executedHero == null || first == null || second == null || first == second || delta >= 0) return;
            // Observe actual native effective pairs after any existing conviction or
            // clan-pair suppression. Do not synthesize vanilla costs or apply them twice.
            foreach (var pair in new[] { new[] { first, second }, new[] { second, first } })
            {
                string key = pair[0].StringId + "|" + pair[1].StringId;
                if (!_nativeExecutionRelations.TryGetValue(key, out var row))
                {
                    row = new JObject { ["subjectId"] = pair[0].StringId, ["targetId"] = pair[1].StringId, ["delta"] = 0, ["reason"] = "native_execution" };
                    _nativeExecutionRelations[key] = row;
                }
                row["delta"] = Math.Max(-100, row.Value<int>("delta") + delta);
            }
        }
        internal void EndExecutionRelations()
        {
            if (_executedHero == null) return;
            if (_executedHero.IsDead)
            {
                if (_nativeExecutionRelations.Count > 0) QueueRelations(_executionId + ":native", new JArray(_nativeExecutionRelations.Values));
                if (_unauthorizedExecutionRows?.Count > 0 && State.Reactions.Add(_executionId + ":unauthorized"))
                    QueueRelations(_executionId + ":unauthorized", _unauthorizedExecutionRows);
            }
            _executedHero = null; _executionId = ""; _nativeExecutionRelations.Clear(); _unauthorizedExecutionRows = null;
        }
    }
}
