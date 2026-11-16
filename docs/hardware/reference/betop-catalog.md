# 北通设备目录与身份线索（2026-11-16）

> 本文只记录**身份**：VID/PID → 型号 / 角色 / 协议 / 配对关系 / 能力声明。
> PID 线索来自 Linux xpad 的 KP 系列补丁（2026-07）与项目真机记录；xpad 只证明 XInput 身份，
> **不证明电量通道存在**。逐型号验证状态见文末分类；协议细节见 [betop.md](../brands/betop.md) §1。

## 1. 数据来源

- **Linux xpad 补丁**：`0x20BC` 的 KP20/KP40/KP50/KP70 控制器与 XInput dongle PID，以及
  `0x20DD:0x515C` 的 KP40D 接收器。用于型号与角色映射，不能当电量协议证据。
- **项目真机记录**：T9（`5073`）、鲲鹏20 2.4G（`5159`）、鲲鹏20 有线（`5158`）的验证状态见
  [betop.md](../brands/betop.md) §1.4、§1.8。
- **用户日志**：`20BC:5130` 的 KP70 XInput dongle 曾广播 `02 15 50 00 50 …` = 80%；仅作线索。

## 2. 目录

`role` 只描述这颗 PID 在目录里的角色：`controller`（手柄/承载电量的 XInput dongle）、
`dongle`（接收器）、`dock`（底座）。`group` 只有 5158/5159 写了 `kunpeng20`。

| VID:PID | 型号 | role | 协议 | 配对/组 | 能力 | 来源与验证 |
|---|---|---|---|---|---|---|
| `20BC:5073` | T9 | controller | `betop-t9` | — | `batteryRead=supported`、`chargingRead=unsupported` | 本机真机；Col04 被动流，无充电标志 |
| `20BC:5125` | —（歧义） | controller | `betop-status15` | — | 未声明 | xpad 标 KP20A/KP40A 共用；电量未验证 |
| `20BC:5126` | 鲲鹏20 | controller | `betop-status15` | — | 未声明 | xpad 标 KP20A；电量未验证 |
| `20BC:5127` | —（歧义） | controller | `betop-status15` | — | 未声明 | xpad 标 KP20A/KP40A 共用；电量未验证 |
| `20BC:5128` | 鲲鹏20 | controller | `betop-status15` | — | 未声明 | xpad 标 KP20A；电量未验证 |
| `20BC:5158` | 鲲鹏20 | controller | `betop-status15` | `pairedPid=5159` / `group=kunpeng20` / `wired=true` | `batteryRead=supported`、`chargingRead=supported` | 贡献者真机；有线 `02 15` 查询 78% 充电中 |
| `20BC:5159` | 鲲鹏20 | controller | `betop-status15` | `pairedPid=5158` / `group=kunpeng20` / `wired=false` | `batteryRead=supported`、`chargingRead=supported` | 贡献者真机；2.4G 杂项页每秒广播 |
| `20BC:5191` | 鲲鹏20 EB | controller | `betop-status15` | — | 未声明 | KP20EB；主动查询未真机验证 |
| `20BC:5145` | —（歧义） | controller | `betop-status15` | — | 未声明 | xpad 标 KP40A/KP40B 共用；电量未验证 |
| `20BC:5146` | —（歧义） | controller | `betop-status15` | — | 未声明 | xpad 标 KP40A/KP40B 共用；电量未验证 |
| `20BC:5154` | 鲲鹏40 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40D；电量未验证 |
| `20BC:5155` | 鲲鹏40 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40D；电量未验证 |
| `20BC:515B` | 鲲鹏40 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40D White；电量未验证 |
| `20BC:515C` | 鲲鹏40 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40D White；电量未验证 |
| `20BC:515D` | 鲲鹏40 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40F White；电量未验证 |
| `20BC:515E` | 鲲鹏40 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40F White；电量未验证 |
| `20BC:5169` | 鲲鹏40 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40F Black；电量未验证 |
| `20BC:516A` | 鲲鹏40 | controller | `betop-status15` | — | 未声明 | xpad 标 KP40F Black；电量未验证 |
| `20BC:5133` | 鲲鹏50 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50B；电量未验证 |
| `20BC:5149` | 鲲鹏50 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50C；电量未验证 |
| `20BC:5150` | 鲲鹏50 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50D；电量未验证 |
| `20BC:5151` | 鲲鹏50 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50D；电量未验证 |
| `20BC:5152` | 鲲鹏50 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50E；电量未验证 |
| `20BC:5153` | 鲲鹏50 | controller | `betop-status15` | — | 未声明 | xpad 标 KP50E；电量未验证 |
| `20BC:5134` | 鲲鹏50 | dongle | `betop-status15` | — | 未声明 | xpad 标 KP50B XInput Dongle；电量未验证 |
| `20BC:514A` | 鲲鹏50 | dongle | `betop-status15` | — | 未声明 | xpad 标 KP50C XInput Dongle；电量未验证 |
| `20BC:512F` | 鲲鹏70 | controller | `betop-status15` | — | 未声明 | xpad 标 KP70A；电量未验证 |
| `20BC:5130` | 鲲鹏70 | controller | `betop-status15` | — | 未声明 | xpad 标 KP70A XInput Dongle；用户日志广播 80% |
| `20BC:515F` | 鲲鹏70 | controller | `betop-status15` | — | 未声明 | xpad 标 KP70A；电量未验证 |
| `20BC:5160` | 鲲鹏70 | controller | `betop-status15` | — | 未声明 | xpad 标 KP70A；电量未验证 |
| `20DD:5128` | 鲲鹏20 接收器 | dongle | `betop-status15` | — | 未声明 | KP20 DONGLE；只回 `02 CD`，电量格式未知（CR-106） |
| `20DD:5130` | 鲲鹏70 底座 | dock | `betop-status15` | — | 未声明 | BTP-KP70A DONGLE 底座；只回 `02 CD`，电量格式未知（CR-106） |
| `20DD:515C` | 鲲鹏40 接收器 | dongle | `betop-status15` | — | 未声明 | xpad 的 KP40D HID receiver；电量未验证 |

## 3. 型号歧义

以下 PID 在 xpad 资料里同时对应 KP20A 与 KP40A/KP40B，**不能按 PID 数值猜型号**：

- `20BC:5125`、`20BC:5127`：KP20A / KP40A 共用。
- `20BC:5145`、`20BC:5146`：KP40A / KP40B 共用。

目录里这些条目的 `model` 留空；显示名由产品串分流（`KP20` → 鲲鹏20，`KP40` → 鲲鹏40，
两者同时出现 → 鲲鹏20/40）。产品串也分不出时使用「北通手柄 (VID:PID)」，不冒充已确认型号。

## 4. 身份规则

1. **只有显式 `group` 才归一**：当前只有 `20BC:5158` / `20BC:5159` 的 `group=kunpeng20`，
   归一到 `betop:kunpeng20`。
2. **不按系列合并**：同属鲲鹏40 的多个 PID 没有配对证据，各自走路径哈希。
3. **不跨 VID 合并**：`20BC:5128` 与 `20DD:5128` 即使 PID 相同也是两条独立身份。
4. **同型号两只并行仍会并成一台**：`group` 是型号粒度，dongle 与本体没有配对关系数据；
   这是现有取舍，与 `betop.md` §1.8.2 记录一致。

## 5. 验证状态分类

| 分类 | 型号 |
|---|---|
| 已确认电量 | T9（`5073`）、鲲鹏20 2.4G（`5159`）、鲲鹏20 有线（`5158`） |
| 型号歧义、待产品串分流 | `5125`、`5127`、`5145`、`5146` |
| 仅身份线索、电量未验证 | KP20/KP40/KP50/KP70 其余 PID、`20BC:5134/514A`、`20DD:515C` |
| 电量格式未知 | `20DD:5128`、`20DD:5130`（CR-106） |

## 6. 相关文档

- 协议与真机时间线：[betop.md](../brands/betop.md) §1。
- 目录/协议 JSON 写法：[protocol-json.md](../../dev/protocol-json.md) §2。
- 未决项：[BACKLOG.md](../../reviews/BACKLOG.md) CR-103 / CR-104 / CR-106。
