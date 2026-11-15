using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Organizer;

public record ValueCount(string Name, int Count);

public record LibraryValues(
    List<ValueCount> Genres,
    List<ValueCount> Tags,
    List<ValueCount> Countries,
    List<ValueCount> Studios,
    List<ValueCount> Ratings,
    List<ValueCount> Franchises);

public record PreviewItem(string Id, string Name, int? Year, List<string> Why, string? Verdict);

public record PreviewResult(int Count, List<PreviewItem> Items, bool HasBuiltInStory);

public record RuledOutTitle(string Rule, string Id, string Name, int? Year);

public record LearnedResult(List<LearnedSignal> Signals, List<RuledOutTitle> RuledOut);

public record TrustRequest(string Rule, string Kind, string Value, bool Trust);

public record StatusResult(SyncReport? Last, int Collections, string Version, List<string> StoryNames);

[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("LibraryOrganizer")]
public class OrganizerController : ControllerBase
{
    private readonly ILibraryManager _library;

    public OrganizerController(ILibraryManager library) => _library = library;

    /// <summary>What genres, tags, countries and so on exist in the library, with counts.</summary>
    [HttpGet("Values")]
    public ActionResult<LibraryValues> GetValues()
    {
        var items = LibraryQuery.MoviesAndSeries(_library);

        static List<ValueCount> Count(IEnumerable<string?> values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v))
                .GroupBy(v => v!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new ValueCount(g.Key, g.Count()))
                .OrderByDescending(v => v.Count).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        return new LibraryValues(
            Count(items.SelectMany(i => i.Genres)),
            Count(items.SelectMany(i => i.Tags)),
            Count(items.SelectMany(i => i.ProductionLocations)),
            Count(items.SelectMany(i => i.Studios)),
            Count(items.Select(i => i.OfficialRating)),
            Count(items.OfType<MediaBrowser.Controller.Entities.Movies.Movie>().Select(m => m.CollectionName)));
    }

    /// <summary>Looks up genres on Wikidata by name for the settings page.</summary>
    [HttpGet("WikidataSearch")]
    public async Task<ActionResult<List<WikidataHit>>> WikidataSearch([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || !WikidataLists.Enabled)
        {
            return new List<WikidataHit>();
        }

        try
        {
            return await WikidataLists.Search(q.Trim(), ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return StatusCode(502, "Could not reach Wikidata");
        }
    }

    /// <summary>Shows what a rule would match right now, without changing anything.</summary>
    [HttpPost("Preview")]
    public async Task<ActionResult<PreviewResult>> Preview([FromBody] CollectionRule rule, CancellationToken ct)
    {
        await WikidataLists.EnsureAsync(rule.PublicGenres, null, ct).ConfigureAwait(false);
        var all = LibraryQuery.MoviesAndSeries(_library);
        await Learning.AnalyzeAsync(rule, all, null, ct).ConfigureAwait(false);
        var matched = LibraryQuery.Match(rule, all);

        // Shown in the order the collection will have, so this doubles as a check of the sort settings.
        var items = ItemSorter.Order(matched, rule.ItemOrder, rule.ItemDescending, rule.StoryOrder)
            .Take(500)
            .Select(i => new PreviewItem(i.Id.ToString("N"), i.Name, i.ProductionYear, RuleMatcher.Explain(rule, i), RuleMatcher.Verdict(rule, i)))
            .ToList();
        return new PreviewResult(matched.Count, items, ItemSorter.HasBuiltInStory(matched));
    }

    /// <summary>What a sync would do with the saved settings, without changing anything.</summary>
    [HttpGet("Plan")]
    public async Task<ActionResult<List<PlannedChange>>> Plan(CancellationToken ct)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var manual = config.Rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Name))
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        await WikidataLists.EnsureAsync(manual.SelectMany(r => r.PublicGenres), null, ct).ConfigureAwait(false);

        var items = LibraryQuery.MoviesAndSeries(_library);
        foreach (var r in manual.Where(r => r.PublicGenres.Length > 0))
        {
            await Learning.AnalyzeAsync(r, items, null, ct).ConfigureAwait(false);
        }

        var existing = _library.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
        {
            IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.BoxSet },
            Recursive = true
        }).OfType<MediaBrowser.Controller.Entities.Movies.BoxSet>().ToList();

        var auto = LibraryQuery.SelectAutoRules(config, items, existing.Select(b => b.Name).ToList(), manual);
        var plan = new List<PlannedChange>();
        foreach (var (rule, isAuto) in manual.Select(r => (r, false)).Concat(auto.Select(r => (r, true))))
        {
            var matched = LibraryQuery.Match(rule, items);
            var min = LibraryQuery.MinimumFor(rule, config, isAuto);
            var kind = !isAuto ? "Your rule" : rule.Franchises.Length > 0 ? "Franchise" : rule.Countries.Length > 0 ? "Country" : "Genre";
            var box = existing.FirstOrDefault(b => string.Equals(b.Name, rule.Name, StringComparison.OrdinalIgnoreCase));
            if (matched.Count < Math.Max(1, min))
            {
                plan.Add(new PlannedChange(rule.Name, kind, "Skip", matched.Count, 0, 0, $"{matched.Count} match, needs {min}"));
                continue;
            }

            if (box is null)
            {
                plan.Add(new PlannedChange(rule.Name, kind, "Create", matched.Count, matched.Count, 0, string.Empty));
                continue;
            }

            var current = box.GetLinkedChildren().Select(c => c.Id).ToHashSet();
            var wanted = matched.Select(m => m.Id).ToHashSet();
            var add = wanted.Except(current).Count();
            var forced = RuleMatcher.ItemIds(rule.ExcludeItems);
            var remove = config.RemoveNonMatching ? current.Except(wanted).Count() : current.Count(id => forced.Contains(id.ToString("N")));
            plan.Add(new PlannedChange(rule.Name, kind, add + remove > 0 ? "Update" : "No change", matched.Count, add, remove, string.Empty));
        }

        return plan;
    }

    /// <summary>What the plugin has learned: ignored tags and genres, and titles ruled out online.</summary>
    [HttpGet("Learned")]
    public ActionResult<LearnedResult> Learned()
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var items = LibraryQuery.MoviesAndSeries(_library);
        var ruledOut = new List<RuledOutTitle>();
        foreach (var rule in config.Rules.Where(r => r.Enabled && r.PublicGenres.Length > 0))
        {
            ruledOut.AddRange(items.Where(i => Learning.RuledOut(rule, i))
                .Select(i => new RuledOutTitle(rule.Name, i.Id.ToString("N"), i.Name, i.ProductionYear)));
        }

        return new LearnedResult(Learning.Describe(), ruledOut);
    }

    /// <summary>Keep counting a tag or genre that was learned to be noise, or let it be learned again.</summary>
    [HttpPost("Learned/Trust")]
    public ActionResult Trust([FromBody] TrustRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Rule) || string.IsNullOrWhiteSpace(request.Value))
        {
            return BadRequest();
        }

        Learning.Trust(request.Rule, request.Kind == "genre" ? "genre" : "tag", request.Value, request.Trust);
        return NoContent();
    }

    /// <summary>When the last sync ran and what it did.</summary>
    [HttpGet("Status")]
    public ActionResult<StatusResult> Status()
    {
        var boxes = _library.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
        {
            IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.BoxSet },
            Recursive = true
        }).Count;
        return new StatusResult(SyncReport.Load(), boxes, typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? string.Empty,
            ItemSorter.BuiltInStoryNames.ToList());
    }
}
