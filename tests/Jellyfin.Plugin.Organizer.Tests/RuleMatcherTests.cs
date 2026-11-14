using System.Linq;
using System;
using Jellyfin.Plugin.Organizer;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

public class RuleMatcherTests
{
    private static Movie Film(string name, int year, string[]? genres = null, string[]? tags = null,
        string[]? countries = null, string[]? studios = null, string? rating = null, float? score = null) =>
        new()
        {
            Name = name,
            ProductionYear = year,
            Genres = genres ?? Array.Empty<string>(),
            Tags = tags ?? Array.Empty<string>(),
            ProductionLocations = countries ?? Array.Empty<string>(),
            Studios = studios ?? Array.Empty<string>(),
            OfficialRating = rating,
            CommunityRating = score
        };

    [Fact]
    public void AnyMatchesOneField()
    {
        var rule = new CollectionRule { Genres = new[] { "Holiday" }, Tags = new[] { "christmas" } };
        Assert.True(RuleMatcher.Matches(rule, Film("A", 2000, genres: new[] { "Holiday" })));
        Assert.True(RuleMatcher.Matches(rule, Film("B", 2000, tags: new[] { "Christmas" })));
        Assert.False(RuleMatcher.Matches(rule, Film("C", 2000, genres: new[] { "Drama" })));
    }

    [Fact]
    public void AllNeedsEveryField()
    {
        var rule = new CollectionRule
        {
            Match = "All", Countries = new[] { "Japan" }, Genres = new[] { "Animation" }
        };
        Assert.True(RuleMatcher.Matches(rule, Film("A", 2001, new[] { "Animation" }, countries: new[] { "Japan" })));
        Assert.False(RuleMatcher.Matches(rule, Film("B", 2001, new[] { "Animation" }, countries: new[] { "France" })));
    }

    [Fact]
    public void ExcludesAlwaysWin()
    {
        var rule = new CollectionRule { Genres = new[] { "Comedy" }, ExcludeTags = new[] { "adult" }, ExcludeKeywords = new[] { "Parody" } };
        Assert.True(RuleMatcher.Matches(rule, Film("Fun", 2000, new[] { "Comedy" })));
        Assert.False(RuleMatcher.Matches(rule, Film("Fun", 2000, new[] { "Comedy" }, tags: new[] { "Adult" })));
        Assert.False(RuleMatcher.Matches(rule, Film("Scary Parody", 2000, new[] { "Comedy" })));
    }

    [Fact]
    public void OnlyExcludesMeansEverythingElse()
    {
        var rule = new CollectionRule { ExcludeGenres = new[] { "Horror" } };
        Assert.True(RuleMatcher.Matches(rule, Film("A", 2000, new[] { "Drama" })));
        Assert.False(RuleMatcher.Matches(rule, Film("B", 2000, new[] { "Horror" })));
    }

    [Fact]
    public void LimitsApplyOnTopOfLists()
    {
        var rule = new CollectionRule { Genres = new[] { "Action" }, YearFrom = 1990, YearTo = 1999, MinCommunityRating = 7 };
        Assert.True(RuleMatcher.Matches(rule, Film("A", 1995, new[] { "Action" }, score: 7.5f)));
        Assert.False(RuleMatcher.Matches(rule, Film("B", 2005, new[] { "Action" }, score: 9f)));
        Assert.False(RuleMatcher.Matches(rule, Film("C", 1995, new[] { "Action" }, score: 5f)));
    }

    [Fact]
    public void EmptyRuleMatchesNothing()
    {
        Assert.False(RuleMatcher.Matches(new CollectionRule(), Film("A", 2000, new[] { "Drama" })));
    }

    [Theory]
    [InlineData("Korean", "kor", true)]
    [InlineData("ko", "kor", true)]
    [InlineData("German", "ger", true)]
    [InlineData("deu", "ger", true)]
    [InlineData("Filipino", "tgl", true)]
    [InlineData("Tagalog", "fil", true)]
    [InlineData("Korean", "jpn", false)]
    [InlineData("klingon", "tlh", false)]
    public void LanguageNamesAndCodesAgree(string wanted, string stream, bool expected)
    {
        Assert.Equal(expected, Languages.AnyMatch(new[] { stream }, new[] { wanted }));
    }

    [Fact]
    public void AutoRulesSkipNamesAlreadyTaken()
    {
        var cfg = new PluginConfiguration { AutoGenreCollections = true, AutoMinItems = 2 };
        var items = new System.Collections.Generic.List<MediaBrowser.Controller.Entities.BaseItem>
        {
            Film("A", 2000, new[] { "Comedy", "Drama" }), Film("B", 2001, new[] { "Comedy", "Drama" }),
            Film("C", 2002, new[] { "Comedy" }), Film("D", 2003, new[] { "Horror" })
        };
        var taken = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Drama Movies" };
        var rules = LibraryQuery.BuildAutoRules(cfg, items, taken);
        Assert.Single(rules);
        Assert.Equal("Comedy Movies", rules[0].Name);
    }
}

public class ConfigurationTests
{
    [Fact]
    public void SavedRulesAreNotMultipliedOnLoad()
    {
        var saved = new PluginConfiguration { Rules = new() { new CollectionRule { Name = "Only One" } } };
        var xml = new System.Xml.Serialization.XmlSerializer(typeof(PluginConfiguration));
        using var w = new System.IO.StringWriter();
        xml.Serialize(w, saved);
        var loaded = (PluginConfiguration)xml.Deserialize(new System.IO.StringReader(w.ToString()))!;
        Assert.Single(loaded.Rules);
        Assert.Equal("Only One", loaded.Rules[0].Name);
    }
}

public class HomeSortTests
{
    [Fact]
    public void NewestFirstFallsBackToProductionYear()
    {
        var items = new MediaBrowser.Controller.Entities.BaseItem[]
        {
            new Movie { Name = "Old", ProductionYear = 1990 },
            new Movie { Name = "New", ProductionYear = 2020 },
            new Movie { Name = "Mid", ProductionYear = 2005 },
        };
        var order = Jellyfin.Plugin.Organizer.HomeResultsHandler.Sort(items, "PremiereDate", true).Select(i => i.Name);
        Assert.Equal(new[] { "New", "Mid", "Old" }, order);
        var asc = Jellyfin.Plugin.Organizer.HomeResultsHandler.Sort(items, "PremiereDate", false).Select(i => i.Name);
        Assert.Equal(new[] { "Old", "Mid", "New" }, asc);
    }
}

public class MigrationTests
{
    [Fact]
    public void OlderSettingsGetAutoCollectionsTurnedOnOnce()
    {
        var cfg = new PluginConfiguration { AutoGenreCollections = false, AutoCountryCollections = false, SettingsVersion = 0 };
        Assert.True(PluginConfiguration.Migrate(cfg));
        Assert.True(cfg.AutoGenreCollections);
        Assert.True(cfg.AutoCountryCollections);
        // A later choice to switch them off must stick.
        cfg.AutoGenreCollections = false;
        Assert.False(PluginConfiguration.Migrate(cfg));
        Assert.False(cfg.AutoGenreCollections);
    }
}
