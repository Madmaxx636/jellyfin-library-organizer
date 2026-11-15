using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>How many of the filled-in fields must match (1 = any one). Ignored when Match is "All".</summary>
    public int MinFieldMatches { get; set; } = 1;

    /// <summary>
    /// Public database (Wikidata) genres, as "Q-id|Label". A title on one of these lists counts as a match
    /// by itself, or as one field when "Combine fields" is "All".
    /// </summary>
    public string[] PublicGenres { get; set; } = Array.Empty<string>();

    /// <summary>Titles to never include, as "id|Name (year)". Wins over every other setting.</summary>
    public string[] ExcludeItems { get; set; } = Array.Empty<string>();

    /// <summary>Titles to always include, as "id|Name (year)".</summary>
    public string[] IncludeItems { get; set; } = Array.Empty<string>();

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

    public const string ChristmasFilm = "Q28026639|Christmas film";

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
            MinFieldMatches = 2,
            PublicGenres = new[] { ChristmasFilm },
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
    public bool AutoGenreCollections { get; set; } = true;

    /// <summary>One collection per production country ("Movies from Japan").</summary>
    public bool AutoCountryCollections { get; set; } = true;

    /// <summary>Movies, Series or Both for the automatic collections.</summary>
    public string AutoMediaType { get; set; } = "Movie";

    public int AutoMinItems { get; set; } = 5;

    public bool AutoShowOnHome { get; set; }

    /// <summary>Re-sync a couple of minutes after movies or shows are added or their metadata changes.</summary>
    public bool SyncOnLibraryChange { get; set; } = true;

    /// <summary>Look up curated lists on Wikidata (only a genre id leaves the server). Turn off for fully offline use.</summary>
    public bool UsePublicDatabase { get; set; } = true;

    /// <summary>Run a sync about a minute after the server starts.</summary>
    public bool SyncOnStartup { get; set; } = true;

    /// <summary>Bumped when a new version needs to change older saved settings once.</summary>
    public int SettingsVersion { get; set; }

    public const int CurrentSettingsVersion = 5;

    /// <summary>
    /// Bumped whenever the settings page gains fields. A page older than this can't send them, so a save from
    /// it must not reset them. Also part of the page address, so browsers never reuse an old cached page.
    /// </summary>
    public const int CurrentPageVersion = 2;

    public int PageVersion { get; set; }

    /// <summary>
    /// Called with a save from an out-of-date (cached) settings page: keep the newer per-rule settings and
    /// options that page doesn't know about, instead of resetting them to defaults.
    /// </summary>
    public static void KeepNewerFields(PluginConfiguration incoming, PluginConfiguration existing)
    {
        if (incoming.PageVersion >= CurrentPageVersion)
        {
            return;
        }

        var byName = existing.Rules
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var rule in incoming.Rules)
        {
            if (!byName.TryGetValue(rule.Name, out var old))
            {
                continue;
            }

            rule.MinFieldMatches = old.MinFieldMatches;
            rule.PublicGenres = old.PublicGenres;
            rule.ExcludeItems = old.ExcludeItems;
            rule.IncludeItems = old.IncludeItems;
        }

        incoming.UsePublicDatabase = existing.UsePublicDatabase;
        incoming.SettingsVersion = Math.Max(incoming.SettingsVersion, existing.SettingsVersion);
        incoming.PageVersion = CurrentPageVersion;
    }

    /// <summary>
    /// Version 2 turned the automatic genre and country collections and both auto-syncs on by default.
    /// Versions 3 to 5 tightened the stock Christmas rule: a single stray "christmas" tag put hundreds of
    /// unrelated titles in it, so it now needs two of genre, tag and title word, or a place on the
    /// Wikidata "Christmas film" list.
    /// </summary>
    public static bool Migrate(PluginConfiguration config)
    {
        if (config.SettingsVersion >= CurrentSettingsVersion)
        {
            return false;
        }

        if (config.SettingsVersion < 2)
        {
            config.AutoGenreCollections = true;
            config.AutoCountryCollections = true;
            config.SyncOnLibraryChange = true;
            config.SyncOnStartup = true;
        }

        // The stock Christmas rule lets one stray tag in. Tighten it unless it was clearly rebuilt by hand
        // ("All" matching) or already strict.
        foreach (var rule in config.Rules)
        {
            var christmas = string.Equals(rule.Name, "Christmas Movies", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(rule.Match, "All", StringComparison.OrdinalIgnoreCase);
            if (!christmas)
            {
                continue;
            }

            if (rule.MinFieldMatches < 2)
            {
                rule.MinFieldMatches = 2;
            }

            if (rule.PublicGenres.Length == 0)
            {
                rule.PublicGenres = new[] { ChristmasFilm };
            }
        }

        config.SettingsVersion = CurrentSettingsVersion;
        return true;
    }

    /// <summary>Delete a collection this plugin made when its rule is removed or disabled. Media files are never touched.</summary>
    public bool DeleteRemovedCollections { get; set; }

    // Written by the sync task, not edited by hand.
    public List<CollectionRule> AutoRules { get; set; } = new();
    public List<string> ManagedCollectionNames { get; set; } = new();
}
