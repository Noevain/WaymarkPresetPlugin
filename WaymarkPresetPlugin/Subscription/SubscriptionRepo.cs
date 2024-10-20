using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaymarkPresetPlugin.Subscription
{
	public struct SubscriptionRepo
	{
		public string _repoUrl;
		public bool _hasUpdates;
		public DateTime _lastUpdateCheck;

		public SubscriptionRepo(string repoUrl,DateTime lastCheck)
		{
			_hasUpdates = true;
			_lastUpdateCheck = lastCheck;
			_repoUrl = repoUrl;
		}
	}
}
