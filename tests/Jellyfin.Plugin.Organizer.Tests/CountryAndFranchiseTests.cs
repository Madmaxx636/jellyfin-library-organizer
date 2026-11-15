using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Organizer;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

public class CountryAndFranchiseTests
{
    private static Movie Film(string name, int year, string? country = null, string? collection = null, string[]? genres = null) =>
        new()
        {
            Id = Guid.NewGuid(), Name = name, ProductionYear = year, CollectionName = collection,
            ProductionLocations = country is null ? Array.Empty<string>() : new[] { country },
            Genres = genres ?? Array.Empty<string>()
        };

    private static PluginConfiguration Quiet() => new()
    {
        AutoGenreCollections = false, AutoFranchiseCollections = false, AutoCountries = Array.Empty<string>(), MinItems = 2
    };

    [Fact]
    public void OnlyTheChosenCountriesGetACollection()
    {
        var cfg = Quiet();
        cfg.AutoCountries = new[] { "Philippines", "United Kingdom" };
        var items = new List<BaseItem>
        {
            Film("A", 2000, "Philippines"), Film("B", 2001, "Philippines"),
            Film("C", 2002, "United Kingdom"), Film("D", 2003, "United Kingdom"),
            Film("E", 2004, "Japan"), Film("F", 2005, "Japan"), Film("G", 2006, "France"), Film("H", 2007, "France")
        };
        var rules = LibraryQuery.BuildAutoRules(cfg, items, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(new[] { "Filipino Movies", "British Movies" }, rules.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void NoCountriesListedMeansNoCountryCollections()
    {
        var items = new List<BaseItem> { Film("A", 2000, "Japan"), Film("B", 2001, "Japan"), Film("C", 2002, "Japan") };
        Assert.Empty(LibraryQuery.BuildAutoRules(Quiet(), items, new HashSet<string>()));
    }

    [Fact]
    public void CountrySpellingsAndNamesAreHandled()
    {
        Assert.True(CountryNames.Same("USA", "United States of America"));
        Assert.True(CountryNames.Same("united states", "USA"));
        Assert.True(CountryNames.Same("Great Britain", "United Kingdom"));
        Assert.False(CountryNames.Same("Japan", "Jordan"));
        Assert.Equal("Filipino Movies", CountryNames.CollectionName("Philippines", "Movies"));
        Assert.Equal("Movies from Peru", CountryNames.CollectionName("Peru", "Movies"));
        Assert.Equal("American Series", CountryNames.CollectionName("USA", "Series"));
    }

    [Fact]
    public void CountryRuleMatchesAliasSpellings()
    {
        var rule = new CollectionRule { Countries = new[] { "United States of America" } };
        Assert.True(RuleMatcher.Matches(rule, Film("A", 2000, "United States")));
        Assert.True(RuleMatcher.Matches(rule, Film("B", 2000, "USA")));
        Assert.False(RuleMatcher.Matches(rule, Film("C", 2000, "Canada")));
    }

    [Fact]
    public void FranchisesAreGroupedAndNamedWithoutTheWordCollection()
    {
        var cfg = Quiet();
        cfg.AutoFranchiseCollections = true;
        var items = new List<BaseItem>
        {
            Film("A New Hope", 1977, collection: "Star Wars Collection"), Film("Empire", 1980, collection: "Star Wars Collection"),
            Film("Raiders", 1981, collection: "Indiana Jones Collection"), Film("Temple", 1984, collection: "Indiana Jones Collection"),
            Film("Lonely Sequel", 2000, collection: "Only One Collection"), Film("Plain", 2001)
        };
        var rules = LibraryQuery.BuildAutoRules(cfg, items, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(new[] { "Indiana Jones", "Star Wars" }, rules.Select(r => r.Name).ToArray());
        var sw = rules.First(r => r.Name == "Star Wars");
        Assert.Equal("PremiereDate", sw.ItemOrder);
        Assert.False(sw.HomeDescending);
        Assert.Equal(2, LibraryQuery.Match(sw, items).Count);
    }

    [Fact]
    public void FranchiseMinimumIsRespectedAndNeverBelowTwo()
    {
        var cfg = Quiet();
        cfg.AutoFranchiseCollections = true;
        cfg.FranchiseMinItems = 3;
        var items = new List<BaseItem>
        {
            Film("1", 2001, collection: "Trilogy Collection"), Film("2", 2002, collection: "Trilogy Collection"), Film("3", 2003, collection: "Trilogy Collection"),
            Film("x", 2001, collection: "Pair Collection"), Film("y", 2002, collection: "Pair Collection")
        };
        Assert.Equal(new[] { "Trilogy" }, LibraryQuery.BuildAutoRules(cfg, items, new HashSet<string>()).Select(r => r.Name).ToArray());
        cfg.FranchiseMinItems = 1;
        Assert.Equal(2, LibraryQuery.BuildAutoRules(cfg, items, new HashSet<string>()).Count);
    }

    [Fact]
    public void ExistingSettingsGetTheCountryListAndFranchisesOnMigration()
    {
        var cfg = new PluginConfiguration { SettingsVersion = 5, AutoCountries = Array.Empty<string>(), AutoFranchiseCollections = false };
        Assert.True(PluginConfiguration.Migrate(cfg));
        Assert.Equal(new[] { "Philippines", "United Kingdom" }, cfg.AutoCountries);
        Assert.True(cfg.AutoFranchiseCollections);
        // Once migrated, a later edit sticks.
        cfg.AutoCountries = new[] { "Japan" };
        Assert.False(PluginConfiguration.Migrate(cfg));
        Assert.Equal(new[] { "Japan" }, cfg.AutoCountries);
    }

    [Fact]
    public void FranchiseCoversUseReleaseOrder()
    {
        var items = new List<BaseItem> { Film("Return", 1983), Film("Hope", 1977), Film("Empire", 1980) };
        Assert.Equal(new[] { "Hope", "Empire", "Return" }, ArtworkService.PickTitles(items, true).Select(i => i.Name).ToArray());
    }

    [Fact]
    public void OldPageDoesNotResetCountryListOrFranchiseSettings()
    {
        var existing = new PluginConfiguration { AutoCountries = new[] { "Japan" }, AutoFranchiseCollections = false, FranchiseMinItems = 4 };
        var incoming = new PluginConfiguration { PageVersion = 3 };
        PluginConfiguration.KeepNewerFields(incoming, existing);
        Assert.Equal(new[] { "Japan" }, incoming.AutoCountries);
        Assert.False(incoming.AutoFranchiseCollections);
        Assert.Equal(4, incoming.FranchiseMinItems);
    }

    [Fact]
    public void AutomaticCollectionsSortFranchisesThenCountriesThenGenres()
    {
        var cfg = new PluginConfiguration { AutoCountries = new[] { "Philippines" }, AutoMinItems = 2, MinItems = 2 };
        var items = new List<BaseItem>
        {
            Film("A", 2000, "Philippines", "Alpha Collection", new[] { "Drama" }),
            Film("B", 2001, "Philippines", "Alpha Collection", new[] { "Drama" })
        };
        var rules = LibraryQuery.BuildAutoRules(cfg, items, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        int Tier(string n) => rules.Single(r => r.Name == n).Position;
        Assert.True(Tier("Alpha") < Tier("Filipino Movies"));
        Assert.True(Tier("Filipino Movies") < Tier("Drama Movies"));
        cfg.GroupAutoCollections = false;
        rules = LibraryQuery.BuildAutoRules(cfg, items, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.All(rules, r => Assert.Equal(0, r.Position));
    }

    [Fact]
    public void OldPageKeepsGroupingChoice()
    {
        var incoming = new PluginConfiguration { PageVersion = 4 };
        PluginConfiguration.KeepNewerFields(incoming, new PluginConfiguration { GroupAutoCollections = false });
        Assert.False(incoming.GroupAutoCollections);
    }
}
