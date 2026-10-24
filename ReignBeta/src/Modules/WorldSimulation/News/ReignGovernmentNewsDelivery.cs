using Reign.Core.Contracts.Government;
using Reign.Core.Contracts.World;
using ReignBeta.UI;
using ReignBeta.Runtime;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNewsCampaignBehavior
    {
        internal void PublishGovernmentEdition(GovernmentNewsEdition edition)
            => Publish(GovernmentArticle(edition));

        internal void OpenGovernmentArchive(GovernmentNewsEdition edition)
        {
            // Explicit archive reading does not dequeue a pending report or manufacture
            // another publication. The same screen owns pause/focus/restoration.
            if (edition == null || ReignDiplomacyAnnouncementScreenManager.IsOpen) return;
            ReignDiplomacyAnnouncementScreenManager.Open(ToAnnouncement(GovernmentArticle(edition)), () => { }, () => { });
        }

        private static ReignNewsArticle GovernmentArticle(GovernmentNewsEdition edition)
            => new ReignNewsArticle {
                Id = edition.Id, Category = ReignNewsCategories.Government,
                Title = "The Reign Herald", Headline = edition.Headline,
                Body = edition.Body, WorldDay = ReignCalendarService.TryFromAbsoluteCalendarDay(edition.PublishedDay, out var time) ? time.ToDays : 0,
                GovernmentNews = edition,
                Consequences = "Presented matters await their decision session. Approval of an obligation does not mean that it has been fulfilled." };
    }
}
