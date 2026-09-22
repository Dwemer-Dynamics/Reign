using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private const string RoyalCouncilRelocationConfirmation = "exercise Reign Royal Council relocation on disposable save";

        internal JObject RunRoyalCouncilTestProfile(string runId, string phase, JObject options, string gameInstanceId)
        {
            if (!TryRequireRoyalCouncilTestSave(options, out string expectedSave, out string activeSave, out string saveError))
                return RoyalCouncilTestFailure(runId, phase, saveError, expectedSave, activeSave);
            switch ((phase ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "save_prepare": return PrepareRoyalCouncilReload(runId, gameInstanceId, expectedSave);
                case "save_verify": return VerifyRoyalCouncilReload(runId, gameInstanceId, expectedSave);
                case "capital_relocation": return ExerciseRoyalCouncilRelocation(runId, options, expectedSave);
                case "cleanup_marker": return CleanupRoyalCouncilMarker(runId, expectedSave);
                default: return RoyalCouncilTestFailure(runId, phase, "Unsupported Royal Council native test phase.", expectedSave, activeSave);
            }
        }

        private JObject PrepareRoyalCouncilReload(string runId, string gameInstanceId, string saveName)
        {
            JObject snapshot = RoyalCouncilNativeSnapshot();
            string fingerprint = RoyalCouncilFingerprint(snapshot);
            JObject marker = new JObject { ["runId"] = runId, ["gameInstanceId"] = gameInstanceId ?? string.Empty,
                ["saveName"] = saveName, ["fingerprint"] = fingerprint, ["snapshot"] = snapshot };
            _royalCouncilTestMarkers.RemoveAll(value => RoyalCouncilMarkerRunId(value) == runId);
            _royalCouncilTestMarkers.Add(marker.ToString(Newtonsoft.Json.Formatting.None));
            while (_royalCouncilTestMarkers.Count > 12) _royalCouncilTestMarkers.RemoveAt(0);
            return new JObject { ["ok"] = true, ["runId"] = runId, ["phase"] = "save_prepare", ["fingerprint"] = fingerprint,
                ["gameInstanceId"] = gameInstanceId ?? string.Empty, ["snapshot"] = snapshot, ["requiresGuardedCheckpointRestart"] = true };
        }

        private JObject VerifyRoyalCouncilReload(string runId, string gameInstanceId, string saveName)
        {
            JObject marker = FindRoyalCouncilMarker(runId);
            if (marker == null) return RoyalCouncilTestFailure(runId, "save_verify", "The save-backed Royal Council marker is missing.", saveName, saveName);
            string current = RoyalCouncilFingerprint(RoyalCouncilNativeSnapshot());
            bool differentInstance = !string.IsNullOrWhiteSpace(gameInstanceId) && !string.Equals(marker.Value<string>("gameInstanceId"), gameInstanceId, StringComparison.OrdinalIgnoreCase);
            bool same = string.Equals(marker.Value<string>("fingerprint"), current, StringComparison.OrdinalIgnoreCase);
            return new JObject { ["ok"] = differentInstance && same, ["runId"] = runId, ["phase"] = "save_verify",
                ["differentGameInstance"] = differentInstance, ["fingerprintMatched"] = same,
                ["preparedFingerprint"] = marker.Value<string>("fingerprint") ?? string.Empty, ["currentFingerprint"] = current };
        }

        private JObject ExerciseRoyalCouncilRelocation(string runId, JObject options, string saveName)
        {
            if (!string.Equals(options?.Value<string>("confirmation"), RoyalCouncilRelocationConfirmation, StringComparison.Ordinal))
                return RoyalCouncilTestFailure(runId, "capital_relocation", "The exact disposable-save relocation confirmation is required.", saveName, saveName);
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            Settlement original = CurrentCapital;
            if (ours == null || original == null)
                return RoyalCouncilTestFailure(runId, "capital_relocation", "A current player-kingdom capital is required.", saveName, saveName);
            List<Settlement> towns = OwnedSafeTowns(ours);
            Settlement alternate = towns.FirstOrDefault(town => town != original);
            Hero economic = RoyalCouncilOfficeHero(ReignCourtOffice.EconomicAdvisor);
            Hero foreign = RoyalCouncilOfficeHero(ReignCourtOffice.ForeignAdvisor);
            if (alternate == null || economic == null || foreign == null)
                return RoyalCouncilTestFailure(runId, "capital_relocation", "Two safe owned towns and staffed Economic and Foreign Advisor offices are required.", saveName, saveName);
            DesignateCapital(alternate);
            bool moved = RoyalCouncilResidentAt(economic, alternate) && RoyalCouncilResidentAt(foreign, alternate);
            KingdomCapitalDesignation designation = CurrentCapitalDesignation();
            HandleCapitalLoss(designation, false);
            bool sheltered = CurrentCapital == null && economic.PartyBelongedTo == null && foreign.PartyBelongedTo == null
                && economic.CurrentSettlement != null && foreign.CurrentSettlement != null
                && economic.CurrentSettlement.OwnerClan?.Kingdom == ours && foreign.CurrentSettlement.OwnerClan?.Kingdom == ours;
            DesignateCapital(original);
            bool recovered = RoyalCouncilResidentAt(economic, original) && RoyalCouncilResidentAt(foreign, original);
            return new JObject { ["ok"] = moved && sheltered && recovered, ["runId"] = runId, ["phase"] = "capital_relocation",
                ["movedWithCapital"] = moved, ["shelteredAfterLoss"] = sheltered, ["recoveredAtReplacement"] = recovered,
                ["originalCapitalId"] = original.StringId, ["alternateCapitalId"] = alternate.StringId, ["requiresBaselineRollback"] = true };
        }

        private JObject CleanupRoyalCouncilMarker(string runId, string saveName)
        {
            int removed = _royalCouncilTestMarkers.RemoveAll(value => RoyalCouncilMarkerRunId(value) == runId);
            return new JObject { ["ok"] = true, ["runId"] = runId, ["phase"] = "cleanup_marker", ["removed"] = removed, ["saveName"] = saveName };
        }

        private JObject RoyalCouncilNativeSnapshot()
        {
            var offices = new JArray(_offices.Where(office => office?.IsActive == true).OrderBy(office => (int)office.Office)
                .Select(office => new JObject { ["officeValue"] = (int)office.Office, ["office"] = office.Office.ToString(), ["heroId"] = office.HeroStringId }));
            return new JObject { ["capitalId"] = CurrentCapital?.StringId ?? string.Empty,
                ["warCouncilorId"] = ReignBeta.Court.WarCouncil.ReignWarCouncilCampaignBehavior.Instance?.SelectedCouncilorHeroId ?? string.Empty,
                ["offices"] = offices };
        }

        private Hero RoyalCouncilOfficeHero(ReignCourtOffice office)
        {
            string id = _offices.FirstOrDefault(value => value?.IsActive == true && value.Office == office)?.HeroStringId;
            return string.IsNullOrWhiteSpace(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(hero => string.Equals(hero.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static bool RoyalCouncilResidentAt(Hero hero, Settlement settlement) => hero != null && settlement != null && hero.PartyBelongedTo == null && hero.CurrentSettlement == settlement;
        private static string RoyalCouncilFingerprint(JObject value)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value.ToString(Newtonsoft.Json.Formatting.None)))).Replace("-", string.Empty).ToLowerInvariant();
        }
        private JObject FindRoyalCouncilMarker(string runId) => (_royalCouncilTestMarkers ?? new List<string>()).Select(ParseRoyalCouncilMarker).LastOrDefault(value => value?.Value<string>("runId") == runId);
        private static string RoyalCouncilMarkerRunId(string value) => ParseRoyalCouncilMarker(value)?.Value<string>("runId") ?? string.Empty;
        private static JObject ParseRoyalCouncilMarker(string value) { try { return string.IsNullOrWhiteSpace(value) ? null : JObject.Parse(value); } catch { return null; } }
        private static bool TryRequireRoyalCouncilTestSave(JObject options, out string expected, out string active, out string error)
        {
            expected = (options?.Value<string>("expectedSaveName") ?? string.Empty).Trim(); active = ReignServerClient.ActiveNativeSaveName().Trim(); error = string.Empty;
            if (expected.Length == 0) error = "The exact MCP-authorized campaign-test Current save name is required.";
            else if (active.Length == 0) error = "Bannerlord did not expose an active loaded save identity.";
            else if (!string.Equals(expected, active, StringComparison.OrdinalIgnoreCase)) error = "The active Bannerlord save does not match the exact MCP-authorized campaign-test Current.";
            return error.Length == 0;
        }
        private static JObject RoyalCouncilTestFailure(string runId, string phase, string error, string expected, string active) => new JObject
        { ["ok"] = false, ["runId"] = runId ?? string.Empty, ["profile"] = "royal_council", ["phase"] = phase ?? string.Empty,
            ["error"] = error ?? string.Empty, ["expectedSaveName"] = expected ?? string.Empty, ["activeSaveName"] = active ?? string.Empty };
    }
}
