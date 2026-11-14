using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Organizer;

public static class RuleMatcher
{
    public static bool Matches(CollectionRule rule, BaseItem item)
    {
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

        var checks = new List<bool>();
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
            // Only limits or exclusions were set; they already passed.
            return rule.YearFrom is not null || rule.YearTo is not null || rule.MinCommunityRating is not null
                   || Clean(rule.ExcludeGenres).Length + Clean(rule.ExcludeTags).Length
                   + Clean(rule.ExcludeCountries).Length + Clean(rule.ExcludeKeywords).Length > 0;
        }

        return string.Equals(rule.Match, "All", StringComparison.OrdinalIgnoreCase)
            ? checks.All(c => c)
            : checks.Any(c => c);
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
