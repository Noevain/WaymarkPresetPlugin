using CheapLoc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WaymarkPresetPlugin.Subscription
{

	public class SubscriptionManager
	{

		public ConcurrentDictionary<string,SubscriptionTaskDetails> SubscriptionTaskDetails = new();

		private SemaphoreSlim JobSemaphore = new SemaphoreSlim(1, 1);

		private SemaphoreSlim LibrarySemaphore = new SemaphoreSlim(1,1);

		private Configuration conf = null!;

		public SubscriptionManager(Configuration conf) {
			this.conf = conf;
		
		}

		/// <summary>
		/// Start the update process for a given repository
		/// This will update regardless if the local manifest is outdated or not,Call CheckUpdate first
		/// to make sure an update is needed or not
		/// </summary>
		/// <param name="repoToSync"></param>
		/// repository to sync
		/// <returns></returns>
		public async Task Sync(string repoToSync)
		{
			var taskDetails = SubscriptionTaskDetails[repoToSync] = new SubscriptionTaskDetails();
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
						var response = await Plugin._httpClient.GetAsync(manifest.folderurl + waymark.url);
						response.EnsureSuccessStatusCode();
						var preset_as_str = await response.Content.ReadAsStringAsync();
						await LibrarySemaphore.WaitAsync();//Access the library 1 at the time to prevent access errors for now
						ProcessSubscriptionImport(preset_as_str,manifest.name);
						LibrarySemaphore.Release();

						
					});
				});
			}finally
			{
				JobSemaphore.Release();
			}
		}
		private async Task<(SubscriptionManifestYAML, bool)> FetchManifest(string url, SubscriptionTaskDetails taskDetails)
		{
			var childTask = taskDetails.Child("Fetch Manifest");

			var response = await childTask.Child("Downloading Headers").Loading(async () => {
				var request = new HttpRequestMessage(HttpMethod.Get, url);
				var response = await Plugin._httpClient.SendAsync(request);
				response.EnsureSuccessStatusCode();
				return response;
			});

			bool hasKnownEtag = false;
			if (conf.urls_to_etags.ContainsKey(url) && response.Headers.ETag != null)
			{
				hasKnownEtag = conf.urls_to_etags[url] == response.Headers.ETag.Tag;
			}
				conf.urls_to_etags[url] = response.Headers.ETag.Tag;

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
