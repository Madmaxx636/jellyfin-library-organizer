using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Organizer.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Organizer;

public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<AutoSyncService>();
    }
}

/// <summary>Runs the sync a couple of minutes after new movies or shows land in the library.</summary>
public sealed class AutoSyncService : IHostedService, IDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(2);
    private readonly ILibraryManager _library;
    private readonly ITaskManager _tasks;
    private readonly object _lock = new();
    private Timer? _timer;

    public AutoSyncService(ILibraryManager library, ITaskManager tasks)
    {
        _library = library;
        _tasks = tasks;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded += OnChanged;
        _library.ItemRemoved += OnChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded -= OnChanged;
        _library.ItemRemoved -= OnChanged;
        return Task.CompletedTask;
    }

    private void OnChanged(object? sender, ItemChangeEventArgs e)
    {
        // Our own collections are BoxSets; only react to real media.
        if (e.Item is not (Movie or Series) || Plugin.Instance?.Configuration.SyncOnLibraryChange != true)
        {
            return;
        }

        lock (_lock)
        {
            _timer?.Dispose();
            _timer = new Timer(_ => _tasks.QueueScheduledTask<SyncCollectionsTask>(), null, Delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
        }
    }
}
