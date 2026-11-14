using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Organizer;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        ConfigurationChanged += (_, _) =>
            HomeSections.Register(Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Library Organizer";

    public override Guid Id => Guid.Parse("6f1c2a54-3b7e-4d0a-9a51-2c8e7d4b1f90");

    public override string Description =>
        "Builds and orders collections automatically from genres, tags, countries, studios and more.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
        };
    }
}
