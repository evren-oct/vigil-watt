using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： 接收器身份策略：非 Direct 且目录登记了展示名的接收器，一律填体系名与图标；未收录 PID 保持 null。
/// 2026/11/11 15:22:00 20211
/// 身份只是目录事实（这颗 PID 属于哪一族），不是链路结论——`Connection` 仍由 LogitechConnectionPolicy 单独门控。
/// </summary>
internal static class LogitechReceiverIdentityPolicy
{
    /// <summary>展示名是身份的锚点：目录没写 `familyDisplay` 就没有标签，填了也是空壳，还会覆盖掉已有身份。</summary>
    public static ReceiverIdentity? For(bool directDevice, ReceiverInfo? receiver)
        => !directDevice && receiver is { DisplayName: { Length: > 0 } } r
            ? new ReceiverIdentity(null, r.Family.ToString().ToLowerInvariant(), r.DisplayName, r.IconKey)
            : null;
}
