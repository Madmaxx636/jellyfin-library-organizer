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

        var items = LibraryQuery.MoviesAndSeries(_library);
        var taken = new HashSet<string>(config.Rules.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        var existing = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            Recursive = true
        }).OfType<BoxSet>().ToList();

        var auto = LibraryQuery.BuildAutoRules(config, items, taken);

        // An automatic collection with exactly the same items as one of your rules would just be a duplicate.
        var manualSets = manual.Select(r => LibraryQuery.Match(r, items).Select(i => i.Id).ToHashSet()).ToList();
        auto = auto.Where(a =>
        {
            // Jellyfin's own "automatically add to collection" option may already have made this franchise.
            var madeByJellyfin = a.Franchises.Length > 0
                && existing.Any(b => a.Franchises.Any(f => string.Equals(b.Name, f, StringComparison.OrdinalIgnoreCase)));
            if (madeByJellyfin)
            {
                _log.LogInformation("Franchise '{Name}' already has a Jellyfin-made collection; skipping", a.Name);
                return false;
            }

            var ids = LibraryQuery.Match(a, items).Select(i => i.Id).ToHashSet();
            return !manualSets.Any(m => m.SetEquals(ids));
        }).ToList();

        var all = manual.Select(r => (Rule: r, Min: config.MinItems))
            .Concat(auto.Select(r => (Rule: r, Min: r.Franchises.Length > 0 ? Math.Max(2, config.FranchiseMinItems) : r.Countries.Length > 0 ? config.MinItems : config.AutoMinItems)))
            .ToList();

        var managed = new List<string>();
        for (var i = 0; i < all.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await ApplyRule(all[i].Rule, all[i].Min, items, existing, config, ct).ConfigureAwait(false))
                {
                    managed.Add(all[i].Rule.Name);
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
        progress.Report(100);
    }

    /// <summary>Returns true when the collection exists after this call.</summary>
    private async Task<bool> ApplyRule(
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
            return box is not null;
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
        }

        // Adding or removing items changes the stored collection, so work on a fresh copy. Saving the older
        // in-memory one would write the previous member list back and undo the change.
        box = _library.GetItemById(box.Id) as BoxSet ?? box;

        box.DisplayOrder = rule.ItemOrder == "SortName" ? "SortName" : "PremiereDate";

        // Jellyfin sorts collections by SortName, so a numeric prefix gives a stable, user-set order.
        box.ForcedSortName = rule.Position > 0
            ? $"{rule.Position:D3} {rule.Name}"
            : string.Empty;

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

        return true;
    }
}
