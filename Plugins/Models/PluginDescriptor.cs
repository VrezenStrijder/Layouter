using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PluginEntry
{
    public class PluginDescriptor
    {
        public string ProtocolVersion { get; set; } = PluginProtocol.CurrentVersion;

        public string Id { get; set; }

        public string Key { get; set; }

        public string Name { get; set; }

        public string PluginClassName { get; set; }

        public string Version { get; set; }

        public string Description { get; set; }

        public string Author { get; set; }

        public int Style { get; set; }

        public bool IsEnabled { get; set; }

        public string CodeFilePath { get; set; }

        public string IconFilePath { get; set; }

        public bool DevelopmentMode { get; set; } = true;

        public bool AllowUnsafeApis { get; set; } = false;

        public static PluginDescriptor FromJson(string json)
        {
            return JsonSerializer.Deserialize<PluginDescriptor>(json);
        }

    }

    public static class PluginProtocol
    {
        public const string CurrentVersion = "1.0";
        public const string ManifestFileName = "plugin.json";
        public const string IconFileName = "icon.json";
        public const string StyleFileName = "style.json";
        public const string PackageExtension = ".plug";
    }
}
