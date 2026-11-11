namespace PluginEntry
{

    public interface IPlugin
    {
        string Name { get; }

        void Register();

        void Unregister();

        Dictionary<string, Func<PluginParameter[], object>> FunctionDict { get; }

        //object Run(string functionKey, dynamic parameters);
        object Run(string functionKey, params PluginParameter[] parameters);

        /// <summary>
        /// 获取参数描述
        /// </summary>
        Dictionary<string, List<PluginParameter>> GetParameterDescriptions();
    }

}
