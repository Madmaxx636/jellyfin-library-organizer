using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Organizer.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Organizer.Tasks;

/// <summary>Registers home rows on every server start (Home Screen Sections forgets them on restart).</summary>
public class RegisterHomeSectionsTask : IScheduledTask
{
    private readonly ILogger<RegisterHomeSectionsTask> _log;

    public RegisterHomeSectionsTask(ILogger<RegisterHomeSectionsTask> log) => _log = log;

    public string Name => "Register organizer home rows";
    public string Key => "OrganizerRegisterHome";
    public string Description => "Adds the organizer collections as rows in Home Screen Sections.";
    public string Category => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        // Startup order isn't guaranteed, so give Home Screen Sections a moment to load.
        for (var attempt = 0; attempt < 12; attempt++)
        {
            if (HomeSections.Register(config, _log))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }

        progress.Report(100);
    }
}
