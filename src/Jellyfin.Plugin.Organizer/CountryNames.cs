using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Organizer;

/// <summary>Country spellings that mean the same place, and the adjective used in collection names ("Filipino").</summary>
public static class CountryNames
{
    // First entry of each row is the canonical name; the rest are aliases.
    private static readonly string[][] Aliases =
    {
        new[] { "United States of America", "United States", "USA", "US", "U.S.", "U.S.A." },
        new[] { "United Kingdom", "UK", "Great Britain", "Britain", "England", "U.K." },
        new[] { "South Korea", "Korea", "Republic of Korea", "Korea, Republic of", "Korea (South)" },
        new[] { "Russia", "Russian Federation" },
        new[] { "China", "People's Republic of China", "PRC" },
        new[] { "Czech Republic", "Czechia" },
        new[] { "Iran", "Islamic Republic of Iran" },
        new[] { "Philippines", "The Philippines", "Republic of the Philippines" },
    };

    private static readonly Dictionary<string, string> Demonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Philippines"] = "Filipino", ["United Kingdom"] = "British", ["United States of America"] = "American",
        ["Japan"] = "Japanese", ["South Korea"] = "Korean", ["China"] = "Chinese", ["Hong Kong"] = "Hong Kong",
        ["Taiwan"] = "Taiwanese", ["India"] = "Indian", ["France"] = "French", ["Germany"] = "German",
        ["Italy"] = "Italian", ["Spain"] = "Spanish", ["Mexico"] = "Mexican", ["Canada"] = "Canadian",
        ["Australia"] = "Australian", ["Ireland"] = "Irish", ["Russia"] = "Russian", ["Thailand"] = "Thai",
        ["Indonesia"] = "Indonesian", ["Vietnam"] = "Vietnamese", ["Brazil"] = "Brazilian", ["Sweden"] = "Swedish",
        ["Denmark"] = "Danish", ["Norway"] = "Norwegian", ["Finland"] = "Finnish", ["Netherlands"] = "Dutch",
        ["Belgium"] = "Belgian", ["Poland"] = "Polish", ["Turkey"] = "Turkish", ["Iran"] = "Iranian",
        ["Argentina"] = "Argentine", ["Greece"] = "Greek", ["Portugal"] = "Portuguese", ["Austria"] = "Austrian",
        ["Switzerland"] = "Swiss", ["Egypt"] = "Egyptian", ["Israel"] = "Israeli", ["New Zealand"] = "New Zealand",
        ["Malaysia"] = "Malaysian", ["Singapore"] = "Singaporean", ["Nigeria"] = "Nigerian", ["South Africa"] = "South African",
        ["Czech Republic"] = "Czech", ["Hungary"] = "Hungarian", ["Romania"] = "Romanian", ["Ukraine"] = "Ukrainian",
        ["Colombia"] = "Colombian", ["Chile"] = "Chilean", ["Cuba"] = "Cuban", ["Pakistan"] = "Pakistani",
    };

    /// <summary>One spelling per country, so "USA", "United States" and "United States of America" compare equal.</summary>
    public static string Canon(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        foreach (var row in Aliases)
        {
            if (row.Any(a => string.Equals(a, n, StringComparison.OrdinalIgnoreCase)))
            {
                return row[0];
            }
        }

        return n;
    }

    public static bool Same(string? a, string? b) =>
        string.Equals(Canon(a), Canon(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>"Filipino Movies" for known countries, otherwise "Movies from Peru".</summary>
    public static string CollectionName(string country, string noun)
    {
        var canon = Canon(country);
        return Demonyms.TryGetValue(canon, out var adjective)
            ? $"{adjective} {noun}"
            : $"{noun} from {canon}";
    }
}
