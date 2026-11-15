using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Organizer;

public static class LibraryQuery
{
    public static IReadOnlyList<BaseItem> MoviesAndSeries(ILibraryManager library) =>
        library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true,
            IsVirtualItem = false
        });

    public static List<BaseItem> Match(CollectionRule rule, IEnumerable<BaseItem> items)
    {
        var movies = rule.MediaType is "Movie" or "Both";
        var series = rule.MediaType is "Series" or "Both";
        return items
            .Where(i => (movies && i is Movie) || (series && i is Series))
            .Where(i => RuleMatcher.Matches(rule, i))
            .ToList();
    }

    /// <summary>Manual rules first, then the automatic ones from the last sync.</summary>
    public static IEnumerable<CollectionRule> AllRules(PluginConfiguration config) =>
        config.Rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Name))
            .Concat(config.AutoRules.Where(r => r.Enabled));

    /// <summary>"Star Wars Collection" (the TMDb name) becomes "Star Wars".</summary>
    public static string FranchiseTitle(string tmdbName)
    {
        var name = (tmdbName ?? string.Empty).Trim();
        foreach (var suffix in new[] { " Collection", " - Collection", " (Collection)" })
        {
            if (name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
            {
                return name[..^suffix.Length].Trim();
            }
        }

        return name;
    }

    public static List<CollectionRule> BuildAutoRules(
        PluginConfiguration config, IReadOnlyList<BaseItem> items, ISet<string> takenNames)
    {
        var result = new List<CollectionRule>();
        var countries = config.AutoCountries.Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim()).Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
        if (!config.AutoGenreCollections && countries.Count == 0 && !config.AutoFranchiseCollections)
        {
            return result;
        }

        var type = config.AutoMediaType is "Series" or "Both" ? config.AutoMediaType : "Movie";
        var pool = items.Where(i => type == "Both" || (type == "Movie" ? i is Movie : i is Series)).ToList();
        var noun = type switch { "Series" => "Series", "Both" => "Titles", _ => "Movies" };

        // Group tiers keep franchises together at the top of the Collections page, then countries, then genres.
        // Within a tier Jellyfin sorts by name. With grouping off the automatic ones sort by name only.
        var tier = 0;

        void Add(string name, CollectionRule rule, int count, int minimum)
        {
            if (count < minimum || takenNames.Contains(name)) return;
            rule.Name = name;
            rule.MediaType = rule.MediaType == "Movie" && type != "Movie" && rule.Franchises.Length == 0 ? type : rule.MediaType;
            rule.Position = config.GroupAutoCollections ? tier : 0;
            rule.ShowOnHome = config.AutoShowOnHome;
            result.Add(rule);
            takenNames.Add(name);
        }

        if (config.AutoGenreCollections)
        {
            tier = 400;
            foreach (var g in pool.SelectMany(i => i.Genres).Where(g => !string.IsNullOrWhiteSpace(g))
                         .GroupBy(g => g.Trim(), System.StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, System.StringComparer.OrdinalIgnoreCase))
            {
                Add($"{g.Key} {noun}", new CollectionRule { Genres = new[] { g.Key }, MediaType = type, ItemOrder = config.AutoItemOrder },
                    g.Count(), config.AutoMinItems);
            }
        }

        // Only the countries named in settings, never every country in the library.
        tier = 300;
        foreach (var country in countries)
        {
            var count = pool.Count(i => i.ProductionLocations.Any(c => CountryNames.Same(c, country)));
            Add(CountryNames.CollectionName(country, noun),
                new CollectionRule { Countries = new[] { CountryNames.Canon(country) }, MediaType = type, ItemOrder = config.AutoItemOrder },
                count, config.MinItems);
        }

        // Real franchises (Star Wars, Indiana Jones) from the collection names TMDb gives each movie.
        if (config.AutoFranchiseCollections)
        {
            tier = 200;
            var groups = items.OfType<Movie>()
                .Where(m => !string.IsNullOrWhiteSpace(m.CollectionName))
                .GroupBy(m => m.CollectionName!.Trim(), System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => FranchiseTitle(g.Key), System.StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                Add(FranchiseTitle(g.Key),
                    new CollectionRule
                    {
                        Franchises = new[] { g.Key },
                        MediaType = "Movie",
                        ItemOrder = config.FranchiseItemOrder,
                        HomeSort = "Same"
                    },
                    g.Count(), Math.Max(2, config.FranchiseMinItems));
            }
        }

        return result;
    }

    /// <summary>
    /// The automatic collections that should actually be made: not already made by Jellyfin itself, and not an
    /// exact copy of one of your own rules. Shared by the sync and the "preview changes" report.
    /// </summary>
    public static List<CollectionRule> SelectAutoRules(
        PluginConfiguration config,
        IReadOnlyList<BaseItem> items,
        IReadOnlyCollection<string> existingCollectionNames,
        IReadOnlyList<CollectionRule> manual,
        Action<string>? onSkip = null)
    {
        var taken = new HashSet<string>(config.Rules.Select(r => r.Name), System.StringComparer.OrdinalIgnoreCase);
        var auto = BuildAutoRules(config, items, taken);
        var manualSets = manual.Select(r => Match(r, items).Select(i => i.Id).ToHashSet()).ToList();
        return auto.Where(a =>
        {
            // Jellyfin's own "automatically add to collection" option may already have made this franchise.
            if (a.Franchises.Length > 0 && existingCollectionNames.Any(n => a.Franchises.Any(f => string.Equals(n, f, System.StringComparison.OrdinalIgnoreCase))))
            {
                onSkip?.Invoke($"Franchise '{a.Name}' already has a Jellyfin-made collection; skipping");
                return false;
            }

            var ids = Match(a, items).Select(i => i.Id).ToHashSet();
            return !manualSets.Any(m => m.SetEquals(ids));
        }).ToList();
    }

    /// <summary>Minimum number of titles for a rule's collection to exist.</summary>
    public static int MinimumFor(CollectionRule rule, PluginConfiguration config, bool isAuto) =>
        !isAuto ? config.MinItems
        : rule.Franchises.Length > 0 ? System.Math.Max(2, config.FranchiseMinItems)
        : rule.Countries.Length > 0 ? config.MinItems
        : config.AutoMinItems;
}
