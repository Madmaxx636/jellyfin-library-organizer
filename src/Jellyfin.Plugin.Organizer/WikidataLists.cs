using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Organizer;

public record WikidataHit(string Id, string Label, string? Description);

/// <summary>
/// Curated film lists from Wikidata (for example every film with the genre "Christmas film"),
/// matched to library items by IMDb or TMDB id. Only the genre id is ever sent; nothing about your library.
/// </summary>
public static class WikidataLists
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly Regex QidPattern = new(@"^Q\d+$", RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<string, Entry> Cache = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly HttpClient Http = CreateClient();

    private sealed record Entry(HashSet<string> Imdb, HashSet<string> TmdbMovie, HashSet<string> TmdbTv, DateTime FetchedAt);

    private sealed class FileEntry
    {
        public DateTime FetchedAt { get; set; }
        public string[] Imdb { get; set; } = Array.Empty<string>();
        public string[] TmdbMovie { get; set; } = Array.Empty<string>();
        public string[] TmdbTv { get; set; } = Array.Empty<string>();
    }

    public static string? CacheDirectory { get; set; }

    /// <summary>Master switch, mirrored from the plugin settings.</summary>
    public static bool Enabled { get; set; } = true;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LibraryOrganizerJellyfinPlugin/0.5.1 (open-source Jellyfin plugin)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/sparql-results+json, application/json");
        return client;
    }

    public static string Qid(string entry) => (entry ?? string.Empty).Split('|')[0].Trim();

    public static string Label(string entry)
    {
        var parts = (entry ?? string.Empty).Split('|');
        return parts.Length > 1 ? string.Join('|', parts.Skip(1)) : parts[0];
    }

    public static bool IsValidQid(string qid) => QidPattern.IsMatch(qid);

    /// <summary>For tests: put a list in the cache without touching the network.</summary>
    public static void Seed(string qid, IEnumerable<string> imdb, IEnumerable<string> tmdbMovie, IEnumerable<string> tmdbTv) =>
        Cache[qid] = new Entry(imdb.ToHashSet(StringComparer.OrdinalIgnoreCase), tmdbMovie.ToHashSet(), tmdbTv.ToHashSet(), DateTime.UtcNow);

    public static void Clear() => Cache.Clear();

    public static bool Contains(string qid, BaseItem item)
    {
        if (!Enabled || !Cache.TryGetValue(qid, out var e))
        {
            return false;
        }

        if (item.ProviderIds.TryGetValue("Imdb", out var imdb) && e.Imdb.Contains(imdb))
        {
            return true;
        }

        if (item.ProviderIds.TryGetValue("Tmdb", out var tmdb))
        {
            if (item is Movie && e.TmdbMovie.Contains(tmdb)) return true;
            if (item is Series && e.TmdbTv.Contains(tmdb)) return true;
        }

        return false;
    }

    /// <summary>Loads (from disk or the internet) every list the given entries refer to. Never throws.</summary>
    public static async Task EnsureAsync(IEnumerable<string> entries, ILogger? log, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return;
        }

        var wanted = entries.Select(Qid).Where(IsValidQid).Distinct().ToList();
        foreach (var qid in wanted)
        {
            if (Cache.TryGetValue(qid, out var cached) && DateTime.UtcNow - cached.FetchedAt < MaxAge)
            {
                continue;
            }

            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Cache.TryGetValue(qid, out cached) && DateTime.UtcNow - cached.FetchedAt < MaxAge)
                {
                    continue;
                }

                var fromDisk = ReadFile(qid);
                if (fromDisk is not null && DateTime.UtcNow - fromDisk.FetchedAt < MaxAge)
                {
                    Cache[qid] = fromDisk;
                    continue;
                }

                try
                {
                    var fresh = await Fetch(qid, ct).ConfigureAwait(false);
                    Cache[qid] = fresh;
                    WriteFile(qid, fresh);
                    log?.LogInformation("Loaded public list {Qid}: {Imdb} IMDb ids", qid, fresh.Imdb.Count);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Offline or Wikidata is busy: keep whatever we had, even if stale.
                    if (fromDisk is not null && !Cache.ContainsKey(qid))
                    {
                        Cache[qid] = fromDisk;
                    }

                    log?.LogWarning("Could not refresh public list {Qid} ({Message}); using the last saved copy if any", qid, ex.Message);
                }
            }
            finally
            {
                Gate.Release();
            }
        }
    }

    private static async Task<Entry> Fetch(string qid, CancellationToken ct)
    {
        var query = $"SELECT ?imdb ?tmdb ?tmdbtv WHERE {{ ?f wdt:P136 wd:{qid} . OPTIONAL {{ ?f wdt:P345 ?imdb }} OPTIONAL {{ ?f wdt:P4947 ?tmdb }} OPTIONAL {{ ?f wdt:P4983 ?tmdbtv }} }}";
        var url = "https://query.wikidata.org/sparql?format=json&query=" + Uri.EscapeDataString(query);
        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var imdb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tmdb = new HashSet<string>();
        var tmdbTv = new HashSet<string>();
        foreach (var row in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            if (row.TryGetProperty("imdb", out var i)) imdb.Add(i.GetProperty("value").GetString() ?? string.Empty);
            if (row.TryGetProperty("tmdb", out var t)) tmdb.Add(t.GetProperty("value").GetString() ?? string.Empty);
            if (row.TryGetProperty("tmdbtv", out var tv)) tmdbTv.Add(tv.GetProperty("value").GetString() ?? string.Empty);
        }

        imdb.Remove(string.Empty);
        tmdb.Remove(string.Empty);
        tmdbTv.Remove(string.Empty);
        return new Entry(imdb, tmdb, tmdbTv, DateTime.UtcNow);
    }

    /// <summary>Lets the settings page look up a genre by name.</summary>
    public static async Task<List<WikidataHit>> Search(string text, CancellationToken ct)
    {
        var url = "https://www.wikidata.org/w/api.php?action=wbsearchentities&language=en&type=item&limit=8&format=json&search="
                  + Uri.EscapeDataString(text);
        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var hits = new List<WikidataHit>();
        if (doc.RootElement.TryGetProperty("search", out var arr))
        {
            foreach (var e in arr.EnumerateArray())
            {
                hits.Add(new WikidataHit(
                    e.GetProperty("id").GetString() ?? string.Empty,
                    e.TryGetProperty("label", out var l) ? l.GetString() ?? string.Empty : string.Empty,
                    e.TryGetProperty("description", out var d) ? d.GetString() : null));
            }
        }

        return hits;
    }

    private static string? PathFor(string qid) =>
        CacheDirectory is null ? null : Path.Combine(CacheDirectory, $"wikidata-{qid}.json");

    private static Entry? ReadFile(string qid)
    {
        try
        {
            var path = PathFor(qid);
            if (path is null || !File.Exists(path)) return null;
            var f = JsonSerializer.Deserialize<FileEntry>(File.ReadAllText(path));
            return f is null ? null : new Entry(
                f.Imdb.ToHashSet(StringComparer.OrdinalIgnoreCase), f.TmdbMovie.ToHashSet(), f.TmdbTv.ToHashSet(), f.FetchedAt);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void WriteFile(string qid, Entry e)
    {
        try
        {
            var path = PathFor(qid);
            if (path is null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new FileEntry
            {
                FetchedAt = e.FetchedAt,
                Imdb = e.Imdb.ToArray(),
                TmdbMovie = e.TmdbMovie.ToArray(),
                TmdbTv = e.TmdbTv.ToArray()
            }));
        }
        catch (Exception)
        {
            // A missing disk cache only means a re-download next time.
        }
    }
}
