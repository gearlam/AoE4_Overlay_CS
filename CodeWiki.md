# AoE4 Overlay CS — Code Wiki

> 本文档基于仓库全部源码（版本 `1.7.6`，commit `8305cec` 之后）逐文件分析生成。
> 生成时间：2026-09-05。

## 目录

1. [项目概述](#1-项目概述)
2. [整体架构](#2-整体架构)
3. [目录结构](#3-目录结构)
4. [模块职责总览](#4-模块职责总览)
5. [关键类与函数说明](#5-关键类与函数说明)

   - [5.1 应用入口：App](#51-应用入口app)

   - [5.2 主窗口：MainWindow](#52-主窗口mainwindow)

   - [5.3 Models 层：AppSettings](#53-models-层appsettings)

   - [5.4 ViewModels 层](#54-viewmodels-层)

   - [5.5 Services 层](#55-services-层)

   - [5.6 Views 层](#56-views-层)

   - [5.7 HTML Overlay（html/）](#57-html-overlayhtml)

   - [5.8 静态资源](#58-静态资源)
6. [依赖关系](#6-依赖关系)
7. [核心链路（数据流）](#7-核心链路数据流)
8. [WebSocket 协议](#8-websocket-协议)
9. [构建与运行](#9-构建与运行)
10. [配置与日志](#10-配置与日志)
11. [扩展与二次开发](#11-扩展与二次开发)
12. [已知行为特性与注意事项](#12-已知行为特性与注意事项)

***

## 1. 项目概述

**AoE4 Overlay CS** 是一款面向《Age of Empires IV（帝国时代 4）》的 Windows 桌面 Overlay 辅助工具，使用 **C# + WPF** 开发（目标框架 `net10.0-windows`）。

**项目定位**：不是游戏 Mod、不是注入式插件，而是独立运行的桌面程序。数据来自 `aoe4world.com` 公开 API，展示依靠「透明置顶 WPF 窗口」与「本地 HTML + WebSocket」双通道。

### 核心功能

| 功能           | 说明                                                  |
| ------------ | --------------------------------------------------- |
| 玩家绑定         | 按 Profile ID / Steam ID / 玩家名搜索并绑定玩家，持久化保存          |
| 对局 Overlay   | 游戏对局中以透明置顶窗口展示双方阵容、文明、段位、胜率等信息                      |
| 历史战绩         | 展示最近 N 场比赛（双方阵容、地图、模式、胜负、分差、对战 ID）                  |
| WebSocket 输出 | 本地启动 WebSocket 服务，向 HTML / OBS 浏览器源广播 `player_data` |
| 多语言          | 中英文界面切换；地图名（84 条）、文明名（29 条）中英双语映射                   |
| 全局热键         | 自定义热键控制 Overlay 显隐与位置锁定/解锁；系统注册失败自动回退 Win32 Hook    |
| 搜索历史         | 记录最近 20 条搜索，支持复用与单条删除                               |
| 系统托盘         | 主窗口关闭后隐藏到托盘常驻，后台持续轮询                                |

### 版本信息

- 程序集版本：`1.7.6`（`Version` / `AssemblyVersion` / `FileVersion` / `InformationalVersion` 四者同步，见 [AoE4OverlayCS.csproj](AoE4OverlayCS.csproj)）

- 目标框架：`net10.0-windows`

- SDK 版本：`10.0.301`（[global.json](global.json) 钉住，`rollForward: latestFeature`）

***

## 2. 整体架构

项目采用经典的 **MVVM 分层 + Service 服务层** 模式，单项目结构，无单元测试项目。

```
AoE4OverlayCS (WPF Desktop App)
│
├─ UI 层 (Views)
│  ├─ MainWindow          主窗口：菜单、Tab、托盘、语言切换
│  ├─ SettingsView        设置页：玩家搜索、搜索历史、热键录制、字号/间距
│  ├─ GamesView           对战记录页：历史战绩列表 + 超链接
│  └─ OverlayWindow       游戏内覆盖层：动态构建玩家行、锁定/穿透/缩放
│
├─ ViewModel 层
│  ├─ MainViewModel       核心协调器（业务编排中心，持有全部 Service）
│  ├─ RelayCommand        ICommand 通用实现
│  ├─ PlayerDisplayInfo   历史战绩中的玩家展示结构
│  └─ MatchHistoryItem    历史战绩单条记录结构
│
├─ Service 层
│  ├─ ApiCheckerService       轮询 aoe4world API，检测新对局
│  ├─ GameProcessor           原始对局 JSON → 展示结构（static）
│  ├─ WebSocketServerService  Fleck WebSocket 广播服务
│  ├─ SettingsService         config/config.json 读写
│  ├─ GlobalHotkeyService     低阶键盘 Hook 热键兜底
│  ├─ MapNameTranslator       地图名中英翻译（85+ 条）
│  ├─ CivNameTranslator       文明名中英翻译（29 条）
│  ├─ CivIconResolver         文明图标多路径查找（根命名空间）
│  ├─ WindowServices          Win32 鼠标穿透封装
│  ├─ OverlayScaleCalculator  Overlay 内容等比缩放算法
│  └─ LogPaths                日志目录工具（根命名空间）
│
├─ Model 层
│  └─ AppSettings         全部可持久化配置项（INotifyPropertyChanged）
│
└─ 静态资源
   ├─ html/                HTML Overlay（jQuery + WebSocket）
   ├─ img/                 文明旗(webp)、国旗(png)、图标
   └─ Resources/           多语言字符串 XAML 字典
```

### 线程模型

| 线程/上下文              | 承担的工作                                               |
| ------------------- | --------------------------------------------------- |
| WPF UI 线程           | 所有窗口创建与 UI 更新（通过 `Dispatcher.Invoke` 从后台切回）         |
| `Task.Run` 后台线程     | `ApiCheckerService.Loop` 轮询循环、`RefreshHistory` 历史拉取 |
| Fleck 内部线程          | WebSocket 收发与广播                                     |
| Win32 Hook 线程（安装线程） | `GlobalHotkeyService` 低阶键盘钩子回调                      |

> 关键约定：`MainViewModel` 中所有触碰 UI 的路径（`OnNewGame` / `UpdateOverlayWithLastGame` / `ToggleOverlay` / `RefreshHistory` 填充 `Games`）都先经过 `Application.Current.Dispatcher.Invoke`。

### 双展示通道

```
                    ┌────────────────────────────┐
 aoe4world API ───► │ ApiCheckerService (轮询)    │
                    └────────────┬───────────────┘
                                 ▼
                    GameProcessor.ProcessGame（标准化）
                                 │
              ┌──────────────────┴──────────────────┐
              ▼                                     ▼
   OverlayWindow.UpdateData              WebSocketServerService.Send
   （WPF 原生覆盖层，立即刷新）           （广播 player_data）
              │                                     ▼
              │                        html/overlay.html + main.js
              │                        （OBS 浏览器源 / 自定义前端）
              └── 两路输出内容与样式基本对齐（配色、列宽、镜像布局）
```

***

## 3. 目录结构

```
AoE4_Overlay_CS/
├─ AoE4OverlayCS.csproj         项目文件（NuGet 依赖、静态资源复制）
├─ global.json                  .NET SDK 版本钉住 (10.0.301)
├─ dotnet-install.ps1           SDK 安装辅助脚本
├─ App.xaml / App.xaml.cs       WPF 应用入口（单实例、全局异常、启停编排）
├─ MainWindow.xaml(.cs)         主窗口 UI + 托盘 + 语言切换
├─ AssemblyInfo.cs              WPF 主题信息（ThemeInfo）
├─ README.md / README_en.md     中英文说明文档
├─ CodeWiki.md                  本文档
│
├─ Models/
│  └─ AppSettings.cs            配置模型（INPC，全部绑定字段）
│
├─ ViewModels/
│  ├─ MainViewModel.cs          核心 VM + PlayerDisplayInfo + MatchHistoryItem
│  └─ RelayCommand.cs           ICommand 通用实现
│
├─ Services/
│  ├─ ApiCheckerService.cs      aoe4world API 轮询器
│  ├─ GameProcessor.cs          对局数据处理器（static）
│  ├─ WebSocketServerService.cs Fleck WebSocket 服务端
│  ├─ SettingsService.cs        配置读写 (config/config.json)
│  ├─ GlobalHotkeyService.cs    WH_KEYBOARD_LL 热键兜底
│  ├─ MapNameTranslator.cs      地图名翻译表
│  ├─ CivNameTranslator.cs      文明名翻译表
│  ├─ CivIconResolver.cs        文明图标解析（注意：位于根命名空间 AoE4OverlayCS）
│  ├─ WindowServices.cs         鼠标穿透 Win32 封装
│  ├─ OverlayScaleCalculator.cs 等比缩放算法
│  └─ LogPaths.cs               日志目录工具（位于根命名空间 AoE4OverlayCS）
│
├─ Views/
│  ├─ SettingsView.xaml(.cs)    设置 Tab（搜索 + 热键录制 + 外观）
│  ├─ GamesView.xaml(.cs)       历史战绩 Tab（列表 + 超链接）
│  └─ OverlayWindow.xaml(.cs)   覆盖层窗口（动态布局 + 图片加载 + 缩放）
│
├─ Resources/
│  ├─ Strings.en-US.xaml        英文字符串字典
│  └─ Strings.zh-CN.xaml        中文字符串字典
│
├─ html/
│  ├─ overlay.html              HTML Overlay 入口
│  ├─ main.js                   WebSocket 连接 + 数据渲染
│  ├─ main.css                  默认样式（CSS 变量驱动）
│  ├─ custom.js / custom.css    用户自定义钩子（空文件，升级不覆盖）
│  └─ jquery.min.js             jQuery 依赖
│
├─ img/
│  ├─ flags/                    23 个文明旗（webp）
│  ├─ countries/                254 面国旗（png，ISO 两位码命名）
│  └─ aoe4_sword_shield.ico     应用/托盘/Overlay 图标
│  （注：CivIconResolver 还会探测 img/build_order/civilization_flag/
│   作为图标回退路径，该目录当前仓库中不存在，见 §5.5.8）
│
└─ docs/
   └─ superpowers/plans/        历史设计文档（Overlay 缩放方案）
```

***

## 4. 模块职责总览

| 模块           | 文件                                                                       | 单一职责                           |
| ------------ | ------------------------------------------------------------------------ | ------------------------------ |
| 应用生命周期       | [App.xaml.cs](App.xaml.cs)                                               | 单实例互斥、全局异常兜底、VM/窗口创建、启停编排      |
| 主窗口壳         | [MainWindow.xaml.cs](MainWindow.xaml.cs)                                 | 托盘、版本号标题、语言热切换、目录打开            |
| 配置模型         | [Models/AppSettings.cs](Models/AppSettings.cs)                           | 全部可持久化字段的载体，INPC 支持双向绑定        |
| 业务编排         | [ViewModels/MainViewModel.cs](ViewModels/MainViewModel.cs)               | 搜索/绑定/历史/Overlay/热键/WS 的唯一协调者  |
| 命令基础设施       | [ViewModels/RelayCommand.cs](ViewModels/RelayCommand.cs)                 | `ICommand` 委托封装                |
| API 拉取       | [Services/ApiCheckerService.cs](Services/ApiCheckerService.cs)           | aoe4world HTTP 调用 + 定时轮询新对局检测  |
| 数据加工         | [Services/GameProcessor.cs](Services/GameProcessor.cs)                   | 原始对局 JSON → Overlay/WS 消费的标准结构 |
| WS 广播        | [Services/WebSocketServerService.cs](Services/WebSocketServerService.cs) | Fleck 服务端、历史消息回放、全量广播          |
| 配置持久化        | [Services/SettingsService.cs](Services/SettingsService.cs)               | config/config.json 加载与保存       |
| 热键兜底         | [Services/GlobalHotkeyService.cs](Services/GlobalHotkeyService.cs)       | NHotkey 失败时的低阶键盘 Hook          |
| 本地化          | MapNameTranslator / CivNameTranslator                                    | 地图名、文明名的 zh-CN 翻译              |
| 图标解析         | [Services/CivIconResolver.cs](Services/CivIconResolver.cs)               | 文明名 → 图标文件路径（多路径按序查找）          |
| 窗口互操作        | [Services/WindowServices.cs](Services/WindowServices.cs)                 | `WS_EX_TRANSPARENT` 鼠标穿透样式     |
| 缩放算法         | [Services/OverlayScaleCalculator.cs](Services/OverlayScaleCalculator.cs) | Overlay 内容等比缩放比例计算             |
| 日志路径         | [Services/LogPaths.cs](Services/LogPaths.cs)                             | `logs/` 目录懒创建与路径拼接             |
| 设置页          | [Views/SettingsView.xaml.cs](Views/SettingsView.xaml.cs)                 | 热键录制状态机、搜索交互、历史下拉              |
| 战绩页          | [Views/GamesView.xaml.cs](Views/GamesView.xaml.cs)                       | 超链接跳转                          |
| 覆盖层          | [Views/OverlayWindow.xaml.cs](Views/OverlayWindow.xaml.cs)               | 动态构建玩家行、锁定/穿透、缩放、图片缓存          |
| HTML Overlay | [html/main.js](html/main.js)                                             | WS 客户端、断线重连、DOM 渲染             |

***

## 5. 关键类与函数说明

### 5.1 应用入口：App

位置：[App.xaml.cs](App.xaml.cs)（XAML：[App.xaml](App.xaml)，`ShutdownMode="OnMainWindowClose"`）

| 成员                                 | 签名         | 说明                                                                                                                                                   |
| ---------------------------------- | ---------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `App()`                            | 构造函数       | ① `Mutex("AoE4OverlayCS_Mutex")` 单实例检查，重复启动弹框后 `Shutdown()`；② 注册 `DispatcherUnhandledException` 与 `AppDomain.UnhandledException`                     |
| `App_DispatcherUnhandledException` | 事件处理       | UI 线程未捕获异常整体写入 `logs/dispatcher_error.log`（`File.WriteAllText`，覆盖式）                                                                                  |
| `CurrentDomain_UnhandledException` | 事件处理       | 后台线程未捕获异常写入 `logs/domain_error.log`                                                                                                                  |
| `OnStartup`                        | `override` | 初始化日志目录 → `new MainViewModel()` → `MainWindow.SetLanguage(Settings.Language)`（启动即应用持久化语言）→ 创建 `MainWindow` 并绑定 DataContext → `Show()` → `vm.Start()` |
| `OnExit`                           | `override` | `vm.Stop()`（停服务、存几何、写配置）                                                                                                                             |

启动时序细节：语言切换发生在窗口创建**之前**，因此主窗口首次渲染即使用上次的语言。

### 5.2 主窗口：MainWindow

位置：[MainWindow.xaml.cs](MainWindow.xaml.cs)（XAML：[MainWindow.xaml](MainWindow.xaml)，标题 `AoE IV: Overlay (C# Remake)`，860×640，禁 WinForms 风格的自绘 TabControl）

**UI 组成**（XAML）：

- 顶部菜单（全部 `DynamicResource` 绑定多语言键）：

  - File：Html 文件目录 / 配置与日志目录 / 退出

  - Settings：`LogMatches` 勾选项（`IsChecked="{Binding Settings.LogMatches}"`）

  - Links：GitHub 仓库（`OpenLinkCommand`）

  - Language：English / 中文

- TabControl 两个页签：Settings（SettingsView）、Games（GamesView）

**关键方法**：

| 方法                               | 说明                                                                                                                                                                                                      |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ApplyVersionToWindowTitle()`    | 读 `AssemblyInformationalVersionAttribute`，截掉 `+` 后的源版本后缀，若标题已含则跳过；否则插入/追加到标题括号内。注释明确：改版本请改 csproj，不要改这里                                                                                                 |
| `InitializeTrayIcon()`           | WinForms `NotifyIcon`；图标取 `img/aoe4_sword_shield.ico`，缺失回退 `SystemIcons.Application`；双击恢复窗口；右键菜单 Open/Exit；异常写 `tray_error.log`                                                                         |
| `OnClosing`                      | `_isExitRequested == false` 时 `Cancel + Hide()`（最小化到托盘）；为 true 时放行真正关闭                                                                                                                                  |
| `Exit_Click` / 托盘 Exit           | 置 `_isExitRequested = true` → `vm.Stop()` → `Close()` → 触发 `App.OnExit`                                                                                                                                 |
| `OpenHtmlFiles_Click`            | 依次探测 3 个候选路径（输出目录 `html/`、向上回溯 4 层、再回溯 5 层的项目目录），打开 Explorer                                                                                                                                            |
| `OpenConfigLogs_Click`           | 打开 `logs/` 并用 `/select` 选中最新修改的 `.log` 文件                                                                                                                                                               |
| `SetLanguage(cultureName, save)` | **static**。非 `zh-CN` 一律归一为 `en-US` → 设置线程 Culture → 移除旧的 `Resources/Strings.*` 合并字典 → 加入新字典；`save=true` 时写回 `Settings.Language`、`SaveCurrentSettings()` 并异步 `RefreshLocalizedDataAfterLanguageChange()` |

### 5.3 Models 层：AppSettings

位置：[Models/AppSettings.cs](Models/AppSettings.cs)

全部可持久化配置项。除 `SearchHistory` / `TeamColors` 外均实现 `INotifyPropertyChanged`（二者为普通自动属性，集合整体替换触发保存）。

| 属性                       | 类型                   | 默认值         | 说明                                              |
| ------------------------ | -------------------- | ----------- | ----------------------------------------------- |
| `WebsocketPort`          | `int`                | `7307`      | WS 监听端口（**构造 VM 时固定，改配置需重启程序**，见 §12）           |
| `LogMatches`             | `bool`               | `true`      | 「记录比赛数据」菜单勾选位（当前仅作为配置项持久化）                      |
| `Interval`               | `int`                | `60`        | API 轮询间隔（秒），每轮循环实时读取，改后下轮生效                     |
| `AppWidth` / `AppHeight` | `double`             | `900 / 600` | **未使用字段**（主窗口尺寸由 XAML 固定，见 §12）                 |
| `SteamId`                | `string?`            | `null`      | 绑定玩家 Steam ID                                   |
| `ProfileId`              | `string?`            | `null`      | 绑定玩家 aoe4world Profile ID（**主键**，所有 API 依赖它）    |
| `PlayerName`             | `string?`            | `null`      | 绑定玩家名                                           |
| `OverlayHotkey`          | `string`             | `""`        | 显隐热键，格式 `Ctrl+Alt+O`（WPF `Key` 枚举名）             |
| `OverlayPositionHotkey`  | `string`             | `""`        | 锁定/解锁位置热键，同上格式                                  |
| `OverlayGeometry`        | `double[]?`          | `null`      | `[x, y, w, h]`，Overlay 关闭时写回                    |
| `FontSize`               | `int`                | `12`        | Overlay 字号（界面可选 10\~24）                         |
| `TeamGap`                | `double`             | `12`        | 队伍间距（Slider 0~~80，OverlayWindow 内再 clamp 0~~40） |
| `MaxGamesHistory`        | `int`                | `20`        | 历史战绩拉取条数                                        |
| `CivStatsColor`          | `string`             | `#BC8AEA`   | Games 页文明文字颜色                                   |
| `OpenOverlayOnNewGame`   | `bool`               | `true`      | 新对局/首次轮询命中时自动 Show Overlay                      |
| `Language`               | `string`             | `"en-US"`   | `en-US` 或 `zh-CN`                               |
| `SearchHistory`          | `List<string>`       | `[]`        | 搜索历史（上限 20）                                     |
| `TeamColors`             | `List<List<object>>` | 5 组         | 队伍徽章背景 `[R, G, B, Alpha(0~1)]`                  |

### 5.4 ViewModels 层

#### MainViewModel（项目中枢）

位置：[ViewModels/MainViewModel.cs](ViewModels/MainViewModel.cs)

实现 `INotifyPropertyChanged`，**持有全部 Service 实例**，是所有业务链路的编排中心。

**字段与依赖**：

```csharp
_settingsService   SettingsService          // 配置
_apiChecker        ApiCheckerService        // API 轮询（依赖 SettingsService）
_wsServer          WebSocketServerService   // WS 广播（端口取自构造时配置）
_globalHotkey      GlobalHotkeyService      // 显隐热键 Hook 兜底
_globalHotkeyPosition GlobalHotkeyService   // 位置热键 Hook 兜底
_overlayWindow     OverlayWindow?           // 覆盖层窗口（Start 时创建）
```

**公开属性 / 命令**：

| 成员                                       | 类型                                       | 说明                                          |
| ---------------------------------------- | ---------------------------------------- | ------------------------------------------- |
| `Settings`                               | `AppSettings`                            | 透传 `_settingsService.Current`               |
| `SearchQuery`                            | `string`                                 | 搜索框文本（绑定 ComboBox Text）                     |
| `ProfileInfo`                            | `string`                                 | 绑定玩家展示文本（初始 `No player identified`）         |
| `ProfileLink`                            | `string`                                 | `https://aoe4world.com/players/{ProfileId}` |
| `SearchStatusText` / `SearchStatusBrush` | `string` / `Brush`                       | 搜索结果提示与颜色（橙红=失败，绿=成功）                       |
| `Games`                                  | `ObservableCollection<MatchHistoryItem>` | 历史战绩                                        |
| `SearchHistory`                          | `ObservableCollection<string>`           | 搜索历史下拉                                      |
| `SearchPlayerCommand`                    | `ICommand`                               | 玩家搜索                                        |
| `SaveSettingsCommand`                    | `ICommand`                               | 保存并重启服务（Stop + Start）                       |
| `ToggleOverlayCommand`                   | `ICommand`                               | 切换 Overlay 显隐                               |
| `ChangeOverlayPositionCommand`           | `ICommand`                               | 切换 Overlay 锁定                               |
| `OpenLinkCommand`                        | `ICommand`                               | 默认浏览器打开 URL（CommandParameter 传 URL）         |

**核心方法**：

| 方法                                          | 签名           | 说明                                                                                                                                                                          |
| ------------------------------------------- | ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Start()`                                   | `void`       | `wsServer.Start()` → `apiChecker.Start()` → `Dispatcher.Invoke`：创建 `OverlayWindow`、`UpdateHotkeyRegistration()`、已绑定玩家时后台 `RefreshHistory()`                                 |
| `Stop()`                                    | `void`       | 停 API 轮询 / WS / 两个 Hook → `_overlayWindow.SaveState() + Close()` → `_settingsService.Save()`                                                                                |
| `SearchPlayer()`                            | `async Task` | 空输入提示（中文「请输入用户ID」）→ `FindPlayer` → 命中：记历史、写 `ProfileId/PlayerName/SteamId`、`Save`、`UpdateProfileDisplay`、`RefreshHistory`、`UpdateOverlayWithLastGame`；未命中：红色 `ID not found` |
| `UpdateOverlayWithLastGame()`               | `async Task` | `GetLastGame` → `GameProcessor.ProcessGame` → `wsServer.Send("player_data")` → Dispatcher：`UpdateData` + 按 `OpenOverlayOnNewGame` 决定是否 `Show()`                             |
| `RefreshHistory()`                          | `async Task` | `GetMatchHistory(MaxGamesHistory)` → Dispatcher 内逐条转 `MatchHistoryItem` 填入 `Games`（单条解析异常静默跳过）                                                                              |
| `UpdateHotkeyRegistration()`                | `void`       | 解析两个热键字符串 → 优先 `NHotkey` `AddOrReplace`；抛异常则回退 `GlobalHotkeyService`（Configure + Start）；全部动作写 `hotkey.log`                                                                  |
| `ToggleOverlay()`                           | `void`       | Dispatcher 切 UI 线程 → `overlay.ToggleVisibility()`                                                                                                                           |
| `ChangeOverlayPosition()`                   | `void`       | `overlay.ToggleLock()`                                                                                                                                                      |
| `SaveSettings()`                            | `void`       | `Stop() + Start()`（热键即时重注册）                                                                                                                                                 |
| `SaveCurrentSettings()`                     | `void`       | 仅写盘                                                                                                                                                                         |
| `RefreshLocalizedDataAfterLanguageChange()` | `async Task` | `RefreshHistory + UpdateOverlayWithLastGame`（语言切换后即时刷新地图/文明名）                                                                                                               |
| `AddSearchHistory(query)`                   | `void`       | 忽略大小写去重 → 头插 → 截断至 20 → 同步回 `Settings.SearchHistory`                                                                                                                        |
| `RemoveSearchHistory(query)`                | `void`       | 删除单条 → `Save`；若当前搜索框恰为该条则清空                                                                                                                                                 |
| `OnNewGame(gameData)`                       | `private`    | 轮询回调：`ProcessGame` → `Send` → Dispatcher `UpdateData`（+可选 Show）→ `Task.Run(RefreshHistory)`                                                                                 |
| `TranslateMode` / `TranslateResult`         | `static`     | zh-CN 下 `rm*→排位赛*`、`win→赢`、`loss→输`；其他语言原样                                                                                                                                  |

> ⚠️ 搜索状态文案（`请输入用户ID` / `ID Found` / `ID not found`）为硬编码字符串，未走资源字典（见 §12）。

#### 内嵌数据类（同文件）

**`PlayerDisplayInfo`** — 历史战绩中每名玩家：

| 属性                                        | 说明                                                 |
| ----------------------------------------- | -------------------------------------------------- |
| `Name` / `ProfileId` / `ProfileIdDisplay` | 玩家名、ID、ID 展示                                       |
| `Civ`                                     | 文明名（已翻译）                                           |
| `CivColor`                                | 文明文字颜色（取 `Settings.CivStatsColor`）                 |
| `ProfileUrl`                              | 计算属性 → `https://aoe4world.com/players/{ProfileId}` |

**`MatchHistoryItem`** — 历史战绩单行：

| 属性                                                   | 说明                                                                 |
| ---------------------------------------------------- | ------------------------------------------------------------------ |
| `Team1Players` / `Team2Players`                      | `List<PlayerDisplayInfo>`                                          |
| `Team1Display` / `Team2Display`                      | 文本拼接（`名字 (文明)` 换行分隔）                                               |
| `Map` / `Started` / `Mode` / `Result` / `RatingDiff` | 地图（翻译，空回退 `?`）、本地化时间（`g` 格式）、模式（翻译）、胜负（翻译）、分差                      |
| `MatchId` / `ProfileId`                              | 来自 `game_id` / 当前绑定                                                |
| `GameUrl`                                            | 计算属性 → `https://aoe4world.com/players/{ProfileId}/games/{MatchId}` |

#### RelayCommand

位置：[ViewModels/RelayCommand.cs](ViewModels/RelayCommand.cs)

标准 `ICommand` 委托实现：构造传入 `Action<object?>` + 可选 `Predicate<object?>`；`CanExecuteChanged` 挂接 `CommandManager.RequerySuggested` 自动刷新。

### 5.5 Services 层

#### 5.5.1 ApiCheckerService — API 拉取与轮询

位置：[Services/ApiCheckerService.cs](Services/ApiCheckerService.cs)

**事件**：

| 事件          | 签名                | 触发时机                                      |
| ----------- | ----------------- | ----------------------------------------- |
| `OnNewGame` | `Action<JObject>` | 轮询发现 `started_at` 晚于上次记录（**首轮必触发**，见 §12） |
| `OnError`   | `Action<string>`  | 轮询循环内异常（当前仅 `Debug.WriteLine`）            |

**方法**：

| 方法                          | 端点 / 逻辑                                                                                                |
| --------------------------- | ------------------------------------------------------------------------------------------------------ |
| `Start()` / `Stop()`        | `CancellationTokenSource` 控制的 `Task.Run(Loop)`；Stop 仅 Cancel 不等待                                       |
| `Loop(token)`               | `private`。已绑定 ProfileId 时 `CheckLastGame`；随后 `Task.Delay(Interval * 1000)`。间隔每轮实时读取                    |
| `CheckLastGame()`           | `private`。取 `GetLastGame`，`started_at > _lastMatchTime` 则更新基线并返回该局（首轮 `_lastMatchTime = MinValue` 必命中） |
| `GetLastGame()`             | `GET /api/v0/players/{pid}/games/last`；响应含 `error` 键或异常时返回 null                                        |
| `GetMatchHistory(limit=10)` | `GET /api/v0/players/{pid}/games?limit=N` → `json["games"]`                                            |
| `FindPlayer(query)`         | 纯数字 → 先 `GET /players/{query}` 直查；失败或非数字 → `GET /players/search?query={q}` 取 `players[0]`；整体失败返回 null  |

> `HttpClient.Timeout = 10s`；所有请求异常一律吞掉返回 null，不中断轮询。

#### 5.5.2 GameProcessor — 对局数据加工（static）

位置：[Services/GameProcessor.cs](Services/GameProcessor.cs)

唯一入口：`ProcessGame(JObject gameData, AppSettings settings) → object`（`Dictionary<string, object>`）。

**处理流程**：

```
1. 顶层字段：
   map      = MapNameTranslator.Translate(map, language)
   mode     = leaderboard_id (int)
   started  = started_at
   ranked   = kind 含 "qm_" 或 "rm_"
   server   = server
   match_id = game_id

2. 模式归一：kind ∈ {rm_4v4, rm_3v3, rm_2v2} → "rm_team"

3. 遍历 teams（JArray of JArray）：
   - 给每个 player 注入 team 索引（直接改写原始 JObject）
   - profile_id == settings.ProfileId → 记录 mainTeam

4. 玩家排序：主队优先，其次按队号

5. 每名玩家：
   a. modes[lookupMode] 缺失时 rm_↔qm_ 互换回退一次
   b. modes[lookupMode].civilizations 中按当前文明匹配
      → civ_games / civ_winrate（格式化为 P1 百分比）
   c. modes[lookupMode] → rating / rank / wins_count / losses_count / win_rate

6. 输出匿名对象列表 → result["players"]
```

**每名玩家输出字段**：

`civ`（下划线→空格）、`civ_display`（翻译）、`name`、`team`（索引+1，从 1 起）、`country`、`rating`、`rank`（格式 `{RM|QM}#{rank}`）、`wins`、`losses`、`winrate`（`{x}%`）、`civ_games`、`civ_winrate`、`civ_win_length_median`（当前恒为空串）。

#### 5.5.3 WebSocketServerService — WS 广播

位置：[Services/WebSocketServerService.cs](Services/WebSocketServerService.cs)

基于 **Fleck**，监听 `ws://0.0.0.0:{port}`（端口构造时固定）。

| 方法                 | 说明                                                                      |
| ------------------ | ----------------------------------------------------------------------- |
| `Start()`          | 启动监听；`OnOpen`：加入连接表并**立即补发历史消息**（有历史时发第一条；超过 1 条再发最后一条——新客户端最多收到 2 条补发） |
| `Stop()`           | `_server.Dispose()`                                                     |
| `Send(type, data)` | 序列化 `{type, data}` → 入历史缓存（上限 50 条，FIFO）→ 广播全部在线连接                      |

线程安全：`_sockets` / `_messageHistory` 均以 `lock` 保护。

#### 5.5.4 SettingsService — 配置持久化

位置：[Services/SettingsService.cs](Services/SettingsService.cs)

| 成员        | 说明                                                               |
| --------- | ---------------------------------------------------------------- |
| 构造函数      | 确保 `{BaseDirectory}/config/` 存在 → `new AppSettings()` → `Load()` |
| `Current` | `AppSettings`，外部直接改属性后需手动 `Save()` 落盘                            |
| `Load()`  | 反序列化 `config/config.json`，失败静默保持默认                               |
| `Save()`  | `Formatting.Indented` 写盘，失败静默                                    |

#### 5.5.5 GlobalHotkeyService — 热键 Hook 兜底

位置：[Services/GlobalHotkeyService.cs](Services/GlobalHotkeyService.cs)

`sealed class`，实现 `IDisposable`。当 `NHotkey.Wpf` 注册失败（如热键被占用）时由 `MainViewModel` 启用，基于 `SetWindowsHookEx(WH_KEYBOARD_LL=13)` 低阶键盘钩子。

| 成员                               | 说明                                                                                                        |
| -------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `IsActive`                       | `bool`，Hook 是否已安装                                                                                         |
| `Configure(hotkey, onTriggered)` | 解析热键串（`Ctrl/Control`、`Shift`、`Alt` + WPF `Key` 枚举名），存触发键与修饰符                                              |
| `Start()`                        | 安装 Hook（`GetModuleHandle(当前模块)`），先 Stop 防重复                                                               |
| `Stop()`                         | `UnhookWindowsHookEx`                                                                                     |
| `HookCallback`                   | `private`。只处理触发键的**按下沿**（`WM_KEYDOWN/WM_SYSKEYDOWN` 且非按住重复），用 `GetKeyState` 实时取修饰符并**精确匹配**后回调；KeyUp 复位状态 |
| Win32 导入                         | `SetWindowsHookEx` / `UnhookWindowsHookEx` / `CallNextHookEx` / `GetModuleHandle` / `GetKeyState`         |

#### 5.5.6 MapNameTranslator — 地图名翻译

位置：[Services/MapNameTranslator.cs](Services/MapNameTranslator.cs)

静态字典 `ZhCnMapNames`（85+ 条，忽略大小写）。`Translate(mapName, language)`：先归一化（Trim、`_`→空格）；非 zh-CN 直接返回归一化值；zh-CN 查字典，未命中返回归一化原名。

调用点：`GameProcessor.ProcessGame`、`MainViewModel.RefreshHistory`。

#### 5.5.7 CivNameTranslator — 文明名翻译

位置：[Services/CivNameTranslator.cs](Services/CivNameTranslator.cs)

静态字典 `ZhCnCivNames`（29 条：Abbasid、Ayyubids、Byzantines、Chinese、Delhi、English、French、Golden Horde、House of Lancaster、HRE、Japanese、Jeanne Darc、Jin Dynasty、Knights Templar、Macedonian、Malians、Mongols、Order of the Dragon、Ottomans、Rus、Sengoku Daimyo、Tughlaq、Zhu Xi's Legacy、Venetians、Poles 等）。

`Translate(civName, language)`：归一化（Trim、`_`→空格、去 `'`）后查表；非 zh-CN 原样返回。

调用点：`GameProcessor.ProcessGame`、`MainViewModel.RefreshHistory`。

#### 5.5.8 CivIconResolver — 文明图标解析

位置：[Services/CivIconResolver.cs](Services/CivIconResolver.cs)（**注意：命名空间为根** **`AoE4OverlayCS`，非** **`AoE4OverlayCS.Services`**）

内部 `CivCodeMapping` 字典（70 条，忽略大小写）：文明缩写（`abb`/`chi`/`eng`/`hre`/`jda`...）、全称、下划线/空格/撇号变体 → 标准图标名（`Abbasid`/`Chinese`/`JeanneDArc`/`KnightsTemplar`...）。

`Resolve(baseDir, civ, civKey)` 按序探测（返回第一个存在的文件）：

1. `img/build_order/civilization_flag/CivIcon-{civ映射名}AoE4.png`
2. `img/build_order/civilization_flag/CivIcon-{civ映射名}AoE4_spacing.png`
3. 同上两条，但用 `civKey` 映射
4. `img/build_order/civilization_flag/{civKey}.webp` / `.png`
5. `img/flags/{civ}.webp` / `.png`

全部未命中返回 null（调用方写 `image_load_error.log`）。

#### 5.5.9 WindowServices — 鼠标穿透

位置：[Services/WindowServices.cs](Services/WindowServices.cs)

Win32 封装，操作窗口扩展样式（`GWL_EXSTYLE = -20`）：

- `SetWindowExTransparent(Window)`：追加 `WS_EX_TRANSPARENT (0x20)` → 鼠标点击穿透

- `RemoveWindowExTransparent(Window)`：移除该位 → 恢复交互

调用点：`OverlayWindow.OnSourceInitialized`（初始锁定）、`OverlayWindow.ToggleLock`。

#### 5.5.10 OverlayScaleCalculator — 缩放算法

位置：[Services/OverlayScaleCalculator.cs](Services/OverlayScaleCalculator.cs)

```csharp
ComputeScale(clientW, clientH, baseW, baseH)
```

- 任一尺寸 ≤ 0 → 返回 1.0（防除零）

- `scale = min(clientW/baseW, clientH/baseH)`

- 再与 `(clientW-2)/baseW`、`(clientH-2)/baseH` 取 min（四周各留 1px 防贴边裁剪）

- `Clamp` 到 `[MinScale=0.5, MaxScale=3.0]`

调用点：`OverlayWindow.ApplyScale`。

#### 5.5.11 LogPaths — 日志目录

位置：[Services/LogPaths.cs](Services/LogPaths.cs)（**命名空间为根** **`AoE4OverlayCS`**）

- `LogsDirectory`：懒创建单例，`{BaseDirectory}/logs/`（double-check lock）

- `Get(fileName)`：拼接完整路径

### 5.6 Views 层

#### SettingsView — 设置页

位置：[Views/SettingsView.xaml.cs](Views/SettingsView.xaml.cs)（XAML：[Views/SettingsView.xaml](Views/SettingsView.xaml)）

两个 GroupBox：

1. **Profile（档案）**

   - `ProfileInfo` 文本 + `ProfileLink` 超链接（`OpenLinkCommand`）

   - 搜索框：**可编辑 ComboBox 兼作搜索历史下拉**（`Text` 绑定 `SearchQuery`，`ItemsSource` 绑定 `SearchHistory`）

   - 历史项模板：文本 + 「删除」按钮（`DeleteSearchHistory_Click`，`Tag` 传文本；删除后保持下拉展开）
2. **Overlay（覆盖层）**

   - 显隐热键按钮 + 位置热键按钮（录制状态机）

   - 字号 ComboBox（10/11/12/13/14/16/18/20/22/24）

   - 队伍间距 Slider（0\~80，步长 4，实时数值显示）

**热键录制状态机**（`HotkeyButton_Click/PreviewKeyDown`，位置热键独立一套）：

- 点击 → `_isRecording = true`，按钮显示 `Press any key...`

- `PreviewKeyDown`：单独的修饰键/Wins 键不计；`Ctrl/Shift/Alt+` 前缀拼接；**Back/Delete = 清空热键**；**Esc = 取消录制**（恢复绑定显示）；其余键写入 `Settings.OverlayHotkey` 并立即 `UpdateHotkeyRegistration()`

> 「删除」按钮文案为硬编码中文，未走资源字典。

#### GamesView — 历史战绩页

位置：[Views/GamesView.xaml.cs](Views/GamesView.xaml.cs)（XAML：[Views/GamesView.xaml](Views/GamesView.xaml)）

8 列布局（`ItemsControl` + `ScrollViewer`，列宽 `2* / 2* / 100 / 120 / 80 / 60 / 60 / 80`）：

`队伍1（玩家名超链接+文明） | 队伍2（同左） | 地图 | 开始时间 | 模式 | 结果 | 分差 | 对战ID超链接`

- 玩家名超链接 → `ProfileUrl`（aoe4world 主页）

- 对战 ID 超链接 → `GameUrl`（aoe4world 对局详情）

- Code-behind 仅 `Hyperlink_RequestNavigate` → 默认浏览器打开

#### OverlayWindow — 覆盖层窗口（最复杂）

位置：[Views/OverlayWindow.xaml.cs](Views/OverlayWindow.xaml.cs)（XAML：[Views/OverlayWindow.xaml](Views/OverlayWindow.xaml)）

**窗口风格**（XAML）：`WindowStyle=None` + `AllowsTransparency=True` + `Background=Transparent` + `Topmost=True` + `ShowInTaskbar=False` + `ResizeMode=NoResize`，默认 700×400。

**布局骨架**（XAML）：

```
Grid
├─ ContentRoot (Margin=6，承载 LayoutTransform 缩放)
│  ├─ MapLabel            顶部地图名（#29e0f8 加粗）
│  └─ [TeamLeftPanel] TeamGapColumn [TeamRightPanel]   三列 StackPanel
├─ LockedBorder    Gold 4px，锁定时可见，IsHitTestVisible=False
├─ UnlockBorder    Red 4px，解锁时可见
└─ ResizeGripControl  右下角缩放手柄（解锁时可见，Cursor=SizeNWSE）
```

**常量**：列宽 `Rating=70 / Winrate=70 / Wins=60 / Losses=70 / CountryFlag=30`；锁定背景 Alpha `77`（≈30%）；Win32 `WM_SYSCOMMAND=0x112`、`SC_SIZE=0xF000`、`WMSZ_BOTTOMRIGHT=8`、`WS_EX_NOACTIVATE=0x08000000`。

**生命周期方法**：

| 方法                               | 说明                                                                                                                                                                                           |
| -------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 构造函数                             | 订阅 `Settings.PropertyChanged`（FontSize/TeamGap 即时生效）；`MapLabel.FontSize = max(10, FontSize-2)`；`TeamGapColumn` clamp 0\~40；从 `OverlayGeometry` 恢复位置尺寸；记录 `_hasSavedGeometry`；挂 `SizeChanged` |
| `OnSourceInitialized`            | 追加 `WS_EX_NOACTIVATE`（不抢焦点，利于游戏前台+热键）→ `SetWindowExTransparent`（初始锁定穿透）→ 30% 黑背景 → 金边框显示、红边框/缩放手柄隐藏                                                                                          |
| `UpdateData(dynamic)`            | 公开入口。Dispatcher 内：更新地图名 → 清空两面板 → 按队号分组（首队左、其余右）→ 逐个 `CreatePlayerRowLeft` / `CreatePlayerRowRightMirrored` → `TryEstablishBaseSize()`                                                       |
| `TryEstablishBaseSize()`         | 首次数据到达时 Measure 内容自然尺寸作为 100% 基准；**无历史几何时窗口贴合内容**；随后 `ApplyScale`                                                                                                                            |
| `ApplyScale()`                   | `ComputeScale(ActualWidth, ActualHeight, baseW, baseH)` → 更新 `ScaleTransform` → 挂到 `ContentRoot.LayoutTransform`                                                                             |
| `OnWindowSizeChanged`            | 已有基准尺寸时重算缩放                                                                                                                                                                                  |
| `ToggleVisibility()`             | `Show()/Hide()` 切换（永不 Close）                                                                                                                                                                 |
| `ToggleLock()`                   | **解锁**：去穿透 + 100% 黑背景 + 红边框 + 显示缩放手柄；**锁定**：反向（穿透 + 30% 背景 + 金边框 + 藏手柄）                                                                                                                      |
| `SaveState()`                    | `OverlayGeometry = [Left, Top, Width, Height]`                                                                                                                                               |
| `ResizeGrip_MouseLeftButtonDown` | `SendMessage(WM_SYSCOMMAND, SC_SIZE+WMSZ_BOTTOMRIGHT)` 走系统原生缩放                                                                                                                               |
| `Settings_PropertyChanged`       | FontSize → 递归刷新两面板所有 TextBlock + MapLabel；TeamGap → 更新中列宽（clamp 0\~40）                                                                                                                       |
| `OnClosed`                       | 解除 Settings 订阅                                                                                                                                                                               |

**玩家行构建**（核心 UI 工厂方法）：

- `CreatePlayerRowLeft(p)`：`文明旗(左，跨2行)` + `姓名徽章(队伍色背景、圆角4、MaxWidth 300)` + 统计行 `[段位徽章 | Rating(蓝#7ab6ff粗体) | 胜率(黄#fffb78) | 胜场W(绿#48bd21) | 负场L(红) | 国旗]`

- `CreatePlayerRowRightMirrored(p)`：完全镜像（国旗在最左、文明旗在最右）

- `FormatWins/FormatLosses`：空串或 `0` 显示为空，否则 `42W` / `38L`

- `CreateCivFlag`：72×36 目标尺寸，`CivIconResolver.Resolve` 查路径，`ApplyImageAspectWidth` 按源图纵横比修正宽度

- `GetTeamNameBrush(team)`：`TeamColors[(team-1) % 5]` 的 RGBA → `SolidColorBrush`，异常回退半透明黑

- `AddTextCell` / `AddCountryFlagCell`：带右分隔线的统计单元格；空内容自动折叠

- `TryLoadImageSource(path)`：`Dictionary` 缓存；**webp → ImageSharp 解码 → PNG 内存流 → BitmapImage**；其他格式直接读文件流；统一 `CacheOption.OnLoad + Freeze()`；失败写 `image_load_error.log`

> 死代码提示：`AddTextCellWithCountry` 已无调用方（见 §12）。

### 5.7 HTML Overlay（html/）

与 WPF Overlay 平行的第二展示通道，可直接用于 OBS 浏览器源。

| 文件                                | 说明                                                                                                                                                         |
| --------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [overlay.html](html/overlay.html) | 入口。结构：`#score > (#team1 表格 + #map + #team2 表格)`；依次加载 `main.css`、`custom.css`、`jquery.min.js`、`main.js`、`custom.js`                                         |
| [main.js](html/main.js)           | 全部交互逻辑（详见下）                                                                                                                                                |
| [main.css](html/main.css)         | 样式：CSS 变量 `--team-gap`、`--overlay-font-size`、列宽变量；`#score` 弹性布局；配色与 WPF 版一致（rating `#7ab6ff`、winrate `#fffb78`、wins `#48bd21`、losses 红、map `#29e0f8`）；背景透明 |
| `custom.css` / `custom.js`        | **空占位文件**，供用户自定义覆盖，升级不被冲掉                                                                                                                                  |

**main.js 关键函数**：

| 函数                                  | 说明                                                                                                        |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `read_number_param(name, fallback)` | 读 URL 查询参数（容错）                                                                                            |
| `apply_params()`                    | `?port=7307&gap=12&font=12` → 端口、CSS 变量 `--team-gap`（≥0）、`--overlay-font-size`（≥8px）                      |
| `connect_to_socket()`               | `$(document).ready` 触发；连 `ws://localhost:{PORT}`；`onmessage → parse_message`                              |
| `reconnect_to_socket()`             | `onclose`/`onerror` 后 **500ms 自动重连**（含 `function_is_running` 防重入）                                         |
| `parse_message(data)`               | `type == "color"` → 更新 `team_colors`（**预留**消息类型，C# 端当前未发送）；`type == "player_data"` → `update_player_data` |
| `update_player_data(data)`          | 首队判定 → 每名玩家生成两行（名字徽章行 + 统计行）+ spacer；第二队**镜像列序**；队徽背景 `rgba_from_team`；文明旗 `../img/flags/{civ}.webp`      |
| `normalize_country_codes(country)`  | 国旗码归一：`uk→gb`、`usa→us`、`eng/sct/wls/nir→gb-xxx（回退 gb）`、`gb-*` 双候选；配合 `onerror` 链式回退                       |
| `rgba_from_team(team)`              | 队伍色数组（JS 端内置 3 组，与 C# `TeamColors` 前 3 组一致）→ `rgba()` 字符串                                                 |

**注意**：HTML 页相对路径引用 `../img/`，因此 `html/` 与 `img/` 必须保持同级目录结构（发布产物已保证）。

### 5.8 静态资源

#### 多语言字典

- [Resources/Strings.zh-CN.xaml](Resources/Strings.zh-CN.xaml) / [Resources/Strings.en-US.xaml](Resources/Strings.en-US.xaml)

- 结构：`ResourceDictionary`，键前缀 `Menu_*`、`Tab_*`、`Settings_*`、`Games_*`

- XAML 端一律 `{DynamicResource Key}` 引用 → 运行时 `MainWindow.SetLanguage` 热切换

- [App.xaml](App.xaml) 默认合并 `Strings.en-US.xaml`（启动时被 `SetLanguage(Settings.Language)` 按需替换）

#### 图片资源（img/）

| 目录                                   | 内容                                                  |
| ------------------------------------ | --------------------------------------------------- |
| `img/flags/`                         | 23 个文明旗 `.webp`（文件名即文明英文名，如 `Abbasid Dynasty.webp`） |
| `img/countries/`                     | 254 面国旗 `.png`（ISO 两位小写码命名，如 `cn.png`、`gb.png`）     |
| `img/build_order/civilization_flag/` | `CivIcon-{文明}AoE4.png` 系列备选图标                       |
| `img/aoe4_sword_shield.ico`          | 应用图标（exe / 托盘 / 窗口共用）                               |

***

## 6. 依赖关系

### 6.1 内部类依赖（箭头 = 使用）

```
App (App.xaml.cs)
└─► MainViewModel
     ├─► SettingsService ──────────► AppSettings (Models)
     ├─► ApiCheckerService ────────► SettingsService
     │        └─► HttpClient / Newtonsoft.Json
     ├─► WebSocketServerService ───► Fleck
     │        └─► Newtonsoft.Json
     ├─► GlobalHotkeyService ×2 ───► Win32 (SetWindowsHookEx 等)
     │        └─► NHotkey.Wpf（注册首选，在 VM 中直接调用）
     ├─► OverlayWindow ────────────► AppSettings（订阅 INPC）
     │        ├─► CivIconResolver
     │        ├─► WindowServices（Win32 穿透）
     │        ├─► OverlayScaleCalculator
     │        ├─► LogPaths
     │        └─► SixLabors.ImageSharp（webp→png）
     └─► GameProcessor (static)
              ├─► MapNameTranslator
              └─► CivNameTranslator

MainWindow ──► MainViewModel (DataContext；SetLanguage 反向调用其刷新)
SettingsView ─► MainViewModel（命令 + 属性绑定）
GamesView ───► MainViewModel（Games 集合绑定）
```

### 6.2 NuGet 依赖（[AoE4OverlayCS.csproj](AoE4OverlayCS.csproj)）

| 包                                | 版本            | 实际使用位置                                                                               | 说明                                      |
| -------------------------------- | ------------- | ------------------------------------------------------------------------------------ | --------------------------------------- |
| `Fleck`                          | 1.2.0         | `WebSocketServerService`                                                             | 本地 WebSocket 服务端                        |
| `Newtonsoft.Json`                | 13.0.4        | `ApiCheckerService` / `GameProcessor` / `SettingsService` / `WebSocketServerService` | JSON 解析与序列化（JObject/JArray Linq 风格）     |
| `NHotkey.Wpf`                    | 4.0.0         | `MainViewModel.UpdateHotkeyRegistration`                                             | 全局热键首选方案                                |
| `Serilog` + `Serilog.Sinks.File` | 4.3.0 / 7.0.0 | **当前代码未使用**                                                                          | 仅在 csproj 引用；实际日志全部为 `File.*` 直写（见 §12） |
| `SixLabors.ImageSharp`           | 3.1.12        | `OverlayWindow.TryLoadImageSource`                                                   | WPF 原生不支持 webp，用于文明旗解码转 PNG             |

框架级：`UseWPF=true` + `UseWindowsForms=true`（后者仅为托盘 `NotifyIcon`）。`Resources/**`、`html/**`、`img/**` 均 `PreserveNewest` 复制到输出目录。

### 6.3 外部 API 依赖（aoe4world.com）

| 端点                                                                    | 用途            | 调用方                |
| --------------------------------------------------------------------- | ------------- | ------------------ |
| `GET https://aoe4world.com/api/v0/players/{profile_id}`               | 玩家直查（纯数字搜索时）  | `FindPlayer`       |
| `GET https://aoe4world.com/api/v0/players/search?query={q}`           | 玩家名模糊搜索（取第一条） | `FindPlayer`       |
| `GET https://aoe4world.com/api/v0/players/{profile_id}/games/last`    | 最近一场对局        | `GetLastGame` / 轮询 |
| `GET https://aoe4world.com/api/v0/players/{profile_id}/games?limit=N` | 最近 N 场历史      | `GetMatchHistory`  |

页面链接（非 API）：玩家主页 `https://aoe4world.com/players/{id}`；对局详情 `https://aoe4world.com/players/{id}/games/{match_id}`。

### 6.4 Win32 API 依赖

| API                                                                                               | 所在类                              | 用途                                               |
| ------------------------------------------------------------------------------------------------- | -------------------------------- | ------------------------------------------------ |
| `GetWindowLong` / `SetWindowLong`                                                                 | `WindowServices`、`OverlayWindow` | `WS_EX_TRANSPARENT` 鼠标穿透、`WS_EX_NOACTIVATE` 不抢焦点 |
| `SendMessage` (WM\_SYSCOMMAND + SC\_SIZE)                                                         | `OverlayWindow`                  | 原生窗口右下角拖拽缩放                                      |
| `SetWindowsHookEx` / `UnhookWindowsHookEx` / `CallNextHookEx` / `GetModuleHandle` / `GetKeyState` | `GlobalHotkeyService`            | 低阶键盘钩子热键兜底                                       |

***

## 7. 核心链路（数据流）

### 7.1 启动流程

```
进程启动
 └─ App() : Mutex 单实例检查 + 注册全局异常处理
 └─ App.OnStartup
     ├─ LogPaths.LogsDirectory        (确保 logs/ 存在)
     ├─ new MainViewModel()           (加载 config.json、创建全部服务、挂事件)
     ├─ MainWindow.SetLanguage(Settings.Language)   (启动即应用持久化语言)
     ├─ new MainWindow + DataContext + Show
     └─ vm.Start()
         ├─ wsServer.Start()          (监听 0.0.0.0:7307)
         ├─ apiChecker.Start()        (Task.Run 轮询循环)
         └─ Dispatcher.Invoke
             ├─ new OverlayWindow(settings)   (恢复几何/锁定穿透)
             ├─ UpdateHotkeyRegistration()    (NHotkey→Hook 兜底)
             └─ 已绑定玩家 → Task.Run(RefreshHistory)

※ 轮询循环首轮 CheckLastGame 必命中(_lastMatchTime=MinValue)
  → OnNewGame 事件 → ProcessGame → WS 广播 + Overlay 更新
  → OpenOverlayOnNewGame=true 时 Overlay 自动显示
```

### 7.2 玩家搜索与绑定

```
SettingsView 搜索框回车 / 搜索按钮
 └─ MainViewModel.SearchPlayer()
     ├─ 空输入 → 橙红提示「请输入用户ID」并返回
     ├─ ApiChecker.FindPlayer(query)
     │    ├─ 纯数字 → GET /players/{query} 直查
     │    └─ 否则/失败 → GET /players/search?query= → players[0]
     ├─ 未命中 → 「ID not found」(红)
     └─ 命中 →
         ├─ AddSearchHistory(query)          (去重头插、上限20)
         ├─ Settings.ProfileId/PlayerName/SteamId = 返回值
         ├─ SettingsService.Save()           (落盘 config.json)
         ├─ UpdateProfileDisplay()           (绿字 "ID Found")
         ├─ RefreshHistory()                 (Games 列表全量替换)
         └─ UpdateOverlayWithLastGame()      (Overlay + WS 立即更新)
```

### 7.3 新对局检测（后台轮询）

```
Loop (每 Interval 秒, Task.Run)
 └─ CheckLastGame → GET /players/{pid}/games/last
      └─ started_at > _lastMatchTime ?
          └─ YES → 更新基线 → OnNewGame(JObject)
               └─ MainViewModel.OnNewGame
                   ├─ GameProcessor.ProcessGame      (标准化+翻译)
                   ├─ wsServer.Send("player_data")   (广播+入历史)
                   ├─ Dispatcher: overlay.UpdateData  (重建面板)
                   │         └─ OpenOverlayOnNewGame → overlay.Show()
                   └─ Task.Run(RefreshHistory)        (战绩刷新)
```

### 7.4 热键注册与兜底

```
MainViewModel.UpdateHotkeyRegistration()   [显隐热键与位置热键各一套]
 ├─ HotkeyManager.Remove("ToggleOverlay") + _globalHotkey.Stop()   (先清理)
 ├─ 解析 "Ctrl+Alt+O" → ModifierKeys + Key
 │    (Left/Right Ctrl/Shift/Alt 归并为修饰符，其余按 Key 枚举)
 ├─ try NHotkey AddOrReplace("ToggleOverlay", key, mods, cb)
 │    ├─ 成功 → hotkey.log 记 "registered"
 │    └─ 异常 → hotkey.log 记 "register-failed"
 │              → _globalHotkey.Configure(hotkey, cb) + Start()
 │                 (WH_KEYBOARD_LL 钩子，按下沿+修饰符精确匹配)
 └─ 同流程处理 "ToggleOverlayPosition" → ChangeOverlayPosition
```

### 7.5 语言切换

```
菜单 Language → MainWindow.SetLanguage("zh-CN", save: true)
 ├─ 线程 Culture 切换
 ├─ 移除全部 Resources/Strings.* 合并字典 → 加入 Strings.zh-CN.xaml
 │    (DynamicResource 使所有 UI 文案即时换语言)
 └─ vm.Settings.Language = "zh-CN"
     ├─ SaveCurrentSettings()
     └─ RefreshLocalizedDataAfterLanguageChange()
          ├─ RefreshHistory()            (地图/文明/模式/结果重译)
          └─ UpdateOverlayWithLastGame() (Overlay 地图/文明名重译)
```

### 7.6 Overlay 锁定 / 解锁 / 缩放

```
解锁 (ToggleLock, 位置热键触发)
 ├─ RemoveWindowExTransparent   (恢复鼠标交互)
 ├─ 背景 → 100% 黑；边框 → 红；显示 ResizeGrip
 └─ 此时可拖动(标题栏区 DragMove)、右下角原生缩放

锁定 (再次触发，反向)
 ├─ SetWindowExTransparent      (点击穿透)
 ├─ 背景 → 30% 黑(Alpha=77)；边框 → 金；隐藏 ResizeGrip
 └─ SaveState 在 Stop() 时统一写回 OverlayGeometry

缩放
 ├─ 首次 UpdateData → TryEstablishBaseSize (Measure 自然尺寸=100%基准)
 │    └─ 无历史几何 → 窗口贴合内容
 └─ 之后每次窗口尺寸变化 → ApplyScale
      → ComputeScale → ScaleTransform → ContentRoot.LayoutTransform
      (等比缩放全部内容，Clamp [0.5, 3.0])
```

***

## 8. WebSocket 协议

**监听地址**：`ws://0.0.0.0:{Settings.WebsocketPort}`（默认 `7307`；端口仅构造时读取）

**消息信封**：

```json
{ "type": "<消息类型>", "data": { ... } }
```

当前 C# 端仅发送 `player_data`；HTML 端额外识别 `color`（预留）。

**`player_data.data`** **完整结构**（`GameProcessor` 输出）：

```json
{
  "map": "干燥阿拉伯",
  "mode": 17,
  "started": "2026-09-05T12:00:00Z",
  "ranked": true,
  "server": "west-europe",
  "match_id": "12345/67890",
  "players": [
    {
      "civ": "English",
      "civ_display": "英格兰",
      "name": "PlayerName",
      "team": 1,
      "country": "us",
      "rating": "1450",
      "rank": "RM#1234",
      "wins": "42",
      "losses": "38",
      "winrate": "52.5%",
      "civ_games": "120",
      "civ_winrate": "55.2%",
      "civ_win_length_median": ""
    }
  ]
}
```

**新客户端接入**：立即补发历史缓存（最多 2 条：最早一条 + 最新一条），无需等待下一轮对局。

**HTML 客户端用法**：直接打开 `html/overlay.html`（可带 `?port=7307&gap=12&font=12`），断线 500ms 自动重连。

***

## 9. 构建与运行

### 9.1 环境要求

- **操作系统**：Windows 10 / 11（WPF 仅限 Windows）

- **SDK**：.NET SDK `10.0.301`（[global.json](global.json) 强制匹配并允许同 Feature Band 前滚；本机缺失可先执行 [dotnet-install.ps1](dotnet-install.ps1)）

### 9.2 命令行构建与运行

```bash
# 还原 + 构建
dotnet build AoE4OverlayCS.csproj

# 开发模式运行
dotnet run --project AoE4OverlayCS.csproj
```

### 9.3 发布（可选）

```bash
dotnet publish AoE4OverlayCS.csproj -c Release -r win-x64 --self-contained false
```

输出：`bin/Release/net10.0-windows/win-x64/publish/AoE4OverlayCS.exe`，双击即可运行。`html/`、`img/`、`Resources/` 已随构建复制到输出目录（`PreserveNewest`）。

### 9.4 首次启动行为

1. 单实例 Mutex 校验，重复启动弹 `App is already running!` 后退出
2. `logs/`、`config/` 目录自动创建
3. 无 `config/config.json` 时全部使用默认值
4. 主窗口显示（默认英文，可在菜单切换中文）
5. 在 Settings 页搜索并绑定玩家后：

   - 历史战绩立即加载

   - 轮询首轮即触发一次 `OnNewGame` → Overlay 自动弹出并广播 WS 数据
6. 关闭主窗口 → 隐藏到托盘继续后台轮询；托盘 Exit / 菜单 Exit 才真正退出

***

## 10. 配置与日志

### 10.1 运行时配置

**位置**：`{exe 目录}/config/config.json`（`SettingsService` 自动创建）

内容与 [AppSettings](#53-models-层appsettings) 字段一一对应。可手工编辑；其中 `WebsocketPort`、`MaxGamesHistory`、`CivStatsColor`、`OpenOverlayOnNewGame`、`TeamColors` 未暴露在 UI，仅能通过该文件修改。

### 10.2 日志

**位置**：`{exe 目录}/logs/`

| 文件                     | 写入方             | 内容                                         |
| ---------------------- | --------------- | ------------------------------------------ |
| `hotkey.log`           | `MainViewModel` | 热键注册成功/失败、NHotkey/Hook 按下记录（追加式，含 ISO 时间戳） |
| `dispatcher_error.log` | `App`           | UI 线程未捕获异常（覆盖式）                            |
| `domain_error.log`     | `App`           | 非 UI 线程未捕获异常（覆盖式）                          |
| `tray_error.log`       | `MainWindow`    | 托盘初始化异常（覆盖式）                               |
| `image_load_error.log` | `OverlayWindow` | 文明图标缺失 / 图片解码失败（追加式）                       |

> 菜单 File → 配置/日志 会打开该目录并选中最新日志。全部日志为裸写文件，未使用已引用的 Serilog。

***

## 11. 扩展与二次开发

### 11.1 推荐阅读顺序

1. [App.xaml.cs](App.xaml.cs) — 启动与生命周期（50 行）
2. [ViewModels/MainViewModel.cs](ViewModels/MainViewModel.cs) — 业务中枢（483 行，理解全部链路）
3. [Services/ApiCheckerService.cs](Services/ApiCheckerService.cs) — 数据源头
4. [Services/GameProcessor.cs](Services/GameProcessor.cs) — 数据变换规则
5. [Views/OverlayWindow.xaml.cs](Views/OverlayWindow.xaml.cs) — 主展示层（644 行，最复杂）

### 11.2 常见扩展点

| 需求                | 修改位置                                                                                                                   |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------- |
| 新增地图翻译            | `MapNameTranslator.ZhCnMapNames` 加条目                                                                                   |
| 新增文明（翻译+图标）       | `CivNameTranslator.ZhCnCivNames` + `CivIconResolver.CivCodeMapping` + 放图到 `img/flags/`                                 |
| 换数据源              | 替换/包装 `ApiCheckerService`（对外仅暴露 `FindPlayer` / `GetLastGame` / `GetMatchHistory` + 两个事件）                               |
| 改 WPF Overlay 样式  | `OverlayWindow` 的 `CreatePlayerRowLeft/RightMirrored`、常量列宽、`GetTeamNameBrush`                                          |
| 改 HTML Overlay 样式 | `html/main.css`（列宽/间距 CSS 变量）或直接写 `custom.css`/`custom.js`（升级安全）                                                       |
| 改 WS 消息结构         | `GameProcessor.ProcessGame` 输出 + `html/main.js` 的 `update_player_data` 同步改                                             |
| 新增 WS 消息类型        | `wsServer.Send("新type", data)` + `main.js parse_message` 加分支                                                           |
| 新增可配置项            | `AppSettings` 加 INPC 属性 + `SettingsView.xaml` 加绑定控件 + `config.json` 自动兼容（缺失字段走默认值）                                     |
| 接 OBS/直播          | 浏览器源指向 `overlay.html?port=7307`，保持 WPF 主程序运行即可                                                                         |
| 补单元测试             | 建议优先覆盖纯逻辑：`GameProcessor`、`MapNameTranslator`、`CivNameTranslator`、`CivIconResolver`、`OverlayScaleCalculator`（均无 UI 依赖） |

### 11.3 本地化扩展

- 界面文案：新增 `Resources/Strings.{culture}.xaml` + `MainWindow.SetLanguage` 中放开文化名白名单（当前硬编码 `zh-CN`/`en-US` 二选一）

- 数据文案：`MapNameTranslator` / `CivNameTranslator` / `MainViewModel.TranslateMode/TranslateResult` 均以 `language == "zh-CN"` 判定，多语言需改为字典 per-language

***

## 12. 已知行为特性与注意事项

阅读或改动代码前建议了解，均为源码核对确认的实际行为：

1. **首轮轮询必触发** **`OnNewGame`**：`_lastMatchTime` 初始为 `DateTime.MinValue`，绑定玩家后第一次 `CheckLastGame` 一定命中 → Overlay 自动显示一次并广播一次数据。这同时是启动自愈机制（断线重连的 HTML 客户端也会收到补发）。
2. **WS 端口不支持热更新**：`WebSocketServerService` 端口在 `MainViewModel` 构造时固定；`SaveSettings()` 的 Stop+Start 不会重建该实例。修改 `config.json` 的端口需重启程序。
3. **轮询间隔支持热更新**：`Loop` 每轮实时读取 `Settings.Interval`，改配置后下一轮延迟即生效。
4. **Serilog 引而未用**：csproj 引用了 `Serilog` + `Serilog.Sinks.File`，但全部代码为 `File.AppendAllText/WriteAllText` 直写。统一日志入口是现成的重构方向。
5. **死配置字段**：`AppSettings.AppWidth/AppHeight` 无任何绑定/读取（主窗口尺寸 XAML 固定 860×640）。
6. **死代码**：`OverlayWindow.AddTextCellWithCountry` 已无调用方；`GameProcessor` 输出的 `civ_win_length_median` 恒为空串。
7. **部分文案未本地化**：搜索状态（`请输入用户ID` / `ID Found` / `ID not found`）、搜索历史删除按钮（`删除`）、热键录制提示（`Press any key...` / `Click to set`）为硬编码字符串。
8. **`GameProcessor`** **会改写输入**：直接向 API 返回的 `JObject` 玩家对象注入 `team` 属性（副作用式编程，复用同一对象时需注意）。
9. **错误处理策略为「静默吞掉」**：API 请求、配置读写、单条历史解析均 catch 后返回 null/跳过，日志较少；排查问题优先看 `logs/` 与网络抓包。
10. **`WS_EX_NOACTIVATE`**：Overlay 永不抢键盘焦点，保证游戏前台时热键与输入不受干扰。
11. **Overlay 只 Hide 不 Close**：显隐切换不销毁窗口；真正关闭仅在 `Stop()`（保存几何 → 写配置）。
12. **TeamGap 双重上限**：UI Slider 0~~80，`OverlayWindow`~~ ~~内再 clamp 0~~40，实际有效上限 40。
13. **HTML 端队伍色与 C# 端独立**：`main.js` 内置 3 组颜色硬编码，若改 `config.json` 的 `TeamColors`，HTML Overlay 不会跟随（WPF 端会）。
14. **`LogMatches`** **当前仅为持久化开关**：菜单可勾选并保存，但没有消费该值的逻辑。
15. **单实例依据**：命名 Mutex `AoE4OverlayCS_Mutex`，未做进程间激活已有窗口。

***

> 本 Wiki 基于源码逐文件核对生成：2026-09-05，对应版本 1.7.6。
> 如代码更新，请同步修订以下章节：§5（类与函数）、§12（已知特性）。

