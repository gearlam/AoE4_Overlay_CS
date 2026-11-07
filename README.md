# AoE Overlay

`AoE Overlay` 是面向《帝国时代 4》和《帝国时代 2：决定版》的桌面 Overlay 工具，使用 `C# + WPF` 开发。程序文件名为 `AoEOverlay.exe`。

它的核心目标是：

- 绑定指定玩家 ID 或玩家名
- 从 `aoe4world.com` 拉取最近比赛与历史战绩
- 在游戏内以 Overlay 形式展示对局信息
- 提供本地 WebSocket 数据通道，驱动 HTML Overlay 或其他外部展示层
- 支持中英文界面切换、地图名称本地化、热键显示/隐藏、拖拽定位和搜索历史记录
- 2.0 新增：热键自动化（按键精灵式循环按键，带操作避让，不干扰游戏操作）
- 2.1.1 新增：无限循环可监控 Shift。只在间隔结束前 1 秒检测，仍按住则等待，松开后立即发送组合键
- 2.2.1 新增：设置页可在 AOE4 / AOE2 之间切换轮询。AOE2 走官方社区接口，对战页列出排位记录，地图名使用游戏官方中文

英文说明请查看：[`README_en.md`](./README_en.md)

## 覆盖层 DLC 文明更新

- 已补齐《北方劫掠者》的 **Vikings（维京人）**、**Scots（苏格兰人）**，AOE4 本地文明旗帜现共 25 个。
- WPF / HTML 覆盖层均支持新旗帜；文明名称随中英文界面切换，旗帜悬停标签也使用当前语言。
- 修正 API 下划线名称的本地图标匹配，英文名称使用标准拼写；历史战绩复用同一翻译表。
- 图标与译名来源见 [`img/flags/SOURCES.md`](./img/flags/SOURCES.md)。

## 3.0.0：游戏聊天即时翻译

从主窗口 **设置 → 游戏聊天翻译设置** 进入：

1. 填写 OpenAI 兼容 API 地址（例如 `https://api.openai.com/v1`，也支持完整 `/chat/completions` 地址）、API Key 和模型名称。
2. 选择网络模式：**Direct（默认直连）/ System（系统代理）/ Custom（自定义代理）**。如果 Cherry Studio 能用而本程序不能用，请先对齐代理设置；Custom 可填写 `http://127.0.0.1:7890` 或 `socks5://127.0.0.1:7890`。点击 **测试模型** 会实际请求模型验证连接、鉴权和文本响应，可能产生费用，不写入历史。连接/TLS 错误会显示网络模式及错误类别；不关闭证书校验。
3. 选择源语言与目标语言，例如 Chinese → English、Spanish → Chinese。
4. 勾选全局快捷键，默认 `F9`；可在快捷键框按键录制。设置窗口打开期间暂停旧快捷键，避免录制时弹窗。点击 **保存并应用** 后关闭设置窗口才启用新快捷键；未保存则恢复原绑定，并检查与 Overlay / 宏的冲突。
5. 游戏中按快捷键打开置顶翻译窗口，输入文本后按 **Enter** 或点击翻译；**Shift+Enter** 换行。点击窗口外或切回游戏时自动隐藏，单纯移开鼠标不会隐藏；再次按快捷键恢复窗口并保留原文、译文。快捷键也可手动隐藏窗口；**Esc** 隐藏并取消当前请求。
6. 翻译成功后 **自动复制译文**，切回游戏即可自行粘贴；隐藏期间仍会完成翻译并自动复制，不主动弹回窗口。失败、取消及“测试模型”不会改写剪贴板。不自动粘贴、不自动发送聊天消息；仍可点击 **复制译文** 或历史旁的复制按钮复用译文。

提示词根据设置页当前的 AOE2 / AOE4 选择使用对应游戏语境，强化文明、兵种、资源、时代、战术与联机聊天表达。无需绑定玩家或等待战绩轮询即可翻译。模型效果和速度取决于供应商及模型；独占全屏可能遮挡窗口，建议无边框或窗口化模式。

### 翻译隐私与本地历史

- 文本会发送到用户配置的第三方供应商；仅请求所需文本，不上传战绩、玩家档案或完整历史。
- 远程接口要求 HTTPS；本机 `localhost` / 回环地址允许 HTTP，可对接无鉴权本地模型。
- 翻译设置保存在 `%LOCALAPPDATA%\AoEOverlay\translation\settings.json`，API Key 使用 Windows DPAPI 当前用户加密，不可直接迁移到其他用户或电脑。
- 历史统一保存到 **`AoEOverlay.exe` 同级的 `translation_history\history.json`**，可通过界面 **打开历史文件夹** 查看。原用户数据目录中的历史会自动迁移，成功写入新文件后才删除旧文件；新目录已有历史时不会覆盖。
- 历史明文保存原文、译文、语言方向、时间与游戏类型，不进入 WebSocket。支持逐条复制、**删除单条** 和需确认的 **清空全部**，重启后保留。程序目录必须允许写入；Program Files 等受保护目录可能导致保存/迁移失败，建议使用可写的便携目录。
- 请求总超时默认 **90 秒**，可设置 **10–300 秒**；连接超时 15 秒。超时与手动取消分别提示；请求期间禁止重复提交，隐藏窗口不取消请求；Esc 或关闭窗口取消本地请求，但供应商仍可能计费。

### 翻译模块验证

```powershell
dotnet run --project tests/TranslationChecks/TranslationChecks.csproj -c Release
dotnet build AoE4OverlayCS.csproj -c Release
dotnet run --project tests/OverlayChecks/OverlayChecks.csproj -c Release
node tests/OverlayChecks/html-checks.cjs
```

自动检查使用模拟 HTTP，不需要真实密钥，不消耗模型额度。

## 项目截图

<img width="843" height="1192" alt="aoe overlary-1" src="https://github.com/user-attachments/assets/a358cd75-07ca-457b-b728-f9c91d70d535" />
<img width="603" height="606" alt="aoe overlary-6" src="https://github.com/user-attachments/assets/d3d058e5-c0fb-4344-8974-8f1ff9a4efd7" />
<img width="570" height="637" alt="aoe overlary-5" src="https://github.com/user-attachments/assets/88f4b385-2d2f-4847-a73f-3d2a618c6fe9" />
<img width="993" height="213" alt="aoe overlary-4" src="https://github.com/user-attachments/assets/90b051a8-da7c-489d-9383-ea5b12ecaefa" />
<img width="844" height="951" alt="aoe overlary-3" src="https://github.com/user-attachments/assets/18eebbf1-52ca-455d-af75-146e3d86c12e" />
<img width="839" height="514" alt="aoe overlary-2" src="https://github.com/user-attachments/assets/f1bff011-5635-4e48-a6b4-b5ecf88d1a9d" />



## 版本信息

- 当前版本：`3.0.0`
- 程序名：`AoE Overlay`（`AoEOverlay.exe`）
- 目标框架：`net10.0-windows`
- 全量地图与文明索引：[`docs/全量图谱索引.md`](./docs/全量图谱索引.md)

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
AoE Overlay (AoEOverlay.exe)
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
│  ├─ Aoe2MatchProcessor
│  ├─ Aoe2Catalog
│  ├─ WebSocketServerService
│  ├─ SettingsService
│  ├─ GlobalHotkeyService
│  ├─ MapNameTranslator
│  ├─ CivNameTranslator
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
- 目标框架：`.NET 10`
- JSON 处理：`Newtonsoft.Json`
- WebSocket 服务：`Fleck`
- 全局热键：`NHotkey.Wpf`
- 键盘兜底方案：`Win32 Low-Level Keyboard Hook`
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
- 锁定后鼠标穿透，金色加粗边框
- 解锁后可拖动和缩放，红色加粗边框
- 锁定状态下背景透明度为 `30%`
- 中英文地图名称即时更新
- 中英文文明名称即时更新
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
- 支持中英文文明名称显示（25个文明）

### Overlay 控制

- 全局热键显示 / 隐藏 Overlay
- 全局热键锁定 / 解锁 Overlay 位置
- 支持自定义位置热键（可录制）
- 系统热键注册失败时自动切换到 Hook 兜底
- 解锁状态支持拖动、缩放和定位
- 锁定状态支持鼠标穿透
- 锁定状态金色加粗边框，解锁状态红色加粗边框

### 热键自动化（2.0 新增）

- 类似按键精灵的循环按键宏：按触发热键开始/停止循环执行按键序列
- 支持组合键（Ctrl+A）与数字键（控制组编队），可录制或手动输入，按键芯片可视化展示；录制只记录键盘，不记录鼠标操作
- 循环与“测试一次”均在每轮发送时屏蔽物理键鼠输入（含鼠标移动/点击/滚轮），至少 500 毫秒，长序列则保护至本轮结束；轮间等待不屏蔽。被屏蔽的输入直接丢弃，不会补执行；输入钩子安装失败时拒绝发送并提示错误
- 停止或退出时，已开始的一轮先执行完并释放模拟按键，再解除屏蔽；收尾期间不允许重新启动或测试，避免按键交叉
- 触发间隔滑条 1~30 秒；也可直接输入更大的秒数（如 100），超过 30 时滑条停在最右端。输入立即生效。重复 1~99 次或无限循环，运行中修改即时生效
- 操作避让：检测到键鼠操作时推迟注入，空闲后才发送，不干扰游戏操作
- Shift 监控（2.1.1）：勾选后，无限循环只在间隔结束前 1 秒检测 Shift；仍按住则等待，松开后立即发送组合键，中间不再空等
- 后台 SendInput 扫描码注入，不切换窗口焦点；触发热键冲突检测
- 实时运行状态显示与“测试”按钮

### 历史战绩

- 显示最近比赛历史
- 显示双方阵容
- 显示当前绑定玩家的比赛结果与分差
- 中文界面下地图显示中文
- 中文界面下模式名本地化（rm 开头翻译为排位赛）
- 中文界面下比赛结果显示中文（win→赢，loss→输）
- 玩家名可点击跳转到 aoe4world.com 个人主页
- 对战 ID 可点击跳转到 aoe4world.com 对战详情页

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
│  ├─ CivNameTranslator.cs
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
- .NET SDK `10.0.301` 或可兼容 `global.json` 的 SDK

### 开发运行

```bash
dotnet build "AoE4OverlayCS.csproj"
dotnet run --project "AoE4OverlayCS.csproj"
```

### 直接运行

编译完成后可直接运行：

```text
AoEOverlay.exe
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
- Overlay 显示/隐藏热键
- Overlay 位置锁定/解锁热键
- Overlay 几何位置
- 字体大小
- 队伍间距
- 文明统计颜色
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
- 文明名称中英双语映射（25个文明）
- 切换语言后即时刷新 Overlay 地图名和文明名
- 切换语言后即时刷新历史对战记录中的地图名和文明名

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
