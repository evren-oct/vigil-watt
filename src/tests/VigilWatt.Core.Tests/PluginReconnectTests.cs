using VigilWatt.Core.Devices;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins;
using VigilWatt.Core.Utils;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 接口移除、物理身份重连和重复接口释放的回归测试，不访问硬件。
/// 2026/10/19 21:32:28 kaniu
/// </summary>
public class PluginReconnectTests : IDisposable
{
    private readonly LogLevel _priorLevel = Log.MinLevel;
    public PluginReconnectTests() => Log.MinLevel = LogLevel.Off;
    public void Dispose() => Log.MinLevel = _priorLevel;

    private static HidDeviceInfo Info(string path, ushort unit = 1)
        => new() { DevicePath = path, Product = "Same controller model", Version = unit };

    [Fact]
    public async Task RemovedInterfacePublishesOfflineSnapshotAndReleasesResources()
    {
        using var plugin = new FakePlugin();
        var claims = new DeviceClaimRegistry();
        var initial = Assert.Single(await plugin.AttachAsync([Info("old")], claims));
        var stored = initial.Clone();
        var removed = Assert.Single(await plugin.AttachAsync([], claims));
        DeviceStatusMerger.Apply(stored, removed);
        Assert.False(stored.IsOnline);
        Assert.Equal(60, stored.BatteryPercent);
        Assert.False(claims.IsClaimedBy("old", plugin.Name));
        Assert.True(plugin.Streams[0].SafeFileHandle.IsClosed);
        Assert.Empty(await plugin.AttachAsync([], claims));
    }

    [Fact]
    public async Task ReconnectInSameScanKeepsOnlyTheNewOnlineStatus()
    {
        using var plugin = new FakePlugin();
        var claims = new DeviceClaimRegistry();
        var initial = Assert.Single(await plugin.AttachAsync([Info("old")], claims));
        var replacement = Assert.Single(await plugin.AttachAsync([Info("new")], claims));
        Assert.Equal(initial.Id, replacement.Id);
        Assert.True(replacement.IsOnline);
        Assert.False(claims.IsClaimedBy("old", plugin.Name));
        Assert.True(claims.IsClaimedBy("new", plugin.Name));
        Assert.True(plugin.Streams[0].SafeFileHandle.IsClosed);
    }

    [Fact]
    public async Task ReconnectAfterGapUpdatesOneManagerSnapshot()
    {
        using var plugin = new FakePlugin();
        var claims = new DeviceClaimRegistry();
        var manager = new Dictionary<string, DeviceStatus>();
        foreach (var scan in new[] { new[] { Info("old") }, [], new[] { Info("new") } })
            foreach (var update in await plugin.AttachAsync(scan, claims))
            {
                if (!manager.TryGetValue(update.Id, out var stored)) manager[update.Id] = update.Clone();
                else DeviceStatusMerger.Apply(stored, update);
            }
        Assert.True(Assert.Single(manager.Values).IsOnline);
    }

    [Fact]
    public async Task SameReceiverReprobeReturnsOnlyOnlineReplacement()
    {
        using var plugin = new FakePlugin();
        var claims = new DeviceClaimRegistry();
        var initial = Assert.Single(await plugin.AttachAsync([Info("same")], claims));
        plugin.IdentityChanged = true;
        plugin.Poll();
        plugin.Poll();
        var replacement = Assert.Single(await plugin.AttachAsync([Info("same")], claims));
        Assert.Equal(initial.Id, replacement.Id);
        Assert.True(replacement.IsOnline);
        Assert.True(claims.IsClaimedBy("same", plugin.Name));
        Assert.Equal(2, plugin.Streams.Count);
        Assert.True(plugin.Streams[0].SafeFileHandle.IsClosed);
    }

    [Fact]
    public async Task EqualNamesWithDifferentPhysicalIdsRemainSeparate()
    {
        using var plugin = new FakePlugin();
        var devices = await plugin.AttachAsync([Info("one", 1), Info("two", 2)], new());
        Assert.Equal(2, devices.Count);
        Assert.Equal(devices[0].Name, devices[1].Name);
        Assert.NotEqual(devices[0].Id, devices[1].Id);
    }

    [Fact]
    public async Task MultipleInterfacesOfSameUnitReleaseDuplicateProbeHandle()
    {
        using var plugin = new FakePlugin();
        var claims = new DeviceClaimRegistry();
        Assert.Single(await plugin.AttachAsync([Info("one"), Info("alias")], claims));
        Assert.True(claims.IsClaimedBy("one", plugin.Name));
        Assert.False(claims.IsClaimedBy("alias", plugin.Name));
        Assert.True(plugin.Streams[1].SafeFileHandle.IsClosed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReplacementDoesNotLeaveOldStatusOnlineOrLeakNewHandle(bool identityFailure)
    {
        using var plugin = new FakePlugin();
        var claims = new DeviceClaimRegistry();
        await plugin.AttachAsync([Info("old")], claims);
        plugin.FailProbe = !identityFailure;
        plugin.FailIdentity = identityFailure;
        Assert.False(Assert.Single(await plugin.AttachAsync([Info("new")], claims)).IsOnline);
        Assert.False(claims.IsClaimedBy("old", plugin.Name));
        Assert.False(claims.IsClaimedBy("new", plugin.Name));
        foreach (var stream in plugin.Streams)
            Assert.True(stream.SafeFileHandle.IsClosed);
    }

    [Fact]
    public async Task PathCaseChangeDoesNotReopenOrRemoveSameWindowsInterface()
    {
        using var plugin = new FakePlugin();
        var claims = new DeviceClaimRegistry();
        var first = Assert.Single(await plugin.AttachAsync([Info("HID#A")], claims));
        var second = Assert.Single(await plugin.AttachAsync([Info("hid#a")], claims));
        Assert.Same(first, second);
        Assert.Single(plugin.Streams);
        Assert.False(claims.TryClaim("hid#a", "Other"));
        Assert.False(Assert.Single(await plugin.AttachAsync([], claims)).IsOnline);
        Assert.False(claims.IsClaimedBy("HID#A", plugin.Name));
        Assert.True(claims.TryClaim("hid#a", "Other"));
    }

    [Fact]
    public async Task CaseAliasesInOneScanDoNotOverwriteAndLeakFirstHandle()
    {
        using var plugin = new FakePlugin();
        Assert.Single(await plugin.AttachAsync([Info("HID#A"), Info("hid#a")], new()));
        Assert.Single(plugin.Streams);
    }

    [Fact]
    public async Task DifferentReceiverReusingSamePathCanBeRediscovered()
    {
        using var plugin = new FakePlugin();
        var claims = new DeviceClaimRegistry();
        var original = Assert.Single(await plugin.AttachAsync([Info("same", 1)], claims));
        plugin.IdentityChanged = true;
        plugin.Poll();
        plugin.Poll();
        var result = await plugin.AttachAsync([Info("same", 2)], claims);
        Assert.Single(result, d => d.Id == original.Id && !d.IsOnline);
        Assert.Single(result, d => d.Id != original.Id && d.IsOnline);
        Assert.Equal(2, plugin.Streams.Count);
        Assert.True(plugin.Streams[0].SafeFileHandle.IsClosed);
    }

    private sealed class FakePlugin : BatteryPluginBase, IDisposable
    {
        public override string Name => "Test";
        public override string DisplayName => "Test";
        public override string DisplayNameEn => "TEST";
        public override DeviceKind Kind => DeviceKind.Gamepad;
        protected override bool ProbeWrites => false;
        protected override bool WritesEnabled => false;
        public List<FileStream> Streams { get; } = [];
        public bool FailProbe { get; set; }
        public bool FailIdentity { get; set; }
        public bool IdentityChanged { get; set; }
        protected override bool IsCandidate(HidDeviceInfo info) => true;
        protected override string BuildDeviceId(HidDeviceInfo info)
            => FailIdentity ? throw new InvalidDataException("Injected identity failure") : $"test:unit-{info.Version}";
        protected override HidDeviceHandle OpenDevice(HidDeviceInfo info)
        {
            var stream = new FileStream(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 0, FileOptions.DeleteOnClose);
            Streams.Add(stream);
            return new HidDeviceHandle(info, stream);
        }
        protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
            => FailProbe ? null : ("test", 60, ChargingState.Discharging);
        protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
        {
            if (IdentityChanged) RequestReprobe(dev);
            return null;
        }
        public void Dispose() => Detach();
    }
}
