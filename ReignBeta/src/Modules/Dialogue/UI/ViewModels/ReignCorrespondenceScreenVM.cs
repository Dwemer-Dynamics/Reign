using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.World;
using ReignBeta.Shared;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignCorrespondenceScreenVM : ViewModel, ReignBeta.UI.IReignActionHost
    {
        public IReadOnlyList<Hero> ActionParticipants => _selected?.Hero == null ? new Hero[0] : new[] { _selected.Hero };
        public bool ActionBusy => IsBusy || _calibrationMode;
        private ReignActionComposerVM _composer;
        public ReignActionComposerVM Composer => _composer ?? (_composer = new ReignActionComposerVM(() => OnPropertyChanged(nameof(InputText))));
        [DataSourceProperty] public string ActionComposerId => Composer.Id;
        public Reign.Core.Contracts.Actions.ActionAgreement ActionDraft => Composer.State.HasValidAttachment ? Composer.State.Draft : null;
        private readonly Action _close;
        private readonly string _preselectedHeroId;
        private readonly bool _calibrationMode;
        private List<ReignLetter> _letters = new List<ReignLetter>();
        private Dictionary<string, ReignCorrespondenceContactState> _contactStates
            = new Dictionary<string, ReignCorrespondenceContactState>(StringComparer.OrdinalIgnoreCase);
        private int _threadLoadVersion;
        private readonly SemaphoreSlim _threadRefreshGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _loadCancellation = new CancellationTokenSource();
        private readonly Dictionary<string, ReignChatLineVM> _letterLines = new Dictionary<string, ReignChatLineVM>();
        private ReignCorrespondenceContactVM _selected;
        private string _selectedName = "Select a correspondent";
        private string _selectedSubtitle = "";
        private string _busyText = "Loading correspondence...";
        private bool _isBusy = true;
        private int _chatScrollVersion;
        private volatile bool _finalized;

        public ReignCorrespondenceScreenVM(Action close, Hero preselected = null, bool calibrationMode = false)
        {
            _close = close;
            _preselectedHeroId = preselected?.StringId ?? string.Empty;
            _calibrationMode = calibrationMode;
            Contacts = new MBBindingList<ReignCorrespondenceContactVM>();
            ChatLines = new MBBindingList<ReignChatLineVM>();
            if (_calibrationMode) InitializeCalibration(preselected);
            else _ = LoadAsync();
        }

        public MBBindingList<ReignCorrespondenceContactVM> Contacts { get; }
        public MBBindingList<ReignChatLineVM> ChatLines { get; }

        [DataSourceProperty]
        public string SelectedName
        {
            get { return _selectedName; }
            set { if (value != _selectedName) { _selectedName = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public string SelectedSubtitle
        {
            get { return _selectedSubtitle; }
            set { if (value != _selectedSubtitle) { _selectedSubtitle = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public string InputText
        {
            get { return Composer.State.Text; }
            set { Composer.SetText(value); }
        }

        [DataSourceProperty]
        public string BusyText
        {
            get { return _busyText; }
            set { if (value != _busyText) { _busyText = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public bool IsBusy
        {
            get { return _isBusy; }
            set { if (value != _isBusy) { _isBusy = value; OnPropertyChangedWithValue(value); } }
        }

        [DataSourceProperty]
        public int ChatScrollVersion
        {
            get { return _chatScrollVersion; }
            set { if (value != _chatScrollVersion) { _chatScrollVersion = value; OnPropertyChangedWithValue(value); } }
        }

        public async void ExecuteSend()
        {
            if (_finalized) return;
            if (_calibrationMode)
            {
                BusyText = "Calibration mode does not send letters.";
                return;
            }
            string body = InputText ?? string.Empty;
            if (_selected?.Hero == null || string.IsNullOrWhiteSpace(body) || IsBusy)
            {
                BusyText = _selected == null ? "Select one character." : "Write a letter before sending.";
                return;
            }

            IsBusy = true;
            BusyText = "Dispatching letter...";
            ReignLetterSendResult result = await SendProductionLetterAsync(
                _selected.Hero,
                body,
                string.Empty, ActionDraft == null ? null : ReignBeta.Actions.ReignActionService.Copy(ActionDraft));
            await ReignMainThread.InvokeAsync(() =>
            {
                if (_finalized) return;
                if (result.Ok)
                {
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Letter dispatched to " + _selected.Name + "."));
                    _close?.Invoke();
                    return;
                }

                IsBusy = false;
                BusyText = "Could not send: " + result.Error;
            });
        }

        public static async Task<ReignLetterSendResult> SendProductionLetterAsync(
            Hero recipient,
            string body,
            string correlationId,
            Reign.Core.Contracts.Actions.ActionAgreement actionDraft = null)
        {
            ReignLetterSendResult result = await ReignServerClient.SendLetterAsync(
                recipient,
                body,
                correlationId, actionDraft).ConfigureAwait(false);
            if (!result.Ok || result.QueuedActions == null || result.QueuedActions.Count == 0)
                return result;

            int queued = await ReignMainThread.InvokeAsync(() =>
                ReignAICampaignBehavior.Instance?.EnqueueAndExecuteServerActions(
                    result.QueuedActions) ?? 0).ConfigureAwait(false);
            ReignLog.Info(
                "Immediate dispatched-letter action import actions="
                + result.QueuedActions.Count
                + " queued=" + queued
                + " correlation=" + (result.CorrelationId ?? string.Empty));
            return result;
        }

        public void ExecuteCancel()
        {
            CancelPendingLoads();
            _close?.Invoke();
        }

        public void CancelPendingLoads()
        {
            _finalized = true;
            _loadCancellation.Cancel();
        }

        public override void OnFinalize()
        {
            CancelPendingLoads();
            ReignActionBuilder.CloseFor(this);
            _composer?.Dispose();
            _finalized = true;
            base.OnFinalize();
        }

        private async Task LoadAsync()
        {
            List<Hero> heroes = await ReignMainThread.InvokeAsync(ReignServerClient.GetKnownCorrespondenceContacts);
            if (_finalized) return;
            ReignCorrespondenceSnapshot snapshot = await ReignServerClient.GetCorrespondenceAsync(heroes, summaryOnly: true,
                isCurrentRequest: () => Task.FromResult(!_finalized));
            if (_finalized) return;
            foreach (string heroId in snapshot.ContactStates.Keys.Concat((snapshot.Letters ?? new List<ReignLetter>())
                .Where(x => string.Equals(x.Source, "campaign_command_urgent_report", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Source, "npc_introduction", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => new[] { x.SenderId, x.RecipientId }))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (heroes.Any(x => string.Equals(x.StringId, heroId, StringComparison.OrdinalIgnoreCase))) continue;
                if (_finalized) return;
                Hero hero = await ReignMainThread.InvokeAsync(() => ReignObjectResolver.FindHero(heroId));
                if (hero != null && hero != Hero.MainHero && ReignConversationEligibility.IsAdultLivingNpc(hero))
                    heroes.Add(hero);
            }
            await ReignMainThread.InvokeAsync(() =>
            {
                if (_finalized) return;
                Contacts.Clear();
                foreach (Hero hero in heroes)
                {
                    Contacts.Add(new ReignCorrespondenceContactVM(hero, SelectContact));
                }

                _letters = snapshot.Letters ?? new List<ReignLetter>();
                _contactStates = snapshot.ContactStates;
                RefreshContactState();
                ReignCorrespondenceContactVM initial = Contacts.FirstOrDefault(x => x.Hero?.StringId == _preselectedHeroId)
                    ?? Contacts.FirstOrDefault(x => x.UnreadCount > 0)
                    ?? Contacts.FirstOrDefault();
                IsBusy = false;
                BusyText = snapshot.Ok ? string.Empty : "Could not load correspondence: " + snapshot.Error;
                if (initial != null) SelectContact(initial);
                else ChatLines.Add(new ReignChatLineVM("", "No known living characters are available for correspondence.", "system"));
            });
        }

        private void InitializeCalibration(Hero contactHero)
        {
            IsBusy = false;
            BusyText = "Provider-free UI calibration fixture";
            if (contactHero == null)
            {
                ChatLines.Add(new ReignChatLineVM("", "No eligible calibration correspondent is available.", "system"));
                return;
            }

            string playerId = Hero.MainHero?.StringId ?? "ui_calibration_player";
            string playerName = Hero.MainHero?.Name?.ToString() ?? "Your Majesty";
            string contactId = contactHero.StringId ?? "ui_calibration_contact";
            string contactName = contactHero.Name?.ToString() ?? "Council Correspondent";
            _letters = new List<ReignLetter>
            {
                new ReignLetter
                {
                    LetterId = "ui_calibration_letter_1",
                    ThreadId = "ui_calibration_correspondence",
                    SenderId = contactId,
                    SenderName = contactName,
                    RecipientId = playerId,
                    RecipientName = playerName,
                    Body = "The road remains open, and your sealed reply may travel with tomorrow's courier.",
                    Status = "read",
                    Source = "ui_calibration",
                    DispatchDay = 1d
                },
                new ReignLetter
                {
                    LetterId = "ui_calibration_letter_2",
                    ThreadId = "ui_calibration_correspondence",
                    SenderId = playerId,
                    SenderName = playerName,
                    RecipientId = contactId,
                    RecipientName = contactName,
                    Body = "Keep me informed of any material change before the council next meets.",
                    Status = "in_transit",
                    Source = "ui_calibration",
                    DispatchDay = 2d
                }
            };
            ReignCorrespondenceContactVM contact = new ReignCorrespondenceContactVM(contactHero, SelectContact);
            Contacts.Add(contact);
            RefreshContactState();
            SelectContact(contact);
        }

        private void SelectContact(ReignCorrespondenceContactVM contact)
        {
            if (_finalized || contact == null || contact == _selected) return;
            foreach (ReignCorrespondenceContactVM row in Contacts) row.IsSelected = row == contact;
            _selected = contact;
            SelectedName = contact.Name;
            SelectedSubtitle = contact.Subtitle;
            if (_calibrationMode) RebuildThread();
            else
            {
                _letterLines.Clear();
                ChatLines.Clear();
                ChatLines.Add(new ReignChatLineVM("", "Loading letters...", "system"));
                _ = LoadSelectedThreadAsync(contact, ++_threadLoadVersion);
            }
        }

        private async Task LoadSelectedThreadAsync(ReignCorrespondenceContactVM contact, int version)
        {
            IsBusy = true;
            BusyText = "Loading letters...";
            List<Hero> heroes = Contacts.Select(row => row.Hero).Where(hero => hero != null).ToList();
            // Keep summary reads after earlier read acknowledgments, so fast
            // contact switching cannot restore an obsolete unread badge.
            try { await _threadRefreshGate.WaitAsync(_loadCancellation.Token); }
            catch (OperationCanceledException) { return; }
            try
            {
                if (await ReignMainThread.InvokeAsync(() => _finalized || version != _threadLoadVersion)) return;
                ReignCorrespondenceSnapshot snapshot = await ReignServerClient.GetCorrespondenceAsync(heroes, contact.Hero.StringId,
                    isCurrentRequest: () => Task.FromResult(!_finalized && version == _threadLoadVersion));
                bool loaded = await ReignMainThread.InvokeAsync(() =>
                {
                    if (_finalized || version != _threadLoadVersion) return false;
                    IsBusy = false;
                    BusyText = snapshot.Ok ? string.Empty : "Could not load letters: " + snapshot.Error;
                    if (!snapshot.Ok) return false;
                    _letters = snapshot.Letters.Where(IsSelectedThread).ToList();
                    _contactStates = snapshot.ContactStates;
                    RefreshContactState();
                    RebuildThread();
                    return true;
                });
                if (loaded) await MarkSelectedLettersReadAsync(contact, version);
            }
            finally { _threadRefreshGate.Release(); }
        }

        private void RebuildThread()
        {
            _letterLines.Clear();
            ChatLines.Clear();
            string playerId = Hero.MainHero?.StringId ?? string.Empty;
            List<ReignLetter> thread = _letters.Where(IsSelectedThread).OrderBy(x => x.DispatchDay).ToList();
            if (thread.Count == 0)
            {
                ChatLines.Add(new ReignChatLineVM("", "No letters exchanged yet.", "system"));
            }
            else
            {
                foreach (ReignLetter letter in thread)
                {
                    bool sent = letter.SenderId == playerId;
                    var line = new ReignChatLineVM(LetterSpeaker(letter), letter.Body, sent ? "player" : "npc");
                    ChatLines.Add(line);
                    _letterLines[letter.LetterId] = line;
                }
            }

            ChatScrollVersion++;
        }

        private async Task MarkSelectedLettersReadAsync(ReignCorrespondenceContactVM contact, int version)
        {
            string playerId = Hero.MainHero?.StringId ?? string.Empty;
            List<ReignLetter> unread = await ReignMainThread.InvokeAsync(() =>
                _finalized || version != _threadLoadVersion || contact.Hero == null ? new List<ReignLetter>()
                    : _letters.Where(x => IsSelectedThread(x) && x.RecipientId == playerId && x.Status == "delivered").ToList());
            if (unread.Count == 0 || _finalized) return;
            try
            {
                if (!await ReignServerClient.MarkLettersReadAsync(unread.Select(letter => letter.LetterId),
                    () => Task.FromResult(!_finalized && version == _threadLoadVersion))) return;
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (_finalized) return;
                    foreach (ReignLetter letter in unread)
                    {
                        letter.Status = "read";
                        if (version == _threadLoadVersion && _letterLines.TryGetValue(letter.LetterId, out var line))
                            line.Speaker = LetterSpeaker(letter);
                    }
                    if (_contactStates.TryGetValue(contact.Hero.StringId, out var state))
                    {
                        state.UnreadCount = Math.Max(0, state.UnreadCount - unread.Count);
                        if (state.Status == "delivered" && state.UnreadCount == 0) state.Status = "read";
                    }
                    RefreshContactState();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ReignLog.Warn("Letter read update failed: " + ex.Message); }
        }

        private static string LetterSpeaker(ReignLetter letter)
        {
            bool sent = letter.SenderId == (Hero.MainHero?.StringId ?? string.Empty);
            string state = letter.Status == "in_transit" ? "in transit" : letter.Status;
            return (sent ? Hero.MainHero?.Name?.ToString() : letter.SenderName) + " - Day "
                + letter.DispatchDay.ToString("0.##") + " (" + state + ")";
        }

        private bool IsSelectedThread(ReignLetter letter)
        {
            string playerId = Hero.MainHero?.StringId ?? string.Empty;
            string contactId = _selected?.Hero?.StringId ?? string.Empty;
            return (letter.SenderId == playerId && letter.RecipientId == contactId)
                || (letter.SenderId == contactId && letter.RecipientId == playerId);
        }

        private void RefreshContactState()
        {
            string playerId = Hero.MainHero?.StringId ?? string.Empty;
            foreach (ReignCorrespondenceContactVM contact in Contacts)
            {
                string id = contact.Hero?.StringId ?? string.Empty;
                if (_contactStates.TryGetValue(id, out var state))
                {
                    contact.UnreadCount = state.UnreadCount;
                    contact.HasCorrespondence = state.HasCorrespondence;
                    contact.Status = state.Status.Replace('_', ' ');
                    continue;
                }
                List<ReignLetter> thread = _letters.Where(x => (x.SenderId == playerId && x.RecipientId == id) || (x.SenderId == id && x.RecipientId == playerId)).ToList();
                contact.UnreadCount = thread.Count(x => x.RecipientId == playerId && x.Status == "delivered");
                contact.HasCorrespondence = thread.Count > 0;
                ReignLetter latest = thread.OrderByDescending(x => x.DispatchDay).FirstOrDefault();
                contact.Status = latest == null ? "No correspondence" : latest.Status.Replace('_', ' ');
            }
            var ordered = CorrespondenceContactOrdering.Order(Contacts, row => row.UnreadCount,
                row => row.HasCorrespondence, row => row.Name, row => row.Hero?.StringId ?? "").ToList();
            if (!Contacts.SequenceEqual(ordered))
            {
                // Native Sorted notifications move existing widgets; Reset
                // recreates every portrait and can disturb keyboard focus.
                var ranks = ordered.Select((contact, rank) => new { contact, rank }).ToDictionary(x => x.contact, x => x.rank);
                Contacts.Sort(Comparer<ReignCorrespondenceContactVM>.Create((left, right) => Math.Sign(ranks[left] - ranks[right])));
            }
        }
    }
}
