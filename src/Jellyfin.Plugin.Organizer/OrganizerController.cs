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

public record PreviewItem(string Id, string Name, int? Year, List<string> Why);

public record PreviewResult(int Count, List<PreviewItem> Items);

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
        var matched = LibraryQuery.Match(rule, LibraryQuery.MoviesAndSeries(_library));
        var items = matched
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .Select(i => new PreviewItem(i.Id.ToString("N"), i.Name, i.ProductionYear, RuleMatcher.Explain(rule, i)))
            .ToList();
        return new PreviewResult(matched.Count, items);
    }
}
