using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AIPortraits;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.MountAndBlade.View.Tableaus;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

namespace ReignBeta.Integration
{
    internal static class ReignCourtAudienceReferenceCapture
    {
        // Native identity renders are local and free. Never call the AI portrait provider here.
        internal static async Task<byte[]> ReadAsync(CourtScenePerson person, string folder, Func<bool> owns)
        {
            if (person.Portrait?.Length > 0) return Normalize(person.Portrait);
            string key = ReignCourtAudienceScene.Hash(Encoding.UTF8.GetBytes(person.CharacterCode ?? ""));
            string path = Path.Combine(folder, "references", key + ".png");
            if (File.Exists(path))
            {
                byte[] cached = File.ReadAllBytes(path);
                if (PngReencode.DecodeToRgba(cached, out int _, out int _) != null) return cached;
            }
            var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            CharacterThumbnailCreationData request = null;
            ThumbnailCacheManager manager = null;
            bool expired = false;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (!owns()) throw new OperationCanceledException("The campaign or audience changed.");
                CharacterCode code = CharacterCode.CreateFrom(person.CharacterCode ?? "");
                if (code == null || code.IsEmpty) throw new InvalidOperationException("No native appearance for " + person.Name + ".");
                // Match the native CharacterImageTextureProvider's supported maturity range.
                // Never force unsupported infant/toddler rendering or substitute an adult face.
                if ((int)FaceGen.GetMaturityTypeWithAge(code.BodyProperties.Age) <= 1)
                    throw new InvalidOperationException("No supported age-appropriate reference is available for " + person.Name + ". No partial-cast scene was requested.");
                manager = ThumbnailCacheManager.Current;
                if (manager == null) throw new InvalidOperationException("Native court portrait renderer is unavailable.");
                request = new CharacterThumbnailCreationData(code, texture =>
                {
                    if (expired || !owns()) { completion.TrySetCanceled(); return; }
                    try
                    {
                        byte[] bytes = texture == null ? null : PortraitCaptureService.CaptureFromTwoDTexture(
                            new TaleWorlds.TwoDimension.Texture(new EngineTexture(texture)));
                        completion.TrySetResult(bytes);
                    }
                    catch (Exception ex) { completion.TrySetException(ex); }
                }, () => completion.TrySetCanceled(), true, 512, 768);
                manager.CreateTexture(request);
            }).ConfigureAwait(false);
            try
            {
                if (await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(45))).ConfigureAwait(false) != completion.Task)
                    throw new TimeoutException("The native reference for " + person.Name + " was not ready. No partial-cast scene was requested.");
                byte[] result = await completion.Task.ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                { if (!owns()) throw new OperationCanceledException("The campaign or audience changed."); }).ConfigureAwait(false);
                if (result?.Length > 0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, result);
                }
                return result;
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    expired = true;
                    if (ReferenceEquals(manager, ThumbnailCacheManager.Current) && request != null)
                        manager.DestroyTexture(request);
                }).ConfigureAwait(false);
            }
        }

        private static byte[] Normalize(byte[] image) => image.Length > 2 && image[0] == 255 && image[1] == 216
            ? JpegToPng.Convert(image) : image;
    }
}
