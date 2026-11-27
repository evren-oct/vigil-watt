using System.Text;

using VigilWatt.Core.Localization;
namespace VigilWatt.Core.Capture;
/// <summary>
/// 功 能： 生成可离线打开的调查摘要（贴 Issue / 贴 AI 对话）。事实、未知分开写，不给未证实的结论。
/// 2026/10/24 12:40:43 20211
/// </summary>
public static class IssueSummaryBuilder
{
    public static string Build(CaptureBundle bundle, string? packagePath = null)
    {
        var session = bundle.Session;
        var device = bundle.Device;
        var analysis = bundle.Analysis;
        var sb = new StringBuilder();

        sb.AppendLine(UiText.Get("S_3680b39df6", "### 设备"));
        if (device == null)
        {
            sb.AppendLine(UiText.Get("S_41348bc6ad", "- 设备：未选择（仅会话级信息）"));
        }
        else
        {
            sb.AppendLine(UiText.Format("S_e918e79ff7", "- 名称：{0}", OrUnknown(device.Product)));
            sb.AppendLine(UiText.Format("S_ba52c51e02", "- 厂商：{0}", OrUnknown(device.Manufacturer)));
            sb.AppendLine(UiText.Format("S_9edc075793", "- VID/PID：{0}（版本 0x{1:X4}）", device.VidPid, device.VersionNumber));
            sb.AppendLine(UiText.Format("S_f842f17c82", "- 接口：{0}  usage 0x{1:X4}/0x{2:X4}", device.InterfaceKey, device.UsagePage, device.Usage));
            sb.AppendLine(UiText.Format("S_caf817a3ed", "- 序列号：{0}", (string.IsNullOrEmpty(device.SerialNumber) ? UiText.Get("S_44a39befd7", "设备未提供") : device.SerialNumber)));
        }

        sb.AppendLine();
        sb.AppendLine(UiText.Get("S_7d213568e1", "### 采集方式与环境"));
        sb.AppendLine(UiText.Format("S_2978106dca", "- 任务：{0}", session.Task.Label()));
        sb.AppendLine(UiText.Get("S_45eaa14106", "- 方式：USB/HID（用户选择，采集期间未切换）"));
        sb.AppendLine(UiText.Format("S_3a15185ffc", "- 系统：{0}", session.OsVersion));
        sb.AppendLine(UiText.Format("S_f6473b3321", "- 应用：VigilWatt {0}，后端 {1}", session.AppVersion, session.Backend));
        sb.AppendLine(UiText.Format("S_56739dfb15", "- 时间：{0:yyyy-MM-dd HH:mm:ss} 起，", session.StartedUtc.ToLocalTime())
                      + UiText.Format("S_427e563587", "耗时 {0}", (session.DurationMs is { } ms ? UiText.Format("S_b1a724bb3b", "{0:0.0} 秒", ms / 1000.0) : UiText.Get("S_d9c32a4c3d", "未知"))));

        sb.AppendLine();
        sb.AppendLine(UiText.Get("S_ba04bd810d", "### 能力预检"));
        foreach (var group in bundle.Session.Capabilities.GroupBy(c => c.Status))
        {
            var names = string.Join("、", group.Select(c => c.Title));
            sb.AppendLine($"- {group.Key.Label()}：{names}");
        }

        sb.AppendLine();
        sb.AppendLine(UiText.Get("S_4e6ff8e11b", "### 采集结果（事实）"));
        sb.AppendLine(UiText.Format("S_ef3a90c709", "- 原始记录 {0} 条（成功 {1}，失败 {2}），", analysis.TotalRecords, analysis.ReceivedRecords, analysis.FailedRecords)
                      + UiText.Format("S_d744b57388", "共 {0} 字节", analysis.TotalBytes));
        sb.AppendLine(UiText.Format("S_9cb523b47e", "- 事件 {0} 条，错误/告警 {1} 条", analysis.TotalEvents, analysis.TotalErrors));
        if (analysis.Groups.Count == 0)
        {
            sb.AppendLine(UiText.Get("S_d29e096567", "- 未记录到任何报文。**这不等于设备没有相关协议**，见下方待验证项。"));
        }
        else
        {
            foreach (var group in analysis.Groups.Take(6))
            {
                var id = group.ReportId is { } rid ? $"0x{rid:X2}" : UiText.Get("S_72077749f7", "无");
                sb.AppendLine(UiText.Format("S_4460e4103e", "- 分组 {0} report={1} 长度={2}：{3} 条，", group.RecordType, id, group.Length, group.Count)
                              + UiText.Format("S_92b530651a", "不同内容 {0} 种，重复率 {1:P0}", group.DistinctPayloads, group.DuplicateRatio));
            }
        }

        if (analysis.StaticGroups.Count > 0)
        {
            sb.AppendLine(UiText.Format("S_a2cb708990", "- **整场没有变化**：{0}——内容完全一致，", analysis.StaticGroupText)
                          + UiText.Get("S_3b6094527a", "可能不是状态通道（配置/握手通道常见这种表现）。"));
        }

        if (analysis.Changes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(UiText.Get("S_8261527e0f", "### 关键差异（首帧 vs 末帧）"));
            foreach (var change in analysis.Changes.Take(8))
            {
                var text = change.LengthChanged
                    ? UiText.Format("S_19937fd261", "长度 {0} → {1}", change.FromLength, change.ToLength)
                    : UiText.Format("S_9cf51e9a59", "偏移 {0}：{1:X2} → {2:X2}", change.Offset, change.From, change.To);
                sb.AppendLine($"- {change.GroupKey}：{text}");
            }
        }

        if (analysis.BatteryReferences.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(UiText.Get("S_02482bb27a", "### 电量核对（用户输入）"));
            foreach (var reference in analysis.BatteryReferences.Take(8))
            {
                var offsets = reference.MatchingOffsets.Count == 0
                    ? UiText.Get("S_4aa48f0d6c", "无匹配字节")
                    : UiText.Get("S_a243674775", "候选偏移 ") + string.Join("/", reference.MatchingOffsets);
                sb.AppendLine(UiText.Format("S_4b2fed5e5a", "- +{0}ms 用户读到 {1}% → {2}", reference.TimestampMono, reference.Percent, offsets));
            }
        }

        if (bundle.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(UiText.Get("S_b9bdceea29", "### 错误与告警"));
            foreach (var error in bundle.Errors.Take(8))
            {
                var hr = error.HResult is { } code ? $"，HRESULT 0x{code:X8}" : "";
                sb.AppendLine($"- [{error.Kind}] {error.Message}{hr}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(UiText.Get("S_0275f17185", "### 需要进一步验证"));
        if (analysis.ReceivedRecords == 0)
        {
            sb.AppendLine(UiText.Get("S_6dfe362bc6", "- 本次没有任何报文：可能是设备不自发推送（需要先开会话）、采错了接口、")
                          + UiText.Get("S_eb38aee869", "或该接口的通道类型不是输入报告。**不能据此判定设备没有电量/状态协议。**"));
        }
        else
        {
            sb.AppendLine(UiText.Get("S_607b2e73db", "- 记录里各分组的字节含义尚未证实，需要对照设备物理状态（按键、切换模式、充电）复采比对。"));
        }
        if (bundle.Session.Capabilities.Any(c => c.Status is CapabilityStatus.Unavailable or CapabilityStatus.NeedsExtraTool))
        {
            sb.AppendLine(UiText.Get("S_de3d26c70c", "- 预检里有不可用/需额外工具项，USB 总线级证据本次未采集。"));
        }

        sb.AppendLine();
        sb.AppendLine(UiText.Get("S_fa80d6a847", "### 诊断包"));
        sb.AppendLine(string.IsNullOrEmpty(packagePath)
            ? UiText.Get("S_50f22cae43", "- 本地路径：<请在此处填写你保存的 vigilwatt-capture.zip 路径>")
            : UiText.Format("S_4c70ede8c2", "- 本地路径：{0}", packagePath));

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string OrUnknown(string value) => string.IsNullOrWhiteSpace(value) ? UiText.Get("S_cd427acfab", "（未提供）") : value;
}
