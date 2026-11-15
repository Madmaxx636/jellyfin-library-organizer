using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Organizer;

/// <summary>A tag or genre the plugin stopped counting for a rule, and why.</summary>
public record LearnedSignal(string Rule, string Kind, string Value, int Known, int Total, string Why, bool Trusted);

/// <summary>
/// Keeps rules that use a public list (for example Christmas) honest. Two helpers:
/// 1. A tag or genre that almost never shows up on the public list or in a title (like "Musical" on the Christmas
///    rule) is ignored for that rule, so one stray tag stops pulling in whole genres.
/// 2. A title that matched only on weak signals is looked up on Wikidata by its IMDb id. If Wikidata knows the film,
///    has genres for it, and none is the one the rule wants, it is ruled out.
/// Things you did by hand always win: "always include" beats both, "never include" is applied first.
/// </summary>
public static class Learning
{
    private const int MinKnownForLearning = 20;
    private const int MinTitlesForSignal = 8;
    private static readonly TimeSpan FactsMaxAge = TimeSpan.FromDays(90);

    public sealed class Facts
    {
        public DateTime Checked { get; set; }
        public string[] Genres { get; set; } = Array.Empty<string>();
    }

    public sealed class SignalRecord
    {
        public string Kind { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public int Known { get; set; }
        public int Total { get; set; }
        public string Why { get; set; } = string.Empty;
    }

    public sealed class RuleRecord
    {
        public List<SignalRecord> Ignored { get; set; } = new();
        public List<string> Trusted { get; set; } = new();
    }

    private sealed class FileState
    {
        public Dictionary<string, RuleRecord> Rules { get; set; } = new();
        public Dictionary<string, Facts> Films { get; set; } = new();
    }

    private static readonly ConcurrentDictionary<string, RuleRecord> Rules = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Facts> Films = new(StringComparer.OrdinalIgnoreCase);

    public static string? Directory { get; set; }

    /// <summary>Ignore tags and genres that rarely match the public list. Mirrors the plugin setting.</summary>
    public static bool LearnSignals { get; set; } = true;

    /// <summary>Look up unsure titles on Wikidata. Mirrors the plugin setting.</summary>
    public static bool CheckOnline { get; set; } = true;

    private static string? PathOrNull => Directory is null ? null : Path.Combine(Directory, "learned.json");

    public static void Load()
    {
        try
        {
            var path = PathOrNull;
            if (path is null || !File.Exists(path)) return;
            var state = JsonSerializer.Deserialize<FileState>(File.ReadAllText(path));
            if (state is null) return;
            Rules.Clear();
            Films.Clear();
            foreach (var (k, v) in state.Rules) Rules[k] = v;
            foreach (var (k, v) in state.Films) Films[k] = v;
        }
        catch (Exception)
        {
            // A damaged file only means relearning on the next sync.
        }
    }

    public static void Save()
    {
        try
        {
            var path = PathOrNull;
            if (path is null) return;
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new FileState
            {
                Rules = Rules.ToDictionary(p => p.Key, p => p.Value),
                Films = Films.ToDictionary(p => p.Key, p => p.Value)
            }));
        }
        catch (Exception)
        {
        }
    }

    /// <summary>For tests.</summary>
    public static void Reset()
    {
        Rules.Clear();
        Films.Clear();
        LearnSignals = true;
        CheckOnline = true;
    }

    public static void SetFilm(string imdb, params string[] genreQids) =>
        Films[imdb] = new Facts { Checked = DateTime.UtcNow, Genres = genreQids };

    public static void Ignore(string rule, string kind, string value) =>
        Rules.GetOrAdd(rule, _ => new RuleRecord()).Ignored.Add(new SignalRecord { Kind = kind, Value = value });

    private static bool SameValue(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IsIgnored(string? rule, string kind, string value)
    {
        if (!LearnSignals || string.IsNullOrWhiteSpace(rule) || !Rules.TryGetValue(rule, out var r))
        {
            return false;
        }

        return r.Ignored.Any(s => s.Kind == kind && SameValue(s.Value, value));
    }

    /// <summary>The tags or genres of a rule without the ones that were learned to be noise.</summary>
    public static string[] Filter(CollectionRule rule, string kind, string[] values) =>
        string.IsNullOrWhiteSpace(rule.Name) || values.Length == 0
            ? values
            : values.Where(v => !IsIgnored(rule.Name, kind, v)).ToArray();

    /// <summary>True when Wikidata knows this film, lists genres for it, and none is what the rule wants.</summary>
    public static bool RuledOut(CollectionRule rule, BaseItem item)
    {
        if (!CheckOnline || !WikidataLists.Enabled || rule.PublicGenres.Length == 0
            || !item.ProviderIds.TryGetValue("Imdb", out var imdb) || !Films.TryGetValue(imdb, out var facts)
            || facts.Genres.Length == 0)
        {
            return false;
        }

        var wanted = rule.PublicGenres.Select(WikidataLists.Qid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (facts.Genres.Any(wanted.Contains))
        {
            return false;
        }

        // A title that says it in its name is believed over a database that may be incomplete.
        return !TitleSaysSo(rule, item);
    }

    private static bool TitleSaysSo(CollectionRule rule, BaseItem item) =>
        rule.TitleKeywords.Any(k => !string.IsNullOrWhiteSpace(k)
                                    && (item.Name ?? string.Empty).Contains(k.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool OnList(CollectionRule rule, BaseItem item) =>
        rule.PublicGenres.Any(e => WikidataLists.Contains(WikidataLists.Qid(e), item));

    /// <summary>
    /// Learns which tags and genres are noise for a rule, then looks up the titles that are still unsure.
    /// Safe to call often: lookups are remembered for 90 days. Never throws.
    /// </summary>
    public static async Task AnalyzeAsync(
        CollectionRule rule, IReadOnlyList<BaseItem> library, ILogger? log, CancellationToken ct, bool persist = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rule.Name) || rule.PublicGenres.Length == 0 || !WikidataLists.Enabled)
            {
                return;
            }

            var movies = rule.MediaType is "Movie" or "Both";
            var series = rule.MediaType is "Series" or "Both";
            var pool = library.Where(i => (movies && i is MediaBrowser.Controller.Entities.Movies.Movie)
                                          || (series && i is MediaBrowser.Controller.Entities.TV.Series)).ToList();

            var known = pool.Count(i => OnList(rule, i));
            if (LearnSignals && known >= MinKnownForLearning)
            {
                LearnFromLibrary(rule, pool);
            }

            if (CheckOnline)
            {
                await LookUpUnsure(rule, pool, log, ct).ConfigureAwait(false);
            }

            if (persist)
            {
                Save();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.LogWarning("Smart checks for '{Rule}' were skipped ({Message})", rule.Name, ex.Message);
        }
    }

    private static void LearnFromLibrary(CollectionRule rule, List<BaseItem> pool)
    {
        var record = Rules.GetOrAdd(rule.Name, _ => new RuleRecord());
        var included = RuleMatcher.ItemIds(rule.IncludeItems);
        var excluded = RuleMatcher.ItemIds(rule.ExcludeItems);
        var ignored = new List<SignalRecord>();

        void Check(string kind, string noun, string[] values, Func<BaseItem, string[]> read)
        {
            foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                // Never second-guess a word the rule also searches titles for ("christmas").
                if (rule.TitleKeywords.Any(k => !string.IsNullOrWhiteSpace(k) && value.Contains(k.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (record.Trusted.Any(t => SameValue(t, $"{kind}:{value}")))
                {
                    continue;
                }

                var withValue = pool.Where(i => read(i).Any(v => SameValue(v, value))).ToList();
                var good = withValue.Count(i => OnList(rule, i) || included.Contains(i.Id.ToString("N")) || TitleSaysSo(rule, i));
                var removed = withValue.Count(i => excluded.Contains(i.Id.ToString("N")));
                var total = withValue.Count;
                string? why = null;
                if (total >= MinTitlesForSignal && good * 4 < total)
                {
                    why = $"{good} of the {total} titles with the {noun} \"{value}\" are known to belong, so it is no longer counted.";
                }
                else if (removed >= 3 && removed * 2 >= total)
                {
                    why = $"You removed {removed} of the {total} titles with the {noun} \"{value}\", so it is no longer counted.";
                }

                if (why is not null)
                {
                    ignored.Add(new SignalRecord { Kind = kind, Value = value, Known = good, Total = total, Why = why });
                }
            }
        }

        Check("genre", "genre", rule.Genres, i => i.Genres);
        Check("tag", "tag", rule.Tags, i => i.Tags);
        record.Ignored = ignored;
    }

    private static async Task LookUpUnsure(CollectionRule rule, List<BaseItem> pool, ILogger? log, CancellationToken ct)
    {
        var included = RuleMatcher.ItemIds(rule.IncludeItems);
        var excluded = RuleMatcher.ItemIds(rule.ExcludeItems);
        var unsure = pool
            .Where(i => !OnList(rule, i) && !included.Contains(i.Id.ToString("N")) && !excluded.Contains(i.Id.ToString("N"))
                        && !TitleSaysSo(rule, i) && RuleMatcher.Matches(rule, i))
            .Select(i => i.ProviderIds.TryGetValue("Imdb", out var imdb) ? imdb : null)
            .Where(imdb => !string.IsNullOrWhiteSpace(imdb)
                           && (!Films.TryGetValue(imdb!, out var f) || DateTime.UtcNow - f.Checked > FactsMaxAge))
            .Select(imdb => imdb!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(400)
            .ToList();
        if (unsure.Count == 0)
        {
            return;
        }

        var found = await WikidataLists.LookupGenres(unsure, ct).ConfigureAwait(false);
        foreach (var imdb in unsure)
        {
            // Not in Wikidata at all counts as "no answer" (no genres), never as a reason to leave a title out.
            Films[imdb] = new Facts
            {
                Checked = DateTime.UtcNow,
                Genres = found.TryGetValue(imdb, out var g) ? g : Array.Empty<string>()
            };
        }

        log?.LogInformation("Checked {Count} unsure titles for '{Rule}' on Wikidata", unsure.Count, rule.Name);
    }

    public static List<LearnedSignal> Describe() =>
        Rules.SelectMany(p => p.Value.Ignored.Select(s => new LearnedSignal(p.Key, s.Kind, s.Value, s.Known, s.Total, s.Why, false))
                .Concat(p.Value.Trusted.Select(t =>
                {
                    var at = t.IndexOf(':');
                    return new LearnedSignal(p.Key, at > 0 ? t[..at] : "tag", at > 0 ? t[(at + 1)..] : t, 0, 0, "You chose to keep counting this.", true);
                })))
            .OrderBy(s => s.Rule, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Keep (or stop keeping) counting a tag or genre that was learned to be noise.</summary>
    public static void Trust(string rule, string kind, string value, bool trust)
    {
        var record = Rules.GetOrAdd(rule, _ => new RuleRecord());
        var key = $"{kind}:{value}";
        record.Trusted.RemoveAll(t => SameValue(t, key));
        if (trust)
        {
            record.Trusted.Add(key);
            record.Ignored.RemoveAll(s => s.Kind == kind && SameValue(s.Value, value));
        }

        Save();
    }
}
