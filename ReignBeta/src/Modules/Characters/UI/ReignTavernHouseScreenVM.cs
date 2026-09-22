using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Shared.Characters;
using ReignBeta.Settings;
using ReignBeta.UI.EventArt;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignTavernHouseScreenVM : ViewModel
    {
        private readonly Action _close;
        private readonly ReignTavernHouseCampaignBehavior _house;
        private readonly List<Hero> _staff;
        private readonly Hero _madam;
        private readonly bool _fixture;
        private string _conversationId = Guid.NewGuid().ToString("N");
        private TavernHouseVisitQuote _quote;
        private JObject _recruitmentAgreement;
        private TavernHouseVisitReceipt _receipt;
        private bool _finalized, _busy, _imageBusy;
        private int _transcriptRevision, _chatScrollVersion;
        private string _inputText = "", _statusText = "", _sceneImageId = "", _imageStatus = "A scene will appear after you agree on a visit.";
        private ReignPartyChatMemberVM _preview;
        private JObject _pendingTurn;
        private string _pendingRequestId;
        private List<Hero> _pendingSpeakers;
        private int _pendingSpeakerIndex;
        private float _portraitPoll;
        public Settlement Town { get; }
        [DataSourceProperty] public MBBindingList<ReignPartyChatMemberVM> Madam { get; } = new MBBindingList<ReignPartyChatMemberVM>();
        [DataSourceProperty] public MBBindingList<ReignPartyChatMemberVM> Workers { get; } = new MBBindingList<ReignPartyChatMemberVM>();
        [DataSourceProperty] public MBBindingList<ReignChatLineVM> ChatLines { get; } = new MBBindingList<ReignChatLineVM>();
        [DataSourceProperty] public bool IsOverlayVisible => !_finalized;
        [DataSourceProperty] public string Title => "Visit the Madam";
        [DataSourceProperty] public string TownName => Town?.Name?.ToString() ?? "";
        [DataSourceProperty] public bool IsBusy => _busy;
        [DataSourceProperty] public bool IsVisiting => _receipt?.ServerConfirmed == true && !_receipt.Ended;
        [DataSourceProperty] public bool HasQuote => _recruitmentAgreement != null || _quote != null && _receipt == null;
        [DataSourceProperty] public bool PaymentConfirmationPending => _receipt?.Paid == true && !_receipt.ServerConfirmed;
        [DataSourceProperty] public bool CanAccept => !_fixture && !_busy && (HasQuote || _receipt?.Paid == true && !_receipt.ServerConfirmed);
        [DataSourceProperty] public bool CanNegotiate => !_busy && HasQuote;
        [DataSourceProperty] public bool InputEnabled => !_fixture && !_busy && _pendingTurn == null && (_receipt == null || IsVisiting);
        [DataSourceProperty] public bool CanSend => !_busy && (InputEnabled || _pendingTurn != null);
        [DataSourceProperty] public bool CanLookAgain => IsVisiting && ImagesEnabled && !_busy && !_imageBusy && _pendingTurn == null;
        [DataSourceProperty] public bool HasSceneImage => !string.IsNullOrEmpty(_sceneImageId);
        [DataSourceProperty] public bool ShowScenePlaceholder => !HasSceneImage;
        [DataSourceProperty] public string SceneImageId => _sceneImageId;
        [DataSourceProperty] public string ImageStatus => _imageStatus;
        [DataSourceProperty] public bool HasImageStatus => !string.IsNullOrWhiteSpace(_imageStatus);
        [DataSourceProperty] public string StatusText => _statusText;
        [DataSourceProperty] public int ChatScrollVersion => _chatScrollVersion;
        [DataSourceProperty] public string SendText => _pendingTurn == null ? "SEND" : "RETRY";
        [DataSourceProperty] public string AcceptText => _recruitmentAgreement != null ? "ACCEPT RECRUITMENT"
            : _receipt?.Paid == true ? "RETRY CONFIRMATION" : "ACCEPT AND PAY";
        [DataSourceProperty] public string QuoteText => _recruitmentAgreement != null
            ? _recruitmentAgreement.Value<string>("displayName") + " offers to join your clan permanently.\nAgreed payment: "
                + _recruitmentAgreement.Value<int>("agreedGold") + " denars.\nAccepting ends this tavern visit."
            : _quote == null ? "" :
            string.Join("\n", _quote.Charges.Select(c => c.DisplayName + " — " + c.Gold + " denars")) + "\nTotal: " + _quote.TotalGold + " denars";
        [DataSourceProperty] public string VisitSummary => IsVisiting
            ? "Private visit · " + string.Join(", ", _receipt.ParticipantHeroIds.Select(id => _staff.FirstOrDefault(h => h.StringId == id)?.Name?.ToString() ?? id))
            : _receipt?.Paid == true ? "Payment complete · confirmation pending" : "Speak with the madam to agree on company and payment.";
        [DataSourceProperty] public string InputText { get => _inputText; set { _inputText = value ?? ""; OnPropertyChangedWithValue(_inputText); } }
        [DataSourceProperty] public bool ImagesEnabled => ReignBetaSettings.Instance?.TavernHouseImageGeneration ?? true;
        [DataSourceProperty] public string ImageToggleText => ImagesEnabled ? "Scene images: ON" : "Scene images: OFF";
        [DataSourceProperty] public bool IsPortraitPreviewVisible => _preview != null;
        [DataSourceProperty] public string PortraitPreviewName => _preview?.Name ?? "";
        [DataSourceProperty] public string PortraitPreviewId => _preview?.PortraitId ?? "";
        [DataSourceProperty] public string PortraitPreviewAdditionalArgs => _preview?.PortraitAdditionalArgs ?? "";
        [DataSourceProperty] public string PortraitPreviewTextureProviderName => _preview?.PortraitTextureProviderName ?? "";
        [DataSourceProperty] public string PortraitPreviewCacheKey => _preview?.PortraitCacheKey ?? "";

        public ReignTavernHouseScreenVM(Settlement town, Action close, bool calibrationFixture = false)
        {
            Town = town; _close = close; _fixture = calibrationFixture;
            if (_fixture)
            {
                _staff = Hero.AllAliveHeroes.Where(h => !h.IsChild && h != Hero.MainHero).Take(6).ToList();
                _madam = _staff.FirstOrDefault();
                foreach (Hero hero in _staff)
                {
                    var member = new ReignPartyChatMemberVM(hero, false, PreviewPortrait, PreviewPortrait, PreviewPortrait, null);
                    (hero == _madam ? Madam : Workers).Add(member);
                }
                _statusText = "Calibration preview · no campaign actions or providers";
                AddLine("Madam", "Welcome. Tell me whose company interests you, and we can discuss the terms.", "npc");
                AddLine("Player", "I would like to hear about the house and its people.", "player");
                return;
            }
            _house = ReignTavernHouseCampaignBehavior.Instance ?? throw new InvalidOperationException("The tavern house is not ready.");
            _house.EnsureTown(town);
            _staff = _house.GetStaff(town).ToList(); _madam = _house.GetMadam(town);
            if (_madam == null) throw new InvalidOperationException("No madam is available at this town.");
            foreach (Hero hero in _staff)
            {
                var member = new ReignPartyChatMemberVM(hero, false, PreviewPortrait, PreviewPortrait,
                    PreviewPortrait, GeneratePortrait);
                (hero == _madam ? Madam : Workers).Add(member);
            }
            _receipt = _house.GetOpenVisit(town);
            if (_receipt != null && !string.IsNullOrWhiteSpace(_receipt.ConversationId)) _conversationId = _receipt.ConversationId;
            Refresh();
        }

        public void Begin()
        {
            if (_fixture) return;
            if (_receipt?.Paid == true) { _ = ConfirmAsync(); return; }
            StartTurn("");
        }

        public void Tick(float dt)
        {
            _portraitPoll += dt;
            if (_portraitPoll < 1f) return;
            _portraitPoll = 0;
            foreach (var member in Madam.Concat(Workers))
            {
                bool selected = _receipt?.ParticipantHeroIds.Contains(member.Hero.StringId) == true;
                member.Status = PortraitCache.IsPending(member.PortraitCacheKey) ? "Portrait pending"
                    : !PortraitCache.ExistsOnDisk(member.PortraitCacheKey) ? "Use the eye for a portrait"
                    : selected && IsVisiting ? "Visiting with you" : member.Hero == _madam ? "Madam" : "House worker";
            }
        }

        public void ExecuteSend()
        {
            if (!CanSend || _finalized) return;
            if (_pendingTurn != null) { _ = ContinueTurnAsync(); return; }
            string text = InputText.Trim();
            if (text.Length == 0) return;
            InputText = ""; AddLine(Hero.MainHero.Name.ToString(), text, "player");
            StartTurn(text);
        }

        private void StartTurn(string text)
        {
            _quote = null; _recruitmentAgreement = null;
            _pendingTurn = ReignServerClient.BuildTavernHousePayload(Town, _conversationId);
            _pendingTurn["playerText"] = text;
            _pendingTurn["playerTurnId"] = Guid.NewGuid().ToString("N");
            _pendingRequestId = null;
            _pendingSpeakers = IsVisiting
                ? _receipt.ParticipantHeroIds.Select(id => _staff.FirstOrDefault(h => h.StringId == id)).Where(h => h != null).ToList()
                : new List<Hero> { _madam };
            _pendingSpeakerIndex = 0;
            _ = ContinueTurnAsync();
        }

        private async Task ContinueTurnAsync()
        {
            _busy = true; _statusText = "Waiting for a reply…"; Refresh();
            try
            {
                while (_pendingSpeakerIndex < _pendingSpeakers.Count)
                {
                    int index = _pendingSpeakerIndex;
                    Hero speaker = _pendingSpeakers[index];
                    JObject request = (JObject)_pendingTurn.DeepClone();
                    request["speakerHeroStringId"] = speaker.StringId;
                    request["requestId"] = _pendingRequestId ?? request.Value<string>("playerTurnId") + "_" + index;
                    JObject response = await ReignServerClient.PostTavernHouseAsync("respond", request).ConfigureAwait(false);
                    bool next = await ReignMainThread.InvokeAsync(() =>
                    {
                        if (_finalized) return false;
                        if (response.Value<bool?>("ok") != true) { _statusText = Error(response); return false; }
                        string reply = response.Value<string>("reply") ?? "";
                        if (reply.Length > 0) AddLine(speaker.Name.ToString(), reply, "npc");
                        _transcriptRevision = response.Value<int?>("transcriptRevision") ?? _transcriptRevision;
                        _quote = response["quote"]?.ToObject<TavernHouseVisitQuote>();
                        if (response["recruitmentAgreement"] is JObject recruitment) _recruitmentAgreement = recruitment;
                        _statusText = response.Value<string>("quoteError") ?? "";
                        _pendingRequestId = null;
                        _pendingSpeakerIndex++;
                        return true;
                    }).ConfigureAwait(false);
                    if (!next) return;
                }
                await ReignMainThread.InvokeAsync(() => { _pendingTurn = null; _pendingSpeakers = null; }).ConfigureAwait(false);
            }
            catch (Exception ex) { await SetErrorAsync(ex.Message).ConfigureAwait(false); }
            finally { await ReignMainThread.InvokeAsync(() => { _busy = false; Refresh(); }).ConfigureAwait(false); }
        }

        public void ExecuteAcceptAgreement()
        {
            if (!CanAccept || _finalized) return;
            if (_recruitmentAgreement != null)
            {
                Hero candidate = _staff.FirstOrDefault(h => h.StringId == _recruitmentAgreement.Value<string>("heroId"));
                if (!_house.TryRecruit(candidate, _recruitmentAgreement.Value<int>("agreedGold"),
                    _recruitmentAgreement.Value<string>("agreementId"), _recruitmentAgreement.Value<bool?>("consentConfirmed") == true, out string reason))
                { _statusText = reason; Refresh(); return; }
                _recruitmentAgreement = null;
                _close?.Invoke();
                return;
            }
            if (_receipt == null)
            {
                if (!_house.ConfirmVisit(_quote, out var receipt, out string reason))
                { _statusText = reason; Refresh(); return; }
                _receipt = receipt;
                // Save the durable server scope before any asynchronous confirmation can fail.
                _receipt.ConversationId = _conversationId;
            }
            _ = ConfirmAsync();
        }

        private async Task ConfirmAsync()
        {
            bool resumePending = false;
            _busy = true; _statusText = "Confirming your visit…"; Refresh();
            try
            {
                JObject request = ReignServerClient.BuildTavernHousePayload(Town, _conversationId);
                var serializer = JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
                JObject receipt = JObject.FromObject(_receipt, serializer);
                receipt["nativePaymentConfirmed"] = _receipt.Paid;
                request["receipt"] = receipt;
                JObject response = await ReignServerClient.PostTavernHouseAsync("confirm", request).ConfigureAwait(false);
                bool confirmed = await ReignMainThread.InvokeAsync(() =>
                {
                    if (_finalized) return false;
                    if (response.Value<bool?>("ok") != true) { _statusText = Error(response); return false; }
                    _house.MarkServerConfirmed(_receipt.VisitId, _conversationId);
                    _quote = null; _statusText = "";
                    if (ChatLines.Count == 0 && response["transcript"] is JArray transcript)
                        foreach (JObject line in transcript.OfType<JObject>())
                            AddLine(line.Value<string>("name") ?? "", line.Value<string>("text") ?? "", line.Value<string>("role") ?? "system");
                    AddLine("Visit", "Your agreed company joins you. Payment: " + _receipt.PaidGold + " denars.", "system");
                    _transcriptRevision = response.Value<int?>("transcriptRevision") ?? _transcriptRevision;
                    resumePending = RestorePendingTurn(response["pendingTurn"] as JObject);
                    return true;
                }).ConfigureAwait(false);
                if (confirmed) await ReignMainThread.InvokeAsync(() => { if (ImagesEnabled) _ = GenerateSceneAsync("arrival"); }).ConfigureAwait(false);
            }
            catch (Exception ex) { await SetErrorAsync(ex.Message).ConfigureAwait(false); }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    _busy = false; Refresh();
                    if (resumePending && !_finalized) _ = ContinueTurnAsync();
                }).ConfigureAwait(false);
            }
        }

        private bool RestorePendingTurn(JObject pending)
        {
            if (pending == null) return false;
            string turnId = pending.Value<string>("playerTurnId"), requestId = pending.Value<string>("requestId");
            string speakerId = pending.Value<string>("speakerHeroStringId"), text = pending.Value<string>("playerText") ?? "";
            var speakers = _receipt.ParticipantHeroIds.Select(id => _staff.FirstOrDefault(h => h.StringId == id)).ToList();
            int index = speakers.FindIndex(h => h?.StringId == speakerId);
            if (string.IsNullOrWhiteSpace(turnId) || string.IsNullOrWhiteSpace(requestId) || index < 0 || speakers.Any(h => h == null))
                throw new InvalidOperationException("The saved reply cannot be resumed with the current visit participants.");
            _pendingTurn = ReignServerClient.BuildTavernHousePayload(Town, _conversationId);
            _pendingTurn["playerText"] = text; _pendingTurn["playerTurnId"] = turnId;
            _pendingRequestId = requestId; _pendingSpeakers = speakers; _pendingSpeakerIndex = index;
            if (text.Length > 0 && pending.Value<bool?>("playerRecordedInTranscript") != true)
                AddLine(Hero.MainHero.Name.ToString(), text, "player");
            return true;
        }

        public void ExecuteContinueNegotiating()
        {
            if (!CanNegotiate) return;
            _quote = null; _recruitmentAgreement = null; _statusText = "Continue the conversation to discuss different terms."; Refresh();
        }

        public void ExecuteToggleImages()
        {
            if (_fixture || ReignBetaSettings.Instance == null) return;
            ReignBetaSettings.Instance.TavernHouseImageGeneration = !ImagesEnabled;
            Refresh();
        }

        public void ExecuteLookAgain()
        {
            if (!CanLookAgain) return;
            _ = GenerateSceneAsync("look_again");
        }

        private async Task GenerateSceneAsync(string kind)
        {
            if (_imageBusy || _finalized || !ImagesEnabled || !IsVisiting) return;
            _imageBusy = true; _imageStatus = "Picturing your company…"; Refresh();
            JObject request = ReignServerClient.BuildTavernHousePayload(Town, _conversationId);
            request["visitId"] = _receipt.VisitId; request["kind"] = kind;
            request["imagesEnabled"] = ImagesEnabled; request["expectedTranscriptRevision"] = _transcriptRevision;
            try
            {
                JObject response = await ReignServerClient.PostTavernHouseAsync("scene", request).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (_finalized) return;
                    if (response.Value<bool?>("ok") != true) { _imageStatus = Error(response); return; }
                    if (kind == "look_again" && response.Value<int?>("transcriptRevision") != _transcriptRevision)
                    { _imageStatus = "The conversation moved on while this image arrived. Look Again for the current scene."; return; }
                    if (!ImagesEnabled) { _imageStatus = "Scene images are off."; return; }
                    string imageId = ReignEventArtTextureFactory.StoreTavernHouseScene(response.Value<string>("cacheKey"),
                        Convert.FromBase64String(response.Value<string>("imageBase64") ?? ""));
                    if (ReignEventArtTextureFactory.GetOrBuild(imageId) == null) { _imageStatus = "The image could not be displayed. Look Again to retry."; return; }
                    _sceneImageId = imageId; _imageStatus = "";
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { await ReignMainThread.InvokeAsync(() => _imageStatus = ex.Message).ConfigureAwait(false); }
            finally { await ReignMainThread.InvokeAsync(() => { _imageBusy = false; Refresh(); }).ConfigureAwait(false); }
        }

        private void PreviewPortrait(ReignPartyChatMemberVM member) { _preview = member; Refresh(); }
        private void GeneratePortrait(ReignPartyChatMemberVM member)
        {
            if (_fixture || member?.Hero == null) return;
            ReignPortraitBridge.TryRequestPortrait(member.Hero, out string message, out uint color);
            _statusText = message; Refresh();
        }
        public void ExecuteClosePortraitPreview() { _preview = null; Refresh(); }
        public void ExecuteNoOp() { }
        public void ExecuteClose()
        {
            // Late callbacks are ignored; a paid receipt awaiting confirmation remains recoverable.
            _close?.Invoke();
        }
        public override void OnFinalize()
        {
            if (_finalized) return;
            _finalized = true;
            // A debit awaiting server confirmation stays open and resumes with the original receipt.
            if (!_fixture && (_receipt == null || _receipt.ServerConfirmed))
            {
                JObject request = ReignServerClient.BuildTavernHousePayload(Town, _conversationId);
                if (_receipt != null) { request["visitId"] = _receipt.VisitId; _house.EndVisit(_receipt.VisitId); }
                _ = CloseServerAsync(request);
            }
            foreach (var member in Madam.Concat(Workers)) member.OnFinalize();
            base.OnFinalize();
        }
        private static async Task CloseServerAsync(JObject request)
        {
            try { await ReignServerClient.PostTavernHouseAsync("close", request).ConfigureAwait(false); }
            catch (Exception ex) { ReignLog.Warn("Tavern visit close: " + ex.Message); }
        }
        private Task SetErrorAsync(string error) => ReignMainThread.InvokeAsync(() => _statusText = error);
        private static string Error(JObject response) => response?.Value<string>("error") ?? "The server did not complete this request. Retry when it is available.";
        private void AddLine(string speaker, string text, string role)
        {
            ChatLines.Add(new ReignChatLineVM(speaker, text, role)); _chatScrollVersion++;
            OnPropertyChanged(nameof(ChatScrollVersion));
        }
        private void Refresh()
        {
            if (_finalized) return;
            foreach (string property in new[] { "IsBusy", "IsVisiting", "HasQuote", "CanAccept", "CanNegotiate", "InputEnabled", "CanSend",
                "CanLookAgain", "HasSceneImage", "ShowScenePlaceholder", "SceneImageId", "ImageStatus", "StatusText", "SendText", "AcceptText",
                "QuoteText", "VisitSummary", "ImagesEnabled", "ImageToggleText", "HasImageStatus", "PaymentConfirmationPending", "IsPortraitPreviewVisible", "PortraitPreviewName",
                "PortraitPreviewId", "PortraitPreviewAdditionalArgs", "PortraitPreviewTextureProviderName", "PortraitPreviewCacheKey" })
                OnPropertyChanged(property);
        }
    }
}
