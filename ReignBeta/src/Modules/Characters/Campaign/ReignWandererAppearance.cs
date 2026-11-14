using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Shared.Characters;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    internal static class ReignWandererAppearance
    {
        private static readonly PopulationFaceSchemaCache SchemaCache = new PopulationFaceSchemaCache();
        internal static PopulationFaceSchemaCache SchemaDiagnostics => SchemaCache;
        internal static void ClearSchemaCache() => SchemaCache.Clear();
        private static IReadOnlyList<int> FaceIndices(int race, bool female, int age, int weightCount)
        {
            var key = new PopulationFaceSchemaKey(race, female, age, weightCount);
            long mismatches = SchemaCache.Mismatches;
            var indices = SchemaCache.Get(key, ReadFaceIndices);
            if (SchemaCache.Mismatches != mismatches)
                ReignBeta.Integration.ReignLog.Warn("Native face schema changed; schema memoization disabled for this session.");
            return indices;
        }
        private static int[] ReadFaceIndices(PopulationFaceSchemaKey key)
        {
            var indices = new List<int>();
            int count = MBBodyProperties.GetFaceGenInstancesLength(key.Race, key.Female ? 1 : 0, key.Age);
            for (int i = 0; i < count && i < key.WeightCount; i++)
                if (IsFace(MBBodyProperties.GetDeformKeyData(i, key.Race, key.Female ? 1 : 0, key.Age).Id)) indices.Add(i);
            return indices.ToArray();
        }
        internal static float[] Shape(BodyProperties body, int race, bool female)
        {
            var p = FaceGenerationParams.Create();
            MBBodyProperties.GetParamsFromKey(ref p, body, false, false);
            var indices = FaceIndices(race, female, (int)body.Age, p.KeyWeights.Length);
            var result = new float[indices.Count];
            for (int i = 0; i < indices.Count; i++) result[i] = p.KeyWeights[indices[i]];
            return result;
        }

        private static bool IsFace(string id) => id != "age" && id != "height" && id != "weight" && id != "build";

        internal static BodyProperties Generate(CharacterObject donor, CultureObject culture, float age, string id,
            IEnumerable<WandererIdentity> reserved, IEnumerable<Hero> living)
        {
            var range = donor.BodyPropertyRange ?? throw new InvalidOperationException("Cultural donor has no face range.");
            int race = donor.Race; bool female = donor.IsFemale;
            var comparison = reserved.Where(r => r.Race == race && r.Female == female && r.FaceShape != null).Select(r => r.FaceShape).ToList();
            // Include existing nobles/Native heroes; changing age or hairstyle alone cannot evade the shape check.
            foreach (var hero in living.Where(h => h.CharacterObject.Race == race && h.IsFemale == female && !h.IsChild))
                comparison.Add(Shape(hero.BodyProperties, race, female));
            var keys = new HashSet<string>(reserved.Select(r => r.FaceKey).Where(k => !string.IsNullOrEmpty(k)));
            int[] hair = TaleWorlds.CampaignSystem.Campaign.Current.Models.BodyPropertiesModel.GetHairIndicesForCulture(race, female ? 1 : 0, age, culture);
            if (hair == null || hair.Length == 0) throw new InvalidOperationException("Culture has no valid adult hair choices.");
            for (int attempt = 0; attempt < 128; attempt++)
            {
                int seed = unchecked(WandererPopulationRules.Seed(id) + attempt * 7919);
                var rng = new Random(seed);
                BodyProperties body = BodyProperties.GetRandomBodyProperties(race, female, range.BodyPropertyMin, range.BodyPropertyMax,
                    0, seed, range.HairTags, range.BeardTags, range.TattooTags, 0f);
                body = new BodyProperties(new DynamicBodyProperties(age, .2f + (float)rng.NextDouble() * .6f, .2f + (float)rng.NextDouble() * .6f), body.StaticProperties);
                var parameters = FaceGenerationParams.Create();
                MBBodyProperties.GetParamsFromKey(ref parameters, body, false, false);
                var indices = FaceIndices(race, female, (int)age, parameters.KeyWeights.Length);
                for (int i = 0; i < indices.Count; i++)
                    parameters.KeyWeights[indices[i]] = .2f + (float)rng.NextDouble() * .6f;
                parameters.HeightMultiplier = .3f + (float)rng.NextDouble() * .4f;
                MBBodyProperties.EnforceConstraints(ref parameters);
                MBBodyProperties.ProduceNumericKeyWithParams(parameters, false, false, ref body);
                TaleWorlds.Core.FaceGen.SetHair(ref body, hair[rng.Next(hair.Length)], female ? 0 : -1, -1);
                float[] shape = Shape(body, race, female);
                if (shape.Length < 5) throw new InvalidOperationException("Native face shape is unavailable; cannot prove visual uniqueness.");
                if (!keys.Contains(Key(body.StaticProperties)) && !comparison.Any(old => WandererPopulationRules.FaceTooClose(shape, old))) return body;
            }
            throw new InvalidOperationException("No sufficiently distinct cultural face was found; creation deferred.");
        }

        internal static string Key(StaticBodyProperties p) => p.KeyPart1.ToString("X16") + p.KeyPart2.ToString("X16")
            + p.KeyPart3.ToString("X16") + p.KeyPart4.ToString("X16") + p.KeyPart5.ToString("X16")
            + p.KeyPart6.ToString("X16") + p.KeyPart7.ToString("X16") + p.KeyPart8.ToString("X16");
    }
}
