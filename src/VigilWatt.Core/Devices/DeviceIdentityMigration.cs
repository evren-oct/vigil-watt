using System.Collections.Concurrent;
using VigilWatt.Core.Settings;

namespace VigilWatt.Core.Devices;

/// <summary>
/// 功 能： 持久化设备精确别名并保留用户的设备设置。
/// 2026/10/24 14:07:39 Administrator
/// 别名先落盘，历史归并可在失败后重试，见 docs/hardware/logitech.md §1。
/// </summary>
public static class DeviceIdentityMigration
{
    public static bool TryApply(AppSettings settings, string id, IReadOnlyList<string> aliases, Func<bool> persist)
    {
        var sources = aliases.Where(a => a != id).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in settings.DeviceAliases)
            if (sources.Contains(pair.Value)) sources.Add(pair.Key);
        var previous = sources.ToArray();
        if (settings.DeviceAliases.ContainsKey(id)
            || previous.Any(a => settings.DeviceAliases.TryGetValue(a, out var target) && target != id && !sources.Contains(target)))
            throw new InvalidOperationException("设备身份别名冲突，未合并记录。");
        var undo = new Stack<Action>();
        try
        {
            foreach (var alias in previous)
            {
                if (settings.DeviceAliases.TryAdd(alias, id))
                    undo.Push(() => ((ICollection<KeyValuePair<string, string>>)settings.DeviceAliases)
                        .Remove(new(alias, id)));
                else if (settings.DeviceAliases.TryGetValue(alias, out var oldTarget) && oldTarget != id
                    && settings.DeviceAliases.TryUpdate(alias, id, oldTarget))
                    undo.Push(() => settings.DeviceAliases.TryUpdate(alias, oldTarget, id));
                Move(settings.DeviceCustomizations, alias, id, undo);
                Move(settings.BlockedDevices, alias, id, undo);
                Move(settings.DismissedDevices, alias, id, undo);
            }
            if (undo.Count == 0 || persist()) return true;
        }
        catch
        {
            while (undo.TryPop(out var action)) action();
            throw;
        }
        while (undo.TryPop(out var action)) action();
        return false;
    }

    private static void Move<T>(ConcurrentDictionary<string, T> values, string alias, string id, Stack<Action> undo)
        where T : notnull
    {
        if (!values.TryGetValue(alias, out var value)) return;
        if (values.TryAdd(id, value))
            undo.Push(() => ((ICollection<KeyValuePair<string, T>>)values).Remove(new(id, value)));
        if (((ICollection<KeyValuePair<string, T>>)values).Remove(new(alias, value)))
            undo.Push(() => values.TryAdd(alias, value));
    }
}
