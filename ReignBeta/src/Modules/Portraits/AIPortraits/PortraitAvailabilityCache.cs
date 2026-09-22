using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace AIPortraits;

// Cache misses as well as hits. Rendering must not repeatedly enumerate the
// shared library or wait for the WSL share when a character has no portrait.
internal sealed class PortraitAvailabilityCache
{
	private sealed class Entry
	{
		public readonly Func<bool> Probe;
		public volatile bool Exists;
		public long RefreshAfter;
		public int Refreshing;

		public Entry(Func<bool> probe, bool exists, long refreshAfter)
		{
			Probe = probe;
			Exists = exists;
			RefreshAfter = refreshAfter;
		}
	}

	private readonly ConcurrentDictionary<string, Lazy<Entry>> _entries = new ConcurrentDictionary<string, Lazy<Entry>>(StringComparer.Ordinal);
	private readonly Func<long> _clock;
	private readonly long _freshness;
	private readonly Action<Action> _schedule;
	private readonly Action _changed;
	private readonly Action<Exception> _error;

	public PortraitAvailabilityCache(Func<long> clock, long freshness, Action<Action> schedule, Action changed, Action<Exception> error)
	{
		_clock = clock;
		_freshness = freshness;
		_schedule = schedule;
		_changed = changed;
		_error = error;
	}

	public bool TryGet(string key, out bool exists)
	{
		if (_entries.TryGetValue(key, out var entry))
		{
			exists = Read(key, entry);
			return true;
		}
		exists = false;
		return false;
	}

	public bool GetOrAdd(string key, Func<bool> probe)
	{
		Lazy<Entry> entry = _entries.GetOrAdd(key, _ => new Lazy<Entry>(
			() => new Entry(probe, probe(), _clock() + _freshness), LazyThreadSafetyMode.ExecutionAndPublication));
		return Read(key, entry);
	}

	public void Invalidate(string key) => _entries.TryRemove(key, out _);
	public void Clear() => _entries.Clear();

	private bool Read(string key, Lazy<Entry> lazy)
	{
		Entry entry;
		try { entry = lazy.Value; }
		catch
		{
			// Do not retain a failed first probe, or remove a newer replacement.
			((ICollection<KeyValuePair<string, Lazy<Entry>>>)_entries).Remove(new KeyValuePair<string, Lazy<Entry>>(key, lazy));
			throw;
		}
		bool exists = entry.Exists;
		if (_clock() >= Volatile.Read(ref entry.RefreshAfter)
			&& Interlocked.CompareExchange(ref entry.Refreshing, 1, 0) == 0)
		{
			try { _schedule(() => Refresh(entry)); }
			catch (Exception ex)
			{
				Volatile.Write(ref entry.RefreshAfter, _clock() + _freshness);
				Volatile.Write(ref entry.Refreshing, 0);
				_error?.Invoke(ex);
			}
		}
		return exists;
	}

	private void Refresh(Entry entry)
	{
		try
		{
			bool exists = entry.Probe();
			bool changed = entry.Exists != exists;
			entry.Exists = exists;
			if (changed) _changed?.Invoke();
		}
		catch (Exception ex) { _error?.Invoke(ex); }
		finally
		{
			Volatile.Write(ref entry.RefreshAfter, _clock() + _freshness);
			Volatile.Write(ref entry.Refreshing, 0);
		}
	}
}
