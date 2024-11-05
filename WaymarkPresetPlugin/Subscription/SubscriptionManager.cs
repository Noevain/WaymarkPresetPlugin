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
    
    public ConcurrentDictionary<int, string> status { get; } = new();
    

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
    public Task Subscribe(string url,CancellationToken token)
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
            }),token);
    }
/// <summary>
/// Remove the repo from subscriptions and stop tracking updates
/// </summary>
/// <param name="repo">SubscriptionRepo to remove</param>
/// <param name="deleteAll">Should delete waymarks that are tracked by this subscription?</param>
    public void Unsubscribe(SubscriptionRepo repo,bool deleteAll)
    {
        //deleting all related waymarks sounds a bit too destructive
        //users still retain the ability to delete them individually
        //buuuut just in case....
        if (deleteAll)
        {
            for (int i = 0; i < Configuration.PresetLibrary.Presets.Count; i++)
            {
                if (Configuration.PresetLibrary.Presets[i].Name.Contains("["+repo.Name+"]"))
                {
                    if (!Configuration.PresetLibrary.DeletePreset(i))
                    {
                        Plugin.Log.Error(
                            $"Failed to delete preset {Configuration.PresetLibrary.Presets[i].Name} at index {i}.");
                    }
                }
            }
        }

        Configuration.Subscriptions.Remove(repo);
        Configuration.Save();
    }
/// <summary>
/// Check the given SubscriptionRepo for any updates against known ETag
/// </summary>
/// <param name="subscription">The SubscriptionRepo to check</param>
/// <param name="shouldUpdate">Should the operation update the manifest and associated waymarks</param>
/// <returns>A task object representing the CheckForUpdates process</returns>
    public Task CheckForUpdates(SubscriptionRepo subscription,bool shouldUpdate,CancellationToken cancellationToken)
    {
        
        Plugin.Log.Debug($"Checking for updates...{subscription.RepoUrl}");
        return Task.Run((async () =>
        {
            var (manifest, hasUpdate) = await FetchManifest(subscription.RepoUrl, shouldUpdate);
            if (hasUpdate)
            {
                Plugin.Log.Debug($"Update found for{manifest.name}");
            }
            //not setting MaxDegreeOfParallelism can result in locking up all threads
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 4 };
            bool hasErrored = false;
            await Parallel.ForEachAsync(manifest.waymarks, parallelOptions, async (waymark, token) =>
            {
                var (waymarkStr,hasWaymarkUpdate) = await FetchWaymark(manifest.folderurl + waymark.url, shouldUpdate);
                var importedPreset = JsonConvert.DeserializeObject<WaymarkPreset>(waymarkStr);
                if (importedPreset == null)
                {
                    hasErrored = true;
                    Plugin.Log.Warning(
                        $"Error while checking {waymark.url} , waymark: Deserialized input resulted in a null!");
                    return;
                }

                var idx = Configuration.PresetLibrary.GetIndiceOfPresetIfExists(importedPreset);
                if (hasWaymarkUpdate)
                {
                    hasUpdate = true;
                    //if this fails anyway this will overwrite the -1 key but since the waymark is not in the library we dont care
                    status[idx] = "Has update";
                    if (shouldUpdate)
                    {
                        try
                        {
                            Plugin.Log.Debug($"Starting update for {importedPreset.Name}");
                            int idx_new = Configuration.PresetLibrary.ImportPreset(importedPreset,manifest.name);
                            status[idx_new] = "Updated";
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Error($"Failed update for {waymark.name}, exception: {ex}");
                            hasErrored = true;
                        }
                    }
                    return;
                }
                //edge case:waymark ETag is up to date but not found in library(user deletion)
                //Check if we want to update,if not just mark the repo has having update
                //Next update will add the waymarks back
                if (idx == -1)
                {
                    if (!shouldUpdate)
                    {
                        hasUpdate = true;
                        return;
                    }

                    try
                    {
                        Plugin.Log.Debug($"Starting update for {importedPreset.Name}");
                        int idx_new = Configuration.PresetLibrary.ImportPreset(importedPreset,manifest.name);
                        status[idx_new] = "Updated";
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"Failed update for {waymark.name}, exception: {ex}");
                        hasErrored = true;
                    }

                    return;
                }
                status[idx] = "No update";
                
            });
            Plugin.Log.Debug("Updating repo timestamp...");
            var subscriptionIdx = Configuration.Subscriptions.IndexOf(subscription);
            //if there was an error we want to leave the update button enabled so we can try again
            if (hasUpdate && shouldUpdate && !hasErrored)
                subscription.HasUpdates = false;//we just updated them
            else
                subscription.HasUpdates = hasUpdate;
            subscription.LastUpdateCheck = DateTime.Now;
            Configuration.Subscriptions[subscriptionIdx] = subscription;
            Configuration.Save();
        }),cancellationToken);
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
                    Plugin.Log.Debug($"Manifest ETag created for {url}");
                }

            }

            else if (Configuration.url_to_etags[url] != response.Headers.ETag.Tag)
            {
                hasUpdated = true;
                if (updateETag)
                {
                    Configuration.url_to_etags[url] = response.Headers.ETag.Tag;
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
                    Plugin.Log.Debug($"Waymark ETag created for {url}");
                }

            }

            else if (Configuration.url_to_etags[url] != response.Headers.ETag.Tag)
            {
                hasUpdated = true;
                if (updateETag)
                {
                    Configuration.url_to_etags[url] = response.Headers.ETag.Tag;
                    Plugin.Log.Debug($"Waymark ETag updated for {url}");
                }
            }
        }
        return (waymarkStr,hasUpdated);
    }
/// <summary>
/// Make a name to be used for storing in the library,in case we need to change the format easily
/// </summary>
/// <param name="name">Name of the waymark</param>
/// <param name="prefix">Prefix to use,should be name of the manifest</param>
/// <returns>The name+prefix formated</returns>
    public static string MakeName(string name, string prefix)
    {
        return "[" + prefix + "]" + name;
    }
}