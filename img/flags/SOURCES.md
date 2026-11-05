# AOE4 DLC 文明旗帜与名称来源

## Raiders of the North / 北方劫掠者

核对日期：2026-10-31。官方公布发布日期为 2026-10-29，新增两种完整文明，不使用 AOE2 图标或现代国家国旗替代。

| API 文明值 | 英文显示 | 简体中文显示 | 本地图标 |
| --- | --- | --- | --- |
| `vikings` | Vikings | 维京人 | `Vikings.png` |
| `scots` | Scots | 苏格兰人 | `Scots.png` |

一手来源：

- 官方英文 DLC 公告，确认新增 Vikings / Scots：<https://www.ageofempires.com/news/pre-order-raiders-of-the-north-for-age-of-empires-iv/>
- 官方 Vikings 文明页：<https://www.ageofempires.com/games/age-of-empires-iv/civilizations/vikings/>
- 官方 Scots 文明页：<https://www.ageofempires.com/games/age-of-empires-iv/civilizations/scots/>
- Xbox 官方简体中文商品页，使用“维京人”“苏格兰人”，发布日期 2026-10-29：<https://www.xbox.com/zh-CN/games/store/age-of-empires-iv-raiders-of-the-north/9n5gz5d6mznk>

图像来源：AoE4World 主页及玩家对局页直接使用的 PNG 资源。原样保存为 PNG（104×56），没有用宣传截图裁切，也没有改用 AOE2 的维京徽章。

- Vikings：<https://static.aoe4world.com/assets/flags/vikings-1b22fd2d6bd467d27d25d3664ea6e4734dc97a444b6920e26d3910427e704f2a.png>
- Scots：<https://static.aoe4world.com/assets/flags/scots-846ab3cc817714bee02975a97b60b4991c3e79ee8d8905f6460dcdcd07d4e025.png>
- 引用页面：<https://aoe4world.com/>、<https://aoe4world.com/players/10367333/games>

游戏图像版权归 Microsoft 及相应权利人所有，遵循其 [Game Content Usage Rules](https://www.xbox.com/en-US/developers/rules)。AoE4World 与本项目均非微软官方产品。

## 覆盖范围与验证

- `img/flags`：25 个 AOE4 正式可玩文明旗帜（23 个已有 + 本次 2 个新增）。代码中遗留的威尼斯、波兰名称条目不代表已发布文明，不据此伪造或增加旗帜。
- `CivNameTranslator`：已知文明输出标准英文名或中文名，兼容 API 下划线和英文直/弯撇号；未知文明保留原值。
- `CivIconResolver`：先按标准英文名定位本地旗帜，再保留旧版图标目录兼容。
- 原生与 HTML 覆盖层均显示本地旗帜；悬停/可访问性标签使用当前语言名称；历史战绩继续复用同一名称翻译。
- AOE2 已有 62 个图标均有目录条目和中英文名，本次未改动未经证实的 API 文明编号。

```powershell
dotnet run --project tests/OverlayChecks/OverlayChecks.csproj -c Release
node tests/OverlayChecks/html-checks.cjs
```
