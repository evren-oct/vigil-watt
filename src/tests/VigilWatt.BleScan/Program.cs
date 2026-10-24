using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

var adapter = await BluetoothAdapter.GetDefaultAsync();
if (adapter == null) { Console.WriteLine("无默认蓝牙适配器"); return; }
Console.WriteLine($"IsLowEnergySupported={adapter.IsLowEnergySupported} IsCentralRoleSupported={adapter.IsCentralRoleSupported}");
var radio = await adapter.GetRadioAsync();
Console.WriteLine($"radio: {radio.Name} state={radio.State}");

var props = new[]
{
    "System.ItemNameDisplay",
    "System.Devices.Aep.IsConnected",
    "System.Devices.Aep.DeviceAddress",
};

// 1) 已配对经典蓝牙设备（全部协议）
var paired = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
Console.WriteLine($"\n已配对经典蓝牙设备: {paired.Count}");
foreach (var d in paired) Console.WriteLine($"  经典: {d.Name}");

var pairedLE = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
Console.WriteLine($"已配对 BLE 设备: {pairedLE.Count}");
foreach (var d in pairedLE) Console.WriteLine($"  LE: {d.Name}");

// 2) AEP 端点全量 + 各自的 IsConnected —— 这是判断"到底连没连"的关键
var aeps = await DeviceInformation.FindAllAsync(
    BluetoothDevice.GetDeviceSelector(), props, DeviceInformationKind.AssociationEndpoint);
Console.WriteLine($"\nAEP（AssociationEndpoint）共 {aeps.Count} 个:");
foreach (var d in aeps)
{
    var conn = d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c)
        ? c?.ToString() ?? "(null)"
        : "(属性缺失)";
    var addr = d.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var a) ? a?.ToString() : null;
    Console.WriteLine($"  Name='{d.Name}'  IsConnected={conn}  Addr={addr}");
    Console.WriteLine($"    id={d.Id}");
}

// 3) 只取"已连接"的 AEP —— 若这里为空，说明系统层面确实一个都没连
var connected = await DeviceInformation.FindAllAsync(
    BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
    props, DeviceInformationKind.AssociationEndpoint);
Console.WriteLine($"\n已连接（GetDeviceSelectorFromConnectionStatus）共 {connected.Count} 个:");
foreach (var d in connected) Console.WriteLine($"  {d.Id}  Name='{d.Name}'");
