using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignChatViewVM : ViewModel
    {
        private readonly Func<JObject> _scope;
        private readonly Func<bool> _available;
        private readonly MBBindingList<ReignChatLineVM> _lines;
        private readonly Action _scroll;
        private readonly HashSet<string> _following = new HashSet<string>();
        private readonly Dictionary<string, ReignChatLineVM> _images = new Dictionary<string, ReignChatLineVM>();
        private bool _busy, _closed, _restoring;
        private string _enlarged = "";
        private readonly string _campaign;
        private readonly string _timeline;
        private readonly string _player;

        internal ReignChatViewVM(Func<JObject> scope, Func<bool> available, MBBindingList<ReignChatLineVM> lines, Action scroll)
        {
            _scope = scope; _available = available; _lines = lines; _scroll = scroll;
            JObject initial = scope(); _campaign = initial.Value<string>("campaignId");
            _timeline = initial.Value<string>("timelineId"); _player = initial.Value<string>("playerId");
            ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_individual");
            ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_tavern_house");
            ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_shared");
            _ = RestoreAsync();
        }

        [DataSourceProperty] public bool CanView => !_closed && !_busy && _available();
        [DataSourceProperty] public bool IsUnavailable => !CanView;
        [DataSourceProperty] public string ButtonText => _busy ? "Viewing…" : "View";
        [DataSourceProperty] public bool IsEnlarged => !string.IsNullOrEmpty(_enlarged);
        [DataSourceProperty] public string EnlargedImageId => _enlarged;

        public void RefreshAvailability() { OnPropertyChanged(nameof(CanView)); OnPropertyChanged(nameof(IsUnavailable)); }
        public void ExecuteCloseImage() { _enlarged = ""; OnPropertyChanged(nameof(EnlargedImageId)); OnPropertyChanged(nameof(IsEnlarged)); }
        public bool HandleEscape() { if (!IsEnlarged) return false; ExecuteCloseImage(); return true; }
        internal void Close() { _closed = true; ExecuteCloseImage(); }

        public async void ExecuteView()
        {
            if (!CanView) return;
            _busy = true; Refresh();
            JObject scope = _scope();
            scope["requestId"] = Guid.NewGuid().ToString("N");
            scope["throughExchangeId"] = _lines.LastOrDefault(line => line.IsNpcLine && !string.IsNullOrEmpty(line.ExchangeId))?.ExchangeId ?? "";
            ReignChatLineVM pending = new ReignChatLineVM("View", "Preparing a view of this scene…", "view");
            _lines.Add(pending);
            _scroll();
            try
            {
                JObject response = await ReignServerClient.PostChatViewAsync("start", scope).ConfigureAwait(false);
                ReignChatViewClient.Check(response);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!Owns(scope)) return;
                    string id = response.Value<string>("jobId");
                    if (_images.TryGetValue(id, out ReignChatLineVM old)) { _lines.Remove(pending); pending = old; }
                    else _images[id] = pending;
                    pending.ExchangeId = response.Value<string>("anchorExchangeId") ?? "";
                }).ConfigureAwait(false);
                await FollowAsync(scope, response, pending).ConfigureAwait(false);
            }
            catch (Exception ex) { await ReignMainThread.InvokeAsync(() => { if (Owns(scope)) pending.Text = ex.Message; }).ConfigureAwait(false); }
            finally { await ReignMainThread.InvokeAsync(() => { _busy = false; Refresh(); }).ConfigureAwait(false); }
        }

        internal async Task RestoreAsync()
        {
            if (_closed || _restoring) return;
            _restoring = true;
            JObject scope = _scope();
            try
            {
                foreach (JObject entry in await ReignChatViewClient.ReadEntriesAsync(scope).ConfigureAwait(false))
                {
                    ReignChatLineVM line = await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!Owns(scope)) return null;
                        string id = entry.Value<string>("jobId");
                        if (!_images.TryGetValue(id, out ReignChatLineVM item))
                        {
                            item = new ReignChatLineVM("View", entry.Value<string>("caption") ?? "Preparing scene…", "view")
                            { ExchangeId = entry.Value<string>("anchorExchangeId") ?? "" };
                            _images[id] = item;
                        }
                        if (!_lines.Contains(item)) InsertAtAnchor(item);
                        return item;
                    }).ConfigureAwait(false);
                    if (line != null) _ = FollowAsync(scope, entry, line);
                }
            }
            catch { /* An empty/new/offline session has no image entries to restore. View click reports actionable errors. */ }
            finally { _restoring = false; }
        }

        private async Task FollowAsync(JObject scope, JObject response, ReignChatLineVM line)
        {
            string id = response.Value<string>("jobId");
            if (line.HasSceneImage) return;
            lock (_following) { if (!_following.Add(id)) return; }
            try
            {
                for (int attempt = 0; attempt < 400; attempt++)
                {
                    if (_closed) return;
                    string status = response.Value<string>("status");
                    if (status == "ready")
                    {
                        JObject ready = await ReignChatViewClient.DownloadAsync(scope, response).ConfigureAwait(false);
                        await ReignMainThread.InvokeAsync(() =>
                        {
                            if (!Owns(scope)) return;
                            line.InlineScenePath = System.IO.Path.ChangeExtension(ready.Value<string>("localPath"), null) + "_inline.png";
                            line.FullScenePath = ready.Value<string>("localPath") ?? "";
                            line.SetScene(ready.Value<string>("imageId"), ready.Value<string>("caption"), () =>
                            {
                                _enlarged = ready.Value<string>("fullImageId");
                                OnPropertyChanged(nameof(EnlargedImageId)); OnPropertyChanged(nameof(IsEnlarged));
                            });
                            _scroll();
                        }).ConfigureAwait(false);
                        return;
                    }
                    if (status == "failed" || status == "uncertain")
                    {
                        await ReignMainThread.InvokeAsync(() => { if (Owns(scope)) line.Text = response.Value<string>("error") ?? "View could not finish this image."; }).ConfigureAwait(false);
                        return;
                    }
                    await Task.Delay(1500).ConfigureAwait(false);
                    JObject query = (JObject)scope.DeepClone(); query["jobId"] = id; query["includeImage"] = false;
                    response = await ReignServerClient.PostChatViewAsync("state", query).ConfigureAwait(false);
                    ReignChatViewClient.Check(response);
                }
                await ReignMainThread.InvokeAsync(() => { if (Owns(scope)) line.Text = "This image is still pending. Reopen the conversation to check it; no new generation is required."; }).ConfigureAwait(false);
            }
            catch (Exception ex) { await ReignMainThread.InvokeAsync(() => { if (Owns(scope)) line.Text = ex.Message; }).ConfigureAwait(false); }
            finally { lock (_following) _following.Remove(id); }
        }

        internal void RestoreSavedScene(ReignChatLineVM line, string inlinePath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(inlinePath) || string.IsNullOrWhiteSpace(fullPath)
                || !System.IO.File.Exists(inlinePath) || !System.IO.File.Exists(fullPath)) return;
            string inline = ReignBeta.UI.EventArt.ReignEventArtTextureFactory.BuildMemorySceneImageId(inlinePath);
            string full = ReignBeta.UI.EventArt.ReignEventArtTextureFactory.BuildMemorySceneImageId(fullPath);
            if (string.IsNullOrWhiteSpace(inline) || string.IsNullOrWhiteSpace(full)) return;
            line.InlineScenePath = inlinePath; line.FullScenePath = fullPath;
            line.SetScene(inline, line.OriginalText, () => {
                _enlarged = full; OnPropertyChanged(nameof(EnlargedImageId)); OnPropertyChanged(nameof(IsEnlarged));
            });
        }

        private bool Owns(JObject captured)
        {
            JObject current = _scope();
            return !_closed && current.Value<string>("campaignId") == _campaign && current.Value<string>("timelineId") == _timeline
                && current.Value<string>("playerId") == _player && current.Value<string>("sessionId") == captured.Value<string>("sessionId");
        }
        private void InsertAtAnchor(ReignChatLineVM line)
        {
            int index = -1;
            for (int i = 0; i < _lines.Count; i++) if (_lines[i].ExchangeId == line.ExchangeId) index = i;
            _lines.Insert(index < 0 ? _lines.Count : index + 1, line);
        }
        private void Refresh() { RefreshAvailability(); OnPropertyChanged(nameof(ButtonText)); }
    }

    public sealed partial class ReignIndividualChatScreenVM
    {
        private ReignChatViewVM _view;
        [DataSourceProperty] public ReignChatViewVM View => _view ?? (_view = new ReignChatViewVM(
            () => ReignChatViewClient.Scope(_conversationSessionId),
            () => !_isFinalized && !_calibrationMode && !IsBusy && !_presentationInitializing && !IsOutfitVisible && !IsPregnancyWarningVisible,
            ChatLines, () => ChatScrollVersion++));
    }

    public sealed partial class ReignPartyChatScreenVM
    {
        private ReignChatViewVM _view;
        [DataSourceProperty] public ReignChatViewVM View => _view ?? (_view = new ReignChatViewVM(
            () => ReignChatViewClient.Scope(string.IsNullOrEmpty(_conversationSessionId) ? _session.SessionId : _conversationSessionId),
            () => !_isFinalized && !IsBusy && !_banterReadOnly && InputEnabled, ChatLines, () => ChatScrollVersion++));
    }
}
