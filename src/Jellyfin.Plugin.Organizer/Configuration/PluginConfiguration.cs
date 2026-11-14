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

    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public double? MinCommunityRating { get; set; }

    /// <summary>Order of items inside the collection: PremiereDate, SortName or Rating.</summary>
    public string ItemOrder { get; set; } = "PremiereDate";

    /// <summary>Show this collection as a row on the home screen (needs Home Screen Sections).</summary>
    public bool ShowOnHome { get; set; } = true;

    /// <summary>Position among collections. Lower comes first. 0 = no forced position.</summary>
    public int Position { get; set; }
}

public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        Rules = new List<CollectionRule>
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
                Position = 20
            }
        };
    }

    public List<CollectionRule> Rules { get; set; }

    /// <summary>Remove items from managed collections when they stop matching.</summary>
    public bool RemoveNonMatching { get; set; } = true;

    /// <summary>Skip collections that would have fewer items than this.</summary>
    public int MinItems { get; set; } = 2;

    /// <summary>How many items each home screen row shows.</summary>
    public int HomeRowLimit { get; set; } = 16;
}
