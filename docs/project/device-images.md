# 设备产品图

> 详情页 Hero 可以显示厂商产品图；**列表、托盘、任务栏贴片、桌面小组件仍用几何图标**。
> 图片只从本机数据目录读取，不联网；没有图或清单损坏时自动回退到 `DeviceIconFactory`。

## 1. 放哪里

```
<数据根>\device-images\<品牌>\<型号>\
    manifest.json
    G87_white.png
    G87WhiteFull_....png
```

数据根由 `AppPaths.RootDir` 决定：Debug 是 exe 旁的 `data\`，Release 安装版是
`%LOCALAPPDATA%\VigilWatt`，便携版是解压根目录的 `data\`。`VIGILWATT_DATA_DIR` 可以覆盖。
开发版从 `bin\x64\Debug\...\win-x64\` 启动，图片包就放在同目录的 `data\device-images\` 下。

当前已整理：迈从 G87 的 11 个配色（缩略图 + 整机图）。图片来自 MCHOSE HUB 的
`webIndexList.colorList` 与 `cdn.mchose.com.cn`，只在本机使用。

## 2. 清单格式

```jsonc
{
  "schemaVersion": 1,
  "brand": "MCHOSE",
  "model": "G87",
  "match": {
    "vendorId": "41E4",
    "productIds": ["2001"],
    "displayNames": ["迈从 G87", "MCHOSE 2.4G Wireless-G87"]
  },
  "defaultColor": "color-white",
  "colors": [
    {
      "key": "color-white",
      "label": "白色",
      "thumbnail": "G87_white.png",
      "image": "G87WhiteFull_b3933ff72b9f.png",
      "source": "https://cdn.mchose.com.cn/..."
    }
  ],
  "source": "MCHOSE HUB 1.2.05"
}
```

`thumbnail` 构图更紧凑，详情页 Hero 优先用它；`image` 是整机图，留给以后的大图预览。
路径相对清单所在目录，必须落在 `device-images\` 根目录内；越界路径会被忽略。

## 3. 匹配规则

1. **显示名精确命中**优先：把清单 `match.displayNames` 与设备当前 `Name` 都去掉空格、连字符、
   下划线并转小写后比较。`迈从 G87` 与 `MCHOSE 2.4G Wireless-G87` 都能命中。
2. 显示名被用户改过时，只有 **协议自报型号 + VID/PID 同时命中**才认（例如迈从 0x0900 的
   `Reported.Model = "G87"` 且 `41E4:2001` 在 `productIds` 里）。
3. **只有 VID/PID 不足以选图**：同一颗 dongle 常被多款型号共用，按 PID 猜会把别的型号图挂上来。
4. 不按设备 ID 匹配：ID 是接口路径哈希，插线/换口会变；历史记录也不能作为图片身份。
5. 多个清单命中时取分数最高的一条：显示名 + VID/PID > 显示名 > 自报型号 + VID/PID。

## 4. 配色与回退

- **官方 HUB 不读取设备配色**：本机 `MCHOSEHUB` 状态里是 `checkIndex: 0` / `lockIndex: null`，
  `checkIndex` 只是官方 UI 记录的用户选择索引（默认 0 = 白色），不是硬件回读。因此 VigilWatt
  不能从协议推断颜色，只能先用 `defaultColor`。
- `defaultColor` 命中就用它；缺失或写错时回退 `colors[0]`。
- **用户覆盖**：详情页 Hero 悬停时右下角出现调整按钮，点开可选任一配色；选择按设备 ID 存进
  `settings.json` 的 `DeviceCustomizations[id].ImageKey`，详情页优先用它，改错随时可改。
- 图片文件不存在、清单 JSON 损坏、路径越界：跳过该条；整个型号没有可用图片就回退几何图标。

## 5. 边界

- 只读本地文件，不访问网络；不参与设备认领、轮询、历史库。
- 厂商产品图版权归厂商；当前按“个人自用、本机展示”处理，不提交到仓库、不随安装包分发。
- 新增品牌/型号只需加目录与 `manifest.json`，不改代码；不同品牌不要共用同一个 `<型号>` 目录。
