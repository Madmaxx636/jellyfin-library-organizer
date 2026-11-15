using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Organizer;

/// <summary>Makes a poster, thumb and backdrop for a collection and saves them, only when something changed.</summary>
public class ArtworkService
{
    public const string HashKey = "LibraryOrganizerArt";
    private const string Version = "v1";

    private readonly IProviderManager _providers;
    private readonly ILogger _log;

    public ArtworkService(IProviderManager providers, ILogger log)
    {
        _providers = providers;
        _log = log;
    }

    /// <summary>Which titles' artwork to show, best-rated first, with a stable order so images don't churn.</summary>
    public static List<BaseItem> PickTitles(IEnumerable<BaseItem> matched, bool releaseOrder = false) =>
        releaseOrder
            ? matched.OrderBy(i => i.PremiereDate ?? (i.ProductionYear is int y && y > 0 ? new DateTime(y, 1, 1) : DateTime.MaxValue)).ThenBy(i => i.Id).ToList()
            : matched.OrderByDescending(i => i.CommunityRating ?? 0).ThenBy(i => i.Id).ToList();

    public static string Hash(string name, string style, string accent, int count, IEnumerable<string> posters, IEnumerable<string> wides)
    {
        var text = string.Join('|', Version, name, style, accent, count, string.Join(',', posters), string.Join(',', wides));
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public async Task Apply(
        BoxSet box, CollectionRule rule, IReadOnlyList<BaseItem> matched, PluginConfiguration config, CancellationToken ct)
    {
        if (config.ArtworkMode == "Off" || rule.KeepOwnArtwork)
        {
            return;
        }

        // A franchise reads best in release order: the first film leads the cover.
        var ordered = PickTitles(matched, rule.Franchises.Length > 0);
        var posters = ordered.Select(i => LocalImage(i, ImageType.Primary)).Where(p => p is not null).Select(p => p!).Take(6).ToList();
        var wides = ordered.Select(i => LocalImage(i, ImageType.Backdrop)).Where(p => p is not null).Select(p => p!).Take(5).ToList();
        if (wides.Count == 0)
        {
            wides = posters.Take(5).ToList();
        }

        var accentText = rule.AccentColor ?? string.Empty;
        var hash = Hash(rule.Name, config.ArtworkStyle, accentText, matched.Count, posters, wides);
        var haveAll = box.HasImage(ImageType.Primary) && box.HasImage(ImageType.Thumb) && box.HasImage(ImageType.Backdrop);
        box.ProviderIds.TryGetValue(HashKey, out var current);

        if (config.ArtworkMode == "FillMissing")
        {
            // Only fill gaps; never touch an image that is there, unless we made it and it is out of date.
            if (haveAll && current is null)
            {
                return;
            }
        }

        if (haveAll && string.Equals(current, hash, StringComparison.Ordinal))
        {
            return;
        }

        var accent = ArtworkRenderer.Accent(rule.Name, accentText);
        var subtitle = ArtworkRenderer.Subtitle(matched.Count, rule.MediaType);
        var style = config.ArtworkStyle == "Title" ? "Title" : "Mosaic";

        var poster = ArtworkRenderer.Poster(rule.Name, subtitle, posters, accent, style);
        var wide = ArtworkRenderer.Wide(rule.Name, subtitle, wides, accent, style);

        await Save(box, poster, ImageType.Primary, ct).ConfigureAwait(false);
        await Save(box, wide, ImageType.Thumb, ct).ConfigureAwait(false);
        if (box.GetImages(ImageType.Backdrop).Any())
        {
            await box.DeleteImageAsync(ImageType.Backdrop, 0).ConfigureAwait(false);
        }

        await Save(box, wide, ImageType.Backdrop, ct).ConfigureAwait(false);

        var fresh = box;
        fresh.ProviderIds[HashKey] = hash;
        await fresh.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, ct).ConfigureAwait(false);
        _log.LogInformation("Made artwork for collection '{Name}' from {Count} posters", rule.Name, posters.Count);
    }

    private static string? LocalImage(BaseItem item, ImageType type)
    {
        try
        {
            var path = item.GetImagePath(type, 0);
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task Save(BaseItem item, byte[] jpeg, ImageType type, CancellationToken ct)
    {
        using var stream = new MemoryStream(jpeg);
        await _providers.SaveImage(item, stream, "image/jpeg", type, null, ct).ConfigureAwait(false);
    }
}
