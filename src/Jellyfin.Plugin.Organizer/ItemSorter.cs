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

    /// <summary>
    /// More story orders, matched by title and year so they work whichever metadata provider named the movie.
    /// Each set is listed in the order the story happens, which differs from release order. A year may be off by one.
    /// Several names for the same film are separated by "/".
    /// </summary>
    private static readonly (string Name, (string Title, int Year)[] Films)[] TitleSets =
    {
        ("Marvel Cinematic Universe", new[]
        {
            ("Captain America: The First Avenger", 2011), ("Captain Marvel", 2019), ("Iron Man", 2008), ("Iron Man 2", 2010),
            ("The Incredible Hulk", 2008), ("Thor", 2011), ("The Avengers/Marvel's The Avengers", 2012), ("Thor: The Dark World", 2013),
            ("Iron Man 3", 2013), ("Captain America: The Winter Soldier", 2014), ("Guardians of the Galaxy", 2014),
            ("Guardians of the Galaxy Vol. 2", 2017), ("Ant-Man", 2015), ("Avengers: Age of Ultron", 2015),
            ("Captain America: Civil War", 2016), ("Black Widow", 2021), ("Black Panther", 2018), ("Spider-Man: Homecoming", 2017),
            ("Doctor Strange", 2016), ("Thor: Ragnarok", 2017), ("Ant-Man and the Wasp", 2018), ("Avengers: Infinity War", 2018),
            ("Avengers: Endgame", 2019), ("Spider-Man: Far From Home", 2019), ("Shang-Chi and the Legend of the Ten Rings", 2021),
            ("Eternals", 2021), ("Doctor Strange in the Multiverse of Madness", 2022), ("Spider-Man: No Way Home", 2021),
            ("Black Panther: Wakanda Forever", 2022), ("Thor: Love and Thunder", 2022),
            ("Ant-Man and the Wasp: Quantumania", 2023), ("Guardians of the Galaxy Vol. 3", 2023), ("The Marvels", 2023),
            ("Deadpool & Wolverine", 2024), ("Captain America: Brave New World", 2025), ("Thunderbolts*/Thunderbolts", 2025)
        }),
        ("X-Men", new[]
        {
            ("X-Men Origins: Wolverine", 2009), ("X-Men: First Class", 2011), ("X-Men: Days of Future Past", 2014),
            ("X-Men: Apocalypse", 2016), ("X-Men: Dark Phoenix/Dark Phoenix", 2019), ("The New Mutants", 2020), ("X-Men", 2000),
            ("X2/X-Men 2/X2: X-Men United", 2003), ("X-Men: The Last Stand", 2006), ("The Wolverine", 2013), ("Logan", 2017)
        }),
        ("Fast and Furious", new[]
        {
            ("The Fast and the Furious", 2001), ("2 Fast 2 Furious", 2003), ("Fast & Furious", 2009), ("Fast Five", 2011),
            ("Fast & Furious 6/Furious 6", 2013), ("The Fast and the Furious: Tokyo Drift", 2006), ("Furious 7", 2015),
            ("The Fate of the Furious", 2017), ("Fast & Furious Presents: Hobbs & Shaw/Hobbs & Shaw", 2019), ("F9/F9: The Fast Saga", 2021),
            ("Fast X", 2023)
        }),
        ("Planet of the Apes", new[]
        {
            ("Rise of the Planet of the Apes", 2011), ("Dawn of the Planet of the Apes", 2014), ("War for the Planet of the Apes", 2017),
            ("Kingdom of the Planet of the Apes", 2024), ("Planet of the Apes", 1968), ("Beneath the Planet of the Apes", 1970),
            ("Escape from the Planet of the Apes", 1971), ("Conquest of the Planet of the Apes", 1972), ("Battle for the Planet of the Apes", 1973)
        }),
        ("Alien", new[]
        {
            ("Prometheus", 2012), ("Alien: Covenant", 2017), ("Alien", 1979), ("Alien: Romulus", 2024), ("Aliens", 1986),
            ("Alien 3/Alien³", 1992), ("Alien: Resurrection", 1997)
        }),
        ("Middle-earth", new[]
        {
            ("The Hobbit: An Unexpected Journey", 2012), ("The Hobbit: The Desolation of Smaug", 2013),
            ("The Hobbit: The Battle of the Five Armies", 2014), ("The Lord of the Rings: The Fellowship of the Ring", 2001),
            ("The Lord of the Rings: The Two Towers", 2002), ("The Lord of the Rings: The Return of the King", 2003)
        }),
        ("Mad Max", new[]
        {
            ("Mad Max", 1979), ("Mad Max 2/Mad Max 2: The Road Warrior", 1981), ("Mad Max Beyond Thunderdome", 1985),
            ("Furiosa: A Mad Max Saga", 2024), ("Mad Max: Fury Road", 2015)
        }),
        ("Predator", new[]
        {
            ("Prey", 2022), ("Predator", 1987), ("Predator 2", 1990), ("Predators", 2010), ("The Predator", 2018), ("Predator: Badlands", 2025)
        }),
        ("The Conjuring Universe", new[]
        {
            ("The Nun", 2018), ("Annabelle: Creation", 2017), ("The Nun II", 2023), ("Annabelle", 2014), ("The Conjuring", 2013),
            ("Annabelle Comes Home", 2019), ("The Conjuring 2", 2016), ("The Conjuring: The Devil Made Me Do It", 2021),
            ("The Conjuring: Last Rites", 2025)
        }),
        ("Transformers", new[]
        {
            ("Bumblebee", 2018), ("Transformers: Rise of the Beasts", 2023), ("Transformers", 2007),
            ("Transformers: Revenge of the Fallen", 2009), ("Transformers: Dark of the Moon", 2011),
            ("Transformers: Age of Extinction", 2014), ("Transformers: The Last Knight", 2017)
        }),
        ("Despicable Me", new[]
        {
            ("Minions", 2015), ("Minions: The Rise of Gru", 2022), ("Despicable Me", 2010), ("Despicable Me 2", 2013),
            ("Despicable Me 3", 2017), ("Despicable Me 4", 2024)
        }),
        ("The Hunger Games", new[]
        {
            ("The Hunger Games: The Ballad of Songbirds & Snakes", 2023), ("The Hunger Games", 2012),
            ("The Hunger Games: Catching Fire", 2013), ("The Hunger Games: Mockingjay - Part 1", 2014),
            ("The Hunger Games: Mockingjay - Part 2", 2015)
        }),
        ("Insidious", new[]
        {
            ("Insidious: Chapter 3", 2015), ("Insidious: The Last Key", 2018), ("Insidious", 2010), ("Insidious: Chapter 2", 2013),
            ("Insidious: The Red Door", 2023)
        }),
        ("The Purge", new[]
        {
            ("The First Purge", 2018), ("The Purge", 2013), ("The Purge: Anarchy", 2014), ("The Purge: Election Year", 2016),
            ("The Forever Purge", 2021)
        }),
        ("Monsters, Inc.", new[] { ("Monsters University", 2013), ("Monsters, Inc.", 2001) }),
        ("Kingsman", new[] { ("The King's Man", 2021), ("Kingsman: The Secret Service", 2014), ("Kingsman: The Golden Circle", 2017) }),
        ("The Omen", new[] { ("The First Omen", 2024), ("The Omen", 1976), ("Damien: Omen II", 1978), ("Omen III: The Final Conflict", 1981) }),
        ("Underworld", new[]
        {
            ("Underworld: Rise of the Lycans", 2009), ("Underworld", 2003), ("Underworld: Evolution", 2006),
            ("Underworld: Awakening", 2012), ("Underworld: Blood Wars", 2016)
        }),
        ("Hannibal Lecter", new[]
        {
            ("Hannibal Rising", 2007), ("Manhunter", 1986), ("Red Dragon", 2002), ("The Silence of the Lambs", 1991), ("Hannibal", 2001)
        })
    };

    private static readonly Lazy<Dictionary<string, List<(int Set, int Pos, int Year)>>> TitleIndex = new(() =>
    {
        var index = new Dictionary<string, List<(int, int, int)>>();
        for (var s = 0; s < TitleSets.Length; s++)
        {
            for (var f = 0; f < TitleSets[s].Films.Length; f++)
            {
                foreach (var alias in TitleSets[s].Films[f].Title.Split('/'))
                {
                    var key = TitleKey(alias);
                    if (!index.TryGetValue(key, out var list))
                    {
                        index[key] = list = new List<(int, int, int)>();
                    }

                    list.Add((s, f, TitleSets[s].Films[f].Year));
                }
            }
        }

        return index;
    });

    /// <summary>Names of the story sets that are built in, for the settings page.</summary>
    public static IEnumerable<string> BuiltInStoryNames =>
        new[] { "Star Wars", "Indiana Jones" }.Concat(TitleSets.Select(s => s.Name));

    /// <summary>"The Fast & the Furious" and "fast and the furious" give the same key.</summary>
    public static string TitleKey(string? title)
    {
        var t = (title ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD).ToLowerInvariant().Replace("&", "and");
        var sb = new System.Text.StringBuilder();
        foreach (var c in t)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        var key = sb.ToString();
        return key.StartsWith("the", StringComparison.Ordinal) && key.Length > 6 ? key[3..] : key;
    }

    private static int TitleRank(BaseItem item)
    {
        var year = item.ProductionYear ?? item.PremiereDate?.Year ?? 0;
        foreach (var name in new[] { item.Name, item.OriginalTitle })
        {
            if (string.IsNullOrWhiteSpace(name) || !TitleIndex.Value.TryGetValue(TitleKey(name), out var hits))
            {
                continue;
            }

            foreach (var hit in hits)
            {
                if (year == 0 || Math.Abs(hit.Year - year) <= 1)
                {
                    return (StoryLists.Length + hit.Set) * 1000 + hit.Pos;
                }
            }
        }

        return -1;
    }

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
        if (id != 0)
        {
            for (var l = 0; l < StoryLists.Length; l++)
            {
                var at = Array.IndexOf(StoryLists[l], id);
                if (at >= 0)
                {
                    return l * 1000 + at;
                }
            }
        }

        return TitleRank(item);
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
