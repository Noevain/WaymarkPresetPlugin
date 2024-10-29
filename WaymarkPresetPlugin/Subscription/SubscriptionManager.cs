using System;
using System.Collections.Concurrent;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Networking.Http;
using System.Net.Http;
using Newtonsoft.Json;

namespace WaymarkPresetPlugin.Subscription;

public class SubscriptionManager
{
    private readonly Configuration Configuration;
    
    
    public static HappyEyeballsCallback _happyEyeballsCallback { get; private set; } = null!;

    public static HttpClient _httpClient { get; private set; } = null!;
    
    private ConcurrentDictionary<string, string> status { get; } = new();
    
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
/// <summary>
/// Check the given SubscriptionRepo for any updates against known ETag
/// </summary>
/// <param name="subscription">The SubscriptionRepo to check</param>
/// <returns>A task object representing the CheckForUpdates process</returns>
    public Task CheckForUpdates(SubscriptionRepo subscription)
    {
        
        Plugin.Log.Debug($"Checking for updates...{subscription.RepoUrl}");
        return Task.Run((async () =>
        {
            var (manifest, hasUpdate) = await FetchManifest(subscription.RepoUrl, false);
            if (hasUpdate)
            {
                Plugin.Log.Debug($"Update found for{manifest.name}");
            }
            //not setting MaxDegreeOfParallelism can result in locking up all threads
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 4 };
            bool waymarkUpdates = false;
            await Parallel.ForEachAsync(manifest.waymarks, parallelOptions, async (waymark, token) =>
            {
                var (waymarkStr,hasWaymarkUpdate) = await FetchWaymark(manifest.folderurl + waymark.url, false);
                if (hasWaymarkUpdate)
                {
                    status[waymark.name] = "Has update";
                    hasUpdate = true;
                    return;
                }

                status[waymark.name] = "No update";
                
            });
            Plugin.Log.Debug("Updating repo timestamp...");
            var subscriptionIdx = Configuration.Subscriptions.IndexOf(subscription);
            subscription.HasUpdates = hasUpdate;
            subscription.LastUpdateCheck = DateTime.Now;
            Configuration.Subscriptions[subscriptionIdx] = subscription;
            Configuration.Save();
        }));
    }

/// <summary>
/// Fetch a manifest and serialize it
/// </summary>
/// <param name="url">URL pointing to the manifest to fetch</param>
/// <param name="updateETag">Should this operation update the ETag</param>
/// <returns>Serialized manifest and if there is a known ETag for it, whether it needs to be updated </returns>
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

            else if (Configuration.url_to_etags[url] != response.Headers.ETag.Tag)
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
/// <summary>
/// Fetch the waymark at the given url
/// </summary>
/// <param name="url">URL pointing to the waymark in json format</param>
/// <param name="updateETag">Should this operation update its ETag</param>
/// <returns>The waymark.json as a string and if there is a known ETag for it, whether it needs to be updated</returns>
    private async Task<(string,bool)> FetchWaymark(string url,bool updateETag)
    {
        Plugin.Log.Debug($"Fetching waymark...{url}");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var waymarkStr = await response.Content.ReadAsStringAsync();
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
                    Plugin.Log.Debug($"Waymark ETag created for {url}");
                }

            }

            else if (Configuration.url_to_etags[url] != response.Headers.ETag.Tag)
            {
                hasUpdated = true;
                if (updateETag)
                {
                    Configuration.url_to_etags[url] = response.Headers.ETag.Tag;
                    Configuration.Save();
                    Plugin.Log.Debug($"Waymark ETag updated for {url}");
                }
            }
        }
        return (waymarkStr,hasUpdated);


    }
}