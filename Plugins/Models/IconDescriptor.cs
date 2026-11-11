using System.IO;
using System.Text.Json;

namespace PluginEntry
{
    public class IconDescriptor
    {
        private Dictionary<string, string> iconPaths = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> IconPaths => iconPaths;

        public static IconDescriptor FromJson(string json)
        {
            var descriptor = new IconDescriptor();
            descriptor.iconPaths = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return descriptor;
        }

        public void ResolveRelativePaths(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory) || iconPaths == null)
            {
                return;
            }

            foreach (var key in iconPaths.Keys.ToList())
            {
                var value = iconPaths[key];
                if (string.IsNullOrWhiteSpace(value) ||
                    value.StartsWith("::", StringComparison.Ordinal) ||
                    Uri.TryCreate(value, UriKind.Absolute, out _))
                {
                    continue;
                }

                if (!Path.IsPathRooted(value))
                {
                    iconPaths[key] = Path.Combine(baseDirectory, value.TrimStart('.', '\\', '/'));
                }
            }
        }
    }

}
