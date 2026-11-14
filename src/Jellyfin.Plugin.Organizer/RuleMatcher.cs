using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Organizer;

public static class RuleMatcher
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string[], HashSet<string>> IdCache = new();

    /// <summary>Item ids from entries like "id|Name (year)", in 32-character form.</summary>
    public static HashSet<string> ItemIds(string[]? entries)
    {
        if (entries is null || entries.Length == 0) return new HashSet<string>();
        return IdCache.GetValue(entries, e =>
        {
            var set = new HashSet<string>();
            foreach (var entry in e)
            {
                var id = (entry ?? string.Empty).Split('|')[0].Trim();
                if (Guid.TryParse(id, out var g)) set.Add(g.ToString("N"));
            }

            return set;
        });
    }

    public static bool Matches(CollectionRule rule, BaseItem item)
    {
        var itemId = item.Id.ToString("N");
        if (ItemIds(rule.ExcludeItems).Contains(itemId)) return false;
        if (ItemIds(rule.IncludeItems).Contains(itemId)) return true;

        if (AnyEq(rule.ExcludeGenres, item.Genres)
            || AnyEq(rule.ExcludeTags, item.Tags)
            || AnyEq(rule.ExcludeCountries, item.ProductionLocations)
            || Clean(rule.ExcludeKeywords).Any(k => Contains(item.Name, k)))
        {
            return false;
        }

        var year = item.ProductionYear;
        if (rule.YearFrom is int from && (year is null || year < from)) return false;
        if (rule.YearTo is int to && (year is null || year > to)) return false;
        if (rule.MinCommunityRating is double min && (item.CommunityRating ?? 0) < min) return false;

        var pub = Clean(rule.PublicGenres);
        var publicHit = pub.Any(e => WikidataLists.Contains(WikidataLists.Qid(e), item));
        var all = string.Equals(rule.Match, "All", StringComparison.OrdinalIgnoreCase);
        if (publicHit && !all)
        {
            return true;
        }

        var checks = new List<bool>();
        if (pub.Length > 0 && all)
        {
            checks.Add(publicHit);
        }

        AddCheck(checks, rule.Genres, v => item.Genres.Any(g => Eq(g, v)));
        AddCheck(checks, rule.Tags, v => item.Tags.Any(t => Eq(t, v)));
        AddCheck(checks, rule.Countries, v => item.ProductionLocations.Any(c => Eq(c, v)));
        AddCheck(checks, rule.Studios, v => item.Studios.Any(s => Eq(s, v)));
        AddCheck(checks, rule.OfficialRatings, v => Eq(item.OfficialRating, v));
        AddCheck(checks, rule.TitleKeywords, v => Contains(item.Name, v));
        if (Clean(rule.Languages).Length > 0)
        {
            checks.Add(Languages.AnyMatch(AudioLanguages(item), rule.Languages));
        }

        if (checks.Count == 0)
        {
            if (pub.Length > 0)
            {
                return false;
            }

            // Only limits or exclusions were set; they already passed.
            return rule.YearFrom is not null || rule.YearTo is not null || rule.MinCommunityRating is not null
                   || Clean(rule.ExcludeGenres).Length + Clean(rule.ExcludeTags).Length
                   + Clean(rule.ExcludeCountries).Length + Clean(rule.ExcludeKeywords).Length > 0;
        }

        var hits = checks.Count(c => c);
        var needed = all
            ? checks.Count
            : Math.Clamp(rule.MinFieldMatches, 1, checks.Count);
        return hits >= needed;
    }

    /// <summary>Plain-language reasons an item matched, for the preview.</summary>
    public static List<string> Explain(CollectionRule rule, BaseItem item)
    {
        var why = new List<string>();
        var id = item.Id.ToString("N");
        if (ItemIds(rule.IncludeItems).Contains(id)) why.Add("always included by you");

        void Hits(string label, string[] wanted, Func<string, bool> test)
        {
            foreach (var w in Clean(wanted).Where(test)) why.Add($"{label}: {w}");
        }

        foreach (var e in Clean(rule.PublicGenres).Where(e => WikidataLists.Contains(WikidataLists.Qid(e), item)))
        {
            why.Add($"public database: {WikidataLists.Label(e)}");
        }

        Hits("genre", rule.Genres, v => item.Genres.Any(g => Eq(g, v)));
        Hits("tag", rule.Tags, v => item.Tags.Any(t => Eq(t, v)));
        Hits("country", rule.Countries, v => item.ProductionLocations.Any(c => Eq(c, v)));
        Hits("studio", rule.Studios, v => item.Studios.Any(s => Eq(s, v)));
        Hits("rating", rule.OfficialRatings, v => Eq(item.OfficialRating, v));
        Hits("title word", rule.TitleKeywords, v => Contains(item.Name, v));
        if (Clean(rule.Languages).Length > 0 && Languages.AnyMatch(AudioLanguages(item), rule.Languages))
        {
            why.Add("audio language");
        }

        return why;
    }

    private static IEnumerable<string?> AudioLanguages(BaseItem item)
    {
        try
        {
            return item.GetMediaStreams()
                .Where(s => s.Type == MediaStreamType.Audio)
                .Select(s => s.Language)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string?>();
        }
    }

    private static string[] Clean(string[] values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();

    private static void AddCheck(List<bool> checks, string[] values, Func<string, bool> test)
    {
        var vals = Clean(values);
        if (vals.Length > 0) checks.Add(vals.Any(test));
    }

    private static bool AnyEq(string[] wanted, IEnumerable<string> have)
    {
        var w = Clean(wanted);
        return w.Length > 0 && have.Any(h => w.Any(x => Eq(h, x)));
    }

    private static bool Contains(string? name, string keyword) =>
        (name ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static bool Eq(string? a, string b) =>
        string.Equals(a?.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
