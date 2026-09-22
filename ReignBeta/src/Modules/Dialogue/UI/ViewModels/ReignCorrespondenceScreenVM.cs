using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignCorrespondenceScreenVM : ViewModel
    {
        private readonly Action _close;
        private readonly string _preselectedHeroId;
        private readonly bool _calibrationMode;
        private List<ReignLetter> _letters = new List<ReignLetter>();
        private ReignCorrespondenceContactVM _selected;
        private string _selectedName = "Select a correspondent";
        private string _selectedSubtitle = "";
        private string _inputText = string.Empty;
        private string _busyText = "Loading correspondence...";
        private bool _isBusy = true;
        private int _chatScrollVersion;
        private bool _finalized;

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
            get { return _inputText; }
            set { if (value != _inputText) { _inputText = value; OnPropertyChangedWithValue(value); } }
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
                string.Empty);
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
            string correlationId)
        {
            ReignLetterSendResult result = await ReignServerClient.SendLetterAsync(
                recipient,
                body,
                correlationId).ConfigureAwait(false);
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
            _close?.Invoke();
        }

        public override void OnFinalize()
        {
            _finalized = true;
            base.OnFinalize();
        }

        private async Task LoadAsync()
        {
            List<Hero> heroes = await ReignMainThread.InvokeAsync(ReignServerClient.GetKnownCorrespondenceContacts);
            ReignCorrespondenceSnapshot snapshot = await ReignServerClient.GetCorrespondenceAsync(heroes);
            foreach (string heroId in (snapshot.Letters ?? new List<ReignLetter>())
                .Where(x => string.Equals(x.Source, "campaign_command_urgent_report", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => new[] { x.SenderId, x.RecipientId })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (heroes.Any(x => string.Equals(x.StringId, heroId, StringComparison.OrdinalIgnoreCase))) continue;
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
                RefreshContactState();
                ReignCorrespondenceContactVM initial = Contacts.FirstOrDefault(x => x.Hero?.StringId == _preselectedHeroId)
                    ?? Contacts.FirstOrDefault(x => x.UnreadCount > 0)
                    ?? Contacts.FirstOrDefault();
                if (initial != null) SelectContact(initial);
                else ChatLines.Add(new ReignChatLineVM("", "No known living characters are available for correspondence.", "system"));
                IsBusy = false;
                BusyText = snapshot.Ok ? string.Empty : "Could not load correspondence: " + snapshot.Error;
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
            if (contact == null) return;
            foreach (ReignCorrespondenceContactVM row in Contacts) row.IsSelected = row == contact;
            _selected = contact;
            SelectedName = contact.Name;
            SelectedSubtitle = contact.Subtitle;
            RebuildThread();
            if (!_calibrationMode) _ = MarkSelectedLettersReadAsync();
        }

        private void RebuildThread()
        {
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
                    string state = letter.Status == "in_transit" ? "in transit" : letter.Status;
                    string speaker = (sent ? Hero.MainHero?.Name?.ToString() : letter.SenderName) + " - Day " + letter.DispatchDay.ToString("0.##") + " (" + state + ")";
                    ChatLines.Add(new ReignChatLineVM(speaker, letter.Body, sent ? "player" : "npc"));
                }
            }

            ChatScrollVersion++;
        }

        private async Task MarkSelectedLettersReadAsync()
        {
            if (_selected?.Hero == null) return;
            string playerId = Hero.MainHero?.StringId ?? string.Empty;
            List<ReignLetter> unread = _letters.Where(x => IsSelectedThread(x) && x.RecipientId == playerId && x.Status == "delivered").ToList();
            foreach (ReignLetter letter in unread)
            {
                try
                {
                    if (await ReignServerClient.MarkLetterReadAsync(letter.LetterId)) letter.Status = "read";
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Letter read update failed: " + ex.Message);
                }
            }

            await ReignMainThread.InvokeAsync(() =>
            {
                if (_finalized) return;
                RefreshContactState();
                RebuildThread();
            });
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
                List<ReignLetter> thread = _letters.Where(x => (x.SenderId == playerId && x.RecipientId == id) || (x.SenderId == id && x.RecipientId == playerId)).ToList();
                contact.UnreadCount = thread.Count(x => x.RecipientId == playerId && x.Status == "delivered");
                ReignLetter latest = thread.OrderByDescending(x => x.DispatchDay).FirstOrDefault();
                contact.Status = latest == null ? "No correspondence" : latest.Status.Replace('_', ' ');
            }
        }
    }
}
