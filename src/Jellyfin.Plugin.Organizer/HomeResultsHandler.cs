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
    private readonly IUserDataManager _userData;

    public HomeResultsHandler(ILibraryManager library, IUserManager users, IDtoService dto, IUserDataManager userData)
    {
        _library = library;
        _users = users;
        _dto = dto;
        _userData = userData;
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

        IEnumerable<BaseItem> children = box?.GetChildren(user, true, null) ?? new List<BaseItem>();
        if (rule?.HideWatched == true)
        {
            children = children.Where(c => _userData.GetUserData(user, c)?.Played != true);
        }

        var items = Sort(children, rule, rule?.HomeSort, rule?.HomeDescending ?? true)
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

    /// <summary>"Same" (the default) follows the collection's own order; the older fixed choices still work.</summary>
    public static IEnumerable<BaseItem> Sort(IEnumerable<BaseItem> items, CollectionRule? rule, string? sort, bool descending)
    {
        if (string.IsNullOrEmpty(sort) || sort == "Same")
        {
            return ItemSorter.Order(items, rule?.ItemOrder, rule?.ItemDescending ?? false, rule?.StoryOrder);
        }

        return ItemSorter.Order(items, sort, descending, rule?.StoryOrder);
    }
}
