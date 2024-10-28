using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WaymarkPresetPlugin.Subscription
{
    public class SubscriptionManifestYAML
    {
        public string name { get; set; } = null!;
        //the path to search for waymarks using their relative urls
        public string folderurl { get; set; } = null!;
        public List<WaymarkYAML> waymarks {  get; set; } = null!;

        public static SubscriptionManifestYAML From(string input){
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            return deserializer.Deserialize<SubscriptionManifestYAML>(input);

        }
    }

    public class WaymarkYAML
    {
        public string? name { get; set; } = null!;
        public string? url { get; set; } = null!;
    }
}