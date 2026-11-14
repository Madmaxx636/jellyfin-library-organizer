using System;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Dto;
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

        var box = _library.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                Recursive = true
            }).OfType<BoxSet>()
            .FirstOrDefault(b => string.Equals(b.Name, payload.AdditionalData, StringComparison.OrdinalIgnoreCase));
        var limit = Math.Max(1, Plugin.Instance?.Configuration.HomeRowLimit ?? 16);
        var items = box?.GetChildren(user, true, null).Take(limit).ToList()
                    ?? new System.Collections.Generic.List<BaseItem>();

        var options = new DtoOptions
        {
            Fields = new[] { ItemFields.PrimaryImageAspectRatio, ItemFields.MediaSourceCount },
            ImageTypes = new[] { ImageType.Primary, ImageType.Backdrop, ImageType.Banner, ImageType.Thumb },
            ImageTypeLimit = 1
        };

        return new QueryResult<BaseItemDto>(_dto.GetBaseItemDtos(items, options, user));
    }
}
