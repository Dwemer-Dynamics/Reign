using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static string StorySceneSessionId(ReignSocialEventSession session) =>
            "npc_story_scene_" + session.PersonalStory.Id + "_" + session.PersonalStory.Revision;
        internal static async Task<JObject> StartNpcStorySceneAsync(ReignSocialEventSession session)
        {
            var story = session.PersonalStory;
            Hero hero = await ReignMainThread.InvokeAsync(() => World.ReignObjectResolver.FindHero(story.HeroId)).ConfigureAwait(false);
            JObject context = await ReignMainThread.InvokeAsync(() => {
                var p = CaptureNpcLivesContext(ReignNpcLivesBehavior.Instance?.Generation ?? "");
                p["sessionId"] = StorySceneSessionId(session); p["heroId"] = story.HeroId;
                p["npcStoryId"] = story.Id; p["npcLivesRevision"] = story.Revision;
                p["conversationMode"] = "npc_story"; p["suppressRelationshipAssessment"] = true;
                return p;
            }).ConfigureAwait(false);
            var opened = await StartConversationSessionWithIdentityAsync(hero, context).ConfigureAwait(false);
            if (!opened.Ok || opened.SessionId != context.Value<string>("sessionId")) throw new InvalidOperationException("The story conversation could not be opened.");
            session.PersonalStoryContext = context;
            var history = await NpcLivesRequestAsync("history", context).ConfigureAwait(false);
            if (history.Value<bool?>("ok") != true) throw new InvalidOperationException(history.Value<string>("error") ?? "The story transcript could not be restored.");
            return history;
        }
        internal static async Task<ReignSocialEventTurnReply> RequestNpcStoryTurnAsync(ReignSocialEventSession session, string text,
            string turnId, Reign.Core.Contracts.Actions.ActionAgreement actionDraft)
        {
            await session.PersonalStoryReady.ConfigureAwait(false);
            var story = session.PersonalStory;
            var owner = await ReignMainThread.InvokeAsync(() => ReignNpcLivesBehavior.Instance).ConfigureAwait(false);
            await owner.RefreshLifeSnapshotAsync().ConfigureAwait(false);
            Hero hero = await ReignMainThread.InvokeAsync(() => {
                var current = owner.State.Stories.FirstOrDefault(s => s.Id == story.Id);
                if (current == null || current.Status != "active" || current.Revision != story.Revision)
                    throw new InvalidOperationException("This chapter has ended or changed. Reopen Personal lives to continue.");
                string place = TaleWorlds.CampaignSystem.Settlements.Settlement.CurrentSettlement?.StringId ?? "";
                var npc = World.ReignObjectResolver.FindHero(story.HeroId);
                if (npc == null || npc.IsPrisoner || !npc.IsAlive || npc.PartyBelongedTo != TaleWorlds.CampaignSystem.Party.MobileParty.MainParty
                    || story.LocationId.Length > 0 && story.LocationId != place)
                    throw new InvalidOperationException("The required companion or story location is no longer available.");
                return npc;
            }).ConfigureAwait(false);
            var reply = await RequestDialogueResponseAsync(hero, text, StorySceneSessionId(session),
                correlationId: turnId, conversationContext: session.PersonalStoryContext, actionDraft: actionDraft).ConfigureAwait(false);
            var result = new ReignSocialEventTurnReply { Ok = reply.Ok, Error = reply.Error, TurnId = turnId, RawResponse = reply.RawResponse };
            if (reply.Ok)
            {
                result.ParticipantResults.Add(new ReignEventReply { Ok = true, HeroStringId = story.HeroId, Text = reply.Text,
                    Emotion = reply.Emotion, Intent = reply.Intent, RawResponse = reply.RawResponse });
                result.AddressedHeroStringIds.Add(story.HeroId);
                await owner.RefreshLifeSnapshotAsync().ConfigureAwait(false);
            }
            return result;
        }
    }
}
