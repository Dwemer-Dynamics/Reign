using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AIPortraits;
using ReignBeta.Court;
using ReignBeta.UI.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Integration
{
    internal static class ReignRulerPetitionSceneClient
    {
        internal static Settlement Host(ReignCourtCampaignBehavior court, object matter)
        {
            // Court Life records its host; petitions' ParentTownId is the beneficiary, not the hall.
            if (matter is ReignCourtLifeMatter life) return Settlement.Find(life.SettlementId ?? "");
            return Settlement.Find(court.Session?.HostSettlementStringId ?? "") ?? Settlement.CurrentSettlement;
        }

        internal static ReignCourtAudienceScenePresentation Create(ReignCourtCampaignBehavior court,
            object matter, Func<string> publicContext, Action<string> show)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            Func<bool> owns = () => ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current)
                && campaignId == ReignCampaignIdentity.CurrentCampaignId() && court.OwnsCourtAudience(matter);
            return new ReignCourtAudienceScenePresentation(
                () => Snapshot(court, matter, campaignId, publicContext()), owns,
                scene => scene.HostId == HostId(court, matter) && scene.RosterStamp == RosterStamp(matter),
                path => court.AttachCourtAudienceArt(matter, path), show,
                court.BeginCourtLifeConversation, court.EndCourtLifeConversation);
        }

        private static CourtSceneSnapshot Snapshot(ReignCourtCampaignBehavior court, object matter,
            string campaignId, string publicContext)
        {
            Settlement host = Host(court, matter);
            string root = FindModuleRoot();
            if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("The court image folder is unavailable.");
            string culture = host?.Culture?.StringId ?? "generic";
            string reference = Path.Combine(root, "GUI", "UiCalibration", "reference-scenes",
                ReignCourtAudienceScene.HallReference(culture));
            var scene = new CourtSceneSnapshot
            {
                CampaignId = campaignId, HostId = HostId(court, matter), RosterStamp = RosterStamp(matter),
                CultureId = culture, PublicContext = publicContext,
                Hall = File.ReadAllBytes(reference), Folder = Path.Combine(root, "CourtPetitionArt")
            };
            var ids = new List<string>();
            if (matter is ReignDocketPetition petition)
            {
                scene.AudienceId = petition.PetitionId; scene.TimelineId = petition.TimelineId;
                ids.Add(petition.PetitionerHeroId); ids.AddRange(petition.ActiveAudienceHeroIds);
            }
            else if (matter is ReignNobleDocketMatter noble)
            {
                scene.AudienceId = noble.MatterId; scene.TimelineId = noble.TimelineId;
                ids.AddRange(noble.Participants.Select(p => p.HeroId)); ids.AddRange(noble.ActiveAudienceHeroIds);
            }
            else if (matter is ReignCourtLifeMatter life)
            {
                scene.AudienceId = life.MatterId; scene.TimelineId = life.TimelineId;
                foreach (ReignCourtLifeParticipant person in life.Participants)
                {
                    if (!string.IsNullOrWhiteSpace(person.HeroId)) { ids.Add(person.HeroId); continue; }
                    // Reuse the exact deterministic appearance shown on this guest's native card.
                    var identifier = ReignCourtLifeAttendeeVM.BuildGuestPortrait(person, life.SettlementId);
                    scene.People.Add(new CourtScenePerson
                    {
                        Id = person.ActorId, Name = person.Name, Age = ReignCourtAudienceScene.ReferenceAge(person.Age),
                        CharacterCode = StableCharacterCode(identifier?.Id),
                        ReferenceStamp = ReignCourtAudienceScene.Hash(Encoding.UTF8.GetBytes(StableCharacterCode(identifier?.Id)))
                    });
                }
            }
            else throw new InvalidOperationException("Unknown court audience type.");
            foreach (string id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (id == Hero.MainHero?.StringId) continue;
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);
                if (hero == null)
                {
                    // A known deceased participant is off-scene; an unresolved identity is an error.
                    if (Hero.DeadOrDisabledHeroes.Any(h => h.StringId == id && !h.IsAlive)) continue;
                    throw new InvalidOperationException("An involved courtier could not be found: " + id);
                }
                if (!hero.IsAlive || hero.IsPrisoner) continue;
                bool present = hero.CurrentSettlement == host || hero.PartyBelongedTo?.CurrentSettlement == host
                    || (host != null && host.HeroesWithoutParty.Contains(hero));
                if (host != null && !present) continue;
                string portraitKey = CharacterCacheId.ForHero(hero);
                byte[] portrait = hero.IsChild ? null : PortraitCache.GetDiskBytes(portraitKey);
                string code = StableCharacterCode(CharacterCode.CreateFrom(hero.CharacterObject)?.Code);
                scene.People.Add(new CourtScenePerson
                {
                    Id = hero.StringId, Name = hero.Name?.ToString() ?? hero.StringId, Age = ReignCourtAudienceScene.ReferenceAge(hero.Age),
                    CharacterCode = code, Portrait = portrait,
                    ReferenceStamp = portrait?.Length > 0 ? ReignCourtAudienceScene.Hash(portrait)
                        : ReignCourtAudienceScene.Hash(Encoding.UTF8.GetBytes(code ?? ""))
                });
            }
            scene.TimelineId = string.IsNullOrWhiteSpace(scene.TimelineId) ? "main" : scene.TimelineId;
            scene.People = ReignCourtAudienceScene.CompleteCast(scene.People);
            return scene;
        }

        private static string FindModuleRoot()
        {
            DirectoryInfo directory = new FileInfo(typeof(ReignRulerPetitionSceneClient).Assembly.Location).Directory;
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SubModule.xml"))) return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }

        private static string HostId(ReignCourtCampaignBehavior court, object matter) =>
            Host(court, matter)?.StringId ?? (matter as ReignCourtLifeMatter)?.SettlementId ?? "";

        private static string StableCharacterCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            CharacterCode code = CharacterCode.CreateFrom(raw);
            if (code == null || code.IsEmpty) return "";
            BodyProperties body = code.BodyProperties;
            code.BodyProperties = new BodyProperties(new DynamicBodyProperties(
                (float)ReignCourtAudienceScene.ReferenceAge(body.Age), body.Weight, body.Build), body.StaticProperties);
            return code.CreateNewCodeString();
        }

        private static string RosterStamp(object matter)
        {
            IEnumerable<string> ids;
            if (matter is ReignDocketPetition petition)
                ids = new[] { petition.PetitionerHeroId }.Concat(petition.ActiveAudienceHeroIds);
            else if (matter is ReignNobleDocketMatter noble)
                ids = noble.Participants.Select(p => p.HeroId).Concat(noble.ActiveAudienceHeroIds);
            else if (matter is ReignCourtLifeMatter life)
                ids = life.Participants.Select(p => string.IsNullOrWhiteSpace(p.HeroId) ? p.ActorId : p.HeroId);
            else throw new InvalidOperationException("Unknown audience.");
            return string.Join("|", ids.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
        }
    }
}
