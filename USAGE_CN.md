# AoE4 Overlay（C# 版）使用说明

本说明面向 `AoE4_Overlay_CS` 目录下的 C# / WPF 重构版本。

## 1. 启动与退出

- 启动（开发模式）
  - 进入仓库根目录后运行：
    - `dotnet run --project AoE4_Overlay_CS/AoE4OverlayCS.csproj`
- 启动（直接运行可执行文件,已经编译好的自己下载1.5.0版本）
  - `AoE4OverlayCS.exe`
- 单实例
  - 程序同时只能运行一个实例；重复启动会弹出提示并自动退出。
- 退出
  - 右上角关闭按钮：缩到系统托盘（不退出）。
  - 托盘右键菜单 `Exit`：彻底退出，并关闭 Overlay 覆盖层与相关服务。
  - 菜单 `File -> Exit`：彻底退出，并关闭 Overlay 覆盖层与相关服务。

## 2. 系统托盘（Tray）

- 托盘图标：双击可显示主窗口。
- 托盘菜单：
  - `Open`：显示主窗口
  - `Exit`：退出程序（会同时关闭 Overlay）

## 3. 菜单功能

- `File -> Html files`
  - 打开运行目录下的 `html` 文件夹（用于查看/编辑 HTML 资源）。
- `File -> Config/logs`
  - 打开配置文件所在目录并尝试选中最近的日志文件（如果存在）。
- `Links -> App on Github`
  - 打开项目地址：`https://github.com/gearlam/AoE4_Overlay_CS`

## 4. Settings 页：绑定玩家与热键

- 玩家搜索/绑定
  - 在 Settings 页输入玩家名或 ProfileId 搜索，成功后会写入配置并用于：
    - Overlay 展示（对局中玩家信息）
    - Games 页历史记录加载
- 热键（全局）
  - 在 Settings 页录制/设置热键（推荐 `F12` 或其他未被占用的按键）。
  - 热键作用：显示/隐藏 Overlay 覆盖层。
  - 若系统热键注册被占用，程序会自动启用键盘 Hook 方案作为兜底（不会弹出额外提示）。

## 5. Overlay 覆盖层操作（显示内容与锁定）

- 显示/隐藏
  - 通过全局热键切换显示/隐藏。
- 锁定/解锁（用于调整位置与大小）
  - 解锁：可拖动/调整大小；背景为 100% 黑色（便于对齐）。
  - 锁定：鼠标穿透（不挡操作）；背景为 50% 黑色。
- 用户名底色（按队伍区分）
  - Overlay 中每个玩家名字都有底色，不同队伍使用不同底色，便于区分队伍。
  - 颜色来自配置中的 `TeamColors`。

## 6. Games 页：历史记录

- Games 页会显示最近的对战历史（默认最多 100 条，可在配置里调整）。
- Team1/Team2 显示格式：
  - `名字 [profile_id] (civilization)`

## 7. 配置与日志位置

### 配置文件

- `%LOCALAPPDATA%\\AoE4_Overlay_CS\\config.json`

### 常见日志文件（运行目录）

以下文件会出现在程序运行目录（或 exe 所在目录）：

- `hotkey.log`：热键注册/触发的调试日志
- `dispatcher_error.log`：WPF Dispatcher 未处理异常
- `domain_error.log`：AppDomain 未处理异常
- `tray_error.log`：托盘初始化相关错误

## 8. 常见问题排查

- 热键按了没反应
  - 确认 Settings 页已设置热键并保存；
  - 查看运行目录下的 `hotkey.log` 是否有记录（用于判断是否触发）。
- Games 页无数据
  - 确认 Settings 页已成功绑定 ProfileId；
  - 确认网络可访问 `aoe4world.com` API。

