using System;
using System.Buffers;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using ReignBeta.Shared;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        private sealed class PersonalGossipPump
        {
            internal TaleWorlds.CampaignSystem.Campaign Campaign;
            internal string CampaignId, Timeline, Generation;
            internal int InFlight;
            internal DateTime NextPoll, NextScopeCheck;
            internal readonly PersonalGossipWakeState Wake = new PersonalGossipWakeState();
            internal double NextDue = double.PositiveInfinity;
            internal JObject Readiness;
            internal string LastError = "";
        }
        private static PersonalGossipPump _personalGossipPump;
        private static readonly object GossipTimingGate = new object();
        private static readonly Queue<double> GossipCaptureTimes = new Queue<double>();
        private static long _gossipCaptureCount, _gossipCaptureGen0Overlap, _gossipCaptureGen1Overlap, _gossipCaptureGen2Overlap;
        private static double _gossipCaptureMax, _gossipCaptureLast;
        private static readonly Queue<double> GossipEncodingTimes = new Queue<double>();
        private static long _gossipEncodingCount, _gossipEncodedCharactersMax, _gossipEncodingReuseCount;
        private static double _gossipEncodingLast, _gossipEncodingMax;
        internal static Task<JObject> PersonalGossipTestRequestAsync(JObject payload, PersonalGossipPresenceSnapshot snapshot) =>
            PostJsonAsync("/gossip/test", payload, () => ReignMainThread.InvokeAsync(() => PersonalGossipSnapshotCurrent(snapshot)));


        // Native access stays in one campaign-thread callback. The returned value
        // owns only immutable strings, numbers and booleans, never Hero/Party references.
        internal sealed class PersonalGossipPresenceSnapshot
        {
            // Worker-only, single-entry value cache. Every native field is still read
            // coherently on every capture. Headers and clock acknowledgements are never cached.
            private static readonly object ContentCacheGate = new object();
            private static readonly Stack<StringBuilder> WriterPool = new Stack<StringBuilder>();
            private const int MaximumRetainedCharacters = 4 * 1024 * 1024;
            private static Person[] _cachedRows;
            private static int _cachedRowCount;
            private static string _cachedCampaign, _cachedTimeline, _cachedGeneration, _cachedJson;

            private readonly struct Person
            {
                internal readonly string Id, Name, SettlementId, PartyId, KingdomId, FatherId, MotherId, SpouseId;
                internal readonly bool Alive, Prisoner, Available, PositionKnown;
                internal readonly double X, Y;
                internal Person(Hero hero)
                {
                    MobileParty party = hero.PartyBelongedTo;
                    Settlement settlement = hero.CurrentSettlement ?? party?.CurrentSettlement;
                    if (hero == Hero.MainHero)
                    {
                        party = party ?? MobileParty.MainParty;
                        settlement = settlement ?? party?.CurrentSettlement ?? Settlement.CurrentSettlement;
                    }
                    var position = party?.GetPosition2D ?? settlement?.GetPosition2D;
                    Id = hero.StringId;
                    Name = hero.Name?.ToString() ?? hero.StringId;
                    Alive = hero.IsAlive;
                    Prisoner = hero.IsPrisoner;
                    Available = hero.IsAlive && hero.IsActive && !hero.IsPrisoner;
                    SettlementId = settlement?.StringId ?? string.Empty;
                    PartyId = party?.StringId ?? string.Empty;
                    KingdomId = hero.Clan?.Kingdom?.StringId ?? string.Empty;
                    FatherId = hero.Father?.StringId ?? string.Empty;
                    MotherId = hero.Mother?.StringId ?? string.Empty;
                    SpouseId = hero.Spouse?.StringId ?? string.Empty;
                    PositionKnown = position.HasValue;
                    X = position.HasValue ? (double)position.Value.X : 0d;
                    Y = position.HasValue ? (double)position.Value.Y : 0d;
                }
                internal Person(string name)
                {
                    Id = "fixture"; Name = name;
                    SettlementId = PartyId = KingdomId = FatherId = MotherId = SpouseId = "";
                    Alive = Available = PositionKnown = true; Prisoner = false; X = 1.25; Y = -0.5;
                }
                internal bool Same(in Person other) => Id == other.Id && Name == other.Name
                    && SettlementId == other.SettlementId && PartyId == other.PartyId && KingdomId == other.KingdomId
                    && FatherId == other.FatherId && MotherId == other.MotherId && SpouseId == other.SpouseId
                    && Alive == other.Alive && Prisoner == other.Prisoner && Available == other.Available
                    && PositionKnown == other.PositionKnown && BitConverter.DoubleToInt64Bits(X) == BitConverter.DoubleToInt64Bits(other.X)
                    && BitConverter.DoubleToInt64Bits(Y) == BitConverter.DoubleToInt64Bits(other.Y);
                // Fixed contract mutations cover each of the fourteen serialized fields.
                internal Person(in Person source, int changed)
                {
                    Id = changed == 0 ? source.Id + "_changed" : source.Id;
                    Name = changed == 1 ? source.Name + " changed" : source.Name;
                    SettlementId = changed == 2 ? "other_town" : source.SettlementId;
                    PartyId = changed == 3 ? "other_party" : source.PartyId;
                    KingdomId = changed == 4 ? "other_kingdom" : source.KingdomId;
                    FatherId = changed == 5 ? "other_father" : source.FatherId;
                    MotherId = changed == 6 ? "other_mother" : source.MotherId;
                    SpouseId = changed == 7 ? "other_spouse" : source.SpouseId;
                    Alive = changed == 8 ? !source.Alive : source.Alive;
                    Prisoner = changed == 9 ? !source.Prisoner : source.Prisoner;
                    Available = changed == 10 ? !source.Available : source.Available;
                    PositionKnown = changed == 11 ? !source.PositionKnown : source.PositionKnown;
                    X = changed == 12 ? source.X + 1 : source.X;
                    Y = changed == 13 ? source.Y + 1 : source.Y;
                }
            }
            private Person[] _people;
            private readonly int _count;
            private readonly bool _complete;
            private readonly object _encodeGate = new object();
            private string _encodedPeople;
            private bool _sharedContentReused;
            internal readonly string Timeline, CampaignId, Generation;
            internal readonly double WorldDay;
            internal readonly int CaptureThreadId;
            private PersonalGossipPresenceSnapshot(Person[] people, int count, bool complete, string timeline, double day, string campaign, int thread, string generation = "")
            { _people = people; _count = count; _complete = complete; Timeline = timeline; WorldDay = day; CampaignId = campaign; CaptureThreadId = thread; Generation = generation; }
            internal void DisposeCapture()
            {
                lock (_encodeGate)
                {
                    var people = Interlocked.Exchange(ref _people, null);
                    if (people != null) ArrayPool<Person>.Shared.Return(people, clearArray: true);
                }
            }
            internal static PersonalGossipPresenceSnapshot Capture()
            {
                if (!ReignMainThread.IsMainThread) throw new InvalidOperationException("Native gossip capture requires the campaign thread.");
                var rows = ArrayPool<Person>.Shared.Rent(8192);
                int count = 0; bool complete = true;
                try
                {
                    foreach (Hero hero in Hero.AllAliveHeroes)
                    {
                        if (hero == null || string.IsNullOrWhiteSpace(hero.StringId)) continue;
                        if (count == 8192) { complete = false; break; }
                        rows[count++] = new Person(hero);
                    }
                    return new PersonalGossipPresenceSnapshot(rows, count, complete,
                        ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                        CampaignTime.Now.ToDays, GetCampaignId(), Thread.CurrentThread.ManagedThreadId,
                        ReignCampaignInitializationGate.Snapshot().Value<string>("generationId"));
                }
                catch { ArrayPool<Person>.Shared.Return(rows, clearArray: true); throw; }
            }

            internal JObject Encode(bool record = true)
            {
                if (ReignMainThread.IsMainThread) throw new InvalidOperationException("Gossip JSON encoding must run off the campaign thread.");
                var timer = Stopwatch.StartNew();
                string json;
                bool reused;
                lock (_encodeGate)
                {
                    if (_encodedPeople == null)
                    {
                        if (_people == null) throw new ObjectDisposedException("PersonalGossipPresenceSnapshot");
                        try
                        {
                            lock (ContentCacheGate)
                            {
                                bool same = _cachedJson != null && _cachedRowCount == _count && _cachedCampaign == CampaignId
                                    && _cachedTimeline == Timeline && _cachedGeneration == Generation;
                                for (int i = 0; same && i < _count; i++) same = _cachedRows[i].Same(in _people[i]);
                                if (same) { _encodedPeople = _cachedJson; _sharedContentReused = true; }
                                else
                                {
                                    _encodedPeople = WritePeopleJson();
                                    if (_cachedRows != null) ArrayPool<Person>.Shared.Return(_cachedRows, clearArray: true);
                                    _cachedRows = null; _cachedJson = null; _cachedRowCount = 0;
                                    if (_encodedPeople.Length <= MaximumRetainedCharacters)
                                    {
                                        _cachedRows = Interlocked.Exchange(ref _people, null); // exclusive ownership transfers to the single cache entry
                                        _cachedRowCount = _count; _cachedJson = _encodedPeople;
                                        _cachedCampaign = CampaignId; _cachedTimeline = Timeline; _cachedGeneration = Generation;
                                    }
                                }
                            }
                        }
                        finally { DisposeCapture(); }
                    }
                    json = _encodedPeople;
                    reused = _sharedContentReused;
                }
                var result = new JObject { ["schema"] = "reign-gossip-presence-v1", ["timelineId"] = Timeline,
                    ["worldDay"] = WorldDay, ["rosterComplete"] = _complete, ["people"] = new JRaw(json) };
                timer.Stop();
                if (record) lock (GossipTimingGate)
                {
                    _gossipEncodingCount++; _gossipEncodingLast = timer.Elapsed.TotalMilliseconds;
                    if (reused) _gossipEncodingReuseCount++;
                    _gossipEncodingMax = Math.Max(_gossipEncodingMax, _gossipEncodingLast);
                    _gossipEncodedCharactersMax = Math.Max(_gossipEncodedCharactersMax, json.Length);
                    GossipEncodingTimes.Enqueue(_gossipEncodingLast);
                    while (GossipEncodingTimes.Count > 256) GossipEncodingTimes.Dequeue();
                }
                return result;
            }

            // Called only by a worker while ContentCacheGate owns the bounded builders.
            private string WritePeopleJson()
            {
                var buffer = WriterPool.Count == 0 ? new StringBuilder(_count * 320) : WriterPool.Pop();
                var order = ArrayPool<int>.Shared.Rent(Math.Max(1, _count));
                try
                {
                    buffer.Clear();
                    for (int i = 0; i < _count; i++) order[i] = i;
                    Array.Sort(order, 0, _count, Comparer<int>.Create((a, b) => {
                        int comparison = StringComparer.Ordinal.Compare(_people[a].Id, _people[b].Id);
                        return comparison != 0 ? comparison : a.CompareTo(b); // preserve reference ordering for ties
                    }));
                    using (var text = new StringWriter(buffer, CultureInfo.InvariantCulture))
                    using (var writer = new JsonTextWriter(text))
                    {
                        writer.WriteStartArray();
                        for (int i = 0; i < _count; i++)
                        {
                            var person = _people[order[i]];
                            writer.WriteStartObject();
                            writer.WritePropertyName("heroStringId"); writer.WriteValue(person.Id);
                            writer.WritePropertyName("name"); writer.WriteValue(person.Name);
                            writer.WritePropertyName("isAlive"); writer.WriteValue(person.Alive);
                            writer.WritePropertyName("isPrisoner"); writer.WriteValue(person.Prisoner);
                            writer.WritePropertyName("available"); writer.WriteValue(person.Available);
                            writer.WritePropertyName("settlementId"); writer.WriteValue(person.SettlementId);
                            writer.WritePropertyName("partyId"); writer.WriteValue(person.PartyId);
                            writer.WritePropertyName("kingdomId"); writer.WriteValue(person.KingdomId);
                            writer.WritePropertyName("fatherId"); writer.WriteValue(person.FatherId);
                            writer.WritePropertyName("motherId"); writer.WriteValue(person.MotherId);
                            writer.WritePropertyName("spouseId"); writer.WriteValue(person.SpouseId);
                            writer.WritePropertyName("positionKnown"); writer.WriteValue(person.PositionKnown);
                            writer.WritePropertyName("positionX"); writer.WriteValue(person.X);
                            writer.WritePropertyName("positionY"); writer.WriteValue(person.Y);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray(); writer.Flush();
                    }
                    return buffer.ToString();
                }
                finally
                {
                    ArrayPool<int>.Shared.Return(order, clearArray: true);
                    if (buffer.Capacity <= MaximumRetainedCharacters && WriterPool.Count < 2)
                    { buffer.Clear(); WriterPool.Push(buffer); }
                }
            }
            private static PersonalGossipPresenceSnapshot Fixed(int count, string name, double day, string timeline,
                string campaign = "fixture", string generation = "")
            {
                var people = ArrayPool<Person>.Shared.Rent(8192);
                int written = Math.Min(count, 8192);
                for (int i = 0; i < written; i++) people[i] = new Person(name);
                return new PersonalGossipPresenceSnapshot(people, written, count <= 8192, timeline, day, campaign, 0, generation);
            }
            private static bool FixedContentCacheContracts(string name)
            {
                bool fields = true;
                for (int field = 0; field < 14; field++)
                {
                    var baseline = Fixed(1, name, 1.25, "fixture").Encode(false);
                    var changed = Fixed(1, name, 1.25, "fixture");
                    var original = changed._people[0]; changed._people[0] = new Person(in original, field);
                    var encoded = changed.Encode(false);
                    fields &= !changed._sharedContentReused
                        && !JToken.DeepEquals(JArray.Parse(((JRaw)baseline["people"]).Value.ToString()), JArray.Parse(((JRaw)encoded["people"]).Value.ToString()));
                }
                return fields && typeof(Person).GetFields(System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).Length == 14;
            }
            internal static JObject FixedEncodingContracts()
            {
                const string name = "Åda \"quoted\" \\ path\nnext";
                var small = Fixed(1, name, 1.25, "fixture").Encode(false);
                var parsed = JObject.Parse(small.ToString(Formatting.None));
                var expected = new JObject {
                    ["heroStringId"] = "fixture",
                    ["name"] = name,
                    ["isAlive"] = true,
                    ["isPrisoner"] = false,
                    ["available"] = true,
                    ["settlementId"] = "",
                    ["partyId"] = "",
                    ["kingdomId"] = "",
                    ["fatherId"] = "",
                    ["motherId"] = "",
                    ["spouseId"] = "",
                    ["positionKnown"] = true,
                    ["positionX"] = 1.25,
                    ["positionY"] = -0.5,
                };
                bool everyFieldInvalidates = FixedContentCacheContracts(name);
                Fixed(1, name, 1.25, "fixture").Encode(false);
                var nextDay = Fixed(1, name, 2.5, "fixture");
                var dayHeader = nextDay.Encode(false);
                bool scopeInvalidates = true;
                foreach (var scope in new[] { Tuple.Create("fixture", "fixture", "other_generation"),
                    Tuple.Create("fixture", "other_timeline", ""), Tuple.Create("other_campaign", "fixture", "") })
                {
                    Fixed(1, name, 1.25, "fixture").Encode(false);
                    var scoped = Fixed(1, name, 1.25, scope.Item2, scope.Item1, scope.Item3);
                    scoped.Encode(false); scopeInvalidates &= !scoped._sharedContentReused;
                }
                var atCap = Fixed(8192, name, 1.25, "fixture").Encode(false);
                var overflow = Fixed(8193, name, 1.25, "fixture").Encode(false);
                return new JObject { ["escapingFixtureMatches"] = JToken.DeepEquals(expected, parsed["people"]?[0]),
                    ["contentCacheAll14FieldsInvalidate"] = everyFieldInvalidates,
                    ["contentCacheScopeInvalidates"] = scopeInvalidates,
                    ["contentCacheKeepsFreshWorldDay"] = nextDay._sharedContentReused && dayHeader.Value<double>("worldDay") == 2.5,
                    ["cap8192Complete"] = atCap.Value<bool>("rosterComplete"),
                    ["cap8193Incomplete"] = !overflow.Value<bool>("rosterComplete"),
                    ["overflowSerializedPeople"] = (JObject.Parse(overflow.ToString(Formatting.None))["people"] as JArray)?.Count ?? -1,
                    ["nativeObjectsRetained"] = typeof(Person).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                        .Any(f => !f.IsInitOnly || f.FieldType != typeof(string) && f.FieldType != typeof(bool) && f.FieldType != typeof(double)) };
            }
            internal static JObject FixedIncomplete(double day, string timeline)
            { return Fixed(8193, "fixture", day, timeline).Encode(false); }
        }

        internal static PersonalGossipPresenceSnapshot CapturePersonalGossipPresence()
        {
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            var timer = Stopwatch.StartNew();
            var snapshot = PersonalGossipPresenceSnapshot.Capture();
            timer.Stop();
            bool collected0 = gen0 != GC.CollectionCount(0), collected1 = gen1 != GC.CollectionCount(1), collected2 = gen2 != GC.CollectionCount(2);
            lock (GossipTimingGate)
            {
                _gossipCaptureCount++; _gossipCaptureLast = timer.Elapsed.TotalMilliseconds;
                if (collected0) _gossipCaptureGen0Overlap++;
                if (collected1) _gossipCaptureGen1Overlap++;
                if (collected2) _gossipCaptureGen2Overlap++;
                _gossipCaptureMax = Math.Max(_gossipCaptureMax, _gossipCaptureLast);
                GossipCaptureTimes.Enqueue(_gossipCaptureLast);
                while (GossipCaptureTimes.Count > 256) GossipCaptureTimes.Dequeue();
            }
            return snapshot;
        }
        internal static Task<JObject> EncodePersonalGossipPresenceAsync(PersonalGossipPresenceSnapshot snapshot) =>
            Task.Run(() => snapshot.Encode());
        internal static bool PersonalGossipSnapshotCurrent(PersonalGossipPresenceSnapshot snapshot) =>
            snapshot != null && !ReignCampaignInitializationGate.IsPending && !ReignSaveSyncCoordinator.IsAlignmentPending
            && TaleWorlds.CampaignSystem.Campaign.Current != null && snapshot.CampaignId == GetCampaignId()
            && snapshot.Timeline == (ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
            && snapshot.Generation == ReignCampaignInitializationGate.Snapshot().Value<string>("generationId");
        internal static Task<JObject> PersonalGossipIncompleteFixtureAsync(double day, string timeline) =>
            Task.Run(() => PersonalGossipPresenceSnapshot.FixedIncomplete(day, timeline));

        // Test-only reference retains the previous full object-tree representation.
        private static JObject BuildPersonalGossipPresenceReference()
        {
            var heroes = Hero.AllAliveHeroes.Where(h => h != null && !string.IsNullOrWhiteSpace(h.StringId))
                .OrderBy(h => h.StringId, StringComparer.Ordinal).Take(8193).ToList();
            var people = new JArray();
            foreach (Hero hero in heroes.Take(8192))
            {
                MobileParty party = hero.PartyBelongedTo;
                Settlement settlement = hero.CurrentSettlement ?? party?.CurrentSettlement;
                if (hero == Hero.MainHero)
                {
                    party = party ?? MobileParty.MainParty;
                    settlement = settlement ?? party?.CurrentSettlement ?? Settlement.CurrentSettlement;
                }
                var position = party?.GetPosition2D ?? settlement?.GetPosition2D;
                people.Add(new JObject {
                    ["heroStringId"] = hero.StringId,
                    ["name"] = hero.Name?.ToString() ?? hero.StringId,
                    ["isAlive"] = hero.IsAlive,
                    ["isPrisoner"] = hero.IsPrisoner,
                    ["available"] = hero.IsAlive && hero.IsActive && !hero.IsPrisoner,
                    ["settlementId"] = settlement?.StringId ?? string.Empty,
                    ["partyId"] = party?.StringId ?? string.Empty,
                    ["kingdomId"] = hero.Clan?.Kingdom?.StringId ?? string.Empty,
                    ["fatherId"] = hero.Father?.StringId ?? string.Empty,
                    ["motherId"] = hero.Mother?.StringId ?? string.Empty,
                    ["spouseId"] = hero.Spouse?.StringId ?? string.Empty,
                    ["positionKnown"] = position.HasValue,
                    ["positionX"] = position.HasValue ? (double)position.Value.X : 0d,
                    ["positionY"] = position.HasValue ? (double)position.Value.Y : 0d,
                });
            }
            return new JObject { ["schema"] = "reign-gossip-presence-v1",
                ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["worldDay"] = CampaignTime.Now.ToDays, ["rosterComplete"] = heroes.Count <= 8192, ["people"] = people };
        }
        internal static Tuple<PersonalGossipPresenceSnapshot, JObject> CapturePersonalGossipComparison()
        {
            var captured = CapturePersonalGossipPresence();
            try { return Tuple.Create(captured, BuildPersonalGossipPresenceReference()); }
            catch { captured.DisposeCapture(); throw; }
        }
        internal static Task<JObject> PersonalGossipPresenceContractAsync(Tuple<PersonalGossipPresenceSnapshot, JObject> captured) => Task.Run(() =>
        {
            var optimized = captured.Item1.Encode();
            var parsed = JObject.Parse(optimized.ToString(Formatting.None));
            var clone = JObject.Parse(optimized.DeepClone().ToString(Formatting.None));
            var result = PersonalGossipPresenceSnapshot.FixedEncodingContracts();
            result["immutableAfterPoolReuse"] = JToken.DeepEquals(parsed, JObject.Parse(captured.Item1.Encode(false).ToString(Formatting.None)));
            result["schema"] = "reign-gossip-presence-contract-v1";
            result["matchesReference"] = JToken.DeepEquals(captured.Item2, parsed);
            result["cloneMatchesReference"] = JToken.DeepEquals(captured.Item2, clone);
            result["peopleCount"] = (parsed["people"] as JArray)?.Count ?? -1;
            result["rosterComplete"] = parsed["rosterComplete"]?.DeepClone();
            result["encodingOffCaptureThread"] = Thread.CurrentThread.ManagedThreadId != captured.Item1.CaptureThreadId;
            result["referenceTimingExcluded"] = true;
            result["coverage"] = "Actual full-roster wire/clone equivalence, immutable value types, encoding and cap boundaries; not a percentile or language verdict.";
            return result;
        });

        private static bool GossipPumpCurrent(PersonalGossipPump pump) => pump != null
            && ReferenceEquals(pump, _personalGossipPump) && ReferenceEquals(pump.Campaign, TaleWorlds.CampaignSystem.Campaign.Current)
            && pump.Timeline == (ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
            && pump.Generation == ReignCampaignInitializationGate.Snapshot().Value<string>("generationId")
            && !ReignCampaignInitializationGate.IsPending && !ReignSaveSyncCoordinator.IsAlignmentPending;

        private static PersonalGossipPump CurrentGossipPump()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCoordinator.IsAlignmentPending || ReignBeta.Campaign.ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress) return null;
            if (!GossipPumpCurrent(_personalGossipPump))
                _personalGossipPump = new PersonalGossipPump { Campaign = TaleWorlds.CampaignSystem.Campaign.Current,
                    CampaignId = GetCampaignId(), Timeline = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                    Generation = ReignCampaignInitializationGate.Snapshot().Value<string>("generationId"), NextPoll = DateTime.UtcNow };
            return _personalGossipPump;
        }

        // ApplicationTick also runs while campaign time is paused. Only a cached
        // deadline, bounded composition watch or slow disabled-settings probe causes polling.
        public static void PulsePersonalGossip()
        {
            var pump = _personalGossipPump;
            DateTime now = DateTime.UtcNow;
            if (pump == null) { _ = TickPersonalGossipAsync(); return; }
            if (Volatile.Read(ref pump.InFlight) != 0 || now < pump.NextPoll) return;
            if (pump.Wake.ShouldPoll(now) || pump.NextDue <= CampaignTime.Now.ToDays)
                _ = PollPersonalGossipAsync(false);
            else if (now >= pump.NextScopeCheck)
            {
                pump.NextScopeCheck = now.AddSeconds(1);
                if (!GossipPumpCurrent(pump)) _ = TickPersonalGossipAsync();
            }
        }

        private static Task<JObject> GossipReadinessAsync(PersonalGossipPump pump, double day) => GetJsonAsync("/gossip/status?deliveryOnly=true&campaignId="
            + Uri.EscapeDataString(pump.CampaignId) + "&timelineId=" + Uri.EscapeDataString(pump.Timeline)
            + "&worldDay=" + day.ToString("R", CultureInfo.InvariantCulture));

        private static async Task PollPersonalGossipAsync(bool force)
        {
            var pump = await ReignMainThread.InvokeAsync(() =>
            {
                var current = CurrentGossipPump();
                if (force && current != null) current.Wake.Request(DateTime.UtcNow);
                return current;
            }).ConfigureAwait(false);
            if (pump == null || Interlocked.CompareExchange(ref pump.InFlight, 1, 0) != 0) return;
            try
            {
                long observedRevision = 0;
                double day = await ReignMainThread.InvokeAsync(() =>
                {
                    observedRevision = pump.Wake.Revision;
                    return GossipPumpCurrent(pump) ? CampaignTime.Now.ToDays : -1d;
                }).ConfigureAwait(false);
                if (day < 0) return;
                JObject ready = await GossipReadinessAsync(pump, day).ConfigureAwait(false);
                var snapshot = await ReignMainThread.InvokeAsync(() =>
                {
                    if (!GossipPumpCurrent(pump)) return null;
                    pump.Readiness = ready;
                    if (ready?.Value<bool?>("ok") != true || ready.Value<string>("schema") != "reign-gossip-delivery-readiness-v1"
                        || ready.Value<string>("campaignId") != pump.CampaignId || ready.Value<string>("timelineId") != pump.Timeline)
                        throw new InvalidOperationException("Gossip delivery readiness is unavailable or belongs to another campaign.");
                    pump.LastError = "";
                    pump.NextDue = ready.Value<double?>("nextDueDay") ?? double.PositiveInfinity;
                    bool work = ready.Value<int?>("pendingComposition") > 0 || ready.Value<int?>("awaitingNativeClock") > 0;
                    pump.Wake.Observe(observedRevision, ready.Value<bool?>("enabled") == true, work, DateTime.UtcNow);
                    if (ready.Value<bool?>("enabled") != true) { pump.NextDue = double.PositiveInfinity; return null; }
                    if (ready.Value<int?>("awaitingNativeClock") == 0 && pump.NextDue > CampaignTime.Now.ToDays) return null;
                    // Readiness was received before this main-thread clock/roster capture.
                    return CapturePersonalGossipPresence();
                }).ConfigureAwait(false);
                if (snapshot != null)
                {
                    var presence = await EncodePersonalGossipPresenceAsync(snapshot).ConfigureAwait(false);
                    var payload = new JObject { ["campaignId"] = pump.CampaignId, ["timelineId"] = snapshot.Timeline,
                        ["worldDay"] = snapshot.WorldDay, ["gossipPresence"] = presence,
                        ["gossipClockEpoch"] = ready["clockEpoch"]?.DeepClone(), ["gossipReadyNonces"] = ready["readyNonces"]?.DeepClone() };
                    var response = await PostJsonAsync("/gossip/tick", payload, isCurrentRequest: () => ReignMainThread.InvokeAsync(() => GossipPumpCurrent(pump))).ConfigureAwait(false);
                    if (response?.Value<bool?>("ok") != true) throw new InvalidOperationException("Server rejected the current gossip clock proof.");
                }
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(pump, _personalGossipPump)) pump.LastError = ex.GetType().Name + ": " + ex.Message;
                ReignLog.Warn("Personal gossip clock deferred: " + ex.Message);
            }
            finally
            {
                pump.NextPoll = DateTime.UtcNow.AddSeconds(string.IsNullOrEmpty(pump.LastError) ? 1 : 5);
                Interlocked.Exchange(ref pump.InFlight, 0);
            }
        }

        internal static async Task<PersonalGossipPresenceSnapshot> RefreshPersonalGossipConversationAsync(JObject payload)
        {
            if (payload?["gossipPresence"] == null) return null;
            var pump = await ReignMainThread.InvokeAsync(CurrentGossipPump).ConfigureAwait(false);
            if (pump == null) return null;
            string campaign = payload.Value<string>("campaignId") ?? "";
            if (campaign.Length > 0 && campaign != pump.CampaignId)
                throw new OperationCanceledException("Gossip input belongs to a previous campaign.");
            JObject ready = null;
            try
            {
                double day = await ReignMainThread.InvokeAsync(() => CampaignTime.Now.ToDays).ConfigureAwait(false);
                var response = await GossipReadinessAsync(pump, day).ConfigureAwait(false);
                if (response?.Value<bool?>("ok") == true && response.Value<string>("schema") == "reign-gossip-delivery-readiness-v1"
                    && response.Value<string>("campaignId") == pump.CampaignId && response.Value<string>("timelineId") == pump.Timeline)
                    ready = response;
            }
            catch (Exception ex) { ReignLog.Warn("Gossip conversation readiness deferred: " + ex.GetType().Name); }
            var snapshot = await ReignMainThread.InvokeAsync(() =>
            {
                if (!GossipPumpCurrent(pump)) return null;
                if (ready != null && ready.Value<bool?>("enabled") != true)
                {
                    // Explicit promised communications still need exact identities
                    // and presence when probabilistic personal gossip is disabled.
                    payload.Remove("gossipClockEpoch"); payload.Remove("gossipReadyNonces");
                }
                return CapturePersonalGossipPresence();
            }).ConfigureAwait(false);
            if (snapshot == null) return null;
            var presence = await EncodePersonalGossipPresenceAsync(snapshot).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                if (!GossipPumpCurrent(pump)) throw new OperationCanceledException("Gossip input belongs to a previous loaded save.");
                payload["gossipPresence"] = presence; payload["worldDay"] = snapshot.WorldDay;
                if (ready != null)
                {
                    payload["gossipClockEpoch"] = ready["clockEpoch"]?.DeepClone();
                    payload["gossipReadyNonces"] = ready["readyNonces"]?.DeepClone();
                }
                else { payload.Remove("gossipClockEpoch"); payload.Remove("gossipReadyNonces"); }
            }).ConfigureAwait(false);
            return snapshot;
        }

        internal static JObject PersonalGossipRuntimeStatus()
        {
            var pump = _personalGossipPump;
            double[] times, encodings;
            long captureCount, encodingCount, characters, reused, gen0, gen1, gen2;
            double captureLast, captureMax, encodingLast, encodingMax;
            lock (GossipTimingGate)
            {
                times = GossipCaptureTimes.OrderBy(x => x).ToArray(); encodings = GossipEncodingTimes.OrderBy(x => x).ToArray();
                captureCount = _gossipCaptureCount; captureLast = _gossipCaptureLast; captureMax = _gossipCaptureMax;
                encodingCount = _gossipEncodingCount; encodingLast = _gossipEncodingLast; encodingMax = _gossipEncodingMax;
                characters = _gossipEncodedCharactersMax;
                reused = _gossipEncodingReuseCount;
                gen0 = _gossipCaptureGen0Overlap; gen1 = _gossipCaptureGen1Overlap; gen2 = _gossipCaptureGen2Overlap;
            }
            return new JObject { ["schema"] = "reign-gossip-clock-runtime-v1", ["inFlight"] = pump != null && Volatile.Read(ref pump.InFlight) != 0
                    || Volatile.Read(ref _communicationInFlight) != 0 || ReignBeta.Campaign.ReignNpcLivesBehavior.Instance?.HasPendingCommunicationReceipts == true,
                ["campaignId"] = pump?.CampaignId ?? _communicationCampaign, ["timelineId"] = pump?.Timeline ?? _communicationTimeline,
                ["lastError"] = pump?.LastError ?? "", ["readiness"] = pump?.Readiness?.DeepClone(),
                ["periodicCommunicationHoldVersion"] = 1,
                ["periodicCommunicationHoldActive"] = CommunicationPollHold.HasLease
                    && CommunicationPollHold.Active(TaleWorlds.CampaignSystem.Campaign.Current, CommunicationPollScope(), DateTime.UtcNow),
                ["periodicCommunicationHoldUntilUtc"] = CommunicationPollHold.UntilUtc,
                ["watchUntilUtc"] = pump?.Wake.UntilUtc, ["knownWork"] = pump?.Wake.HasKnownWork ?? false,
                ["disabledSettingsWatch"] = pump?.Wake.IsDisabled ?? false,
                ["nextBackgroundPollUtc"] = pump?.Wake.NextBackgroundPollUtc,
                ["captureCount"] = captureCount, ["captureWindowCount"] = times.Length,
                ["captureP95Ms"] = times.Length == 0 ? 0 : times[Math.Max(0, (int)Math.Ceiling(times.Length * .95) - 1)],
                ["captureMaxMs"] = captureMax, ["captureLastMs"] = captureLast,
                ["captureGen0CollectionOverlap"] = gen0, ["captureGen1CollectionOverlap"] = gen1, ["captureGen2CollectionOverlap"] = gen2,
                ["captureMetricScope"] = "native-immutable-snapshot-main-thread-v2; encoding measured separately on worker",
                ["encodingCount"] = encodingCount, ["encodingWindowCount"] = encodings.Length, ["encodingContentReuseCount"] = reused,
                ["encodingLastMs"] = encodingLast, ["encodingMaxMs"] = encodingMax,
                ["encodingP95Ms"] = encodings.Length == 0 ? 0 : encodings[Math.Max(0, (int)Math.Ceiling(encodings.Length * .95) - 1)],
                ["encodedPeopleCharactersMax"] = characters };
        }

        public static async Task TickPersonalGossipAsync()
        {
            await PollPersonalGossipAsync(true).ConfigureAwait(false);
        }
    }
}
