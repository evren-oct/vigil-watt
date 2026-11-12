# 设备产品图接入

> 这是产品图的**单一接入手册**。新品牌/新型号按本文走，不要重新设计一套图片加载逻辑。
> 当前内置资产：迈从键盘 31 款 / 170 个配色 / 201 个文件 / 压缩后约 4.5 MB。
> 快速校验：`python scripts/check-device-images.py`

## 0. 一句话流程

**官方源 → 解包/抓取 → 按型号与 PID 分组 → 只下载缩略图 → 生成 manifest → 压缩图片 →
校验 → 构建 → 更新文档。**

产品图只服务详情页 Hero；列表、托盘、任务栏贴片、桌面小组件继续用几何图标。

## 1. 资产位置与读取顺序

### 1.1 项目内置资产

```
src/VigilWatt.App/Assets/device-images/<品牌>/<型号>/
    manifest.json
    <配色>_thumb.png
```

`VigilWatt.App.csproj` 用下面的规则把它复制到输出目录：

```xml
<Content Include="Assets\device-images\**\*" CopyToOutputDirectory="PreserveNewest" />
```

构建后运行时路径是 exe 旁的 `Assets\device-images\`；安装包和便携包会一并带走。

**内置资产只放缩略图。** 详情页 Hero 只显示缩略图；整机大图体积大，不作为默认项目资产。

### 1.2 用户覆盖资产

用户仍可把自己的图片包放到数据根：

```
<数据根>\device-images\<品牌>\<型号>\
```

数据根由 `AppPaths.RootDir` 决定：Debug 是 exe 旁的 `data\`，Release 安装版是
`%LOCALAPPDATA%\VigilWatt`，便携版是解压根目录的 `data\`；`VIGILWATT_DATA_DIR` 可覆盖。

`DeviceImageService` 的读取顺序是：

1. 用户数据目录（可放整机图或用户自己的修正）；
2. 项目内置 `Assets/device-images`。

用户目录命中就优先用；没有才回退内置缩略图。两者都没有才回退 `DeviceIconFactory`。

## 2. 清单格式

```jsonc
{
  "schemaVersion": 1,
  "brand": "MCHOSE",
  "model": "Ace 68 Air II",
  "match": {
    "vendorId": "41E4",
    "productIds": ["2120", "2121"],
    "displayNames": ["迈从 Ace 68 Air II", "Ace 68 Air II"]
  },
  "defaultColor": "color-white",
  "colors": [
    {
      "key": "color-white",
      "label": "white",
      "thumbnail": "color-white_thumb.png",
      "image": "color-white_thumb.png",
      "source": "https://cdn.mchose.com.cn/..."
    }
  ],
  "source": "MCHOSE HUB cardList.json"
}
```

- `thumbnail`：详情页 Hero 用，必须存在。
- `image`：大图预览预留；内置资产可让它指向同一张缩略图，`DeviceImageCatalog` 也会在
  `image` 缺失时回退到 `thumbnail`。
- `source`：原始官方 URL，排错和重新抓取时用。
- 相对路径必须落在图片包根目录内；`..\` 越界会被忽略。

## 3. 匹配规则

1. **显示名精确命中**优先：清单 `match.displayNames` 与设备当前 `Name` 都去掉空格、连字符、
   下划线并转小写后比较。
2. 显示名被用户改过时，只有 **协议自报型号 + VID/PID 同时命中**才认。
3. **只有 VID/PID 不足以选图**：同一颗 dongle 常被多款型号共用，按 PID 猜会把别的型号图挂上来。
4. **不按设备 ID 匹配**：ID 是接口路径哈希，插线/换口会变；历史记录也不能作为图片身份。
5. 多个清单命中时取分数最高的一条：显示名 + VID/PID > 显示名 > 自报型号 + VID/PID。

### 3.1 共用 dongle / 多 VID 条目

一个官方条目可能同时列出 `0x41E4` 和 `0x3837` 两组 VID/PID。manifest 只有一个
`match.vendorId` 字段，所以：

- **单 VID**：写 `vendorId` + 全部 `productIds`，改名后仍能靠「自报型号 + VID/PID」兜底。
- **多 VID**：省略 `vendorId` / `productIds`，只靠 `displayNames` 匹配。不要为了凑字段
  随便选一个 VID，否则会误匹配。

## 4. 配色与用户覆盖

- **官方 HUB 不读取设备配色**：MCHOSE 本机状态里是 `checkIndex: 0` / `lockIndex: null`，
  `checkIndex` 只是官方 UI 记录的用户选择索引，不是硬件回读。不能从协议推断颜色。
- `defaultColor` 命中就用它；缺失或写错时回退 `colors[0]`。
- 详情页 Hero 悬停时右下角出现铅笔按钮，点开可选任一配色；选择按设备 ID 存进
  `settings.json` 的 `DeviceCustomizations[id].ImageKey`，详情页优先用它。
- 用户选错随时改；设备改名后如果无法匹配型号，会回退几何图标，不会硬套别的图。

## 5. 接入新品牌/新设备的完整流程

### Step 1：找官方源

优先级：**官方配套应用/官方 CDN > 官方文档 > 公开逆向资料 > 现场猜测**。

先确认图片是「官方设备图」还是「系统/驱动通用图标」。Windows INF 里的图标通常只是
设备类别图标，不是产品照片；真正可用的产品图一般在厂商配套应用或它的 CDN。

### Step 2：解包/抓取

- 本地已安装配套应用：先查它的 `resources`、`app.asar`、缓存目录和用户配置。
- 只有网页驱动：从官方主包或配置 JSON 抓取，不要用电商图/第三方图库替代。
- 记录**官方源 URL、版本、抓取日期**，写进 manifest 的 `source` 和本文的变更记录。

### Step 3：把官方条目映射到型号与 PID

至少拿到三样东西：

1. 官方型号名（用于 `model` / `displayNames`）；
2. 无线与有线 PID（用于 `match.productIds`）；
3. 配色列表与每张图的 URL（用于 `colors[]`）。

型号映射优先查项目已有目录（如 `mchose-catalog.json`），不要用显示名猜 PID。

### Step 4：按型号分组，处理别名

- 同一型号的多条配置合并成一个 manifest，配色取并集。
- `displayNames` 至少包含 `品牌 + 型号` 和裸型号两种写法。
- 如果某个别名被多个型号共用（例如 `Ace 68 Air` 同时是 II/III 的营销名），
  **不要把这个共用别名写进任何一个 manifest**，否则会随机命中错误型号。
- 同一 `className` 在同一型号里只保留一次；不同 URL 的同名配色要人工判断，不要静默覆盖。

### Step 5：下载图片

- 详情页内置只下载缩略图；整机图仅在需要大图预览时下载到用户目录。
- URL 含中文/空格时必须先做 URL 编码；直接 `urllib`/curl 可能报
  `'ascii' codec can't encode characters`。
- 下载后校验文件头：PNG `89 50 4E 47`、JPEG `FF D8 FF`、WebP `RIFF....WEBP`。
- 同一 URL 在多个型号复用是正常的；按最终型号目录落盘，不要跨目录共享路径。

### Step 6：生成 manifest

- 一个型号一个目录、一个 `manifest.json`。
- `defaultColor` 取官方顺序的第一项或官方 `current`；不要自己发明颜色顺序。
- `label` 可以先用官方 `className` 清洗后的英文；缩略图本身是主要识别依据。
- 内置资产的 `image` 可以直接等于 `thumbnail`，减少重复文件。

### Step 7：压缩缩略图

- 保留透明通道，不要转成白底 JPG。
- 不要放大；只做有损/无损压缩，目标是在 200~300px 宽度下仍能看清轮廓。
- 压缩后**不要改文件名**，除非同步改 manifest；否则用校验脚本会报缺失。
- 本次迈从压缩前约 22.4 MB，压缩后约 4.5 MB。

### Step 8：校验

```powershell
python scripts/check-device-images.py
dotnet build VigilWatt.sln -c Debug
```

校验脚本会检查：每个 `thumbnail`/`image` 是否存在、显示名是否跨型号冲突、型号/配色/文件数/体积。
退出码 1 表示有缺失或冲突，不能提交。

### Step 9：更新文档

- 本文「当前资产」和「变更记录」；
- `docs/project/changelog.md` 加一条；
- `docs/project/handoff.md` 只改 §1 现状；
- 新品牌脚本/源地址补进 `scripts/README.md`（如果新增脚本）。

## 6. 本次迈从全量记录（2026-11-12）

### 6.1 来源

- 键盘全量：`https://cdn.mchose.com.cn/configCenter/custom/cardList.json`
  - 内容是 `__compressBase64__` → zip → `data.json`；
  - 30 个条目、159 个配色；
  - 每个条目有 `identities[]`（VID/PID）和 `colorList[]`（`className` / `image` / `deviceImage`）。
- G87 V1：不在 `cardList.json`，来自本机
  `%APPDATA%\MCHOSEHUB\files\config\mc_main_store_key.json` 的 `webIndexList[0].colorList`
  （11 个配色）。
- `newMouseConfig.json` / `mouseConfig.json` 只有固件版本和固件文件，**没有产品图字段**；
  鼠标/耳机图片不能套用键盘 cardList，需另找官方源。

### 6.2 结果

| 项 | 数量 |
|---|---:|
| 内置型号 | 31（30 cardList 键盘 + G87 V1） |
| 配色 | 170（159 + 11） |
| 文件 | 201（31 manifest + 170 缩略图） |
| 压缩后体积 | 约 4.5 MB |

### 6.3 重抓脚本

```powershell
python scripts/import-mchose-images.py
```

- 默认只下载缩略图并写项目 `Assets/device-images/mchose`；
- 已有 G87 V1 目录不会被删；
- 需要整机大图时加 `--with-full --debug-data-dir ...`，整机图只写本机数据目录，不入库。

## 7. 已知坑（接新品牌前先看）

1. **不要把官方默认色当成硬件颜色**：官方 HUB 的 `checkIndex` 是用户选择，不是设备回读。
2. **不要按设备 ID 选图**：插线/换口 ID 会变，历史记录也不能作为图片身份。
3. **不要只按 VID/PID 选图**：共用 dongle 会把别的型号图挂上来。
4. **共用别名不要写进 manifest**：`Ace 68 Air` 这类营销名可能同时属于 II/III。
5. **URL 中文/空格要编码**：否则下载线程会直接失败。
6. **压缩后不要改文件名**：manifest 引用的是文件名；改名必须同步改 JSON。
7. **内置资产只放缩略图**：整机图放用户目录，避免仓库/安装包膨胀。
8. **不同品牌不要共用 `<型号>` 目录**：品牌目录是匹配边界的一部分。
9. **cardList 不是所有品牌都有**：MCHOSE 键盘有，鼠标/耳机没有；新品牌先确认源结构。

## 8. 验证清单

- [ ] `python scripts/check-device-images.py`：`missing=0`、`duplicateDisplayNames=0`
- [ ] `dotnet build VigilWatt.sln -c Debug`：0 警告 0 错误
- [ ] 输出目录有 `Assets/device-images/<品牌>/<型号>/manifest.json` 与缩略图
- [ ] 详情页 Hero 能显示图；无图设备回退几何图标
- [ ] Hero 悬停调整按钮能切换配色，保存后立即生效
- [ ] 列表、托盘、任务栏贴片、桌面小组件没有变化

## 9. 边界

- 产品图只读本地文件，不联网；不参与设备认领、轮询、历史库。
- 厂商产品图版权归厂商；当前按个人自用处理，缩略图作为项目资源随包分发。将来若商用，
  需要重新评估授权。
- 新增品牌/型号优先只加资产与 manifest；只有通用匹配/加载能力确实不够时才改代码。
