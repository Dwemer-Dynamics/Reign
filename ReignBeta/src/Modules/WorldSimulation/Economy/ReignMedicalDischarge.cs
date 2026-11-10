#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Economy
{
    // Native references are held ONLY while the event is active. Closed details live in bounded JSON receipts.
    internal static class ReignMedicalDischarge
    {
        private const string Owner = "com.bannerlordreign.medical-discharge.v1";
        private static bool installed;
        internal static string BindingError = "";
        private static readonly FieldInfo Applied = AccessTools.Field(typeof(MapEvent), "_mapEventResultsApplied");
        private sealed class Frame
        {
            internal MedicalEvent Event;
            internal bool Closed;
            internal List<PartyPlan> Parties = new List<PartyPlan>();
        }
        private sealed class PartyPlan
        {
            internal MobileParty Party;
            internal MedicalParty Row;
            internal Dictionary<string, int[]> Expected;
        }
        private static readonly Dictionary<MapEvent, Frame> Finalizing = new Dictionary<MapEvent, Frame>(ReignWarEconomyReferenceComparer<MapEvent>.Instance);
        private static ReignEconomyCampaignBehavior Host => ReignEconomyCampaignBehavior.Instance;
        private static string Key(MobileParty p) => ReignWarEconomyTransactions.GenerationKey(p);
        private static bool Military(MobileParty p) => p != null && p.IsActive && !p.IsMilitia
            && (p.IsLordParty || p.IsGarrison || p.PartyComponent is TaleWorlds.CampaignSystem.Party.PartyComponents.PatrolPartyComponent);
        private static Dictionary<string, int[]> Roster(MobileParty p) => p.MemberRoster.GetTroopRoster()
            .Where(x => !x.Character.IsHero && x.Number > 0).ToDictionary(x => x.Character.StringId, x => new[] { x.Number, x.WoundedNumber, x.Xp }, StringComparer.Ordinal);
        private static int Count(Dictionary<string, int[]> r, string id, int column) => r.TryGetValue(id, out var v) ? v[column] : 0;
        private static bool Kind(MapEvent e) => e.EventType == MapEvent.BattleTypes.FieldBattle
            || e.EventType == MapEvent.BattleTypes.Siege || e.EventType == MapEvent.BattleTypes.SiegeOutside
            || e.EventType == MapEvent.BattleTypes.SallyOut || e.EventType == MapEvent.BattleTypes.BlockadeBattle
            || e.EventType == MapEvent.BattleTypes.BlockadeSallyOutBattle;

        internal static void Install()
        {
            if (installed) return;
            var h = new Harmony(Owner);
            try
            {
                var add = AccessTools.Method(typeof(MapEventSide), "AddPartyInternal", new[] { typeof(PartyBase) });
                var finalize = AccessTools.Method(typeof(MapEvent), "FinalizeEventAux", Type.EmptyTypes);
                var rescue = AccessTools.Method(typeof(MapEvent), "LootDefeatedPartyPrisoners",
                    new[] { typeof(MBReadOnlyList<MapEventParty>), typeof(MBReadOnlyList<MapEventParty>) });
                var upgrades = AccessTools.Method(typeof(PartyUpgraderCampaignBehavior), "MapEventEnded", new[] { typeof(MapEvent) });
                if (add == null || finalize == null || rescue == null || upgrades == null
                    || Applied == null || Applied.FieldType != typeof(bool))
                    throw new MissingMethodException("Medical discharge native binding unavailable");
                h.Patch(add, postfix: new HarmonyMethod(typeof(ReignMedicalDischarge), nameof(Joined)));
                h.Patch(finalize, prefix: new HarmonyMethod(typeof(ReignMedicalDischarge), nameof(BeforeFinal)),
                    postfix: new HarmonyMethod(typeof(ReignMedicalDischarge), nameof(AfterFinal)),
                    finalizer: new HarmonyMethod(typeof(ReignMedicalDischarge), nameof(FinalFailed)));
                h.Patch(rescue, prefix: new HarmonyMethod(typeof(ReignMedicalDischarge), nameof(BeforeRescue)),
                    postfix: new HarmonyMethod(typeof(ReignMedicalDischarge), nameof(AfterRescue)));
                h.Patch(upgrades, prefix: new HarmonyMethod(typeof(ReignMedicalDischarge), nameof(BeforeUpgrades)),
                    postfix: new HarmonyMethod(typeof(ReignMedicalDischarge), nameof(AfterUpgrades)));
                installed = true;
            }
            catch (Exception ex) { h.UnpatchAll(Owner); BindingError = ex.Message; ReignLog.Exception("Medical binding", ex); }
        }
        private static void Joined(MapEventSide __instance, PartyBase party)
        { try { Enroll(__instance.MapEvent, party?.MobileParty); } catch (Exception ex) { Error(ex); } }
        internal static void Started(MapEvent e, PartyBase attacker, PartyBase defender)
        {
            try { foreach (var p in e.InvolvedParties.ToList()) Enroll(e, p.MobileParty); }
            catch (Exception ex) { Error(ex); }
        }
        private static void Error(Exception ex)
        {
            if (Host != null) { Host.EconomyState.UnsupportedEvents++; Host.EconomyState.LastError = "Medical discharge: " + ex.Message; }
            ReignLog.Exception("Medical discharge", ex);
        }
        private static MedicalEvent Find(MapEvent e)
        {
            if (Host == null) return null;
            int i = Host.MedicalEvents.IndexOf(e);
            if (i >= 0 && (i >= Host.MedicalIds.Count || !Host.EconomyState.MedicalActive.ContainsKey(Host.MedicalIds[i])))
                throw new InvalidOperationException("Medical reference has no matching journal");
            return i >= 0 && i < Host.MedicalIds.Count && Host.EconomyState.MedicalActive.TryGetValue(Host.MedicalIds[i], out var record) ? record : null;
        }
        private static void Enroll(MapEvent e, MobileParty p)
        {
            if (!installed || BindingError.Length != 0 || Host == null || e == null || e.IsFinalized || !Military(p)) return;
            var record = Find(e);
            if (record == null)
            {
                if (Host.MedicalEvents.Count >= 256) { Host.EconomyState.UnsupportedEvents++; return; }
                record = new MedicalEvent { Id = ++Host.EconomyState.MedicalSequence, Seed = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) };
                Host.EconomyState.MedicalActive.Add(record.Id, record);
                Host.MedicalEvents.Add(e); Host.MedicalIds.Add(record.Id);
            }
            if (record.Status != "enrolled" || record.Parties.Any(x => x.Key == Key(p))) return;
            if (record.Parties.Count >= 256 || record.Parties.Sum(x => x.Joining.Count) + p.MemberRoster.Count > 8192) { record.Status = "excluded"; return; }
            record.Parties.Add(new MedicalParty { Key = Key(p), Clan = p.ActualClan?.StringId,
                Kingdom = p.ActualClan?.Kingdom?.StringId, Medicine = p.EffectiveSurgeon?.GetSkillValue(DefaultSkills.Medicine) ?? 0,
                Joining = Roster(p) });
        }
        private static void BeforeRescue(MapEvent __instance, MBReadOnlyList<MapEventParty> __0,
            out List<PartyPlan> __state)
        {
            __state = null;
            try
            {
                if (!installed || BindingError.Length != 0 || !Kind(__instance)) return;
                var record = Find(__instance);
                if (record == null || record.Status != "enrolled") return;
                __state = new List<PartyPlan>();
                foreach (var winner in __0)
                {
                    var party = winner.Party.MobileParty;
                    // Native player rescue writes to the encounter's separate loot roster.
                    if (!Military(party) || party == MobileParty.MainParty) continue;
                    var row = record.Parties.FirstOrDefault(x => x.Key == Key(party));
                    if (row?.Status != "enrolled") continue;
                    __state.Add(new PartyPlan { Party = party, Row = row, Expected = Roster(party) });
                }
            }
            catch (Exception ex)
            {
                __state = null;
                Error(ex);
            }
        }

        private static void AfterRescue(List<PartyPlan> __state)
        {
            if (__state == null) return;
            foreach (var plan in __state)
            {
                try
                {
                    if (!ReignMedicalDischargePolicy.HealthyChanges(plan.Expected, Roster(plan.Party), true, out var gains))
                    {
                        plan.Row.Status = "excluded";
                        plan.Row.Reason = "native prisoner rescue changed wounds or removed members";
                        continue;
                    }
                    foreach (var gain in gains)
                    {
                        plan.Row.Rescued.TryGetValue(gain.Key, out int prior);
                        plan.Row.Rescued[gain.Key] = checked(prior + gain.Value);
                    }
                }
                catch (Exception ex)
                {
                    plan.Row.Status = "excluded";
                    plan.Row.Reason = "native prisoner rescue could not be reconciled";
                    Error(ex);
                }
            }
        }

        private static void BeforeUpgrades(MapEvent __0, out Frame __state)
        {
            __state = null;
            try
            {
                if (!Finalizing.TryGetValue(__0, out var frame)) return;
                __state = frame;
                foreach (var plan in frame.Parties)
                    if (plan.Expected != null && !ReignMedicalDischargePolicy.SameHeadcounts(plan.Expected, Roster(plan.Party)))
                        plan.Expected = null;
            }
            catch (Exception ex)
            {
                if (__state != null) __state.Event.Status = "uncertain";
                Error(ex);
            }
        }

        private static void AfterUpgrades(Frame __state)
        {
            if (__state == null || __state.Event.Status != "resolving") return;
            try
            {
                foreach (var plan in __state.Parties)
                {
                    if (plan.Expected == null) continue;
                    var upgraded = Roster(plan.Party);
                    plan.Expected = ReignMedicalDischargePolicy.HealthyChanges(plan.Expected, upgraded, false, out _)
                        ? upgraded : null;
                }
            }
            catch (Exception ex)
            {
                __state.Event.Status = "uncertain";
                Error(ex);
            }
        }

        private static void BeforeFinal(MapEvent __instance, out Frame __state)
        {
            __state = null;
            try
            {
                if (!installed || BindingError.Length != 0) return;
                var record = Find(__instance);
                if (record == null || record.Status != "enrolled" || __instance.IsFinalized) return;
                record.Status = "resolving"; // Reentrancy fence before any callbacks.
                var frame = new Frame { Event = record }; __state = frame;
                if (!Kind(__instance) || !(bool)Applied.GetValue(__instance)) { record.Status = "excluded"; return; }
                var battleParties = __instance.AttackerSide.Parties.Concat(__instance.DefenderSide.Parties).ToList();
                record.NativeWounded = battleParties.Sum(bp => bp.WoundedInBattle.TotalRegulars);
                record.WoundsObserved = true;
                foreach (var bp in battleParties)
                {
                    var p = bp.Party.MobileParty;
                    if (p == null) continue;
                    var row = record.Parties.FirstOrDefault(x => x.Key == Key(p));
                    if (row != null) row.ObservedWounded = bp.WoundedInBattle.TotalRegulars;
                    if (!Military(p)) continue;
                    if (row?.Status != "enrolled") continue; // No retroactive assessment of unenrolled/save-migrated battles.
                    row.Status = "excluded"; row.Reason = "native retention or roster attribution unproven";
                    var now = Roster(p);
                    var wounds = bp.WoundedInBattle.GetTroopRoster().Where(x => !x.Character.IsHero && x.Number > 0)
                        .ToDictionary(x => x.Character.StringId, x => x.Number, StringComparer.Ordinal);
                    var dead = bp.DiedInBattle.GetTroopRoster().Where(x => !x.Character.IsHero).ToDictionary(x => x.Character.StringId, x => x.Number);
                    var routed = bp.RoutedInBattle.GetTroopRoster().Where(x => !x.Character.IsHero).ToDictionary(x => x.Character.StringId, x => x.Number);
                    bool retained = ReignMedicalDischargePolicy.Retained(row.Joining, now, wounds, dead, routed, row.Rescued);
                    if (!retained || !SameOwner(p, row)) continue;
                    // No capture at all on a defeated non-retreat side, including unclaimed prisoners.
                    if (bp.Party.Side != __instance.WinningSide && !__instance.EndedByRetreat) continue;
                    ulong seed = record.Seed;
                    unchecked { foreach (char c in row.Key) seed = (seed ^ c) * 1099511628211UL; }
                    row.Selected = ReignMedicalDischargePolicy.Select(wounds, row.Medicine, seed);
                    row.Status = "planned";
                    frame.Parties.Add(new PartyPlan { Party = p, Row = row, Expected = now });
                }
                Finalizing.Add(__instance, frame);
            }
            catch (Exception ex) { if (__state != null) __state.Event.Status = "uncertain"; Error(ex); }
        }
        private static bool SameOwner(MobileParty p, MedicalParty row) => p.ActualClan?.StringId == row.Clan
            && p.ActualClan?.Kingdom?.StringId == row.Kingdom;
        private static void AfterFinal(MapEvent __instance, Frame __state)
        {
            if (__state == null) return;
            try
            {
                if (__state.Event.Status != "resolving") return;
                if (!__instance.IsFinalized) { __state.Event.Status = "uncertain"; return; }
                bool complete = true;
                foreach (var plan in __state.Parties)
                {
                    try { SettleParty(plan); }
                    catch (Exception ex)
                    {
                        complete = false;
                        var row = plan.Row;
                        row.Status = "uncertain"; row.Reason = ex.Message;
                        if (row.Before.Count > 0)
                        {
                            try
                            {
                                row.After = Roster(plan.Party);
                                row.Removed = row.Before.Values.Sum(x => x[0]) - row.After.Values.Sum(x => x[0]);
                            }
                            catch { row.Reason += "; live roster could not be reread; last observation retained"; }
                            row.HearthReturned = row.Credits.Values.Sum();
                            row.HearthUnallocated = row.Removed * ReignWarEconomyPolicy.HearthReturnPerDischarge - row.HearthReturned;
                        }
                        Error(ex);
                    }
                }
                __state.Event.Status = complete ? "completed" : "uncertain";
            }
            finally { Finish(__instance, __state); }
        }

        private static void SettleParty(PartyPlan plan)
        {
            var p = plan.Party;
            var row = plan.Row;
            if (!Military(p) || p.MapEvent != null || p.LeaderHero?.IsPrisoner == true || !SameOwner(p, row))
            { row.Status = "excluded"; row.Reason = "party captured, detached unsafely, inactive, or changed owner"; return; }
            var current = Roster(p);
            if (!ReignMedicalDischargePolicy.SameHeadcounts(plan.Expected, current))
            { row.Status = "excluded"; row.Reason = "unexplained headcount or wound change during native finalization"; return; }
            if (row.Selected.Any(selected => !current.TryGetValue(selected.Key, out var entry)
                || selected.Value <= 0 || selected.Value > entry[1]))
            { row.Status = "excluded"; row.Reason = "party preflight failed before its first removal"; return; }

            row.EligibleWounded = row.ObservedWounded;
            if (row.Selected.Count == 0)
            { row.Status = "completed"; row.Reason = "no medical discharges selected"; return; }

            row.Before = current;
            row.Status = "applying";
            var expectedXp = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var selected in row.Selected)
            {
                var el = p.MemberRoster.GetTroopRoster().First(x => !x.Character.IsHero && x.Character.StringId == selected.Key);
                if (selected.Value > el.WoundedNumber) throw new InvalidOperationException("Medical wound budget mismatch");
                int xp = (int)((long)Math.Max(0, el.Xp) * selected.Value / el.Number);
                int remaining = el.Number - selected.Value;
                int remainingXp = el.Xp - xp;
                if (xp != 0 && remaining > 0)
                {
                    // Native OnXpChanged clamps against the remaining stack's highest upgrade cost.
                    int maximum = 0;
                    for (int i = 0; i < el.Character.UpgradeTargets.Length; i++)
                        maximum = Math.Max(maximum, el.Character.GetUpgradeXpCost(p.Party, i));
                    remainingXp = Math.Max(0, Math.Min(remainingXp, remaining * maximum));
                }
                expectedXp.Add(selected.Key, remaining == 0 ? 0 : remainingXp);
                p.MemberRoster.AddToCounts(el.Character, -selected.Value, false, -selected.Value, -xp);
            }
            var after = row.After = Roster(p);
            row.Removed = current.Values.Sum(x => x[0]) - after.Values.Sum(x => x[0]);
            if (after.Keys.Except(current.Keys).Any()) throw new InvalidOperationException("Medical roster gained an unexpected stack");
            foreach (var before in current)
            {
                int n = row.Selected.TryGetValue(before.Key, out var value) ? value : 0;
                int xp = expectedXp.TryGetValue(before.Key, out var expected) ? expected : before.Value[2];
                if (Count(after, before.Key, 0) != before.Value[0] - n || Count(after, before.Key, 1) != before.Value[1] - n
                    || Count(after, before.Key, 2) != xp)
                    throw new InvalidOperationException("Medical roster/XP conservation failed");
            }
            bool credit = p.ActualClan?.Kingdom != null && !p.ActualClan.Kingdom.IsEliminated && !p.ActualClan.IsUnderMercenaryService;
            row.HearthReturned = ReignMilitaryReadiness.ReturnDischarged(credit ? p.ActualClan.Kingdom : null, row.Removed, row.Credits);
            row.HearthUnallocated = row.Removed * ReignWarEconomyPolicy.HearthReturnPerDischarge - row.HearthReturned;
            row.Status = "completed"; row.Reason = "new free battle wounded medically discharged";
        }

        private static Exception FinalFailed(Exception __exception, MapEvent __instance, Frame __state)
        {
            if (__exception != null && __state != null && !__state.Closed)
            {
                __state.Event.Status = "uncertain";
                Finish(__instance, __state);
            }
            return __exception; // Never swallow native exceptions or change native capture behavior.
        }

        private static void Finish(MapEvent e, Frame frame)
        {
            if (frame.Closed) return;
            frame.Closed = true;
            Finalizing.Remove(e);
            if (frame.Event.Status == "resolving") frame.Event.Status = "uncertain";
            try { Close(e, frame.Event); }
            catch (Exception ex)
            {
                BindingError = "Medical receipt closure quarantined: " + ex.Message;
                Error(ex);
            }
        }

        private static void Close(MapEvent e, MedicalEvent record)
        {
            if (Host == null || !Host.EconomyState.MedicalActive.ContainsKey(record.Id)) return;
            if (record.WoundsObserved)
            {
                Host.EconomyState.MedicalObservedBattles++;
                Host.EconomyState.MedicalNativeWounded += record.NativeWounded;
            }
            foreach (var row in record.Parties)
            {
                if (row.Status == "enrolled" || row.Status == "planned")
                { row.Status = "excluded"; row.Reason = "event ended without proven safe settlement"; }
                Host.EconomyState.MedicalObservedWounded += row.ObservedWounded;
                Host.EconomyState.MedicalEligibleWounded += row.EligibleWounded;
                if (row.EligibleWounded > 0) Host.EconomyState.MedicalSelectedWounded += row.Selected.Values.Sum();
                Host.EconomyState.MedicalDischarged += row.Removed;
                Host.EconomyState.MedicalHearthReturned += row.Credits.Values.Sum();
                if (row.Status == "excluded") Host.EconomyState.MedicalExcluded++;
            }
            Host.EconomyState.MedicalRecent.Add(record);
            if (Host.EconomyState.MedicalRecent.Count > 128) Host.EconomyState.MedicalRecent.RemoveAt(0);
            int i = Host.MedicalEvents.IndexOf(e);
            if (i >= 0) { Host.MedicalEvents.RemoveAt(i); Host.MedicalIds.RemoveAt(i); }
            Host.EconomyState.MedicalActive.Remove(record.Id);
        }
        internal static void Prune()
        {
            if (BindingError.Length != 0) return;
            try { PruneCore(); }
            catch (Exception ex)
            {
                BindingError = "Medical save mapping quarantined: " + ex.Message;
                Error(ex); // Preserve the raw mapping/journal and allow native saving to continue.
            }
        }
        private static void PruneCore()
        {
            if (Host == null) return;
            if (Host.MedicalEvents.Count != Host.MedicalIds.Count) throw new InvalidOperationException("Medical native reference mapping mismatch");
            if (Host.MedicalIds.Distinct().Count() != Host.MedicalIds.Count
                || Host.MedicalEvents.Distinct().Count() != Host.MedicalEvents.Count
                || Host.EconomyState.MedicalActive.Keys.Except(Host.MedicalIds).Any())
                throw new InvalidOperationException("Medical save mapping is not one-to-one; refusing to discard evidence");
            for (int i = Host.MedicalEvents.Count - 1; i >= 0; i--)
                if ((Host.EconomyState.MedicalActive.TryGetValue(Host.MedicalIds[i], out var active)
                        && (active.Status == "resolving" || active.Status == "uncertain") && !Finalizing.Values.Any(frame => ReferenceEquals(frame.Event, active)))
                    || Host.MedicalEvents[i] == null || Host.MedicalEvents[i].IsFinalized
                    || (TaleWorlds.CampaignSystem.Campaign.Current != null && !TaleWorlds.CampaignSystem.Campaign.Current.MapEventManager.MapEvents.Contains(Host.MedicalEvents[i])))
                {
                    if (Host.EconomyState.MedicalActive.TryGetValue(Host.MedicalIds[i], out var row))
                    { row.Status = "uncertain"; Close(Host.MedicalEvents[i], row); }
                    else { Host.MedicalEvents.RemoveAt(i); Host.MedicalIds.RemoveAt(i); }
                }
        }
    }
}
