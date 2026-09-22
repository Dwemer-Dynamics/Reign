using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Court
{
    internal static class ReignFamilyChambersPrompt
    {
        internal static string BuildDialogueContract(IEnumerable<Hero> participants)
        {
            List<Hero> list = (participants ?? Enumerable.Empty<Hero>()).Where(hero => hero != null).ToList();
            string children = string.Join(", ", list.Where(hero => hero.IsChild)
                .Select(hero => (hero.Name?.ToString() ?? hero.StringId) + " (age " + hero.Age.ToString("0.0") + ")"));
            string adults = string.Join(", ", list.Where(hero => !hero.IsChild)
                .Select(hero => hero.Name?.ToString() ?? hero.StringId));
            return "FAMILY CHAMBERS CONTRACT: This is a benign household scene appropriate to the current time of day. "
                + "Children were already playing, studying, resting, eating, listening to a story, practicing a small skill, or otherwise occupied before the ruler entered. "
                + "Every selected adult enters with the ruler and witnesses the same shared scene. Each speaker acts and speaks only as themselves. "
                + "Use exact age-appropriate cognition and language: babies and toddlers may react, gesture, play, or babble instead of speaking in complete sentences. "
                + "Never introduce romance, sexuality, marriage negotiation, pregnancy, coercion, political office, combat, punishment, or adult social/reputation mechanics for a child. "
                + "Do not fabricate a line or action for the player. Children: " + (children.Length == 0 ? "none" : children)
                + ". Arriving adults: " + (adults.Length == 0 ? "none" : adults) + ".";
        }
    }
}
