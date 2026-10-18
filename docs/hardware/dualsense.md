# DualSense / DS5 蓝牙电量

## 1. 来源与本机样本

协议依据为 Linux `hid-playstation.c` 的 DualSense 输入结构、CRC32 与电量解析，2026-10-19 核验：
https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c
本实现依帧格式独立实现解析，未复制 Linux 驱动主体。
本机物理蓝牙 HID：054C:0CE6、服务 UUID 1124、Usage 0001:0005、InputReportLength 78。
不匹配 USB、虚拟 HID 或 Edge 的 0DF2，不依据 Output/Feature caps 的 547 上限构造报文。
首次 32 帧均通过 CRC；后续读取 3837 帧、间隔保留 32 个样本，跨度 7795ms，这 32 个样本全部通过 CRC。

## 2. 解析与精度

仅解析完整 0x31 / 78 字节蓝牙输入。CRC32 为 A1 前缀加前 74 字节，末 4 字节小端值；不接受简化 0x01 帧。
状态字节零基索引 54：低 4 位档位 0–10；高 4 位 0 放电、1 充电、2 满电。
放电/充电估算为 min(档位 × 10 + 5, 100)，满电为 100。
档位 0 估算 5%，不表示已精确测到 5%；档位 1 对应约 10%–19%，显示中点 15%。
保留/错误状态和超过 10 的档位整帧不采信，不伪造为 0% 或充电中。
目前本机仅实测状态 01，即档位 1、放电。其它电量、充电/满电只经协议与合成测试验证。

## 3. 生命周期与集成

DualSensePlugin 继承 BatteryPluginBase，WritesEnabled=false、ProbeWrites=false；无输出、Feature 请求或模式切换。
纯被动插件默认启用；缺键配置按 AppSettings 默认表补齐，用户已保存的开关不覆盖。
发现窗口 2 秒，认领后基类持续只读监听；参与 15 秒快速发现，探测冷却 5 秒。
没有普通主动轮询；空读不记查询失败，10 秒沉默经基类标离线，接口消失在扫描时释放。
设备名为 DualSense，Kind=Gamepad，Source=BluetoothClassic；历史恢复按 dualsense: 前缀保留蓝牙来源。
基类来源扩展默认仍为 Hid24G，其它既有插件行为保持原值。
Protocol 显示约 10% 档位。收到合法读数后复用既有变化推送、低电量提醒与历史采样，不改变数据库格式。
经典蓝牙 watcher 也可能提供无电量的同名物理手柄条目；当前不跨来源合并身份，电量统计使用 dualsense: 插件条目。
插件关闭或退出由基类停止监听、释放句柄与认领。关闭监听只控制监控，不涉及硬件写授权。

## 4. 验证与限制

2026-10-19 本地适配版工具实测 15%、Discharging、BluetoothClassic、Gamepad，Detach 后认领释放。
主程序托盘、低电量提醒与 SQLite 历史记录已验证。上述是此前本地适配版实测，不等于整理后每个分支都重新运行了真实设备。
测试覆盖实测样本 CRC、独立 zlib 预计算状态样本、长度、损坏、状态/范围、接口隔离及来源恢复。
其它电量、充电/满电、睡眠/重连、USB、Edge、多手柄和其它固件未真机验证。
同一物理手柄可由插件和通用经典蓝牙 watcher 提供两条记录，后者无电量，尚未跨来源合并，见 BACKLOG CR-67。
