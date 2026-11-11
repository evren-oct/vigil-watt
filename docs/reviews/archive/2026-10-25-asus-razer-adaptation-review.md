# 2026-10-25 ASUS / Razer 适配审查

## 1. 基线与边界

最终基线：上游 main `b8fe68f0f11120f0f9a889166bf1356ac20b7fda`（PR #56 合并后）。
范围：Scope II 96 / Omni、BlackShark V2 Pro 2023、Viper V3 Pro SE，以及 Feature 专用句柄。
本次 PR 整理没有向真实设备发帧；硬件证据来自 2026-10-24/25 的隔离插件验证。

## 2. 改动核查

- R1/R2：Asus 和 Razer 默认关闭，发送点受品牌开关约束，均继承 BatteryPluginBase。
- R3：每句柄状态隔离；轮询沿用基类 IoGate，无新增后台监听线程。
- R4：Feature 句柄直接拥有 SafeFileHandle；流构造失败释放句柄；Omni 控制句柄 using 释放；耳机会话 finally 归还。
- R5：无效回应记去重日志，查询失败纳入基类熔断与递增退避；会话释放失败留告警。
- R6/R7/R8：协议事实与限制集中于 hardware 文档；纯解析、接口匹配和查询状态链接真实源码测试。
- 键盘只向同一物理接收器查询配对表，拒绝重复或不匹配的键盘通道。
- 新增鼠标接口仅使用 Feature；耳机 PA 协议与鼠标独立，设备类型为 Headset。

## 3. 修正与遗漏

补上重复键盘通道、其他通道配对设备和耳机非零充电标志的边界回归。
上游同期接入 V4：保留 imx1n 署名、RazerCodec 公共 API、PID、缩放及原查询重试；
新增 SE 严格解析独立为 RazerViperSeCodec，不参与 V4 事务号学习。
V4 查询发送点补查品牌开关，失败改由实际查询结果记账；原 8 秒冷却、15 秒发现和低电量下界保持。
HID 保留上游 OpenRead/OpenReadWrite 权限分支，Feature 专用访问另走 OpenFeature。
移除文档中的开发机器路径、启停经过与本地开关配置。
本次改动范围未发现新增未修项；休眠恢复仍沿用既有机制（BACKLOG CR-81）。

## 4. 未证实部分

未实测自然休眠/唤醒、实际充电切换、低电量、物理拔插、不同固件、多只 Omni 或长期并行雷云。
开机发现恢复仅做隔离调度模拟，未通过真实电脑重启验收。

## 5. 验证与去向

- 最终基线完整 Debug 解决方案：0 警告、0 错误；相关回归 172 通过、0 失败、0 跳过，包含上游 V4 协议测试。
- 隔离调用实际编译插件的 21 项检查通过：候选接口、旧 V4 路径/名称/下界、新型号权限/类型/下界、发现冷却、默认关闭不认领。该检查未向硬件发帧。
- 既有真机集成证据：键盘 95% / Keyboard，耳机 77% / Headset，鼠标 58% / Mouse，均 Discharging。
- 默认关闭不认领；启用后的轮询、独立 SQLite 历史与 Detach 释放已验证。
- 协议与硬件证据见 [ASUS](../../hardware/brands/asus-rog.md)、[耳机](../../hardware/devices/razer-headset.md)、[鼠标](../../hardware/devices/razer-viper-se.md)。
- 无新增 BACKLOG 项。
