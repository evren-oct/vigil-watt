using System.Runtime.InteropServices;
using System.Text;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 选择诊断包的本地保存位置。
/// 2026/10/23 22:40:44 Administrator
/// Win32 保存框兼容当前 Windows App SDK 1.7 与管理员运行，不引入新依赖。
/// </summary>
internal static class DiagnosticSaveDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int Size;
        public IntPtr Owner, Instance;
        public string Filter;
        public IntPtr CustomFilter;
        public int MaxCustomFilter, FilterIndex;
        public IntPtr File;
        public int MaxFile;
        public IntPtr FileTitle;
        public int MaxFileTitle;
        public string? InitialDirectory;
        public string Title;
        public uint Flags;
        public ushort FileOffset, FileExtension;
        public string DefaultExtension;
        public IntPtr CustomData, Hook;
        public string? Template;
        public IntPtr Reserved;
        public uint ReservedValue, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileNameW(ref OpenFileName file);

    [DllImport("comdlg32.dll", ExactSpelling = true)]
    private static extern uint CommDlgExtendedError();

    public static string? Choose(IntPtr owner)
    {
        const int capacity = 32768;
        var buffer = Marshal.AllocHGlobal(capacity * sizeof(char));
        try
        {
            var initialName = Encoding.Unicode.GetBytes($"VigilWatt-diag-{DateTime.Now:yyyyMMdd-HHmmss}.zip\0");
            Marshal.Copy(initialName, 0, buffer, initialName.Length);
            var file = new OpenFileName
            {
                Size = Marshal.SizeOf<OpenFileName>(), Owner = owner,
                Filter = "诊断包 (*.zip)\0*.zip\0\0", FilterIndex = 1,
                File = buffer, MaxFile = capacity, Title = "保存诊断包", DefaultExtension = "zip",
                // 保留当前工作目录，避免相对路径资源因打开保存框而失效。
                Flags = 0x00080000 | 0x00000002 | 0x00000800 | 0x00000008 | 0x02000000,
            };
            if (GetSaveFileNameW(ref file)) return Marshal.PtrToStringUni(buffer);
            uint error = CommDlgExtendedError();
            if (error != 0) throw new InvalidOperationException($"无法打开保存对话框（0x{error:X8}）。");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
