using System;
using System.Collections.Generic;
using System.Linq;
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
    List<ValueCount> Ratings);

public record PreviewResult(int Count, List<string> Items);

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
            Count(items.Select(i => i.OfficialRating)));
    }

    /// <summary>Shows what a rule would match right now, without changing anything.</summary>
    [HttpPost("Preview")]
    public ActionResult<PreviewResult> Preview([FromBody] CollectionRule rule)
    {
        var matched = LibraryQuery.Match(rule, LibraryQuery.MoviesAndSeries(_library));
        var names = matched
            .Select(i => i.ProductionYear is int y ? $"{i.Name} ({y})" : i.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();
        return new PreviewResult(matched.Count, names);
    }
}
