# Layouter 开发计划

## 项目结构
- **项目名称**: Layouter
- **主要模块**:
  - **DesktopManager**: 负责管理桌面分区和图标。
  - **IconManager**: 负责管理桌面图标的加载、保存和恢复。
  - **PartitionManager**: 负责管理分区的创建、删除和交互。
  - **SettingsManager**: 负责管理用户设置和配置。
  - **TrayIconManager**: 负责管理托盘图标及其右键菜单。
  - **PluginManager**: 负责管理插件的加载和执行。

## 功能模块
- **DesktopManager**:
  - 创建、删除分区。
  - 管理分区与桌面图标的交互（拖进/拖出）。
  - 支持分区的自动排列或对齐功能。
- **IconManager**:
  - 加载桌面图标。
  - 保存图标的精确位置。
  - 恢复图标到上次保存的状态。
- **PartitionManager**:
  - 支持在分区内右键关联新程序或文件。
  - 支持添加特殊快捷方式或图标。
- **SettingsManager**:
  - 管理分区的UI设置（背景色、标题颜色、透明度等）。
  - 保存和加载用户配置。
- **TrayIconManager**:
  - 显示托盘图标。
  - 提供右键菜单，支持新建分区、调整配置等功能。
- **PluginManager**:
  - 加载插件。
  - 执行插件功能（如通过快捷键打开程序或功能）。

## 数据存储
- 使用JSON文件保存用户配置和图标位置。
- 使用WPF的本地存储功能保存分区状态。
- 插件配置文件（如快捷键映射）也使用JSON格式保存。

## 用户界面
- 分区支持自定义背景色、标题颜色、透明度等。
- 托盘图标右键菜单支持主要设置功能。
- 插件管理界面，允许用户配置快捷键和插件功能。

## 技术栈
- **语言**: C#
- **框架**: WPF
- **MVVM**: CommunityToolkit.Mvvm

## Mermaid图表

```mermaid
graph TD
    A[Layouter] --> B[DesktopManager]
    A --> C[IconManager]
    A --> D[PartitionManager]
    A --> E[SettingsManager]
    A --> F[TrayIconManager]
    A --> G[PluginManager]
    
    B --> H[创建分区]
    B --> I[删除分区]
    B --> J[分区交互]
    B --> K[自动排列/对齐]
    
    C --> L[加载图标]
    C --> M[保存图标位置]
    C --> N[恢复图标]
    
    D --> O[右键关联]
    D --> P[添加特殊快捷方式]
    
    E --> Q[UI设置]
    E --> R[保存配置]
    E --> S[加载配置]
    
    F --> T[托盘图标]
    F --> U[右键菜单]
    
    G --> V[加载插件]
    G --> W[执行插件功能]