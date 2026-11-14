using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Organizer.Configuration;

public class CollectionRule
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>Movie, Series or Both.</summary>
    public string MediaType { get; set; } = "Movie";

    /// <summary>How the filled-in fields combine: "Any" = one field matching is enough, "All" = every filled field must match. Values inside a field are always "any of".</summary>
    public string Match { get; set; } = "Any";

    public string[] Genres { get; set; } = Array.Empty<string>();
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] Countries { get; set; } = Array.Empty<string>();
    public string[] Studios { get; set; } = Array.Empty<string>();
    public string[] TitleKeywords { get; set; } = Array.Empty<string>();
    public string[] OfficialRatings { get; set; } = Array.Empty<string>();

    /// <summary>Audio languages, by name (Korean) or code (kor, ko). Movies only, read from the audio tracks.</summary>
    public string[] Languages { get; set; } = Array.Empty<string>();

    /// <summary>Items matching any of these are always left out.</summary>
    public string[] ExcludeGenres { get; set; } = Array.Empty<string>();
    public string[] ExcludeTags { get; set; } = Array.Empty<string>();
    public string[] ExcludeCountries { get; set; } = Array.Empty<string>();
    public string[] ExcludeKeywords { get; set; } = Array.Empty<string>();

    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public double? MinCommunityRating { get; set; }

    /// <summary>Order of items inside the collection page: PremiereDate or SortName.</summary>
    public string ItemOrder { get; set; } = "PremiereDate";

    /// <summary>Show this collection as a row on the home screen (needs Home Screen Sections).</summary>
    public bool ShowOnHome { get; set; } = true;

    /// <summary>Home row sort: PremiereDate, SortName, CommunityRating, DateAdded or Random.</summary>
    public string HomeSort { get; set; } = "PremiereDate";

    public bool HomeDescending { get; set; } = true;

    /// <summary>Position among collections. Lower comes first. 0 = no forced position.</summary>
    public int Position { get; set; }
}

public class PluginConfiguration : BasePluginConfiguration
{
    // Do not fill Rules here: the XML loader appends saved rules to whatever the constructor made,
    // which duplicated the defaults on every load. Plugin seeds them once instead.
    public List<CollectionRule> Rules { get; set; } = new();

    public bool DefaultsSeeded { get; set; }

    public static List<CollectionRule> DefaultRules() => new()
    {
        new()
        {
            Name = "Filipino Movies",
            Countries = new[] { "Philippines" },
            Position = 10
        },
        new()
        {
            Name = "Christmas Movies",
            Genres = new[] { "Holiday" },
            Tags = new[] { "christmas", "xmas" },
            TitleKeywords = new[] { "christmas", "xmas", "santa" },
            ItemOrder = "SortName",
            HomeSort = "SortName",
            HomeDescending = false,
            Position = 20
        }
    };

    /// <summary>Remove items from managed collections when they stop matching.</summary>
    public bool RemoveNonMatching { get; set; } = true;

    /// <summary>Skip collections that would have fewer items than this.</summary>
    public int MinItems { get; set; } = 2;

    /// <summary>How many items each home screen row shows.</summary>
    public int HomeRowLimit { get; set; } = 16;

    /// <summary>One collection per genre ("Comedy Movies").</summary>
    public bool AutoGenreCollections { get; set; }

    /// <summary>One collection per production country ("Movies from Japan").</summary>
    public bool AutoCountryCollections { get; set; }

    /// <summary>Movies, Series or Both for the automatic collections.</summary>
    public string AutoMediaType { get; set; } = "Movie";

    public int AutoMinItems { get; set; } = 5;

    public bool AutoShowOnHome { get; set; }

    /// <summary>Re-sync a couple of minutes after new movies or shows are added.</summary>
    public bool SyncOnLibraryChange { get; set; } = true;

    /// <summary>Delete a collection this plugin made when its rule is removed or disabled. Media files are never touched.</summary>
    public bool DeleteRemovedCollections { get; set; }

    // Written by the sync task, not edited by hand.
    public List<CollectionRule> AutoRules { get; set; } = new();
    public List<string> ManagedCollectionNames { get; set; } = new();
}
