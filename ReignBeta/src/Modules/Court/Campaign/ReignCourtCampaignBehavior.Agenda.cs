using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.Library;
using ReignBeta.Characters.Integration;
using ReignBeta.Characters.World;
using ReignBeta.Court.FamilyChambers;
using ReignBeta.Court.Integration;
using ReignBeta.Court.UI;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.Spymaster.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Court.Campaign
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private void BuildDawnAgenda(int day, bool playerWasSitting = false)
        {
            if (_session == null || _session.LastAgendaDay == day) return;
            ResolvePreviousDailyAgenda(day, playerWasSitting);
            ApplyExpiredNativeOutcomes(day);
            QueueFactualMatters(day);
            _agenda.RemoveAll(x => x == null || x.AgendaDay < day - 7 || x.Status == "completed");
            _agenda.AddRange(ReignCourtScheduler.BuildAgenda(_matters, day));
            _session.LastAgendaDay = day;
            _session.Revision++;
            QueueRegentMailForMajorMatters(day, playerWasSitting);
            StateChanged?.Invoke();
            if (_serverSessionOpened) _ = SyncServerAgendaAsync();
        }

        private void ResolvePreviousDailyAgenda(int day, bool playerWasSitting)
        {
            List<CourtAgendaItem> prior = _agenda.Where(x => x != null && x.AgendaDay < day && x.Status != "completed").OrderBy(x => x.AgendaDay).ThenBy(x => x.Slot).ToList();
            List<CourtAgendaItem> previousDay = _agenda.Where(x => x != null && x.AgendaDay == day - 1).ToList();
            bool personallyCompleted = playerWasSitting && previousDay.All(x =>
            {
                CourtMatter m = _matters.FirstOrDefault(y => y.MatterId == x.MatterId);
                return x.Status == "completed" && m != null && string.Equals(m.ResolutionMode, "personal", StringComparison.OrdinalIgnoreCase);
            });

            Hero regent = FindHero(ActiveRegent?.HeroStringId);
            foreach (CourtAgendaItem item in prior)
            {
                CourtMatter matter = _matters.FirstOrDefault(x => x.MatterId == item.MatterId);
                if (matter == null || matter.IsTerminal) { item.Status = "completed"; continue; }
                if (matter.IsMajorRealmEvent)
                {
                    ApplyPredefinedDefaultOutcome(matter, day, regent == null ? "no_regent" : "regent_mail_unanswered");
                    item.Status = "expired";
                    continue;
                }
                if (regent != null)
                {
                    bool positive = RegentPositiveRoll(regent, matter, item.AgendaDay);
                    matter.State = ReignCourtMatterState.Resolved;matter.SelectedOptionId = positive ? "regent_positive" : "regent_negative";
                    matter.ResolutionMode = positive ? "regent_positive" : "regent_negative";matter.ResolvedDay = day;
                    matter.ResolutionReceiptJson = new JObject
                    {
                        ["ok"] = true,["resolver"] = "regent",["heroStringId"] = regent.StringId,["outcome"] = positive ? "positive" : "negative",
                        ["skillRule"] = "charm_plus_steward",["playerPresenceCredit"] = false
                    }.ToString(Newtonsoft.Json.Formatting.None);
                    matter.Revision++;item.Status = positive ? "delegated_positive" : "delegated_negative";
                    if (!positive) ApplyPredefinedDefaultOutcome(matter, day, "regent_negative");
                }
                else
                {
                    ApplyPredefinedDefaultOutcome(matter, day, "no_regent");
                    item.Status = "ignored";
                }
            }
        }

        private bool RegentPositiveRoll(Hero regent, CourtMatter matter, int agendaDay)
        {
            int charm = regent?.GetSkillValue(TaleWorlds.Core.DefaultSkills.Charm) ?? 0;
            int steward = regent?.GetSkillValue(TaleWorlds.Core.DefaultSkills.Steward) ?? 0;
            double normalized = Math.Max(0d, Math.Min(1d, (charm + steward) / 600d));
            double chance = Math.Min(0.70d, 0.20d + 0.50d * normalized);
            return StableUnit((_session?.CampaignId ?? "default") + "|" + (_session?.TimelineId ?? "main") + "|regent|" + (matter?.MatterId ?? "") + "|" + agendaDay + "|" + (regent?.StringId ?? "")) < chance;
        }

        private void ApplyPredefinedDefaultOutcome(CourtMatter matter, int day, string reason)
        {
            JObject outcome;
            try { outcome = JObject.Parse(string.IsNullOrWhiteSpace(matter.DefaultOutcomeJson) ? "{}" : matter.DefaultOutcomeJson); }
            catch { outcome = new JObject(); }
            string effect = outcome.Value<string>("effect") ?? "none";
            bool applied = true;string result = effect == "none" ? "Status quo retained." : string.Empty;
            try
            {
                if (effect == "rebellion_response")
                {
                    string movementId = outcome.Value<string>("movementId") ?? string.Empty;
                    applied = ReignServices.Rebellion?.TryResolvePlayerUltimatum(movementId, outcome.Value<string>("response") ?? "deadline_expired", out result) == true;
                }
                else if (effect == "declare_war")
                {
                    Kingdom actor = Kingdom.All.FirstOrDefault(x => x.StringId == (outcome.Value<string>("actorKingdomId") ?? string.Empty));
                    Kingdom target = Kingdom.All.FirstOrDefault(x => x.StringId == (outcome.Value<string>("targetKingdomId") ?? string.Empty));
                    if (actor == null || target == null) { applied = false;result = "The coded war threat no longer identifies two kingdoms."; }
                    else if (!FactionManager.IsAtWarAgainstFaction(actor, target)) { DeclareWarAction.ApplyByKingdomDecision(actor, target);result = "The unprevented threat became war."; }
                    else result = "The kingdoms were already at war.";
                }
                // Marriage and land-trade defaults deliberately perform no mutation.
                else if (effect == "marriage_response" || effect == "land_trade") result = "No agreement occurred; existing state was retained.";
            }
            catch (Exception ex) { applied = false;result = ex.Message; }
            matter.State = applied ? ReignCourtMatterState.Expired : ReignCourtMatterState.Invalidated;
            matter.SelectedOptionId = "daily_deadline_ignored";matter.ResolutionMode = reason;matter.ResolvedDay = day;
            matter.ResolutionReceiptJson = new JObject { ["ok"] = applied,["reason"] = reason,["effect"] = effect,["result"] = result,["defaultOutcome"] = outcome }.ToString(Newtonsoft.Json.Formatting.None);
            matter.InvalidReason = applied ? string.Empty : result;matter.Revision++;
        }

        private void QueueRegentMailForMajorMatters(int day, bool playerWasSitting)
        {
            Hero regent = FindHero(ActiveRegent?.HeroStringId);
            if (regent == null || playerWasSitting) return;
            foreach (CourtMatter matter in _agenda.Where(x => x.AgendaDay == day).Select(x => _matters.FirstOrDefault(m => m.MatterId == x.MatterId))
                .Where(x => x != null && x.IsMajorRealmEvent && !x.IsTerminal && x.RegentMailSentDay < 0f))
            {
                matter.RegentMailSentDay = day;matter.Revision++;
                _ = ReignCorrespondenceClient.SendRegentCourtLetterAsync(regent, matter);
            }
        }

        private static double StableUnit(string text)
        {
            unchecked { uint hash = 2166136261;foreach (char c in text ?? string.Empty) { hash ^= c;hash *= 16777619; }return hash / (double)uint.MaxValue; }
        }

        private void QueueFactualMatters(int day)
        {
            foreach (ReignSettlementSupplySnapshot land in ReignCourtSupplyService.GetScopedSnapshots(_session.Authority).Where(x => x.HasShortage || x.IsBesieged).Take(3))
            {
                QueueMatter(new CourtMatter
                {
                    SourceKey = "settlement_supply:" + land.SettlementStringId,
                    Kind = ReignCourtMatterKind.SettlementCrisis,
                    Title = land.Name + " requires attention",
                    Summary = land.IsBesieged ? "The settlement is under siege." : "Food supply is deteriorating and the settlement requests aid.",
                    SettlementStringId = land.SettlementStringId,
                    CreatedDay = day,
                    DueDay = day + (land.IsBesieged ? 1 : 3),
                    Priority = land.IsBesieged ? 100 : 75,
                    IsCritical = land.IsBesieged,
                    DecisionOptionsJson = NavigationOptions("Open Lands", "lands", "Postpone", "postpone"),
                    DefaultOutcomeJson = "{\"effect\":\"none\",\"reason\":\"deadline_ignored\"}"
                });
            }

            foreach (ReignCourtOffice office in Enum.GetValues(typeof(ReignCourtOffice)).Cast<ReignCourtOffice>())
            {
                if (_offices.Any(x => x.IsActive && x.Office == office)) continue;
                QueueMatter(new CourtMatter
                {
                    SourceKey = "office_vacancy:" + office.ToString().ToLowerInvariant(),
                    Kind = ReignCourtMatterKind.OfficeBusiness,
                    Title = office + " office is vacant",
                    Summary = "Basic reports remain available, but advanced " + office.ToString().ToLowerInvariant() + " operations require an appointment.",
                    CreatedDay = day,
                    DueDay = -1f,
                    Priority = 35,
                    DecisionOptionsJson = NavigationOptions("Review appointments", "subjects", "Leave vacant", "refuse")
                });
            }

            foreach (CourtObligation obligation in _obligations.Where(x => x.Status == "unresolved" && x.DueDay >= 0f && x.DueDay <= day + 3))
            {
                QueueMatter(new CourtMatter
                {
                    SourceKey = "obligation:" + obligation.ObligationId,
                    Kind = ReignCourtMatterKind.Obligation,
                    Title = "Obligation approaching its deadline",
                    Summary = obligation.Description,
                    CreatedDay = day,
                    DueDay = obligation.DueDay,
                    Priority = 90,
                    IsCritical = obligation.DueDay <= day + 1,
                    TermsHash = obligation.TermsHash,
                    DecisionOptionsJson = NavigationOptions("Review obligation", "court", "Refuse", "refuse"),
                    DefaultOutcomeJson = obligation.BreachRuleJson
                });
            }

            ReignRebellionMovementRecord rebellion = ReignServices.Rebellion?.Movements?
                .FirstOrDefault(x => x.IsPlayerKingdom && !x.ResolutionApplied && (x.UltimatumIssued || x.Pressure >= 70f));
            if (rebellion != null)
            {
                QueueMatter(new CourtMatter
                {
                    SourceKey = "rebellion_ultimatum:" + rebellion.MovementId,
                    Kind = ReignCourtMatterKind.RebellionUltimatum,
                    Title = rebellion.UltimatumIssued ? "A coalition delivers an ultimatum" : "A dangerous coalition is forming",
                    Summary = "Known coalition contacts demand a ruler-facing response. Hidden pressure and undiscovered members are not shown.",
                    KingdomStringId = rebellion.ParentKingdomStringId,
                    ParticipantHeroIdsCsv = rebellion.LeaderHeroStringId,
                    CreatedDay = day,
                    DueDay = day + 7,
                    Priority = 100,
                    IsCritical = rebellion.UltimatumIssued,
                    IsMajorRealmEvent = true,
                    RequiresServer = true,
                    DecisionOptionsJson = RebellionDecisionOptions(rebellion.MovementId),
                    DefaultOutcomeJson = new JObject
                    {
                        ["effect"] = "rebellion_response",
                        ["response"] = "deadline_expired",
                        ["movementId"] = rebellion.MovementId
                    }.ToString(Newtonsoft.Json.Formatting.None)
                });
            }

            Hero prisoner = Hero.AllAliveHeroes.Where(x => x != null && x.IsLord && x.IsPrisoner && x.PartyBelongedToAsPrisoner?.MapFaction == Clan.PlayerClan?.MapFaction)
                .OrderByDescending(x => x.Clan?.Tier ?? 0).FirstOrDefault();
            if (prisoner != null)
            {
                QueueMatter(new CourtMatter
                {
                    SourceKey = "prisoner_petition:" + prisoner.StringId,
                    Kind = ReignCourtMatterKind.PrisonerPetition,
                    Title = prisoner.Name + " petitions the court",
                    Summary = "A current prisoner asks to be heard. Any pardon, ransom, punishment, or transfer must use a valid coded native action.",
                    ParticipantHeroIdsCsv = prisoner.StringId,
                    CreatedDay = day,
                    DueDay = day + 5,
                    Priority = 58,
                    Confidentiality = "realm",
                    RequiresServer = true,
                    DecisionOptionsJson = NavigationOptions("Hear petition", "court", "Refuse audience", "refuse")
                });
            }

            if (_session.Authority == ReignCourtAuthority.Royal && Clan.PlayerClan?.Kingdom != null
                && Kingdom.All.Any(x => x != null && x != Clan.PlayerClan.Kingdom && FactionManager.IsAtWarAgainstFaction(Clan.PlayerClan.Kingdom, x)))
            {
                QueueMatter(new CourtMatter
                {
                    SourceKey = "war_council:" + day,
                    Kind = ReignCourtMatterKind.WarCouncil,
                    Title = "The Marshal requests a war council",
                    Summary = "Active wars, commanders, supplies, and readiness require a validated objective. The council may submit plans only through the existing strategy executor.",
                    CreatedDay = day,
                    DueDay = day + 2,
                    Priority = 78,
                    IsMajorRealmEvent = true,
                    RequiresServer = true,
                    DecisionOptionsJson = NavigationOptions("Open Military", "military", "Postpone", "refuse")
                });
            }

            Hero courtier = Hero.AllAliveHeroes.Where(x => ReignCourtNobleCampaignBehavior.IsCourtNoble(x)
                    && (_session.Authority == ReignCourtAuthority.Royal ? x.Clan?.Kingdom == Clan.PlayerClan?.Kingdom : x.Clan == Clan.PlayerClan))
                .OrderBy(x => StableCourtOrder(x.StringId, day)).FirstOrDefault();
            if (courtier != null)
            {
                ReignCourtMatterKind ambientKind = day % 3 == 0 ? ReignCourtMatterKind.Performer : day % 3 == 1 ? ReignCourtMatterKind.Artisan : ReignCourtMatterKind.PrivateCounsel;
                QueueMatter(new CourtMatter
                {
                    SourceKey = "ambient_court:" + courtier.StringId + ":" + day,
                    Kind = ambientKind,
                    Title = courtier.Name + " requests a brief audience",
                    Summary = "A generated court noble seeks a roleplay audience grounded in current court context. Dialogue may characterize the meeting but coded outcomes remain effect-free unless separately validated.",
                    ParticipantHeroIdsCsv = courtier.StringId,
                    CreatedDay = day,
                    DueDay = day,
                    Priority = 12,
                    IsAmbientRoleplay = true,
                    Confidentiality = ambientKind == ReignCourtMatterKind.PrivateCounsel ? "private" : "public",
                    RequiresServer = true,
                    DecisionOptionsJson = NavigationOptions("Receive", "court", "Dismiss", "refuse")
                });
            }
        }

        internal static int StableCourtOrder(string id,int day)
        {
            unchecked{int hash=day;foreach(char c in id??string.Empty)hash=hash*31+c;return hash&int.MaxValue;}
        }

        private void ApplyExpiredNativeOutcomes(int day)
        {
            foreach (CourtMatter matter in _matters.Where(x => x != null && !x.IsTerminal && x.DueDay >= 0f && x.DueDay < day).ToList())
            {
                if (matter.Kind != ReignCourtMatterKind.RebellionUltimatum) continue;
                string movementId = (matter.SourceKey ?? string.Empty).StartsWith("rebellion_ultimatum:", StringComparison.OrdinalIgnoreCase)
                    ? matter.SourceKey.Substring("rebellion_ultimatum:".Length)
                    : string.Empty;
                string result = string.Empty;
                bool applied = ReignServices.Rebellion?.TryResolvePlayerUltimatum(movementId, "deadline_expired", out result) == true;
                matter.State = applied ? ReignCourtMatterState.Expired : ReignCourtMatterState.Invalidated;
                matter.SelectedOptionId = "deadline_expired";
                matter.ResolvedDay = day;
                matter.ResolutionReceiptJson = new JObject { ["ok"] = applied, ["result"] = result ?? string.Empty, ["defaultOutcome"] = JObject.Parse(matter.DefaultOutcomeJson ?? "{}") }.ToString(Newtonsoft.Json.Formatting.None);
                matter.InvalidReason = applied ? string.Empty : result ?? "The rebellion ultimatum could not be executed.";
                matter.Revision++;
            }
        }

        private void QueueHostCrisis(Settlement host, string kind)
        {
            ReignCourtScheduler.QueueUrgentInterruption(_matters, _agenda, new CourtMatter
            {
                SourceKey = "host_interruption:" + host.StringId + ":" + kind,
                Kind = ReignCourtMatterKind.SettlementCrisis,
                Title = "Court interrupted at " + host.Name,
                Summary = "A verified native state change has made the court unsafe.",
                SettlementStringId = host.StringId,
                CreatedDay = CurrentDayFloat(),
                DueDay = CurrentDayFloat(),
                Priority = 100,
                DefaultOutcomeJson = "{\"effect\":\"court_closed\"}",
                DecisionOptionsJson = "[]"
            }, CurrentDay());
        }

        private void QueueMatter(CourtMatter matter)
        {
            matter.CampaignId = _session.CampaignId;
            matter.TimelineId = _session.TimelineId;
            ReignCourtScheduler.TryQueueUnique(_matters, matter);
        }

        private void RecordCounterSample(int day)
        {
            CourtCounterSample sample = _counterSamples.FirstOrDefault(x => x.Day == day);
            if (sample == null)
            {
                sample = new CourtCounterSample { Day = day };
                _counterSamples.Add(sample);
            }
            sample.Supply = ReignCourtSupplyService.GetStrategicSupplyUnits(_session.Authority);
            sample.Gold = Hero.MainHero?.Gold ?? 0;
            sample.Influence = Clan.PlayerClan?.Influence ?? 0f;
            sample.Strength = _session.Authority == ReignCourtAuthority.Royal
                ? Clan.PlayerClan?.Kingdom?.CurrentTotalStrength ?? 0f
                : Clan.PlayerClan?.CurrentTotalStrength ?? 0f;
            sample.Renown = Clan.PlayerClan?.Renown ?? 0f;
            _counterSamples.RemoveAll(x => x.Day < day - 7);
        }

        private static string NavigationOptions(string primaryLabel, string primaryTab, string secondaryLabel, string secondaryOption)
        {
            JArray options = new JArray();
            foreach (JObject option in new[]
            {
                new JObject { ["optionId"] = "open_" + primaryTab, ["label"] = primaryLabel, ["effect"] = "none", ["navigateTab"] = primaryTab, ["consequences"] = new JObject() },
                new JObject { ["optionId"] = secondaryOption, ["label"] = secondaryLabel, ["effect"] = "none", ["consequences"] = new JObject() }
            })
            {
                option["termsHash"] = ReignCourtTerms.Hash(option["consequences"].ToString(Newtonsoft.Json.Formatting.None));
                options.Add(option);
            }
            return options.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string RebellionDecisionOptions(string movementId)
        {
            JArray options = new JArray();
            foreach (JObject choice in new[]
            {
                new JObject { ["optionId"] = "accept", ["label"] = "Accept demands", ["description"] = "Apply the coalition's coded demand and avoid civil war.", ["response"] = "accept" },
                new JObject { ["optionId"] = "negotiate", ["label"] = "Negotiate", ["description"] = "Open a time-limited negotiation while the coalition remains active.", ["response"] = "negotiate" },
                new JObject { ["optionId"] = "refuse", ["label"] = "Refuse", ["description"] = "Reject the ultimatum and permit the rebellion executor to begin civil war.", ["response"] = "refuse" }
            })
            {
                JObject consequences = new JObject
                {
                    ["nativeActions"] = new JArray(new JObject
                    {
                        ["type"] = "rebellion_response",
                        ["movementId"] = movementId ?? string.Empty,
                        ["response"] = choice.Value<string>("response")
                    })
                };
                choice.Remove("response");
                choice["consequences"] = consequences;
                choice["termsHash"] = ReignCourtTerms.Hash(consequences.ToString(Newtonsoft.Json.Formatting.None));
                options.Add(choice);
            }
            return options.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
