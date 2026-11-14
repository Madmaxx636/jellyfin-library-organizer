using System;
using System.Linq;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Organizer;

public static class RuleMatcher
{
    public static bool Matches(CollectionRule rule, BaseItem item)
    {
        var all = string.Equals(rule.Match, "All", StringComparison.OrdinalIgnoreCase);
        var checks = new System.Collections.Generic.List<bool>();

        Add(checks, rule.Genres, v => item.Genres.Any(g => Eq(g, v)));
        Add(checks, rule.Tags, v => item.Tags.Any(t => Eq(t, v)));
        Add(checks, rule.Countries, v => item.ProductionLocations.Any(c => Eq(c, v)));
        Add(checks, rule.Studios, v => item.Studios.Any(s => Eq(s, v)));
        Add(checks, rule.OfficialRatings, v => Eq(item.OfficialRating, v));
        Add(checks, rule.TitleKeywords, v =>
            (item.Name ?? string.Empty).Contains(v, StringComparison.OrdinalIgnoreCase));

        // Numeric limits always apply on top of the list matching.
        var year = item.ProductionYear;
        if (rule.YearFrom is int from && (year is null || year < from)) return false;
        if (rule.YearTo is int to && (year is null || year > to)) return false;
        if (rule.MinCommunityRating is double min && (item.CommunityRating ?? 0) < min) return false;

        if (checks.Count == 0)
        {
            // Only numeric limits were set; they already passed.
            return rule.YearFrom is not null || rule.YearTo is not null || rule.MinCommunityRating is not null;
        }

        return all ? checks.All(c => c) : checks.Any(c => c);
    }

    private static void Add(System.Collections.Generic.List<bool> checks, string[] values, Func<string, bool> test)
    {
        var vals = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        if (vals.Length == 0) return;
        checks.Add(vals.Any(test));
    }

    private static bool Eq(string? a, string b) =>
        string.Equals(a?.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
