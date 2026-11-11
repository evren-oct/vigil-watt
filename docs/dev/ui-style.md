# 界面视觉规则

> 改界面、加颜色、加图标之前先读本文。色值的唯一来源是 `src/VigilWatt.App/Services/UiPalette.cs`，
> 字号与卡片样式在 `App.xaml` 的资源字典里；本文只写**规则和为什么**，值变了以代码为准。

## 1. 总原则：颜色只表达电量状态

四档状态色（绿 / 琥珀 / 红 / 蓝）是**全局同一把尺子**，各界面共用同一组色值，不做按设备、按品牌、按用户偏好的自定义颜色。

原因：低电量气泡只在跨阈值时弹一次，**红色是之后唯一的持续提示**。允许改色等于允许静默关掉告警。

由此派生出两条推论：

- **设备身份不靠颜色区分**，靠图标形状（见 §5）。
- **来源（蓝牙 / 2.4G / 有线 / 本机）用独立色相**，且刻意避开状态色的红绿琥珀，免得"这是来源还是电量"分不清（见 §3）。

## 2. 电量状态色

| 语义 | 色值 | 取值条件 |
|---|---|---|
| 充电中 | `#0A84FF` | `charging == true`，优先于电量档位 |
| 低电量 | `#E2445C` | `percent <= 20` |
| 中电量 | `#FAAA17` | `percent <= 50` |
| 良好 | `#34C759` | `percent > 50` |
| 离线 / 无读数 | `#8E8E93` | `percent == null`，或 `IsOnline == false` |

- 阈值的单一来源是 `UiPalette.BatteryLevel()`；不要在页面里另写一份 `if (p < 20)`。
- 多系列图表循环取色用 `UiPalette.SeriesColors`（首位是充电蓝，作为主系列色）。

## 3. 来源徽标色

| 来源 | 色值 | 说明 |
|---|---|---|
| 蓝牙 | `#0A84FF` | 蓝牙的通用色 |
| 2.4G | `#AF52DE` | 紫，与蓝牙蓝拉开 |
| 有线 | `#5E7C99` | 钢蓝灰，比蓝牙更暗更灰，不抢色相 |
| 本机 | `#30B0C7` | 青 |
| 未知 | `#8E8E93` | **与离线共用同一个灰**，表示"没有信息"而不是"某种链路" |

徽标是**胶囊底 + 同色文字**：底色取同色 16% 不透明度（`UiPalette.Tint()` 默认 `alpha = 0x28`），明暗主题下都够淡，不抢电量条。

徽标本身写着「有线」「2.4G」等文字，**颜色只做辅助**，不是唯一信息载体。

## 4. 指标标签色（DPI / 档位 / 回报率）

| 指标 | 色值 |
|---|---|
| DPI | `#1D9E75` |
| 档位 | `#BA7517` |
| 回报率 | `#534AB7` |

- **只给标签上色，数值保持主题前景色**。颜色当路标用，不让彩色数字跟右侧大号百分比抢注意力。
- 三个色相刻意避开来源徽标（蓝/紫/青）与状态色（红/琥珀/绿/蓝/灰），取更深的中间调。
- 未获取的项用灰（`Offline`），不上色。

## 5. 图标：形状承载身份，颜色承载电量

- **设备类型图标**按 `DeviceKind` 现画，名义框统一 `14×13` 再整体缩放到 22px，各类型视觉大小才一致（`DeviceIconFactory.Scaled`）。
- **图标颜色跟随电量状态色**（`IconColor = PrimaryColor`），不是中性色——所以"形状 = 这台是什么，颜色 = 它还有多少电"。
- **接收器体系标签**（Bolt / Unifying 等）用 `14px` 小尺寸，跟在链路徽标之后；目录没登记就整块不占位。
- `bolt` / `unifying` 直接复用 **OpenLogi** 的官方 path 原文（MIT, Copyright (c) 2026 AprilNEA），不是手绘近似；其余体系（Nano / LIGHTSPEED / 27MHz）为自绘几何。**范围仅限设备列表的小尺寸体系标签**，不用于应用图标、安装包或宣传物料。
- 画好的图标元素**不能存进行模型**：行复用会把同一个元素交给第二个容器（`runtime-pitfalls.md` §22）。每个行容器各持一个 presenter 实例。

## 6. 字体与字号

字体**钉死 `Segoe UI`**（`ContentControlThemeFontFamily` / `TextControlThemeFontFamily`）。Win10 没有 `Segoe UI Variable`，用系统默认字体在 Win10 上会宽 5~8%，排版走样（`runtime-pitfalls.md` §13）。

| 用途 | 样式 | 字号 / 字重 |
|---|---|---|
| 页面标题 | `AppPageTitleStyle` | 18 / SemiBold |
| 页面副标题 | `AppPageSubtitleStyle` | 12 / 次要前景色，超出省略 |
| 卡片标签 | `AppCardLabelStyle` | 12 / 次要前景色 |
| 卡片脚注 | `AppCardFootnoteStyle` | 11 / 三级前景色，超出省略 |
| 设备名 | 页面内联 | 15 / SemiBold |
| 来源徽标 / 体系标签 | 页面内联 | 10 / SemiBold |

新增字号前先问：现有的六档够不够？多一档字号就多一套对齐要维护。

## 7. 卡片与间距

- 通用卡片 `AppCardStyle`：圆角 8、`CardBackgroundFillColorDefaultBrush` 底、`CardStrokeColorDefaultBrush` 1px 描边。**各页共用，不要各自重复写这四条属性**。
- 指标卡 `AppKpiCardStyle`：继承通用卡片，`Padding = 14,10`。四张并排时留白要收着。
- 设备行内：图标与内容列间距 14，内容列内元素间距 6，同行徽标间距 8。

## 8. 中性色一律走 ThemeResource

文字、卡片底、分隔线、描边**不用硬编码色值**，一律 `ThemeResource`（`TextFillColorPrimaryBrush` / `TextFillColorSecondaryBrush` / `TextFillColorTertiaryBrush` / `CardBackgroundFillColorDefaultBrush` / `CardStrokeColorDefaultBrush`）。

只有**状态色、来源色、指标色**是固定值，因为它们在明暗主题下要表达同一个语义。

## 9. 系统版本差异

Win10 与 Win11 的视觉能力不同，降级规则记在 `runtime-pitfalls.md`：Mica 不支持会出双标题栏（§13）、小组件在 Win10 上圆角与投影表现不同（§14）、任务栏贴片只能靠 `SetWindowPos(HWND_BOTTOM)` 钉住（§15）。

**改视觉前先在两套系统上各看一眼**，本机只有一套系统时，把未验证的那一半写进 BACKLOG，不要当成已完成。

## 10. 不要做的

- 不加按设备 / 按品牌的自定义颜色（§1）。
- 不给电量状态加第五档，或改阈值（阈值只有 `UiPalette.BatteryLevel` 一处）。
- 不用颜色作为唯一的信息载体：徽标要带文字，图表要有数值。
- 不为了"好看"改状态色值——那是告警语义，不是装饰。
- 不新增字号档位或卡片样式，除非现有六档 + 两种卡片确实表达不了。
