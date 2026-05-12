using System;
using System.Collections.Generic;
using System.Linq;

namespace PlanetExplorer
{
    public static class QuizGenerator
    {
        private static readonly Random _rng = new();

        public static List<QuizQuestion> GenerateForItem(SpaceItem item, List<SpaceItem> allItems, int count = 7)
        {
            if (item == null) return new List<QuizQuestion>();
            allItems = allItems?.Where(x => x.IsActive).ToList() ?? new List<SpaceItem>();

            var questions = new List<QuizQuestion>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddIfUnique(QuizQuestion? q, string key)
            {
                if (q == null) return;
                if (used.Add(key))
                    questions.Add(q);
            }

            AddIfUnique(BuildTypeQuestion(item, allItems), $"TYPE:{item.ItemId}");

            var def = BuildDefinitionQuestion(item, allItems);
            AddIfUnique(def, $"DEF:{item.ItemId}");

            var dist = BuildDistanceCompareQuestion(item, allItems);
            AddIfUnique(dist, $"DIST:{item.ItemId}:{string.Join(",", dist?.Options ?? new List<string>())}");

            var mass = BuildMassCompareQuestion(item, allItems);
            AddIfUnique(mass, $"MASS:{item.ItemId}:{string.Join(",", mass?.Options ?? new List<string>())}");

            var dia = BuildDiameterCompareQuestion(item, allItems);
            AddIfUnique(dia, $"DIA:{item.ItemId}:{string.Join(",", dia?.Options ?? new List<string>())}");

            while (questions.Count < count)
            {
                var extra = PickExtraQuestion(item, allItems);
                if (extra == null) break;

                var key = $"{extra.QuestionText}|{string.Join(",", extra.Options)}";
                AddIfUnique(extra, key);

                // safety: avoid infinite loop if data is limited
                if (used.Count > 50) break;
            }

            return questions.OrderBy(_ => _rng.Next()).Take(count).ToList();
        }


        private static QuizQuestion BuildTypeQuestion(SpaceItem item, List<SpaceItem> allItems)
        {
            var types = allItems.Select(x => x.Type)
                                .Where(t => !string.IsNullOrWhiteSpace(t))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

            // Ensure correct type included
            if (!types.Contains(item.Type, StringComparer.OrdinalIgnoreCase))
                types.Add(item.Type);

            var wrongTypes = types.Where(t => !t.Equals(item.Type, StringComparison.OrdinalIgnoreCase))
                                  .OrderBy(_ => _rng.Next())
                                  .Take(3)
                                  .ToList();

            var options = new List<string>(wrongTypes) { item.Type };
            options = options.OrderBy(_ => _rng.Next()).ToList();

            return new QuizQuestion
            {
                QuestionText = $"What type of space object is \"{item.Name}\"?",
                Options = options,
                CorrectIndex = options.FindIndex(o => o.Equals(item.Type, StringComparison.OrdinalIgnoreCase))
            };
        }

        private static QuizQuestion? BuildDefinitionQuestion(SpaceItem item, List<SpaceItem> allItems)
        {
            if (string.IsNullOrWhiteSpace(item.ShortExplanation))
                return null;

            // Pick 3 other explanations as wrong options
            var wrong = allItems.Where(x => x.ItemId != item.ItemId && !string.IsNullOrWhiteSpace(x.ShortExplanation))
                                .OrderBy(_ => _rng.Next())
                                .Take(3)
                                .Select(x => Shorten(x.ShortExplanation!, 140))
                                .ToList();

            if (wrong.Count < 3) return null;

            var correct = Shorten(item.ShortExplanation!, 140);

            var options = new List<string>(wrong) { correct };
            options = options.OrderBy(_ => _rng.Next()).ToList();

            return new QuizQuestion
            {
                QuestionText = $"Which description best matches \"{item.Name}\"?",
                Options = options,
                CorrectIndex = options.IndexOf(correct)
            };
        }

        private static QuizQuestion? BuildDistanceCompareQuestion(SpaceItem item, List<SpaceItem> allItems)
        {
            // Needs distance values
            var withDist = allItems.Where(x => x.DistanceFromSunKm.HasValue).ToList();
            if (!item.DistanceFromSunKm.HasValue || withDist.Count < 4) return null;

            // choose 3 random other items with distance
            var others = withDist.Where(x => x.ItemId != item.ItemId)
                                 .OrderBy(_ => _rng.Next())
                                 .Take(3)
                                 .ToList();

            if (others.Count < 3) return null;

            var options = new List<SpaceItem>(others) { item };
            options = options.OrderBy(_ => _rng.Next()).ToList();

            // Ask: which is closest to the Sun?
            var correctItem = options.OrderBy(x => x.DistanceFromSunKm!.Value).First();

            return new QuizQuestion
            {
                QuestionText = "Which object is closest to the Sun (based on stored distance)?",
                Options = options.Select(x => x.Name).ToList(),
                CorrectIndex = options.FindIndex(x => x.ItemId == correctItem.ItemId)
            };
        }

        private static QuizQuestion? BuildMassCompareQuestion(SpaceItem item, List<SpaceItem> allItems)
        {
            var withMass = allItems.Where(x => x.MassKg.HasValue).ToList();
            if (!item.MassKg.HasValue || withMass.Count < 4) return null;

            var others = withMass.Where(x => x.ItemId != item.ItemId)
                                 .OrderBy(_ => _rng.Next())
                                 .Take(3)
                                 .ToList();

            if (others.Count < 3) return null;

            var options = new List<SpaceItem>(others) { item };
            options = options.OrderBy(_ => _rng.Next()).ToList();

            var correctItem = options.OrderByDescending(x => x.MassKg!.Value).First();

            return new QuizQuestion
            {
                QuestionText = "Which object has the greatest mass (based on stored mass)?",
                Options = options.Select(x => x.Name).ToList(),
                CorrectIndex = options.FindIndex(x => x.ItemId == correctItem.ItemId)
            };
        }

        private static QuizQuestion? BuildDiameterCompareQuestion(SpaceItem item, List<SpaceItem> allItems)
        {
            var withDia = allItems.Where(x => x.DiameterKm.HasValue).ToList();
            if (!item.DiameterKm.HasValue || withDia.Count < 4) return null;

            var others = withDia.Where(x => x.ItemId != item.ItemId)
                                .OrderBy(_ => _rng.Next())
                                .Take(3)
                                .ToList();

            if (others.Count < 3) return null;

            var options = new List<SpaceItem>(others) { item };
            options = options.OrderBy(_ => _rng.Next()).ToList();

            var correctItem = options.OrderByDescending(x => x.DiameterKm!.Value).First();

            return new QuizQuestion
            {
                QuestionText = "Which object has the largest diameter (based on stored diameter)?",
                Options = options.Select(x => x.Name).ToList(),
                CorrectIndex = options.FindIndex(x => x.ItemId == correctItem.ItemId)
            };
        }

        private static QuizQuestion? PickExtraQuestion(SpaceItem item, List<SpaceItem> allItems)
        {
            // Try in random order
            var candidates = new Func<QuizQuestion?>[]
            {
                () => BuildDistanceCompareQuestion(item, allItems),
                () => BuildMassCompareQuestion(item, allItems),
                () => BuildDiameterCompareQuestion(item, allItems),
                () => BuildDefinitionQuestion(item, allItems),
            };

            foreach (var f in candidates.OrderBy(_ => _rng.Next()))
            {
                var q = f();
                if (q != null) return q;
            }
            return null;
        }

        private static string Shorten(string text, int max)
        {
            text = text.Trim();
            if (text.Length <= max) return text;
            return text.Substring(0, max).Trim() + "...";
        }
    }
}
