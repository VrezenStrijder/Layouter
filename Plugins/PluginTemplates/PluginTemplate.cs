using System;
using System.Collections.Generic;
using System.Windows;
using PluginEntry;

public class PluginTemplate : IPlugin
{
    public Dictionary<string, Func<PluginParameter[], object>> FunctionDict { get; set; } = new Dictionary<string, Func<PluginParameter[], object>>();

    public string Name => "插件名称";

    /// <summary>
    /// 注册插件方法
    /// </summary>
    public void Register()
    {
        FunctionDict.Add("自定义方法", CustomFunction);
    }

    /// <summary>
    /// 注销插件
    /// </summary>
    public void Unregister()
    {
        FunctionDict.Clear();
    }


    /// <summary>
    /// 执行方法
    /// </summary>
    /// <param name="functionKey">方法标识</param>
    /// <param name="parameters">方法参数数组</param>
    /// <returns>返回值</returns>
    public object Run(string functionKey, params PluginParameter[] parameters)
    {
        if (FunctionDict.ContainsKey(functionKey))
        {
            try
            {
                return FunctionDict[functionKey].Invoke(parameters);
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"执行功能出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// 获取参数列表(如果需要传递参数)
    /// </summary>
    public Dictionary<string, List<PluginParameter>> GetParameterDescriptions()
    {
        return null;
    }

    /// <summary>
    /// 自定义方法
    /// (请替换为自己要实现的方法)
    /// </summary>
    /// <param name="parameters">方法参数</param>
    /// <returns></returns>
    private object CustomFunction(PluginParameter[] parameters)
    {
        return null;
    }

}
