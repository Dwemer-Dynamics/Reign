using ArenaOverhaul.TeamTournament;
using ArenaOverhaul.Tournament;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace Reign.Mcp.Tests
{
    public sealed class ArenaCompanionEligibilityTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void RecruitedCivilianAppearsInOwnPartySelection(bool notable, bool noncombatant)
        {
            var hero = new Hero { CompanionOf = new object(), IsNotable = notable, IsNoncombatant = noncombatant };
            Assert.True(new CharacterObject(hero).CanBeAParticipant(false, false));
        }

        [Theory]
        [InlineData("dead")]
        [InlineData("child")]
        [InlineData("wounded")]
        [InlineData("prisoner")]
        public void CompanionStatusNeverOverridesUnavailableState(string state)
        {
            var hero = new Hero
            {
                CompanionOf = new object(), IsNotable = true, IsNoncombatant = true,
                IsDead = state == "dead", IsChild = state == "child",
                IsWounded = state == "wounded", IsPrisoner = state == "prisoner"
            };
            Assert.False(new CharacterObject(hero).CanBeAParticipant(false, true));
            Assert.False(new CharacterObject(hero).CanJoinPlayerTeam());
            Assert.False(TournamentHeroEligibility.CanParticipate(hero, true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CivilianWithoutConfirmedCompanionMembershipRemainsExcluded(bool allowNotables)
        {
            // Same-town presence, clan affiliation or temporary travel alone does
            // not supply CompanionOf and must not bypass civilian eligibility.
            var civilian = new Hero { IsNoncombatant = true, IsNotable = true };
            Assert.False(new CharacterObject(civilian).CanBeAParticipant(false, allowNotables));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OrdinaryCombatantNotableStillUsesNotableSetting(bool allowNotables)
        {
            var notable = new Hero { IsNotable = true };
            Assert.Equal(allowNotables, new CharacterObject(notable).CanBeAParticipant(false, allowNotables));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void SettlementRecruitmentKeepsExistingMeleeSkillThreshold(int qualifyingSkill)
        {
            var hero = new Hero { CompanionOf = new object(), IsNotable = true, IsNoncombatant = true };
            var character = new CharacterObject(hero);
            Assert.False(character.CanBeAParticipant(true));
            var skill = new[] { DefaultSkills.OneHanded, DefaultSkills.TwoHanded, DefaultSkills.Polearm }[qualifyingSkill];
            hero.Skills[skill] = 99;
            Assert.False(character.CanBeAParticipant(true));
            hero.Skills[skill] = 100;
            Assert.True(character.CanBeAParticipant(true));
            hero.IsWounded = true;
            Assert.False(character.CanBeAParticipant(true));
        }

        [Theory]
        [InlineData(2, true, false)]
        [InlineData(3, true, true)]
        [InlineData(1, false, true)]
        public void RegularTroopSelectionKeepsItsExistingTierRules(int tier, bool considerSkills, bool expected)
        {
            Assert.Equal(expected, new CharacterObject { Tier = tier }.CanBeAParticipant(considerSkills));
        }

        [Fact]
        public void OrdinaryHeroesRemainEligibleAndMissingHeroIsRejected()
        {
            Assert.True(new CharacterObject(new Hero()).CanBeAParticipant(false));
            Assert.False(TournamentHeroEligibility.CanParticipate(null!, false));
        }

        [Fact]
        public void ApplicantGenerationSharesGuardAndRecognizesCompanions()
        {
            string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
                "ReignBeta/src/Modules/Arena/ArenaOverhaul/Tournament/AbstractTournamentApplicantManager.cs"));
            Assert.Contains("TournamentHeroEligibility.CanParticipate(hero, allowNotables)", source);
            Assert.Contains("hero.IsLord || hero.IsWanderer || hero.CompanionOf != null", source);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ActualPartyCivilianOrGuestCanJoinWithoutCompanionRegistration(bool notable)
        {
            var guest = new CharacterObject(new Hero { IsNotable = notable, IsNoncombatant = true });
            Assert.True(guest.CanJoinPlayerTeam());
            Assert.False(guest.CanBeAParticipant(false));
            Assert.True(new CharacterObject { Tier = 1 }.CanJoinPlayerTeam());
        }

        [Fact]
        public void CompletePartyRosterStaysOutOfOpponentsEvenWhenMembersWereNotSelected()
        {
            var player = new CharacterObject(new Hero()) { IsPlayerCharacter = true };
            var unusedCompanion = new CharacterObject(new Hero { CompanionOf = new object() });
            var guest = new CharacterObject(new Hero { IsNotable = true, IsNoncombatant = true });
            var selectedAlly = new CharacterObject(new Hero());
            var enemy = new CharacterObject(new Hero());
            var soldier = new CharacterObject { Tier = 3 };
            var party = new[] { player, unusedCompanion, guest, soldier };
            var opponents = TeamTournamentRosterPolicy.BuildOpponents(
                new[] { player, unusedCompanion, guest, selectedAlly, enemy }, new[] { soldier },
                Array.Empty<CharacterObject>(), party, new[] { player, selectedAlly }, true, 24);

            Assert.Equal(24, opponents.Count);
            Assert.Same(enemy, opponents[0]);
            Assert.DoesNotContain(player, opponents);
            Assert.DoesNotContain(unusedCompanion, opponents);
            Assert.DoesNotContain(guest, opponents);
            Assert.DoesNotContain(selectedAlly, opponents);
            // Another party's identical soldier template remains a valid rival.
            Assert.Equal(23, opponents.Count(x => x == soldier));
        }

        [Fact]
        public void SpectatingDoesNotBanUnselectedPartyHeroesFromCompeting()
        {
            var companion = new CharacterObject(new Hero());
            var result = TeamTournamentRosterPolicy.BuildOpponents(new[] { companion },
                Array.Empty<CharacterObject>(), Array.Empty<CharacterObject>(), new[] { companion },
                Array.Empty<CharacterObject>(), false, 1);
            Assert.Same(companion, Assert.Single(result));
        }

        [Fact]
        public void DuplicateLocationEntriesNeverCreateDuplicateHeroOpponents()
        {
            var hero = new CharacterObject(new Hero());
            var soldier = new CharacterObject { Tier = 3 };
            var result = TeamTournamentRosterPolicy.BuildOpponents(new[] { hero, hero },
                new[] { soldier }, Array.Empty<CharacterObject>(), Array.Empty<CharacterObject>(),
                Array.Empty<CharacterObject>(), true, 8);
            Assert.Single(result, x => x == hero);
            Assert.Equal(7, result.Count(x => x == soldier));
        }

        [Fact]
        public void MoreEligibleHeroesThanSlotsProduceExactlyTheRequiredRoster()
        {
            var heroes = Enumerable.Range(0, 10).Select(_ => new CharacterObject(new Hero())).ToArray();
            var result = TeamTournamentRosterPolicy.BuildOpponents(heroes,
                Array.Empty<CharacterObject>(), Array.Empty<CharacterObject>(), Array.Empty<CharacterObject>(),
                Array.Empty<CharacterObject>(), true, 3);
            Assert.Equal(heroes.Take(3), result);
        }

        [Fact]
        public void NoExternalTroopsUsesRegularArenaFallbackAndTerminates()
        {
            var excludedPartyHero = new CharacterObject(new Hero());
            var recruit = new CharacterObject { Tier = 1 };
            var regular = new CharacterObject { Tier = 3 };
            var result = TeamTournamentRosterPolicy.BuildOpponents(new[] { excludedPartyHero },
                Array.Empty<CharacterObject>(), new[] { excludedPartyHero, recruit, regular },
                new[] { excludedPartyHero }, Array.Empty<CharacterObject>(), true, 248);
            Assert.Equal(248, result.Count);
            Assert.All(result, x => Assert.Same(regular, x));
        }

        [Fact]
        public void EmptyPoolsFailExplicitlyInsteadOfLoopingForever()
        {
            Assert.Throws<InvalidOperationException>(() => TeamTournamentRosterPolicy.BuildOpponents(
                Array.Empty<CharacterObject>(), Array.Empty<CharacterObject>(), Array.Empty<CharacterObject>(),
                Array.Empty<CharacterObject>(), Array.Empty<CharacterObject>(), true, 1));
        }

        [Fact]
        public void EmptyTournamentNeedsNoFallback()
        {
            Assert.Empty(TeamTournamentRosterPolicy.BuildOpponents(Array.Empty<CharacterObject>(),
                Array.Empty<CharacterObject>(), Array.Empty<CharacterObject>(), Array.Empty<CharacterObject>(),
                Array.Empty<CharacterObject>(), true, 0));
        }

        [Fact]
        public void NativeAdaptersUseActualPartyRosterAndExcludeMainPartyTroopSource()
        {
            string root = Path.Combine(TestOptions.FindWorkspace(), "ReignBeta/src/Modules/Arena/ArenaOverhaul/TeamTournament");
            string selection = File.ReadAllText(Path.Combine(root, "TeamTournamentInfo.cs"));
            Assert.Contains("!x.IsWounded && x.Troop.CanJoinPlayerTeam()", selection);
            string generation = File.ReadAllText(Path.Combine(root, "TeamTournamentBehavior.cs"));
            Assert.Contains("MobileParty.MainParty.MemberRoster.GetTroopRoster()", generation);
            Assert.Contains("IsPlayerParticipating && party == MobileParty.MainParty", generation);
            Assert.Contains("TeamTournamentRosterPolicy.BuildOpponents(", generation);
            Assert.True(generation.IndexOf("IsPlayerParticipating = isPlayerParticipating;", StringComparison.Ordinal)
                < generation.IndexOf("CreateTeams();", StringComparison.Ordinal));
        }
    }
}

// Minimal engine boundary doubles for the linked production eligibility and
// team-selection helper. These tests never create or mutate a native campaign.
namespace TaleWorlds.CampaignSystem
{
    public sealed class Hero
    {
        public bool IsDead { get; set; }
        public bool IsChild { get; set; }
        public bool IsWounded { get; set; }
        public bool IsPrisoner { get; set; }
        public bool IsNotable { get; set; }
        public bool IsNoncombatant { get; set; }
        public object? CompanionOf { get; set; }
        public Hero? Father { get; set; }
        public Hero? Mother { get; set; }
        public Hero? Spouse { get; set; }
        public List<Hero> Children { get; } = new();
        public List<Hero> Siblings { get; } = new();
        public List<Hero> ExSpouses { get; } = new();
        public Dictionary<object, int> Skills { get; } = new();
        public int GetSkillValue(object skill) => Skills.GetValueOrDefault(skill);
    }

    public sealed class CharacterObject
    {
        public CharacterObject() { }
        public CharacterObject(Hero hero) { IsHero = true; HeroObject = hero; }
        public bool IsHero { get; }
        public bool IsPlayerCharacter { get; set; }
        public Hero HeroObject { get; } = null!;
        public int Tier { get; set; }
    }
}

namespace TaleWorlds.Core
{
    public static class DefaultSkills
    {
        public static object OneHanded { get; } = new();
        public static object TwoHanded { get; } = new();
        public static object Polearm { get; } = new();
    }
}

namespace TaleWorlds.CampaignSystem.Settlements
{
    public sealed class Settlement
    {
        public static Settlement CurrentSettlement { get; } = new();
        public Town Town { get; } = new();
        public LocationComplex LocationComplex { get; } = new();
    }

    public sealed class Town { public bool HasTournament { get; set; } }
    public sealed class LocationCharacter { public CharacterObject Character { get; set; } = new(); }
    public sealed class LocationComplex
    {
        public IEnumerable<LocationCharacter> GetListOfCharacters() => Array.Empty<LocationCharacter>();
    }
}
