// 在真机上把「VgnPlugin 的 y2 分支」和「GanssPlugin 的 F108 分支」各跑一遍：
// 用 Core 真正的 HidDeviceScanner / HidDeviceHandle / Codec 发一条查询，打印拿到的原始帧与解析结果。
// 只发读电量这一条查询帧，不写配置、不碰 %LOCALAPPDATA% 数据目录、不开窗口。
using VigilWatt.Core.Hid;
using VigilWatt.Core.Plugins.Ganss;
using VigilWatt.Core.Plugins.Vgn;

Console.OutputEncoding = System.Text.Encoding.UTF8;

int vgnHit = 0, aulaHit = 0;

foreach (var info in HidDeviceScanner.Scan())
{
    bool y2 = VgnY2Codec.IsMatch(info.VendorId, info.ProductId, info.UsagePage, info.Usage,
        info.InputReportLength, info.OutputReportLength);

    bool aula = GanssCodec.IsSupportedInterface(info.VendorId, info.ProductId, info.DevicePath, info.Product,
        info.UsagePage, info.Usage, info.InputReportLength, info.OutputReportLength, info.OutputReportId);

    if (!y2 && !aula) continue;

    Console.WriteLine(new string('-', 72));
    Console.WriteLine($"接口: {info}");
    Console.WriteLine($"  路径: {info.DevicePath}");
    Console.WriteLine($"  产品名: '{info.Product}'  outputReportId={info.OutputReportId?.ToString() ?? "null"}");

    if (y2)
    {
        vgnHit++;
        Console.WriteLine("  判据: ✅ 命中 VgnPlugin 的 vgn-y2 分支（3554:FB56/FB57 + 0xFF02/0x0002 + 17/17）");
        using var handle = info.Open();
        var frame = VgnY2Codec.BuildCommand(VgnY2Codec.CmdBatteryLevel);
        Console.WriteLine($"  发送: {Convert.ToHexString(frame)}");
        if (!handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500)))
        {
            Console.WriteLine("  ❌ 写失败");
            continue;
        }
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.2);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            Console.WriteLine($"  收到: {Convert.ToHexString(input)}");
            if (VgnY2Codec.TryParseBattery(input, out int percent, out var charging, out int mv))
            {
                Console.WriteLine($"  ✅ 解析: 电量={percent}% 充电={charging} 电压={mv}mV");
                break;
            }
            Console.WriteLine("  … 不是电量应答，继续等");
        }
    }

    if (aula)
    {
        aulaHit++;
        string brand = GanssCodec.LooksLikeAulaF108(info.Product) ? "狼蛛 F108 Pro" : "GANSS GS3104T-Pro";
        Console.WriteLine($"  判据: ✅ 命中 GanssPlugin（识别为 {brand}）");
        using var handle = info.Open();
        var frame = GanssCodec.CreateQuery();
        Console.WriteLine($"  发送: {Convert.ToHexString(frame)}");
        if (!handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500)))
        {
            Console.WriteLine("  ❌ 写失败");
            continue;
        }
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.2);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            Console.WriteLine($"  收到: {Convert.ToHexString(input)}");
            if (GanssCodec.TryParse(input, out int percent))
            {
                Console.WriteLine($"  ✅ 解析: 电量={percent}%");
                break;
            }
            Console.WriteLine("  … 不是电量应答，继续等");
        }
    }
}

Console.WriteLine(new string('-', 72));
Console.WriteLine($"命中 VGN y2 接口 {vgnHit} 条，05AC:024F 电量接口 {aulaHit} 条");
if (vgnHit == 0) Console.WriteLine("⚠ 没扫到 VGN y2 通道：确认接收器插着、且是 3554:FB56 / 0xFF02:0x0002 / 17-17");
if (aulaHit == 0) Console.WriteLine("⚠ 没扫到 05AC:024F 电量接口：确认 dongle 插着；若驱动占着独占句柄，先关掉官方驱动");
