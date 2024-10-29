using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaymarkPresetPlugin.Subscription
{
    public struct SubscriptionRepo
    {
        public string Name { get; set; }
        public string RepoUrl;
        public bool HasUpdates;
        public DateTime LastUpdateCheck;
        public string LastKnownManifestETag;

        public SubscriptionRepo(string name,string repoUrl,DateTime lastCheck)
        {
            Name = name;
            HasUpdates = true;
            LastUpdateCheck = lastCheck;
            RepoUrl = repoUrl;
            LastKnownManifestETag = string.Empty;
        }
    }
}