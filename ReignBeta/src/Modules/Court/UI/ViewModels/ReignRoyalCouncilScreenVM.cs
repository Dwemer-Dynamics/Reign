using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Court.WarCouncil;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignRoyalCouncilScreenVM : ViewModel
    {
        private static readonly string[] Roles = { "war", "spymaster", "economic", "foreign" };
        private readonly ReignCourtCampaignBehavior _court;
        private readonly Action _close;
        private readonly string _sessionId = "royal_council_" + Guid.NewGuid().ToString("N");
        private bool _busy;
        private string _inputText = string.Empty;
        private string _statusText = "The Royal Council is assembling.";
        private int _providerCallCount;
        private List<string> _lastRoles = new List<string>();
        private readonly bool _calibrationFixture;

        public ReignRoyalCouncilScreenVM(ReignCourtCampaignBehavior court, Action close)
            : this(court, close, false)
        {
        }

        internal ReignRoyalCouncilScreenVM(ReignCourtCampaignBehavior court, Action close, bool calibrationFixture)
        {
            _court = court ?? throw new ArgumentNullException(nameof(court));
            _close = close;
            _calibrationFixture = calibrationFixture;
            Transcript = new MBBindingList<ReignChatLineVM>();
            RefreshSeats();
            if (_calibrationFixture) PopulateCalibrationBriefings();
            else _ = RequestOpeningBriefingsAsync();
        }

        [DataSourceProperty] public ReignRoyalCouncilSeatVM WarSeat { get; private set; }
        [DataSourceProperty] public ReignRoyalCouncilSeatVM SpymasterSeat { get; private set; }
        [DataSourceProperty] public ReignRoyalCouncilSeatVM EconomicSeat { get; private set; }
        [DataSourceProperty] public ReignRoyalCouncilSeatVM ForeignSeat { get; private set; }
        [DataSourceProperty] public MBBindingList<ReignChatLineVM> Transcript { get; }
        [DataSourceProperty] public string InputText { get => _inputText; set { if (_inputText != value) { _inputText = value ?? string.Empty; OnPropertyChangedWithValue(_inputText); } } }
        [DataSourceProperty] public string StatusText { get => _statusText; private set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool IsBusy { get => _busy; private set { if (_busy != value) { _busy = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(CanSend)); } } }
        [DataSourceProperty] public bool CanSend => !IsBusy && !string.IsNullOrWhiteSpace(InputText);
        public int AutomationProviderCallCount => _providerCallCount;
        public string AutomationTranscript => string.Join("\n", Transcript.Select(line => line.Speaker + ": " + line.Text));
        public bool AutomationCalibrationFixture => _calibrationFixture;

        public void ExecuteClose() => _close?.Invoke();
        public void ExecuteSend()
        {
            string text = (InputText ?? string.Empty).Trim();
            if (IsBusy || text.Length == 0) return;
            InputText = string.Empty;
            _ = SendAsync(text);
        }

        public bool TryAutomation(string action, string value, out string error)
        {
            error = string.Empty;
            string verb = (action ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            if (verb == "send") { if (IsBusy) { error = "The council is busy."; return false; } InputText = value ?? string.Empty; ExecuteSend(); return true; }
            if (verb == "open-briefings") { if (IsBusy) { error = "The council is busy."; return false; } _ = RequestOpeningBriefingsAsync(); return true; }
            if (verb == "open-seat")
            {
                ReignRoyalCouncilSeatVM seat = Seat(value);
                if (seat == null) { error = "Unknown Royal Council seat."; return false; }
                seat.ExecuteOpen(); return true;
            }
            error = "Supported Royal Council actions are send, open-briefings, and open-seat.";
            return false;
        }

        private async Task RequestOpeningBriefingsAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = "Receiving the council's opening briefings...";
            foreach (string role in Roles)
            {
                ReignRoyalCouncilSeatVM seat = Seat(role);
                if (seat?.IsAvailable == true) await RequestAdvisorAsync(role, seat.Hero, string.Empty, true, false).ConfigureAwait(false);
            }
            await ReignMainThread.InvokeAsync(() => { IsBusy = false; StatusText = "The council awaits your question."; });
        }

        private void PopulateCalibrationBriefings()
        {
            AddCalibrationBriefing("war", "No wars threaten the realm. Our field forces remain assembled, and the only recent fighting involved bandits beyond the royal host.");
            AddCalibrationBriefing("spymaster", "No intelligence operation is active and no agent is exposed. I have no substantiated rumor requiring Your Majesty's attention.");
            AddCalibrationBriefing("economic", "The treasury is stable, supplies are adequate, and no settlement reports a material shortage. Current shipments remain on schedule.");
            AddCalibrationBriefing("foreign", "Relations with neighboring rulers are steady and political pressure remains low. No foreign conflict presently requires a royal response.");
            if (Transcript.Count == 0)
                Transcript.Add(new ReignChatLineVM("Council Clerk", "No staffed and available advisor can deliver a briefing.", "system"));
            IsBusy = false;
            StatusText = "The council awaits your question.";
        }

        private void AddCalibrationBriefing(string role, string text)
        {
            ReignRoyalCouncilSeatVM seat = Seat(role);
            if (seat?.IsAvailable != true) return;
            Transcript.Add(new ReignChatLineVM(seat.Hero?.Name?.ToString() ?? seat.Title, text, "npc"));
        }

        private async Task SendAsync(string text)
        {
            IsBusy = true;
            Transcript.Add(new ReignChatLineVM("You", text, "player"));
            List<string> routed = Route(text);
            bool expand = IsExpansion(text);
            if (expand && routed.Count == 0) routed = _lastRoles.ToList();
            routed = routed.Distinct(StringComparer.OrdinalIgnoreCase).Where(role => Seat(role)?.IsAvailable == true).ToList();
            if (routed.Count == 0)
            {
                Transcript.Add(new ReignChatLineVM("Council Clerk", "Name an advisor or ask about war, intelligence, the economy, or foreign affairs.", "system"));
                IsBusy = false;
                StatusText = "No advisor was called; no provider request was made.";
                return;
            }
            _lastRoles = routed;
            foreach (string role in routed) await RequestAdvisorAsync(role, Seat(role).Hero, text, false, expand).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => { IsBusy = false; StatusText = "The council awaits your next question."; });
        }

        private async Task RequestAdvisorAsync(string role, Hero hero, string playerText, bool opening, bool expand)
        {
            JArray record = new JArray(Transcript.Select(line => new JObject { ["speaker"] = line.Speaker, ["role"] = line.Role, ["text"] = line.Text }));
            JArray present = new JArray(Roles.Select(Seat).Where(seat => seat?.IsAvailable == true).Select(seat => seat.Hero.StringId));
            ReignCourtServerResponse response = await ReignCourtServerClient.RequestRoyalCouncilAdviceAsync(_sessionId,
                "council_turn_" + Guid.NewGuid().ToString("N"), role, hero, playerText, opening, expand, BuildDomainPacket(role), record, present).ConfigureAwait(false);
            _providerCallCount += Math.Max(0, response.Raw.Value<int?>("providerCallCount") ?? 0);
            await ReignMainThread.InvokeAsync(() =>
            {
                if (!response.Ok) Transcript.Add(new ReignChatLineVM("Council Clerk", Seat(role).Title + " could not report: " + response.Error, "system"));
                else Transcript.Add(new ReignChatLineVM(hero?.Name?.ToString() ?? Seat(role).Title, response.Raw.Value<string>("reply") ?? "Nothing material to report.", "npc"));
            });
        }

        private JObject BuildDomainPacket(string role)
        {
            Kingdom ours = Clan.PlayerClan?.Kingdom;
            if (role == "war")
            {
                return new JObject
                {
                    ["wars"] = new JArray(Kingdom.All.Where(k => k != null && k != ours && !k.IsEliminated && ours != null && FactionManager.IsAtWarAgainstFaction(ours, k)).Take(12).Select(k => k.Name?.ToString())),
                    ["hostileForces"] = new JArray(MobileParty.All.Where(p => p?.LeaderHero != null && p.CurrentSettlement != null && p.CurrentSettlement.MapFaction == ours && ours != null && FactionManager.IsAtWarAgainstFaction(ours, p.MapFaction)).Take(20).Select(p => p.Name?.ToString())),
                    ["realmForces"] = ours == null ? 0 : ours.CurrentTotalStrength,
                    ["recentBattles"] = new JArray((ReignWarCouncilCampaignBehavior.Instance?.RecentReports
                        ?? Enumerable.Empty<ReignWarCouncilBattleReport>()).Take(12).Select(report => new JObject
                    {
                        ["day"] = report.Day, ["type"] = report.BattleType, ["winner"] = report.Winner,
                        ["attacker"] = report.Attacker, ["defender"] = report.Defender,
                        ["attackerLosses"] = report.AttackerLosses, ["defenderLosses"] = report.DefenderLosses,
                        ["settlement"] = report.Settlement, ["playerRealmInvolved"] = report.PlayerRealmInvolved
                    })),
                    ["conflicts"] = new JArray()
                };
            }
            if (role == "economic")
            {
                ReignEconomicReportState state = _court.EconomicReportState;
                return new JObject
                {
                    ["treasury"] = Hero.MainHero?.Gold ?? 0, ["income"] = 0, ["expenses"] = 0,
                    ["shortages"] = new JArray((state?.Settlements ?? new List<ReignSettlementSupplySnapshot>()).Where(s => s.HasShortage).Take(20).Select(s => s.Name)),
                    ["resources"] = new JArray((state?.Settlements ?? new List<ReignSettlementSupplySnapshot>()).Take(20).Select(s => new JObject { ["settlement"] = s.Name, ["food"] = s.FoodStocks, ["change"] = s.FoodChange })),
                    ["settlements"] = state?.Settlements?.Count ?? 0,
                    ["shipments"] = new JArray((state?.Shipments ?? new List<ReignEconomicShipment>()).Skip(Math.Max(0, (state?.Shipments?.Count ?? 0) - 12)).Select(s => new JObject { ["from"] = s.SourceSettlementName, ["to"] = s.TargetSettlementName, ["status"] = s.Status, ["amount"] = s.Amount })),
                    ["developments"] = new JArray()
                };
            }
            if (role == "spymaster")
            {
                return new JObject
                {
                    ["operations"] = new JArray(_court.IntelligenceOperations.Skip(Math.Max(0, _court.IntelligenceOperations.Count - 20)).Select(o => new JObject { ["type"] = o.OperationType, ["targetType"] = o.TargetType, ["state"] = o.State.ToString(), ["confidence"] = o.Confidence })),
                    ["agents"] = _court.SpymasterState?.ForeignAgents?.Count ?? 0,
                    ["exposure"] = _court.IntelligenceOperations.Count(o => o.IsExposed),
                    ["rumors"] = new JArray(_court.Matters.Where(m => m.Kind == ReignCourtMatterKind.Rumor && !m.IsTerminal).Take(12).Select(m => m.Summary)),
                    ["confidenceLimitations"] = "Only intelligence already known to the player's Spymaster system is included."
                };
            }
            return new JObject
            {
                ["rulerRelationships"] = new JArray(Kingdom.All.Where(k => k != null && k != ours && !k.IsEliminated).Take(20).Select(k => new JObject { ["realm"] = k.Name?.ToString(), ["ruler"] = k.Leader?.Name?.ToString(), ["relation"] = k.Leader == null ? 0 : Hero.MainHero.GetRelation(k.Leader) })),
                ["politicalPressure"] = new JArray(),
                ["ambassadors"] = new JArray(_court.ForeignAmbassadors.Where(a => a.IsActive).Take(12).Select(a => new JObject { ["originKingdomId"] = a.OriginKingdomStringId, ["status"] = a.Status })),
                ["referrals"] = new JArray(),
                ["foreignWars"] = new JArray(Kingdom.All.Where(k => k != null && !k.IsEliminated).Take(20).Select(k => new JObject { ["realm"] = k.Name?.ToString(), ["atWarWithPlayer"] = ours != null && k != ours && FactionManager.IsAtWarAgainstFaction(ours, k) })),
                ["nationalDevelopments"] = new JArray()
            };
        }

        private void RefreshSeats()
        {
            WarSeat = CreateSeat("war", "WAR COUNCILOR", FindHero(ReignWarCouncilCampaignBehavior.Instance?.SelectedCouncilorHeroId), OpenWar);
            SpymasterSeat = CreateSeat("spymaster", "SPYMASTER", OfficeHero(ReignCourtOffice.Spymaster), OpenSpymaster);
            EconomicSeat = CreateSeat("economic", "ECONOMIC ADVISOR", OfficeHero(ReignCourtOffice.EconomicAdvisor), () => OpenOrAppoint(ReignCourtOffice.EconomicAdvisor));
            ForeignSeat = CreateSeat("foreign", "FOREIGN ADVISOR", OfficeHero(ReignCourtOffice.ForeignAdvisor), () => OpenOrAppoint(ReignCourtOffice.ForeignAdvisor));
            OnPropertyChanged(nameof(WarSeat)); OnPropertyChanged(nameof(SpymasterSeat)); OnPropertyChanged(nameof(EconomicSeat)); OnPropertyChanged(nameof(ForeignSeat));
        }

        private ReignRoyalCouncilSeatVM CreateSeat(string role, string title, Hero hero, Action open) => new ReignRoyalCouncilSeatVM(role, title, hero, IsAvailable(hero), open);
        private Hero OfficeHero(ReignCourtOffice office) => FindHero(_court.Offices.FirstOrDefault(a => a.IsActive && a.Office == office)?.HeroStringId);
        private static Hero FindHero(string id) => string.IsNullOrWhiteSpace(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(h => string.Equals(h.StringId, id, StringComparison.OrdinalIgnoreCase));
        private static bool IsAvailable(Hero hero) => hero != null && hero.IsAlive && !hero.IsPrisoner;
        private ReignRoyalCouncilSeatVM Seat(string role) => role == "war" ? WarSeat : role == "spymaster" ? SpymasterSeat : role == "economic" ? EconomicSeat : role == "foreign" ? ForeignSeat : null;
        private void OpenWar() { _close?.Invoke(); ReignWarCouncilScreenManager.Open(_court); }
        private void OpenSpymaster() { _close?.Invoke(); ReignSpymasterScreenManager.Open(_court); }

        private void OpenOrAppoint(ReignCourtOffice office)
        {
            Hero incumbent = OfficeHero(office);
            if (incumbent != null) { BeginAppointment(office, true); return; }
            BeginAppointment(office, false);
        }

        private void BeginAppointment(ReignCourtOffice office, bool replacing)
        {
            List<Hero> candidates = ResidentAdvisorCandidates().ToList();
            if (candidates.Count == 0) { InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] No eligible free adult lord is present in the capital.")); return; }
            List<InquiryElement> choices = candidates.Select(hero => new InquiryElement(hero,
                (hero.Name?.ToString() ?? hero.StringId) + DutyWarning(hero), null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                replacing ? "Replace " + OfficeTitle(office) : "Appoint " + OfficeTitle(office),
                "Choose a distinct adult, active, free lord physically present in the capital. Any governorship or safely retireable non-combat party command shown below will be removed.",
                choices, true, 1, 1, replacing ? "Replace" : "Appoint", "Cancel", selected =>
                {
                    Hero hero = selected?.FirstOrDefault()?.Identifier as Hero;
                    if (hero != null) _ = AppointResidentAdvisorAsync(office, hero);
                }, null, string.Empty, false), true, false);
        }

        private IEnumerable<Hero> ResidentAdvisorCandidates()
        {
            var used = new HashSet<string>(_court.Offices.Where(a => a.IsActive).Select(a => a.HeroStringId), StringComparer.OrdinalIgnoreCase);
            string war = ReignWarCouncilCampaignBehavior.Instance?.SelectedCouncilorHeroId;
            if (!string.IsNullOrWhiteSpace(war)) used.Add(war);
            return Hero.AllAliveHeroes.Where(hero => hero != null && hero.Occupation == Occupation.Lord && !hero.IsChild && !hero.IsPrisoner
                && hero.CurrentSettlement == _court.CurrentCapital && !used.Contains(hero.StringId));
        }

        private async Task AppointResidentAdvisorAsync(ReignCourtOffice office, Hero hero)
        {
            if (!ResidentAdvisorCandidates().Contains(hero)) { InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Native conditions changed; no appointment was made.")); return; }
            MobileParty party = hero.PartyBelongedTo;
            if (party != null && party.LeaderHero == hero && (party.CurrentSettlement != _court.CurrentCapital || party.MapEvent != null || party.Army != null))
            { InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] That party cannot be safely retired at the capital.")); return; }
            string error = await _court.AssignOfficeAsync(office, hero).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(async () =>
            {
                if (!string.IsNullOrWhiteSpace(error)) { InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + error)); return; }
                try
                {
                    if (hero.GovernorOf != null) ChangeGovernorAction.RemoveGovernorOf(hero);
                    if (party != null && party.LeaderHero == hero) DisbandPartyAction.StartDisband(party);
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + hero.Name + " appointed " + OfficeTitle(office) + "."));
                    RefreshSeats();
                }
                catch (Exception ex)
                {
                    CourtOfficeAssignment active = _court.Offices.FirstOrDefault(a => a.IsActive && a.Office == office && a.HeroStringId == hero.StringId);
                    if (active != null) await _court.DismissOfficeAsync(active);
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Appointment rolled back: " + ex.Message));
                }
            });
        }

        private static string DutyWarning(Hero hero)
        {
            List<string> duties = new List<string>();
            if (hero?.GovernorOf != null) duties.Add("governorship of " + hero.GovernorOf.Name);
            if (hero?.PartyBelongedTo?.LeaderHero == hero) duties.Add("party command");
            return duties.Count == 0 ? string.Empty : " — removes " + string.Join(" and ", duties);
        }
        private static string OfficeTitle(ReignCourtOffice office) => office == ReignCourtOffice.EconomicAdvisor ? "Economic Advisor" : "Foreign Advisor";
        private static bool IsExpansion(string text) => new[] { "expand", "detail", "why", "how", "tell me more" }.Any(k => (text ?? string.Empty).IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        internal static List<string> Route(string text)
        {
            string value = (text ?? string.Empty).ToLowerInvariant();
            List<string> roles = new List<string>();
            if (Any(value, "war", "enemy", "battle", "army", "troop", "conflict", "war councilor", "marshal")) roles.Add("war");
            if (Any(value, "spy", "spymaster", "agent", "operation", "rumor", "exposure", "intelligence")) roles.Add("spymaster");
            if (Any(value, "economic", "economy", "treasury", "income", "expense", "food", "shortage", "resource", "settlement", "shipment", "trade")) roles.Add("economic");
            if (Any(value, "foreign", "diplomacy", "relationship", "ruler", "pressure", "ambassador", "nation", "realm")) roles.Add("foreign");
            return roles;
        }
        private static bool Any(string text, params string[] values) => values.Any(value => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public sealed class ReignRoyalCouncilSeatVM : ViewModel
    {
        private readonly Action _open;
        private readonly ImageIdentifierVM _portrait;
        public ReignRoyalCouncilSeatVM(string role, string title, Hero hero, bool available, Action open)
        {
            Role = role; Title = title; Hero = hero; IsAvailable = available; _open = open;
            PortraitCacheKey = CharacterCacheId.ForHero(hero) ?? string.Empty;
            try { CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject); _portrait = string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code); } catch { }
        }
        public Hero Hero { get; }
        [DataSourceProperty] public string Role { get; }
        [DataSourceProperty] public string Title { get; }
        [DataSourceProperty] public string Name => Hero?.Name?.ToString() ?? "VACANT";
        [DataSourceProperty] public string Status => Hero == null ? "CLICK TO APPOINT" : IsAvailable ? "AVAILABLE" : "UNAVAILABLE";
        [DataSourceProperty] public bool IsAvailable { get; }
        [DataSourceProperty] public bool HasPortrait => Hero != null;
        [DataSourceProperty] public string PortraitCacheKey { get; }
        [DataSourceProperty] public string PortraitId => _portrait?.Id ?? string.Empty;
        [DataSourceProperty] public string PortraitAdditionalArgs => _portrait?.AdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string PortraitTextureProviderName => _portrait?.TextureProviderName ?? string.Empty;
        public void ExecuteOpen() => _open?.Invoke();
    }
}
