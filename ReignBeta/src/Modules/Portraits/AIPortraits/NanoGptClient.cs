using System;
using System.Net.Http;
using AIEventsAndIntrigue.Settings;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;

namespace AIPortraits
{
    /// <summary>
    /// Sends a captured in-game portrait to the selected image provider
    /// and returns an AI-generated high-quality replacement as PNG bytes.
    ///
    /// All settings are read from AIEventsSettings (MCM) at call time,
    /// so changes in the MCM UI take effect immediately without restart.
    /// </summary>
    public static class NanoGptClient
    {
        private const string ImageGenerationsUrl = "https://nano-gpt.com/api/v1/images/generations";
        private const string ChatCompletionsUrl = "https://nano-gpt.com/api/v1/chat/completions";
        private const string AtlasGenerateImageUrl = "https://api.atlascloud.ai/api/v1/model/generateImage";
        private const string AtlasUploadMediaUrl = "https://api.atlascloud.ai/api/v1/model/uploadMedia";
        private const string AtlasPredictionUrlPrefix = "https://api.atlascloud.ai/api/v1/model/prediction/";
        private const int AtlasPollIntervalMs = 2500;
        private const int AtlasPollTimeoutMs = 300000;

        public const string DefaultPromptStyle =
            "Generate this character portrait using the shared prompt library configured in the Bannerlord Reign server.";

        public const string DefaultPrompt = DefaultPromptStyle;

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        public static string LastError { get; private set; }

        private static readonly AsyncLocal<string> _lastImageProviderRequestLog = new AsyncLocal<string>();

        public static string LastImageProviderRequestLog => _lastImageProviderRequestLog.Value;

        public static string LastErrorForDisplay
        {
            get
            {
                string error = PortraitRequestScope.Current != null ? PortraitRequestScope.Current.Error : LastError;
                if (string.IsNullOrWhiteSpace(error))
                    return null;

                var text = error.Replace('\r', ' ').Replace('\n', ' ').Trim();
                return text.Length <= 180 ? text : text.Substring(0, 177) + "...";
            }
        }

        // Semaphore recreated if MaxConcurrentRequests changes in MCM
        private static int _lastMaxConcurrent = -1;
        private static SemaphoreSlim _throttle = new SemaphoreSlim(2);

        private static SemaphoreSlim Throttle
        {
            get
            {
                var settings = AIEventsSettings.Instance;
                var max = settings?.MaxConcurrentRequests ?? 2;
                if (max != _lastMaxConcurrent)
                {
                    _throttle = new SemaphoreSlim(max);
                    _lastMaxConcurrent = max;
                }
                return _throttle;
            }
        }

        /// <summary>
        /// Sends sourceImagePng (the vanilla game portrait) to the selected image provider
        /// and returns the AI-generated PNG bytes. Returns null on any failure.
        /// </summary>
        public static async Task<byte[]> GeneratePortraitAsync(
            string prompt,
            byte[] sourceImagePng,
            CancellationToken ct = default)
        {
            return await GeneratePortraitAsync(prompt, sourceImagePng, null, ct);
        }

        public static async Task<byte[]> GeneratePortraitAsync(
            string prompt,
            byte[] sourceImagePng,
            string outputSizeOverride,
            CancellationToken ct = default)
        {
            return await GeneratePortraitAsync(prompt, sourceImagePng, outputSizeOverride, null, null, ct);
        }

        public static async Task<byte[]> GeneratePortraitAsync(
            string prompt,
            byte[] sourceImagePng,
            string outputSizeOverride,
            string cacheKey,
            string heroStringId,
            CancellationToken ct = default)
        {
            return await GeneratePortraitAsync(prompt, sourceImagePng, outputSizeOverride, cacheKey, heroStringId, null, ct);
        }

        public static async Task<byte[]> GeneratePortraitAsync(
            string prompt,
            byte[] sourceImagePng,
            string outputSizeOverride,
            string cacheKey,
            string heroStringId,
            PortraitPromptContext promptContext,
            CancellationToken ct = default)
        {
            ReignPortraitGenerationResult result = await GeneratePortraitProductAsync(
                prompt,
                sourceImagePng,
                outputSizeOverride,
                cacheKey,
                heroStringId,
                promptContext,
                ct).ConfigureAwait(false);
            return result?.ImageBytes;
        }

        public static async Task<ReignPortraitGenerationResult> GeneratePortraitProductAsync(
            string prompt,
            byte[] sourceImagePng,
            string outputSizeOverride,
            string cacheKey,
            string heroStringId,
            PortraitPromptContext promptContext,
            CancellationToken ct = default)
        {
            await Throttle.WaitAsync(ct);
            try
            {
                LastError = null;
                _lastImageProviderRequestLog.Value = null;
                var settings = AIEventsSettings.Instance;
                if (settings != null && !settings.ModEnabled)
                {
                    SetLastError("AI Portraits is disabled in Bannerlord Reign options.");
                    return null;
                }

                string generationOperationId = Guid.NewGuid().ToString("N");
                if (!string.IsNullOrWhiteSpace(cacheKey)
                    && PortraitCache.TryGetPendingGenerationOperation(cacheKey, out string pendingHeroId, out string pendingOperationId))
                {
                    ReignPortraitGenerationResult recovered = await ReignServerClient.RecoverPortraitGenerationAsync(
                        cacheKey,
                        string.IsNullOrWhiteSpace(pendingHeroId) ? heroStringId : pendingHeroId,
                        pendingOperationId,
                        180000).ConfigureAwait(false);
                    if (recovered != null && recovered.Ok && recovered.ProductAccepted)
                    {
                        TaleWorlds.Library.Debug.Print("[AIPortraits] Recovered completed portrait operation " + pendingOperationId + ".");
                        return recovered;
                    }
                    if (recovered != null && recovered.GenerationMayStillComplete)
                    {
                        SetLastError(recovered.Error);
                        return null;
                    }
                    PortraitCache.ClearPendingGenerationOperation(cacheKey, pendingOperationId);
                }

                if (!string.IsNullOrWhiteSpace(cacheKey))
                    PortraitCache.MarkPendingGenerationOperation(cacheKey, heroStringId, generationOperationId);

                _lastImageProviderRequestLog.Value = "POST /portraits/generate via Bannerlord Reign local server. Source image bytes and API keys are omitted from game logs.";
                ReignPortraitGenerationResult result = await ReignServerClient.GeneratePortraitAsync(cacheKey, heroStringId, prompt, sourceImagePng, outputSizeOverride, promptContext, "portrait", generationOperationId).ConfigureAwait(false);
                if (result == null || !result.Ok || result.ImageBytes == null || result.ImageBytes.Length == 0)
                {
                    if (result == null || !result.GenerationMayStillComplete)
                        PortraitCache.ClearPendingGenerationOperation(cacheKey, generationOperationId);
                    SetLastError(result?.Error ?? "Bannerlord Reign server returned no portrait image.");
                    return null;
                }
                if (!result.ProductAccepted
                    || result.ProductVersion != 1
                    || !string.Equals(result.ProductSchema, "reign-portrait-product-v1", StringComparison.Ordinal)
                    || result.FaceFocus == null
                    || result.FaceFocus.CandidateCount != 1)
                {
                    PortraitCache.ClearPendingGenerationOperation(cacheKey, generationOperationId);
                    SetLastError("Bannerlord Reign server returned a portrait without the required versioned face-focus receipt. The existing portrait was kept.");
                    return null;
                }

                TaleWorlds.Library.Debug.Print("[AIPortraits] Server portrait generation ok provider="
                    + result.Provider
                    + " model=" + result.Model
                    + " adapter=" + result.Adapter
                    + " clientMs=" + result.ClientTotalMs
                    + " serverMs=" + result.ServerDurationMs
                    + " timing=" + result.TimingSummary);
                return result;
            }
            catch (OperationCanceledException)
            {
                SetLastError("The portrait request was interrupted. Reign will check for the completed provider result before allowing another generation.");
                return null;
            }
            catch (Exception ex)
            {
                SetLastError("GeneratePortraitAsync exception: " + ex.Message);
                return null;
            }
            finally
            {
                Throttle.Release();
            }
        }

        public static async Task<byte[]> GenerateMemoryImageAsync(
            string prompt,
            byte[] sourceImagePng,
            string outputSizeOverride,
            string heroStringId,
            PortraitPromptContext promptContext,
            CancellationToken ct = default)
        {
            await Throttle.WaitAsync(ct);
            try
            {
                LastError = null;
                _lastImageProviderRequestLog.Value = "POST /portraits/generate memory image via Bannerlord Reign local server. Source image bytes and API keys are omitted from game logs.";
                ReignPortraitGenerationResult result = await ReignServerClient.GeneratePortraitAsync(null, heroStringId, prompt, sourceImagePng, outputSizeOverride, promptContext, "memory").ConfigureAwait(false);
                if (result == null || !result.Ok || result.ImageBytes == null || result.ImageBytes.Length == 0)
                {
                    SetLastError(result?.Error ?? "Bannerlord Reign server returned no memory image.");
                    return null;
                }

                return result.ImageBytes;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                SetLastError("GenerateMemoryImageAsync exception: " + ex.Message);
                return null;
            }
            finally
            {
                Throttle.Release();
            }
        }

        private static async Task<byte[]> GenerateAtlasImageAsync(
            AIEventsSettings settings,
            string prompt,
            byte[] sourceImagePng,
            string outputSizeOverride,
            CancellationToken ct)
        {
            if (sourceImagePng == null || sourceImagePng.Length == 0)
            {
                SetLastError("Atlas Cloud generation needs a source image.");
                return null;
            }

            var model = settings.SelectedAtlasModel;
            var outputSize = outputSizeOverride ?? settings.OutputSize;
            TaleWorlds.Library.Debug.Print(
                "[AIPortraits] Using Atlas Cloud image adapter: model=" + model + ", output_size=" + outputSize);

            var sourceUrl = await UploadAtlasMediaAsync(settings, sourceImagePng, ct);
            if (string.IsNullOrWhiteSpace(sourceUrl))
                return null;

            var payload = BuildAtlasImagePayload(model, prompt, sourceUrl, outputSize);
            _lastImageProviderRequestLog.Value = BuildAtlasImageRequestLog(outputSize, payload);

            using (var request = new HttpRequestMessage(HttpMethod.Post, AtlasGenerateImageUrl))
            {
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                request.Headers.Add("Authorization", "Bearer " + settings.AtlasApiKey);

                using (var response = await _http.SendAsync(request, ct))
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        SetLastError("Atlas Cloud image submit API error " + (int)response.StatusCode + ": " + json);
                        return null;
                    }

                    var predictionId = ExtractAtlasPredictionId(json);
                    if (string.IsNullOrWhiteSpace(predictionId))
                    {
                        SetLastError("Atlas Cloud image submit returned no prediction id. " + Shorten(json, 260));
                        return null;
                    }

                    return await PollAtlasPredictionAsync(settings, predictionId, ct);
                }
            }
        }

        private static JObject BuildAtlasImagePayload(
            string model,
            string prompt,
            string sourceUrl,
            string outputSize)
        {
            if (UsesAtlasFluxKontextDev(model))
            {
                return new JObject
                {
                    ["model"] = model,
                    ["seed"] = -1,
                    ["width"] = GetAtlasFluxKontextWidth(outputSize),
                    ["height"] = GetAtlasFluxKontextHeight(outputSize),
                    ["image"] = sourceUrl,
                    ["prompt"] = prompt ?? string.Empty,
                    ["num_images"] = 1,
                    ["guidance_scale"] = 5,
                    ["num_inference_steps"] = 30,
                    ["enable_base64_output"] = false,
                    ["enable_safety_checker"] = true
                };
            }

            if (UsesAtlasSeedreamEdit(model))
            {
                return new JObject
                {
                    ["model"] = model,
                    ["prompt"] = prompt ?? string.Empty,
                    ["images"] = new JArray(sourceUrl),
                    ["size"] = MapToAtlasSeedreamSize(outputSize),
                    ["enable_base64_output"] = false
                };
            }

            if (UsesAtlasSeedreamV5ProEdit(model))
            {
                return new JObject
                {
                    ["model"] = model,
                    ["prompt"] = prompt ?? string.Empty,
                    ["images"] = new JArray(sourceUrl),
                    ["size"] = MapToAtlasSeedreamV5Size(outputSize),
                    ["output_format"] = "png",
                    ["thinking"] = "enabled",
                    ["enable_base64_output"] = false
                };
            }

            if (UsesAtlasWan25ImageEdit(model))
            {
                return new JObject
                {
                    ["model"] = model,
                    ["images"] = new JArray(sourceUrl),
                    ["prompt"] = prompt ?? string.Empty,
                    ["negative_prompt"] = "text, words, letters, numbers, captions, labels, names, ages, cultures, subtitles, signatures, logos, watermarks, interface elements, borders, banners, white strips, footer panels, metadata panels, armor, weapons, helmets, chainmail, plastic skin, CGI, cartoon, anime, painterly style, modern clothing",
                    ["seed"] = -1,
                    ["size"] = MapToAtlasWan25Size(outputSize),
                    ["enable_prompt_expansion"] = true
                };
            }

            if (UsesAtlasWanImageEdit(model))
            {
                return new JObject
                {
                    ["model"] = model,
                    ["prompt"] = prompt ?? string.Empty,
                    ["images"] = new JArray(sourceUrl),
                    ["size"] = MapToAtlasWanSize(outputSize),
                    ["n"] = 1,
                    ["thinking_mode"] = true,
                    ["seed"] = -1,
                    ["enable_sync_mode"] = false,
                    ["enable_base64_output"] = false
                };
            }

            var payload = new JObject
            {
                ["model"] = model,
                ["prompt"] = prompt ?? string.Empty,
                ["num_images"] = 1,
                ["aspect_ratio"] = MapToAtlasAspectRatio(outputSize),
                ["resolution"] = "1k"
            };

            if (AtlasModelUsesImageUrlsArray(model))
                payload["image_urls"] = new JArray(sourceUrl);
            else
                payload["image_url"] = sourceUrl;

            return payload;
        }

        private static string BuildAtlasImageRequestLog(string outputSize, JObject payload)
        {
            var sanitized = payload == null ? new JObject() : (JObject)payload.DeepClone();
            RedactAtlasReferenceImages(sanitized);

            return "POST " + AtlasGenerateImageUrl + Environment.NewLine +
                   "Authorization: Bearer [Atlas key omitted]" + Environment.NewLine +
                   "Uploaded reference image: [two-character portrait reference URL omitted]" + Environment.NewLine +
                   "Requested output size: " + (outputSize ?? string.Empty) + Environment.NewLine +
                   "Payload:" + Environment.NewLine +
                   sanitized.ToString(Formatting.Indented);
        }

        private static void RedactAtlasReferenceImages(JObject payload)
        {
            if (payload == null)
                return;

            if (payload["image"] != null)
                payload["image"] = "[uploaded two-character reference image omitted]";

            if (payload["image_url"] != null)
                payload["image_url"] = "[uploaded two-character reference image omitted]";

            if (payload["images"] != null)
                payload["images"] = new JArray("[uploaded two-character reference image omitted]");

            if (payload["image_urls"] != null)
                payload["image_urls"] = new JArray("[uploaded two-character reference image omitted]");
        }

        private static async Task<string> UploadAtlasMediaAsync(
            AIEventsSettings settings,
            byte[] sourceImagePng,
            CancellationToken ct)
        {
            using (var content = new MultipartFormDataContent())
            {
                var imageContent = new ByteArrayContent(sourceImagePng);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                content.Add(imageContent, "file", "aiportraits_reference.png");

                using (var request = new HttpRequestMessage(HttpMethod.Post, AtlasUploadMediaUrl))
                {
                    request.Content = content;
                    request.Headers.Add("Authorization", "Bearer " + settings.AtlasApiKey);

                    using (var response = await _http.SendAsync(request, ct))
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            SetLastError("Atlas Cloud upload API error " + (int)response.StatusCode + ": " + json);
                            return null;
                        }

                        var uploadUrl = ExtractAtlasUploadUrl(json);
                        if (string.IsNullOrWhiteSpace(uploadUrl))
                        {
                            SetLastError("Atlas Cloud upload returned no file URL. " + Shorten(json, 260));
                            return null;
                        }

                        return uploadUrl;
                    }
                }
            }
        }

        private static async Task<byte[]> PollAtlasPredictionAsync(
            AIEventsSettings settings,
            string predictionId,
            CancellationToken ct)
        {
            var started = DateTime.UtcNow;
            var predictionUrl = AtlasPredictionUrlPrefix + Uri.EscapeDataString(predictionId);

            while ((DateTime.UtcNow - started).TotalMilliseconds < AtlasPollTimeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                using (var request = new HttpRequestMessage(HttpMethod.Get, predictionUrl))
                {
                    request.Headers.Add("Authorization", "Bearer " + settings.AtlasApiKey);

                    using (var response = await _http.SendAsync(request, ct))
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            SetLastError("Atlas Cloud prediction API error " + (int)response.StatusCode + ": " + json);
                            return null;
                        }

                        var root = ParseJsonOrError(json, "Atlas Cloud prediction");
                        if (root == null)
                            return null;

                        var status = ExtractJsonString(root, "data.status") ?? ExtractJsonString(root, "status");
                        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
                        {
                            var outputUrl = ExtractAtlasOutputUrl(root);
                            if (string.IsNullOrWhiteSpace(outputUrl))
                            {
                                SetLastError("Atlas Cloud prediction completed but returned no output URL. " + Shorten(json, 260));
                                return null;
                            }

                            TaleWorlds.Library.Debug.Print("[AIPortraits] Atlas Cloud prediction completed; downloading image.");
                            return await _http.GetByteArrayAsync(outputUrl);
                        }

                        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
                        {
                            SetLastError("Atlas Cloud prediction failed: " + ExtractAtlasError(root));
                            return null;
                        }
                    }
                }

                await Task.Delay(AtlasPollIntervalMs, ct);
            }

            SetLastError("Atlas Cloud prediction timed out after " + (AtlasPollTimeoutMs / 1000) + " seconds.");
            return null;
        }

        private static async Task<byte[]> GenerateFluxKontextAsync(
            AIEventsSettings settings,
            string prompt,
            byte[] sourceImagePng,
            string outputSizeOverride,
            CancellationToken ct)
        {
            // NanoGPT's Flux Kontext route accepts the source image as a base64 data URL.
            var b64          = Convert.ToBase64String(sourceImagePng);
            var imageDataUrl = "data:image/png;base64," + b64;

            var payload = new
            {
                model               = settings.SelectedModel,
                prompt              = prompt,
                n                   = 1,
                size                = outputSizeOverride ?? settings.OutputSize,
                imageDataUrl        = imageDataUrl,
                strength            = settings.ImageStrength,
                guidance_scale      = settings.GuidanceScale,
                num_inference_steps = settings.InferenceSteps,
                response_format     = "b64_json"
            };

            using (var request = new HttpRequestMessage(
                       HttpMethod.Post,
                       ImageGenerationsUrl))
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("Authorization", "Bearer " + settings.ApiKey);

                var response = await _http.SendAsync(request, ct);
                return await ReadImageResponseAsync(response, "Flux image generation");
            }
        }

        private static async Task<byte[]> GenerateImageReferenceAsync(
            AIEventsSettings settings,
            string prompt,
            byte[] sourceImagePng,
            string outputSizeOverride,
            CancellationToken ct)
        {
            var gptSize = MapToGptImageSize(outputSizeOverride ?? settings.OutputSize);
            TaleWorlds.Library.Debug.Print(
                "[AIPortraits] Using NanoGPT image reference adapter: model=" + settings.SelectedModel + ", size=" + gptSize);

            var b64          = Convert.ToBase64String(sourceImagePng);
            var imageDataUrl = "data:image/png;base64," + b64;

            var payload = new
            {
                model           = settings.SelectedModel,
                prompt          = prompt,
                n               = 1,
                size            = gptSize,
                imageDataUrl    = imageDataUrl,
                response_format = "b64_json"
            };

            using (var request = new HttpRequestMessage(
                       HttpMethod.Post,
                       ImageGenerationsUrl))
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("Authorization", "Bearer " + settings.ApiKey);

                var response = await _http.SendAsync(request, ct);
                return await ReadImageResponseAsync(response, "Image reference generation");
            }
        }

        private static async Task<byte[]> GenerateGeminiChatImageAsync(
            AIEventsSettings settings,
            string prompt,
            byte[] sourceImagePng,
            CancellationToken ct)
        {
            TaleWorlds.Library.Debug.Print(
                "[AIPortraits] Using NanoGPT Gemini chat image adapter: model=" + settings.SelectedModel);

            var b64 = Convert.ToBase64String(sourceImagePng);
            var imageDataUrl = "data:image/png;base64," + b64;
            var payload = new
            {
                model = settings.SelectedModel,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = imageDataUrl } }
                        }
                    }
                },
                stream = false
            };

            using (var request = new HttpRequestMessage(
                       HttpMethod.Post,
                       ChatCompletionsUrl))
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(payload),
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("Authorization", "Bearer " + settings.ApiKey);

                var response = await _http.SendAsync(request, ct);
                return await ReadChatImageResponseAsync(response, "Gemini chat image generation");
            }
        }

        private static async Task<byte[]> ReadImageResponseAsync(HttpResponseMessage response, string adapterName)
        {
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    SetLastError(adapterName + " API error " + (int)response.StatusCode + ": " + err);
                    return null;
                }

                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<NanoGptResponse>(json);

                if (result?.Data == null || result.Data.Length == 0)
                {
                    SetLastError(adapterName + " API returned empty data.");
                    return null;
                }

                var image = result.Data[0];
                if (!string.IsNullOrEmpty(image.B64Json))
                    return Convert.FromBase64String(image.B64Json);

                if (!string.IsNullOrEmpty(image.Url))
                {
                    TaleWorlds.Library.Debug.Print("[AIPortraits] " + adapterName + " returned URL; downloading image.");
                    return await _http.GetByteArrayAsync(image.Url);
                }

                SetLastError(adapterName + " API returned data without b64_json or url.");
                return null;
            }
        }

        private static async Task<byte[]> ReadChatImageResponseAsync(HttpResponseMessage response, string adapterName)
        {
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    SetLastError(adapterName + " API error " + (int)response.StatusCode + ": " + err);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var imageBytes = await TryExtractImageBytesAsync(json, adapterName);
                if (imageBytes != null)
                    return imageBytes;

                SetLastError(adapterName + " API returned no image data. " + SummarizeChatContent(json));
                return null;
            }
        }

        private static async Task<byte[]> TryExtractImageBytesAsync(string json, string adapterName)
        {
            JToken root;
            try
            {
                root = JToken.Parse(json);
            }
            catch (Exception ex)
            {
                SetLastError(adapterName + " API returned invalid JSON: " + ex.Message);
                return null;
            }

            var b64 = FindFirstPropertyString(root, "b64_json");
            if (!string.IsNullOrWhiteSpace(b64) && TryDecodeBase64OrDataUrl(b64, out var b64Bytes))
                return b64Bytes;

            var dataUrl = FindDataImageUrl(root);
            if (!string.IsNullOrWhiteSpace(dataUrl) && TryDecodeDataUrl(dataUrl, out var dataUrlBytes))
                return dataUrlBytes;

            var url = FindFirstImageUrl(root);
            if (!string.IsNullOrWhiteSpace(url))
            {
                TaleWorlds.Library.Debug.Print("[AIPortraits] " + adapterName + " returned URL; downloading image.");
                return await _http.GetByteArrayAsync(url);
            }

            return null;
        }

        private static string FindFirstPropertyString(JToken token, string propertyName)
        {
            if (token == null)
                return null;

            var obj = token as JObject;
            if (obj != null)
            {
                foreach (var prop in obj.Properties())
                {
                    if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.Type == JTokenType.String)
                            return prop.Value.Value<string>();

                        var nestedUrl = FindFirstPropertyString(prop.Value, "url");
                        if (!string.IsNullOrWhiteSpace(nestedUrl))
                            return nestedUrl;
                    }
                }

                foreach (var prop in obj.Properties())
                {
                    var found = FindFirstPropertyString(prop.Value, propertyName);
                    if (!string.IsNullOrWhiteSpace(found))
                        return found;
                }
            }

            var arr = token as JArray;
            if (arr != null)
            {
                foreach (var child in arr)
                {
                    var found = FindFirstPropertyString(child, propertyName);
                    if (!string.IsNullOrWhiteSpace(found))
                        return found;
                }
            }

            return null;
        }

        private static string FindDataImageUrl(JToken token)
        {
            foreach (var text in EnumerateStringValues(token))
            {
                var match = Regex.Match(text, @"data:image/(?:png|jpeg|jpg|webp);base64,[A-Za-z0-9+/=]+", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Value;
            }

            return null;
        }

        private static string FindFirstImageUrl(JToken token)
        {
            var explicitUrl = FindFirstPropertyString(token, "url");
            if (IsHttpUrl(explicitUrl))
                return explicitUrl;

            var imageUrl = FindFirstPropertyString(token, "image_url");
            if (IsHttpUrl(imageUrl))
                return imageUrl;

            foreach (var text in EnumerateStringValues(token))
            {
                var match = Regex.Match(text, @"https?://[^\s""')>]+", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Value;
            }

            return null;
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateStringValues(JToken token)
        {
            if (token == null)
                yield break;

            if (token.Type == JTokenType.String)
            {
                yield return token.Value<string>();
                yield break;
            }

            var container = token as JContainer;
            if (container == null)
                yield break;

            foreach (var child in container.Children())
                foreach (var value in EnumerateStringValues(child))
                    yield return value;
        }

        private static bool TryDecodeBase64OrDataUrl(string text, out byte[] bytes)
        {
            if (TryDecodeDataUrl(text, out bytes))
                return true;

            try
            {
                bytes = Convert.FromBase64String(text);
                return true;
            }
            catch
            {
                bytes = null;
                return false;
            }
        }

        private static bool TryDecodeDataUrl(string dataUrl, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrWhiteSpace(dataUrl))
                return false;

            var comma = dataUrl.IndexOf(',');
            if (comma < 0)
                return false;

            try
            {
                bytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));
                return true;
            }
            catch
            {
                bytes = null;
                return false;
            }
        }

        private static bool IsHttpUrl(string url) =>
            !string.IsNullOrWhiteSpace(url) &&
            (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        private static bool AtlasModelUsesImageUrlsArray(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                return true;

            var trimmed = model.Trim();
            return trimmed.IndexOf("grok-imagine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   trimmed.IndexOf("reference", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   trimmed.EndsWith("/edit", StringComparison.OrdinalIgnoreCase);
        }

        private static bool UsesAtlasFluxKontextDev(string model) =>
            !string.IsNullOrWhiteSpace(model) &&
            model.Trim().Equals(AIEventsSettings.AtlasModelFluxKontextDev, StringComparison.OrdinalIgnoreCase);

        private static bool UsesAtlasSeedreamEdit(string model) =>
            !string.IsNullOrWhiteSpace(model) &&
            model.Trim().Equals(AIEventsSettings.AtlasModelSeedreamEdit, StringComparison.OrdinalIgnoreCase);

        private static bool UsesAtlasSeedreamV5ProEdit(string model) =>
            !string.IsNullOrWhiteSpace(model) &&
            model.Trim().Equals(AIEventsSettings.AtlasModelSeedreamV5ProEdit, StringComparison.OrdinalIgnoreCase);

        private static bool UsesAtlasWanImageEdit(string model) =>
            !string.IsNullOrWhiteSpace(model) &&
            model.Trim().Equals(AIEventsSettings.AtlasModelWanImageEdit, StringComparison.OrdinalIgnoreCase);

        private static bool UsesAtlasWan25ImageEdit(string model) =>
            !string.IsNullOrWhiteSpace(model) &&
            model.Trim().Equals(AIEventsSettings.AtlasModelWan25ImageEdit, StringComparison.OrdinalIgnoreCase);

        private static JToken ParseJsonOrError(string json, string adapterName)
        {
            try
            {
                return JToken.Parse(json);
            }
            catch (Exception ex)
            {
                SetLastError(adapterName + " returned invalid JSON: " + ex.Message);
                return null;
            }
        }

        private static string ExtractJsonString(JToken root, string path)
        {
            var token = root?.SelectToken(path);
            if (token == null || token.Type == JTokenType.Null)
                return null;

            return token.Type == JTokenType.String
                ? token.Value<string>()
                : token.ToString(Formatting.None);
        }

        private static string ExtractAtlasPredictionId(string json)
        {
            var root = ParseJsonOrError(json, "Atlas Cloud image submit");
            if (root == null)
                return null;

            return ExtractJsonString(root, "data.id") ??
                   ExtractJsonString(root, "id") ??
                   FindFirstPropertyString(root, "predictionId") ??
                   FindFirstPropertyString(root, "prediction_id");
        }

        private static string ExtractAtlasUploadUrl(string json)
        {
            var root = ParseJsonOrError(json, "Atlas Cloud upload");
            if (root == null)
                return null;

            var url =
                ExtractJsonString(root, "data.download_url") ??
                ExtractJsonString(root, "data.url") ??
                ExtractJsonString(root, "download_url") ??
                ExtractJsonString(root, "url");

            return IsHttpUrl(url) ? url : FindFirstImageUrl(root);
        }

        private static string ExtractAtlasOutputUrl(JToken root)
        {
            var token =
                root?.SelectToken("data.outputs[0]") ??
                root?.SelectToken("outputs[0]") ??
                root?.SelectToken("data.output") ??
                root?.SelectToken("output");

            if (token != null)
            {
                if (token.Type == JTokenType.String)
                {
                    var text = token.Value<string>();
                    if (IsHttpUrl(text))
                        return text;
                }

                var nestedUrl = FindFirstImageUrl(token);
                if (IsHttpUrl(nestedUrl))
                    return nestedUrl;
            }

            return FindFirstImageUrl(root);
        }

        private static string ExtractAtlasError(JToken root)
        {
            var error =
                ExtractJsonString(root, "data.error") ??
                ExtractJsonString(root, "error") ??
                ExtractJsonString(root, "data.message") ??
                ExtractJsonString(root, "message");

            return string.IsNullOrWhiteSpace(error) ? "unknown error" : Shorten(error, 260);
        }

        private static string SummarizeChatContent(string json)
        {
            try
            {
                var root = JToken.Parse(json);
                var content = root.SelectToken("choices[0].message.content");
                if (content == null)
                    return "Raw response: " + Shorten(json, 260);

                var text = content.Type == JTokenType.String
                    ? content.Value<string>()
                    : content.ToString(Formatting.None);
                return "Text response: " + Shorten(text, 260);
            }
            catch
            {
                return "Raw response: " + Shorten(json, 260);
            }
        }

        private static string Shorten(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 3) + "...";
        }

        private static void SetLastError(string message)
        {
            LastError = message;
            if (PortraitRequestScope.Current != null) PortraitRequestScope.Current.Error = message;
            TaleWorlds.Library.Debug.Print("[AIPortraits] " + message);
        }

        internal static void ReportPortraitProductError(string message)
        {
            SetLastError(string.IsNullOrWhiteSpace(message)
                ? "The generated portrait product could not be committed."
                : message);
        }

        private static bool UsesImageReferenceAdapter(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                return false;

            var trimmed = model.Trim();
            return trimmed.StartsWith("gpt-image-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool UsesGeminiChatImageAdapter(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                return false;

            return model.Trim().Equals("gemini-3-pro-image-preview", StringComparison.OrdinalIgnoreCase);
        }

        private static string MapToGptImageSize(string outputSize)
        {
            if (string.IsNullOrWhiteSpace(outputSize))
                return "1024x1536";

            if (!TryParseSize(outputSize, out var width, out var height))
                return "1024x1536";

            if (width == height)
                return "1024x1024";

            return width > height ? "1536x1024" : "1024x1536";
        }

        private static string MapToAtlasAspectRatio(string outputSize)
        {
            if (string.IsNullOrWhiteSpace(outputSize))
                return "3:4";

            if (!TryParseSize(outputSize, out var width, out var height))
                return "3:4";

            var target = (float)width / height;
            var names = new[] { "2:1", "16:9", "3:2", "4:3", "1:1", "3:4", "2:3", "9:16", "1:2" };
            var ratios = new[] { 2f, 16f / 9f, 1.5f, 4f / 3f, 1f, 0.75f, 2f / 3f, 9f / 16f, 0.5f };
            var bestIndex = 0;
            var bestDistance = Math.Abs(target - ratios[0]);

            for (var i = 1; i < ratios.Length; i++)
            {
                var distance = Math.Abs(target - ratios[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return names[bestIndex];
        }

        private static string MapToAtlasWanSize(string outputSize)
        {
            if (!TryParseSize(outputSize, out var width, out var height))
                return "2K";

            return Math.Max(width, height) > 1024 ? "2K" : "1K";
        }

        private static string MapToAtlasWan25Size(string outputSize)
        {
            if (!TryParseSize(outputSize, out var width, out var height))
                return "768*1024";

            var names = new[]
            {
                "576*1344", "720*1280", "720*1680", "768*1024", "800*1200", "816*1904",
                "936*1664", "960*1280", "960*1440", "1024*768", "1024*1024", "1040*1560",
                "1104*1472", "1200*800", "1280*720", "1280*960", "1280*1280", "1344*576",
                "1440*960", "1472*1104", "1560*1040", "1664*936", "1680*720", "1904*816"
            };
            var ratios = new[]
            {
                576f / 1344f, 720f / 1280f, 720f / 1680f, 768f / 1024f, 800f / 1200f, 816f / 1904f,
                936f / 1664f, 960f / 1280f, 960f / 1440f, 1024f / 768f, 1f, 1040f / 1560f,
                1104f / 1472f, 1200f / 800f, 1280f / 720f, 1280f / 960f, 1f, 1344f / 576f,
                1440f / 960f, 1472f / 1104f, 1560f / 1040f, 1664f / 936f, 1680f / 720f, 1904f / 816f
            };
            var target = (float)width / height;
            var bestIndex = 0;
            var bestDistance = Math.Abs(target - ratios[0]);
            for (var i = 1; i < ratios.Length; i++)
            {
                var distance = Math.Abs(target - ratios[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }
            return names[bestIndex];
        }

        private static string MapToAtlasSeedreamSize(string outputSize)
        {
            if (!TryParseSize(outputSize, out var width, out var height))
                return "2048*2048";

            if (width == height)
                return "2048*2048";

            var target = (float)width / height;
            var names = new[] { "3136*1344", "2848*1600", "2496*1664", "2304*1728", "1728*2304", "1664*2496", "1600*2848" };
            var ratios = new[] { 3136f / 1344f, 2848f / 1600f, 2496f / 1664f, 2304f / 1728f, 1728f / 2304f, 1664f / 2496f, 1600f / 2848f };
            var bestIndex = 0;
            var bestDistance = Math.Abs(target - ratios[0]);

            for (var i = 1; i < ratios.Length; i++)
            {
                var distance = Math.Abs(target - ratios[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return names[bestIndex];
        }

        private static string MapToAtlasSeedreamV5Size(string outputSize)
        {
            if (TryParseSize(outputSize, out var width, out var height) && height > width)
                return AIEventsSettings.AtlasSeedreamV5ProPortraitSize;

            return AIEventsSettings.AtlasSeedreamV5ProLandscapeSize;
        }

        private static int GetAtlasFluxKontextWidth(string outputSize)
        {
            if (!TryParseSize(outputSize, out var width, out var height))
                return 1024;

            return width;
        }

        private static int GetAtlasFluxKontextHeight(string outputSize)
        {
            if (!TryParseSize(outputSize, out var width, out var height))
                return 1024;

            return height;
        }

        private static bool TryParseSize(string size, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrWhiteSpace(size))
                return false;

            var parts = size.ToLowerInvariant().Split('x');
            return parts.Length == 2 &&
                   int.TryParse(parts[0], out width) &&
                   int.TryParse(parts[1], out height) &&
                   width > 0 &&
                   height > 0;
        }

        /// <summary>
        /// Builds the generation prompt. Default mode uses the built-in structured
        /// prompt; custom mode sends exactly the text entered in the MCM prompt boxes.
        /// </summary>
        public static string BuildPrompt(PortraitPromptContext context = null)
        {
            var prompt = new StringBuilder(DefaultPromptStyle);
            AppendPromptPart(prompt, BuildIdentityRequirement(context));
            return prompt.ToString();
        }

        private static string BuildDefaultPrompt(PortraitPromptContext context)
        {
            return ExpandIdentityTokens(DefaultPromptStyle, context);
        }

        private static string GetCharacterName(PortraitPromptContext context) =>
            string.IsNullOrWhiteSpace(context?.CharacterName)
                ? "this character"
                : context.CharacterName.Trim();

        private static string GetCharacterAge(PortraitPromptContext context) =>
            context?.AgeYears != null
                ? context.AgeYears.Value.ToString()
                : "unknown";

        private static string GetCharacterCulture(PortraitPromptContext context)
        {
            if (!string.IsNullOrWhiteSpace(context?.CultureName))
                return context.CultureName.Trim();

            if (!string.IsNullOrWhiteSpace(context?.CultureId))
                return context.CultureId.Trim();

            return "unknown";
        }

		private static string GetCharacterClanTier(PortraitPromptContext context) =>
			context?.ClanTier != null ? context.ClanTier.Value.ToString() : "unknown";

		private static string GetCharacterSocialStation(PortraitPromptContext context) =>
			string.IsNullOrWhiteSpace(context?.SocialStation) ? "unranked" : context.SocialStation.Trim();

        private static string BuildCustomPromptStyle(AIEventsSettings settings, PortraitPromptContext context)
        {
            var prompt = new StringBuilder();
            AppendPromptPart(prompt, ExpandIdentityTokens(settings.PromptSuffix, context));
            AppendPromptPart(prompt, BuildIdentityRequirement(context));
            return prompt.ToString();
        }

        private static string ExpandIdentityTokens(string prompt, PortraitPromptContext context)
        {
            return (prompt ?? string.Empty)
                .Replace("[CHARACTER NAME]", GetCharacterName(context))
                .Replace("[AGE]", GetCharacterAge(context))
				.Replace("[CULTURE]", GetCharacterCulture(context))
				.Replace("[GENDER]", string.IsNullOrWhiteSpace(context?.Gender) ? "person" : context.Gender.Trim())
				.Replace("[CLAN TIER]", GetCharacterClanTier(context))
				.Replace("[SOCIAL STATION]", GetCharacterSocialStation(context));
        }

        private static string BuildIdentityRequirement(PortraitPromptContext context)
        {
            if (context == null || !context.HasMetadata)
                return string.Empty;

            var requirement = new StringBuilder("Identity requirements: ");
            requirement.Append(GetCharacterName(context));
            if (context.AgeYears.HasValue)
            {
                requirement.Append(" is exactly ");
                requirement.Append(context.AgeYears.Value);
                requirement.Append(" years old");
            }
            if (!string.IsNullOrWhiteSpace(context.Gender))
            {
                requirement.Append(", a ");
                requirement.Append(context.Gender.Trim());
            }
            requirement.Append(" of ");
            requirement.Append(GetCharacterCulture(context));
			requirement.Append(" culture");
			if (context.ClanTier.HasValue)
			{
				requirement.Append(", clan tier ");
				requirement.Append(context.ClanTier.Value);
				requirement.Append(" (");
				requirement.Append(GetCharacterSocialStation(context));
				requirement.Append(")");
			}
			requirement.Append(". Preserve the source identity and depict the stated age accurately with age-appropriate facial structure and skin. Do not make the character significantly younger or older.");
            return requirement.ToString();
        }

        private static string BuildConciseIdentityRequirement(PortraitPromptContext context)
        {
            if (context == null || !context.HasMetadata)
                return string.Empty;

            return "Identity: " + GetCharacterName(context)
                + (context.AgeYears.HasValue ? ", exact age " + context.AgeYears.Value : string.Empty)
                + (!string.IsNullOrWhiteSpace(context.Gender) ? ", " + context.Gender.Trim() : string.Empty)
                + ", " + GetCharacterCulture(context)
				+ (context.ClanTier.HasValue ? ", clan tier " + context.ClanTier.Value + " (" + GetCharacterSocialStation(context) + ")" : string.Empty)
                + ". Match stated age; do not de-age or over-age.";
        }

        private static void AppendPromptPart(StringBuilder prompt, string part)
        {
            var trimmed = part?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return;

            if (prompt.Length > 0)
                prompt.Append(" ");

            prompt.Append(trimmed);
        }
    }
}
