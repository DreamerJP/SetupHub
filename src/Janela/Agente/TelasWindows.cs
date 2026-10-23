using System.Runtime.InteropServices;

namespace SetupHub.Janela.Agente;

// O ADLX não muda a taxa de atualização: vai pelo Windows.
static class TelasWindows
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevicesW(string? dev, uint i, ref DisplayDevice d, uint f);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string dev, int modo, ref DevMode m);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string dev, ref DevMode m, IntPtr h, uint f, IntPtr p);

    public static List<(string dispositivo, string monitor, int w, int h, int hz, List<double> taxas)> Listar()
    {
        var r = new List<(string, string, int, int, int, List<double>)>();
        for (uint i = 0; ; i++)
        {
            var d = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevicesW(null, i, ref d, 0)) break;
            if ((d.StateFlags & 1) == 0) continue;
            var mon = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            string monitor = EnumDisplayDevicesW(d.DeviceName, 0, ref mon, 0) ? mon.DeviceString : "";
            var atual = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            if (!EnumDisplaySettingsW(d.DeviceName, -1, ref atual)) continue;
            var taxas = new SortedSet<double>();
            var m = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            for (int k = 0; EnumDisplaySettingsW(d.DeviceName, k, ref m); k++)
                if (m.dmPelsWidth == atual.dmPelsWidth && m.dmPelsHeight == atual.dmPelsHeight && m.dmBitsPerPel == atual.dmBitsPerPel) taxas.Add(m.dmDisplayFrequency);
            r.Add((d.DeviceName, monitor, atual.dmPelsWidth, atual.dmPelsHeight, atual.dmDisplayFrequency, taxas.Reverse().ToList()));
        }
        return r;
    }

    public static string? Taxa(string dispositivo, double hz)
    {
        var m = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
        if (!EnumDisplaySettingsW(dispositivo, -1, ref m)) return "tela não encontrada";
        if (Math.Abs(m.dmDisplayFrequency - hz) < 0.6) return null;
        m.dmDisplayFrequency = (int)Math.Round(hz);
        m.dmFields = 0x400000;
        int r = ChangeDisplaySettingsExW(dispositivo, ref m, IntPtr.Zero, 1, IntPtr.Zero);
        if (r != 0) return $"o Windows recusou {hz:0} Hz (código {r})";
        var lido = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
        EnumDisplaySettingsW(dispositivo, -1, ref lido);
        return Math.Abs(lido.dmDisplayFrequency - hz) < 0.6 ? null : "a taxa não conferiu depois de gravar";
    }
}
