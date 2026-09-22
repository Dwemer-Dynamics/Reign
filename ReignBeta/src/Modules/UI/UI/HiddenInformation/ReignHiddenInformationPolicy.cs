using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ReignBeta.Integration;
using ReignBeta.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Items;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace ReignBeta.UI.HiddenInformation
{
    internal static class ReignHiddenInformationPolicy
    {
        private sealed class HeroContext
        {
            public Hero Hero;
        }

        private static readonly ConditionalWeakTable<object, HeroContext> Contexts =
            new ConditionalWeakTable<object, HeroContext>();
        private static readonly HashSet<string> LoggedFailures =
            new HashSet<string>(StringComparer.Ordinal);

        public static bool IsCheatRevealEnabled
        {
            get
            {
                try
                {
                    return ReignBetaSettings.Instance?.RevealAllCharacterStatsAndRelationships == true;
                }
                catch (Exception ex)
                {
                    LogOnce("cheat-reveal-setting", ex);
                    return false;
                }
            }
        }

        public static bool ShouldMask(Hero hero)
        {
            return !IsCheatRevealEnabled
                && hero != null
                && hero != Hero.MainHero
                && !hero.IsHumanPlayerCharacter;
        }

        public static bool ShouldMask(CharacterObject character)
        {
            return character != null && character.IsHero && ShouldMask(character.HeroObject);
        }

        public static void Associate(object presentationObject, Hero hero)
        {
            if (presentationObject == null || hero == null)
            {
                return;
            }

            Contexts.Remove(presentationObject);
            Contexts.Add(presentationObject, new HeroContext { Hero = hero });
        }

        public static Hero AssociatedHero(object presentationObject)
        {
            return presentationObject != null && Contexts.TryGetValue(presentationObject, out HeroContext context)
                ? context.Hero
                : null;
        }

        public static void AssociateSkillTree(object root, Hero hero)
        {
            if (root == null || hero == null)
            {
                return;
            }

            HashSet<object> visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            AssociateSkillTree(root, hero, visited, 0);
        }

        public static void MaskRelationshipProperties(IEnumerable<TooltipProperty> properties)
        {
            if (properties == null)
            {
                return;
            }

            foreach (TooltipProperty property in properties)
            {
                if (property == null)
                {
                    continue;
                }

                string definition = (property.DefinitionLabel ?? string.Empty).Trim().TrimEnd(':').Trim();
                bool relation = definition.IndexOf("relation", StringComparison.CurrentCultureIgnoreCase) >= 0
                    || definition.IndexOf("opinion", StringComparison.CurrentCultureIgnoreCase) >= 0;
                if (relation)
                {
                    property.ValueLabel = string.Empty;
                }
            }
        }

        public static bool TryResolveHero(object value, out Hero hero)
        {
            hero = value as Hero;
            if (hero != null)
            {
                return true;
            }

            CharacterObject character = value as CharacterObject;
            if (character != null)
            {
                hero = character.HeroObject;
                return hero != null;
            }

            if (value == null)
            {
                return false;
            }

            foreach (string propertyName in new[] { "Hero", "OwnerHero", "Character", "Owner", "HeroData" })
            {
                try
                {
                    object candidate = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value, null)
                        ?? value.GetType().GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
                    if (candidate != null && !ReferenceEquals(candidate, value) && TryResolveHero(candidate, out hero))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    LogOnce("resolve:" + value.GetType().FullName + ":" + propertyName, ex);
                }
            }

            return false;
        }

        public static void LogOnce(string key, Exception exception)
        {
            if (string.IsNullOrWhiteSpace(key) || !LoggedFailures.Add(key))
            {
                return;
            }
            ReignLog.Warn("Hidden-information presentation fallback (" + key + "): " + exception.Message);
        }

        private static void AssociateSkillTree(object value, Hero hero, ISet<object> visited, int depth)
        {
            if (value == null || depth > 6 || value is string || value is Hero || value is CharacterObject)
            {
                return;
            }
            if (!value.GetType().IsValueType && !visited.Add(value))
            {
                return;
            }

            if (value is EncyclopediaSkillVM)
            {
                Associate(value, hero);
                return;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    AssociateSkillTree(item, hero, visited, depth + 1);
                }
                return;
            }

            if (!(value is ViewModel))
            {
                return;
            }

            Associate(value, hero);
            foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                Type propertyType = property.PropertyType;
                if (!typeof(ViewModel).IsAssignableFrom(propertyType)
                    && !typeof(IEnumerable).IsAssignableFrom(propertyType))
                {
                    continue;
                }

                try
                {
                    AssociateSkillTree(property.GetValue(value, null), hero, visited, depth + 1);
                }
                catch (Exception ex)
                {
                    LogOnce("tree:" + value.GetType().FullName + ":" + property.Name, ex);
                }
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
