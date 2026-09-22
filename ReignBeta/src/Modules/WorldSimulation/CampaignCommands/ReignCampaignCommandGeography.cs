using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.World
{
    public static class ReignCampaignCommandGeography
    {
        private sealed class AnchorPoint
        {
            public Settlement Settlement;
            public float X;
            public float Y;
        }

        public static IReadOnlyList<Settlement> ResolveAnchors(MobileParty party, string region,
            Settlement explicitSettlement, int maximum = 5)
        {
            if (explicitSettlement != null && string.IsNullOrWhiteSpace(region))
                return new[] { explicitSettlement };
            if (party?.MapFaction == null)
                return explicitSettlement == null ? new Settlement[0] : new[] { explicitSettlement };

            List<AnchorPoint> footprint = Settlement.All
                .Where(x => x != null && !x.IsHideout && x.MapFaction == party.MapFaction
                    && (x.IsTown || x.IsCastle || x.IsVillage))
                .Select(x => new AnchorPoint { Settlement = x, X = x.GetPosition2D.x, Y = x.GetPosition2D.y })
                .ToList();
            if (explicitSettlement != null && footprint.All(x => x.Settlement != explicitSettlement))
                footprint.Add(new AnchorPoint { Settlement = explicitSettlement,
                    X = explicitSettlement.GetPosition2D.x, Y = explicitSettlement.GetPosition2D.y });
            if (footprint.Count == 0) return new Settlement[0];

            string selector = NormalizeRegion(region);
            List<AnchorPoint> selected;
            if (selector == "frontier")
            {
                List<Settlement> hostiles = Settlement.All.Where(x => x != null && x.IsFortification
                    && x.MapFaction != null && x.MapFaction != party.MapFaction
                    && x.MapFaction.IsAtWarWith(party.MapFaction)).ToList();
                selected = hostiles.Count == 0
                    ? footprint
                    : footprint.OrderBy(x => hostiles.Min(h => x.Settlement.GetPosition2D.DistanceSquared(h.GetPosition2D))).ToList();
            }
            else
            {
                float minX = footprint.Min(x => x.X), maxX = footprint.Max(x => x.X);
                float minY = footprint.Min(x => x.Y), maxY = footprint.Max(x => x.Y);
                float xCutLow = minX + (maxX - minX) * 0.40f;
                float xCutHigh = minX + (maxX - minX) * 0.60f;
                float yCutLow = minY + (maxY - minY) * 0.40f;
                float yCutHigh = minY + (maxY - minY) * 0.60f;
                switch (selector)
                {
                    case "north": selected = footprint.Where(x => x.Y >= yCutHigh).OrderByDescending(x => x.Y).ToList(); break;
                    case "south": selected = footprint.Where(x => x.Y <= yCutLow).OrderBy(x => x.Y).ToList(); break;
                    case "east": selected = footprint.Where(x => x.X >= xCutHigh).OrderByDescending(x => x.X).ToList(); break;
                    case "west": selected = footprint.Where(x => x.X <= xCutLow).OrderBy(x => x.X).ToList(); break;
                    case "central":
                        selected = footprint.Where(x => x.X >= xCutLow && x.X <= xCutHigh
                            && x.Y >= yCutLow && x.Y <= yCutHigh).ToList();
                        break;
                    default: selected = footprint; break;
                }
            }
            if (selected.Count == 0) selected = footprint;

            // Native AI always receives real settlement anchors. Ordering from the
            // party's current position keeps the first route practical; subsequent
            // hourly reviews may recalculate after ownership changes.
            List<Settlement> route = new List<Settlement>();
            var cursor = party.GetPosition2D;
            foreach (AnchorPoint point in selected.OrderBy(x => cursor.DistanceSquared(x.Settlement.GetPosition2D)))
            {
                if (route.Count >= Math.Max(1, maximum)) break;
                if (!route.Contains(point.Settlement)) route.Add(point.Settlement);
            }
            return route;
        }

        public static string NormalizeRegion(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant()
                .Replace('-', ' ').Replace('_', ' ');
            if (normalized.Contains("south")) return "south";
            if (normalized.Contains("north")) return "north";
            if (normalized.Contains("east")) return "east";
            if (normalized.Contains("west")) return "west";
            if (normalized.Contains("front") || normalized.Contains("border")) return "frontier";
            if (normalized.Contains("center") || normalized.Contains("central") || normalized.Contains("heart")) return "central";
            return string.IsNullOrWhiteSpace(normalized) ? string.Empty : "all";
        }
    }
}
