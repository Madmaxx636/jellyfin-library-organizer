using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Organizer.Tasks;

public class SyncCollectionsTask : IScheduledTask
{
    private readonly ILibraryManager _library;
    private readonly ICollectionManager _collections;
    private readonly ILogger<SyncCollectionsTask> _log;

    public SyncCollectionsTask(
        ILibraryManager library,
        ICollectionManager collections,
        ILogger<SyncCollectionsTask> log)
    {
        _library = library;
        _collections = collections;
        _log = log;
    }

    public string Name => "Sync organizer collections";
    public string Key => "OrganizerSyncCollections";
    public string Description => "Creates, fills and orders collections from the Library Organizer rules.";
    public string Category => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var rules = config.Rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Name)).ToList();
        if (rules.Count == 0)
        {
            progress.Report(100);
            return;
        }

        var items = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true,
            IsVirtualItem = false
        });

        var existing = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            Recursive = true
        }).OfType<BoxSet>().ToList();

        for (var i = 0; i < rules.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await ApplyRule(rules[i], items, existing, config, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Organizer rule '{Rule}' failed", rules[i].Name);
            }

            progress.Report((i + 1) * 100.0 / rules.Count);
        }

        HomeSections.Register(config, _log);
    }

    private async Task ApplyRule(
        CollectionRule rule,
        IReadOnlyList<BaseItem> allItems,
        List<BoxSet> existing,
        PluginConfiguration config,
        CancellationToken ct)
    {
        var wantMovies = rule.MediaType is "Movie" or "Both";
        var wantSeries = rule.MediaType is "Series" or "Both";

        var matched = allItems
            .Where(i => (wantMovies && i is Movie) || (wantSeries && i is Series))
            .Where(i => RuleMatcher.Matches(rule, i))
            .ToList();

        var box = existing.FirstOrDefault(b =>
            string.Equals(b.Name, rule.Name, StringComparison.OrdinalIgnoreCase));

        if (matched.Count < Math.Max(1, config.MinItems))
        {
            _log.LogInformation("Rule '{Rule}': {Count} matches, below minimum, skipped", rule.Name, matched.Count);
            return;
        }

        if (box is null)
        {
            box = await _collections.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = rule.Name,
                ItemIdList = matched.Select(m => m.Id.ToString("N")).ToArray(),
                IsLocked = false
            }).ConfigureAwait(false);
            existing.Add(box);
            _log.LogInformation("Created collection '{Rule}' with {Count} items", rule.Name, matched.Count);
        }
        else
        {
            var current = box.GetLinkedChildren().Select(c => c.Id).ToHashSet();
            var wanted = matched.Select(m => m.Id).ToHashSet();

            var toAdd = wanted.Except(current).ToList();
            if (toAdd.Count > 0)
            {
                await _collections.AddToCollectionAsync(box.Id, toAdd).ConfigureAwait(false);
            }

            if (config.RemoveNonMatching)
            {
                var toRemove = current.Except(wanted).ToList();
                if (toRemove.Count > 0)
                {
                    await _collections.RemoveFromCollectionAsync(box.Id, toRemove).ConfigureAwait(false);
                }
            }
        }

        // Order of items inside the collection.
        box.DisplayOrder = rule.ItemOrder switch
        {
            "SortName" => "SortName",
            _ => "PremiereDate"
        };

        // Order of the collection among other collections: Jellyfin sorts by SortName,
        // so a numeric prefix gives a stable, user-controlled order.
        box.ForcedSortName = rule.Position > 0
            ? $"{rule.Position:D3} {rule.Name}"
            : string.Empty;

        await box.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
    }
}
