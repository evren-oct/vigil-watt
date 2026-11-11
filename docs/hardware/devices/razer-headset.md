# Razer BlackShark V2 Pro (2023)

## 1. 范围与协议依据

本地实测 USB 接收器 `1532:0555`，产品字符串 Razer BlackShark V2 Pro 2.4，
usage `FF00:0001`，`MI_03&Col02`，64 字节输入与输出，report ID `02`。
只接入这一已验证接口，不接入旧版 2020 的 `0528` 或未测的 `0556`。

协议依据是 [OpenRazer PR #2862](https://github.com/openrazer/openrazer/pull/2862)
中的 [PA 驱动](https://github.com/FalconHeavy57/openrazer/blob/1125585244f3c89a895523a53793b8045eef125d/driver/razerblackshark_driver.c)
与 [格式定义](https://github.com/FalconHeavy57/openrazer/blob/1125585244f3c89a895523a53793b8045eef125d/driver/razerblackshark_driver.h)；
该 PR 截至 2026-10-25 读取时尚未合并。
[HaloBattery 的同型号实现](https://github.com/HeyOkay/HaloBattery/blob/main/providers/blackshark.py)
独立印证了 Windows 接口、唤醒和查询顺序。

OpenRazer 驱动声明 GPL-2.0-or-later；这里仅按公开协议事实和本机响应独立编写 C#，
没有复制驱动或外部 provider 代码。

## 2. 格式

这不是鼠标的 90 字节 Chroma 协议，而是独立 **PA** 音频协议。
所有偏移均包含 report ID。

查询 64 字节：`[0]=02 [1]=80 [2]=08 [5]=50 [6]=41 [7]=08 [9]=03 [10]=command`，
其余为零。命令仅允许 `21`（电量）与 `2A`（充电），不允许 EQ、音量、麦克风、
配置或固件命令。

远程模式帧：同 PA 外壳，但 `[2]=07 [7]=0E [9]=02 [10]=E1 [11]=on/off`。
应答是 PI 外壳：`[0]=02 [2]=50 [3]=49 [12]=命令 [13]=01 ACK [14]=01 [15]=返回值`。
严格核对 64 字节长度、外壳、命令、ACK 和长度后采信；通知帧不能代替应答。
电量为直接的 0–100，充电为零/非零。

## 3. 会话和预算

无线链路短暂空闲就可能丢弃第一帧，不能按鼠标协议直接问电量。

每轮只读一种数据，并完成一笔有界的会话：
1. Remote-on 作为唤醒帧，等 35 ms。
2. 再次 Remote-on，等 35 ms。
3. 发一条电量或充电查询，700 ms 内等待匹配应答。
4. `finally` 发送 Remote-off，归还会话；失败留告警。

因此本型号工作一轮固定最多 **3 条会话控制帧 + 1 条只读查询**。
这是公开协议所需的唤醒/进入/释放序列，不是命令枚举。
进入事务时核对 Razer 开关并捕获本笔授权，事务完成前即使外部切换开关也须执行释放；
之后新事务会被关闭的开关阻挡。尚未运行无响应时的额外重试。

未认领接口参与 15 秒发现节拍，失败冷却 8 秒；已认领接口的发现路径不发帧。
首次探测读电量，之后按 60 秒主轮询交替电量和充电，每类约 120 秒一次；
刚发现时充电状态为 Unknown，拿到回应后才更新。鼠标与耳机状态按句柄分别缓存，
耳机不会影响鼠标的报文、权限或设备图标。均使用同一个「雷蛇 / Razer」品牌开关。

失败走基类计数与递增退避，释放句柄和认领沿用基类。
休眠恢复的等待边界沿用现有查询型插件机制（见现有 BACKLOG CR-81），
没有声称消除了无线休眠或雷云并发的所有影响。

## 4. 实机证据

2026-10-25：
- 电量应答头 `02 0E 50 49 08 CE 92 DE 05 00 04 00 21 01 01 4F` → **79%**。
- 充电应答头 `02 0E 50 49 08 CF B6 E0 05 00 04 00 2A 01 01 00` → 未充电。
- Remote-off 发送成功；读取期间雷云保持运行。

已有 Viper V3 Pro SE 鼠标支持保留，仍通过 Feature 专用句柄读取，设备类型为 Mouse；
新增耳机协议类型 `razer-pa` 映射到 Headset。

未实测：实际充电切换、关机、自然睡眠/唤醒、拔插、不同固件、长期与雷云并行。

实际插件集成验证读取耳机 77% / Headset / Discharging；
默认禁用不认领，启用后轮询、独立 SQLite 历史与 Detach 释放通过。
构建与回归检查见 [适配审查](../../reviews/archive/2026-10-25-asus-razer-adaptation-review.md)。
