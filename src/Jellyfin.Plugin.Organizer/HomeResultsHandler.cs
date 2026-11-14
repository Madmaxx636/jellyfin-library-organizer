using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Organizer;

public class HomePayload
{
    public Guid UserId { get; set; }
    public string? AdditionalData { get; set; }
}

/// <summary>Called by Home Screen Sections to fill a row.</summary>
public class HomeResultsHandler
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly IDtoService _dto;

    public HomeResultsHandler(ILibraryManager library, IUserManager users, IDtoService dto)
    {
        _library = library;
        _users = users;
        _dto = dto;
    }

    public QueryResult<BaseItemDto> GetCollectionResults(HomePayload payload)
    {
        var user = _users.GetUserById(payload.UserId);
        if (user is null)
        {
            return new QueryResult<BaseItemDto>();
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var rule = config.Rules.Concat(config.AutoRules)
            .FirstOrDefault(r => string.Equals(r.Name, payload.AdditionalData, StringComparison.OrdinalIgnoreCase));

        var box = _library.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                Recursive = true
            }).OfType<BoxSet>()
            .FirstOrDefault(b => string.Equals(b.Name, payload.AdditionalData, StringComparison.OrdinalIgnoreCase));

        var children = box?.GetChildren(user, true, null) ?? new List<BaseItem>();
        var items = Sort(children, rule?.HomeSort, rule?.HomeDescending ?? true)
            .Take(Math.Max(1, config.HomeRowLimit))
            .ToList();

        var options = new DtoOptions
        {
            Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.MediaSourceCount },
            ImageTypes = new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Banner, ImageType.Thumb },
            ImageTypeLimit = 1
        };

        return new QueryResult<BaseItemDto>(_dto.GetBaseItemDtos(items, options, user));
    }

    private static DateTime ReleaseDate(BaseItem i) =>
        i.PremiereDate ?? (i.ProductionYear is int y && y > 0 && y < 9999 ? new DateTime(y, 1, 1) : DateTime.MinValue);

    public static IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, string? sort, bool descending)
    {
        switch (sort)
        {
            case "Random":
                return items.OrderBy(_ => Random.Shared.Next());
            case "SortName":
                return descending
                    ? items.OrderByDescending(i => i.SortName, StringComparer.OrdinalIgnoreCase)
                    : items.OrderBy(i => i.SortName, StringComparer.OrdinalIgnoreCase);
            case "CommunityRating":
                return descending
                    ? items.OrderByDescending(i => i.CommunityRating ?? 0)
                    : items.OrderBy(i => i.CommunityRating ?? 0);
            case "DateAdded":
                return descending
                    ? items.OrderByDescending(i => i.DateCreated)
                    : items.OrderBy(i => i.DateCreated);
            default:
                return descending
                    ? items.OrderByDescending(i => ReleaseDate(i))
                    : items.OrderBy(i => ReleaseDate(i));
        }
    }
}
