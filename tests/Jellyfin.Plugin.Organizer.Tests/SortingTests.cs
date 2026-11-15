using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Organizer;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Xunit;

public class SortingTests
{
    private static Movie Film(string name, int? year, float? rating = null, long? runtimeMin = null, string? tmdb = null)
    {
        var m = new Movie
        {
            Id = Guid.NewGuid(), Name = name, SortName = name, ProductionYear = year, CommunityRating = rating,
            RunTimeTicks = runtimeMin is null ? null : runtimeMin * 600_000_000L
        };
        if (tmdb is not null) m.SetProviderId(MetadataProvider.Tmdb, tmdb);
        return m;
    }

    private static string[] Names(IEnumerable<BaseItem> items) => items.Select(i => i.Name).ToArray();

    [Fact]
    public void ReleaseOrderAscendingAndDescending()
    {
        var items = new BaseItem[] { Film("B", 2000), Film("C", 2010), Film("A", 1990) };
        Assert.Equal(new[] { "A", "B", "C" }, Names(ItemSorter.Order(items, "Release", false)));
        Assert.Equal(new[] { "C", "B", "A" }, Names(ItemSorter.Order(items, "Release", true)));
    }

    [Fact]
    public void OldStoredValuesStillWork()
    {
        var items = new BaseItem[] { Film("B", 2000), Film("A", 2010) };
        Assert.Equal(new[] { "A", "B" }, Names(ItemSorter.Order(items, "SortName", false)));
        Assert.Equal(new[] { "B", "A" }, Names(ItemSorter.Order(items, "PremiereDate", false)));
    }

    [Fact]
    public void UndatedTitlesAreAlwaysLast()
    {
        var items = new BaseItem[] { Film("None", null), Film("Old", 1990), Film("New", 2020) };
        Assert.Equal("None", Names(ItemSorter.Order(items, "Release", false)).Last());
        Assert.Equal("None", Names(ItemSorter.Order(items, "Release", true)).Last());
        Assert.Equal("None", Names(ItemSorter.Order(items, "Year", true)).Last());
    }

    [Fact]
    public void RatingPutsUnratedLastEitherWay()
    {
        var items = new BaseItem[] { Film("Mid", 2000, 6.5f), Film("Unrated", 2000), Film("Top", 2000, 9f), Film("Low", 2000, 3f) };
        Assert.Equal(new[] { "Top", "Mid", "Low", "Unrated" }, Names(ItemSorter.Order(items, "Rating", true)));
        Assert.Equal(new[] { "Low", "Mid", "Top", "Unrated" }, Names(ItemSorter.Order(items, "Rating", false)));
    }

    [Fact]
    public void RuntimeAndYearAndDateAdded()
    {
        var items = new BaseItem[] { Film("Long", 2001, runtimeMin: 180), Film("Short", 2000, runtimeMin: 80), Film("NoRun", 1999) };
        Assert.Equal(new[] { "Short", "Long", "NoRun" }, Names(ItemSorter.Order(items, "Runtime", false)));
        Assert.Equal(new[] { "NoRun", "Short", "Long" }, Names(ItemSorter.Order(items, "Year", false)));
        var a = Film("First", 2000); a.DateCreated = new DateTime(2020, 1, 1);
        var b = Film("Second", 2000); b.DateCreated = new DateTime(2024, 1, 1);
        Assert.Equal(new[] { "Second", "First" }, Names(ItemSorter.Order(new BaseItem[] { a, b }, "DateAdded", true)));
    }

    [Fact]
    public void BuiltInStoryOrderPutsTempleOfDoomFirst()
    {
        var items = new BaseItem[]
        {
            Film("Raiders", 1981, tmdb: "85"), Film("Temple of Doom", 1984, tmdb: "87"), Film("Last Crusade", 1989, tmdb: "89")
        };
        Assert.Equal(new[] { "Raiders", "Temple of Doom", "Last Crusade" }, Names(ItemSorter.Order(items, "Release", false)));
        Assert.Equal(new[] { "Temple of Doom", "Raiders", "Last Crusade" }, Names(ItemSorter.Order(items, "Story", false)));
        Assert.True(ItemSorter.HasBuiltInStory(items));
    }

    [Fact]
    public void StarWarsStoryOrderIsChronological()
    {
        var items = new BaseItem[]
        {
            Film("IV", 1977, tmdb: "11"), Film("I", 1999, tmdb: "1893"), Film("III", 2005, tmdb: "1895"), Film("V", 1980, tmdb: "1891"),
            Film("II", 2002, tmdb: "1894")
        };
        Assert.Equal(new[] { "I", "II", "III", "IV", "V" }, Names(ItemSorter.Order(items, "Story", false)));
    }

    [Fact]
    public void YourOwnStoryOrderBeatsTheBuiltInOne()
    {
        var a = Film("A", 2000); var b = Film("B", 2001); var c = Film("C", 2002);
        var story = new[] { c.Id.ToString("N") + "|C", a.Id.ToString("N") + "|A" };
        Assert.Equal(new[] { "C", "A", "B" }, Names(ItemSorter.Order(new BaseItem[] { a, b, c }, "Story", false, story)));
    }

    [Fact]
    public void StoryOrderWithNothingKnownFallsBackToRelease()
    {
        var items = new BaseItem[] { Film("B", 2005), Film("A", 1995) };
        Assert.Equal(new[] { "A", "B" }, Names(ItemSorter.Order(items, "Story", false)));
        Assert.False(ItemSorter.HasBuiltInStory(items));
    }

    [Fact]
    public void OnlyAscendingReleaseNameAndDateAddedStayLive()
    {
        Assert.Equal("PremiereDate", ItemSorter.NativeDisplayOrder("Release", false));
        Assert.Equal("SortName", ItemSorter.NativeDisplayOrder("SortName", false));
        Assert.Null(ItemSorter.NativeDisplayOrder("Release", true));
        Assert.Null(ItemSorter.NativeDisplayOrder("Story", false));
        Assert.Null(ItemSorter.NativeDisplayOrder("Rating", false));
    }

    [Fact]
    public void HomeRowFollowsTheCollectionByDefault()
    {
        var rule = new CollectionRule { ItemOrder = "Rating", ItemDescending = true };
        var items = new BaseItem[] { Film("Low", 2000, 3f), Film("High", 2000, 9f) };
        Assert.Equal(new[] { "High", "Low" }, Names(HomeResultsHandler.Sort(items, rule, "Same", true)));
        Assert.Equal(new[] { "Low", "High" }, Names(HomeResultsHandler.Sort(items, rule, "Rating", false)));
    }

    [Fact]
    public void CollectionSortNameUsesPinsThenTheChosenOrder()
    {
        var films = new List<BaseItem> { Film("A", 2001, 8f), Film("B", 2020, 6f) };
        Assert.Equal(string.Empty, ItemSorter.CollectionSortName(new CollectionRule { Name = "X" }, films, "Name"));
        Assert.Equal("010 X", ItemSorter.CollectionSortName(new CollectionRule { Name = "X", Position = 10 }, films, "Name"));
        var newer = ItemSorter.CollectionSortName(new CollectionRule { Name = "New" }, films, "Newest");
        var older = ItemSorter.CollectionSortName(new CollectionRule { Name = "Old" }, new List<BaseItem> { Film("C", 1980) }, "Newest");
        Assert.True(string.CompareOrdinal(newer, older) < 0);
        var big = ItemSorter.CollectionSortName(new CollectionRule { Name = "Big" }, films, "Size");
        var small = ItemSorter.CollectionSortName(new CollectionRule { Name = "Small" }, new List<BaseItem> { Film("C", 1980) }, "Size");
        Assert.True(string.CompareOrdinal(big, small) < 0);
    }

    [Fact]
    public void ServerOwnedFieldsSurviveAStaleSave()
    {
        var existing = new PluginConfiguration { ManagedCollectionNames = new() { "Star Wars" }, LegacyCountryCleanupDone = true };
        existing.AutoRules.Add(new CollectionRule { Name = "Star Wars" });
        var incoming = new PluginConfiguration { PageVersion = PluginConfiguration.CurrentPageVersion };
        PluginConfiguration.KeepNewerFields(incoming, existing);
        Assert.Single(incoming.AutoRules);
        Assert.Equal(new[] { "Star Wars" }, incoming.ManagedCollectionNames.ToArray());
        Assert.True(incoming.LegacyCountryCleanupDone);
    }
}
