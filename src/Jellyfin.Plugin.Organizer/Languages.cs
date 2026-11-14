using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Organizer;

/// <summary>Maps language names and ISO codes onto one key so "Korean", "ko", "kor" all compare equal.</summary>
public static class Languages
{
    // name, two-letter, ISO 639-2/T, ISO 639-2/B (empty when the same), extra aliases
    private static readonly string[][] Table =
    {
        new[] { "english", "en", "eng", "" },
        new[] { "filipino", "fil", "tgl", "", "tagalog" },
        new[] { "korean", "ko", "kor", "" },
        new[] { "japanese", "ja", "jpn", "" },
        new[] { "chinese", "zh", "zho", "chi", "mandarin", "cmn", "cantonese", "yue" },
        new[] { "spanish", "es", "spa", "" },
        new[] { "french", "fr", "fra", "fre" },
        new[] { "german", "de", "deu", "ger" },
        new[] { "italian", "it", "ita", "" },
        new[] { "portuguese", "pt", "por", "" },
        new[] { "russian", "ru", "rus", "" },
        new[] { "hindi", "hi", "hin", "" },
        new[] { "arabic", "ar", "ara", "" },
        new[] { "thai", "th", "tha", "" },
        new[] { "vietnamese", "vi", "vie", "" },
        new[] { "indonesian", "id", "ind", "" },
        new[] { "malay", "ms", "msa", "may" },
        new[] { "turkish", "tr", "tur", "" },
        new[] { "dutch", "nl", "nld", "dut" },
        new[] { "swedish", "sv", "swe", "" },
        new[] { "danish", "da", "dan", "" },
        new[] { "norwegian", "no", "nor", "" },
        new[] { "finnish", "fi", "fin", "" },
        new[] { "polish", "pl", "pol", "" },
        new[] { "czech", "cs", "ces", "cze" },
        new[] { "greek", "el", "ell", "gre" },
        new[] { "hebrew", "he", "heb", "" },
        new[] { "persian", "fa", "fas", "per" },
        new[] { "tamil", "ta", "tam", "" },
        new[] { "telugu", "te", "tel", "" },
        new[] { "bengali", "bn", "ben", "" },
        new[] { "urdu", "ur", "urd", "" },
        new[] { "ukrainian", "uk", "ukr", "" },
        new[] { "hungarian", "hu", "hun", "" },
        new[] { "romanian", "ro", "ron", "rum" },
    };

    private static readonly Dictionary<string, string> Lookup = Build();

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Table)
        {
            var key = row[0];
            foreach (var alias in row.Where(a => !string.IsNullOrEmpty(a)))
            {
                map[alias] = key;
            }
        }

        return map;
    }

    public static string Canon(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        return Lookup.TryGetValue(v, out var key) ? key : v.ToLowerInvariant();
    }

    public static bool AnyMatch(IEnumerable<string?> streamLanguages, IEnumerable<string> wanted)
    {
        var have = new HashSet<string>(
            streamLanguages.Where(l => !string.IsNullOrWhiteSpace(l)).Select(Canon));
        return wanted.Where(w => !string.IsNullOrWhiteSpace(w)).Any(w => have.Contains(Canon(w)));
    }
}
