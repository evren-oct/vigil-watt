using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using VigilWatt.Core.Utils;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 启动日志的身份快照：完整性级别决定能不能写用户目录、能不能跟任务栏说话。
/// 2026/10/20 23:05:00 xliu
/// 只读；失败返回"未知"，绝不让诊断本身变成启动失败的原因。判据见 runtime-pitfalls.md §20。
/// 管理员与沙箱组只在日志级别 Debug 时计算：那两项要查域，启动路径上等不起。
/// </summary>
internal static class ProcessIdentity
{
    private const int TokenIntegrityLevel = 25;

    public static string Describe()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            var text = $"用户={id.Name} 会话={Process.GetCurrentProcess().SessionId} " +
                       $"完整性={IntegrityLevel(id.Token)}";

            // 组名翻译在域机上要逐个问 LSASS/AD（二三十个组近 1 秒），只在日志级别降到 Debug 时才算
            if (!Log.IsEnabled(LogLevel.Debug)) return text;

            var sandbox = id.Groups?
                .Select(g =>
                {
                    try { return g.Translate(typeof(NTAccount)).Value; }
                    catch { return string.Empty; }
                })
                .Where(n => n.Contains("Sandbox", StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? Array.Empty<string>();

            var elevated = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator) ? "是" : "否";
            return text + $" 管理员={elevated} " +
                          $"沙箱组={(sandbox.Length == 0 ? "无" : string.Join("|", sandbox))}";
        }
        catch (Exception ex)
        {
            return "未知（" + ex.GetType().Name + "）";
        }
    }

    /// <summary>Low 进程写不了 Medium 的用户目录与 HKCU，也发不了 shell 消息——这三个症状同源。</summary>
    private static string IntegrityLevel(IntPtr token)
    {
        int size;
        GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out size);
        if (size <= 0) return "未知";

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out size)) return "未知";

            // TOKEN_MANDATORY_LABEL = { SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes } }
            var sid = Marshal.ReadIntPtr(buffer);
            var count = Marshal.ReadByte(sid, 1);          // SID 头：Revision, SubAuthorityCount
            var rid = Marshal.ReadInt32(sid, 8 + (count - 1) * 4);  // 最后一个 SubAuthority

            return rid switch
            {
                0x0000 => "Untrusted",
                0x1000 => "Low",
                0x2000 => "Medium",
                0x3000 => "High",
                0x4000 => "System",
                _ => $"0x{rid:X}",
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr buffer, int length, out int returnLength);
}
