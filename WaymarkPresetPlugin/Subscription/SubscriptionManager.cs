using System;
using System.Collections.Concurrent;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Networking.Http;
using System.Net.Http;
namespace WaymarkPresetPlugin.Subscription;

public class SubscriptionManager
{
    private readonly Configuration Configuration;
    
    
    public static HappyEyeballsCallback _happyEyeballsCallback { get; private set; } = null!;

    public static HttpClient _httpClient { get; private set; } = null!;
    
    private ConcurrentDictionary<string,Task> WaymarkTasks = new();
    private ConcurrentDictionary<string, Task> SubscriptionsTasks = new();
    
    private SemaphoreSlim JobSemaphore = new SemaphoreSlim(1, 1);
    private SemaphoreSlim LibraryWriterSemaphore = new SemaphoreSlim(1, 1);

    public SubscriptionManager(Configuration configuration)
    {
        Configuration = configuration;
        _happyEyeballsCallback = new HappyEyeballsCallback();
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = _happyEyeballsCallback.ConnectCallback,
            MaxConnectionsPerServer = 4 // Github won't allow more then 10 max connections
        });
    }
/// <summary>
/// Try to subscribe to the given URL
/// </summary>
/// <param name="url">URL to sub to</param>
/// <returns>A task object representing the subscription process</returns>
/// <exception cref="ArgumentException">Thrown if URL already exist or is not an http or https link</exception>
    public Task Subscribe(string url)
    {
        if(Configuration.Subscriptions.Any(repo => repo.RepoUrl == url))
            throw new DuplicateNameException($"Subscription URL {url} already exists.");
        if(!url.StartsWith("http") || !url.StartsWith("https"))
            throw new ArgumentException($"URL does not start with 'http' or 'https'.");
        return Task.Run((async () =>
            {
                var (manifest,_) = await FetchManifest(url, true);
                SubscriptionRepo repo = new SubscriptionRepo(manifest.name,url,DateTime.MinValue);
                Configuration.Subscriptions.Add(repo);
                Configuration.Save();
            }));
        
    }

    public async void CheckForUpdates(SubscriptionRepo subscription)
    {
        Plugin.Log.Debug($"Checking for updates...{subscription.RepoUrl}");
        
    }

/// <summary>
/// Fetch a manifest and serialize it
/// </summary>
/// <param name="url">URL pointing to the manifest to fetch</param>
/// <param name="updateETag">Should this operation update the ETag</param>
/// <returns>Serialized manifest and if there is a known ETag for it, whether or not it needs to be updated </returns>
    private async Task<(SubscriptionManifestYAML,bool)> FetchManifest(string url,bool updateETag)
    {
        Plugin.Log.Debug($"Fetching manifest...{url}");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var manifestStr = await response.Content.ReadAsStringAsync();
        var manifest = SubscriptionManifestYAML.From(manifestStr);
        bool hasUpdated = false;
        if (response.Headers.ETag != null)
        {
            if (!Configuration.url_to_etags.ContainsKey(url))
            {
                hasUpdated = true;
                if (updateETag)
                {
                    Configuration.url_to_etags[url] = response.Headers.ETag.Tag;
                    Configuration.Save();
                    Plugin.Log.Debug($"Manifest ETag created for {url}");
                }

            }

            if (Configuration.url_to_etags[url] != response.Headers.ETag.Tag)
            {
                hasUpdated = true;
                if (updateETag)
                {
                    Configuration.url_to_etags[url] = response.Headers.ETag.Tag;
                    Configuration.Save();
                    Plugin.Log.Debug($"Manifest ETag updated for {url}");
                }
            }
        }


        return (manifest,hasUpdated);
    }
}