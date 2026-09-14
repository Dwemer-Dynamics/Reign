using System;
using ReignBeta.Settings;

namespace AIEventsAndIntrigue.Settings
{
    public sealed class AIEventsSettings
    {
        public const string ProviderNanoGpt = "NanoGPT";
        public const string ProviderAtlasCloud = "Atlas Cloud";
        public const string AtlasModelFluxKontextDev = "black-forest-labs/flux-kontext-dev";
        public const string AtlasModelSeedreamEdit = "bytedance/seedream-v4.5/edit";
        public const string AtlasModelSeedreamV5ProEdit = "bytedance/seedream-v5.0-pro/edit";
        public const string AtlasSeedreamV5ProLandscapeSize = "2720*1530";
        public const string AtlasSeedreamV5ProPortraitSize = "1530*2720";
        public const string AtlasModelWan25ImageEdit = "alibaba/wan-2.5/image-edit";
        public const string AtlasModelWanImageEdit = "alibaba/wan-2.7-pro/image-edit";
        public const string AtlasModelGrokImageQualityEdit = "xai/grok-imagine-image-quality/edit";

        private static readonly AIEventsSettings Adapter = new AIEventsSettings();

        public static AIEventsSettings Instance => Adapter;

        public bool EventsEnabled => Settings?.EventsEnabled ?? true;
        public bool TournamentCelebrationsEnabled => Settings?.TournamentCelebrationsEnabled ?? true;
        public bool EventRelationshipChangesEnabled => Settings?.EventRelationshipChangesEnabled ?? true;
        public bool ModEnabled => Settings?.AiPortraitsEnabled ?? true;
        public bool EnableAIInfluenceVisualResponses => false;
        public bool GameMasterLiveCallsEnabled => false;
        public bool AIInfluenceHistoryWritesEnabled => false;
        public bool PromptOutboxEnabled => false;
        public bool LogUIMovies => false;
        public bool TextureTraceEnabled => false;
        public bool UseDefaultPrompt => false;
        public string PromptSuffix => AIPortraits.NanoGptClient.DefaultPromptStyle;
        public int MaxConcurrentRequests => 2;
        public string ApiKey => string.Empty;
        public string AtlasApiKey => string.Empty;
        public string SelectedModel => "gpt-image-1.5";
        public string SelectedAtlasModel => AtlasModelWan25ImageEdit;
        public string OutputSize => "768x1024";
        public float ImageStrength => 0.75f;
        public int InferenceSteps => 28;
        public float GuidanceScale => 3.5f;
        public bool UsesAtlasCloud => false;
        public string ActiveImageProvider => "Bannerlord Reign Server";
        public string ActiveImageModel => "server-configured";
        public string ActiveApiKey => string.Empty;
        public bool IsNanoGptApiKeySet => true;
        public bool IsAtlasApiKeySet => true;
        public bool IsApiKeySet => true;

        private static ReignBetaSettings Settings => ReignBetaSettings.Instance;

        private static string Clean(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

    }
}
