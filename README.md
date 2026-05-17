# AoE4 Overlay CS

`AoE4 Overlay CS` 是一个面向《Age of Empires IV》的桌面 Overlay 工具，使用 `C# + WPF` 开发。

它的核心目标是：

- 绑定指定玩家 ID 或玩家名
- 从 `aoe4world.com` 拉取最近比赛与历史战绩
- 在游戏内以 Overlay 形式展示对局信息
- 提供本地 WebSocket 数据通道，驱动 HTML Overlay 或其他外部展示层
- 支持中英文界面切换、地图名称本地化、热键显示/隐藏、拖拽定位和搜索历史记录

英文说明请查看：[`README_en.md`](./README_en.md)

## 项目截图

<img width="1149" height="236" alt="overlay-preview-1" src="https://github.com/user-attachments/assets/e24325f7-cda0-432e-bdf5-1dbd8ce83d10" />
<img width="972" height="245" alt="overlay-preview-2" src="https://github.com/user-attachments/assets/9be65e8d-ebb0-4341-8469-6cccc6c20b45" />
<img width="845" height="635" alt="image" src="https://github.com/user-attachments/assets/956ad7ca-1a67-4f9e-b718-6307bdb29c00" />
<img width="842" height="636" alt="image" src="https://github.com/user-attachments/assets/9e09304e-6b8c-47c6-b1e8-fdab989fe7a0" />


## 版本信息

- 当前版本：`1.7.3`
- 目标框架：`net8.0-windows;net10.0-windows`

## 项目定位

这个项目不是游戏 Mod，也不是游戏内注入式插件，而是一个独立运行的 Windows 桌面辅助工具。

它通过公开 API 获取数据，通过透明置顶窗口和本地 HTML/WebSocket 数据输出实现 Overlay 展示。

适用场景：

- 单人查看最近比赛双方信息
- 直播/录屏时叠加展示玩家信息
- 调试和自定义 HTML Overlay 展示效果
- 本地二次开发，复用 WebSocket 数据

## 技术架构

### 整体架构

```text
AoE4OverlayCS (WPF Desktop App)
├─ UI Layer
│  ├─ MainWindow
│  ├─ SettingsView
│  ├─ GamesView
│  └─ OverlayWindow
├─ ViewModel Layer
│  └─ MainViewModel
├─ Service Layer
│  ├─ ApiCheckerService
│  ├─ GameProcessor
│  ├─ WebSocketServerService
│  ├─ SettingsService
│  ├─ GlobalHotkeyService
│  ├─ MapNameTranslator
│  ├─ CivIconResolver
│  ├─ WindowServices
│  └─ LogPaths
├─ Model Layer
│  └─ AppSettings
└─ Static Assets
   ├─ html/
   ├─ img/
   └─ Resources/
```

### 数据流

```text
用户输入玩家名 / ProfileId
  -> MainViewModel.SearchPlayer()
  -> ApiCheckerService 调用 aoe4world API
  -> SettingsService 保存绑定信息
  -> RefreshHistory() 刷新历史战绩
  -> GetLastGame() 获取最近一场比赛
  -> GameProcessor 处理对局数据
  -> WebSocketServerService 广播 player_data
  -> OverlayWindow 立即更新显示
```

### 本地展示链路

```text
aoe4world API
  -> ApiCheckerService
  -> GameProcessor
  -> OverlayWindow (WPF 原生覆盖层)
  -> WebSocketServerService
  -> html/overlay.html + main.js (HTML Overlay)
```

## 技术栈

- 桌面 UI：`WPF`
- 语言：`C#`
- 目标框架：`.NET 8 / .NET 10`
- JSON 处理：`Newtonsoft.Json`
- WebSocket 服务：`Fleck`
- 全局热键：`NHotkey.Wpf`
- 键盘兜底方案：`Win32 Low-Level Keyboard Hook`
- 日志依赖：`Serilog`、`Serilog.Sinks.File`
- 图片处理：`SixLabors.ImageSharp`
- HTML Overlay：`HTML + CSS + JavaScript + jQuery`

## 核心模块说明

### 1. 主窗口与页面层

- `MainWindow.xaml`：程序主窗口，负责菜单、标签页、托盘入口和语言切换
- `SettingsView.xaml`：玩家搜索、搜索历史、热键、字体大小、队伍间距等配置
- `GamesView.xaml`：历史对战记录展示
- `OverlayWindow.xaml`：游戏内覆盖层窗口，支持锁定、解锁、穿透与即时数据刷新

### 2. MainViewModel

`MainViewModel` 是当前项目的核心协调层，负责：

- 搜索玩家
- 保存绑定信息
- 刷新历史记录
- 更新 Overlay
- 启停 WebSocket 服务
- 管理语言切换后的地图名刷新
- 管理搜索历史记录

### 3. API 拉取层

`ApiCheckerService` 负责和 `aoe4world.com` 通信，包括：

- 按玩家名或 ProfileId 搜索用户
- 获取最近一场比赛
- 获取历史比赛列表
- 轮询检查是否出现新对局

### 4. 数据处理层

`GameProcessor` 负责把原始对局 JSON 处理成 Overlay 可直接消费的数据，包括：

- 地图名
- 模式和服务器信息
- 玩家队伍排序
- 排名、分数、胜率、战绩
- 文明信息
- 国家信息

### 5. 地图翻译层

`MapNameTranslator` 负责地图名本地化：

- 中文界面下：地图名自动显示中文
- 英文界面下：保持原始英文地图名
- 同时作用于：
  - Overlay 覆盖层地图名
  - 历史对战记录地图名

### 6. Overlay 层

`OverlayWindow` 具备以下能力：

- 透明置顶显示
- 锁定后鼠标穿透
- 解锁后可拖动和缩放
- 锁定状态下背景透明度为 `30%`
- 中英文地图名称即时更新
- 文明图标、国家图标、本地缓存加载

### 7. WebSocket 输出层

`WebSocketServerService` 会在本地启动 WebSocket 服务，默认端口由配置指定。

作用：

- 向外广播 `player_data`
- 供 `html/overlay.html` 使用
- 供后续自定义前端或直播组件复用

## 功能清单

### 玩家搜索与绑定

- 支持输入玩家名或 ProfileId 搜索
- 搜索按钮触发搜索
- 输入框按回车立即搜索
- 空白输入时给出提示
- 成功搜索后自动刷新最近一场比赛和历史战绩

### 搜索历史

- 每次成功搜索的 ID / 玩家关键字都会记录
- 点击输入框可查看历史记录
- 可直接选择旧记录再次搜索
- 可单独删除某条搜索历史
- 搜索历史持久化保存

### Overlay 显示

- 显示最近一场比赛地图信息
- 显示双方玩家阵容
- 显示文明、国家、分数、段位、胜率、胜负场
- 显示队伍颜色背景
- 支持中英文地图显示

### Overlay 控制

- 全局热键显示 / 隐藏 Overlay
- 系统热键注册失败时自动切换到 Hook 兜底
- 解锁状态支持拖动、缩放和定位
- 锁定状态支持鼠标穿透

### 历史战绩

- 显示最近比赛历史
- 显示双方阵容
- 显示当前绑定玩家的比赛结果与分差
- 中文界面下地图显示中文
- 英文界面下保持英文地图名

### 多语言支持

- 支持 `中文 / English` 界面切换
- 语言设置持久化保存
- 下次启动自动恢复上次语言
- 语言切换后历史记录和 Overlay 地图名即时刷新

### 本地 HTML Overlay

- 提供 `html/overlay.html`
- 通过 WebSocket 实时接收 `player_data`
- 支持后续自定义 HTML / CSS / JS 样式

### 程序行为

- 单实例运行
- 主窗口关闭后缩小到系统托盘
- 托盘菜单支持打开和退出
- 支持查看 HTML 文件目录和日志目录

## 目录结构

```text
AoE4_Overlay_CS/
├─ AoE4OverlayCS.csproj
├─ App.xaml
├─ App.xaml.cs
├─ MainWindow.xaml
├─ MainWindow.xaml.cs
├─ Models/
│  └─ AppSettings.cs
├─ ViewModels/
│  ├─ MainViewModel.cs
│  └─ RelayCommand.cs
├─ Services/
│  ├─ ApiCheckerService.cs
│  ├─ CivIconResolver.cs
│  ├─ GameProcessor.cs
│  ├─ GlobalHotkeyService.cs
│  ├─ LogPaths.cs
│  ├─ MapNameTranslator.cs
│  ├─ SettingsService.cs
│  ├─ WebSocketServerService.cs
│  └─ WindowServices.cs
├─ Views/
│  ├─ GamesView.xaml
│  ├─ OverlayWindow.xaml
│  ├─ OverrideView.xaml
│  └─ SettingsView.xaml
├─ Resources/
│  ├─ Strings.zh-CN.xaml
│  └─ Strings.en-US.xaml
├─ html/
│  ├─ overlay.html
│  ├─ main.js
│  ├─ main.css
│  ├─ custom.js
│  └─ custom.css
└─ img/
   ├─ flags/
   ├─ maps/
   └─ ...
```

## 构建与运行

### 环境要求

- Windows
- .NET SDK `10.0.103` 或可兼容 `global.json` 的 SDK

### 开发运行

```bash
dotnet build "AoE4OverlayCS.csproj"
dotnet run --project "AoE4OverlayCS.csproj"
```

### 直接运行

编译完成后可直接运行：

```text
AoE4OverlayCS.exe
```

### 说明

仓库已包含 `global.json`，用于固定 SDK 版本，保证 `net10.0-windows` 构建兼容性。

## 配置与日志

### 配置文件

当前代码实际将配置保存到程序运行目录：

```text
config/config.json
```

配置内容包括：

- 绑定玩家信息
- 语言设置
- Overlay 热键
- Overlay 几何位置
- 字体大小
- 队伍间距
- 是否自动打开 Overlay
- 搜索历史记录

### 常见日志

日志位于程序日志目录，典型文件包括：

- `hotkey.log`
- `dispatcher_error.log`
- `domain_error.log`
- `tray_error.log`
- `image_load_error.log`

## 本地化说明

当前项目已实现：

- 中英文界面切换
- 地图名称中英双语映射
- 切换语言后即时刷新 Overlay 地图名
- 切换语言后即时刷新历史对战记录中的地图名

## 当前实现特点

### 优点

- 单项目结构简单，便于直接修改
- WPF 原生 Overlay 和 HTML Overlay 双输出
- 搜索、历史、Overlay、热键形成闭环
- 本地化能力已经接入实际数据链路

### 适合继续扩展的方向

- 补充测试项目
- 统一日志入口
- 继续丰富地图翻译与图标资源
- 增加更多比赛字段和 UI 样式配置
- 允许更灵活的 WebSocket 消费端接入

## 开源依赖

- `Fleck`
- `Newtonsoft.Json`
- `NHotkey.Wpf`
- `Serilog`
- `Serilog.Sinks.File`
- `SixLabors.ImageSharp`

## 项目链接

- GitHub：`https://github.com/gearlam/AoE4_Overlay_CS`

## 许可证与说明

如果你准备基于这个项目继续扩展，建议优先从以下入口阅读代码：

1. `MainWindow.xaml.cs`
2. `ViewModels/MainViewModel.cs`
3. `Services/ApiCheckerService.cs`
4. `Services/GameProcessor.cs`
5. `Views/OverlayWindow.xaml.cs`

这样可以最快理解整个项目的主链路。
