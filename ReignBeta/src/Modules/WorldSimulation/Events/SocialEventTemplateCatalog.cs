using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;

namespace ReignBeta.Events
{
    public static class SocialEventTemplateCatalog
    {
        private static readonly List<SocialEventTemplate> Templates = BuildTemplates();

        public static IReadOnlyList<SocialEventTemplate> All => Templates;

        internal static SocialEventTemplate WeddingForCulture(string cultureId)
        {
            string culture = (cultureId ?? "").ToLowerInvariant();
            if (culture.Contains("empire")) culture = "empire";
            return GetById("wedding_" + culture) ?? GetById("wedding_neutral");
        }

        public static SocialEventTemplate GetById(string templateId)
        {
            return Templates.FirstOrDefault(x => x.TemplateId == templateId) ?? Templates[0];
        }

        public static bool HasTemplate(string templateId)
        {
            return Templates.Any(x => x.TemplateId == templateId);
        }

        public static SocialEventTemplate GetRandomForCulture(string cultureId)
        {
            string normalizedCulture = NormalizeCulture(cultureId);
            List<SocialEventTemplate> matches = Templates
                .Where(x => x.TemplateId.EndsWith("_" + normalizedCulture) && !x.TemplateId.StartsWith("wedding_"))
                .ToList();

            if (matches.Count == 0)
            {
                matches = Templates.Where(x => x.TemplateId.EndsWith("_empire") && !x.TemplateId.StartsWith("wedding_")).ToList();
            }

            return matches[MBRandom.RandomInt(matches.Count)];
        }

        public static SocialEventTemplate GetTournamentCelebrationForCulture(string cultureId, string stableKey = null)
        {
            string normalizedCulture = NormalizeCulture(cultureId);
            List<SocialEventTemplate> matches = Templates
                .Where(x => x.TemplateId.EndsWith("_" + normalizedCulture)
                    && (x.TemplateId.StartsWith("feast_") || x.TemplateId.StartsWith("fair_") || x.TemplateId.StartsWith("dance_")))
                .OrderBy(x => x.TemplateId)
                .ToList();

            if (matches.Count == 0)
            {
                matches = Templates
                    .Where(x => x.TemplateId.EndsWith("_empire")
                        && (x.TemplateId.StartsWith("feast_") || x.TemplateId.StartsWith("fair_") || x.TemplateId.StartsWith("dance_")))
                    .OrderBy(x => x.TemplateId)
                    .ToList();
            }

            if (string.IsNullOrWhiteSpace(stableKey)) return matches[MBRandom.RandomInt(matches.Count)];
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in stableKey)
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return matches[(int)(hash % (uint)matches.Count)];
            }
        }

        private static List<SocialEventTemplate> BuildTemplates()
        {
            List<SocialEventTemplate> templates = new List<SocialEventTemplate>();

            AddVariants(templates, "feast", 4f, BuildFeastPhases(), new Dictionary<string, string>
            {
                { "empire", "Villa Supper" },
                { "vlandia", "Court Feast" },
                { "sturgia", "Hall Feast" },
                { "battania", "Forest Feast" },
                { "aserai", "Merchant Banquet" },
                { "khuzait", "Steppe Feast" },
                { "nord", "Longhouse Feast" }
            });

            AddVariants(templates, "fair", 4f, BuildFairPhases(), new Dictionary<string, string>
            {
                { "empire", "Artisan Salon" },
                { "vlandia", "Village Patron Fair" },
                { "sturgia", "Fur Market" },
                { "battania", "Woodcarver's Fair" },
                { "aserai", "Textile Exhibition" },
                { "khuzait", "Tack Fair" },
                { "nord", "Sea Fair" }
            });

            AddVariants(templates, "dance", 4f, BuildDancePhases(), new Dictionary<string, string>
            {
                { "empire", "Courtyard Dance" },
                { "vlandia", "Courtly Ball" },
                { "sturgia", "Hall Revel" },
                { "battania", "Grove Dance" },
                { "aserai", "Moonlit Courtyard Dance" },
                { "khuzait", "Steppe Circle Dance" },
                { "nord", "Longhouse Revel" }
            });

            AddVariants(templates, "performance", 3f, BuildPerformancePhases(), new Dictionary<string, string>
            {
                { "empire", "Poetry Recital" },
                { "vlandia", "Minstrel Performance" },
                { "sturgia", "Saga Night" },
                { "battania", "Bardic Gathering" },
                { "aserai", "Storyteller's Evening" },
                { "khuzait", "Horse-Song Performance" },
                { "nord", "Skaldic Night" }
            });

            AddVariants(templates, "contest", 3f, BuildContestPhases(), new Dictionary<string, string>
            {
                { "empire", "Patron's Contest" },
                { "vlandia", "Court Challenge" },
                { "sturgia", "Hall Trial" },
                { "battania", "Grove Contest" },
                { "aserai", "Market Challenge" },
                { "khuzait", "Steppe Challenge" },
                { "nord", "Thing Challenge" }
            });

            AddVariants(templates, "outing", 2f, BuildOutingPhases(), new Dictionary<string, string>
            {
                { "empire", "Garden Outing" },
                { "vlandia", "Hunting Outing" },
                { "sturgia", "Winter Walk" },
                { "battania", "Grove Outing" },
                { "aserai", "Oasis Outing" },
                { "khuzait", "Steppe Ride" },
                { "nord", "Shore Walk" }
            });

            templates.Add(new SocialEventTemplate(
                "generated_wilderness",
                "Wilderness Encounter",
                "wilderness encounter",
                0.25f,
                30,
                BuildGeneratedWildernessPhases()));

            var customs = new Dictionary<string, string[]>
            {
                ["empire"] = new[] { "Households at the villa", "The families read their pledges before the witnesses.", "The witnesses acclaim the vows and the new household." },
                ["vlandia"] = new[] { "The hall receives the banners", "The two houses present their tokens and name their witnesses.", "The couple gives their vows before the gathered hall." },
                ["sturgia"] = new[] { "Hearth welcome", "The elders offer bread and speak their blessings at the hearth.", "The households hear the vows and share the closing toast." },
                ["battania"] = new[] { "Procession to the green", "The kin exchange gifts while a bard names the two houses.", "The witnesses hear the vows and close the gathering with song." },
                ["aserai"] = new[] { "Welcome in the courtyard", "The families exchange gifts and the elders give their blessings.", "The couple speaks before the witnesses and receives the farewell." },
                ["khuzait"] = new[] { "Welcome under the canopy", "The families share the welcome cup and honor both lines of kin.", "The vows are witnessed and the musicians play the farewell." },
                ["nord"] = new[] { "Arrival at the longhouse", "The elders pass the cup and recite the houses' pledges.", "The couple gives their vows and the guests raise the final toast." },
                ["neutral"] = new[] { "Arrival of both families", "The families exchange greetings, gifts, and blessings.", "The couple gives their vows before the witnesses." }
            };
            foreach (var row in customs)
                templates.Add(new SocialEventTemplate("wedding_" + row.Key, "Wedding ceremony", "wedding", 1f, 30,
                    new List<SocialEventPhase>
                    {
                        new SocialEventPhase("wedding_arrivals", row.Value[0], "Both households arrive and the guests are introduced.", 4, 8, 0, 0, 0),
                        new SocialEventPhase("wedding_rites", "Family rites", row.Value[1], 4, 8, 0, 0, 0),
                        new SocialEventPhase("wedding_vows", "Vows and farewell", row.Value[2], 4, 8, 0, 0, 0)
                    }));
            return templates;
        }

        private static IReadOnlyList<SocialEventPhase> BuildGeneratedWildernessPhases()
        {
            return new List<SocialEventPhase>
            {
                new SocialEventPhase("generated_opening", "Encounter", "A moment on the road draws your attention.", 4, 8, 0f, 0f, 0f)
            };
        }

        private static void AddVariants(
            List<SocialEventTemplate> templates,
            string kind,
            float opportunityDays,
            IReadOnlyList<SocialEventPhase> phases,
            Dictionary<string, string> namesByCulture)
        {
            foreach (KeyValuePair<string, string> pair in namesByCulture)
            {
                templates.Add(new SocialEventTemplate(
                    kind + "_" + pair.Key,
                    pair.Value,
                    pair.Value,
                    opportunityDays,
                    30,
                    phases));
            }
        }

        private static string NormalizeCulture(string cultureId)
        {
            string value = (cultureId ?? string.Empty).ToLowerInvariant();

            if (value.Contains("vland"))
            {
                return "vlandia";
            }

            if (value.Contains("sturg"))
            {
                return "sturgia";
            }

            if (value.Contains("battan"))
            {
                return "battania";
            }

            if (value.Contains("aserai"))
            {
                return "aserai";
            }

            if (value.Contains("khuz"))
            {
                return "khuzait";
            }

            if (value.Contains("nord"))
            {
                return "nord";
            }

            return "empire";
        }

        private static IReadOnlyList<SocialEventPhase> BuildFeastPhases()
        {
            return new List<SocialEventPhase>
            {
                new SocialEventPhase("arrival_and_seating", "Arrival and Seating", "Guests arrive and are guided toward their places.", 3, 6, 0.02f, 0.01f, 0.0025f),
                new SocialEventPhase("first_course", "First Course", "The first dishes are served and formal courtesy sets the tone.", 5, 9, 0.03f, 0.015f, 0.004f),
                new SocialEventPhase("toasts_and_table_talk", "Toasts and Table Talk", "The host invites toasts while guests settle into sharper conversation.", 6, 10, 0.05f, 0.02f, 0.0075f),
                new SocialEventPhase("entertainment_and_side_talk", "Entertainment and Side Talk", "A performance, tale, or display draws attention while side conversations grow more private.", 6, 10, 0.05f, 0.02f, 0.0075f),
                new SocialEventPhase("farewells", "Farewells", "The feast closes with thanks, fatigue, and last impressions.", 3, 6, 0.02f, 0.01f, 0.0025f)
            };
        }

        private static IReadOnlyList<SocialEventPhase> BuildPerformancePhases()
        {
            return new List<SocialEventPhase>
            {
                new SocialEventPhase("guests_gather", "Guests Gather", "The audience gathers while performers tune, rehearse, or wait to be called.", 3, 6, 0.02f, 0.01f, 0.0025f),
                new SocialEventPhase("host_introduction", "Host Introduction", "The host introduces the performers and sets the evening's tone.", 3, 5, 0.02f, 0.01f, 0.003f),
                new SocialEventPhase("first_performance", "First Performance", "The first tale, song, poem, or display begins.", 4, 8, 0.03f, 0.015f, 0.004f),
                new SocialEventPhase("audience_reactions", "Audience Reactions", "Guests praise, mock, compare, or quietly judge what they have heard.", 6, 10, 0.05f, 0.02f, 0.006f),
                new SocialEventPhase("second_performance", "Second Performance", "A second performance changes the mood of the gathering.", 4, 8, 0.03f, 0.015f, 0.004f),
                new SocialEventPhase("debate_and_praise", "Debate and Praise", "Audience members debate meaning, skill, memory, and taste.", 6, 10, 0.05f, 0.02f, 0.006f),
                new SocialEventPhase("final_performance", "Final Performance", "The closing piece gives guests one last shared moment.", 4, 7, 0.03f, 0.015f, 0.004f),
                new SocialEventPhase("closing_conversations", "Closing Conversations", "Guests linger to compliment, argue, or seek private words.", 5, 9, 0.04f, 0.015f, 0.004f)
            };
        }

        private static IReadOnlyList<SocialEventPhase> BuildContestPhases()
        {
            return new List<SocialEventPhase>
            {
                new SocialEventPhase("gathering_and_signups", "Gathering and Signups", "Guests gather around the contest area and decide whether to participate.", 3, 6, 0.02f, 0.01f, 0.0025f),
                new SocialEventPhase("opening_boasts", "Opening Boasts", "Participants posture, joke, and test each other's nerve.", 4, 7, 0.04f, 0.02f, 0.006f),
                new SocialEventPhase("first_round", "First Round", "The first round begins while the crowd weighs skill and confidence.", 4, 8, 0.03f, 0.015f, 0.004f),
                new SocialEventPhase("rest_and_reactions", "Rest and Reactions", "Competitors catch their breath and guests react to the early showing.", 6, 10, 0.05f, 0.02f, 0.0075f),
                new SocialEventPhase("second_round", "Second Round", "The contest narrows and pride begins to matter.", 4, 8, 0.04f, 0.02f, 0.006f),
                new SocialEventPhase("final_challenge", "Final Challenge", "The final challenge draws the whole room's attention.", 4, 7, 0.04f, 0.02f, 0.0075f),
                new SocialEventPhase("prizes_and_praise", "Prizes and Praise", "The winner is recognized and the losers make their peace with it.", 4, 7, 0.03f, 0.015f, 0.004f),
                new SocialEventPhase("after_contest_mingling", "After-Contest Mingling", "Guests turn the result into gossip, praise, jokes, or grudges.", 5, 9, 0.04f, 0.015f, 0.004f)
            };
        }

        private static IReadOnlyList<SocialEventPhase> BuildFairPhases()
        {
            return new List<SocialEventPhase>
            {
                new SocialEventPhase("arrival_at_stalls", "Arrival at the Stalls", "Guests arrive among wares, demonstrations, and waiting artisans.", 3, 6, 0.02f, 0.01f, 0.0025f),
                new SocialEventPhase("demonstrations", "Demonstrations", "Craftsmen or traders show their best work to the noble guests.", 4, 8, 0.03f, 0.015f, 0.004f),
                new SocialEventPhase("browsing_and_bargaining", "Browsing and Bargaining", "Guests browse, bargain, compliment, and quietly compete through patronage.", 6, 10, 0.04f, 0.02f, 0.005f),
                new SocialEventPhase("patron_judging_and_purchase", "Patron Judging and Purchase", "High-status guests draw attention with judgments, gifts, purchases, and refusals.", 5, 9, 0.05f, 0.02f, 0.0075f),
                new SocialEventPhase("closing_the_fair", "Closing the Fair", "Guests depart with purchases, promises, and fresh impressions.", 3, 6, 0.02f, 0.01f, 0.0025f)
            };
        }

        private static IReadOnlyList<SocialEventPhase> BuildOutingPhases()
        {
            return new List<SocialEventPhase>
            {
                new SocialEventPhase("gathering_outdoors", "Gathering Outdoors", "Guests assemble outside the settlement or in its gardens before setting out.", 3, 6, 0.02f, 0.01f, 0.0025f),
                new SocialEventPhase("setting_out", "Setting Out", "The party begins the outing and companions fall into small groups.", 4, 7, 0.03f, 0.015f, 0.004f),
                new SocialEventPhase("first_display", "First Display", "The first notable display or shared view gives guests a subject for judgment.", 4, 8, 0.03f, 0.015f, 0.004f),
                new SocialEventPhase("trail_conversations", "Trail Conversations", "Conversation drifts as guests walk, ride, watch, or rest.", 6, 10, 0.04f, 0.015f, 0.004f),
                new SocialEventPhase("main_activity", "Main Activity", "The outing reaches its main activity and the boldest guests step forward.", 4, 8, 0.04f, 0.02f, 0.006f),
                new SocialEventPhase("rest_and_refreshment", "Rest and Refreshment", "The company pauses for food, drink, and private remarks.", 5, 9, 0.04f, 0.015f, 0.005f),
                new SocialEventPhase("returning", "Returning", "The party returns with fresh jokes, fatigue, and small rivalries.", 5, 9, 0.03f, 0.015f, 0.004f),
                new SocialEventPhase("parting_words", "Parting Words", "Guests exchange final comments before dispersing.", 3, 6, 0.02f, 0.01f, 0.0025f)
            };
        }

        private static IReadOnlyList<SocialEventPhase> BuildDancePhases()
        {
            return new List<SocialEventPhase>
            {
                new SocialEventPhase("arrival", "Arrival", "Guests gather, exchange greetings, and take measure of the room.", 3, 6, 0.02f, 0.01f, 0.0025f),
                new SocialEventPhase("opening_mingling", "Opening Mingling", "Small groups form while the host's household finishes the preparations.", 6, 10, 0.04f, 0.015f, 0.004f),
                new SocialEventPhase("first_dance", "First Dance", "The musicians begin a formal tune and the floor opens.", 4, 8, 0.03f, 0.015f, 0.005f),
                new SocialEventPhase("refreshments_and_second_dance", "Refreshments and Second Dance", "Dancers rest over wine and small dishes before livelier music draws them back.", 6, 10, 0.05f, 0.02f, 0.0075f),
                new SocialEventPhase("closing", "Closing", "Farewells begin and the gathering loosens into departures.", 3, 6, 0.02f, 0.01f, 0.0025f)
            };
        }
    }
}
