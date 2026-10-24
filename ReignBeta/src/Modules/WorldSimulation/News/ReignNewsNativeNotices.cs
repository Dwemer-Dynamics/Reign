using System;
using System.Linq;
using Reign.Core.Contracts.World;
using ReignBeta.Events;
using ReignBeta.Integration;
using ReignBeta.Shared.Rulers;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNewsCampaignBehavior
    {
        // Only the enrolled, paused, disposable-save ruler harness calls this. It publishes
        // bounded presentation samples through production formatters, without political effects.
        internal void PreparePresentationFixture(int sample, Hero actor, Hero target, string operation)
        {
            if (sample < 0 || sample > 7 || actor?.IsAlive != true || target?.IsAlive != true || actor == target)
                throw new ArgumentException("News samples require two distinct living heroes and amount zero through seven.");
            if (sample == 0) return;
            string id = "news_fixture:" + operation;
            if (sample <= 2)
                ClanRuling(new ReignClanConflictNotice
                {
                    IncidentId = id, TimelineId = "fixture", Headline = "A Dispute over Pasture Rights",
                    NewsDescription = "Both houses claim seasonal grazing rights over the same uplands.",
                    KingdomId = actor.Clan?.Kingdom?.StringId ?? "", KingdomName = actor.Clan?.Kingdom?.Name?.ToString() ?? "the realm",
                    RulerName = actor.Clan?.Kingdom?.Leader?.Name?.ToString() ?? actor.Name.ToString(),
                    ClanAName = actor.Clan?.Name?.ToString() ?? actor.Name.ToString(), ClanBName = target.Clan?.Name?.ToString() ?? target.Name.ToString(),
                    LeaderAId = actor.StringId, LeaderBId = target.StringId, WinningSide = "a", MediationSucceeded = sample == 1, WorldDay = (float)CampaignTime.Now.ToDays
                });
            else if (sample == 3)
                ForeignRuling(new ReignPoliticalPressureNotice
                {
                    IncidentId = id, TimelineId = "fixture", Headline = "A Dispute at the Border", Polarity = "hostile",
                    NewsDescription = "Nobles from the two realms dispute the right to collect tolls along a shared trade route.",
                    OriginKingdomId = actor.Clan?.Kingdom?.StringId ?? "", TargetKingdomId = target.Clan?.Kingdom?.StringId ?? "",
                    OriginKingdomName = actor.Clan?.Kingdom?.Name?.ToString() ?? "the western realm", TargetKingdomName = target.Clan?.Kingdom?.Name?.ToString() ?? "the eastern realm",
                    OriginRulerId = actor.StringId, TargetRulerId = target.StringId, OriginRulerName = actor.Name.ToString(), TargetRulerName = target.Name.ToString(),
                    OriginStance = "support_lords", TargetStance = "preserve_relations", OriginPressureBefore = 10, OriginPressureAfter = 20,
                    TargetPressureBefore = 10, TargetPressureAfter = 0, WorldDay = (float)CampaignTime.Now.ToDays
                });
            else
            {
                var record = new ReignKingdomEventRecord
                {
                    EventId = id, ArchetypeId = sample == 5 ? "golden_age" : sample == 7 ? "orderly_succession" : "famine",
                    Title = sample == 5 ? "Golden Age" : sample == 7 ? "Orderly Succession" : "Famine", KingdomStringId = actor.Clan?.Kingdom?.StringId ?? "",
                    StartDay = (float)CampaignTime.Now.ToDays, EndDay = (float)CampaignTime.Now.ToDays + 30,
                    IsTimed = sample != 7, Status = sample == 6 ? "expired" : "active", Polarity = sample == 5 || sample == 7 ? "beneficial" : "harmful"
                };
                KingdomEvent(record, actor.Clan?.Kingdom, null, actor, sample == 7 ? target : null, sample == 6);
            }
        }

        internal void Judgment(RulerJudgment judgment, string phase)
        {
            bool execution = ReignRulerPolicy.Execution(judgment.Verdict);
            if (phase == "sentenced" && !execution) return; // Immediate settlements are announced only once they succeed.
            var prisoner = ReignRulerCampaignBehavior.HeroById(judgment.PrisonerId);
            var victor = ReignRulerCampaignBehavior.HeroById(judgment.VictorId);
            var defeated = ReignRulerCampaignBehavior.Realm(judgment.DefeatedKingdomId);
            var winning = ReignRulerCampaignBehavior.Realm(judgment.VictorKingdomId);
            string name = prisoner?.Name?.ToString() ?? "The captive leader", ruler = victor?.Name?.ToString() ?? "The victorious ruler";
            string realm = defeated?.Name?.ToString() ?? "the defeated realm", conqueror = winning?.Name?.ToString() ?? "the victorious realm";
            var news = WithPeople(new ReignNewsArticle
            {
                Id = "crier:judgment:" + judgment.Id + ":" + phase,
                Category = phase == "sentenced" ? ReignNewsCategories.Sentences : ReignNewsCategories.RulerOutcomes,
                Title = phase == "sentenced" ? "A Sentence of Death" : "The Fate of a Captive Leader",
                Headline = phase == "sentenced" ? "Fifteen days until execution" : "A ruling takes effect",
                WorldDay = CampaignTime.Now.ToDays
            }, victor, prisoner, "The ruling authority", phase == "sentenced" ? "Condemned to death" : "The defeated leader");
            // Keep the realms at the time of judgment even after annexation or exile.
            news.ActorRealmId = judgment.VictorKingdomId; news.ActorRealmName = conqueror;
            news.TargetRealmId = judgment.DefeatedKingdomId; news.TargetRealmName = realm;
            if (phase == "sentenced")
            {
                news.Body = ruler + " has condemned " + name + " to death. "
                    + (judgment.Verdict == RulerVerdict.TortureExecution
                        ? "The prisoner will endure fifteen days of torture and public humiliation before the execution."
                        : "The execution is to take place fifteen days after this sentence, without torture or humiliation.");
                news.Consequences = judgment.DefeatedLord
                    ? "No ransom will be accepted. Escape or rescue would void this sentence. The settlement ending the rebellion would remain in force."
                    : "No ransom will be accepted. Escape or rescue would void this sentence and leave the war unresolved.";
            }
            else if (phase == "escaped")
            {
                news.Title = "A Captive Leader Goes Free"; news.Headline = "The pending judgment is void";
                news.Body = name + " is no longer in the captor's custody. The pending judgment has been cancelled.";
                news.Consequences = judgment.DefeatedLord
                    ? "The death sentence will not be carried out. The settlement ending the rebellion remains in force."
                    : "No settlement or other consequences of that judgment take effect. The war continues on its existing terms.";
            }
            else
            {
                switch (judgment.Verdict)
                {
                    case RulerVerdict.TortureExecution:
                    case RulerVerdict.CleanExecution:
                    case RulerVerdict.RebelExecution:
                        news.Body = judgment.DefeatedLord
                            ? ruler + " has carried out the death sentence against " + name + "."
                            : "Following the execution of " + name + ", the struggle between " + realm + " and " + conqueror + " has entered a new chapter.";
                        if (judgment.DefeatedLord)
                            news.Consequences = "The settlement ending the rebellion remains in force.";
                        else if (judgment.CivilWar)
                            news.Consequences = judgment.RebelVictory
                                ? "The rebels retain their independence. The civil war continues against the loyalist realm."
                                : "The rebellion has been defeated and its realm has been brought back under the crown.";
                        else
                        {
                            string successor = defeated?.Leader?.IsAlive == true ? defeated.Leader.Name.ToString() : "";
                            int defections = judgment.Clans.Count(c => ReignRulerCampaignBehavior.ClanById(c.ClanId)?.Kingdom == winning);
                            news.Consequences = (successor.Length > 0 ? successor + " now leads " + realm + ". " : "The succession remains unsettled. ")
                                + (defeated?.IsEliminated == false && winning?.IsAtWarWith(defeated) == true ? "The war continues. " : "")
                                + (defections > 0 ? defections + (defections == 1 ? " noble house has" : " noble houses have") + " joined " + conqueror + "." : "No noble houses have defected under this judgment.");
                        }
                        break;
                    case RulerVerdict.Exile:
                        news.Body = ruler + " has spared " + name + " and sent the defeated ruler into exile. " + realm + " and " + conqueror + " are now at peace.";
                        var holding = ReignRulerCampaignBehavior.Holding(judgment.CededSettlementId);
                        var successorHero = ReignRulerCampaignBehavior.HeroById(judgment.SuccessorId);
                        news.Consequences = (successorHero?.Name?.ToString() ?? "A new ruler") + " takes the throne of " + realm
                            + ". The peace grants " + (holding?.Name?.ToString() ?? "the agreed holding") + " and "
                            + judgment.Payments.Values.Sum(v => (long)v).ToString("N0") + " denars in reparations to " + conqueror + ".";
                        break;
                    case RulerVerdict.Submission:
                        news.Body = name + " has submitted to " + ruler + ". The noble houses and lands of " + realm + " have joined " + conqueror + ".";
                        news.Consequences = "The former ruler lives on as a clan leader, grateful for being spared. The united realm must now weather the unrest of conquest.";
                        break;
                    case RulerVerdict.Independence:
                        news.Body = ruler + " has secured recognition of the rebels' independence and released " + name + ".";
                        news.Consequences = "The two realms are now at peace. Their surviving rulers retain their crowns.";
                        break;
                    case RulerVerdict.Reunification:
                        news.Body = "The realm has been reunited under " + ruler + ". " + name + " has been spared.";
                        news.Consequences = "The former ruler remains an ordinary member of the same noble house. The civil war is over.";
                        break;
                    case RulerVerdict.Freedom:
                        news.Body = ruler + " has granted " + name + " freedom.";
                        news.Consequences = judgment.CivilWar && !judgment.DefeatedLord ? "The defeated rebellion has been brought back under the crown." : "The captive has been released.";
                        break;
                    case RulerVerdict.Imprisonment:
                        news.Body = ruler + " has spared " + name + " but ordered the defeated leader to remain in prison.";
                        news.Consequences = "The prisoner remains alive in the victor's custody.";
                        break;
                }
            }
            Publish(news);
        }

        internal void KingdomEvent(ReignKingdomEventRecord record, Kingdom kingdom, Kingdom other, Hero formerRuler, Hero heir, bool ended = false)
        {
            var news = WithPeople(new ReignNewsArticle
            {
                Id = "crier:world:" + record.EventId + (ended ? ":ended" : ":began"), Category = ReignNewsCategories.WorldEvents,
                Title = ended ? "News from the Realm" : "A Turn in the Realm's Fortune", Headline = ended ? record.Title + " ends" : record.Title,
                Body = ReignNewsCopy.WorldEvent(record.ArchetypeId, kingdom?.Name?.ToString(), other?.Name?.ToString(),
                    formerRuler?.Name?.ToString(), heir?.Name?.ToString(), ended, record.Status == "cancelled"),
                Consequences = !ended && record.IsTimed ? "These conditions will last for " + (record.EndDay - record.StartDay).ToString("0") + " days." : "",
                WorldDay = ended ? CampaignTime.Now.ToDays : record.StartDay, Positive = record.Polarity == "beneficial"
            }, formerRuler ?? kingdom?.Leader, heir ?? other?.Leader, record.ArchetypeId == "orderly_succession" ? "The former ruler" : "The affected realm", heir == null ? "The neighboring realm" : "The new ruler");
            news.ActorRealmId = record.KingdomStringId; news.ActorRealmName = kingdom?.Name?.ToString() ?? "";
            Publish(news);
        }

        internal void ClanRuling(ReignClanConflictNotice notice)
        {
            Publish(new ReignNewsArticle
            {
                Id = "crier:clan:" + notice.TimelineId + ":" + notice.IncidentId, Category = ReignNewsCategories.ClanRulings,
                Title = "News from the Royal Court", Headline = notice.Headline, WorldDay = notice.WorldDay,
                Body = ReignNewsCopy.Name(notice.RulerName, "The ruler") + " of " + ReignNewsCopy.Name(notice.KingdomName, "the realm")
                    + " has heard a dispute between " + ReignNewsCopy.Name(notice.ClanAName, "one noble house") + " and " + ReignNewsCopy.Name(notice.ClanBName, "another") + ". "
                    + ReignNewsCopy.Sentence(notice.NewsDescription),
                Consequences = ReignNewsCopy.ClanRuling(notice.RulerName, notice.ClanAName, notice.ClanBName, notice.WinningSide, notice.MediationSucceeded),
                ActorId = notice.LeaderAId, ActorName = notice.ClanAName, ActorRealmId = notice.KingdomId, ActorRealmName = notice.KingdomName, ActorCaption = "A party to the dispute",
                TargetId = notice.LeaderBId, TargetName = notice.ClanBName, TargetRealmId = notice.KingdomId, TargetRealmName = notice.KingdomName, TargetCaption = "A party to the dispute",
                Positive = notice.MediationSucceeded
            });
        }
        internal void ForeignRuling(ReignPoliticalPressureNotice notice)
        {
            var origin = new ReignForeignRuling { Ruler = notice.OriginRulerName, Realm = notice.OriginKingdomName,
                Stance = notice.OriginStance, Petitioners = notice.OriginPetitioners, Before = notice.OriginPressureBefore, After = notice.OriginPressureAfter };
            var target = new ReignForeignRuling { Ruler = notice.TargetRulerName, Realm = notice.TargetKingdomName,
                Stance = notice.TargetStance, Petitioners = notice.TargetPetitioners, Before = notice.TargetPressureBefore, After = notice.TargetPressureAfter };
            Publish(new ReignNewsArticle
            {
                Id = "crier:foreign:" + notice.TimelineId + ":" + notice.IncidentId, Category = ReignNewsCategories.ForeignRulings,
                Title = "News Between the Realms", Headline = notice.Headline, WorldDay = notice.WorldDay,
                Body = ReignNewsCopy.ForeignRulingSummary(notice.Headline, notice.Channel, origin, target)
                    + (string.IsNullOrWhiteSpace(notice.NewsDescription) ? "" : "\n\nThe petition: " + ReignNewsCopy.Sentence(notice.NewsDescription)),
                Consequences = ReignNewsCopy.ForeignRulingConsequences(notice.Polarity, notice.Channel, origin, target),
                ActorId = notice.OriginRulerId, ActorName = notice.OriginRulerName, ActorRealmId = notice.OriginKingdomId, ActorRealmName = notice.OriginKingdomName,
                TargetId = notice.TargetRulerId, TargetName = notice.TargetRulerName, TargetRealmId = notice.TargetKingdomId, TargetRealmName = notice.TargetKingdomName,
                Positive = notice.Polarity != "hostile"
            });
        }
    }
}
