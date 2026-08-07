# Overlay 内容随窗口等比缩放 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 OverlayWindow 在用户拖动缩放窗口大小时，内部内容（文字、图标、国旗、间距）随之等比放大或缩小（限制 0.5x~3x），以首次加载对局数据时内容的自适应尺寸为 100% 基准。

**Architecture:** 不改动任何内部元素构建代码，把内容 Grid 改为居中容器并通过 `LayoutTransform`（ScaleTransform）整体缩放。首次 `UpdateData` 后用 `Measure(Infinity)` 测量内容自然尺寸（DesiredSize）作为基准；无历史几何时自动把窗口贴合到基准尺寸；之后订阅 `SizeChanged` 按 `min(宽比, 高比)` 计算并 clamp 到 [0.5, 3.0]。缩放计算提取为纯函数 `OverlayScaleCalculator.ComputeScale`，与 UI 分离。

**Tech Stack:** C# / WPF / .NET 10（net10.0-windows），仅 WPF 内置布局与变换 API，无新依赖。

## Global Constraints

- 目标框架 `net10.0-windows`，`UseWPF=true`（见 `AoE4OverlayCS.csproj`），构建命令在仓库根目录执行：`dotnet build "AoE4OverlayCS.csproj"`。
- 只允许修改：`Views/OverlayWindow.xaml`、`Views/OverlayWindow.xaml.cs`；新增：`Services/OverlayScaleCalculator.cs`。**禁止**改动 `GameProcessor` / `WebSocketServerService` / `MainViewModel` 等数据链路（`MainViewModel` 只负责调用 `UpdateData`，接口不变）。
- 缩放为**等比**（不变形，`min` 比例），范围 clamp 到 `[0.5, 3.0]`。
- 基准尺寸 = 首次 `UpdateData()` 后 `ContentRoot.DesiredSize`（含 Margin=6）。
- 有历史几何（`OverlayGeometry` 存在且长度为 4）时保留用户上次窗口尺寸，只建立基准不贴合；无历史几何时自动贴合窗口到基准尺寸。
- 基准只在首次 `UpdateData` 建立一次；后续不同人数对局数据在相同基准下缩放显示（内容居中，超出部分裁剪），此为预期行为。
- 项目当前无测试项目，验证方式为「构建通过 + 手动验证清单」（Task 4）。注释使用中文。

---

### Task 1: 提取缩放计算纯函数 OverlayScaleCalculator

**Files:**
- Create: `Services/OverlayScaleCalculator.cs`

**Interfaces:**
- Consumes: 无（纯静态工具类）
- Produces: `AoE4OverlayCS.Services.OverlayScaleCalculator.ComputeScale(double clientWidth, double clientHeight, double baseWidth, double baseHeight) : double`；常量 `MinScale = 0.5`、`MaxScale = 3.0`。Task 3 依赖此签名。

- [ ] **Step 1: 创建文件**

```csharp
using System;

namespace AoE4OverlayCS.Services
{
    /// <summary>
    /// 计算 Overlay 内容随窗口尺寸变化的等比缩放比例。
    /// </summary>
    public static class OverlayScaleCalculator
    {
        public const double MinScale = 0.5;
        public const double MaxScale = 3.0;

        /// <summary>
        /// 以 min(宽比, 高比) 计算等比缩放比例，并 clamp 到 [MinScale, MaxScale]。
        /// 任一尺寸无效（<=0）时返回 1.0，避免除零与异常布局。
        /// </summary>
        public static double ComputeScale(double clientWidth, double clientHeight, double baseWidth, double baseHeight)
        {
            if (clientWidth <= 0 || clientHeight <= 0 || baseWidth <= 0 || baseHeight <= 0)
                return 1.0;

            double scaleX = clientWidth / baseWidth;
            double scaleY = clientHeight / baseHeight;
            double scale = Math.Min(scaleX, scaleY);
            return Math.Clamp(scale, MinScale, MaxScale);
        }
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build "AoE4OverlayCS.csproj"`
Expected: 构建成功（0 error）。此文件暂无调用方，编译通过即可。

- [ ] **Step 3: 提交**

```bash
git add Services/OverlayScaleCalculator.cs
git commit -m "feat: 新增 Overlay 等比缩放比例计算纯函数"
```

---

### Task 2: XAML 调整——内容 Grid 改为居中缩放容器

**Files:**
- Modify: `Views/OverlayWindow.xaml:18`

**Interfaces:**
- Consumes: 无
- Produces: 具名元素 `ContentRoot`（Grid，居中，内部布局结构完全不变）。Task 3 的代码通过 `ContentRoot` 引用此元素，`LayoutTransform` 在此元素上设置。

- [ ] **Step 1: 修改内容 Grid**

把 `Views/OverlayWindow.xaml` 第 18 行的：

```xml
<Grid Margin="6">
```

改为：

```xml
<Grid x:Name="ContentRoot" Margin="6" HorizontalAlignment="Center" VerticalAlignment="Center">
```

说明：`HorizontalAlignment/VerticalAlignment = Center` 使内容 Grid 尺寸等于其子元素自然尺寸（不再被窗口拉伸填满），这是 LayoutTransform 等比缩放生效的前提；居中保证缩放后内容始终位于窗口中央，不会因窗口宽高比变化而偏移。

- [ ] **Step 2: 构建验证**

Run: `dotnet build "AoE4OverlayCS.csproj"`
Expected: 构建成功（0 error）。

- [ ] **Step 3: 提交**

```bash
git add Views/OverlayWindow.xaml
git commit -m "feat: Overlay 内容 Grid 改为居中缩放容器"
```

---

### Task 3: OverlayWindow 缩放逻辑（测量基准、自动贴合、SizeChanged 驱动）

**Files:**
- Modify: `Views/OverlayWindow.xaml.cs:54-70`（构造函数）、`Views/OverlayWindow.xaml.cs:138-179`（UpdateData）、`Views/OverlayWindow.xaml.cs:639-680` 之后新增方法（或紧跟 UpdateData 之后）

**Interfaces:**
- Consumes: Task 1 的 `OverlayScaleCalculator.ComputeScale(double, double, double, double) : double`；Task 2 的 `ContentRoot`（Grid）
- Produces: 窗口行为——首次数据后自动贴合、拖动缩放时内容等比缩放、有历史几何时保持用户尺寸。对外接口（`UpdateData(dynamic)`、`ToggleVisibility()`、`SaveState()`、`ToggleLock()`）签名不变，`MainViewModel` 无需任何改动。

- [ ] **Step 1: 新增私有字段**

在 `Views/OverlayWindow.xaml.cs` 的 `_isLocked` 字段（第 28 行）之后添加：

```csharp
private readonly ScaleTransform _contentScale = new ScaleTransform(1, 1);
private double _baseContentWidth;
private double _baseContentHeight;
private bool _hasBaseSize;
private bool _hasSavedGeometry;
```

`ScaleTransform` 位于 `System.Windows.Media` 命名空间，文件顶部第 10 行 `using System.Windows.Media;` 已存在，无需新增 using。

- [ ] **Step 2: 构造函数记录历史几何并订阅 SizeChanged**

把构造函数（第 54-70 行）末尾，在恢复几何的 `if` 块之后追加：

```csharp
_hasSavedGeometry = _settings.OverlayGeometry != null && _settings.OverlayGeometry.Length == 4;
SizeChanged += OnWindowSizeChanged;
```

- [ ] **Step 3: UpdateData 末尾建立基准**

在 `UpdateData` 的 `Dispatcher.Invoke` 块末尾（右队 `foreach` 循环之后、`});` 之前）追加调用：

```csharp
TryEstablishBaseSize();
```

注意 `UpdateData` 中 `players == null` / `playerList.Count == 0` 的提前 `return` 不变，此时不建立基准；数据有效时才触发。

- [ ] **Step 4: 新增三个私有方法**

在 `UpdateData` 方法之后（`CreatePlayerRowLeft` 之前）插入：

```csharp
private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
{
    if (!_hasBaseSize) return;
    ApplyScale();
}

private void TryEstablishBaseSize()
{
    if (_hasBaseSize) return;

    // 手动测量内容自然尺寸（未显示时也可测量；此时 LayoutTransform 尚未设置，不受缩放影响）
    ContentRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    if (ContentRoot.DesiredSize.Width <= 0 || ContentRoot.DesiredSize.Height <= 0) return;

    _baseContentWidth = ContentRoot.DesiredSize.Width;
    _baseContentHeight = ContentRoot.DesiredSize.Height;
    _hasBaseSize = true;

    // 无历史几何时把窗口贴合到内容自然尺寸，作为 100% 基准状态
    if (!_hasSavedGeometry)
    {
        Width = _baseContentWidth;
        Height = _baseContentHeight;
    }

    ApplyScale();
}

private void ApplyScale()
{
    double scale = OverlayScaleCalculator.ComputeScale(
        ActualWidth, ActualHeight, _baseContentWidth, _baseContentHeight);

    _contentScale.ScaleX = scale;
    _contentScale.ScaleY = scale;
    ContentRoot.LayoutTransform = _contentScale;
}
```

说明（行为契约）：
- `ActualWidth/ActualHeight` 为 0（窗口未显示）时 `ComputeScale` 返回 1.0，随后窗口首次显示触发的 `SizeChanged` 会重新计算一次，保证最终缩放正确。
- 贴合窗口会触发 `SizeChanged`，此时 `_hasBaseSize` 已为 true，正常走 `ApplyScale`（scale≈1）。
- 基准只建一次：后续新对局 `UpdateData` 重建子元素时 `TryEstablishBaseSize` 直接返回，内容按固定基准缩放、居中显示。

- [ ] **Step 5: 构建验证**

Run: `dotnet build "AoE4OverlayCS.csproj"`
Expected: 构建成功（0 error）。如有编译错误，检查 `ScaleTransform`/`SizeChangedEventArgs` 的 using 是否齐全（均已在 `System.Windows.Media`/`System.Windows`）。

- [ ] **Step 6: 提交**

```bash
git add Views/OverlayWindow.xaml.cs
git commit -m "feat: Overlay 内容随窗口等比缩放（0.5x-3x，首帧自适应基准）"
```

---

### Task 4: 手动功能验证

**Files:**
- 无代码改动，仅运行验证

**Interfaces:**
- Consumes: Task 1-3 的全部交付物

- [ ] **Step 1: 构建并启动**

Run: `dotnet build "AoE4OverlayCS.csproj"` 后 `dotnet run --project "AoE4OverlayCS.csproj"`
Expected: 程序启动无异常，`logs/` 目录无新增报错日志。

- [ ] **Step 2: 验证首次自动贴合**

- 备份或删除 `config/config.json`（清除历史几何），启动程序
- 搜索并绑定一个玩家
- Expected: Overlay 自动出现，窗口尺寸贴合对局内容（上下左右无大面积留白、无裁剪）

- [ ] **Step 3: 验证放大**

- 解锁 Overlay（位置热键或对应按钮），拖动右下角 ResizeGrip 放大窗口
- Expected: 地图名、玩家名、文明图标、国旗、统计数字、队伍间距全部同步等比放大，无变形、无错位

- [ ] **Step 4: 验证缩小**

- 继续缩小窗口
- Expected: 内容等比缩小；拖到极小后内容停在 0.5x 不再缩小（下限生效）

- [ ] **Step 5: 验证上限与居中**

- 把窗口拖到很大
- Expected: 内容停在 3x 不再放大（上限生效）；任意窗口宽高比下内容始终居中

- [ ] **Step 6: 验证锁定与穿透**

- 锁定 Overlay
- Expected: 锁定后内容保持缩放后尺寸，鼠标穿透正常，金色边框完整

- [ ] **Step 7: 验证持久化**

- 退出程序（托盘 Exit）再重新启动
- Expected: Overlay 窗口恢复上次尺寸，内容缩放比例一致；且**不再自动贴合**（保留用户调整的尺寸）

- [ ] **Step 8: 验证不同人数对局**

- 等一场新对局触发 `OnNewGame`（或重新搜索）
- Expected: 数据正常刷新，内容按既有基准缩放显示、居中，无布局错乱

- [ ] **Step 9: 已知预期现象确认**

- 放大到 2x 以上时位图图标（文明旗帜/国旗）出现插值模糊，属位图缩放正常现象，非缺陷
- 若功能不符合预期，回到 Task 3 检查 `_hasSavedGeometry` 判定与 `ApplyScale` 的触发时机

- [ ] **Step 10: 提交（如有遗留修改）**

```bash
git status
git add -A
git commit -m "chore: overlay 缩放功能手动验证通过"
```

---

## 自检记录

- **Spec 覆盖**：用户三项决策（内容自适应基准 / 等比缩放 / 0.5x~3x 限制）→ Task 2+3；自动贴合 → Task 3 Step 4；持久化兼容 → Task 3 Step 2 + Task 4 Step 7；不破坏数据链路 → Global Constraints 明确禁改文件清单。
- **占位符扫描**：无 TBD/TODO；每步均含完整代码或精确运行命令。
- **类型一致性**：`ComputeScale(double,double,double,double):double` 在 Task 1 定义、Task 3 Step 4 按此签名调用；`ContentRoot` 在 Task 2 命名、Task 3 引用；`_hasSavedGeometry`/`_hasBaseSize`/`_baseContentWidth`/`_baseContentHeight`/`_contentScale` 命名在 Task 3 各步骤间一致。
