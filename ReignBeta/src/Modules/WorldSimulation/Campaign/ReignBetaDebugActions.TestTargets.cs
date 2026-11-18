using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using ReignBeta.Dialogue.UI;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Diplomacy.Integration;
using ReignBeta.KingdomEvents.Campaign;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignBetaDebugActions
    {
        private static bool RequireTestMode()
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings == null || settings.McmTestModeEnabled)
            {
                return true;
            }

            Show("Enable Bannerlord Reign > Debug > Enable Debug Controls before using test actions.");
            return false;
        }

        private static bool TryGetPlayerKingdom(out Kingdom kingdom)
        {
            kingdom = Clan.PlayerClan?.Kingdom;
            if (ReignServices.AI == null || kingdom == null)
            {
                Show("No active player kingdom or Bannerlord Reign behavior.");
                return false;
            }

            return true;
        }

        private static Kingdom FindEnemyKingdom(Kingdom actor)
        {
            return Kingdom.All.FirstOrDefault(x => x != null && x != actor && !x.IsEliminated && actor.IsAtWarWith(x));
        }

        private static Kingdom FindPeacefulKingdom(Kingdom actor)
        {
            return Kingdom.All.FirstOrDefault(x => x != null && x != actor && !x.IsEliminated && !actor.IsAtWarWith(x));
        }

        private static Hero FindTestLord(Kingdom kingdom)
        {
            return kingdom?.AliveLords
                .Where(x => x != null && x != Hero.MainHero && x.PartyBelongedTo != null && !x.PartyBelongedTo.IsMainParty)
                .OrderByDescending(x => x.PartyBelongedTo.Party?.EstimatedStrength ?? 0f)
                .FirstOrDefault();
        }

        private static Settlement FindHostileFortification(Kingdom kingdom, Hero actor)
        {
            if (kingdom == null)
            {
                return null;
            }

            Vec2 origin = actor?.PartyBelongedTo == null ? Vec2.Zero : actor.PartyBelongedTo.GetPosition2D;
            return Settlement.All
                .Where(x => x != null
                    && x.IsFortification
                    && x.MapFaction != null
                    && kingdom.IsAtWarWith(x.MapFaction)
                    && !x.IsUnderSiege)
                .OrderBy(x => origin == Vec2.Zero ? 0f : origin.DistanceSquared(x.GetPosition2D))
                .FirstOrDefault();
        }

        private static Settlement FindEnemyFortificationOwnedBy(Kingdom target)
        {
            return Settlement.All
                .Where(x => x != null && x.IsFortification && x.MapFaction == target && !x.IsUnderSiege)
                .OrderBy(x => x.StringId)
                .FirstOrDefault();
        }

        private static Settlement FindAnyUsefulSettlement(Kingdom kingdom)
        {
            return kingdom?.Settlements.FirstOrDefault(x => x != null && !x.IsHideout)
                ?? Settlement.All.FirstOrDefault(x => x != null && !x.IsHideout);
        }

        private static List<Settlement> FindRoyalCourtTestLandPlan(Clan playerClan)
        {
            List<Settlement> plan = playerClan?.Fiefs
                .Select(x => x?.Settlement)
                .Where(x => x?.IsFortification == true && !x.IsUnderSiege)
                .OrderByDescending(x => x.IsTown)
                .ThenBy(x => x.StringId)
                .Take(2)
                .ToList() ?? new List<Settlement>();
            if (plan.Count >= 2) return plan;

            Settlement anchor = FindZeonica();
            IFaction anchorFaction = anchor?.MapFaction;
            IEnumerable<Settlement> candidates = Settlement.All
                .Where(x => x != null
                    && x.IsFortification
                    && !x.IsUnderSiege
                    && x.OwnerClan != null
                    && x.OwnerClan != playerClan
                    && !plan.Contains(x))
                .OrderBy(x => x == anchor ? 0 : anchorFaction != null && x.MapFaction == anchorFaction ? 1 : 2)
                .ThenBy(x => x == anchor ? 0 : x.IsCastle ? 0 : 1)
                .ThenBy(x => playerClan != null && x.Culture == playerClan.Culture ? 0 : 1)
                .ThenBy(x => x.StringId);
            foreach (Settlement candidate in candidates)
            {
                plan.Add(candidate);
                if (plan.Count >= 2) break;
            }
            return plan;
        }

        private static void EnsureRoyalCourtTestVassals(Kingdom kingdom, Hero player, List<string> notes)
        {
            if (kingdom == null || player == null) return;
            const int targetVassalClans = 4;

            List<Clan> invalidTestVassals = kingdom.Clans
                .Where(x => x != kingdom.RulingClan && x != Clan.PlayerClan && IsInvalidTestVassal(x))
                .ToList();
            foreach (Clan invalid in invalidTestVassals)
            {
                if (invalid.IsUnderMercenaryService)
                {
                    ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(invalid, true);
                }
                else
                {
                    ChangeKingdomAction.ApplyByLeaveKingdom(invalid, true);
                }
                notes?.Add(invalid.Name + " removed from the test realm because it is not a regular noble clan");
            }

            List<Clan> current = kingdom.Clans.Where(x => IsUsableTestVassal(x, kingdom)).ToList();
            int needed = Math.Max(0, targetVassalClans - current.Count);
            if (needed > 0)
            {
                List<Clan> candidates = Clan.All
                    .Where(x => x != null
                        && x != Clan.PlayerClan
                        && x.Kingdom != kingdom
                        && !x.IsEliminated
                        && !x.IsMinorFaction
                        && !x.IsBanditFaction
                        && !x.IsClanTypeMercenary
                        && !x.IsUnderMercenaryService
                        && x.Leader != null
                        && x.Leader.IsAlive
                        && x.Leader.IsActive
                        && !x.Leader.IsPrisoner
                        && (x.Kingdom == null || x.Kingdom.RulingClan != x))
                    .OrderBy(x => x.Fiefs.Count == 0 ? 0 : 1)
                    .ThenBy(x => x.Culture == Clan.PlayerClan?.Culture ? 0 : 1)
                    .ThenBy(x => x.Fiefs.Count)
                    .ThenByDescending(x => x.Tier)
                    .ThenBy(x => x.StringId)
                    .ToList();

                foreach (Clan candidate in candidates)
                {
                    if (needed <= 0) break;
                    Kingdom formerKingdom = candidate.Kingdom;
                    List<Settlement> formerHoldings = candidate.Fiefs
                        .Select(x => x?.Settlement)
                        .Where(x => x != null)
                        .ToList();
                    Hero formerRuler = formerKingdom?.RulingClan?.Leader;
                    if (formerHoldings.Count > 0 && formerRuler == null) continue;

                    foreach (Settlement holding in formerHoldings)
                    {
                        ChangeOwnerOfSettlementAction.ApplyByGift(holding, formerRuler);
                    }

                    ChangeKingdomAction.ApplyByJoinToKingdom(candidate, kingdom, CampaignTime.Zero, true);
                    if (candidate.IsUnderMercenaryService)
                    {
                        EndMercenaryServiceAction.EndByBecomingVassal(candidate);
                    }

                    if (IsUsableTestVassal(candidate, kingdom))
                    {
                        current.Add(candidate);
                        needed--;
                        notes?.Add(candidate.Name + " joined as a regular test vassal");
                    }
                    else
                    {
                        notes?.Add(candidate.Name + " could not be confirmed as a regular test vassal");
                    }
                }
            }

            foreach (Clan clan in kingdom.Clans.Where(x => IsUsableTestVassal(x, kingdom)).Take(targetVassalClans))
            {
                if (clan.Influence < 500f) ChangeClanInfluenceAction.Apply(clan, 500f - clan.Influence);
                int relation = player.GetRelation(clan.Leader);
                if (relation < 25) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(player, clan.Leader, 25 - relation, false);
            }

            int finalCount = kingdom.Clans.Count(x => IsUsableTestVassal(x, kingdom));
            notes?.Add(finalCount >= targetVassalClans
                ? "four usable vassal clans ensured"
                : "only " + finalCount + " usable vassal clans were available");
        }

        private static void MovePlayerIntoTestSeat(Settlement settlement, List<string> notes)
        {
            MobileParty party = MobileParty.MainParty;
            if (party == null || settlement == null)
            {
                notes?.Add("test-seat travel unavailable");
                return;
            }

            if (party.MapEvent != null)
            {
                notes?.Add("test-seat travel deferred because the player is in a map event");
                return;
            }

            if (party.CurrentSettlement == settlement
                && PlayerEncounter.EncounterSettlement == settlement
                && PlayerEncounter.LocationEncounter != null)
            {
                party.SetMoveModeHold();
                notes?.Add("already inside " + settlement.Name);
                return;
            }

            if (PlayerEncounter.Current != null)
            {
                PlayerEncounter.Finish();
            }
            else if (party.CurrentSettlement != null)
            {
                LeaveSettlementAction.ApplyForParty(party);
            }

            party.SetPositionAfterMapChange(settlement.GatePosition);
            party.SetMoveModeHold();
            EncounterManager.StartSettlementEncounter(party, settlement);
            if (PlayerEncounter.EncounterSettlement != settlement
                || PlayerEncounter.LocationEncounter == null
                || party.CurrentSettlement != settlement)
            {
                throw new InvalidOperationException("Native settlement encounter did not initialize for " + settlement.Name + ".");
            }
            notes?.Add("player moved inside " + settlement.Name);
        }

        private static bool IsUsableTestVassal(Clan clan, Kingdom kingdom)
        {
            return clan != null
                && clan.Kingdom == kingdom
                && clan != kingdom?.RulingClan
                && clan != Clan.PlayerClan
                && !clan.IsEliminated
                && !clan.IsMinorFaction
                && !clan.IsBanditFaction
                && !clan.IsClanTypeMercenary
                && !clan.IsUnderMercenaryService
                && clan.Leader != null
                && clan.Leader.IsAlive
                && clan.Leader.IsActive
                && !clan.Leader.IsPrisoner;
        }

        private static bool IsInvalidTestVassal(Clan clan)
        {
            return clan == null
                || clan.IsEliminated
                || clan.IsMinorFaction
                || clan.IsBanditFaction
                || clan.IsClanTypeMercenary
                || clan.IsUnderMercenaryService;
        }

        private static Settlement FindZeonica()
        {
            return Settlement.All.FirstOrDefault(x => x != null
                    && x.IsTown
                    && string.Equals(x.StringId, "town_EW2", System.StringComparison.OrdinalIgnoreCase))
                ?? Settlement.All.FirstOrDefault(x => x != null
                    && x.IsTown
                    && x.Name != null
                    && x.Name.ToString().IndexOf("Zeonica", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static Clan FindClaimantClan(Kingdom kingdom)
        {
            return kingdom?.Clans
                .Where(x => x != null && x != kingdom.RulingClan && !x.IsEliminated && !x.IsClanTypeMercenary)
                .OrderByDescending(x => x.Fiefs.Count)
                .FirstOrDefault();
        }

        private static IEnumerable<Clan> FindSupporterClans(Kingdom kingdom, Clan claimant)
        {
            return kingdom?.Clans
                .Where(x => x != null && x != claimant && x != kingdom.RulingClan && !x.IsEliminated && !x.IsClanTypeMercenary)
                .OrderByDescending(x => x.Fiefs.Count)
                .Take(2)
                ?? Enumerable.Empty<Clan>();
        }

        private static string NameOrNone(Kingdom kingdom)
        {
            return kingdom == null ? "none" : kingdom.InformalName.ToString();
        }

        private static string NameOrNone(Hero hero)
        {
            return hero == null ? "none" : hero.Name.ToString();
        }

        private static string NameOrNone(Settlement settlement)
        {
            return settlement == null ? "none" : settlement.Name.ToString();
        }

        private static string NameOrNone(Clan clan)
        {
            return clan == null ? "none" : clan.Name.ToString();
        }

        private static void Show(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(0xFF66CCFF)));
        }
    }
}
