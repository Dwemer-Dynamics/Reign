using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed partial class ReignIndividualChatScreenVM
    {
        private string _outfitState = "closed", _outfitDescription = "", _outfitStatus = "", _outfitBeforePath = "", _outfitAfterPath = "";
        private float _outfitBeforeWidth = 438, _outfitBeforeHeight = 584, _outfitAfterWidth = 438, _outfitAfterHeight = 584;
        private ReignOutfitAttempt _outfitAttempt;
        private CancellationTokenSource _outfitCancellation;
        [DataSourceProperty] public bool CanChangeOutfit => ReignOutfitClient.Eligible(_hero);
        [DataSourceProperty] public bool IsOutfitVisible => _outfitState != "closed";
        [DataSourceProperty] public bool IsOutfitInputVisible => _outfitState == "input";
        [DataSourceProperty] public bool IsOutfitComparisonVisible => IsOutfitVisible && !IsOutfitInputVisible;
        [DataSourceProperty] public bool IsOutfitReady => _outfitState == "ready" || _outfitState == "commit_retry";
        [DataSourceProperty] public bool CanCancelOutfit => _outfitState != "saving" && _outfitState != "commit_retry";
        [DataSourceProperty] public bool IsOutfitChanging => _outfitState == "changing";
        [DataSourceProperty] public string OutfitDescription { get => _outfitDescription; set { _outfitDescription = value ?? ""; OnPropertyChangedWithValue(_outfitDescription); } }
        [DataSourceProperty] public string OutfitStatus => _outfitStatus;
        [DataSourceProperty] public string OutfitBeforePath => _outfitBeforePath;
        [DataSourceProperty] public string OutfitAfterPath => _outfitAfterPath;
        [DataSourceProperty] public float OutfitBeforeWidth => _outfitBeforeWidth;
        [DataSourceProperty] public float OutfitBeforeHeight => _outfitBeforeHeight;
        [DataSourceProperty] public float OutfitAfterWidth => _outfitAfterWidth;
        [DataSourceProperty] public float OutfitAfterHeight => _outfitAfterHeight;

        private void RefreshOutfit(string state, string status = "")
        {
            _outfitState = state; _outfitStatus = status;
            foreach (string property in new[] { nameof(IsOutfitVisible), nameof(IsOutfitInputVisible), nameof(IsOutfitComparisonVisible),
                nameof(IsOutfitReady), nameof(CanCancelOutfit), nameof(IsOutfitChanging), nameof(OutfitStatus), nameof(OutfitBeforePath),
                nameof(OutfitAfterPath), nameof(OutfitBeforeWidth), nameof(OutfitBeforeHeight), nameof(OutfitAfterWidth), nameof(OutfitAfterHeight), nameof(InputEnabled) })
                OnPropertyChanged(property);
        }

        public void ExecuteChangeClothes()
        {
            if (_isFinalized || IsBusy || IsPregnancyWarningVisible || IsOutfitVisible || !CanChangeOutfit) return;
            ClosePortraitZoom(); OutfitDescription = "";
            _outfitBeforePath = _outfitAfterPath = "";
            RefreshOutfit("input");
        }

        public async void ExecuteSubmitOutfit()
        {
            if (!IsOutfitInputVisible || _isFinalized) return;
            if (_calibrationMode) { RefreshOutfit("input", "Calibration does not generate portraits."); return; }
            string description = OutfitDescription.Trim();
            if (description.Length == 0 || description.Length > ReignOutfitClient.MaxDescriptionLength)
            { RefreshOutfit("input", "Describe the clothes using 1 to 4000 characters."); return; }
            if (!CanChangeOutfit) { ExecuteCancelOutfit(); return; }
            var attempt = ReignOutfitClient.Create(_hero);
            _outfitAttempt = attempt;
            _outfitCancellation = new CancellationTokenSource();
            CancellationToken token = _outfitCancellation.Token;
            JObject request = attempt.Identity;
            request["npc"] = ReignOutfitClient.Profile(_hero);
            request["player"] = ReignOutfitClient.Profile(Hero.MainHero);
            request["description"] = description;
            RefreshOutfit("changing");
            try
            {
                byte[] before = await Task.Run(() =>
                {
                    using (new PortraitRequestScope(attempt.CampaignId, attempt.CampaignFolder, new JObject())) return PortraitCache.GetDiskBytes(attempt.CacheKey);
                }).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (before == null || before.Length == 0) throw new InvalidOperationException("Use Look at them to create a portrait first.");
                attempt.SourceHash = ReignOutfitClient.Hash(before);
                Directory.CreateDirectory(attempt.DirectoryPath);
                File.WriteAllBytes(attempt.BeforePath, before);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!OutfitCurrent(attempt)) return;
                    _outfitBeforePath = attempt.BeforePath;
                    OutfitFit(before, out _outfitBeforeWidth, out _outfitBeforeHeight);
                    RefreshOutfit("changing");
                }).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!await ReignMainThread.InvokeAsync(() => OutfitCurrent(attempt)).ConfigureAwait(false)) return;
                request["sourceImageBase64"] = Convert.ToBase64String(before);
                JObject result = await ReignOutfitClient.Request(attempt, "begin", request).ConfigureAwait(false);
                while (result.Value<string>("status") == "changing")
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                    JObject poll = attempt.Identity; poll["includeImage"] = true;
                    result = await ReignOutfitClient.Request(attempt, "status", poll).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                if (result.Value<string>("status") != "ready")
                {
                    string refusal = result.Value<string>("npcMessage");
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!OutfitCurrent(attempt)) return;
                        if (!string.IsNullOrWhiteSpace(refusal))
                        { AddChatLine(_hero.Name.ToString(), refusal, "npc"); ExecuteCancelOutfit(); }
                        else RefreshOutfit("failed", result.Value<string>("error") ?? "The outfit could not be generated.");
                    }).ConfigureAwait(false);
                    return;
                }
                if (string.IsNullOrWhiteSpace(result.Value<string>("imageBase64")))
                { JObject poll = attempt.Identity; poll["includeImage"] = true; result = await ReignOutfitClient.Request(attempt, "status", poll).ConfigureAwait(false); }
                token.ThrowIfCancellationRequested();
                byte[] after = Convert.FromBase64String(result.Value<string>("imageBase64") ?? "");
                File.WriteAllBytes(attempt.AfterPath, after);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!OutfitCurrent(attempt)) return;
                    _outfitAfterPath = attempt.AfterPath;
                    OutfitFit(after, out _outfitAfterWidth, out _outfitAfterHeight);
                    RefreshOutfit("ready");
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() => { if (OutfitCurrent(attempt)) RefreshOutfit("failed", FriendlyServerError(ex.Message)); }).ConfigureAwait(false);
            }
            finally
            {
                if (token.IsCancellationRequested) { await ReignOutfitClient.Cancel(attempt).ConfigureAwait(false); attempt.Cleanup(); }
            }
        }

        public async void ExecuteConfirmOutfit()
        {
            var attempt = _outfitAttempt;
            if (!IsOutfitReady || !OutfitCurrent(attempt) || _calibrationMode) return;
            if (!attempt.ConfirmationRequested && !CanChangeOutfit) { ExecuteCancelOutfit(); return; }
            JObject request = attempt.Identity;
            request["npc"] = ReignOutfitClient.Profile(_hero);
            request["player"] = ReignOutfitClient.Profile(Hero.MainHero);
            byte[] current = PortraitCache.GetDiskBytes(attempt.CacheKey);
            if (!attempt.ConfirmationRequested && (current == null || ReignOutfitClient.Hash(current) != attempt.SourceHash))
            { RefreshOutfit("failed", "The portrait changed. Cancel and try again."); return; }
            request["sourceSha256"] = attempt.SourceHash;
            attempt.ConfirmationRequested = true;
            RefreshOutfit("saving", "Saving changes...");
            bool committed = false;
            try
            {
                JObject result = await ReignOutfitClient.Request(attempt, "confirm", request).ConfigureAwait(false);
                if (result.Value<string>("status") != "committed")
                {
                    // A server rejection proves that this confirmation did not commit.
                    attempt.ConfirmationRequested = false;
                    throw new InvalidOperationException(result.Value<string>("error") ?? "The outfit could not be saved.");
                }
                committed = true;
                byte[] bytes = Convert.FromBase64String(result.Value<string>("imageBase64"));
                await ReignOutfitClient.Install(attempt, bytes, result["portraitReceipt"] as JObject).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!OutfitCurrent(attempt)) return;
                    TextureFactory.Invalidate(attempt.CacheKey);
                    NpcPortrait = BuildPortrait(_hero, _npcPortraitCacheKey);
                    AddSystemLine("Clothes changed. " + _hero.Name + " now knows what they are wearing.");
                    FinishOutfit();
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!OutfitCurrent(attempt)) return;
                    RefreshOutfit(attempt.ConfirmationRequested ? "commit_retry" : "ready",
                        (committed ? "The outfit was saved, but its local portrait could not refresh. Confirm again to retry. "
                            : attempt.ConfirmationRequested ? "Confirmation could not be verified. Confirm again to retry. " : "") + FriendlyServerError(ex.Message));
                }).ConfigureAwait(false);
            }
        }

        private bool OutfitCurrent(ReignOutfitAttempt attempt) => !_isFinalized && attempt != null && ReferenceEquals(_outfitAttempt, attempt) && attempt.IsCurrent;
        private static void OutfitFit(byte[] bytes, out float width, out float height)
        {
            if (bytes == null || bytes.Length < 24) throw new InvalidDataException("The outfit preview image is invalid.");
            int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            if (w <= 0 || h <= 0) throw new InvalidDataException("The outfit preview dimensions are invalid.");
            float scale = Math.Min(464f / w, 584f / h); width = w * scale; height = h * scale;
        }
        public void ExecuteCancelOutfit()
        {
            if (!CanCancelOutfit) return;
            var attempt = _outfitAttempt;
            _outfitCancellation?.Cancel(); FinishOutfit();
            _ = ReignOutfitClient.Cancel(attempt);
        }
        private void FinishOutfit()
        {
            _outfitAttempt?.Cleanup(); _outfitAttempt = null;
            _outfitBeforePath = _outfitAfterPath = ""; RefreshOutfit("closed");
        }
        internal bool HandleOutfitEscape()
        {
            if (!IsOutfitVisible) return false;
            ExecuteCancelOutfit(); return true;
        }
    }
}
