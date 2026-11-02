using System;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using PluginEntry;

namespace Layouter.Plugins.Controls
{
    /// <summary>
    /// CodeEditor.xaml 的交互逻辑
    /// </summary>
    public partial class CodeEditor : UserControl
    {
        public event EventHandler<RoutedEventArgs> SaveRequested;
        private JObject pluginDescriptor;
        private bool isUpdatingPluginDescription = false;
        private bool isUpdatingCode = false;

        // 文件列表相关
        private ObservableCollection<PluginFileItem> pluginFiles = new ObservableCollection<PluginFileItem>();
        private string currentPluginDirectory;
        private string currentFilePath;

        // 当前编辑的文件
        private Dictionary<string, string> fileContents = new Dictionary<string, string>();

        public string Code
        {
            get { return aeEditor.Text; }
            set { aeEditor.Text = value; }
        }

        public string PluginDescription
        {
            get { return JsonConvert.SerializeObject(pluginDescriptor, Formatting.Indented); }
            set { UpdatePluginDescriptionFromJson(value); }
        }

        public CodeEditor()
        {
            InitializeComponent();

            // 初始化插件描述
            pluginDescriptor = new JObject
            {
                { "ProtocolVersion", PluginProtocol.CurrentVersion },
                {"Id", Guid.NewGuid().ToString() },
                { "Key", "PluginTemplate" },
                { "Name", "插件1" },
                { "PluginClassName", "PluginTemplate" },
                { "Version", "1.0.0" },
                { "Description", "插件描述" },
                { "Author", "VrezenStrijder" },
                { "Style", 1 },
                { "IsEnabled", true },
                { "CodeFilePath", "./PluginTemplate.cs" },
                { "DevelopmentMode", true },
                { "AllowUnsafeApis", false }
            };

            // 初始化文件列表
            InitializePluginFiles();

            // 初始化右键菜单
            InitializeContextMenu();

            // 设置默认代码
            SetDefaultCode();

            UpdateFormFromPluginDescriptor();
        }

        private void CodeEditor_TextChanged(object sender, EventArgs e)
        {
            if (isUpdatingCode) return;

            // 检查类名是否更改，如果更改则更新插件描述
            UpdateClassNameInPluginDescription();

            // 确保JSON文本框更新
            UpdateJsonTextBox();
        }

        private void PluginForm_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isUpdatingPluginDescription || pluginDescriptor == null) return;

            try
            {
                isUpdatingPluginDescription = true;

                // 更新插件描述对象
                if (sender == PluginNameTextBox)
                {
                    pluginDescriptor["Name"] = PluginNameTextBox.Text;
                }
                else if (sender == PluginClassNameTextBox)
                {
                    string className = PluginClassNameTextBox.Text;
                    string oldClassName = pluginDescriptor["PluginClassName"]?.ToString() ?? "PluginTemplate";
                    pluginDescriptor["PluginClassName"] = className;

                    // 更新ID和文件路径
                    UpdatePluginIdAndCodeFilePath(className);

                    // 更新代码中的类名
                    UpdateClassNameInCode(className);

                    // 延迟更新文件树中的文件名
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateMainFileNameInTree(oldClassName, className);
                    }));
                }
                else if (sender == PluginVersionTextBox)
                {
                    pluginDescriptor["Version"] = PluginVersionTextBox.Text;
                }
                else if (sender == PluginAuthorTextBox)
                {
                    pluginDescriptor["Author"] = PluginAuthorTextBox.Text;
                }
                else if (sender == PluginDescriptionTextBox)
                {
                    pluginDescriptor["Description"] = PluginDescriptionTextBox.Text;
                }
                // 更新只读字段
                UpdateReadOnlyFields();

                // 更新隐藏的JSON文本框
                UpdateJsonTextBox();
            }
            finally
            {
                isUpdatingPluginDescription = false;
            }
        }

        private void PluginForm_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isUpdatingPluginDescription)
            {
                UpdatePluginDescriptor();
            }
        }

        private void UpdatePluginDescriptor()
        {
            if (pluginDescriptor == null) return;

            isUpdatingPluginDescription = true;

            try
            {
                pluginDescriptor["Name"] = PluginNameTextBox.Text;
                pluginDescriptor["PluginClassName"] = PluginClassNameTextBox.Text;
                pluginDescriptor["Version"] = PluginVersionTextBox.Text;
                pluginDescriptor["Author"] = PluginAuthorTextBox.Text;
                pluginDescriptor["Description"] = PluginDescriptionTextBox.Text;
                
                // 获取选中的Style值
                if (PluginStyleComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
                {
                    int styleValue = int.Parse(selectedItem.Tag.ToString());
                    pluginDescriptor["Style"] = styleValue;
                }
                
                pluginDescriptor["IsEnabled"] = PluginEnabledCheckBox.IsChecked ?? false;

                PluginDescriptionJsonTextBox.Text = pluginDescriptor.ToString();
            }
            finally
            {
                isUpdatingPluginDescription = false;
            }
        }

        private void UpdatePluginForm()
        {
            if (pluginDescriptor == null) return;

            isUpdatingPluginDescription = true;

            try
            {
                PluginNameTextBox.Text = pluginDescriptor["Name"]?.ToString() ?? "";
                PluginClassNameTextBox.Text = pluginDescriptor["PluginClassName"]?.ToString() ?? "";
                PluginVersionTextBox.Text = pluginDescriptor["Version"]?.ToString() ?? "";
                PluginAuthorTextBox.Text = pluginDescriptor["Author"]?.ToString() ?? "";
                PluginDescriptionTextBox.Text = pluginDescriptor["Description"]?.ToString() ?? "";

                // 设置Style下拉列表的选中项
                if (pluginDescriptor["Style"] != null && int.TryParse(pluginDescriptor["Style"].ToString(), out int styleValue))
                {
                    foreach (ComboBoxItem item in PluginStyleComboBox.Items)
                    {
                        if (item.Tag != null && int.TryParse(item.Tag.ToString(), out int itemValue) && itemValue == styleValue)
                        {
                            PluginStyleComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
                else
                {
                    PluginStyleComboBox.SelectedIndex = 0; // 默认选择第一项
                }
                
                PluginEnabledCheckBox.IsChecked = pluginDescriptor["IsEnabled"]?.ToObject<bool>() ?? false;

                PluginDescriptionJsonTextBox.Text = pluginDescriptor.ToString();
            }
            finally
            {
                isUpdatingPluginDescription = false;
            }
        }

        private void PluginForm_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (isUpdatingPluginDescription || pluginDescriptor == null) return;

            try
            {
                isUpdatingPluginDescription = true;

                if (sender == PluginEnabledCheckBox)
                {
                    pluginDescriptor["IsEnabled"] = PluginEnabledCheckBox.IsChecked ?? true;
                    UpdateJsonTextBox();
                }
            }
            finally
            {
                isUpdatingPluginDescription = false;
            }
        }

        private void UpdateFormFromPluginDescriptor()
        {
            isUpdatingPluginDescription = true;
            try
            {
                // 更新表单控件
                PluginNameTextBox.Text = pluginDescriptor["Name"]?.ToString() ?? "";
                PluginClassNameTextBox.Text = pluginDescriptor["PluginClassName"]?.ToString() ?? "";
                PluginVersionTextBox.Text = pluginDescriptor["Version"]?.ToString() ?? "";
                PluginAuthorTextBox.Text = pluginDescriptor["Author"]?.ToString() ?? "";
                PluginDescriptionTextBox.Text = pluginDescriptor["Description"]?.ToString() ?? "";

                // 样式值从1开始，索引从0开始
                int styleValue = pluginDescriptor["Style"]?.Value<int>() ?? 1;
                PluginStyleComboBox.SelectedIndex = styleValue - 1;

                PluginEnabledCheckBox.IsChecked = pluginDescriptor["IsEnabled"]?.Value<bool>() ?? true;

                // 更新只读字段
                UpdateReadOnlyFields();

                // 更新隐藏的JSON文本框
                UpdateJsonTextBox();
            }
            finally
            {
                isUpdatingPluginDescription = false;
            }
        }

        private void UpdateReadOnlyFields()
        {
            PluginIdTextBox.Text = pluginDescriptor["Id"]?.ToString() ?? "";
            PluginKeyTextBox.Text = pluginDescriptor["Key"]?.ToString() ?? "";
            PluginCodeFilePathTextBox.Text = pluginDescriptor["CodeFilePath"]?.ToString() ?? "";
        }

        private void UpdateJsonTextBox()
        {
            if (PluginDescriptionJsonTextBox != null)
            {
                PluginDescriptionJsonTextBox.Text = JsonConvert.SerializeObject(pluginDescriptor, Formatting.Indented);
            }
        }

        private void UpdatePluginDescriptionFromJson(string json)
        {
            try
            {
                var newDescriptor = JObject.Parse(json);
                pluginDescriptor = newDescriptor;
                UpdateFormFromPluginDescriptor();
            }
            catch (JsonException ex)
            {
                AppMessageBox.Show($"解析插件描述JSON失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateClassNameInPluginDescription()
        {
            string code = aeEditor.Text;
            var match = Regex.Match(code, @"\bclass\s+([A-Za-z0-9_]+)");
            if (match.Success)
            {
                string className = match.Groups[1].Value;
                if (className != pluginDescriptor["PluginClassName"]?.ToString())
                {
                    pluginDescriptor["PluginClassName"] = className;
                    UpdatePluginIdAndCodeFilePath(className);
                    UpdateFormFromPluginDescriptor();
                }
            }
        }

        private void UpdatePluginIdAndCodeFilePath(string className)
        {
            pluginDescriptor["Key"] = className.ToLower();
            pluginDescriptor["CodeFilePath"] = $"./{className}.cs";
        }

        private void UpdateClassNameInCode(string newClassName)
        {
            isUpdatingCode = true;

            string code = aeEditor.Text;
            var match = Regex.Match(code, @"\bclass\s+([A-Za-z0-9_]+)");
            if (match.Success)
            {
                string oldClassName = match.Groups[1].Value;
                if (oldClassName != newClassName)
                {
                    // 替换类名
                    code = Regex.Replace(code, $@"\bclass\s+{oldClassName}\b", $"class {newClassName}");
                    aeEditor.Text = code;
                }
            }

            isUpdatingCode = false;
        }

        private void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            string result = ValidateCode(aeEditor.Text);
            if (string.IsNullOrEmpty(result))
            {
                AppMessageBox.Show("代码验证通过！", "验证结果", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                AppMessageBox.Show($"代码验证失败：\n{result}", "验证结果", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 保存当前编辑的文件内容
            if (!string.IsNullOrEmpty(currentFilePath) && fileContents.ContainsKey(currentFilePath))
            {
                fileContents[currentFilePath] = aeEditor.Text;
            }

            // 验证主文件代码
            string mainFilePath = GetMainFilePath();
            string mainFileCode = fileContents.ContainsKey(mainFilePath) ? fileContents[mainFilePath] : "";
            string result = ValidateCode(mainFileCode);

            if (string.IsNullOrEmpty(result))
            {
                // 弹出文件夹选择对话框
                var dialog = new SaveFileDialog
                {
                    Title = "选择插件保存位置",
                    Filter = "Layouter插件|*.plug",
                    DefaultExt = ".plug",
                    FileName = pluginDescriptor["PluginClassName"]?.ToString() + ".plug"
                };

                if (dialog.ShowDialog() == true)
                {
                    string pluginFilePath = dialog.FileName;
                    string baseFolder = Path.GetDirectoryName(pluginFilePath);
                    string pluginName = pluginDescriptor["PluginClassName"]?.ToString() ?? "PluginTemplate";
                    string pluginFolderPath = Path.Combine(baseFolder, pluginName);
                    SavePlugin(pluginFolderPath);

                    // 不再触发SaveRequested事件，因为已经在SavePlugin方法中完成了所有保存操作
                    // SaveRequested?.Invoke(this, e);
                }
            }
            else
            {
                AppMessageBox.Show($"代码验证失败，无法保存：\n{result}", "验证结果", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public string ValidateCode(string code)
        {
            try
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var diagnostics = syntaxTree.GetDiagnostics();

                if (diagnostics.Any())
                {
                    var errors = diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => $"行 {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")
                        .ToList();

                    if (errors.Any())
                    {
                        return string.Join("\n", errors);
                    }
                }

                var descriptor = pluginDescriptor.ToObject<PluginDescriptor>();
                var securityResult = new PluginSecurityChecker().ValidateCode(code, descriptor);
                if (!securityResult.IsAllowed)
                {
                    return securityResult.ToString();
                }

                return string.Empty; // 没有错误
            }
            catch (Exception ex)
            {
                return $"验证过程中发生错误: {ex.Message}";
            }
        }

        public void SetDefaultCode()
        {
            try
            {
                // 从嵌入资源加载模板代码
                using (Stream stream = this.GetType().Assembly.GetManifestResourceStream("Layouter.Plugins.PluginTemplates.PluginTemplate.cs"))
                using (StreamReader reader = new StreamReader(stream))
                {
                    string sourceText = reader.ReadToEnd();

                    // 创建主文件
                    string className = pluginDescriptor["PluginClassName"]?.ToString() ?? "PluginTemplate";
                    string mainFilePath = $"{className}.cs";

                    // 添加到文件列表和内容字典
                    AddFileToList(mainFilePath, sourceText);

                    // 显示在编辑器中
                    aeEditor.Text = sourceText;
                    currentFilePath = mainFilePath;

                    // 更新类名和插件描述
                    UpdateClassNameInPluginDescription();
                }
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"加载模板代码失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 确保控件加载完成后更新JSON文本框
        public void LoadPluginFromDirectory(string pluginDirectory)
        {
            if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
            {
                throw new DirectoryNotFoundException($"Plugin directory not found: {pluginDirectory}");
            }

            var manifestPath = Path.Combine(pluginDirectory, PluginProtocol.ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException($"Plugin manifest not found: {manifestPath}");
            }

            currentPluginDirectory = pluginDirectory;
            pluginFiles.Clear();
            fileContents.Clear();
            originalFilePaths.Clear();

            pluginFiles.Add(new PluginFileItem
            {
                Name = "插件文件",
                IsDirectory = true,
                IsExpanded = true,
                FullPath = "插件文件"
            });

            FileTreeView.ItemsSource = pluginFiles;
            FileTreeView.AllowDrop = false;

            var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
            pluginDescriptor = JObject.Parse(manifestJson);
            EnsureDescriptorDefaults();

            foreach (var filePath in Directory.EnumerateFiles(pluginDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(pluginDirectory, filePath).Replace('\\', '/');
                if (IsTextPluginFile(filePath))
                {
                    AddFileToList(relativePath, File.ReadAllText(filePath, Encoding.UTF8));
                }
                else
                {
                    AddFileToList(relativePath, $"[binary file: {relativePath}]");
                    originalFilePaths[relativePath] = filePath;
                    originalFilePaths[Path.GetFileNameWithoutExtension(relativePath)] = filePath;
                }
            }

            var mainFilePath = GetMainFilePath();
            if (!fileContents.ContainsKey(mainFilePath))
            {
                mainFilePath = fileContents.Keys.FirstOrDefault(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    ?? PluginProtocol.ManifestFileName;
            }

            currentFilePath = mainFilePath;
            aeEditor.Text = fileContents.TryGetValue(mainFilePath, out var content) ? content : string.Empty;
            SetEditorSyntax(mainFilePath);
            UpdateFormFromPluginDescriptor();
            RefreshFileTree();
        }

        private void EnsureDescriptorDefaults()
        {
            pluginDescriptor["ProtocolVersion"] ??= PluginProtocol.CurrentVersion;
            pluginDescriptor["Id"] ??= Guid.NewGuid().ToString();
            pluginDescriptor["Key"] ??= pluginDescriptor["PluginClassName"]?.ToString()?.ToLowerInvariant() ?? "plugin";
            pluginDescriptor["Version"] ??= "1.0.0";
            pluginDescriptor["Style"] ??= 1;
            pluginDescriptor["IsEnabled"] ??= true;
            pluginDescriptor["CodeFilePath"] ??= $"./{pluginDescriptor["PluginClassName"]?.ToString() ?? "PluginTemplate"}.cs";
            pluginDescriptor["DevelopmentMode"] ??= true;
            pluginDescriptor["AllowUnsafeApis"] ??= false;
        }

        private static bool IsTextPluginFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension is ".cs" or ".json" or ".xaml" or ".xml" or ".txt" or ".md" or ".config" or ".props" or ".targets";
        }

        private void SetEditorSyntax(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            switch (extension)
            {
                case ".cs":
                    aeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("C#");
                    break;
                case ".xml":
                case ".xaml":
                    aeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("XML");
                    break;
                case ".json":
                    aeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("JavaScript");
                    break;
                default:
                    aeEditor.SyntaxHighlighting = null;
                    break;
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateJsonTextBox();
        }

        #region 文件列表相关方法

        private void InitializePluginFiles()
        {
            // 清空现有文件列表
            pluginFiles.Clear();
            fileContents.Clear();
            originalFilePaths.Clear();

            // 创建根节点
            var rootItem = new PluginFileItem
            {
                Name = "插件文件",
                IsDirectory = true,
                IsExpanded = true,
                FullPath = "插件文件"
            };

            // 获取主文件名
            string className = pluginDescriptor["PluginClassName"]?.ToString() ?? "PluginTemplate";
            string mainFilePath = $"{className}.cs";

            // 添加默认主文件 - 只保留一个主代码文件
            var mainFileItem = new PluginFileItem
            {
                Name = mainFilePath,
                IsDirectory = false,
                Parent = rootItem,
                FullPath = mainFilePath
            };

            // 添加icon.json文件
            var iconJsonItem = new PluginFileItem
            {
                Name = "icon.json",
                IsDirectory = false,
                Parent = rootItem,
                FullPath = "icon.json"
            };

            rootItem.Children.Add(mainFileItem);
            rootItem.Children.Add(iconJsonItem);
            pluginFiles.Add(rootItem);

            // 设置默认内容
            fileContents[mainFilePath] = aeEditor.Text;
            fileContents["icon.json"] = "{}";
            currentFilePath = mainFilePath;

            // 绑定到TreeView
            FileTreeView.ItemsSource = pluginFiles;

            // 拖放事件
            FileTreeView.AllowDrop = false;
            //FileTreeView.PreviewDragOver += FileTreeView_PreviewDragOver;
            //FileTreeView.Drop += FileTreeView_Drop;
        }

        private void RefreshFileTree()
        {
            // 保存当前文件内容
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                fileContents[currentFilePath] = aeEditor.Text;
            }

            // 刷新TreeView
            FileTreeView.Items.Refresh();
        }

        private void FileTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileTreeView.SelectedItem is PluginFileItem selectedItem && !selectedItem.IsDirectory)
            {
                // 保存当前编辑的文件内容
                if (!string.IsNullOrEmpty(currentFilePath))
                {
                    fileContents[currentFilePath] = aeEditor.Text;
                }

                // 打开选中的文件
                currentFilePath = selectedItem.FullPath;

                // 如果文件内容不存在，创建空内容
                if (!fileContents.ContainsKey(currentFilePath))
                {
                    fileContents[currentFilePath] = string.Empty;
                }

                // 显示文件内容
                aeEditor.Text = fileContents[currentFilePath];

                // 根据文件类型设置语法高亮
                string extension = Path.GetExtension(currentFilePath).ToLower();
                switch (extension)
                {
                    case ".cs":
                        aeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("C#");
                        break;
                    case ".xml":
                    case ".xaml":
                        aeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("XML");
                        break;
                    case ".json":
                        aeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("JavaScript");
                        break;
                    default:
                        aeEditor.SyntaxHighlighting = null;
                        break;
                }
            }
        }

        //private void AddFileButton_Click(object sender, RoutedEventArgs e)
        //{
        //    // 获取当前选中的节点，如果没有选中则使用根节点
        //    PluginFileItem parentItem = FileTreeView.SelectedItem as PluginFileItem;
        //    if (parentItem == null)
        //    {
        //        parentItem = pluginFiles[0]; // 根节点
        //    }
        //    else if (!parentItem.IsDirectory)
        //    {
        //        parentItem = parentItem.Parent; // 如果选中的是文件，则使用其父目录
        //    }

        //    // 检查是否在根目录下添加文件
        //    if (parentItem == pluginFiles[0])
        //    {
        //        AppMessageBox.Show("根目录下不能添加其他文件，请使用右键菜单添加特定目录后再添加文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        //        return;
        //    }

        //    // 根据父目录类型确定允许的文件类型
        //    string fileFilter = "";
        //    string dialogTitle = "添加文件";

        //    if (parentItem.Name == "icons")
        //    {
        //        fileFilter = "图片文件|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.ico;*.svg";
        //        dialogTitle = "添加图标文件";
        //    }
        //    else if (parentItem.Name == "libs")
        //    {
        //        fileFilter = "DLL文件|*.dll";
        //        dialogTitle = "添加第三方库文件";
        //    }

        //    // 创建文件输入对话框
        //    var dialog = new InputDialog(dialogTitle, "请输入文件名：", "");
        //    if (dialog.ShowDialog() == true)
        //    {
        //        string fileName = dialog.ResponseText.Trim();

        //        // 验证文件名
        //        if (string.IsNullOrWhiteSpace(fileName))
        //        {
        //            AppMessageBox.Show("文件名不能为空", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //            return;
        //        }

        //        // 检查文件扩展名是否符合要求
        //        string extension = Path.GetExtension(fileName).ToLower();
        //        if (parentItem.Name == "icons" && !(extension == ".png" || extension == ".jpg" || extension == ".jpeg" ||
        //            extension == ".gif" || extension == ".bmp" || extension == ".ico" || extension == ".svg"))
        //        {
        //            AppMessageBox.Show("图标目录只能添加图片文件（.png, .jpg, .jpeg, .gif, .bmp, .ico, .svg）", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //            return;
        //        }
        //        else if (parentItem.Name == "libs" && extension != ".dll")
        //        {
        //            AppMessageBox.Show("第三方库目录只能添加DLL文件（.dll）", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //            return;
        //        }

        //        // 构建完整路径
        //        string fullPath = Path.Combine(parentItem.FullPath, fileName);

        //        // 检查文件是否已存在
        //        if (fileContents.ContainsKey(fullPath))
        //        {
        //            AppMessageBox.Show($"文件 '{fileName}' 已存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //            return;
        //        }

        //        // 添加新文件
        //        AddFileToList(fullPath, string.Empty);

        //        // 切换到新文件
        //        currentFilePath = fullPath;
        //        aeEditor.Text = string.Empty;

        //        // 根据文件类型设置语法高亮
        //        switch (extension)
        //        {
        //            case ".cs":
        //                aeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("C#");
        //                break;
        //            case ".xml":
        //            case ".xaml":
        //                aeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("XML");
        //                break;
        //            case ".json":
        //                aeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("JavaScript");
        //                break;
        //            default:
        //                aeEditor.SyntaxHighlighting = null;
        //                break;
        //        }
        //    }
        //}

        private void AddStyleFile_Click(object sender, RoutedEventArgs e)
        {
            // 获取根节点
            if (pluginFiles.Count > 0 && pluginFiles[0].IsDirectory)
            {
                var rootItem = pluginFiles[0];

                // 检查是否已存在style.json文件
                var existingStyleFile = rootItem.Children.FirstOrDefault(c => c.Name == "style.json" && !c.IsDirectory);
                if (existingStyleFile != null)
                {
                    AppMessageBox.Show("样式文件已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 创建style.json文件
                var styleFileItem = new PluginFileItem
                {
                    Name = "style.json",
                    IsDirectory = false,
                    Parent = rootItem,
                    FullPath = "style.json"
                };

                // 添加到根目录
                rootItem.Children.Add(styleFileItem);

                // 创建默认样式内容
                var defaultStyle = new JObject
                {
                    { "WindowPosition", new JObject
                        {
                            { "Left", 100 },
                            { "Top", 100 },
                            { "Width", 400 },
                            { "Height", 300 }
                        }
                    },
                    { "BackgroundColor", new JObject
                        {
                            { "R", 51 },
                            { "G", 51 },
                            { "B", 51 },
                            { "A", 255 }
                        }
                    },
                    { "Opacity", 0.8 },
                    { "CycleExecution", false },
                    { "Inteval", 1 }
                };

                // 添加默认内容
                fileContents["style.json"] = JsonConvert.SerializeObject(defaultStyle, Formatting.Indented);

                // 刷新文件树
                RefreshFileTree();

                // 切换到新文件
                currentFilePath = "style.json";
                aeEditor.Text = fileContents["style.json"];
                aeEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("JavaScript");
            }
        }

        private void DeleteFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (FileTreeView.SelectedItem is PluginFileItem selectedItem)
            {
                if (!selectedItem.IsDirectory)
                {
                    // 检查是否是主文件
                    string mainFilePath = GetMainFilePath();
                    if (selectedItem.FullPath == mainFilePath)
                    {
                        AppMessageBox.Show("不能删除主文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 确认删除文件
                    if (AppMessageBox.Show($"确定要删除文件 '{selectedItem.Name}' 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        // 从文件列表中移除
                        selectedItem.Parent.Children.Remove(selectedItem);

                        // 从内容字典中移除
                        if (fileContents.ContainsKey(selectedItem.FullPath))
                        {
                            fileContents.Remove(selectedItem.FullPath);
                        }

                        // 如果当前正在编辑该文件，切换到主文件
                        if (currentFilePath == selectedItem.FullPath)
                        {
                            currentFilePath = mainFilePath;
                            aeEditor.Text = fileContents[mainFilePath];
                        }

                        // 刷新文件树
                        RefreshFileTree();
                    }
                }
                else
                {
                    // 不允许删除根目录
                    if (selectedItem == pluginFiles[0])
                    {
                        AppMessageBox.Show("不能删除根目录", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 确认删除目录
                    if (AppMessageBox.Show($"确定要删除文件夹 '{selectedItem.Name}' 及其所有内容吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        // 删除目录下的所有文件
                        DeleteDirectoryContents(selectedItem);

                        // 从父节点中移除
                        selectedItem.Parent.Children.Remove(selectedItem);

                        // 刷新文件树
                        RefreshFileTree();
                    }
                }
            }
            else
            {
                AppMessageBox.Show("请选择要删除的文件或文件夹", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DeleteDirectoryContents(PluginFileItem directory)
        {
            // 递归删除所有子项
            foreach (var child in directory.Children.ToList())
            {
                if (child.IsDirectory)
                {
                    DeleteDirectoryContents(child);
                }
                else
                {
                    // 从内容字典中移除文件
                    if (fileContents.ContainsKey(child.FullPath))
                    {
                        fileContents.Remove(child.FullPath);
                    }

                    // 如果当前正在编辑该文件，切换到主文件
                    if (currentFilePath == child.FullPath)
                    {
                        string mainFilePath = GetMainFilePath();
                        currentFilePath = mainFilePath;
                        aeEditor.Text = fileContents[mainFilePath];
                    }
                }
            }

            // 清空子项列表
            directory.Children.Clear();
        }

        private void AddFileToList(string fileName, string content)
        {
            // 添加到内容字典
            fileContents[fileName] = content;

            // 添加到文件树
            if (pluginFiles.Count > 0 && pluginFiles[0].IsDirectory)
            {
                var rootItem = pluginFiles[0];

                // 处理可能的子目录结构
                string[] pathParts = fileName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                PluginFileItem currentParent = rootItem;
                string currentPath = "";

                // 如果有子目录，创建目录结构
                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    string dirName = pathParts[i];
                    currentPath = string.IsNullOrEmpty(currentPath) ? dirName : Path.Combine(currentPath, dirName);

                    // 查找或创建目录
                    var dirItem = currentParent.Children.FirstOrDefault(c => c.Name == dirName && c.IsDirectory);
                    if (dirItem == null)
                    {
                        dirItem = new PluginFileItem
                        {
                            Name = dirName,
                            IsDirectory = true,
                            IsExpanded = true,
                            Parent = currentParent,
                            FullPath = currentPath
                        };
                        currentParent.Children.Add(dirItem);
                    }

                    currentParent = dirItem;
                }

                // 获取实际文件名（路径的最后一部分）
                string actualFileName = pathParts[pathParts.Length - 1];

                // 检查文件是否已存在
                var existingFile = currentParent.Children.FirstOrDefault(f => f.Name == actualFileName && !f.IsDirectory);
                if (existingFile == null)
                {
                    var fileItem = new PluginFileItem
                    {
                        Name = actualFileName,
                        IsDirectory = false,
                        Parent = currentParent,
                        FullPath = fileName
                    };

                    currentParent.Children.Add(fileItem);
                    RefreshFileTree();
                }
            }
        }

        private string GetMainFilePath()
        {
            var codeFilePath = pluginDescriptor["CodeFilePath"]?.ToString();
            if (!string.IsNullOrWhiteSpace(codeFilePath))
            {
                return NormalizePluginRelativePath(codeFilePath);
            }

            string className = pluginDescriptor["PluginClassName"]?.ToString() ?? "Plugin1";
            return $"{className}.cs";
        }

        private static string NormalizePluginRelativePath(string path)
        {
            return path.Trim().TrimStart('.', '/', '\\').Replace('\\', '/');
        }

        // 为icons目录添加文件 - 使用文件选择对话框
        private void AddIconsFiles_Click(object sender, RoutedEventArgs e)
        {
            // 获取当前选中的节点
            PluginFileItem parentItem = FileTreeView.SelectedItem as PluginFileItem;
            if (parentItem == null || !parentItem.IsDirectory || parentItem.Name != "icons")
            {
                AppMessageBox.Show("请先选择图标目录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 创建文件选择对话框
            var dialog = new OpenFileDialog
            {
                Title = "选择图标文件",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.ico;*.svg",
                Multiselect = true // 允许多选
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (string filePath in dialog.FileNames)
                {
                    try
                    {
                        // 获取文件名
                        string fileName = Path.GetFileName(filePath);
                        string fullPath = Path.Combine(parentItem.FullPath, fileName);
                        string fileKey = Path.GetFileNameWithoutExtension(fileName);

                        // 检查文件是否已存在
                        if (fileContents.ContainsKey(fullPath))
                        {
                            AppMessageBox.Show($"文件 '{fileName}' 已存在，将被跳过", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                            continue;
                        }

                        // 记录原始文件路径，用于保存时直接复制
                        originalFilePaths[fileKey] = filePath;

                        // 创建新文件项
                        var fileItem = new PluginFileItem
                        {
                            Name = fileName,
                            IsDirectory = false,
                            Parent = parentItem,
                            FullPath = fullPath
                        };

                        // 添加到父目录
                        parentItem.Children.Add(fileItem);

                        // 添加一个占位符内容，实际保存时会直接复制原文件
                        fileContents[fullPath] = "[图片文件]";
                    }
                    catch (Exception ex)
                    {
                        AppMessageBox.Show($"添加文件 '{Path.GetFileName(filePath)}' 失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // 刷新文件树
                RefreshFileTree();
            }
        }

        // 为libs目录添加文件 - 使用文件选择对话框
        private void AddLibsFiles_Click(object sender, RoutedEventArgs e)
        {
            // 获取当前选中的节点
            PluginFileItem parentItem = FileTreeView.SelectedItem as PluginFileItem;
            if (parentItem == null || !parentItem.IsDirectory || parentItem.Name != "libs")
            {
                AppMessageBox.Show("请先选择第三方库目录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 创建文件选择对话框
            var dialog = new OpenFileDialog
            {
                Title = "选择DLL文件",
                Filter = "DLL文件|*.dll",
                Multiselect = true // 允许多选
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (string filePath in dialog.FileNames)
                {
                    try
                    {
                        // 获取文件名
                        string fileName = Path.GetFileName(filePath);
                        string fullPath = Path.Combine(parentItem.FullPath, fileName);

                        // 检查文件是否已存在
                        if (fileContents.ContainsKey(fullPath))
                        {
                            AppMessageBox.Show($"文件 '{fileName}' 已存在，将被跳过", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                            continue;
                        }

                        // 记录原始文件路径，用于保存时直接复制
                        originalFilePaths[fullPath] = filePath;

                        // 创建新文件项
                        var fileItem = new PluginFileItem
                        {
                            Name = fileName,
                            IsDirectory = false,
                            Parent = parentItem,
                            FullPath = fullPath
                        };

                        // 添加到父目录
                        parentItem.Children.Add(fileItem);

                        // 添加一个占位符内容，实际保存时会直接复制原文件
                        fileContents[fullPath] = "[DLL文件]";
                    }
                    catch (Exception ex)
                    {
                        AppMessageBox.Show($"添加文件 '{Path.GetFileName(filePath)}' 失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // 刷新文件树
                RefreshFileTree();
            }
        }

        // 用于存储图片和DLL文件的原始路径
        private Dictionary<string, string> originalFilePaths = new Dictionary<string, string>();

        private void SavePlugin(string folderPath)
        {
            try
            {
                // 确保路径存在
                if (string.IsNullOrEmpty(folderPath))
                {
                    AppMessageBox.Show("保存路径无效", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 创建插件目录
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // 保存当前编辑的文件内容
                if (!string.IsNullOrEmpty(currentFilePath))
                {
                    fileContents[currentFilePath] = aeEditor.Text;
                }

                // 验证主文件代码
                string mainFilePath = GetMainFilePath();
                string mainFileCode = fileContents.ContainsKey(mainFilePath) ? fileContents[mainFilePath] : "";
                string validationResult = ValidateCode(mainFileCode);

                if (!string.IsNullOrEmpty(validationResult))
                {
                    AppMessageBox.Show($"代码验证失败：\n{validationResult}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 确保style.json文件存在，如果不存在则创建默认样式文件
                if (!fileContents.ContainsKey("style.json"))
                {
                    // 创建默认样式文件
                    var defaultStyle = new JObject
                    {
                        { "WindowPosition", new JObject
                            {
                                { "Left", 100 },
                                { "Top", 100 },
                                { "Width", 400 },
                                { "Height", 300 }
                            }
                        },
                        { "BackgroundColor", new JObject
                            {
                                { "R", 51 },
                                { "G", 51 },
                                { "B", 51 },
                                { "A", 255 }
                            }
                        },
                        { "Opacity", 0.8 },
                        { "CycleExecution", false },
                        { "Inteval", 1 }
                    };
                    fileContents["style.json"] = JsonConvert.SerializeObject(defaultStyle, Formatting.Indented);
                }

                // 保存所有文件
                foreach (var file in fileContents)
                {
                    // 跳过plugin.json文件，因为它将从右侧插件描述中生成
                    if (file.Key == "plugin.json")
                    {
                        continue;
                    }
                    string filePath = Path.Combine(folderPath, file.Key);

                    // 确保目录存在（处理子目录情况）
                    string directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // 检查是否是图片或DLL文件
                    if (file.Key.ToLower().StartsWith("icons") || file.Key.ToLower().StartsWith("libs"))
                    {
                        string keyStr = Path.GetFileNameWithoutExtension(file.Key);
                        // 如果有原始文件路径，直接复制文件
                        if (originalFilePaths.ContainsKey(keyStr) && File.Exists(originalFilePaths[keyStr]))
                        {
                            File.Copy(originalFilePaths[keyStr], filePath, true);
                        }
                        else if (file.Value.StartsWith("[") && file.Value.EndsWith("]"))
                        {
                            // 占位符内容，但没有原始文件路径，跳过
                            continue;
                        }
                        else
                        {
                            // 尝试解码Base64内容并写入二进制文件（兼容旧版本）
                            try
                            {
                                byte[] fileBytes = Convert.FromBase64String(file.Value);
                                File.WriteAllBytes(filePath, fileBytes);
                            }
                            catch
                            {
                                // 如果解码失败，则作为文本写入
                                File.WriteAllText(filePath, file.Value);
                            }
                        }
                    }
                    else
                    {
                        // 普通文本文件
                        File.WriteAllText(filePath, file.Value);
                    }
                }

                // 保存插件描述文件 - 从右侧插件描述中获取
                // 更新CodeFilePath确保与主文件名一致
                string className = pluginDescriptor["PluginClassName"]?.ToString() ?? "Plugin1";
                pluginDescriptor["CodeFilePath"] = $"./{mainFilePath.Replace('\\', '/')}";

                string pluginJsonPath = Path.Combine(folderPath, "plugin.json");
                File.WriteAllText(pluginJsonPath, JsonConvert.SerializeObject(pluginDescriptor, Formatting.Indented));

                // 更新当前插件目录
                currentPluginDirectory = folderPath;

                // 创建ZIP文件
                string pluginName = pluginDescriptor["Name"]?.ToString() ?? "Plugin";
                string zipFilePath = Path.Combine(Path.GetDirectoryName(folderPath), $"{className}.plug");

                // 如果文件已存在，先删除
                if (File.Exists(zipFilePath))
                {
                    File.Delete(zipFilePath);
                }

                // 创建ZIP文件
                ZipFile.CreateFromDirectory(folderPath, zipFilePath);

                if (AppMessageBox.Show($"已成功生成插件,是否打开文件所在目录?", "保存成功", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    string directoryPath = Path.GetDirectoryName(zipFilePath);
                    if (!string.IsNullOrEmpty(directoryPath) && Directory.Exists(directoryPath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", directoryPath);
                    }
                }
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"保存插件失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region 文件拖放相关方法

        //private void FileTreeView_PreviewDragOver(object sender, DragEventArgs e)
        //{
        //    e.Effects = DragDropEffects.Copy;
        //    e.Handled = true;
        //}

        //private void FileTreeView_Drop(object sender, DragEventArgs e)
        //{
        //    if (e.Data.GetDataPresent(DataFormats.FileDrop))
        //    {
        //        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
        //        ImportFiles(files);
        //    }
        //}

        //private void ImportFiles(string[] filePaths)
        //{
        //    foreach (string filePath in filePaths)
        //    {
        //        try
        //        {
        //            if (File.Exists(filePath))
        //            {
        //                // 获取文件名
        //                string fileName = Path.GetFileName(filePath);

        //                // 读取文件内容
        //                string content = File.ReadAllText(filePath);

        //                // 添加到文件列表
        //                AddFileToList(fileName, content);
        //            }
        //            else if (Directory.Exists(filePath))
        //            {
        //                // 处理目录
        //                ImportDirectory(filePath, "");
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            AppMessageBox.Show($"导入文件失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //        }
        //    }
        //}

        //private void ImportDirectory(string dirPath, string relativePath)
        //{
        //    // 获取目录中的所有文件
        //    foreach (string filePath in Directory.GetFiles(dirPath))
        //    {
        //        try
        //        {
        //            // 获取文件名
        //            string fileName = Path.GetFileName(filePath);

        //            // 构建相对路径
        //            string relativeFilePath = string.IsNullOrEmpty(relativePath) ?
        //                fileName : Path.Combine(relativePath, fileName);

        //            // 读取文件内容
        //            string content = File.ReadAllText(filePath);

        //            // 添加到文件列表
        //            AddFileToList(relativeFilePath, content);
        //        }
        //        catch (Exception ex)
        //        {
        //            AppMessageBox.Show($"导入文件失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        //        }
        //    }

        //    // 递归处理子目录
        //    foreach (string subDirPath in Directory.GetDirectories(dirPath))
        //    {
        //        string dirName = Path.GetFileName(subDirPath);
        //        string newRelativePath = string.IsNullOrEmpty(relativePath) ?
        //            dirName : Path.Combine(relativePath, dirName);

        //        ImportDirectory(subDirPath, newRelativePath);
        //    }
        //}

        #endregion

        // 添加右键菜单功能
        public void InitializeContextMenu()
        {
            // 创建TreeView的右键菜单
            ContextMenu rootContextMenu = new ContextMenu();

            // 根目录菜单项 - 只保留添加特殊目录的选项
            MenuItem addIconsFolderMenuItem = new MenuItem() { Header = "添加图标目录" };
            addIconsFolderMenuItem.Click += AddIconsFolder_Click;

            MenuItem addLibsFolderMenuItem = new MenuItem() { Header = "添加第三方库目录" };
            addLibsFolderMenuItem.Click += AddLibsFolder_Click;

            MenuItem addStyleFileMenuItem = new MenuItem() { Header = "添加样式文件" };
            addStyleFileMenuItem.Click += AddStyleFile_Click;

            rootContextMenu.Items.Add(addIconsFolderMenuItem);
            rootContextMenu.Items.Add(addLibsFolderMenuItem);
            rootContextMenu.Items.Add(addStyleFileMenuItem);

            // 创建icons目录的右键菜单
            ContextMenu iconsContextMenu = new ContextMenu();

            MenuItem addIconsMenuItem = new MenuItem() { Header = "添加文件" };
            addIconsMenuItem.Click += AddIconsFiles_Click;

            // 添加到icons目录菜单
            iconsContextMenu.Items.Add(addIconsMenuItem);

            // 创建libs目录的右键菜单
            ContextMenu libsContextMenu = new ContextMenu();

            MenuItem addLibsMenuItem = new MenuItem() { Header = "添加文件" };
            addLibsMenuItem.Click += AddLibsFiles_Click;

            // 添加到libs目录菜单
            libsContextMenu.Items.Add(addLibsMenuItem);

            // 创建图标文件的右键菜单
            ContextMenu iconFileContextMenu = new ContextMenu();

            MenuItem deleteIconFileMenuItem = new MenuItem() { Header = "删除" };
            deleteIconFileMenuItem.Click += DeleteFileButton_Click;

            MenuItem renameIconFileMenuItem = new MenuItem() { Header = "重命名" };
            renameIconFileMenuItem.Click += RenameItem_Click;

            // 添加到图标文件菜单
            iconFileContextMenu.Items.Add(deleteIconFileMenuItem);
            iconFileContextMenu.Items.Add(renameIconFileMenuItem);

            // 创建库文件的右键菜单
            ContextMenu libFileContextMenu = new ContextMenu();

            MenuItem deleteLibFileMenuItem = new MenuItem() { Header = "删除" };
            deleteLibFileMenuItem.Click += DeleteFileButton_Click;

            MenuItem renameLibFileMenuItem = new MenuItem() { Header = "重命名" };
            renameLibFileMenuItem.Click += RenameItem_Click;

            // 添加到库文件菜单
            libFileContextMenu.Items.Add(deleteLibFileMenuItem);
            libFileContextMenu.Items.Add(renameLibFileMenuItem);

            // 设置到TreeView - 使用事件动态设置右键菜单
            FileTreeView.ContextMenuOpening += (sender, e) =>
            {
                if (FileTreeView.SelectedItem is PluginFileItem selectedItem)
                {
                    // 根据选中项类型设置不同的右键菜单
                    if (selectedItem == pluginFiles[0]) // 根节点
                    {
                        FileTreeView.ContextMenu = rootContextMenu;
                    }
                    else if (selectedItem.IsDirectory && selectedItem.Name == "icons")
                    {
                        FileTreeView.ContextMenu = iconsContextMenu;
                    }
                    else if (selectedItem.IsDirectory && selectedItem.Name == "libs")
                    {
                        FileTreeView.ContextMenu = libsContextMenu;
                    }
                    else if (!selectedItem.IsDirectory) // 文件项
                    {
                        // 检查是否是主文件或icon.json，如果是则不显示右键菜单
                        string mainFilePath = GetMainFilePath();
                        if (selectedItem.FullPath == mainFilePath || selectedItem.Name == "icon.json")
                        {
                            e.Handled = true;
                            FileTreeView.ContextMenu = null;
                        }
                        else if (selectedItem.Parent != null && selectedItem.Parent.Name == "icons")
                        {
                            // 图标文件显示删除和重命名菜单
                            FileTreeView.ContextMenu = iconFileContextMenu;
                        }
                        else if (selectedItem.Parent != null && selectedItem.Parent.Name == "libs")
                        {
                            // 库文件显示删除和重命名菜单
                            FileTreeView.ContextMenu = libFileContextMenu;
                        }
                        else
                        {
                            // 其他文件不显示右键菜单
                            e.Handled = true;
                            FileTreeView.ContextMenu = null;
                        }
                    }
                    else
                    {
                        // 其他项不显示右键菜单
                        e.Handled = true;
                        FileTreeView.ContextMenu = null;
                    }
                }
                else
                {
                    // 未选中项时使用根目录菜单
                    FileTreeView.ContextMenu = rootContextMenu;
                }
            };
        }

        private void AddIconsFolder_Click(object sender, RoutedEventArgs e)
        {
            // 检查根节点是否已存在icons目录
            var rootItem = pluginFiles[0]; // 根节点
            var existingFolder = rootItem.Children.FirstOrDefault(f => f.Name == "icons" && f.IsDirectory);
            if (existingFolder != null)
            {
                AppMessageBox.Show("图标目录已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 创建新的icons目录节点
            var folderItem = new PluginFileItem
            {
                Name = "icons",
                IsDirectory = true,
                IsExpanded = true,
                Parent = rootItem,
                FullPath = "icons"
            };

            rootItem.Children.Add(folderItem);
            RefreshFileTree();
            AppMessageBox.Show("图标目录已创建，此目录仅支持添加图片文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddLibsFolder_Click(object sender, RoutedEventArgs e)
        {
            // 检查根节点是否已存在libs目录
            var rootItem = pluginFiles[0]; // 根节点
            var existingFolder = rootItem.Children.FirstOrDefault(f => f.Name == "libs" && f.IsDirectory);
            if (existingFolder != null)
            {
                AppMessageBox.Show("第三方库目录已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 创建新的libs目录节点
            var folderItem = new PluginFileItem
            {
                Name = "libs",
                IsDirectory = true,
                IsExpanded = true,
                Parent = rootItem,
                FullPath = "libs"
            };

            rootItem.Children.Add(folderItem);
            RefreshFileTree();
            AppMessageBox.Show("第三方库目录已创建，此目录仅支持添加dll文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateMainFileNameInTree(string oldClassName, string newClassName)
        {
            // 获取旧的和新的文件路径
            string oldFilePath = $"{oldClassName}.cs";
            string newFilePath = $"{newClassName}.cs";

            // 查找文件树中的主文件项
            if (pluginFiles.Count > 0 && pluginFiles[0].IsDirectory)
            {
                var rootItem = pluginFiles[0];
                var mainFileItem = rootItem.Children.FirstOrDefault(f => f.Name == oldFilePath && !f.IsDirectory);

                if (mainFileItem != null)
                {
                    // 更新文件内容字典
                    if (fileContents.ContainsKey(oldFilePath))
                    {
                        string content = fileContents[oldFilePath];
                        fileContents.Remove(oldFilePath);
                        fileContents[newFilePath] = content;

                        // 如果当前正在编辑该文件，更新当前文件路径
                        if (currentFilePath == oldFilePath)
                        {
                            currentFilePath = newFilePath;
                        }
                    }

                    // 更新文件项
                    mainFileItem.Name = newFilePath;
                    mainFileItem.FullPath = newFilePath;

                    // 刷新文件树
                    RefreshFileTree();
                }
            }
        }

        private void RenameItem_Click(object sender, RoutedEventArgs e)
        {
            if (FileTreeView.SelectedItem is PluginFileItem selectedItem)
            {
                // 检查是否是主文件
                string mainFilePath = GetMainFilePath();
                if (!selectedItem.IsDirectory && selectedItem.FullPath == mainFilePath)
                {
                    AppMessageBox.Show("不能重命名主文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 创建重命名对话框
                var dialog = new InputDialog("重命名", "请输入新名称：", selectedItem.Name);
                if (dialog.ShowDialog() == true)
                {
                    string newName = dialog.ResponseText.Trim();

                    // 验证名称
                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        AppMessageBox.Show("名称不能为空", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 检查名称是否已存在
                    var existingItem = selectedItem.Parent.Children.FirstOrDefault(f => f.Name == newName);
                    if (existingItem != null && existingItem != selectedItem)
                    {
                        AppMessageBox.Show($"'{newName}' 已存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 如果是文件，需要更新文件内容字典
                    if (!selectedItem.IsDirectory)
                    {
                        string oldPath = selectedItem.FullPath;
                        string parentPath = selectedItem.Parent.FullPath == "插件文件" ? "" : selectedItem.Parent.FullPath;
                        string newPath = string.IsNullOrEmpty(parentPath) ? newName : Path.Combine(parentPath, newName);

                        // 更新文件内容字典
                        if (fileContents.ContainsKey(oldPath))
                        {
                            string content = fileContents[oldPath];
                            fileContents.Remove(oldPath);
                            fileContents[newPath] = content;

                            // 如果当前正在编辑该文件，更新当前文件路径
                            if (currentFilePath == oldPath)
                            {
                                currentFilePath = newPath;
                            }
                        }

                        // 更新originalFilePaths字典（针对icons和libs目录下的文件）
                        if (originalFilePaths.ContainsKey(oldPath))
                        {
                            string originalPath = originalFilePaths[oldPath];
                            originalFilePaths.Remove(oldPath);
                            originalFilePaths[newPath] = originalPath;
                        }
                        // 针对icons目录下的文件，还需要更新fileKey
                        else if (selectedItem.Parent != null && selectedItem.Parent.Name == "icons")
                        {
                            string oldFileKey = Path.GetFileNameWithoutExtension(oldPath);
                            string newFileKey = Path.GetFileNameWithoutExtension(newPath);
                            if (originalFilePaths.ContainsKey(oldFileKey))
                            {
                                string originalPath = originalFilePaths[oldFileKey];
                                originalFilePaths.Remove(oldFileKey);
                                originalFilePaths[newFileKey] = originalPath;
                            }
                        }

                        // 更新文件路径
                        selectedItem.FullPath = newPath;
                    }
                    else
                    {
                        // 如果是目录，需要更新所有子项的路径
                        string oldPath = selectedItem.FullPath;
                        string parentPath = selectedItem.Parent.FullPath == "插件文件" ? "" : selectedItem.Parent.FullPath;
                        string newPath = string.IsNullOrEmpty(parentPath) ? newName : Path.Combine(parentPath, newName);

                        // 更新目录路径
                        selectedItem.FullPath = newPath;

                        // 更新所有子项的路径
                        UpdateChildrenPaths(selectedItem, oldPath, newPath);
                    }

                    // 更新名称
                    selectedItem.Name = newName;
                    RefreshFileTree();
                }
            }
        }

        private void UpdateChildrenPaths(PluginFileItem item, string oldBasePath, string newBasePath)
        {
            foreach (var child in item.Children)
            {
                // 更新子项的路径
                string relativePath = child.FullPath.Substring(oldBasePath.Length);
                if (relativePath.StartsWith("/") || relativePath.StartsWith("\\"))
                {
                    relativePath = relativePath.Substring(1);
                }

                string oldPath = child.FullPath;
                string newPath = Path.Combine(newBasePath, relativePath);
                child.FullPath = newPath;

                // 如果是文件，更新文件内容字典
                if (!child.IsDirectory)
                {
                    if (fileContents.ContainsKey(oldPath))
                    {
                        string content = fileContents[oldPath];
                        fileContents.Remove(oldPath);
                        fileContents[newPath] = content;

                        // 如果当前正在编辑该文件，更新当前文件路径
                        if (currentFilePath == oldPath)
                        {
                            currentFilePath = newPath;
                        }
                    }

                    // 更新originalFilePaths字典（针对icons和libs目录下的文件）
                    if (originalFilePaths.ContainsKey(oldPath))
                    {
                        string originalPath = originalFilePaths[oldPath];
                        originalFilePaths.Remove(oldPath);
                        originalFilePaths[newPath] = originalPath;
                    }
                    // 针对icons目录下的文件，还需要更新fileKey
                    else if (item.Name == "icons" || oldBasePath.Contains("icons"))
                    {
                        string oldFileKey = Path.GetFileNameWithoutExtension(oldPath);
                        string newFileKey = Path.GetFileNameWithoutExtension(newPath);
                        if (originalFilePaths.ContainsKey(oldFileKey))
                        {
                            string originalPath = originalFilePaths[oldFileKey];
                            originalFilePaths.Remove(oldFileKey);
                            originalFilePaths[newFileKey] = originalPath;
                        }
                    }
                }

                // 递归处理子目录
                if (child.IsDirectory)
                {
                    UpdateChildrenPaths(child, oldPath, newPath);
                }
            }
        }


        #endregion
    }

    // 插件文件项类
    public class PluginFileItem
    {
        public string Name { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsExpanded { get; set; }
        public PluginFileItem Parent { get; set; }
        public string FullPath { get; set; }
        public ObservableCollection<PluginFileItem> Children { get; set; } = new ObservableCollection<PluginFileItem>();
    }

    // 输入对话框
    public class InputDialog : Window
    {
        private TextBox inputTextBox;

        public string ResponseText { get; private set; }

        public InputDialog(string title, string promptText, string defaultValue)
        {
            Title = title;
            Width = 350;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            var promptLabel = new TextBlock
            {
                Text = promptText,
                Margin = new Thickness(10, 10, 10, 5)
            };
            grid.Children.Add(promptLabel);
            Grid.SetRow(promptLabel, 0);

            inputTextBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(10, 5, 10, 10)
            };
            grid.Children.Add(inputTextBox);
            Grid.SetRow(inputTextBox, 1);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var okButton = new Button
            {
                Content = "确定",
                Width = 75,
                Height = 23,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) => { DialogResult = true; };

            var cancelButton = new Button
            {
                Content = "取消",
                Width = 75,
                Height = 23,
                IsCancel = true
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            grid.Children.Add(buttonPanel);
            Grid.SetRow(buttonPanel, 2);

            Content = grid;

            inputTextBox.SelectAll();
            inputTextBox.Focus();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            ResponseText = inputTextBox.Text;
            base.OnClosing(e);
        }
    }
}
