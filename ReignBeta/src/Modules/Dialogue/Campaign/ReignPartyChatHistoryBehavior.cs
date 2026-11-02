using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using ReignBeta.Runtime;
using ReignBeta.Save;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Campaign
{
    public sealed class ReignPartyChatHistoryBehavior : CampaignBehaviorBase
    {
        public static ReignPartyChatHistoryBehavior Instance { get; private set; }
        private NativeCampaign _owner;
        public int Epoch { get; private set; }
        internal ReignPartyChatContinuation Last { get; private set; }
        public override void RegisterEvents() { _owner = NativeCampaign.Current; Instance = this; }
        internal bool Owns(int epoch) => Instance == this && _owner == NativeCampaign.Current && Epoch == epoch;
        internal void Store(ReignPartyChatContinuation snapshot, int epoch)
        {
            if (Owns(epoch)) Last = snapshot;
        }
        internal void EndCampaign() { Epoch++; Last = null; if (Instance == this) Instance = null; }

        public override void SyncData(IDataStore store)
        {
            if (store.IsSaving) ReignPartyChatScreenManager.ActiveViewModel?.PersistPartyChat();
            List<string> chunks = store.IsSaving && Last != null
                ? ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(Last)) : null;
            store.SyncData("_reign_party_chat_continuation_v1", ref chunks);
            if (!store.IsLoading) return;
            Epoch++;
            Last = null;
            if (chunks == null || chunks.Count == 0) return; // Pre-feature saves remain compatible.
            Last = JsonConvert.DeserializeObject<ReignPartyChatContinuation>(ReignSavePayloadCodec.Decode(chunks))
                ?? throw new InvalidOperationException("Saved party chat could not be restored.");
        }
    }
}
