using VigilWatt.Core.Plugins.Mchose;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 迈从家族接口分流的回归测试（键盘 / 鼠标 / 耳机三条链路不能互相抢）。
/// 2026/10/19 21:10:00 xliu
/// 指纹来源见 docs/hardware/mchose.md §1、§1.6，以及 docs/hardware/mchose-v9-pro.md。
/// </summary>
public sealed class MchoseInterfaceClassifierTests
{
    private const string HeadsetPath = @"\\?\hid#vid_291d&pid_385d&mi_00&col05#test";

    [Theory]
    // 耳机：完整判据命中
    [InlineData(0x291D, 0x385D, HeadsetPath, 0xFF90, 0x0001, 64, 64, 0, 0x55, MchoseInterfaceKind.HeadsetV9)]
    // 耳机同 VID/PID 但换了 collection：不认，也不能掉进键鼠分支
    [InlineData(0x291D, 0x385D, @"\\?\hid#vid_291d&pid_385d&mi_00&col01#test", 0xFF90, 0x0001, 64, 64, 0, 0x55, MchoseInterfaceKind.None)]
    // 耳机同 VID/PID 但长度是键盘的 20：仍不认（VID 不在键鼠白名单里）
    [InlineData(0x291D, 0x385D, HeadsetPath, 0xFF02, 0x0002, 20, 20, 0, 0x13, MchoseInterfaceKind.None)]
    // 键盘：本机 G87 V1（41E4:2001）
    [InlineData(0x41E4, 0x2001, @"\\?\hid#vid_41e4&pid_2001&mi_01&col01#test", 0xFF02, 0x0002, 20, 20, 0, 0x13, MchoseInterfaceKind.KeyboardV1)]
    // 键盘：G87 V2 接收器（3837:201B）
    [InlineData(0x3837, 0x201B, @"\\?\hid#vid_3837&pid_201b&mi_01&col01#test", 0xFF02, 0x0002, 20, 20, 0, 0x13, MchoseInterfaceKind.KeyboardV1)]
    // 鼠标：A7 V2 Pro 接收器（3837:100A，65 字节含 report ID）
    [InlineData(0x3837, 0x100A, @"\\?\hid#vid_3837&pid_100a&mi_02&col03#test", 0xFF00, 0x0002, 65, 65, 0, 0x11, MchoseInterfaceKind.MouseV2)]
    // 鼠标：QHW 世代 dongle（3837:3033）
    [InlineData(0x3837, 0x3033, @"\\?\hid#vid_3837&pid_3033&mi_01&col01#test", 0xFF00, 0x0002, 64, 64, 0, 0x4D, MchoseInterfaceKind.MouseV2)]
    // 鼠标：K7 V2 Ultra+ 的命令通道（3837:1018，FF01/0x0001，64 字节 output 0x4D）——必须抢在 MouseV2 之前
    [InlineData(0x3837, 0x1018, @"\\?\hid#vid_3837&pid_1018&mi_02&col02#test", 0xFF01, 0x0001, 64, 64, 0, 0x4D, MchoseInterfaceKind.MouseK7)]
    // 同一颗 K7 dongle 的另一条接口（FF0B/0x00C4，61 字节）：实测零报文，仍按普通鼠标接口走
    [InlineData(0x3837, 0x1018, @"\\?\hid#vid_3837&pid_1018&mi_02&col01#test", 0xFF0B, 0x00C4, 61, 61, 0, 0x00, MchoseInterfaceKind.MouseV2)]
    // 鼠标：Gmouse 主动查询接口（3837:4245，FF01/0x0010，65 字节）——必须抢在 MouseV2 之前
    [InlineData(0x3837, 0x4245, @"\\?\hid#vid_3837&pid_4245&mi_02#test", 0xFF01, 0x0010, 65, 65, 0, 0x00, MchoseInterfaceKind.Gmouse)]
    // 同一颗 G3 V2 dongle 的其它厂商集合（FF05/0x0002，64 字节无输出）：不按 Gmouse 认，落 MouseV2 兜底
    [InlineData(0x3837, 0x4245, @"\\?\hid#vid_3837&pid_4245&mi_01&col05#test", 0xFF06, 0x0002, 64, 64, 0, 0x00, MchoseInterfaceKind.MouseV2)]
    // 标准键盘用法：即使 VID 是迈从也不是配置通道
    [InlineData(0x3837, 0x201B, @"\\?\hid#vid_3837&pid_201b&mi_00&col01#test", 0x0001, 0x0006, 20, 20, 0, 0x00, MchoseInterfaceKind.None)]
    // 别家 VID：罗技（046D）、雷柏（24AE）、AULA（3554）都不能进迈从链路
    [InlineData(0x046D, 0xC548, @"\\?\hid#vid_046d&pid_c548&mi_01&col01#test", 0xFF00, 0x0001, 20, 20, 0, 0x11, MchoseInterfaceKind.None)]
    [InlineData(0x24AE, 0x1412, @"\\?\hid#vid_24ae&pid_1412&mi_01&col01#test", 0xFF00, 0x0002, 19, 19, 0, 0x07, MchoseInterfaceKind.None)]
    [InlineData(0x3554, 0xFA09, @"\\?\hid#vid_3554&pid_fa09&mi_01&col01#test", 0xFF02, 0x0002, 20, 20, 0, 0x13, MchoseInterfaceKind.None)]
    // 本地适配的雷神与黑爵接口不可被迈从认领。
    [InlineData(0x369B, 0x0234, @"\\?\hid#vid_369b&pid_0234&mi_01&col04#test", 0xFFA0, 0x0002, 64, 64, 0, 0xA0, MchoseInterfaceKind.None)]
    [InlineData(0x3151, 0x4011, @"\\?\hid#vid_3151&pid_4011&mi_02#test", 0xFFFF, 0x0002, 0, 0, 65, 0x00, MchoseInterfaceKind.None)]
    // 长度既不是 20 也不到 60：不认
    [InlineData(0x3837, 0x201B, @"\\?\hid#vid_3837&pid_201b&mi_01&col01#test", 0xFF00, 0x0002, 32, 32, 0, 0x13, MchoseInterfaceKind.None)]
    public void ClassifyRoutesEachLinkToItsOwnKind(ushort vid, ushort pid, string path,
        ushort page, ushort usage, ushort input, ushort output, ushort feature, int id,
        MchoseInterfaceKind expected)
        => Assert.Equal(expected, MchoseInterfaceClassifier.Classify(vid, pid, path,
            page, usage, input, output, feature, (byte)id));

    /// <summary>耳机判据要求 feature 长度为 0、reportId 为 0x55，任一条不满足就不能落到耳机链路。</summary>
    [Theory]
    [InlineData(1, 0x55)]
    [InlineData(0, 0x00)]
    [InlineData(0, 0x13)]
    public void HeadsetRequiresFeatureLengthZeroAndReportId55(ushort feature, byte id)
        => Assert.Equal(MchoseInterfaceKind.None, MchoseInterfaceClassifier.Classify(
            0x291D, 0x385D, HeadsetPath, 0xFF90, 0x0001, 64, 64, feature, id));
}
