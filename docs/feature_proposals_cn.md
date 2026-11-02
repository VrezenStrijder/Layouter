# Layouter 功能提案与竞品对标

> **文档类型**：产品设计提案（仅设计，不含实现）
> **对标对象**：Stardock Fences 6（主）、Stardock Groupy 2（次）、Windows 11 Snap Layouts / Snap Groups（次）
> **撰写日期**：2026-10-27
> **适用版本**：Layouter 2.0.0（`docs/changelog.md` 最新条目）/ `Layouter.csproj` `Version 1.0.5`
> **英文版**：[feature_proposals.md](./feature_proposals.md)

---

## 目录

- [1. 背景与文档定位](#1-背景与文档定位)
- [2. 竞品对标：Stardock Fences 6](#2-竞品对标stardock-fences-6)
  - [2.1 Fences 6 的版本节奏](#21-fences-6-的版本节奏)
  - [2.2 Fences 6 功能全景](#22-fences-6-功能全景)
  - [2.3 次要对标：Groupy 2 与 Windows 11 Snap](#23-次要对标groupy-2-与-windows-11-snap)
  - [2.4 差距总览](#24-差距总览)
- [3. Layouter 现状盘点](#3-layouter-现状盘点)
  - [3.1 已经具备的能力](#31-已经具备的能力)
  - [3.2 结构性短板](#32-结构性短板)
- [4. 功能提案](#4-功能提案)
  - [4.1 优先级总表](#41-优先级总表)
  - [4.2 P0：近期（1–2 个版本）](#42-p0近期1-2-个版本)
  - [4.3 P1：中期](#43-p1中期)
  - [4.4 P2：远期](#44-p2远期)
- [5. UI / 视觉美化提案](#5-ui--视觉美化提案)
  - [5.1 设计令牌基线](#51-设计令牌基线)
  - [5.2 桌面分区窗口](#52-桌面分区窗口)
  - [5.3 管理窗口](#53-管理窗口)
  - [5.4 托盘菜单](#54-托盘菜单)
  - [5.5 插件窗口](#55-插件窗口)
- [6. 实施路线图](#6-实施路线图)
  - [6.1 近期（1–2 个版本）](#61-近期1-2-个版本)
  - [6.2 中期](#62-中期)
  - [6.3 远期](#63-远期)
  - [6.4 暂不建议](#64-暂不建议)
- [7. 参考资料](#7-参考资料)

---

## 1. 背景与文档定位

Layouter 是一款基于 .NET / WPF 的 Windows 桌面分区整理工具，核心能力与 Stardock 的 Fences 处于同一象限：在桌面上创建可移动、带标题栏样式的半透明"分区"，把桌面图标收进分区里，并提供按分区定制外观、管理面板、托盘菜单与插件系统。

两者定位接近，但路线明显不同：

| 维度 | Stardock Fences 6 | Layouter |
|---|---|---|
| 商业模式 | 闭源商业软件，分个人版与企业版 | 开源免费（Apache-2.0） |
| 技术栈 | 原生 Win32/C++ | .NET 6 + WPF + MVVM（CommunityToolkit.Mvvm） |
| 核心资产 | 15 年 Shell/桌面层面的兼容性积累 | 轻量、安静、可扩展，插件系统是差异化点 |
| 更新节奏 | 商业级，近 12 个月发布 6.46 / 6.52 / 6.54 / 6.58 | 单人维护，版本跨度大（1.0.0 → 2.0.0） |

**本文档的目的不是"把 Layouter 做成 Fences"**，而是回答三个具体问题：

1. Fences 6 相比 Layouter 到底领先在哪里，哪些是 Layouter 应当补齐的**基础能力缺口**（低垂果实），哪些是商业软件的规模效应（不值得追）。
2. 在保持"简洁安静"定位的前提下，Layouter 有哪些**自己的差异化方向**可以反过来形成优势。
3. 视觉层如何统一到 `Layouter/Styles/Theme.xaml` + `AppControls.xaml` 的令牌体系，让 UI 在新增功能时不继续腐化。

工作量的估算口径：S ≈ 1–2 人日，M ≈ 3–7 人日，L ≈ 1–3 周（以单人熟悉代码库计）。价值分高／中／低，指对"让用户愿意把 Layouter 当主力桌面整理工具"的贡献度。

> **关于 Fences 版本号的说明**：任务原始描述里提到"Fences 2 / 2024+ 版本"。实际调研结果是：截至 2026-10，Fences 的产品线已经是 **Fences 6**（6.0 于 2025-05-28 正式发布，当前最新为 **6.58，发布于 2026-10-14**）。本文档一律以 Fences 6 为对标基准。

---

## 2. 竞品对标：Stardock Fences 6

### 2.1 Fences 6 的版本节奏

Stardock 帮助中心的 Changelogs 索引显示，Fences 6 的发布节奏大致如下（仅列出已核实条目）：

| 版本 | 发布日期 | 关键内容 |
|---|---|---|
| 6.0（正式版） | 2025-05-28 | Fences 6 正式发布；标签颜色默认开启；修复 Peek 性能问题；支持把图标拖到标签页头部 |
| 6.20（UI）/ 6.2.0.1（可执行） | 2025-12-09 | 折叠分组新增"需点击展开""需悬停展开"两种触发方式；边缘等距吸附体验增强；自动化规则新增"图标名称完全匹配"；多键盘布局检测（避免 Peek 默认热键 Win+Space 抢占输入法切换键） |
| 6.46 | 2026-04-27 | 多显示器处理细化（切换显示器配置时图标与分组保持原位）；网络盘 Folder Portal 启动性能；DPI 支持提升至 400%；图标着色配置改进；Peek 图标可显示在任务栏 |
| 6.48 | 2026-06-04 | 修复开启图标位移动画时右键桌面导致图标抖动 |
| 6.52 | 2026-08-04 | 修复 Large 图标模式下重叠间距问题；DPI 变化处理改进 |
| 6.54 | 2026-08-18 | Peek 支持左右侧任务栏；修复拖动分组时的掉帧 |
| 6.58 | 2026-10-14 | 新的桌面间距检测算法（默认对所有图标尺寸启用）；折叠分组滚动条动画改进；修复折叠分组内图标拖放失败 |

**从节奏能读出的三点结论：**

1. Fences 6 的迭代重心已从"加功能"转向"**修细节**"——DPI、多显示器、动画帧率、图标间距、滚动条行为。这些恰恰是最消耗支持工单的类型，也是小团队最容易踩坑的地方。
2. **多显示器 + 高 DPI** 在 Fences 的更新日志里反复出现，说明这是桌面工具的硬骨头，而不是可选项。
3. Stardock 在 6.x 期间持续为 Fences 追加**企业/脚本化**能力（帮助中心同时提供 AltTabby、Clairvoyance 等条目），但这些属于另一个产品线。

### 2.2 Fences 6 功能全景

以下功能在 Fences 6 产品页与上述变更日志中均得到确认：

**分区（核心）**
- Fence 分组：桌面上的半透明区域，容纳桌面图标；可调尺寸、颜色、透明度
- 分组锁定：固定位置不移动（产品页在无障碍场景中明确提到"lock their location so that they never move"）
- 快速隐藏分区以获得无干扰桌面
- 调整大小时按住 Shift 可临时禁用吸附

**Peek™**
- 默认热键 `Win + Space`，把所有分区临时置于所有窗口之上，用于快速取用桌面图标
- 可选把 Peek 图标显示在任务栏（6.46 变更日志中的 "Show in Taskbar" 修复项证实）
- 6.54 起支持任务栏位于左右侧

**Folder Portals™**
- 把任意目录镜像到桌面上作为一个分区，支持本地目录、网络驱动器、云盘占位目录
- 提供"列表""水平列表"等展示样式；可隐藏标题栏上的文件夹图标
- 右键选择"打开"时在新资源管理器窗口打开（即使开启了"导航"模式）

**Tabs（标签页）**
- 一个分区内可开多个标签页，每页持有独立的一组图标
- 标签颜色默认开启；可把图标直接拖到标签页头部
- 标签可从标签组中"分离"（undock）；每组自定义的标题字体与颜色会应用到其标签页

**Chameleon™ 与 Icon Tint**
- Chameleon：让桌面图标按主色"融进"壁纸，达到背景融合效果
- Icon Tint：给图标统一着色，使不同来源的图标视觉上更一致

**Roll-up（折叠分组）**
- 分组可折叠为只剩标题栏
- 6.2 起支持两种触发方式："需点击展开"与"需悬停展开"
- 6.58 改进折叠分组的滚动条动画

**对齐吸附**
- 分组放置到显示器边缘附近时的对齐吸附，并支持"固定间距"模式

**自动化规则**
- 基于条件的规则引擎：按文件类型／名称／时间排序，把图标移动到目标位置
- 6.2 新增"图标名称完全匹配"规则；新增桌面目录变更的智能检测

**其他**
- 调整大小的吸附可用 Shift 临时关闭
- 支持简体中文本地化
- 企业场景：网络驱动器部署、内容锁定、可脚本化的清理

**未在本次调研中验证到的 Fences 能力**（不下断言，仅记录为待确认）：

- 是否有"Tab 键在分区之间循环切换"的键盘导航
- 是否有完整意义上的"任务栏／停靠集成"（仅验证到 Peek 图标可上任务栏）
- 对移动硬盘／外接盘分区的专门支持（仅验证到 Folder Portal 支持网络驱动器）
- 聊天软件／通知弹窗与分区的集成
- "自定义桌面"（虚拟桌面）概念

**关于 AI**：Stardock 确有独立产品线做 AI（DesktopGPT、Clairvoyance AI Staff），但**Fences 本身没有 AI 功能**。这一点在竞品分析里很关键——AI 不是 Fences 的卖点，Layouter 也不必把它当成对标必答题（详见 §6.4）。

### 2.3 次要对标：Groupy 2 与 Windows 11 Snap

**Stardock Groupy 2** —— 与 Fences 同期但解决的问题不同：把多个应用窗口拖到一起，合并到一个统一的标签栏下。关键能力：兼容窗口把标签集成进标题栏；自定义标题栏的窗口则把标签显示在标题栏上方；鼠标移到未分组窗口标题栏顶部时弹出 Groupy 工具条；提供分组设置页（热键、Explorer 增强、"在所有窗口显示添加标签按钮"等）。

**对 Layouter 的启示**：Groupy 处理的是"应用窗口"，Fences 处理的是"桌面图标容器"。两者的标签交互高度相似（标签着色、拖放、分离）。**Layouter 未来若做分区标签页（F-17），交互模型可以直接借鉴 Groupy 2 的标签条设计**，而不是从零发明。

**Windows 11 Snap Layouts / Snap Groups** —— 系统自带能力：悬停窗口最大化按钮或按 `Win + Z` 呼出布局浮层；把窗口拖到屏幕顶部也能呼出；选中一个分区后用 Snap Assist 填充其余分区；Snap Groups 则能保存一组窗口的布局，之后一键恢复。

**对 Layouter 的启示**：Windows 11 的 Snap Groups 解决了"我今天开了一组窗口，想原样恢复"的需求——这正是 Layouter 的**布局快照（F-09）**想解决但针对桌面图标的版本。两者理念一致，Snap Groups 的存在说明这个需求真实且已被验证。

### 2.4 差距总览

| 能力域 | Fences 6 | Layouter 现状 | 差距判定 |
|---|---|---|---|
| 分区容器 + 图标收纳 | ✅ | ✅ | 已对齐 |
| 分区外观定制（标题栏/透明度/图标尺寸/对齐） | ✅ | ✅（`PartitionSettings`） | 已对齐 |
| 分区折叠（Roll-up） | ✅（可配置触发方式） | ⚠️ 有 `IsRolledUp` 字段，交互待打磨 | 需细化 |
| 分区显隐 | ✅ | ⚠️ 只能在分区管理窗口里勾选 | **缺口** |
| 快捷键 | ✅（Peek 默认 Win+Space，且做多键盘布局检测） | ❌ 无热键体系 | **缺口** |
| Peek / 桌面置顶预览 | ✅ | ❌ | **缺口** |
| 拖拽吸附 + 等距参考线 | ✅ | ⚠️ 仅有"自动捕捉"的只言片语 | **缺口** |
| 多显示器 | ✅（持续投入） | ❌ 仅用 `SystemParameters.WorkArea` | **缺口** |
| 高 DPI（>200%） | ✅（支持至 400%） | ❌ 未见处理 | **缺口** |
| 标签页 Tabs | ✅ | ❌ | 缺口（远期） |
| 文件夹门户 | ✅ | ❌ | 缺口（远期） |
| 图标着色 / 背景融合 | ✅ | ❌ | 缺口（中期） |
| 自动化规则 | ✅ | ❌ | 缺口（远期） |
| 插件系统 | ❌ | ✅ | **Layouter 独有优势** |
| 内置代码编辑器 | ❌ | ✅ | **Layouter 独有优势** |

**结论**：Layouter 在"分区本体"上与 Fences 差距不大，真正的差距集中在**桌面级的交互效率**（快捷键、显隐、Peek、吸附、多屏、DPI），以及**分区内容的组织方式**（Tabs、Folder Portal）。前者是必须补的基础能力，后者才是拉开差距的功能。

---

## 3. Layouter 现状盘点

### 3.1 已经具备的能力

阅读代码后确认的现有能力（作为提案的基础）：

| 能力 | 落点 |
|---|---|
| 桌面级分区窗口 | `Views/DesktopManagerWindow.xaml`，通过 `SysUtil.SetDesktopLevelWindow` 置为桌面层 |
| 分区样式定制 | `Models/PartitionSettings.cs`：`TitleForeground` / `TitleBackground` / `TitleFont` / `TitleFontSize` / `TitleAlignment` / `Opacity` / `IconSize` / `IconTextSize` / `IsLocked` / `IsRolledUp` |
| 全局样式 | `Models/GlobalPartitionSettings`、`GeneralSettings.EnableGlobalStyle`、`WindowManagerService.UpdateAllWindowStyles()` |
| 分区数据持久化 | `Services/PartitionDataService.cs`（`PartitionData` / `PartitionMetadata`），按 `windowId` 归档 |
| 桌面图标枚举与隐藏 | `Services/DesktopIconService.cs`，配合 `GeneralSettings.HiddenStoreDirectory` |
| 一键排列分区 | `WindowManagerService.ArrangeWindows()` / `ArrangeAllPartitionWindows()`（按 √N 网格均分工作区） |
| 分区批量管理 | `PartitionManagerViewModel`：`ToggleSelectAll` / `ToggleVisibilitySelected` / `DeleteSelected` / `EditGlobalStyle` |
| 托盘菜单 | `Services/TrayMenuService.cs`：新建分区／分区管理／开机自启动／插件／关于／退出 |
| 内置更新 | `Views/UpdateWindow.xaml` + `Services/UpdateService`（版本列表 + 多版本选择下载） |
| 插件系统 | `Plugins/PluginManager.cs`、`Plugins/PluginLoader.cs`、内置代码编辑器（`Plugins/Controls/CodeEditor.xaml`，AvalonEdit） |
| Shell 互操作 | `Utility/ShellUtil.cs`、`Utility/ShortcutUtil.cs` |
| 视觉令牌雏形 | `Styles/Theme.xaml`、`Styles/AppControls.xaml`、`Styles/TrayIconResources.xaml`，已有 `AppPageTitleText`、`ColorSwatchStyle`、`TrayMenuStyle` 等资源键 |

### 3.2 结构性短板

从代码里能直接看出的、会影响后续功能实现的结构性问题：

1. **缺少热键层**。`Utility/SysUtil.cs` 目前只有 `GetWindowHandle` / `SetDesktopLevelWindow` / `IsWindows11` / `MeasureText`，没有任何 `RegisterHotKey` 封装。所有"快捷键类"提案都需要先补这一层。
2. **多显示器被隐式忽略**。`WindowManagerService.ArrangeAllPartitionWindows()` 直接用 `SystemParameters.WorkArea`（主屏），`PartitionData` 里也没有记录分区归属显示器的字段。跨屏场景目前是未定义行为。
3. **DPI 未处理**。目标框架是 `net6.0-windows7.0`，没有任何 `PerMonitorV2` 声明或 DPI 变更响应。Fences 为此专门迭代了大半年。
4. **可见性模型分散**。`GeneralSettings.PartitionVisibility` 存了字典，但唯一的 UI 入口是 `PartitionManagerWindow`；托盘与快捷键都没有直接入口。
5. **样式是"每分区一份值"，不是"令牌 + 覆盖"**。`PartitionSettings` 存的是绝对色值（`Color` 类型），没有"跟随全局 / 局部覆盖"的分层语义。批量改样式只能整体赋值。
6. **版本与文档不一致**。`docs/readme_cn.md` 写"基于 .net 8 开发"，而 `Layouter.csproj` 的 `TargetFramework` 是 `net6.0-windows7.0`；`docs/changelog.md` 最新为 `2.0.0 (2026-06-03)`，而 csproj 里 `Version` / `AssemblyVersion` 仍是 `1.0.5`。这会直接影响用户升级判断与 issue 排查。
7. **技术债可见**。`Layouter.csproj` 里用约 40 条 `<Compile Remove>` / `<Page Remove>` 排除了 `Plugins/Deprecated/**`、`Services/ConfigurationService.cs`、`Views/Feature1Window.xaml` 等历史文件。功能继续增长前需要一次清理。
8. **可访问性基线缺失**。分区标题栏上的按钮没有 `AutomationProperties` 标注，没有键盘焦点样式。Fences 6.46 专门"改进了自定义面板中标题栏样式选项的无障碍"，说明这一块是专业软件的基线要求。

---

## 4. 功能提案

### 4.1 优先级总表

| 编号 | 提案 | 一句话说明 | 工作量 | 价值 | 优先级 |
|---|---|---|---|---|---|
| F-01 | 分区快速显隐 + 专注模式 | 托盘与快捷键一键收起全部分区，或单独切换某个分区 | S | 高 | P0 |
| F-02 | 托盘分区快捷面板 | 托盘菜单直接列出所有分区，支持勾选显隐 | S | 高 | P0 |
| F-03 | 全局快捷键框架 | 新增可配置的全局热键服务，带冲突检测 | M | 高 | P0 |
| F-04 | 分区拖拽吸附与等距参考线 | 拖动时贴边、贴邻边、贴等距位置，显示 1px 参考线 | M | 高 | P0 |
| F-05 | 一键布局预设 | 网格／横向／纵向／主次四种布局一键切换 | M | 中 | P0 |
| F-06 | 文档与版本号一致性治理 | 统一 readme、changelog、csproj 的版本与框架表述 | S | 中 | P0 |
| F-07 | Peek 桌面预览模式 | 热键把所有分区临时置于所有窗口之上，再按一次还原 | M | 高 | P1 |
| F-08 | 多显示器分区归属 | 分区记录所属显示器，跨屏拖动与配置变更时保持原位 | L | 高 | P1 |
| F-09 | 布局快照 | 保存多套命名布局，一键切换整桌分区排布 | M | 高 | P1 |
| F-10 | 图标排序与自动分组规则 | 按名称／类型／时间排序，按规则把图标自动归入分区 | L | 中 | P1 |
| F-11 | 图标着色与背景融合 | 对标 Icon Tint 与 Chameleon，统一图标色调／融入壁纸 | M | 中 | P1 |
| F-12 | 批量样式与批量重命名 | 多选分区后统一改色、改透明度、改标题栏字体 | M | 中 | P1 |
| F-13 | 透明度／内容背景／阴影解耦 | 三个视觉维度独立可调，替代当前单一 `Opacity` | M | 中 | P1 |
| F-14 | 配置备份、还原与迁移 | 把 `BackupRootDirectory` 做成可用的备份／还原功能，并做配置版本迁移 | M | 高 | P1 |
| F-15 | 安全模式与崩溃恢复 | 启动失败时进入安全模式（禁用插件），并保留崩溃现场 | M | 高 | P1 |
| F-16 | 插件 API 版本化与能力声明 | 插件清单声明 API 版本与所需能力，主程序按能力协商加载 | L | 中 | P1 |
| F-17 | 分区选项卡（Tabs） | 一个分区内多标签页，每页独立图标集合 | L | 高 | P2 |
| F-18 | 文件夹门户（Folder Portal） | 把任意目录映射为虚拟分区，支持网络／云盘 | L | 高 | P2 |
| F-19 | 主题包 / 外观市场 | 可导入导出的主题集合（颜色、字体、圆角、阴影） | M | 低 | P2 |
| F-20 | 多语言（英文界面） | 把界面字符串抽成资源，支持中英切换 | M | 中 | P2 |

### 4.2 P0：近期（1–2 个版本）

#### F-01 分区快速显隐 + 专注模式

**描述**：提供"一键隐藏全部分区"（专注模式）与"单独切换某个分区显隐"两种能力，并把这两种能力接到托盘与快捷键上。

**用户价值**：桌面整理工具的核心矛盾是"分区既要存在，又不能在工作时碍事"。目前用户必须打开"分区管理"窗口逐个取消勾选才能收起桌面，这是最高频也最笨拙的操作。专注模式（对应 Fences 的 distraction-free 桌面）能让用户"清空桌面开始工作"，再一键还原。

**实现思路**：
- 在 `GeneralSettings` 增加 `FocusModeEnabled` 布尔位；进入专注模式时批量调用 `WindowManagerService` 的隐藏逻辑（复用 `PartitionManagerViewModel.UpdateWindowVisibility` 背后的同一段实现，把该逻辑下沉到 `WindowManagerService`，避免 ViewModel 之间互相调用）。
- 隐藏的分区不应丢失图标归属：`GeneralSettings.PartitionVisibility` 已有字典，直接复用。
- 托盘入口见 F-02；快捷键入口见 F-03。

**工作量**：S　**价值**：高
**依赖与风险**：`PartitionManagerViewModel.UpdateWindowVisibility` 目前在 ViewModel 层直接操作 `WindowManagerService` 与窗口实例，需要先做一次小幅重构（把窗口显隐下沉到 `WindowManagerService`），否则会出现 ViewModel 之间的耦合。

#### F-02 托盘分区快捷面板

**描述**：在 `TrayMenuService.CreateTrayMenu()` 中，于"新建分区"之后插入一个"分区"子菜单，动态列出当前所有分区，勾选框表示可见性，支持单击标题直接切换显示。

**用户价值**：托盘是这类常驻工具唯一的"控制中心"。现在托盘只有新建／管理／自启动／插件四项，用户想隐藏一个分区得点两层、进窗口、找复选框。补上这一项后，日常 90% 的操作可以在托盘里完成。

**实现思路**：
- 在 `TrayMenuService` 中新增 `CreatePartitionMenuItems()`，数据源用 `PartitionDataService.Instance.GetMetadata().WindowIds` + `PartitionDataService.Instance.GetPartitionData(id)`，与 `PartitionManagerViewModel.LoadPartitions()` 的取数逻辑一致；建议把这段取数抽到 `PartitionDataService` 的一个 `GetAllPartitionSummaries()` 方法，避免两处重复。
- 复用 `GeneralSettingsService.Instance.PartitionVisibilityChanged` 事件做增量刷新，避免每次开菜单全量重建（`PartitionManagerViewModel` 已有同类订阅，可参考）。
- 分区名旁加一个 16px 的 FluentIcons 图标区分状态。

**工作量**：S　**价值**：高
**依赖与风险**：低。`TrayMenuService` 已有 `menuItems` 字典用于按 key 定位菜单项，沿用该模式即可。

#### F-03 全局快捷键框架

**描述**：新增 `Services/HotkeyService.cs`，封装 `RegisterHotKey` / `UnregisterHotKey`，提供一组默认快捷键并允许用户自定义，提供冲突检测与"占用中"的友好提示。

**用户价值**：桌面整理本质是高频操作，只有鼠标可达的产品会被降级为"偶尔用一下"。Fences 的 Peek 默认就绑定 `Win + Space`，并且专门做了多键盘布局检测，避免和输入法切换键冲突——这类细节说明快捷键在专业桌面工具里不是加分项而是及格线。

**实现思路**：
- `Services/HotkeyService.cs`：`RegisterHotKey` 封装 + `HotkeyBinding` 模型（修饰键 + 键 + 动作 ID）+ `HotkeySettings` 持久化到 `GeneralSettings`。
- 默认绑定建议：`Win + Space` → Peek（F-07），`Win + Shift + D` → 专注模式，`Win + Shift + N` → 新建分区，`Win + Shift + M` → 打开分区管理。
- 注册失败（被其他程序占用）时通过 `TrayIconService.Instance.ShowBalloonTip` 提示，与现有 `TrayMenuService.ToggleAutoStart` 的错误处理风格一致。
- 在 `App.xaml.cs` 启动流程中注册，退出时注销。

**工作量**：M　**价值**：高
**依赖与风险**：`Win + Space` 在中文输入法下默认用于中英文切换，会与系统抢占。**必须在注册时做 `GetKeyState` / 布局检测，并提供"改为其他组合键"的选项**——这是 Fences 已经踩过的坑（其 6.2 变更日志明确记录了这一点）。

#### F-04 分区拖拽吸附与等距参考线

**描述**：拖动分区窗口时，实时计算与屏幕边缘、其他分区的吸附位置，命中时显示 1px 强调色参考线并锁定位置。支持"固定间距"模式，使多个分区之间的间隙保持一致。

**用户价值**：这是"分区能不能用得爽"的分水岭。没有吸附时，多分区桌面要靠肉眼对齐；有了吸附 + 等距，才能真正形成"整齐的桌面网格"。Fences 在 6.2 专门增强了"靠近显示器边缘时的对齐吸附 + 固定间距"，说明这是用户主动要求的。

**实现思路**：
- 吸附计算抽成独立组件 `Services/SnapEngine.cs`（纯函数，输入候选矩形 + 其他分区矩形集合 + 屏幕工作区，输出吸附后的矩形与命中的参考线列表），便于单测。
- `DesktopManagerWindow` 的拖拽逻辑中接入 `SnapEngine`；`WindowManagerService` 中已有的 `isArrangingWindows` 标志可复用来抑制"程序化移动"触发的吸附。
- 参考线用一个全屏 `AdornerDecorator` 或置顶 `Popup` 绘制，线条颜色取 `Theme.xaml` 的强调色令牌（`#2563EB`），1px，透明度 0.8。
- "固定间距"间距值存到 `GeneralSettings`，默认 16。

**工作量**：M　**价值**：高
**依赖与风险**：中。拖拽事件在 WPF 中是 `Thumb.DragDelta` 语义，与 `Window.Left` / `Top` 的交互容易抖动，需要在真实 DPI 下反复调参；建议第一步只做"屏幕边缘吸附"，等距吸附放第二个迭代。

#### F-05 一键布局预设

**描述**：在 `WindowManagerService` 现有 `ArrangeAllPartitionWindows()` 之外，增加多种布局策略：网格（现状）、横向均分、纵向均分、主次（左主右辅）、以及"保留现状"。

**用户价值**：用户换显示器、分辨率变化、或者只是想"重新整齐一下"时，现在只能接受 √N 网格这一种结果。横向均分和主次布局覆盖了绝大多数真实场景。

**实现思路**：
- 新增 `Models/LayoutStrategy.cs` 枚举（`Grid` / `Horizontal` / `Vertical` / `PrimarySecondary` / `KeepManual`）。
- 把 `WindowManagerService.ArrangeAllPartitionWindows()` 拆成"计算布局"与"应用布局"两步，计算部分按策略分支；现有的网格算法保留为 `Grid` 分支，行为不变。
- 在 `PartitionManagerWindow` 的工具条上提供策略选择（见 §5.3），并在 `DesktopManagerWindow` 的设置弹窗 `SettingsPopup` 中提供"应用布局"入口。

**工作量**：M　**价值**：中
**依赖与风险**：低。`ArrangeAllPartitionWindows()` 已有 `isArrangingWindows` 保护与 `PartitionDataService.Instance.SavePartitionData` 持久化，拆分时注意保留。

#### F-06 文档与版本号一致性治理

**描述**：统一版本号与框架描述，并清理已废弃的工程文件。

**用户价值**：看似不产生功能，但直接影响到用户升级意愿与 issue 排查效率。`readme_cn.md` 声明 .NET 8 而 csproj 是 net6.0，changelog 到 2.0.0 而程序集版本停在 1.0.5——这类不一致会让用户怀疑软件是不是坏了。

**实现思路**：
- 决定版本号策略（建议以 `docs/changelog.md` 为准），同步 `Layouter.csproj` 的 `<Version>` / `<AssemblyVersion>` / `<FileVersion>`；`UpdateWindow.xaml` 的更新检查逻辑需要一并确认它读取的是哪个版本字段。
- 修正 `docs/readme_cn.md` / `readme.md` 中的框架表述（要么把 csproj 升到 net8.0-windows，要么改文案，二选一；考虑 `net6.0-windows7.0` 已停止支持，倾向升级 csproj）。
- 删除 `Layouter.csproj` 中已排除的废弃文件与 `Plugins/Deprecated/**` 目录，同步清理 `Plugins/Deprecated/PluginManagerWindow.xaml` 等。
- 在 `docs/changelog.md` 中补上 2.0.0 之后的变更，并写明本文档的提案编号体系。

**工作量**：S　**价值**：中
**依赖与风险**：低，但**改 csproj 目标框架会影响运行时行为**（需重新验证 Shell 互操作与桌面层窗口），建议单独提交。

### 4.3 P1：中期

#### F-07 Peek 桌面预览模式

**描述**：按一次快捷键，所有分区临时置顶到所有窗口之上（含 Peek 边框与遮罩），再按一次或点击别处还原。

**用户价值**：Fences 最被用户津津乐道的功能。它解决的是"我要拿一个桌面上的文件，但不想先把所有窗口最小化"这个高频场景。

**实现思路**：
- 复用 `SysUtil.SetDesktopLevelWindow` 的思路，但需要一个更靠前的层级（`WS_EX_TOPMOST`）。注意 Layouter 目前的"桌面层"是低于普通窗口的，进 Peek 需要临时提升为顶层。
- 状态机：`Normal → Peeking → Normal`。进入时记录全部窗口的 Z 序与位置，退出时精确还原（避免与其他程序的 Z 序管理打架）。
- 遮罩用半透明黑色层（透明度与分区 `Opacity` 解耦，见 F-13）。
- Peek 时不弹出焦点，避免抢走用户正在输入的窗口焦点——这是 Fences 踩过的性能与体验坑（6.0 修复过 Peek 性能问题）。

**工作量**：M　**价值**：高
**依赖与风险**：**高**。Z 序操作在 Windows 上是全局状态，第三方软件（尤其是任务栏、虚拟桌面软件）会互相干扰。建议在 §6.3 之后再做，且必须做成可关闭的实验特性。

#### F-08 多显示器分区归属

**描述**：为每个分区记录所属显示器（设备名 + 分辨率 + 缩放），显示器配置变化（插拔、改分辨率、切换主屏）时把分区放回原显示器对应位置。

**用户价值**：用笔记本的人几乎都有外接屏。没有这项，换个屏幕分辨率分区就乱跑，是"用过一次就卸载"的典型原因。

**实现思路**：
- `Models/PartitionData.cs` 增加 `DisplayDeviceName` / `DisplayBounds` 字段，序列化时向后兼容（旧配置缺字段走默认值）。
- `WindowManagerService` 用 `System.Windows.Forms.Screen`（需引用 `UseWindowsForms`）或直接 P/Invoke `EnumDisplayMonitors` 枚举显示器，替代当前固定的 `SystemParameters.WorkArea`。
- 监听 `Microsoft.Win32.SystemEvents.DisplaySettingsChanged`，做一次重定位（注意该事件在非 UI 线程触发，需要 marshal 到 Dispatcher）。
- DPI：目标框架需声明 `PerMonitorV2`（在 `app.manifest` 中加 `<dpiAwareness>PerMonitorV2</dpiAwareness>`），并响应 `WM_DPICHANGED`。

**工作量**：L　**价值**：高
**依赖与风险**：**高**。DPI 与多显示器是 Fences 花了大半年迭代的部分，涉及原生窗口消息处理。建议**先做 DPI 感知（升 PerMonitorV2 + 响应 WM_DPICHANGED），再做多显示器归属**，顺序不能反。

#### F-09 布局快照

**描述**：把"所有分区的位置、尺寸、显隐、样式"打包成一份命名快照（如"工作"、"游戏"、"演示"），可一键应用，也可在显示器或分辨率变化后一键恢复。

**用户价值**：与 Windows 11 的 Snap Groups 同构的需求——"把我刚才那套布局原样搬回来"。对 Layouter 来说这是分区数量增长后的必然需求：到了 8 个分区以上，手动重排已经不现实。

**实现思路**：
- 新增 `Models/LayoutSnapshot.cs`（名称 + 分区 id → 几何/样式/显隐的字典 + 创建时间）。
- 存储放在 `GeneralSettings.BackupRootDirectory` 指向的目录下（该字段已存在但目前没有产品化入口，正好一并用起来）。
- `WindowManagerService` 增加 `ApplySnapshot(snapshot)`，复用 F-05 拆分出的"应用布局"管线。

**工作量**：M　**价值**：高
**依赖与风险**：中。依赖 F-05 的布局管线拆分先落地。

#### F-10 图标排序与自动分组规则

**描述**：支持按名称／类型／大小／修改时间排序；并提供简单规则——满足条件的图标自动移动到指定分区。

**用户价值**：分区建立之后，新下载的文件图标会散落在桌面外，需要手动拖。自动归位是 Fences "automation rules" 的核心价值，也是分区数量增长后的刚需。

**实现思路**：
- 排序：`DesktopIconService` 已有图标枚举能力，在其上增加 `SortIcons(partitionId, IconSortRule)`，作用于 `PartitionData` 内的图标顺序，并回写到桌面实际位置（`Utility/ShellUtil.cs` 已有 Shell 互操作可用）。
- 自动分组：新增 `Models/AutoGroupRule.cs`（条件 + 目标分区 id），由 `DesktopIconService` 的目录变更监听触发（`SystemEvents` 的 `FileSystemWatcher` 需在后台线程执行再 marshal 回 UI 线程）。
- 规则 UI 放在分区设置窗口的独立分组中，避免与外观设置混在一起。

**工作量**：L　**价值**：中
**依赖与风险**：中。文件变更监听的性能与线程模型是主要风险；建议第一版只做"定时轮询 + 手动触发"，不做实时监听。

#### F-11 图标着色与背景融合

**描述**：对标 Fences 的 Icon Tint（统一图标色调）与 Chameleon（让图标按主色融入壁纸）。提供全局开关 + 按分区覆盖。

**用户价值**：Windows 桌面图标的视觉一致性是"杂乱感"的主要来源之一。着色能立刻让桌面"整齐"，也是很多用户对 Fences 的第一印象功能。

**实现思路**：
- 图标着色：Layouter 的桌面图标是自绘控件（`DesktopManagerWindow` 中的 `iconsContainer`），可在 `IconPresenter` 层用 `Image.Source` + `ColorMatrix`（`System.Windows.Media.Imaging`）做色相偏移与饱和度调整，成本低。
- 背景融合（Chameleon）：需要采样壁纸主色并按图标区域做渐隐，实现成本明显更高，建议第二阶段。
- 颜色选择器复用 `PartitionSettingsWindow` 已有的 `ColorSwatchStyle` + `TitleForegroundButton` 交互范式。

**工作量**：M（着色）/ L（融合）　**价值**：中
**依赖与风险**：低。注意着色会影响图标可识别度，需要提供"一键关闭"。

#### F-12 批量样式与批量重命名

**描述**：在 `PartitionManagerWindow` 中多选分区后，批量设置标题栏前景色／背景色／字体／字号／透明度／图标尺寸，并支持批量重命名（支持前缀替换）。

**用户价值**：现有 `PartitionManagerViewModel` 已经有多选机制（`AllSelected` / `AnySelectedVisible`），但批量操作只覆盖了显隐与删除。样式批量设置是补齐这个闭环的最小增量。

**实现思路**：
- `PartitionSettingsWindow` 现有实现是"单个 ViewModel + 一个 `isGlobal` 标志"的模式（`new PartitionSettingsWindow(viewModel, false)` / `(viewModel, true)`）。批量设置建议新开一个轻量的"批量样式"对话框，复用同一套控件但输出到多个分区。
- 保存时逐个走 `PartitionSettingsService.Instance.SaveSettings(vm, false)`。

**工作量**：M　**价值**：中
**依赖与风险**：低。注意 `EnableGlobalStyle` 开启时全局样式会覆盖分区样式，批量修改前应给出提示。

#### F-13 透明度／内容背景／阴影解耦

**描述**：把当前单一的 `Opacity` 拆成三个独立维度：整个分区的透明度、图标区域内容背景的不透明度、以及阴影／描边的有无与强度。

**用户价值**：当前 `Opacity` 是单一值，用户想让"标题栏实心但图标区半透"是做不到的，只能整体调。Fences 把"透明度（transparency）"和"不透明度（opacity）"分成两个独立设置，用户因此能做出层次感。

**实现思路**：
- `Models/PartitionSettings.cs` 增加 `ContentOpacity`、`ShadowEnabled` / `ShadowBlur` / `ShadowOpacity` 字段；保留 `Opacity` 语义不变以兼容旧配置（旧配置加载时把 `ContentOpacity` 初始化为 1）。
- `DesktopManagerWindow.xaml` 中 `IconsContainer` 的 `Background="{Binding ContentBackground}"` 改为绑定一个带独立透明度的画刷；阴影通过 `Effect` + `DropShadowEffect` 实现，注意 `Effect` 会触发离屏渲染，大面积分区可能影响性能——建议只对标题栏或用 `Border` 的伪阴影。

**工作量**：M　**价值**：中
**依赖与风险**：低到中。`Effect` 的渲染开销需要实测。

#### F-14 配置备份、还原与迁移

**描述**：把 `GeneralSettings.BackupRootDirectory` 做成真正可用的功能：定时或手动备份全部配置（分区数据、样式、通用设置、插件），支持从备份列表还原；同时为配置格式引入版本号与迁移逻辑。

**用户价值**：桌面布局是用户花了时间积累的资产，弄丢了不可再生。而且 Layouter 的配置存放在用户目录下，一旦用户重装系统或换机就全没了。竞品侧 Stardock 的企业版明确提供"部署到网络驱动器"与"内容锁定"，个人用户侧同样看重这点。

**实现思路**：
- 备份 = 打包 `PartitionDataService` / `GeneralSettingsService` / `PartitionSettingsService` 各自的数据目录为 zip（`System.IO.Compression` 已是依赖）。
- 还原 = 解压到临时目录 → 校验 → 原子替换。替换前必须先关闭并重建所有分区窗口（`WindowManagerService` 需要提供 `ReloadAll()`）。
- 迁移：在配置根目录放一个 `config-version` 文件，读取时按版本号逐级迁移。配合 F-06 的版本号治理一起做。

**工作量**：M　**价值**：高
**依赖与风险**：中。还原过程中的窗口重建失败会导致分区丢失，必须先做"校验通过才替换"。

#### F-15 安全模式与崩溃恢复

**描述**：启动时若上一次异常退出（未写入正常退出标记），下次启动进入安全模式：不加载任何插件，只加载核心分区功能，并提示用户。

**用户价值**：Layouter 有内置代码编辑器和插件加载能力（`Plugins/PluginLoader.cs`），用户自写的插件出问题导致整个程序起不来是真实风险。没有安全模式，用户只能手动删配置目录。

**实现思路**：
- 在配置目录写 `running.lock`（含进程 id），正常退出时删除；启动时若发现残留则判定为异常退出。
- `PluginManager` 增加 `LoadAll(bool includePlugins)` 参数，安全模式下跳过插件加载。
- 恢复现场：`Serilog` 已接入，把崩溃前若干条日志（滚动文件）打包放到导出目录。

**工作量**：M　**价值**：高
**依赖与风险**：低。

#### F-16 插件 API 版本化与能力声明

**描述**：为插件清单（`Plugins/PluginTemplates/PluginInfo.json`）引入 `ApiVersion` 与 `Capabilities` 字段；主程序按版本判断兼容性，按能力协商是否启用某插件。

**用户价值**：插件系统是 Layouter 相对 Fences 的核心差异化，也是最容易被破坏的地方。目前插件清单没有版本概念，主程序一升级就可能让老插件静默失效或崩溃。有了版本与能力声明，插件作者才能放心升级。

**实现思路**：
- `Plugins/PluginManager.cs` 增加 API 版本常量与兼容性判定；不兼容时在 `PluginsManagerWindow` 里禁用并标注原因，而不是直接抛异常。
- `Capabilities` 用于声明插件需要的能力（如"需要分区 API v2""需要读取图标列表"），主程序据此决定是否放行。

**工作量**：L　**价值**：中
**依赖与风险**：中。属于"给未来做准备"的投入，短期收益不明显，但越晚做越贵。

### 4.4 P2：远期

#### F-17 分区选项卡（Tabs）

**描述**：一个分区内可开多个标签页，每页持有独立的一组桌面图标；标签可着色、可拖放图标进去、可分离成独立分区。

**用户价值**：Fences 6 最具辨识度的功能。当用户有 30+ 个桌面图标时，单个分区会膨胀到需要滚动，标签页能显著压缩空间占用。

**实现思路**：
- 数据模型：`PartitionData` 增加 `Tabs` 集合，每个 tab 持有自己的图标列表；`PartitionSettings` 增加 `ActiveTabIndex` 与每个 tab 的颜色。
- UI：`DesktopManagerWindow.xaml` 的 `TitleBar`（当前 `Height="30"`）上方或下方插入标签条；标签条高度约 24px。
- 交互模型可参考 Groupy 2 的标签条（见 §2.3），包括"拖放到标签头部""从标签组分离"。
- 拖放落点判定需要扩展 `DesktopManagerWindow.xaml.cs` 中现有的图标拖放逻辑。

**工作量**：L　**价值**：高
**依赖与风险**：**高**。这是本提案清单里对现有数据模型冲击最大的一项，会同时影响 `PartitionDataService` 的序列化、`PartitionManagerWindow` 的列表展示、以及 `DesktopIconService` 的图标归属逻辑。建议在 F-08（多屏）与 F-14（配置迁移）之后做，否则配置迁移要同时处理两个大变更。

#### F-18 文件夹门户（Folder Portal）

**描述**：把任意本地／网络／云盘目录映射为一个虚拟分区，内容以列表或图标网格展示，支持进入子目录。

**用户价值**：对标 Fences 的 Folder Portals。它把"常用的项目目录"直接搬到桌面上，比建立分区再往里塞快捷方式更自然。

**实现思路**：
- 新增 `Models/FolderPortal.cs`（路径、显示模式、是否显示文件夹图标、是否隐藏原始目录标题）。
- 与 `DesktopIconService` 分离：门户不管理真实桌面图标，只做目录枚举 + `ShellUtil` 打开操作。
- 网络目录的枚举是 I/O 阻塞操作，必须放后台线程 + 缓存（`GeneralSettings` 里已有 `HiddenStoreDirectory` 这类目录字段的先例）。

**工作量**：L　**价值**：高
**依赖与风险**：**高**。与 F-17 共享"分区内容模型"这一核心改动，建议与 F-17 一起设计数据结构，或在 F-17 之后实现。

#### F-19 主题包 / 外观市场

**描述**：把一组外观参数（强调色、圆角、标题栏字体、阴影、滚动条样式）打包成可导入导出的主题文件。

**用户价值**：让用户能一键换一套视觉风格，也方便在社区里分享"同款桌面"。

**实现思路**：
- 主题模型与 `Styles/Theme.xaml` 的令牌一一对应；运行时通过 `Application.Current.Resources` 覆盖令牌值来实现（这是 WPF 主题化的标准做法）。
- 导入导出为 JSON，放在 `GeneralSettings` 同级目录。

**工作量**：M　**价值**：低
**依赖与风险**：低。前提是 `Theme.xaml` 的令牌已经足够完整、可被整体替换（见 §5.1）。

#### F-20 多语言（英文界面）

**描述**：把界面字符串从代码与 XAML 中抽出为资源，支持中英切换（跟随系统或手动指定）。

**用户价值**：Layouter 是开源项目，GitHub 仓库面向国际社区。Fences 支持包括简体中文在内的多语言。当前 Layouter 全界面硬编码简体中文（`TrayMenuService`、`PartitionManagerViewModel`、`UpdateWindow` 中的中文字符串随处可见），是国际化的一票否决项。

**实现思路**：
- 新建 `Resources/Strings.zh-CN.xaml` / `Strings.en-US.xaml`，用 `{x:Static}` 或绑定方式替换硬编码字符串。
- `UpdateWindow.xaml` 的版本说明、`TrayMenuService` 的菜单标题、`PartitionManagerViewModel` 的提示文案优先改造。

**工作量**：M　**价值**：中
**依赖与风险**：中。改造面广但机械；建议与 F-06 一起做，避免两次遍历全部界面文件。

---

## 5. UI / 视觉美化提案

### 5.1 设计令牌基线

统一方向已经明确：`Layouter/Styles/Theme.xaml` 存放语义令牌（颜色、字号、圆角、间距），`Layouter/Styles/AppControls.xaml` 存放控件样式（基于令牌），`Styles/TrayIconResources.xaml` 存放托盘专用资源。以下是执行本节所有提案的前提——**令牌必须先补齐，否则每个界面都会重新硬编码颜色**。

建议在 `Theme.xaml` 中至少固化以下语义令牌（名称待定，原则是语义化而非色值化）：

| 令牌类别 | 用途 | 建议初值 |
|---|---|---|
| `Accent` / `AccentHover` / `AccentPressed` | 强调色，用于主按钮、选中态、参考线 | `#2563EB` 及其明暗变体 |
| `Surface` / `SurfaceElevated` / `SurfaceSunken` | 卡片、分组内嵌区、输入框底色 | 三级灰阶 |
| `Border` / `BorderStrong` | 常规描边与强调描边 | 1px 低饱和灰 |
| `TextPrimary` / `TextSecondary` / `TextDisabled` | 三级文字色 | |
| `TitleBarForeground` / `TitleBarBackground` | 分区标题栏默认色（当前只能逐分区设定） | 建议留空以区分"用户未设置"与"用户设为该色" |
| `RadiusCard` / `RadiusControl` | 圆角半径 | 8px（卡片）/ 6px（控件） |
| `SpaceX1..X6` | 间距梯度 | 4 / 8 / 12 / 16 / 24 / 32 |
| `IconSizeSm/Md/Lg` | 图标尺寸 | 16 / 20 / 24 |
| `ScrollbarWidth` | 细滚动条宽度 | 6px |
| `ShadowCard` | 卡片阴影 | 低透明度、高模糊 |

三条硬性规则：

1. **任何视图不得再出现字面量颜色**。`TrayMenuService.CreateMenuItem(..., Colors.DodgerBlue)` 是典型例子——DodgerBlue 与 `#2563EB` 视觉上接近但不是同一个色，混用会让强调色不一致。应改为从资源字典取色。
2. **分区标题栏颜色从"必填"变为"可选"**。`PartitionSettings.TitleForeground` / `TitleBackground` 是 `Color` 值类型，无法表达"未设置"。改为 `Color?` 或增加一个 `UseThemeColor` 布尔位，才能让新增的默认主题对老用户也生效。
3. **深色模式暂不纳入本轮**。`Theme.xaml` 目前没有主题切换机制；引入深色模式会同时影响所有控件模板与图标着色（F-11），建议在令牌体系稳定后再单独立项。

### 5.2 桌面分区窗口

涉及文件：`Layouter/Views/DesktopManagerWindow.xaml`、`Views/DesktopManagerWindow.xaml.cs`

当前结构：`MainBorder`（`CornerRadius="8"`）→ `TitleBar`（`Height="30"`）→ `IconsContainer`（`CornerRadius="0,0,8,8"`）。整体已具备卡片形态，问题集中在**状态表达**与**交互反馈**。

| 编号 | 提案 | 现状 | 建议 |
|---|---|---|---|
| U-01 | 标题栏操作按钮按需显现 | `NewPartitionButton` / `LockButton` / `SettingsButton` 常驻显示 | 默认低透明度（0.35），鼠标进入 `TitleBar` 时过渡到 1.0，移出恢复。用 `Storyboard` 做 120ms 透明度过渡，避免突兀 |
| U-02 | 锁定态图标语义一致 | `LockIcon` 绑定 `FluentIcons.Common.Symbol.LockOpen`，锁定后应换成 `LockClosed` | 锁定态同时把图标染成 `TextDisabled` 色，一眼可辨；并同步 `ToolTip` 文案（当前是固定字符串"未锁定"） |
| U-03 | 折叠态（Roll-up）有独立视觉 | `IsRolledUp` 只影响 `IconsRowDefinition` 高度，没有专属样式 | 折叠后把标题栏变成 4–6px 高的把手条：保留一个 1px 强调色的上边缘线 + 极窄的标题文字；展开时用 150ms 高度动画过渡，而不是瞬变 |
| U-04 | 拖拽吸附参考线 | 无 | 命中时画 1px `Accent` 参考线，配 8% 透明度的 `Accent` 填充带；松手后 200ms 淡出 |
| U-05 | 拖拽中的对齐预览 | 无 | 拖动时在原位置留一个 6% 透明度的"幽灵"轮廓，帮助用户理解落点 |
| U-06 | 图标悬停反馈 | 依赖系统 ListBox 行为 | 图标项悬停时显示圆角矩形底（`RadiusControl`），按下时 4% 缩放；这些圆角/缩放值直接取令牌，不要写死 |
| U-07 | 焦点可见性 | 分区按钮无焦点样式 | 所有可聚焦元素加 2px `Accent` 外描边（`:focus-visible` 语义，WPF 侧用 `IsKeyboardFocused` 触发器），满足键盘可达性 |
| U-08 | 标题栏高度分级 | 固定 `Height="30"` | 提供紧凑（24）/ 标准（30）/ 宽松（36）三档，跟着 `TitleFontSize` 自动联动，避免大字号标题被裁切 |

### 5.3 管理窗口

涉及文件：`Views/PartitionManagerWindow.xaml`、`Views/PartitionSettingsWindow.xaml`、`Views/UpdateWindow.xaml`

#### 5.3.1 分区管理窗口（`PartitionManagerWindow.xaml`，780×620）

**信息层级问题**：当前窗口把"全局样式开关"、"全选"、"分区列表"、"批量操作"平铺在一起，用户扫一眼分不清哪些是全局的、哪些是针对当前选中的。建议按下面的层级重排：

```
┌─ 标题区 ───────────────────────────────┐
│  分区管理                      [全局样式 ●]│  ← 全局开关独立成一行，带说明 tooltip
├─────────────────────────────────────────┤
│  工具条： [新建] [编辑] [显示] [隐藏] [删除] │  ← 随选中状态启禁用
├─────────────────────────────────────────┤
│  ☑  工作        · 8 图标   [锁定][折叠]    │  ← 列表行
│  ☐  开发        · 24 图标                 │
│  ...                                     │
├─────────────────────────────────────────┤
│  已选 2 项                    [删除所选]   │  ← 状态栏
└─────────────────────────────────────────┘
```

具体建议：

| 编号 | 提案 | 说明 |
|---|---|---|
| U-09 | 工具条与批量操作分离 | 把"新建/编辑/显示/隐藏/删除"提到顶部工具条，作用于**当前选中**；"全选/反选"放在底部状态栏。避免用户误以为删除是立即作用于所有分区 |
| U-10 | 空状态 | 列表为空时显示一个居中的引导块：一枚 48px 的 FluentIcons 图标（`FluentIcons.Common.Symbol.Grid`）+ 主文案"还没有分区" + 副文案"创建一个分区，把桌面图标收纳进去" + 主按钮"新建分区"（`Accent` 填充）+ 快捷键提示"Ctrl/⌘ + Shift + N"。当前空列表是完全空白的，用户会以为软件坏了 |
| U-11 | 行密度与状态芯片 | 行高收到 40–44px；分区名 13px；右侧用小圆角芯片表示状态（已锁定 / 已折叠 / 已隐藏），芯片用 `SurfaceSunken` 底 + `TextSecondary` 字，不要用高饱和色 |
| U-12 | 图标数量提示 | 每行显示图标数（`DesktopIconService` 可提供），帮助用户判断哪个分区臃肿；>30 个时用 `TextDisabled` |
| U-13 | 危险操作二次确认 | 删除分区目前直接执行。改为删除后立即出现 5 秒"撤销"提示条，而不是弹模态确认框——更轻量，且降低误删代价 |
| U-14 | 搜索与排序 | 分区超过 10 个时，工具条右侧加一个搜索框，支持按名称过滤；配合 F-05 的布局策略下拉 |
| U-15 | 窗口尺寸与 DPI | `MinWidth="640"` 在 125% 缩放的小屏上会溢出。改为按 `SystemParameters.WorkArea` 动态计算初始尺寸并钳制在 60%–90% 工作区 |

#### 5.3.2 分区设置窗口（`PartitionSettingsWindow.xaml`，460×660）

当前结构已经不错（`HeaderTitleText` 用 `AppPageTitleText` 样式、颜色用 `ColorSwatchStyle` 预览、底部三按钮"恢复默认/取消/保存"）。建议：

| 编号 | 提案 | 说明 |
|---|---|---|
| U-16 | 设置分组化 | 460px 宽的单列长表单信息密度过高。建议分为"标题栏""内容区""图标"三组，每组用小标题 + 细分隔线分隔，分组之间留 24px 间距（`SpaceX6`） |
| U-17 | 实时预览 | 已有 `PreviewBorder`，建议改为实时反映所有设置改动（而非仅颜色），并在右侧或下方显示"应用全局样式时本分区会被覆盖"的提示（当 `EnableGlobalStyle` 开启） |
| U-18 | 滑块与数值联动 | `TitleFontSizeSlider` / `OpacitySlider` / `TextSizeSlider` 右侧的数值文本应显示实际值并可点击直接输入（当前字号旁是硬编码的 `Text="14"`） |
| U-19 | 主次按钮层级 | "保存"用 `Accent` 实心主按钮，"取消"用 `Secondary` 次按钮，"恢复默认"用 `TextButton` 文字按钮，三者视觉权重明确区分 |

#### 5.3.3 更新窗口（`UpdateWindow.xaml`，640×520）

| 编号 | 提案 | 说明 |
|---|---|---|
| U-20 | 版本列表的信息层级 | `VersionList` 中每个版本应展示：版本号、发布日期、更新说明摘要、文件大小。当前 `rbVersionA` / `rbVersionB` 两个固定 RadioButton 是硬编码的示例态，应改为 `ItemsControl` + `DataTemplate` 绑定真实版本集合 |
| U-21 | 下载按钮状态 | `Download` 按钮需要区分"可下载 / 下载中 / 已下载"，下载中显示不确定进度条并禁用重复点击 |
| U-22 | 说明链接 | `OpenReleasePage` 超链接应有 `TextDecorations="None"` 与 `Accent` 颜色，避免默认下划线破坏视觉体系 |

### 5.4 托盘菜单

涉及文件：`Services/TrayMenuService.cs`、`Styles/TrayIconResources.xaml`

**现状问题**：菜单项图标全部用 `Colors.DodgerBlue`（与设计令牌 `#2563EB` 不一致）；"开机自启动"是一个父项，其下挂一个"开启/停止"的子项——两层嵌套表达一个布尔开关，是明显的反模式；插件子菜单里"插件管理"和"创建插件"用了几乎相同的图标（`PlugConnected` / `PlugConnectedSettings` / `PlugConnectedAdd`），辨识度低。

| 编号 | 提案 | 说明 |
|---|---|---|
| U-23 | 布尔开关扁平化 | "开机自启动"改为可直接勾选的单项（`IsCheckable="True"`），去掉二级子菜单；勾选状态与 `GeneralSettings.AutoStartEnabled` 双向绑定 |
| U-24 | 菜单图标统一令牌色 | 所有菜单图标改用 `Theme.xaml` 的 `Accent`，或按语义区分（主操作 `Accent`，次操作 `TextSecondary`） |
| U-25 | 分区快捷面板 | 对应 F-02。位置放在"新建分区"正下方，作为一级子菜单，条目形如 `☑ 工作` / `☐ 开发` |
| U-26 | 分组与分隔 | 用分隔线把菜单切成三组：**动作**（新建分区、新建、专注模式）／**分区**（列表）／**更多**（插件、设置、检查更新、退出） |
| U-27 | 版本号与检查更新 | 底部加一行灰色小字"v2.0.0 · 检查更新"，点击打开 `UpdateWindow`。当前托盘没有版本信息，用户无法确认自己是不是最新版 |
| U-28 | 图标区分度 | 为"插件管理""创建插件""插件"选用语义差异更明显的 FluentIcons 符号，避免三个近似图标 |
| U-29 | 圆角与内边距 | `TrayMenuStyle` 统一圆角（`RadiusCard`）与内边距梯度，菜单项高度 32px，图标 16px，与 `AppControls.xaml` 中的按钮保持同一套度量 |

### 5.5 插件窗口

涉及文件：`Plugins/PluginsManagerWindow.xaml`、`Plugins/Views/FloatingWindow.xaml`、`Plugins/Views/DetailedTemplateWindow.xaml`、`Plugins/Controls/CodeEditor.xaml`

| 编号 | 提案 | 说明 |
|---|---|---|
| U-30 | 插件列表卡片化 | `PluginsManagerWindow` 的插件条目改为 `RadiusCard` 圆角卡片，卡片内含图标、名称、版本、启用开关、状态标签；网格布局，卡片间距 `SpaceX4` |
| U-31 | 插件状态语义色 | 启用/禁用/加载失败用状态点（`Accent` / `TextDisabled` / 错误红）而非文字标签，减少视觉噪声 |
| U-32 | 浮窗与主窗口同源 | `FloatingWindow` 目前是独立的视觉体系。统一使用 `Theme.xaml` 令牌：背景用 `SurfaceElevated`，标题栏用 `Surface`，边框用 `Border`，圆角用 `RadiusCard` |
| U-33 | 代码编辑器主题联动 | `CodeEditor.xaml` 使用 AvalonEdit，其语法高亮配色是硬编码的 VS 暗色主题。应改为从 `Theme.xaml` 读色（关键字用 `Accent`，字符串/注释用 `TextSecondary` 的降饱和变体），使编辑器与整体主题一致 |
| U-34 | 编辑器工具条 | 在编辑器顶部加一条细工具条：运行、保存、格式化、字体大小。插件开发是 Layouter 的招牌能力，值得专门打磨 |
| U-35 | 模板窗口信息补齐 | `DetailedTemplateWindow` 作为"详细模板"展示页，应列出模板包含的类、属性、生命周期钩子，并与 `docs/plugin_development_cn.md` 的章节锚点链接，形成"文档—代码"闭环 |
| U-36 | 插件失败提示 | 插件加载失败时，`PluginsManagerWindow` 中该条目就地显示失败原因（来自 `Serilog` 日志的最后一条异常），而不是只在日志文件里留痕 |

---

## 6. 实施路线图

### 6.1 近期（1–2 个版本）

目标：**把"日常操作鼠标不可达"这个硬伤补上**，同时做一次轻量的工程治理。

| 顺序 | 提案 | 理由 |
|---|---|---|
| 1 | **F-06** 文档与版本号一致性治理 | 最便宜，且为后续所有版本发布建立可信基线 |
| 2 | **F-03** 全局快捷键框架 | 后续 F-01 / F-07 都依赖它，必须先有热键层 |
| 3 | **F-02** 托盘分区快捷面板 | 立刻可用的效率提升，改动面小 |
| 4 | **F-01** 分区快速显隐 + 专注模式 | 与 F-02、F-03 组合成完整的"快速操作闭环" |
| 5 | **F-04** 分区拖拽吸附与等距参考线 | 分区体验的核心手感；建议先只做屏幕边缘吸附 |
| 6 | **F-05** 一键布局预设 | 复用 F-04 的几何计算代码 |
| 7 | **U-01 ~ U-08** 桌面分区窗口视觉 | 令牌落地后的第一批受益者 |
| 8 | **U-09 ~ U-15** 分区管理窗口视觉 | 同上 |
| 9 | **U-23 ~ U-29** 托盘菜单视觉 | 随 F-02 一并改造 |

**近期不建议动**：F-08（多显示器）、F-17（Tabs）、F-18（Folder Portal）。理由见 §6.4。

### 6.2 中期

目标：**把桌面级的"场景切换"能力补齐**，并把可靠性做厚。

| 顺序 | 提案 | 理由 |
|---|---|---|
| 1 | **F-08 前置：DPI 感知升级**（PerMonitorV2 + `WM_DPICHANGED`） | 所有几何相关功能的地基。虽不在提案表中，但它是 F-04 / F-05 / F-09 的正确性前提，必须在中期开始 |
| 2 | **F-08** 多显示器分区归属 | 完成多屏场景后，Layouter 才真正适合双屏用户 |
| 3 | **F-09** 布局快照 | 依赖 F-05 的布局管线 |
| 4 | **F-14** 配置备份、还原与迁移 | 用户资产保护；同时是 F-17 / F-18 大改动前的安全网 |
| 5 | **F-15** 安全模式与崩溃恢复 | 插件生态的兜底机制 |
| 6 | **F-11** 图标着色 | 低风险的视觉收益 |
| 7 | **F-12 / F-13** 批量样式、透明度解耦 | 补齐样式体系 |
| 8 | **F-10** 图标排序与自动分组规则 | 建议第一版只做排序，自动分组放规则引擎稳定后 |
| 9 | **F-16** 插件 API 版本化 | 在插件生态真正繁荣之前完成，成本最低 |
| 10 | **U-16 ~ U-22 / U-30 ~ U-36** 其余视觉统一 | 收尾 |

### 6.3 远期

目标：**从"分区容器"进化为"桌面内容管理器"**。

| 顺序 | 提案 | 理由 |
|---|---|---|
| 1 | **F-07** Peek 桌面预览模式 | 有了热键框架后实现成本可控；但 Z 序操作风险高，做成可关闭的实验特性 |
| 2 | **F-17** 分区选项卡（Tabs） | 需要 F-14 的配置迁移与 F-08 的多屏作为前置 |
| 3 | **F-18** 文件夹门户（Folder Portal） | 与 F-17 共享内容模型，建议紧邻实施 |
| 4 | **F-20** 多语言 | 社区规模的函数，放在功能稳定后 |
| 5 | **F-19** 主题包 | 锦上添花，优先级最低 |

**远期的一个战略性建议**：F-17 + F-18 落地后，Layouter 的数据结构就与"分区 = 图标容器"这个模型产生了本质分歧。此时应该同步把 `Plugins/PluginEntry` 的插件 API 升级到 v3，让插件也能访问标签页与门户内容——这才是插件系统相对 Fences 形成壁垒的路径。

### 6.4 暂不建议

以下想法看起来诱人，但对一个小型开源项目而言性价比不成立。写在这里是为了避免反复讨论。

| 想法 | 不建议的理由 |
|---|---|
| **AI 智能图标分类 / 自然语言布局** | 首先，Fences 6 **本身没有 AI 功能**——Stardock 的 AI 能力在 DesktopGPT、Clairvoyance 等独立产品线里，AI 不是这个品类的门槛。其次，实现意味着引入模型依赖、API 密钥管理、联网行为与隐私说明，对"简洁安静、离线可用"的定位是负收益。若真要做，优先考虑做进插件系统而不是内核。 |
| **虚拟桌面 / 自定义桌面** | 与 Windows 10/11 自带的虚拟桌面概念直接冲突，两套桌面切换逻辑并存会让用户彻底混乱；且需要大量 Shell 层 hack，收益不确定。 |
| **内置文件管理器 / 替换 Explorer** | 这是另一个品类的软件（Total Commander 类）。Layouter 的定位是"桌面整理"，不是"文件管理"。做一半反而两头不讨好。 |
| **任务栏替换 / 完整停靠集成** | 侵入性极强，会与 Start11、ExplorerPatcher、WindowBlinds 等成熟工具正面冲突，且 Windows 每次更新都可能打破兼容。小项目承担不起这种维护成本。 |
| **内置脚本引擎（对标 Fences 企业版 scripting）** | 安全风险高（用户脚本在主进程内执行）、沙箱成本高、维护负担大。如果用户需要自动化，用现有的插件系统承载更合适——把脚本能力做进 `PluginEntry` 的 API，比自造一个脚本语言更划算。 |
| **在线账号 / 云同步 / 主题市场** | 需要服务器与运维，对单人维护的开源项目是长期负债。如果要做同步，用本地文件同步（网盘 / Git）就够了。 |
| **macOS / Linux 移植** | Layouter 深度依赖 Windows Shell（`ShellUtil.cs`、`ShortcutUtil.cs`、`DesktopIconService` 的图标视图实现），跨平台等于重写。 |
| **3D 桌面 / 动画特效 / 壁纸轮播** | 与"简洁安静无干扰"的产品定位直接冲突。Fences 走到今天也只做克制的毛玻璃与阴影。 |
| **内置截图 / 标注 / 剪贴板工具** | 赛道拥挤且与定位无关，Windows 自带截图工具已经够用。 |
| **桌面图标整体替换（自定义 Shell 图标视图）** | Fences 与 Layouter 都在"图标视图中托管部分图标"这一有限范围内工作，因为完全接管 Shell 图标视图会破坏 Explorer 的各种行为（拖放、缩略图、右键菜单、搜索）。Layouter 目前的做法是安全的，不建议越线。 |

---

## 7. 参考资料

以下为本次调研实际访问并用于撰写本文档的来源。所有关于 Fences 的功能描述均基于 Stardock 官方一手资料；未能验证的内容已在 §2.2 中显式标注。

**Fences 产品页与帮助中心（一手来源）**

1. Stardock Fences 6 产品页 — https://www.stardock.com/products/fences/
2. Fences 官方 Wiki：如何自定义 Fences — https://wiki.stardocksoftware.com/index.php/How_to_Customize_Stardock_Fences
3. Stardock 帮助中心：Fences Information & Usage — https://support.stardock.com/space/SHC/pages/1331789828/Fences+Information+Usage
4. Fences 6.58 更新日志（2026-10-14，当前最新）— https://stardock.atlassian.net/wiki/spaces/SHC/pages/3856138244/Fences+6.58+Changelog+Released+10+14+2026
5. Fences v6.54 更新日志（2026-08-18）— https://stardock.atlassian.net/wiki/spaces/SHC/pages/3799187483/Fences+v6.54+Changelog+Released+8+18+2026
6. Fences v6.52 更新日志（2026-08-04）— https://stardock.atlassian.net/wiki/spaces/SHC/pages/3613065236/Fences+v6.52+Changelog+Released+8+04+2026
7. Fences 6.46 更新日志（2026-04-27）— https://stardock.atlassian.net/wiki/spaces/SHC/pages/2957279305/Fences+6.46+Changelog+Released+4+27+2026
8. Fences 6.2.0.1 更新日志（2025-12-09）— https://stardock.atlassian.net/wiki/spaces/SHC/pages/2506883119/Fences+6.2.0.1+Changelog+Released+12-09-2025
9. Fences 6.0 更新日志（2025-05-28，正式发布）— https://stardock.atlassian.net/wiki/spaces/SHC/pages/2463989872/Fences+6.0+Changelog+Released+5+28+2025
10. Fences 6 Public Beta 2 更新日志（2025-05-07）— https://stardock.atlassian.net/wiki/spaces/SHC/pages/2455502876/Fences+6+Public+Beta+2+Changelog+Released+05+07+2025
11. Stardock 论坛：Fences 6.0 发布反馈 — https://forums.stardock.com/536148/fences-6-0-release-feedback

**次要对标**

12. Stardock Groupy 2 产品页 — https://www.stardock.com/products/groupy/
13. Microsoft 支持：Snap Your Windows（Snap Layouts / Snap Assist / Snap bar）— https://support.microsoft.com/en-us/windows/experience/snap-your-windows
14. Microsoft Learn：Support snap layouts for desktop apps（布局浮层的调用方式，Win + Z / 悬停最大化按钮）— https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu
15. Microsoft Tech Community：Mastering Snap Layouts and Snap Groups in Windows 11 — https://techcommunity.microsoft.com/discussions/windows11/mastering-snap-layouts-and-snap-groups-in-windows-11/4444796

**检索入口（用于定位上述一手页面）**

16. DuckDuckGo HTML 检索：Stardock Fences 2025 features portal windows snap — https://html.duckduckgo.com/html/?q=Stardock+Fences+2025+features+portal+windows+snap
17. DuckDuckGo HTML 检索：Stardock Fences 6 AI features Clairvoyance DesktopGPT — https://html.duckduckgo.com/html/?q=Stardock+Fences+6+AI+features+Clairvoyance+DesktopGPT+desktop+organizer
18. DuckDuckGo HTML 检索：Stardock Groupy 2 features tabs windows grouping — https://html.duckduckgo.com/html/?q=Stardock+Groupy+2+features+tabs+windows+grouping
19. DuckDuckGo HTML 检索：Microsoft Windows 11 Snap Layouts official support page — https://html.duckduckgo.com/html/?q=Microsoft+Windows+11+Snap+Layouts+official+support+page

**本仓库内部依据**（提案中引用的类名与文件均出自以下路径）

- `Layouter/Layouter.csproj`、`Layouter/docs/readme_cn.md`、`Layouter/docs/readme.md`、`Layouter/docs/changelog.md`、`Layouter/docs/DevelopPlan.md`
- `Layouter/Models/`：`PartitionSettings.cs`、`PartitionData.cs`、`DesktopIcon.cs`、`IconPartition.cs`、`GeneralSettings.cs`
- `Layouter/Services/`：`WindowManagerService.cs`、`DesktopIconService.cs`、`PartitionDataService.cs`、`PartitionSettingsService.cs`、`GeneralSettingsService.cs`、`TrayMenuService.cs`、`TrayIconService.cs`
- `Layouter/ViewModels/`：`DesktopManagerViewModel.cs`、`PartitionManagerViewModel.cs`、`TrayIconViewModel.cs`
- `Layouter/Views/`：`DesktopManagerWindow.xaml`、`PartitionManagerWindow.xaml`、`PartitionSettingsWindow.xaml`、`UpdateWindow.xaml`
- `Layouter/Styles/`：`Theme.xaml`、`AppControls.xaml`、`TrayIconResources.xaml`
- `Layouter/Plugins/`：`PluginManager.cs`、`PluginLoader.cs`、`PluginsManagerWindow.xaml`、`Views/FloatingWindow.xaml`、`Controls/CodeEditor.xaml`
- `Layouter/Utility/`：`SysUtil.cs`、`ShellUtil.cs`、`ShortcutUtil.cs`

---

*本文档仅提出建议，不代表任何一项已经实现。文中工作量与价值均为设计阶段的估算，实际排期应结合当时的人力与社区反馈调整。*
