using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： 首版接收器身份策略：仅目录中 Bolt 族且非 Direct 路径填 Bolt，其它保持 null。
/// 2026/11/10 21:22:41 20211
/// </summary>
internal static class LogitechReceiverIdentityPolicy
{
    /// <summary>当前目录只有 C548 为 Bolt；C52B/C532 等其它族不开放。</summary>
    public static ReceiverIdentity? For(bool directDevice, ushort receiverPid)
        => !directDevice && HidppCatalog.Family(receiverPid) == ReceiverFamily.Bolt
            ? new ReceiverIdentity(null, "bolt")
            : null;
}
