using Reign.Core.Contracts.Actions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Relationships;
using Reign.Core.Contracts.Relationships;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignRelationshipCampaignBehavior
    {
        private List<string> _marriageJournal = new List<string>();
        private List<string> _directorOutbox = new List<string>();
        private List<string> _directorOutboxChunks = new List<string>();
        private long _lastDirectorCapturePeriod = -1;
        private string _directorCaptureTimeline = "";
        private long _directorCaptureGaps;
        private long _lastDirectorCompletedPeriod=-1;
        private DateTime _nextDirectorUploadUtc;
        private readonly RelationshipSnapshotHeaderCache _directorHeader = new RelationshipSnapshotHeaderCache();
        private long _lastDirectorDocumentChars;
        private double _lastDirectorParseMs;
        private bool _lastDirectorParseOnMainThread;

        // These are advisory headers of canonical serialized queue documents;
        // full document validation remains in the upload/recovery paths.
        private static object ReadMarriageHeader(string document, string property)
        {
            using (var text = new StringReader(document))
            using (var reader = new JsonTextReader(text))
                while (reader.Read())
                    if (reader.Depth == 1 && reader.TokenType == JsonToken.PropertyName && (string)reader.Value == property)
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Marriage snapshot header is missing.");
                        return reader.Value;
                    }
            return null;
        }
        private static double ReadDirectorQueuedDay(string document)
        {
            object value = ReadMarriageHeader(document, "worldDay");
            if (value == null) throw new InvalidOperationException("Marriage snapshot day is missing.");
            return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        internal JObject ArrangedMarriageDiagnostics()
        {
            if (_directorOutbox.Count == 0) _directorHeader.Clear();
            double oldest = _directorOutbox.Count == 0 ? -1 : _directorHeader.ReadDay(_directorOutbox[0], ReadDirectorQueuedDay);
            return new JObject {
            ["schema"]="reign-arranged-marriage-v1", ["policyVersion"]=ArrangedMarriagePolicy.Version,
            ["capturedPeriod"]=_lastDirectorCapturePeriod,["queuedPeriods"]=_directorOutbox.Count,
            ["uncapturedPeriods"]=_directorCaptureGaps,["uploadInFlight"]=_directorSnapshotInFlight,
            ["completedPeriod"]=_lastDirectorCompletedPeriod,
            ["oldestQueuedDay"]=oldest,
            ["lagDays"]=_directorOutbox.Count==0?0:Math.Max(0,CampaignTime.Now.ToDays-oldest),
            ["lastDocumentChars"]=_lastDirectorDocumentChars, ["lastParseDurationMs"]=_lastDirectorParseMs,
            ["lastParseOnMainThread"]=_lastDirectorParseOnMainThread,
            ["recoveryTransactions"]=_marriageJournal.Count(x=>Convert.ToString(ReadMarriageHeader(x,"State"))=="recovery") };
        }

        private void SyncMarriageOutbox(IDataStore dataStore)
        {
            if(dataStore.IsSaving)
                _directorOutboxChunks=ReignBeta.Save.ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_directorOutbox));
            dataStore.SyncData("_reignMarriageOutboxChunksV1",ref _directorOutboxChunks);
            if(dataStore.IsLoading)
            {
                _directorHeader.Clear();
                _directorOutbox=_directorOutboxChunks?.Count>0
                    ?JsonConvert.DeserializeObject<List<string>>(ReignBeta.Save.ReignSavePayloadCodec.Decode(_directorOutboxChunks))
                        ??throw new InvalidOperationException("Marriage outbox save payload is null.")
                    :new List<string>();
            }
        }

        private void CaptureDirectorPeriod(double day)
        {
            string timeline=ReignWorldHistoryCampaignBehavior.Instance?.TimelineId??"main";
            long period=ArrangedMarriagePolicy.Period(day);
            if(_directorCaptureTimeline!=timeline) { _directorOutbox.Clear(); _directorHeader.Clear(); _lastDirectorCapturePeriod=-1; _lastDirectorCompletedPeriod=-1; _directorCaptureTimeline=timeline; }
            if(period<=_lastDirectorCapturePeriod)return;
            if(_directorOutbox.Count>=64) { ReignLog.Warn("Marriage outbox is full; capture is blocked, not acknowledged."); return; }
            using var timing=ReignLog.MeasureWork("relationships.capture",0f);
            var snapshot=ReignServerClient.CaptureMarriageDirectorSnapshot();
            if(_lastDirectorCapturePeriod>=0) _directorCaptureGaps+=Math.Max(0,period-_lastDirectorCapturePeriod-1);
            string document=snapshot.ToString(Formatting.None);
            _directorOutbox.Add(document);
            if(_directorOutbox.Count==1) _directorHeader.ReadDay(document,_=>snapshot.Value<double>("worldDay"));
            _lastDirectorCapturePeriod=period;
        }

        private async System.Threading.Tasks.Task UploadDirectorPeriodAsync()
        {
            if(_directorSnapshotInFlight||_directorOutbox.Count==0||DateTime.UtcNow<_nextDirectorUploadUtc
                ||ReignSaveSyncCoordinator.IsAlignmentPending||ReignCampaignInitializationGate.IsPending)return;
            string captured=_directorOutbox[0];
            _directorSnapshotInFlight=true;
            var campaign=TaleWorlds.CampaignSystem.Campaign.Current;
            try
            {
                long capturedPeriod = -1;
                var response=await System.Threading.Tasks.Task.Run(async () => {
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    _lastDirectorParseOnMainThread=ReignMainThread.IsMainThread;
                    var payload=JObject.Parse(captured);
                    capturedPeriod=payload.Value<long>("periodIndex");
                    _lastDirectorDocumentChars=captured.Length;
                    _lastDirectorParseMs=timer.Elapsed.TotalMilliseconds;
                    return await ReignServerClient.UploadMarriageDirectorSnapshotAsync(payload).ConfigureAwait(false);
                }).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(()=> {
                    if(!ReferenceEquals(Instance,this)||!ReferenceEquals(campaign,TaleWorlds.CampaignSystem.Campaign.Current))return;
                    if(ArrangedMarriagePolicy.CanAcknowledge(capturedPeriod,
                    response.Value<long?>("periodIndex")??-1,response.Value<bool?>("ok")==true,response.Value<bool?>("skipped")==true)
                        &&_directorOutbox.Count>0&&_directorOutbox[0]==captured)
                    { _directorOutbox.RemoveAt(0); _directorHeader.Clear(); _lastDirectorCompletedPeriod=response.Value<long>("periodIndex"); }
                }).ConfigureAwait(false);
            }
            catch(Exception ex) { ReignLog.Warn("Marriage period upload retained for retry: "+ex.Message); }
            finally
            {
                await ReignMainThread.InvokeAsync(() => {
                    _nextDirectorUploadUtc=DateTime.UtcNow.AddSeconds(2); _directorSnapshotInFlight=false;
                }).ConfigureAwait(false);
            }
        }

        private static bool NativeRoyal(Hero hero)
            => hero?.Clan?.Kingdom?.RulingClan==hero?.Clan && hero?.Clan!=null
                || Kingdom.All.Any(k=>!k.IsEliminated && k.Leader!=null && (hero?.Father==k.Leader||hero?.Mother==k.Leader));

        internal JObject ApplyArrangedMarriage(ReignDirectorAction action, bool playerConsent = false, ActionAgreement consideration = null)
        {
            var campaign=TaleWorlds.CampaignSystem.Campaign.Current;
            JObject Receipt(string status,string error="")=>new JObject {
                ["directorActionId"]=action.DirectorActionId,["status"]=status,["error"]=error,
                ["worldDay"]=CampaignTime.Now.ToDays,["timelineId"]=ReignWorldHistoryCampaignBehavior.Instance?.TimelineId??"main",
                ["marriageVerified"]=status=="completed",["paymentVerified"]=status=="completed" };
            if(!ReferenceEquals(Instance,this))return Receipt("obsolete","The originating campaign instance was replaced.");
            var waiting = Wedding(action.DirectorActionId);
            if (waiting != null) return WeddingDirectorReceipt(waiting);
            if(campaign?.SaveHandler?.IsSaving==true||campaign?.ConversationManager?.IsConversationInProgress==true
                ||ReignSaveSyncCoordinator.IsAlignmentPending||ReignCampaignInitializationGate.IsPending)
                return Receipt("deferred","Waiting for a safe native transaction boundary.");
            var terms=action.Payload?["marriageTerms"] as JObject;
            string timeline=action.Payload?.Value<string>("timelineId");
            if(timeline!=(ReignWorldHistoryCampaignBehavior.Instance?.TimelineId??"main")
                ||(action.Payload?.Value<double?>("scheduledWorldDay")??0)>CampaignTime.Now.ToDays)
                return Receipt("obsolete","The marriage belongs to a different save/timeline.");
            if(terms?.Value<int?>("policyVersion")!=ArrangedMarriagePolicy.Version)
                return Receipt("invalid","Legacy arranged proposal requires a new monetary quote.");
            Hero a=FindHero(action.ActorId),b=FindHero(action.TargetId);
            if(a==null||b==null||a.Clan==null||b.Clan==null||a==Hero.MainHero||b==Hero.MainHero
                ||!playerConsent&&(a.Clan==Clan.PlayerClan||b.Clan==Clan.PlayerClan))
                return Receipt("obsolete","The exact approved NPC couple is unavailable or requires player consent.");
            var prior=_marriageJournal.Select(JObject.Parse).FirstOrDefault(x=>x.Value<string>("Id")==action.DirectorActionId);
            var journal=prior?.ToObject<MarriagePaymentJournal>()??new MarriagePaymentJournal {Id=action.DirectorActionId,Fee=terms.Value<int>("fee")};
            bool Married()=>a.Spouse==b&&b.Spouse==a;
            if(journal.State=="completed")
            {
                var replay=Married()?Receipt("completed"):Receipt("invalid","Saved marriage receipt does not match native spouses.");
                replay["fee"]=journal.Fee;return replay;
            }
            if(journal.State!="quoted")return Receipt("recovery","Marriage transaction is fenced for recovery: "+journal.State);
            bool Eligible()=> a.IsAlive&&b.IsAlive && a.Clan.StringId==terms.Value<string>("clanAId")&&b.Clan.StringId==terms.Value<string>("clanBId")
                &&a.Clan.Leader?.StringId==terms.Value<string>("leaderAId")&&b.Clan.Leader?.StringId==terms.Value<string>("leaderBId")
                &&a.Clan.Tier==terms.Value<int>("tierA")&&b.Clan.Tier==terms.Value<int>("tierB")
                &&a.Clan.Fiefs.Count==terms.Value<int>("fiefsA")&&b.Clan.Fiefs.Count==terms.Value<int>("fiefsB")
                &&NativeRoyal(a)==terms.Value<bool>("royalA")&&NativeRoyal(b)==terms.Value<bool>("royalB")
                &&ArrangedMarriagePolicy.RoyalAllowed(ReignCampaignIdentity.CurrentCampaignId(),a.StringId,NativeRoyal(a),b.Clan.Fiefs.Count,terms.Value<int>("nativeYear"))
                &&ArrangedMarriagePolicy.RoyalAllowed(ReignCampaignIdentity.CurrentCampaignId(),b.StringId,NativeRoyal(b),a.Clan.Fiefs.Count,terms.Value<int>("nativeYear"))
                &&a.Spouse==null&&b.Spouse==null&&campaign.Models.MarriageModel.IsCoupleSuitableForMarriage(a,b);
            if((action.Payload?.Value<double?>("scheduledWorldDay")??CampaignTime.Now.ToDays)+14d<CampaignTime.Now.ToDays)
                return Receipt("obsolete","The arranged proposal expired before execution.");
            if(!Eligible()||ArrangedMarriagePolicy.Fee(a.Clan.Tier,b.Clan.Tier)!=journal.Fee)
                return Receipt("obsolete","The couple or quoted clan standing changed; a new proposal is required.");
            Hero payer=FindHero(terms.Value<string>("payerId")),payee=FindHero(terms.Value<string>("payeeId"));
            if(payer==null||payee==null||payer.Gold<journal.Fee)return Receipt("obsolete","Quoted funds are no longer available.");
            if (WeddingFeatureReady)
            {
                var package = consideration ?? new ActionAgreement { Id = action.DirectorActionId, PlayerId = payer.StringId, NpcId = payee.StringId };
                if (journal.Fee > 0)
                {
                    var fee = ReignWeddingBargain.Asset("give.gold", payer, payee, journal.Fee);
                    fee.Id = "arranged_fee";
                    package.Clauses.Add(fee);
                }
                if (!RequestWedding(action.DirectorActionId, a, b, package, "", 0, out var match,
                    trustedConsideration: consideration != null))
                    return Receipt("invalid", "The approved couple could not be reserved for a wedding.");
                if (action.DirectorActionId.StartsWith("director_action_", StringComparison.Ordinal))
                    match.DirectorAction = true;
                return WeddingDirectorReceipt(match);
            }
            void SaveJournal()
            {
                _marriageJournal.RemoveAll(x=>JObject.Parse(x).Value<string>("Id")==journal.Id);
                _marriageJournal.Add(JObject.FromObject(journal).ToString(Formatting.None));
            }
            try
            {
                bool ok=MarriagePaymentTransaction.Execute(journal,Married,Eligible,()=>payer.Gold,()=>payee.Gold,
                    amount=> { if(amount>0)GiveGoldAction.ApplyBetweenCharacters(payer,payee,amount,true);
                        else if(amount<0)GiveGoldAction.ApplyBetweenCharacters(payee,payer,-amount,true); },
                    ()=>MarriageAction.Apply(a,b,true),SaveJournal);
                var receipt=Receipt(ok?"completed":"obsolete"); receipt["fee"]=journal.Fee; receipt["payerId"]=payer.StringId; receipt["payeeId"]=payee.StringId;
                return receipt;
            }
            catch(Exception ex) { return Receipt(journal.State=="refunded"?"invalid":"recovery",ex.Message); }
        }
    }
}
