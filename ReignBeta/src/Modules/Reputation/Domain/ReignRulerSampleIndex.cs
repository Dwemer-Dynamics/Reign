#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Campaign
{
    // Campaign-owned sample rows are immutable after append. The owner invalidates
    // on pruning/loading; every query also checks array identity, day, count and tail.
    // This is a disposable per-day lookup, never serialized campaign authority.
    internal sealed class ReignRulerSampleIndex
    {
        private readonly Func<JObject, string?> _readId;
        private readonly Func<JObject, double> _readDay;
        private readonly Dictionary<string, List<Sample>> _rows =
            new Dictionary<string, List<Sample>>(StringComparer.Ordinal);
        private readonly List<Sample> _nullRows = new List<Sample>();
        private JArray? _source;
        private JToken? _last;
        private int _count = -1;
        private double _day;
        private bool _unsafe;

        private sealed class Sample
        {
            internal readonly JObject Row;
            internal readonly double Day;
            internal Sample(JObject row, double day) { Row = row; Day = day; }
        }

        internal ReignRulerSampleIndex(Func<JObject, string?> readId,
            Func<JObject, double> readDay)
        {
            _readId = readId ?? throw new ArgumentNullException(nameof(readId));
            _readDay = readDay ?? throw new ArgumentNullException(nameof(readDay));
        }

        internal void Invalidate()
        {
            _source = null;
            _last = null;
            _count = -1;
            _unsafe = false;
            _rows.Clear();
            _nullRows.Clear();
        }

        private void Ensure(JArray source, double day)
        {
            if (ReferenceEquals(_source, source) && _day == day
                && _count == source.Count && ReferenceEquals(_last, source.Last)) return;
            Invalidate();
            _source = source;
            _day = day;
            foreach (JObject row in source.OfType<JObject>())
            {
                if (!TryAppend(row)) break;
            }
            _count = source.Count;
            _last = source.Last;
        }

        private bool TryAppend(JObject row)
        {
            try
            {
                string? id = _readId(row);
                var sample = new Sample(row, _readDay(row));
                if (id == null) _nullRows.Add(sample);
                else
                {
                    if (!_rows.TryGetValue(id, out List<Sample>? rows))
                        _rows.Add(id, rows = new List<Sample>());
                    rows.Add(sample);
                }
                return true;
            }
            catch
            {
                // An unrelated malformed row must not make an earlier legacy
                // short-circuit query throw. Re-run that exact query instead.
                _unsafe = true;
                return false;
            }
        }

        private IEnumerable<Sample> Rows(string? id) => id == null ? _nullRows
            : _rows.TryGetValue(id, out List<Sample>? rows) ? rows : Enumerable.Empty<Sample>();

        internal bool Contains(JArray source, string? id, double day)
        {
            Ensure(source, day);
            return _unsafe
                ? source.OfType<JObject>().Any(row => _readId(row) == id && _readDay(row) == day)
                : Rows(id).Any(row => row.Day == day);
        }

        internal JObject? Previous(JArray source, string? id, double day)
        {
            Ensure(source, day);
            if (_unsafe)
                return source.OfType<JObject>().Where(row => _readId(row) == id && _readDay(row) < day)
                    .OrderByDescending(_readDay).FirstOrDefault();
            Sample? best = null;
            foreach (Sample row in Rows(id))
                if (row.Day < day && (best == null || row.Day > best.Day)) best = row;
            return best?.Row;
        }

        internal void Add(JArray source, JObject row, double day)
        {
            Ensure(source, day);
            source.Add(row);
            // JArray clones an already-parented token. Index the stored row.
            if (!_unsafe && source.Last is JObject actual) TryAppend(actual);
            _count = source.Count;
            _last = source.Last;
        }
    }
}
