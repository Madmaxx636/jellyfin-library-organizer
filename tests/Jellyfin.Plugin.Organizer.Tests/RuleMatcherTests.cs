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
        var cfg = new PluginConfiguration { AutoGenreCollections = true, AutoMinItems = 2, AutoFranchiseCollections = false };
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
        var cfg = new PluginConfiguration { AutoGenreCollections = false, SettingsVersion = 0 };
        Assert.True(PluginConfiguration.Migrate(cfg));
        Assert.True(cfg.AutoGenreCollections);
        // A later choice to switch them off must stick.
        cfg.AutoGenreCollections = false;
        Assert.False(PluginConfiguration.Migrate(cfg));
        Assert.False(cfg.AutoGenreCollections);
    }
}

public class CorrectionTests
{
    private static Movie Film(string name, string[]? genres = null, string[]? tags = null) =>
        new() { Id = Guid.NewGuid(), Name = name, ProductionYear = 2000, Genres = genres ?? Array.Empty<string>(), Tags = tags ?? Array.Empty<string>() };

    private static CollectionRule Christmas() => new()
    {
        Genres = new[] { "Holiday" }, Tags = new[] { "christmas" }, TitleKeywords = new[] { "christmas" }
    };

    [Fact]
    public void NeverIncludeBeatsEverythingElse()
    {
        var bond = Film("Casino Royale", new[] { "Action" }, new[] { "christmas" });
        var rule = Christmas();
        Assert.True(RuleMatcher.Matches(rule, bond));
        rule.ExcludeItems = new[] { bond.Id.ToString("N") + "|Casino Royale (2000)" };
        Assert.False(RuleMatcher.Matches(rule, bond));
    }

    [Fact]
    public void AlwaysIncludeWorksEvenWithNoMatchingFields()
    {
        var odd = Film("Die Hard", new[] { "Action" });
        var rule = Christmas();
        Assert.False(RuleMatcher.Matches(rule, odd));
        rule.IncludeItems = new[] { odd.Id.ToString("D") + "|Die Hard (1988)" };
        Assert.True(RuleMatcher.Matches(rule, odd));
    }

    [Fact]
    public void RequiringTwoFieldsDropsStrayTags()
    {
        var rule = Christmas();
        rule.MinFieldMatches = 2;
        var bond = Film("Casino Royale", new[] { "Action" }, new[] { "christmas" });
        var elf = Film("Elf", new[] { "Holiday" }, new[] { "christmas" });
        var polar = Film("The Christmas Express", new[] { "Holiday" });
        Assert.False(RuleMatcher.Matches(rule, bond));
        Assert.True(RuleMatcher.Matches(rule, elf));
        Assert.True(RuleMatcher.Matches(rule, polar));
    }

    [Fact]
    public void ExplainNamesTheSignals()
    {
        var elf = Film("Elf", new[] { "Holiday" }, new[] { "Christmas" });
        var why = RuleMatcher.Explain(Christmas(), elf);
        Assert.Contains("genre: Holiday", why);
        Assert.Contains("tag: christmas", why);
    }
}

public class ChristmasMigrationTests
{
    [Fact]
    public void StockChristmasRuleBecomesStricterOnce()
    {
        var cfg = new PluginConfiguration { SettingsVersion = 2, Rules = PluginConfiguration.DefaultRules() };
        var stock = cfg.Rules.First(r => r.Name == "Christmas Movies");
        stock.MinFieldMatches = 1;
        stock.PublicGenres = Array.Empty<string>();
        Assert.True(PluginConfiguration.Migrate(cfg));
        Assert.Equal(2, cfg.Rules.First(r => r.Name == "Christmas Movies").MinFieldMatches);
    }

    [Fact]
    public void CustomisedChristmasRuleIsLeftAlone()
    {
        var cfg = new PluginConfiguration { SettingsVersion = 2, Rules = PluginConfiguration.DefaultRules() };
        var r = cfg.Rules.First(x => x.Name == "Christmas Movies");
        r.MinFieldMatches = 1; r.Match = "All";
        PluginConfiguration.Migrate(cfg);
        Assert.Equal(1, r.MinFieldMatches);
    }
}

public class PublicDatabaseTests
{
    private const string Q = "Q28026639";

    private static Movie Film(string name, string imdb, string[]? genres = null, string[]? tags = null)
    {
        var m = new Movie { Id = Guid.NewGuid(), Name = name, Genres = genres ?? Array.Empty<string>(), Tags = tags ?? Array.Empty<string>() };
        m.ProviderIds["Imdb"] = imdb;
        return m;
    }

    private static CollectionRule Rule(string match = "Any") => new()
    {
        Match = match, MinFieldMatches = 2, PublicGenres = new[] { Q + "|Christmas film" },
        Genres = new[] { "Holiday" }, Tags = new[] { "christmas" }
    };

    public PublicDatabaseTests()
    {
        WikidataLists.Enabled = true;
        WikidataLists.Seed(Q, new[] { "tt0319343", "tt0338348" }, new[] { "10719" }, Array.Empty<string>());
    }

    [Fact]
    public void ListedTitleMatchesWithoutAnyTag()
    {
        Assert.True(RuleMatcher.Matches(Rule(), Film("Elf", "tt0319343")));
    }

    [Fact]
    public void UnlistedTitleWithOneStrayTagIsRejected()
    {
        Assert.False(RuleMatcher.Matches(Rule(), Film("Casino Royale", "tt0381061", tags: new[] { "christmas" })));
    }

    [Fact]
    public void UnlistedTitleStillQualifiesWithTwoLocalSignals()
    {
        Assert.True(RuleMatcher.Matches(Rule(), Film("Home Alone", "tt0099785", new[] { "Holiday" }, new[] { "christmas" })));
    }

    [Fact]
    public void TmdbMovieIdsDoNotCollideWithTvIds()
    {
        var m = new Movie { Id = Guid.NewGuid(), Name = "X" };
        m.ProviderIds["Tmdb"] = "10719";
        var s = new MediaBrowser.Controller.Entities.TV.Series { Id = Guid.NewGuid(), Name = "Y" };
        s.ProviderIds["Tmdb"] = "10719";
        Assert.True(WikidataLists.Contains(Q, m));
        Assert.False(WikidataLists.Contains(Q, s));
    }

    [Fact]
    public void OnlyPublicListAndNoHitMatchesNothing()
    {
        var rule = new CollectionRule { PublicGenres = new[] { Q + "|Christmas film" } };
        Assert.False(RuleMatcher.Matches(rule, Film("Die Hard", "tt0095016")));
        Assert.True(RuleMatcher.Matches(rule, Film("Elf", "tt0319343")));
    }

    [Fact]
    public void SwitchingTheDatabaseOffIgnoresTheList()
    {
        WikidataLists.Enabled = false;
        try { Assert.False(RuleMatcher.Matches(Rule(), Film("Elf", "tt0319343"))); }
        finally { WikidataLists.Enabled = true; }
    }

    [Fact]
    public void StockChristmasRuleGetsThePublicListOnMigration()
    {
        var cfg = new PluginConfiguration { SettingsVersion = 3, Rules = PluginConfiguration.DefaultRules() };
        var r = cfg.Rules.First(x => x.Name == "Christmas Movies");
        r.PublicGenres = Array.Empty<string>();
        Assert.True(PluginConfiguration.Migrate(cfg));
        Assert.Contains(PluginConfiguration.ChristmasFilm, r.PublicGenres);
    }
}

public class LooseChristmasMigrationTests
{
    [Fact]
    public void ChristmasRuleWithoutTheStockTagsIsStillTightened()
    {
        var rule = new CollectionRule { Name = "Christmas Movies", Tags = new[] { "holiday", "xmas" }, MinFieldMatches = 1 };
        var cfg = new PluginConfiguration { SettingsVersion = 4, Rules = new() { rule } };
        Assert.True(PluginConfiguration.Migrate(cfg));
        Assert.Equal(2, rule.MinFieldMatches);
        Assert.Contains(PluginConfiguration.ChristmasFilm, rule.PublicGenres);
    }

    [Fact]
    public void AlreadyStrictRuleKeepsItsOwnPublicLists()
    {
        var rule = new CollectionRule { Name = "Christmas Movies", MinFieldMatches = 3, PublicGenres = new[] { "Q1|Mine" } };
        var cfg = new PluginConfiguration { SettingsVersion = 4, Rules = new() { rule } };
        PluginConfiguration.Migrate(cfg);
        Assert.Equal(3, rule.MinFieldMatches);
        Assert.Equal(new[] { "Q1|Mine" }, rule.PublicGenres);
    }
}

public class StalePageTests
{
    [Fact]
    public void SaveFromAnOldPageDoesNotResetNewerSettings()
    {
        var existing = new PluginConfiguration
        {
            Rules = new() { new CollectionRule { Name = "Christmas Movies", MinFieldMatches = 2, PublicGenres = new[] { "Q1|X" }, ExcludeItems = new[] { "a|b" } } }
        };
        // What an old page sends: the rule without any of the newer fields.
        var incoming = new PluginConfiguration { Rules = new() { new CollectionRule { Name = "Christmas Movies" } }, PageVersion = 0 };
        PluginConfiguration.KeepNewerFields(incoming, existing);
        Assert.Equal(2, incoming.Rules[0].MinFieldMatches);
        Assert.Equal(new[] { "Q1|X" }, incoming.Rules[0].PublicGenres);
        Assert.Equal(new[] { "a|b" }, incoming.Rules[0].ExcludeItems);
    }

    [Fact]
    public void SaveFromTheCurrentPageIsTakenAsIs()
    {
        var existing = new PluginConfiguration { Rules = new() { new CollectionRule { Name = "R", MinFieldMatches = 3 } } };
        var incoming = new PluginConfiguration { Rules = new() { new CollectionRule { Name = "R", MinFieldMatches = 1 } }, PageVersion = PluginConfiguration.CurrentPageVersion };
        PluginConfiguration.KeepNewerFields(incoming, existing);
        Assert.Equal(1, incoming.Rules[0].MinFieldMatches);
    }
}
