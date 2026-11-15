using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.Organizer;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

public class LearningTests : IDisposable
{
    private const string Q = "Q99999991";

    public LearningTests()
    {
        Learning.Reset();
        WikidataLists.Enabled = true;
    }

    public void Dispose() => Learning.Reset();

    private static Movie Film(string name, string imdb, string[]? genres = null, string[]? tags = null, int year = 2000)
    {
        var m = new Movie { Id = Guid.NewGuid(), Name = name, ProductionYear = year, Genres = genres ?? Array.Empty<string>(), Tags = tags ?? Array.Empty<string>() };
        m.ProviderIds["Imdb"] = imdb;
        return m;
    }

    private static CollectionRule Rule() => new()
    {
        Name = "Christmas Movies", MinFieldMatches = 1, PublicGenres = new[] { Q + "|Christmas film" },
        Genres = new[] { "Holiday", "Musical" }, Tags = new[] { "christmas" }, TitleKeywords = new[] { "christmas" }
    };

    private static List<MediaBrowser.Controller.Entities.BaseItem> Library(out HashSet<string> known)
    {
        var items = new List<MediaBrowser.Controller.Entities.BaseItem>();
        known = new HashSet<string>();
        // 25 real Christmas films (on the public list) and 30 ordinary musicals.
        for (var i = 0; i < 25; i++)
        {
            var id = $"tt90000{i:D2}";
            known.Add(id);
            items.Add(Film($"Holiday film {i}", id, new[] { "Holiday" }));
        }

        for (var i = 0; i < 30; i++)
        {
            items.Add(Film($"Musical {i}", $"tt80000{i:D2}", new[] { "Musical" }));
        }

        return items;
    }

    [Fact]
    public async System.Threading.Tasks.Task AGenreThatRarelyMatchesIsIgnored()
    {
        var items = Library(out var known);
        WikidataLists.Seed(Q, known, Array.Empty<string>(), Array.Empty<string>());
        Learning.CheckOnline = false;
        var rule = Rule();
        Assert.True(RuleMatcher.Matches(rule, items.Last()));

        await Learning.AnalyzeAsync(rule, items, null, CancellationToken.None);

        Assert.True(Learning.IsIgnored(rule.Name, "genre", "Musical"));
        Assert.False(Learning.IsIgnored(rule.Name, "genre", "Holiday"));
        Assert.False(RuleMatcher.Matches(rule, items.Last()));
        Assert.True(RuleMatcher.Matches(rule, items.First()));
    }

    [Fact]
    public async System.Threading.Tasks.Task TrustingASignalBringsItBack()
    {
        var items = Library(out var known);
        WikidataLists.Seed(Q, known, Array.Empty<string>(), Array.Empty<string>());
        Learning.CheckOnline = false;
        var rule = Rule();
        await Learning.AnalyzeAsync(rule, items, null, CancellationToken.None);
        Learning.Trust(rule.Name, "genre", "Musical", true);
        await Learning.AnalyzeAsync(rule, items, null, CancellationToken.None);
        Assert.False(Learning.IsIgnored(rule.Name, "genre", "Musical"));
        Assert.True(RuleMatcher.Matches(rule, items.Last()));
    }

    [Fact]
    public async System.Threading.Tasks.Task TagsThatAreAlsoTitleWordsAreNeverIgnored()
    {
        var items = Library(out var known);
        // A "christmas" tag on titles Wikidata does not list (made-for-TV films) must stay trusted.
        for (var i = 0; i < 12; i++) items.Add(Film($"TV romance {i}", $"tt70000{i:D2}", tags: new[] { "christmas" }));
        WikidataLists.Seed(Q, known, Array.Empty<string>(), Array.Empty<string>());
        Learning.CheckOnline = false;
        var rule = Rule();
        await Learning.AnalyzeAsync(rule, items, null, CancellationToken.None);
        Assert.False(Learning.IsIgnored(rule.Name, "tag", "christmas"));
    }

    [Fact]
    public async System.Threading.Tasks.Task NothingIsLearnedFromAnEmptyList()
    {
        var items = Library(out _);
        WikidataLists.Seed(Q, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var rule = Rule();
        await Learning.AnalyzeAsync(rule, items, null, CancellationToken.None);
        Assert.False(Learning.IsIgnored(rule.Name, "genre", "Musical"));
    }

    [Fact]
    public void ARemovedTitleStillTeachesWhenTheListIsSmall()
    {
        // Direct exclusions always beat learning.
        var m = Film("Cats", "tt1196946", new[] { "Musical" });
        var rule = Rule();
        rule.ExcludeItems = new[] { m.Id.ToString("N") + "|Cats" };
        Assert.False(RuleMatcher.Matches(rule, m));
    }

    [Fact]
    public void AFilmWikidataKnowsIsRuledOutUnlessItsTitleSaysOtherwise()
    {
        var rule = Rule();
        var cats = Film("Cats", "tt5697572", new[] { "Musical" });
        Learning.SetFilm("tt5697572", "Q1", "Q2");
        Assert.True(Learning.RuledOut(rule, cats));
        Assert.False(RuleMatcher.Matches(rule, cats));

        var titled = Film("The Christmas Musical", "tt5697572", new[] { "Musical" });
        Assert.False(Learning.RuledOut(rule, titled));
    }

    [Fact]
    public void AnUnknownFilmOrOneWithoutGenresIsNeverRuledOut()
    {
        var rule = Rule();
        var unknown = Film("Obscure", "tt1111111", new[] { "Musical" });
        Assert.False(Learning.RuledOut(rule, unknown));
        Learning.SetFilm("tt1111111");
        Assert.False(Learning.RuledOut(rule, unknown));
    }

    [Fact]
    public void AFilmWithTheWantedGenreIsNotRuledOut()
    {
        var rule = Rule();
        var elf = Film("Elf", "tt0319343", new[] { "Musical" });
        Learning.SetFilm("tt0319343", "Q1", Q);
        Assert.False(Learning.RuledOut(rule, elf));
    }

    [Fact]
    public void AlwaysIncludeBeatsTheOnlineVerdict()
    {
        var rule = Rule();
        var cats = Film("Cats", "tt5697572", new[] { "Holiday" });
        Learning.SetFilm("tt5697572", "Q1");
        rule.IncludeItems = new[] { cats.Id.ToString("N") + "|Cats" };
        Assert.True(RuleMatcher.Matches(rule, cats));
    }

    [Fact]
    public void VerdictsSayHowSureTheRuleIs()
    {
        WikidataLists.Seed(Q, new[] { "tt0319343" }, Array.Empty<string>(), Array.Empty<string>());
        var rule = Rule();
        Assert.Equal("Confirmed", RuleMatcher.Verdict(rule, Film("Elf", "tt0319343")));
        Assert.Equal("Likely", RuleMatcher.Verdict(rule, Film("Home Alone", "tt0099785", new[] { "Holiday" }, new[] { "christmas" })));
        Assert.Equal("Unsure", RuleMatcher.Verdict(rule, Film("Cats", "tt5697572", new[] { "Musical" })));
        Assert.Null(RuleMatcher.Verdict(new CollectionRule { Genres = new[] { "Comedy" } }, Film("X", "tt1")));
    }
}
