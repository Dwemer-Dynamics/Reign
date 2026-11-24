using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.UI.EventArt;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.UI;

namespace ReignBeta.Court.Integration
{
    // Per-view presentation owns only notifications; the shared engine owns the pending job.
    internal sealed class ReignCourtAudienceScenePresentation
    {
        private readonly Func<CourtSceneSnapshot> _snapshot;
        private readonly Func<bool> _owns;
        private readonly Func<CourtSceneSnapshot, bool> _currentAudience;
        private readonly Action<string> _persist, _show;
        private readonly Action _begin, _end;
        private readonly CourtSceneViewState _state = new CourtSceneViewState();
        internal string Status => _state.Status;
        internal string Error => _state.Error;
        internal bool Complete => Status != "pending";
        internal bool Ready => Status == "ready";
        internal JObject Evidence
        {
            get
            {
                var result = new JObject { ["status"] = Status, ["ready"] = Ready,
                    ["preparationComplete"] = Complete, ["error"] = Error, ["sceneAssetPath"] = _state.Path };
                if (Ready && File.Exists(_state.Path + ".json"))
                {
                    try { result["receipt"] = JObject.Parse(File.ReadAllText(_state.Path + ".json")); }
                    catch (Exception) { result["receiptUnavailable"] = true; }
                }
                return result;
            }
        }

        internal ReignCourtAudienceScenePresentation(Func<CourtSceneSnapshot> snapshot, Func<bool> owns,
            Func<CourtSceneSnapshot, bool> currentAudience, Action<string> persist, Action<string> show, Action begin, Action end)
        { _snapshot = snapshot; _owns = owns; _currentAudience = currentAudience; _persist = persist; _show = show; _begin = begin; _end = end; }

        internal void Close() { _state.Close(); }

        internal void Refresh()
        {
            int revision = _state.Begin();
            try
            {
                CourtSceneSnapshot scene = _snapshot();
                bool lookupNeeded = scene.People.Any(person => person.RetrieveStoredPortrait != null);
                string cached = lookupNeeded ? "" : ReignCourtAudienceScene.CachedPath(scene);
                if (!string.IsNullOrEmpty(cached))
                { _state.Publish(revision, cached, _owns(), _persist, ShowPath); return; }
                _show(ReignEventArtTextureFactory.BuildCourtPetitionReferenceImageId(scene.CultureId));
                bool allowGeneration = ReignBetaSettings.Instance?.CastleChatImageGeneration != false;
                if (!allowGeneration && !lookupNeeded) { _state.Disable(revision); return; }
                _begin();
                _ = PrepareAsync(scene, revision, allowGeneration);
            }
            catch (Exception ex) { _state.Fail(revision, ex.Message); ReignLog.Warn("Court scene preparation: " + Error); }
        }

        private async Task PrepareAsync(CourtSceneSnapshot scene, int revision, bool allowGeneration)
        {
            Task EnsureCurrent() => ReignMainThread.InvokeAsync(() =>
            {
                if (!_owns() || !_currentAudience(scene))
                    throw new OperationCanceledException("The campaign or audience changed.");
            });

            try
            {
                string path = await Task.Run(async () =>
                {
                    foreach (var person in scene.People.Where(value => value.RetrieveStoredPortrait != null))
                    {
                        await EnsureCurrent().ConfigureAwait(false);
                        person.Portrait = await person.RetrieveStoredPortrait().ConfigureAwait(false);
                        if (person.Portrait != null)
                            person.ReferenceStamp = ReignCourtAudienceScene.Hash(person.Portrait);
                    }
                    await EnsureCurrent().ConfigureAwait(false);
                    if (!allowGeneration) return ReignCourtAudienceScene.CachedPath(scene);
                    return await ReignCourtAudienceScene.PrepareAsync(scene,
                        person => ReignCourtAudienceReferenceCapture.ReadAsync(person, scene.Folder, _owns),
                        ReignServerClient.PostCastleSceneAsync, EnsureCurrent).ConfigureAwait(false);
                }).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!_owns() || !_currentAudience(scene)) return;
                    if (!allowGeneration && string.IsNullOrEmpty(path)) _state.Disable(revision);
                    else _state.Publish(revision, path, true, _persist, ShowPath);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    _state.Fail(revision, ex.Message);
                    ReignLog.Warn("Court scene preparation: " + Error);
                }).ConfigureAwait(false);
            }
            finally { await ReignMainThread.InvokeAsync(_end).ConfigureAwait(false); }
        }

        private void ShowPath(string path)
        {
            ReignEventArtTextureFactory.Clear();
            _show(ReignEventArtTextureFactory.BuildCourtPetitionSceneImageId(path));
        }
    }
}
