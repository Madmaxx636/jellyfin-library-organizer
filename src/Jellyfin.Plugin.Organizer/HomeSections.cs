using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Organizer.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.Organizer;

/// <summary>Registers one Home Screen Sections row per rule, if that plugin is installed.</summary>
public static class HomeSections
{
    public static string SectionId(CollectionRule rule) =>
        "LibraryOrganizer_" + Regex.Replace(rule.Name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

    public static bool Register(PluginConfiguration config, ILogger log)
    {
        var assembly = AssemblyLoadContext.All
            .SelectMany(c => c.Assemblies)
            .FirstOrDefault(a => a.FullName?.Contains(".HomeScreenSections", StringComparison.Ordinal) ?? false);

        if (assembly is null)
        {
            log.LogInformation("Home Screen Sections is not installed; skipping home rows");
            return false;
        }

        var iface = assembly.GetType("Jellyfin.Plugin.HomeScreenSections.PluginInterface");
        var register = iface?.GetMethod("RegisterSection");
        if (register is null)
        {
            log.LogWarning("Home Screen Sections has no PluginInterface.RegisterSection; update that plugin");
            return false;
        }

        var self = typeof(HomeSections).Assembly;
        foreach (var rule in LibraryQuery.AllRules(config).Where(r => r.ShowOnHome))
        {
            var payload = new JObject
            {
                ["id"] = SectionId(rule),
                ["displayText"] = rule.Name,
                ["limit"] = 1,
                ["additionalData"] = rule.Name,
                ["resultsAssembly"] = self.FullName,
                ["resultsClass"] = typeof(HomeResultsHandler).FullName,
                ["resultsMethod"] = nameof(HomeResultsHandler.GetCollectionResults)
            };

            try
            {
                register.Invoke(null, new object?[] { payload });
                log.LogInformation("Registered home row '{Name}'", rule.Name);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Could not register home row '{Name}'", rule.Name);
            }
        }

        return true;
    }
}
