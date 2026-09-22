using System;
using System.Linq;

namespace ReignBeta.Court
{
    public partial class ReignCourtCampaignBehavior
    {
        internal bool OwnsCourtAudience(object matter) => ReferenceEquals(Instance, this)
            && ((matter is ReignDocketPetition petition && (_rulerDocketState?.Petitions.Contains(petition) ?? false))
                || (matter is ReignNobleDocketMatter noble && (_rulerDocketState?.NobleMatters.Contains(noble) ?? false))
                || (matter is ReignCourtLifeMatter life && (_rulerDocketState?.CourtLifeMatters.Contains(life) ?? false)));

        internal void AttachCourtAudienceArt(object matter, string path)
        {
            if (!OwnsCourtAudience(matter)) return;
            string id;
            if (matter is ReignDocketPetition petition) { petition.SceneAssetPath = path; id = petition.PetitionId; }
            else if (matter is ReignNobleDocketMatter noble) { noble.SceneAssetPath = path; id = noble.MatterId; }
            else if (matter is ReignCourtLifeMatter life) { life.SceneAssetPath = path; id = life.MatterId; }
            else return;
            foreach (ReignDocketHistoryRecord history in _rulerDocketState.History.Where(x => x.PetitionId == id))
                history.SceneAssetPath = path;
            StateChanged?.Invoke();
        }
    }
}
