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
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Organizer.Tasks;

public class SyncCollectionsTask : IScheduledTask
{
    private readonly ILibraryManager _library;
    private readonly ICollectionManager _collections;
    private readonly ILogger<SyncCollectionsTask> _log;
    private readonly ArtworkService _artwork;

    public SyncCollectionsTask(
        ILibraryManager library,
        ICollectionManager collections,
        IProviderManager providers,
        ILogger<SyncCollectionsTask> log)
    {
        _library = library;
        _collections = collections;
        _log = log;
        _artwork = new ArtworkService(providers, log);
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
        var plugin = Plugin.Instance;
        var config = plugin?.Configuration ?? new PluginConfiguration();

        var manual = config.Rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Name))
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        WikidataLists.Enabled = config.UsePublicDatabase;
        await WikidataLists.EnsureAsync(manual.SelectMany(r => r.PublicGenres), _log, ct).ConfigureAwait(false);

        var started = DateTime.UtcNow;
        var report = new SyncReport { TimeUtc = started };
        var items = LibraryQuery.MoviesAndSeries(_library);
        var existing = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            Recursive = true
        }).OfType<BoxSet>().ToList();

        var auto = LibraryQuery.SelectAutoRules(config, items, existing.Select(b => b.Name).ToList(), manual,
            note => _log.LogInformation("{Note}", note));

        var all = manual.Select(r => (Rule: r, Min: LibraryQuery.MinimumFor(r, config, false)))
            .Concat(auto.Select(r => (Rule: r, Min: LibraryQuery.MinimumFor(r, config, true))))
            .ToList();

        var managed = new List<string>();
        for (var i = 0; i < all.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await ApplyRule(all[i].Rule, all[i].Min, items, existing, config, ct).ConfigureAwait(false);
                if (result.Exists)
                {
                    managed.Add(all[i].Rule.Name);
                }

                switch (result.Action)
                {
                    case "created": report.Created++; report.Lines.Add($"Created {all[i].Rule.Name} ({result.Count} titles)"); break;
                    case "updated":
                        report.Updated++;
                        report.Lines.Add($"Updated {all[i].Rule.Name}: {result.Added} added, {result.Removed} removed");
                        break;
                    case "skipped": report.Skipped++; break;
                    default: report.Unchanged++; break;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Organizer rule '{Rule}' failed", all[i].Rule.Name);
            }

            progress.Report((i + 1) * 100.0 / Math.Max(1, all.Count));
        }

        if (config.DeleteRemovedCollections)
        {
            var current = new HashSet<string>(all.Select(a => a.Rule.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var name in config.ManagedCollectionNames.Where(n => !current.Contains(n)))
            {
                var box = existing.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
                if (box is null) continue;
                _library.DeleteItem(box, new DeleteOptions { DeleteFileLocation = false });
                _log.LogInformation("Deleted collection '{Name}' because its rule is gone", name);
                report.Removed++;
                report.Lines.Add($"Deleted {name} (its rule is gone)");
            }
        }

        // One time: earlier versions made a collection for every country ("Movies from Peru"). Only the countries
        // you list in settings get one now, so remove the ones this plugin made that no rule asks for any more.
        var deletedLegacy = new List<string>();
        if (!config.LegacyCountryCleanupDone)
        {
            var wanted = new HashSet<string>(all.Select(a => a.Rule.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var name in config.ManagedCollectionNames.Where(n =>
                         System.Text.RegularExpressions.Regex.IsMatch(n, "^(Movies|Series|Titles) from ") && !wanted.Contains(n)))
            {
                var box = existing.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
                if (box is not null)
                {
                    _library.DeleteItem(box, new DeleteOptions { DeleteFileLocation = false });
                    _log.LogInformation("Removed old per-country collection '{Name}'; only countries you list in settings get one now", name);
                    report.Removed++;
                    report.Lines.Add($"Removed old {name}");
                }

                deletedLegacy.Add(name);
            }

            config.LegacyCountryCleanupDone = true;
        }

        // Keep names of removed rules while deletion is off, so turning it on later still cleans up.
        var keep = config.DeleteRemovedCollections
            ? new List<string>()
            : config.ManagedCollectionNames.Where(n => !managed.Contains(n, StringComparer.OrdinalIgnoreCase) && !deletedLegacy.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        config.ManagedCollectionNames = managed.Concat(keep).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        config.AutoRules = auto;
        plugin?.SaveConfiguration();

        HomeSections.Register(config, _log);
        report.Seconds = Math.Round((DateTime.UtcNow - started).TotalSeconds, 1);
        report.Save();
        progress.Report(100);
    }

    private readonly record struct RuleResult(bool Exists, string Action, int Count, int Added, int Removed);

    private async Task<RuleResult> ApplyRule(
        CollectionRule rule,
        int minItems,
        IReadOnlyList<BaseItem> allItems,
        List<BoxSet> existing,
        PluginConfiguration config,
        CancellationToken ct)
    {
        var matched = LibraryQuery.Match(rule, allItems);
        var box = existing.FirstOrDefault(b =>
            string.Equals(b.Name, rule.Name, StringComparison.OrdinalIgnoreCase));

        if (matched.Count < Math.Max(1, minItems))
        {
            _log.LogInformation("Rule '{Rule}': {Count} matches, below minimum {Min}, skipped", rule.Name, matched.Count, minItems);
            return new RuleResult(box is not null, "skipped", matched.Count, 0, 0);
        }

        var action = "unchanged";
        int added = 0, removed = 0;

        if (box is null)
        {
            box = await _collections.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = rule.Name,
                ItemIdList = matched.Select(m => m.Id.ToString("N")).ToArray(),
                IsLocked = false
            }).ConfigureAwait(false);
            existing.Add(box);
            action = "created";
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

            // Titles you excluded by hand always come out, even when "remove items that stop matching" is off.
            var forced = RuleMatcher.ItemIds(rule.ExcludeItems);
            var toRemove = config.RemoveNonMatching
                ? current.Except(wanted).ToList()
                : current.Where(id => forced.Contains(id.ToString("N"))).ToList();
            _log.LogInformation(
                "Rule '{Rule}': {Current} in collection, {Wanted} wanted, adding {Add}, removing {Remove}",
                rule.Name, current.Count, wanted.Count, toAdd.Count, toRemove.Count);
            if (toRemove.Count > 0)
            {
                await _collections.RemoveFromCollectionAsync(box.Id, toRemove).ConfigureAwait(false);
            }

            added = toAdd.Count;
            removed = toRemove.Count;
            if (added + removed > 0)
            {
                action = "updated";
            }
        }

        // Adding or removing items changes the stored collection, so work on a fresh copy. Saving the older
        // in-memory one would write the previous member list back and undo the change.
        box = _library.GetItemById(box.Id) as BoxSet ?? box;

        // Where the collection sits among the others, and the order of the titles inside it.
        box.ForcedSortName = ItemSorter.CollectionSortName(rule, matched, config.CollectionOrder);
        var native = ItemSorter.NativeDisplayOrder(rule.ItemOrder, rule.ItemDescending);
        if (native is not null)
        {
            box.DisplayOrder = native;
        }
        else
        {
            // Jellyfin can only sort a collection by a few keys, ascending. For the rest (story order, rating,
            // newest first...) it keeps the order the collection was stored in, so store it in the order we want.
            box.DisplayOrder = "Default";
            var ordered = ItemSorter.Order(matched, rule.ItemOrder, rule.ItemDescending, rule.StoryOrder);
            var inBox = box.GetLinkedChildren().GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            var arranged = ordered.Where(o => inBox.ContainsKey(o.Id)).Select(o => inBox[o.Id]).ToList();
            var arrangedIds = arranged.Select(c => c.Id).ToHashSet();
            // Titles that are in the collection but no longer match (when "take titles out" is off) stay, at the end.
            arranged.AddRange(inBox.Values.Where(c => !arrangedIds.Contains(c.Id)));
            box.LinkedChildren = arranged.Select(LinkedChild.Create).ToArray();
        }

        await box.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);

        try
        {
            box = _library.GetItemById(box.Id) as BoxSet ?? box;
            await _artwork.Apply(box, rule, matched, config, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Artwork is a nicety; never let it stop the collection itself.
            _log.LogWarning(ex, "Could not make artwork for '{Rule}'", rule.Name);
        }

        return new RuleResult(true, action, matched.Count, added, removed);
    }
}
