namespace PluginEntry
{
    public class PluginParameter
    {
        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public Type ParameterType { get; set; }

        public object ParameterValue { get; set; }

        public bool IsRequired { get; set; } = true;
    }
}
