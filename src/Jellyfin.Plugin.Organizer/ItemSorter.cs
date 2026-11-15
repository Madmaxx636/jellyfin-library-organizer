using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Organizer;

/// <summary>Puts the titles of a collection in order, and works out where a collection sits among the others.</summary>
public static class ItemSorter
{
    public const string Release = "Release";
    public const string Year = "Year";
    public const string Name = "Name";
    public const string Rating = "Rating";
    public const string Runtime = "Runtime";
    public const string DateAdded = "DateAdded";
    public const string Story = "Story";
    public const string Random = "Random";

    /// <summary>
    /// Story (in-universe) order for franchises where it differs from release order, as TMDb movie ids.
    /// Only franchises listed here get a built-in story order; for any other, set your own order on a rule.
    /// </summary>
    private static readonly int[][] StoryLists =
    {
        // Star Wars: prequels, Clone Wars, Solo, Rogue One, original trilogy, sequels.
        new[] { 1893, 1894, 12180, 1895, 348350, 330459, 11, 1891, 1892, 140607, 181808, 181812 },

        // Indiana Jones: Temple of Doom (1935) comes before Raiders (1936).
        new[] { 87, 85, 89, 217, 335977 }
    };

    /// <summary>Accepts the older stored values (PremiereDate, SortName, CommunityRating) too.</summary>
    public static string Normalize(string? order) => order switch
    {
        "SortName" or "Name" => Name,
        "Year" or "ProductionYear" => Year,
        "CommunityRating" or "Rating" => Rating,
        "Runtime" => Runtime,
        "DateAdded" or "DateCreated" => DateAdded,
        "Story" => Story,
        "Random" => Random,
        _ => Release
    };

    public static DateTime ReleaseDate(BaseItem i) =>
        i.PremiereDate ?? (i.ProductionYear is int y && y > 0 && y < 9999 ? new DateTime(y, 1, 1) : DateTime.MaxValue);

    /// <summary>The name Jellyfin sorts by, falling back to the plain name when it has no sort name yet.</summary>
    private static string SortKey(BaseItem i)
    {
        try
        {
            return string.IsNullOrEmpty(i.SortName) ? i.Name ?? string.Empty : i.SortName;
        }
        catch (NullReferenceException)
        {
            return i.Name ?? string.Empty;
        }
    }

    private static bool HasDate(BaseItem i) => i.PremiereDate.HasValue || i.ProductionYear is > 0;

    private static int YearOf(BaseItem i) =>
        i.ProductionYear is int y && y > 0 ? y : i.PremiereDate?.Year ?? int.MaxValue;

    private static int TmdbId(BaseItem i) =>
        int.TryParse(i.GetProviderId(MetadataProvider.Tmdb), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;

    private static string EntryId(string entry) => entry.Split('|')[0];

    /// <summary>Position in the built-in story lists, or -1. Each list is offset so two franchises never interleave.</summary>
    public static int BuiltInStoryRank(BaseItem item)
    {
        var id = TmdbId(item);
        if (id == 0)
        {
            return -1;
        }

        for (var l = 0; l < StoryLists.Length; l++)
        {
            var at = Array.IndexOf(StoryLists[l], id);
            if (at >= 0)
            {
                return l * 1000 + at;
            }
        }

        return -1;
    }

    /// <summary>True when a built-in story order exists for at least one of these titles.</summary>
    public static bool HasBuiltInStory(IEnumerable<BaseItem> items) => items.Any(i => BuiltInStoryRank(i) >= 0);

    /// <summary>Orders titles. Titles without a value (no rating, no runtime) always go last.</summary>
    public static List<BaseItem> Order(IEnumerable<BaseItem> items, string? order, bool descending, string[]? story = null)
    {
        var list = items.ToList();
        var key = Normalize(order);
        IEnumerable<BaseItem> sorted;

        switch (key)
        {
            case Random:
                return list.OrderBy(_ => System.Random.Shared.Next()).ToList();

            case Name:
                sorted = list.OrderBy(SortKey, StringComparer.OrdinalIgnoreCase);
                return Finish(sorted, descending);

            case Year:
                return WithMissingLast(list, HasDate, i => YearOf(i), descending);

            case DateAdded:
                sorted = list.OrderBy(i => i.DateCreated);
                return Finish(sorted, descending);

            case Rating:
                return WithMissingLast(list, i => i.CommunityRating.HasValue, i => (double)i.CommunityRating!.Value, descending);

            case Runtime:
                return WithMissingLast(list, i => i.RunTimeTicks.HasValue, i => (double)i.RunTimeTicks!.Value, descending);

            case Story:
                var custom = (story ?? Array.Empty<string>()).Select(EntryId).ToList();
                sorted = list
                    .OrderBy(i =>
                    {
                        var at = custom.IndexOf(i.Id.ToString("N"));
                        return at >= 0 ? 0 : BuiltInStoryRank(i) >= 0 ? 1 : 2;
                    })
                    .ThenBy(i =>
                    {
                        var at = custom.IndexOf(i.Id.ToString("N"));
                        return at >= 0 ? at : BuiltInStoryRank(i) >= 0 ? BuiltInStoryRank(i) : 0;
                    })
                    .ThenBy(ReleaseDate)
                    .ThenBy(SortKey, StringComparer.OrdinalIgnoreCase);
                return Finish(sorted, descending);

            default:
                return WithMissingLast(list, HasDate, i => ReleaseDate(i).Ticks, descending);
        }
    }

    private static List<BaseItem> Finish(IEnumerable<BaseItem> sorted, bool descending)
    {
        var result = sorted.ToList();
        if (descending)
        {
            result.Reverse();
        }

        return result;
    }

    private static List<BaseItem> WithMissingLast(
        List<BaseItem> list, Func<BaseItem, bool> has, Func<BaseItem, double> value, bool descending)
    {
        var known = list.Where(has);
        var ordered = (descending ? known.OrderByDescending(value) : known.OrderBy(value))
            .ThenBy(SortKey, StringComparer.OrdinalIgnoreCase);
        return ordered.Concat(list.Where(i => !has(i)).OrderBy(SortKey, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Jellyfin sorts a collection page by itself only for a few keys and only ascending. For everything else
    /// the collection keeps the order we give it, so this says whether Jellyfin can keep the order live.
    /// </summary>
    public static string? NativeDisplayOrder(string? order, bool descending)
    {
        if (descending)
        {
            return null;
        }

        return Normalize(order) switch
        {
            Name => "SortName",
            Release => "PremiereDate",
            DateAdded => "DateCreated",
            _ => null
        };
    }

    /// <summary>
    /// The sort name for a collection, which sets where it sits on the Collections page. A pinned position wins;
    /// otherwise the chosen collection order (Name, Newest, Oldest, Rating, Size) decides.
    /// </summary>
    public static string CollectionSortName(CollectionRule rule, IReadOnlyList<BaseItem> matched, string? collectionOrder)
    {
        string Pad(long n, int w) => Math.Max(0, n).ToString("D" + w, CultureInfo.InvariantCulture);
        var dated = matched.Select(ReleaseDate).Where(d => d != DateTime.MaxValue).ToList();
        string key = collectionOrder switch
        {
            "Newest" when dated.Count > 0 => Pad(999999 - (dated.Max().Year * 12L + dated.Max().Month), 6) + " ",
            "Oldest" when dated.Count > 0 => Pad(dated.Min().Year * 12L + dated.Min().Month, 6) + " ",
            "Rating" => Pad(1000 - (long)Math.Round(matched.Where(m => m.CommunityRating.HasValue)
                .Select(m => (double)m.CommunityRating!.Value).DefaultIfEmpty(0).Average() * 10), 4) + " ",
            "Size" => Pad(99999 - matched.Count, 5) + " ",
            _ => string.Empty
        };

        if (rule.Position > 0)
        {
            return $"{rule.Position:D3} {key}{rule.Name}";
        }

        return key.Length > 0 ? $"500 {key}{rule.Name}" : string.Empty;
    }
}
