using System;
using System.Collections.Generic;
using System.Linq;

namespace ReignBeta.Government
{
    // Finish a finite cache sweep before restarting its refresh timer. Large
    // populations must not make early batches expire while later ones still wait.
    internal sealed class GovernmentPoliticalRefreshCycle
    {
        private DateTime _startedUtc;
        private DateTime _nextUtc;
        private int? _interval;
        private bool _active;

        internal void Observe(DateTime utc, int interval)
        {
            if (_interval == interval && (_active || utc < _nextUtc)) return;
            _interval = interval;
            _startedUtc = utc;
            _active = true;
        }

        internal bool NeedsRefresh(DateTime? lastSuccessfulSync, bool required = false) =>
            required || !lastSuccessfulSync.HasValue || _active && lastSuccessfulSync.Value < _startedUtc;

        internal bool CompleteIfAcknowledged(IEnumerable<DateTime?> livingAcknowledgements, DateTime utc)
        {
            if (!_active || livingAcknowledgements.Any(last => !last.HasValue || last.Value < _startedUtc)) return false;
            _active = false;
            _nextUtc = utc.AddMinutes(5);
            return true;
        }
    }
}
