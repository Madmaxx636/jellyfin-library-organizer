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

    public static List<CollectionRule> BuildAutoRules(
        PluginConfiguration config, IReadOnlyList<BaseItem> items, ISet<string> takenNames)
    {
        var result = new List<CollectionRule>();
        if (!config.AutoGenreCollections && !config.AutoCountryCollections)
        {
            return result;
        }

        var type = config.AutoMediaType is "Series" or "Both" ? config.AutoMediaType : "Movie";
        var pool = items.Where(i => type == "Both" || (type == "Movie" ? i is Movie : i is Series)).ToList();
        var noun = type switch { "Series" => "Series", "Both" => "Titles", _ => "Movies" };
        var position = 1000;

        void Add(string name, CollectionRule rule, int count)
        {
            if (count < config.AutoMinItems || takenNames.Contains(name)) return;
            rule.Name = name;
            rule.MediaType = type;
            rule.Position = position++;
            rule.ItemOrder = "SortName";
            rule.ShowOnHome = config.AutoShowOnHome;
            result.Add(rule);
            takenNames.Add(name);
        }

        if (config.AutoGenreCollections)
        {
            foreach (var g in pool.SelectMany(i => i.Genres).Where(g => !string.IsNullOrWhiteSpace(g))
                         .GroupBy(g => g.Trim(), System.StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, System.StringComparer.OrdinalIgnoreCase))
            {
                Add($"{g.Key} {noun}", new CollectionRule { Genres = new[] { g.Key } }, g.Count());
            }
        }

        if (config.AutoCountryCollections)
        {
            foreach (var c in pool.SelectMany(i => i.ProductionLocations).Where(c => !string.IsNullOrWhiteSpace(c))
                         .GroupBy(c => c.Trim(), System.StringComparer.OrdinalIgnoreCase)
                         .OrderBy(c => c.Key, System.StringComparer.OrdinalIgnoreCase))
            {
                Add($"{noun} from {c.Key}", new CollectionRule { Countries = new[] { c.Key } }, c.Count());
            }
        }

        return result;
    }
}
