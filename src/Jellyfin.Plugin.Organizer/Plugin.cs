using System;
using System.Collections.Generic;
using System.Linq;
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

        // A config written by an older version may hold duplicated default rules; keep the first of each name.
        var distinct = Configuration.Rules
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        var changed = distinct.Count != Configuration.Rules.Count;
        Configuration.Rules = distinct;
        if (!Configuration.DefaultsSeeded)
        {
            if (Configuration.Rules.Count == 0)
            {
                Configuration.Rules = PluginConfiguration.DefaultRules();
            }

            Configuration.DefaultsSeeded = true;
            changed = true;
        }

        changed |= PluginConfiguration.Migrate(Configuration);

        if (changed)
        {
            SaveConfiguration();
        }

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
            DisplayName = Name,
            EnableInMainMenu = true,
            MenuSection = "server",
            MenuIcon = "collections_bookmark",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
        };
    }
}
