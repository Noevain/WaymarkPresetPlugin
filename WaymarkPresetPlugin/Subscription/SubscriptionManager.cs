using CheapLoc;
using Lumina.Data.Structs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WaymarkPresetPlugin.Subscription
{

	public class SubscriptionManager
	{

		private ConcurrentDictionary<string, Task> SubscriptionTask = new();
		public ConcurrentDictionary<string,SubscriptionTaskDetails> SubscriptionTaskDetails = new();

		private SemaphoreSlim JobSemaphore = new SemaphoreSlim(1, 1);

		private SemaphoreSlim LibrarySemaphore = new SemaphoreSlim(1,1);

		private SemaphoreSlim CheckSemaphore = new SemaphoreSlim(1, 1);

		private Configuration conf = null!;

		public SubscriptionManager(Configuration conf) {
			this.conf = conf;
		
		}

		public void ScheduleCheckForUpdates(SubscriptionRepo repo)
		{
			Plugin.Log.Debug("Schedule entered");
			if (SubscriptionTask.ContainsKey(repo._repoUrl)) { return; }
			Plugin.Log.Debug("Starting");
			SubscriptionTask[repo._repoUrl] = Task.Run(async () =>
			{
				try
				{
					Plugin.Log.Debug("checking");
					await CheckForUpdates(repo);
				}
				finally
				{
					SubscriptionTask.Remove(repo._repoUrl, out var _);
				}
			});
		}

		private async Task CheckForUpdates(SubscriptionRepo repo)
		{
			var taskDetails = SubscriptionTaskDetails[repo._repoUrl] = new SubscriptionTaskDetails();
			await taskDetails.Child("Waiting for exisiting jobs to finish").Loading(async () =>
			{
				await JobSemaphore.WaitAsync();
			});
			try
			{
				await taskDetails.Catching(async () =>
				{
					var (manifest, needUpdate) = await FetchManifest(repo, taskDetails);

					if (needUpdate)
					{
						repo._hasUpdates = true;//manifest has updates so no need to check everything
						int ind = conf.subscribed_repos.FindIndex(item => item._repoUrl == repo._repoUrl);
						if(ind != -1)
						{
							conf.subscribed_repos[ind] = repo;
						}
						return;
					}
					repo._hasUpdates = false;
					var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 4 };
					await Parallel.ForEachAsync(manifest.waymarks, parallelOptions, async (waymark, token) =>
					{
						
						if(await CheckWaymark(waymark.url, taskDetails))
						{
							try
							{
								await CheckSemaphore.WaitAsync();
								repo._hasUpdates = true;
							}
							finally
							{
								CheckSemaphore.Release();
							}
						}
					});
				});
				
			}
			finally
			{
				JobSemaphore.Release();
			}
		}

		/// <summary>
		/// Start the update process for a given repository
		/// This will update regardless if the local manifest is outdated or not,Call CheckForUpdate first
		/// to make sure an update is needed or not
		/// </summary>
		/// <param name="repoToSync"></param>
		/// repository to sync
		/// <returns></returns>
		public async Task Sync(SubscriptionRepo repoToSync)
		{
			var taskDetails = SubscriptionTaskDetails[repoToSync._repoUrl] = new SubscriptionTaskDetails();
			await taskDetails.Child("Waiting for exisiting jobs to finish").Loading(async () =>
			{
				await JobSemaphore.WaitAsync();
			});

			try
			{
				await taskDetails.Catching(async () =>
				{
					var (manifest,_ ) = await FetchManifest(repoToSync, taskDetails);
					var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 4 };
					await Parallel.ForEachAsync(manifest.waymarks, parallelOptions, async (waymark,token) =>
					{
						string fullurl = manifest.folderurl + waymark.url;
						var childDetails = taskDetails.Child($"Sync {waymark.url}");
						await SyncOrCheckWaymark(fullurl,manifest.name,childDetails);
					});
					repoToSync._hasUpdates = false;
					repoToSync._lastUpdateCheck = DateTime.Now;
					int ind = conf.subscribed_repos.FindIndex(item => item._repoUrl == repoToSync._repoUrl);
					if (ind != -1)
					{
						conf.subscribed_repos[ind] = repoToSync;
					}

				});
			}finally
			{
				JobSemaphore.Release();
			}
		}

		private async Task<bool> CheckWaymark(string url, SubscriptionTaskDetails taskDetails)
		{
			var childDetails = taskDetails.Child($"Checking");
			Plugin.Log.Debug("Hello");
			var response = await Plugin._httpClient.GetAsync(url);
			response.EnsureSuccessStatusCode();
			if (conf.urls_to_etags.ContainsKey(url) && response.Headers.ETag != null)
			{
				if (conf.urls_to_etags[url] != response.Headers.ETag.Tag)
				{
					taskDetails.Child($"Need update");
					return true;
				}
				return false;
			}
			//unknown waymark so need to update
			return true;

		}

		private async Task SyncOrCheckWaymark(string url,string manifest_name,SubscriptionTaskDetails taskDetails)
		{
			var response = await Plugin._httpClient.GetAsync(url);
			response.EnsureSuccessStatusCode();
			if (conf.urls_to_etags.ContainsKey(url) && response.Headers.ETag != null)
			{
				if (conf.urls_to_etags[url] != response.Headers.ETag.Tag)
				{
					var preset_as_str = await response.Content.ReadAsStringAsync();
					await LibrarySemaphore.WaitAsync();//Access the library 1 at the time to prevent access errors for now
					ProcessSubscriptionImport(preset_as_str, manifest_name);
					LibrarySemaphore.Release();
					conf.urls_to_etags[url] = response.Headers.ETag.Tag;
					taskDetails.Child($"Updated");
				}
				else
				{
					taskDetails.Child($"No changes");
				}
			}
			else
			{//no existing ETag
				var preset_as_str = await response.Content.ReadAsStringAsync();
				await LibrarySemaphore.WaitAsync();
				ProcessSubscriptionImport(preset_as_str, manifest_name);
				LibrarySemaphore.Release();
				conf.urls_to_etags[url] = response.Headers.ETag.Tag;
				taskDetails.Child($"Added");
			}

		}
		private async Task<(SubscriptionManifestYAML, bool)> FetchManifest(SubscriptionRepo repo, SubscriptionTaskDetails taskDetails)
		{
			var childTask = taskDetails.Child("Fetch Manifest");

			var response = await childTask.Child("Downloading Headers").Loading(async () => {
				var request = new HttpRequestMessage(HttpMethod.Get, repo._repoUrl);
				var response = await Plugin._httpClient.SendAsync(request);
				response.EnsureSuccessStatusCode();
				return response;
			});

			bool hasKnownEtag = false;
			if (conf.urls_to_etags.ContainsKey(repo._repoUrl) && response.Headers.ETag != null)
			{
				hasKnownEtag = conf.urls_to_etags[repo._repoUrl] == response.Headers.ETag.Tag;
			}
				conf.urls_to_etags[repo._repoUrl] = response.Headers.ETag.Tag;

			var manifestStr = await response.Content.ReadAsStringAsync();
			var manifest = SubscriptionManifestYAML.From(manifestStr);

			if (response.Headers.ETag != null && !hasKnownEtag)
			{
				childTask.Child($"Has changes - ETag mismatch {response.Headers.ETag.Tag}");
				return (manifest, true);
			}
			else
			{
				childTask.Child("No changes - ETag match");
				return (manifest, false);
			}
		}

		public void ProcessSubscriptionImport(string preset, string prefix)
		{
			try
			{
				var tempPreset = conf.PresetLibrary.ImportPreset(preset, prefix);
				conf.Save();
				Plugin.Log.Debug("Yay waymarks");
			}
			catch (Exception ex)
			{
				Plugin.Log.Error(ex.Message);
			}
		}


	}
}
