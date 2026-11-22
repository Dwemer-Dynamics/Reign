using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Court;
using ReignBeta.UI.HiddenInformation;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using ReignBeta.Court.Campaign;
using ReignBeta.Court.FamilyChambers;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.UI;
using ReignBeta.Government.UI;
using ReignBeta.Platform.Integration;
using ReignBeta.Spymaster.UI;
using ReignBeta.WorldSimulation.World;

namespace ReignBeta.Court.UI
{
    public sealed partial class ReignCourtScreenVM
    {
        private void BuildActions(ReignCourtCardVM card)
        {
            Actions.Clear();
            if (card == null) return;
            if (card.TabId == "military" && card.Id == "military_standing" && card.Payload is Kingdom standingRealm)
            {
                bool sovereign = standingRealm.Leader == Hero.MainHero;
                foreach (string mode in new[] { "peace", "war" })
                    Actions.Add(new ReignCourtActionVM("ORDER " + mode.ToUpperInvariant() + " STANDING",
                        mode == "war" ? "Permit full field recruitment before war. Every soldier still needs local manpower, food and funding."
                            : "Restore the saved peace field target, including during war. Garrisons keep full targets.",
                        sovereign, sovereign ? string.Empty : "Only the current sovereign may issue this order.", () => {
                            if (Hero.MainHero?.Clan?.Kingdom != standingRealm || standingRealm.Leader != Hero.MainHero)
                            { StatusText = "The kingdom or its sovereign changed; reopen military standing."; return; }
                            ReignBeta.WorldSimulation.Economy.ReignMilitaryReadiness.Order(Hero.MainHero, mode, out string message);
                            RefreshAll();
                            var updated = TabRows.FirstOrDefault(x => x.Id == "military_standing");
                            if (updated != null) SelectCard(updated);
                            StatusText = message;
                        }));
                return;
            }
            if (card.Payload is ReignDocketHistoryRecord docketHistory)
            {
                ReignNobleDocketMatter noble = _court.ResolveNobleJudgmentHistory(docketHistory);
                if (noble == null) return;
                var fence = ReignServerClient.RequestFence.Capture();
                CourtSession session = _court.Session;
                long viewRevision = _audienceViewRevision;
                bool OwnsJudgment() => !_finalized && DetailVisible && viewRevision == _audienceViewRevision
                    && ReferenceEquals(_selectedCard, card) && ReferenceEquals(_court.Session, session)
                    && fence.IsCurrent() && ReferenceEquals(_court.ResolveNobleJudgmentHistory(docketHistory), noble);

                bool mayReverse = !noble.Irreversible && noble.CustodyHold?.Executed != true;
                Actions.Add(new ReignCourtActionVM("REVERSE JUDGMENT",
                    noble.Irreversible
                        ? "An execution or other irreversible death prevents reversal."
                        : "Append a formal reversal, compensate safe relation/reputation effects, and accept a credibility cost. Native family, gold, and death effects are not rewound.",
                    mayReverse, mayReverse ? string.Empty : "This judgment cannot be reversed.",
                    () => ConfirmNobleReversal(noble, OwnsJudgment)));
                if (noble.IsMurder && noble.State == ReignNobleMatterState.Convicted)
                {
                    bool mayExecute = !noble.Irreversible && _court.ResolveHeldNobleJudgmentPrisoner(noble) != null;
                    Actions.Add(new ReignCourtActionVM("EXECUTE CONVICTED PRISONER",
                        "Irreversibly execute the living prisoner. Native execution consequences remain; only automatic ruler-relation losses are suppressed.",
                        mayExecute, mayExecute ? string.Empty : "The prisoner is no longer held under this sentence.",
                        () => ConfirmMurderExecution(noble, OwnsJudgment)));
                }
                return;
            }
            if (card.Payload is CourtMatter matter)
            {
                if (card.Hero != null && !matter.IsTerminal)
                    Actions.Add(new ReignCourtActionVM("BEGIN AUDIENCE", "Speak in-screen. Dialogue may characterize and advise but cannot apply mechanics.", _court.ServerAvailable, _court.ServerStatus, () => BeginAudience(matter, card.Hero)));
                JArray options;
                try { options = JArray.Parse(matter.DecisionOptionsJson ?? "[]"); } catch { options = new JArray(); }
                foreach (JObject option in options.OfType<JObject>())
                {
                    JObject captured = option;
                    string navigate = captured.Value<string>("navigateTab");
                    if (!string.IsNullOrWhiteSpace(navigate))
                    {
                        Actions.Add(new ReignCourtActionVM(captured.Value<string>("label")?.ToUpperInvariant() ?? "OPEN REPORT", "Open the linked filtered court tab.", true, string.Empty,
                            () => SelectTab(Tabs.FirstOrDefault(x => x.Id == navigate))));
                    }
                    else
                    {
                        Actions.Add(new ReignCourtActionVM(captured.Value<string>("label")?.ToUpperInvariant() ?? "DECIDE", captured.Value<string>("description") ?? "Preview and confirm this coded consequence package.",
                            _court.ServerAvailable && !matter.IsTerminal, _court.ServerStatus, () => ConfirmMatterDecision(matter, captured)));
                    }
                }
                if (matter.Kind == ReignCourtMatterKind.Rumor && _court.Offices.Any(x => x.IsActive && x.Office == ReignCourtOffice.Spymaster))
                {
                    Actions.Add(new ReignCourtActionVM("INVESTIGATE CLAIM", IntelTerms("Result remains source- and confidence-limited.",750,8f,7f,0.2f), _court.ServerAvailable, _court.ServerStatus,
                        () => RunCourtAction(() => _court.StartIntelligenceOperationAsync("investigate_claim", "matter", matter.MatterId, 750, 8f, 7f, 0.2f), "Investigation started.")));
                    Actions.Add(new ReignCourtActionVM("TRACE RUMOR", IntelTerms("Trace the known source chain without revealing canonical secret truth.",900,10f,10f,0.25f), _court.ServerAvailable, _court.ServerStatus,
                        () => RunCourtAction(() => _court.StartIntelligenceOperationAsync("trace_rumor", "matter", matter.MatterId, 900, 10f, 10f, 0.25f), "Rumor trace started.")));
                    Actions.Add(new ReignCourtActionVM("COUNTER RUMOR", IntelTerms("Attempt a counter-message with no guaranteed public belief change.",1200,12f,14f,0.3f), _court.ServerAvailable, _court.ServerStatus,
                        () => RunCourtAction(() => _court.StartIntelligenceOperationAsync("counter_rumor", "matter", matter.MatterId, 1200, 12f, 14f, 0.3f), "Counter-rumor operation started.")));
                    Actions.Add(new ReignCourtActionVM("PLANT COUNTERCLAIM", IntelTerms("Plant a competing source-limited claim.",1500,15f,14f,0.4f), _court.ServerAvailable, _court.ServerStatus,
                        () => RunCourtAction(() => _court.StartIntelligenceOperationAsync("plant_rumor", "matter", matter.MatterId, 1500, 15f, 14f, 0.4f), "Rumor-planting operation started.")));
                }
                return;
            }

            if (card.Payload is ReignSettlementSupplySnapshot land)
            {
                Actions.Add(new ReignCourtActionVM("SEND 25 GRAIN", "Atomically transfer real grain between scoped ItemRosters after revalidation.", _court.ServerAvailable, _court.ServerStatus, () => TrySendEmergencyGrain(land)));
                Town town = Settlement.Find(land.SettlementStringId)?.Town;
                Hero governor = SubjectHeroes().Where(x => x?.Clan == Clan.PlayerClan && !x.IsPrisoner && x.IsAlive && x.IsActive
                        && x.Age >= TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge)
                    .OrderByDescending(x => x.GetSkillValue(DefaultSkills.Steward)).FirstOrDefault();
                bool canGovern = town?.OwnerClan == Clan.PlayerClan && governor != null;
                Actions.Add(new ReignCourtActionVM("APPOINT GOVERNOR", canGovern ? "Appoint " + governor.Name + " after native eligibility is checked again." : "Only an eligible player-clan member may govern this fief.",
                    _court.ServerAvailable && canGovern, canGovern ? _court.ServerStatus : "No eligible player-clan governor is available.",
                    () => RunCourtAction(() => _court.SubmitNativeActionAsync(new JObject { ["type"] = "appoint_governor", ["settlementStringId"] = land.SettlementStringId, ["heroStringId"] = governor?.StringId ?? string.Empty }), "Governor appointed.")));
                bool canFund = town?.OwnerClan == Clan.PlayerClan && (Hero.MainHero?.Gold ?? 0) >= 5000;
                Actions.Add(new ReignCourtActionVM("FUND CONSTRUCTION (5,000)", "Spend real player gold and add it to Bannerlord's native construction-boost reserve.",
                    _court.ServerAvailable && canFund, canFund ? _court.ServerStatus : "Requires a player-clan fief and 5,000 gold.",
                    () => RunCourtAction(() => _court.SubmitNativeActionAsync(new JObject { ["type"] = "fund_construction", ["settlementStringId"] = land.SettlementStringId, ["goldAmount"] = 5000 }), "Construction funding delivered.")));
                return;
            }

            if (card.Payload is Hero hero)
            {
                Actions.Add(new ReignCourtActionVM("CORRESPOND", "Open a persistent correspondence with this known person.", true, string.Empty, () =>
                {
                    _close?.Invoke();
                    ReignCorrespondenceScreenManager.Open(hero);
                }));
                if (hero != Hero.MainHero)
                {
                    bool canGift = (Hero.MainHero?.Gold ?? 0) >= 1000;
                    Actions.Add(new ReignCourtActionVM("GIVE 1,000 GOLD", "Transfer player gold through Bannerlord's native gold action.", _court.ServerAvailable && canGift,
                        canGift ? _court.ServerStatus : "The player needs 1,000 gold.",
                        () => RunCourtAction(() => _court.SubmitNativeActionAsync(new JObject { ["type"] = "transfer_gold", ["fromHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty, ["toHeroStringId"] = hero.StringId, ["amount"] = 1000 }), "Gift delivered.")));
                    Actions.Add(new ReignCourtActionVM("PROMISE 1,000 GOLD", "Create a private fourteen-day obligation with a coded breach and receipt-backed fulfillment matter.", _court.ServerAvailable, _court.ServerStatus,
                        () => RunCourtAction(() => _court.RecordGoldPromiseAsync(hero,1000,14f), "Promise recorded and added to the court agenda.")));
                    bool hasSpymaster = _court.Offices.Any(x => x.IsActive && x.Office == ReignCourtOffice.Spymaster);
                    Actions.Add(new ReignCourtActionVM("INVESTIGATE PERSON", IntelTerms("Assign a source-limited inquiry.",750,8f,7f,0.2f), _court.ServerAvailable && hasSpymaster,
                        hasSpymaster ? _court.ServerStatus : "Appoint a Spymaster first.",
                        () => RunCourtAction(() => _court.StartIntelligenceOperationAsync("investigate_person", "hero", hero.StringId, 750, 8f, 7f, 0.2f), "Investigation started.")));
                    Actions.Add(new ReignCourtActionVM("RECRUIT ASSET", IntelTerms("Attempt a recruitment with substantial exposure risk.",2500,20f,21f,0.45f), _court.ServerAvailable && hasSpymaster,
                        hasSpymaster ? _court.ServerStatus : "Appoint a Spymaster first.",
                        () => RunCourtAction(() => _court.StartIntelligenceOperationAsync("recruit_asset", "hero", hero.StringId, 2500, 20f, 21f, 0.45f), "Asset recruitment started.")));
                }
                CourtOfficeAssignment held = _court.Offices.FirstOrDefault(x => x.IsActive && x.HeroStringId == hero.StringId);
                if (held != null)
                {
                    Actions.Add(new ReignCourtActionVM("DISMISS AS " + held.Office.ToString().ToUpperInvariant(), "End the major office assignment with a revision-checked receipt.", _court.ServerAvailable, _court.ServerStatus,
                        () => RunCourtAction(() => _court.DismissOfficeAsync(held), "Office dismissed.")));
                }
                else
                {
                    foreach (ReignCourtOffice office in Enum.GetValues(typeof(ReignCourtOffice)).Cast<ReignCourtOffice>())
                    {
                        ReignCourtOffice selectedOffice = office;
                        CourtOfficeAssignment incumbent = _court.Offices.FirstOrDefault(x => x.IsActive && x.Office == selectedOffice);
                        bool eligible = ReignCourtOfficeService.IsEligible(hero, _court.Session.Authority, _court.Offices, out string reason);
                        string description = incumbent == null ? "Appoint to the vacant major office." : "Replace " + (FindHero(incumbent.HeroStringId)?.Name?.ToString() ?? "the incumbent") + ".";
                        Actions.Add(new ReignCourtActionVM("APPOINT " + selectedOffice.ToString().ToUpperInvariant(), description, _court.ServerAvailable && eligible,
                            eligible ? _court.ServerStatus : reason, () => RunCourtAction(() => _court.AssignOfficeAsync(selectedOffice, hero), selectedOffice + " appointed.")));
                    }
                }
                if (_court.Session?.Authority == ReignCourtAuthority.Royal && hero != Hero.MainHero)
                {
                    CourtRegentAssignment regent = _court.ActiveRegent;
                    bool regentEligible = hero.IsAlive && hero.IsActive && !hero.IsPrisoner
                        && hero.Age >= TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge
                        && (hero.Clan == Clan.PlayerClan || hero.CompanionOf == Clan.PlayerClan || ReignCourtNobleCampaignBehavior.IsCourtNoble(hero) || hero.Clan?.Kingdom == Clan.PlayerClan?.Kingdom);
                    if (regent?.HeroStringId == hero.StringId)
                        Actions.Add(new ReignCourtActionVM("DISMISS AS REGENT","End the separate Regent assignment. This does not change any major office.",_court.ServerAvailable,_court.ServerStatus,
                            ()=>RunCourtAction(()=>_court.DismissRegentAsync(),"Regent dismissed.")));
                    else
                        Actions.Add(new ReignCourtActionVM(regent==null?"APPOINT REGENT":"REPLACE REGENT",regent==null?"Delegate ordinary unattended royal tasks. Major matters still require your mail reply.":"Replace "+(FindHero(regent.HeroStringId)?.Name?.ToString()??"the current Regent")+". Major offices are unaffected.",
                            _court.ServerAvailable&&regentEligible,regentEligible?_court.ServerStatus:"The Regent must be an adult, free, active person within royal court scope.",
                            ()=>RunCourtAction(()=>_court.AssignRegentAsync(hero),"Regent appointed.")));
                }
                return;
            }

            if(card.Payload is CourtRegentAssignment regentAssignment)
            {
                if(regentAssignment.IsActive)Actions.Add(new ReignCourtActionVM("DISMISS REGENT","End this separate delegation without changing the hero's major office.",_court.ServerAvailable,_court.ServerStatus,
                    ()=>RunCourtAction(()=>_court.DismissRegentAsync(),"Regent dismissed.")));
                return;
            }


            if(card.Payload is CourtObligation obligation)
            {
                CourtMatter obligationMatter=_court.Matters.FirstOrDefault(x=>!x.IsTerminal&&x.SourceKey=="obligation:"+obligation.ObligationId);
                Actions.Add(new ReignCourtActionVM("OPEN FULFILLMENT MATTER","Review the exact promised terms and the predefined breach outcome.",obligationMatter!=null,
                    obligationMatter==null?"This obligation has no currently actionable coded matter.":string.Empty,()=>SelectCard(obligationMatter==null?null:MatterCard(obligationMatter,"subjects"))));
                return;
            }

            if(card.Payload is CourtPlot plot)
            {
                bool hasSpymaster=_court.Offices.Any(x=>x.IsActive&&x.Office==ReignCourtOffice.Spymaster);
                Actions.Add(new ReignCourtActionVM("EXPOSE PLOT",IntelTerms("Run a source-limited exposure operation; discovery does not reveal canonical hidden truth.",1500,15f,10f,0.3f),_court.ServerAvailable&&hasSpymaster,
                    hasSpymaster?_court.ServerStatus:"Appoint a Spymaster first.",()=>RunCourtAction(()=>_court.StartIntelligenceOperationAsync("expose_plot","plot",plot.PlotId,1500,15f,10f,0.3f),"Exposure operation started.")));
                return;
            }

            if (card.Payload is CourtOfficeAssignment officeAssignment)
            {
                if (officeAssignment.IsActive)
                    Actions.Add(new ReignCourtActionVM("DISMISS OFFICEHOLDER", "Dismiss with revision and command-id validation.", _court.ServerAvailable, _court.ServerStatus,
                        () => RunCourtAction(() => _court.DismissOfficeAsync(officeAssignment), "Office dismissed.")));
                if (officeAssignment.Office == ReignCourtOffice.Spymaster && officeAssignment.IsActive)
                {
                    Actions.Add(new ReignCourtActionVM("OPEN SHADOW OFFICE",
                        "Open the dedicated intelligence, subterfuge, and rumor-management interface.", true, string.Empty,
                        () => { _close?.Invoke(); ReignSpymasterScreenManager.Open(_court); }));
                }
                return;
            }

            if (card.Payload is AmbassadorPosting posting)
            {
                Actions.Add(new ReignCourtActionVM("GATHER PUBLIC INFORMATION", "Assign a source-safe public information mission.", _court.ServerAvailable, _court.ServerStatus,
                    () => RunCourtAction(() => _court.SetAmbassadorMissionAsync(posting, "public_information", new JObject()), "Ambassador mission updated.")));
                Actions.Add(new ReignCourtActionVM("IMPROVE RELATIONS", "Direct the posting toward sustained public relationship work.", _court.ServerAvailable, _court.ServerStatus,
                    () => RunCourtAction(() => _court.SetAmbassadorMissionAsync(posting, "improve_relations", new JObject { ["targetKingdomStringId"] = posting.TargetKingdomStringId }), "Ambassador mission updated.")));
                Actions.Add(new ReignCourtActionVM("DELIVER TRADE PROPOSAL","Send a specific package through this posting; the foreign NPC ruler may accept, refuse, or counter.",_court.ServerAvailable,_court.ServerStatus,
                    ()=>RunCourtAction(()=>_court.DeliverAmbassadorProposalAsync(posting,ReignWorldActionType.DiplomacySignTradeAgreement,new JObject()),"Ambassador reports that the proposal was accepted.")));
                Actions.Add(new ReignCourtActionVM("RECALL", "End this persistent posting without creating a vulnerable map party.", _court.ServerAvailable, _court.ServerStatus,
                    () => RunCourtAction(() => _court.RecallAmbassadorAsync(posting), "Ambassador recalled.")));
                return;
            }

            if (card.Payload is IntelligenceOperation operation)
            {
                Actions.Add(new ReignCourtActionVM("CANCEL OPERATION", "Cancel an active operation; spent costs are not refunded.", _court.ServerAvailable && operation.State == ReignIntelligenceOperationState.Active,
                    operation.State == ReignIntelligenceOperationState.Active ? _court.ServerStatus : "Only active operations can be cancelled.",
                    () => RunCourtAction(() => _court.CancelIntelligenceOperationAsync(operation), "Operation cancelled.")));
                return;
            }

            if (card.Payload is Kingdom kingdom)
            {
                Kingdom ours = Clan.PlayerClan?.Kingdom;
                bool war = ours != null && FactionManager.IsAtWarAgainstFaction(ours, kingdom);
                ReignWorldActionType type = war ? ReignWorldActionType.DiplomacyMakePeace : ReignWorldActionType.DiplomacySignTradeAgreement;
                Actions.Add(new ReignCourtActionVM(war ? "SUBMIT PEACE PACKAGE" : "SUBMIT TRADE PACKAGE", "Previewed royal package submitted through the existing diplomacy validator and atomic executor.", _court.ServerAvailable, _court.ServerStatus,
                    () => SubmitDiplomaticPlan(new ReignWorldActionRecord
                    {
                        Type = type,
                        ActorHeroStringId = ours?.Leader?.StringId ?? string.Empty,
                        ActorKingdomStringId = ours?.StringId ?? string.Empty,
                        TargetHeroStringId = kingdom.Leader?.StringId ?? string.Empty,
                        TargetKingdomStringId = kingdom.StringId,
                        Reason = "A package submitted from royal court.",
                        TermsJson = "{}"
                    }, war ? "Peace package submitted." : "Trade package submitted.")));
                if(!war)
                {
                    Actions.Add(new ReignCourtActionVM("PROPOSE NON-AGGRESSION PACT","Send a ten-year pact to the foreign ruler, who may accept, refuse, or counter.",_court.ServerAvailable,_court.ServerStatus,
                        ()=>SubmitDiplomaticPlan(new ReignWorldActionRecord{Type=ReignWorldActionType.DiplomacySignNonAggressionPact,ActorHeroStringId=ours?.Leader?.StringId??string.Empty,ActorKingdomStringId=ours?.StringId??string.Empty,TargetHeroStringId=kingdom.Leader?.StringId??string.Empty,TargetKingdomStringId=kingdom.StringId,Reason="The royal court proposes a ten-year non-aggression pact.",TermsJson="{\"durationDays\":1260}"},"Pact accepted.")));
                    Actions.Add(new ReignCourtActionVM("PROPOSE ALLIANCE","Send a formal alliance proposal; deterministic power and consideration rules remain authoritative.",_court.ServerAvailable,_court.ServerStatus,
                        ()=>SubmitDiplomaticPlan(new ReignWorldActionRecord{Type=ReignWorldActionType.DiplomacySignAlliance,ActorHeroStringId=ours?.Leader?.StringId??string.Empty,ActorKingdomStringId=ours?.StringId??string.Empty,TargetHeroStringId=kingdom.Leader?.StringId??string.Empty,TargetKingdomStringId=kingdom.StringId,Reason="The royal court proposes a formal alliance.",TermsJson="{}"},"Alliance accepted.")));
                    Actions.Add(new ReignCourtActionVM("OFFER 10,000-DENAR SUBSIDY","Offer a concrete subsidy through the negotiated diplomacy package flow.",_court.ServerAvailable&&(Hero.MainHero?.Gold??0)>=10000,(Hero.MainHero?.Gold??0)>=10000?_court.ServerStatus:"The player needs 10,000 gold.",
                        ()=>SubmitDiplomaticPlan(new ReignWorldActionRecord{Type=ReignWorldActionType.DiplomacyLoanOrSubsidy,ActorHeroStringId=ours?.Leader?.StringId??string.Empty,ActorKingdomStringId=ours?.StringId??string.Empty,TargetHeroStringId=kingdom.Leader?.StringId??string.Empty,TargetKingdomStringId=kingdom.StringId,Reason="The royal court offers a diplomatic subsidy.",TermsJson=new JObject{{"gold",10000},{"fromHeroStringId",Hero.MainHero?.StringId??string.Empty},{"toHeroStringId",kingdom.Leader?.StringId??string.Empty}}.ToString(Newtonsoft.Json.Formatting.None)},"Subsidy package accepted.")));
                    Actions.Add(new ReignCourtActionVM("GUARANTEE INDEPENDENCE","Offer a formal guarantee through the foreign-ruler response flow.",_court.ServerAvailable,_court.ServerStatus,
                        ()=>SubmitDiplomaticPlan(new ReignWorldActionRecord{Type=ReignWorldActionType.DiplomacyGuaranteeIndependence,ActorHeroStringId=ours?.Leader?.StringId??string.Empty,ActorKingdomStringId=ours?.StringId??string.Empty,TargetHeroStringId=kingdom.Leader?.StringId??string.Empty,TargetKingdomStringId=kingdom.StringId,Reason="The royal court offers a guarantee of independence.",TermsJson="{}"},"Guarantee accepted.")));
                }
                return;
            }

            if (card.Payload is MobileParty party)
            {
                Settlement friendly = party.CurrentSettlement ?? Clan.PlayerClan?.Kingdom?.Fiefs.FirstOrDefault()?.Settlement;
                Settlement hostile = Settlement.All.Where(x => x.IsFortification && party.MapFaction != null && x.MapFaction != null && x.MapFaction.IsAtWarWith(party.MapFaction))
                    .OrderBy(x => x.GetPosition2D.DistanceSquared(party.GetPosition2D)).FirstOrDefault();
                if (friendly != null)
                {
                    Actions.Add(new ReignCourtActionVM("RECRUIT AND RECOVER", "Submit a validated recovery objective to the existing strategy executor.", _court.ServerAvailable, _court.ServerStatus,
                        () => SubmitWorldPlan(StrategyPlan(ReignWorldActionType.StrategyRecruitAndRecover, party, friendly), "Recovery plan submitted.")));
                    Actions.Add(new ReignCourtActionVM("DEFEND " + friendly.Name.ToString().ToUpperInvariant(), "Order this commander toward a friendly defensive objective through the existing movement validator.", _court.ServerAvailable, _court.ServerStatus,
                        () => SubmitWorldPlan(StrategyPlan(ReignWorldActionType.RegularGoToSettlement, party, friendly), "Defensive movement submitted.")));
                    Actions.Add(new ReignCourtActionVM("PATROL " + friendly.Name.ToString().ToUpperInvariant(), "Submit a validated patrol objective around the selected friendly settlement.", _court.ServerAvailable, _court.ServerStatus,
                        () => SubmitWorldPlan(StrategyPlan(ReignWorldActionType.RegularPatrolAroundSettlement, party, friendly), "Patrol plan submitted.")));
                }
                if (hostile != null)
                {
                    Actions.Add(new ReignCourtActionVM("FORM ARMY", "Attempt to form an army around this commander for the selected hostile objective.", _court.ServerAvailable, _court.ServerStatus,
                        () => SubmitWorldPlan(StrategyPlan(ReignWorldActionType.StrategyFormArmy, party, hostile), "Army plan submitted.")));
                    Actions.Add(new ReignCourtActionVM("ATTACK " + hostile.Name.ToString().ToUpperInvariant(), "Submit the hostile objective through the existing strategy validator and executor.", _court.ServerAvailable, _court.ServerStatus,
                        () => SubmitWorldPlan(StrategyPlan(ReignWorldActionType.StrategyAttackSettlement, party, hostile), "Attack plan submitted.")));
                    Actions.Add(new ReignCourtActionVM("CAPTURE " + hostile.Name.ToString().ToUpperInvariant(), "Submit the staged recruit, form, attack, and capture plan.", _court.ServerAvailable, _court.ServerStatus,
                        () => SubmitWorldPlan(StrategyPlan(ReignWorldActionType.StrategyCaptureSettlement, party, hostile), "Capture plan submitted.")));
                }
                return;
            }

            if (card.TabId == "ambassadors" && card.Id == "vacant")
            {
                Hero candidate = SubjectHeroes().FirstOrDefault(x => !x.IsPrisoner && !_court.Ambassadors.Any(a => a.HeroStringId == x.StringId && a.Status != "recalled"));
                Kingdom target = Kingdom.All.Where(x => x != null && !x.IsEliminated && x != Clan.PlayerClan?.Kingdom).OrderByDescending(x => x.CurrentTotalStrength).FirstOrDefault();
                Actions.Add(new ReignCourtActionVM("FILL POSTING", candidate == null || target == null ? "No eligible candidate or target realm is available." : "Assign " + candidate.Name + " to " + target.Name + ".",
                    _court.ServerAvailable && candidate != null && target != null, _court.ServerStatus,
                    () => RunCourtAction(() => _court.AssignAmbassadorAsync(candidate, target, "public_information"), "Ambassador assigned.")));
            }
        }
    }
}
