#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ReignBeta.Campaign
{
    // Native-independent outbreak planning. Ownership is the final allegiance of
    // each holding, captured before any clan-change or war-declaration callback.
    internal static class ReignRebellionResidentEscape
    {
        internal const int LoyalistEscapePercent = 25;

        internal sealed class Resident
        {
            public string HeroId = string.Empty;
            public string ClanId = string.Empty;
            public string SettlementId = string.Empty;
            public string HomeSettlementId = string.Empty;
            public bool IsRebel;
            public bool IsParticipant;
            public bool IsNoble;
            public bool IsAlive;
            public bool IsPrisoner;
            public bool HasParty;
            public bool IsPlayer;
        }

        internal sealed class Stronghold
        {
            public string SettlementId = string.Empty;
            public string OwnerClanId = string.Empty;
            public bool IsRebel;
            public bool IsSafe;
        }

        internal sealed class Escape
        {
            public string HeroId = string.Empty;
            public string OriginSettlementId = string.Empty;
            // Empty means there is no safe holding on this side: go into hiding.
            public string DestinationSettlementId = string.Empty;
            public bool IsRebel;
            public int Roll;
            public bool Escapes;
        }

        internal static List<Escape> Plan(string movementId, IEnumerable<Resident> residents,
            IEnumerable<Stronghold> strongholds, int loyalistEscapePercent = LoyalistEscapePercent)
        {
            if (string.IsNullOrWhiteSpace(movementId)) throw new ArgumentException("A movement identity is required.", nameof(movementId));
            if (residents == null) throw new ArgumentNullException(nameof(residents));
            if (strongholds == null) throw new ArgumentNullException(nameof(strongholds));
            if (loyalistEscapePercent < 0 || loyalistEscapePercent > 100)
                throw new ArgumentOutOfRangeException(nameof(loyalistEscapePercent));
            List<Stronghold> holdings = strongholds.Where(x => x != null
                && !string.IsNullOrWhiteSpace(x.SettlementId)).ToList();
            var origins = holdings.ToDictionary(x => x.SettlementId, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<Escape>();
            foreach (Resident resident in residents.Where(x => x != null)
                .OrderBy(x => x.HeroId, StringComparer.Ordinal))
            {
                if (!resident.IsParticipant || !resident.IsNoble || !resident.IsAlive
                    || resident.IsPrisoner || resident.HasParty || resident.IsPlayer
                    || string.IsNullOrWhiteSpace(resident.HeroId) || string.IsNullOrWhiteSpace(resident.ClanId)
                    || string.IsNullOrWhiteSpace(resident.SettlementId)
                    || !origins.TryGetValue(resident.SettlementId, out var origin)
                    || resident.IsRebel == origin.IsRebel || !seen.Add(resident.HeroId)) continue;
                int roll = resident.IsRebel ? 0 : LoyalistRoll(movementId, resident.HeroId);
                Stronghold? destination = holdings.Where(x => x.IsSafe && x.IsRebel == resident.IsRebel)
                    .OrderByDescending(x => string.Equals(x.SettlementId, resident.HomeSettlementId, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => string.Equals(x.OwnerClanId, resident.ClanId, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(x => x.SettlementId, StringComparer.Ordinal).FirstOrDefault();
                result.Add(new Escape
                {
                    HeroId = resident.HeroId,
                    OriginSettlementId = resident.SettlementId,
                    DestinationSettlementId = destination?.SettlementId ?? string.Empty,
                    IsRebel = resident.IsRebel,
                    Roll = roll,
                    Escapes = resident.IsRebel || roll <= loyalistEscapePercent
                });
            }
            return result;
        }

        internal static int LoyalistRoll(string movementId, string heroId)
        {
            // Stable across load/retry, input ordering, processes and runtimes;
            // no extra save fields and no chance to reroll a failed escape.
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(movementId + "\n" + heroId));
                uint value = (uint)bytes[0] | ((uint)bytes[1] << 8)
                    | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 24);
                return (int)(value % 100) + 1;
            }
        }
    }
}
