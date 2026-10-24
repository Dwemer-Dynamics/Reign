using System;
using System.Collections.Generic;
using System.Linq;

namespace ReignBeta.Campaign
{
    // Diagnostic observations only. Never used to decide movement or fulfillment.
    internal sealed class NpcMeetingMovementEvidence
    {
        internal const int Capacity = 64;
        internal const int HeroLimit = 16;
        private readonly Queue<Row> _rows = new Queue<Row>();
        private long _sequence;

        internal sealed class Row
        {
            public long Sequence { get; }
            public string HeroId { get; }
            public double WorldDay { get; }
            public string Operation { get; }
            public string HeroState { get; }
            public string SettlementId { get; }
            public string PartyId { get; }
            public string TargetId { get; }
            public string Detail { get; }
            public bool Held { get; }
            public bool Allowed { get; }
            public IReadOnlyList<string> Callers { get; }

            internal Row(long sequence, string hero, double day, string operation, string state,
                string settlement, string party, string target, string detail, bool held, bool allowed,
                IEnumerable<string> callers)
            {
                Sequence = sequence; HeroId = Bound(hero); WorldDay = day; Operation = Bound(operation);
                HeroState = Bound(state); SettlementId = Bound(settlement); PartyId = Bound(party);
                TargetId = Bound(target); Detail = Bound(detail); Held = held; Allowed = allowed;
                Callers = Array.AsReadOnly((callers ?? Enumerable.Empty<string>()).Take(12).Select(x => Bound(x, 256)).ToArray());
            }
        }

        internal void Add(string hero, double day, string operation, string state, string settlement,
            string party, string target, string detail, bool held, bool allowed, IEnumerable<string> callers)
        {
            if (string.IsNullOrWhiteSpace(hero) || hero.Length > 128 || double.IsNaN(day) || double.IsInfinity(day) || day < 0) return;
            _rows.Enqueue(new Row(++_sequence, hero, day, operation, state, settlement, party, target, detail, held, allowed, callers));
            while (_rows.Count > Capacity) _rows.Dequeue();
        }

        internal IReadOnlyList<Row> ForHero(string hero) => Array.AsReadOnly(_rows.Where(x => x.HeroId == hero)
            .Reverse().Take(HeroLimit).Reverse().ToArray());
        internal int CountForHero(string hero) => _rows.Count(x => x.HeroId == hero);
        internal void Clear() { _rows.Clear(); _sequence = 0; }
        private static string Bound(string value, int maximum = 128) => new string((value ?? "").Take(maximum)
            .Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }
}
