using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SetupHub.Servico.Hardware;

static class Hid
{
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int n);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int n);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [StructLayout(LayoutKind.Sequential)]
    struct Caps
    {
        public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] r;
        public ushort a, b, c, d, e, f, g, h, i, j;
    }
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out Caps c);
    [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
    [StructLayout(LayoutKind.Sequential)] struct Did { public int cb; public Guid g; public int f; public IntPtr r; }
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref Did did);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr s, ref Did did, IntPtr det, int sz, out int req, IntPtr dev);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFileW(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WriteFile(SafeFileHandle h, byte[] b, uint n, out uint gravados, IntPtr sobreposto);
    [StructLayout(LayoutKind.Sequential)]
    struct Sobreposto
    {
        public IntPtr Interno, InternoAlto;
        public uint Deslocamento, DeslocamentoAlto;
        public IntPtr Evento;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ReadFile(SafeFileHandle h, IntPtr b, uint n, out uint lidos, IntPtr sobreposto);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr sobreposto, out uint lidos, [MarshalAs(UnmanagedType.Bool)] bool esperar);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CancelIoEx(SafeFileHandle h, IntPtr sobreposto);

    public static List<string> Caminhos(string filtro)
    {
        var l = new List<string>();
        HidD_GetHidGuid(out var g);
        IntPtr s = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        try
        {
            for (int i = 0; ; i++)
            {
                var did = new Did { cb = Marshal.SizeOf<Did>() };
                if (!SetupDiEnumDeviceInterfaces(s, IntPtr.Zero, ref g, i, ref did)) break;
                SetupDiGetDeviceInterfaceDetailW(s, ref did, IntPtr.Zero, 0, out int req, IntPtr.Zero);
                IntPtr b = Marshal.AllocHGlobal(req);
                try
                {
                    Marshal.WriteInt32(b, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(s, ref did, b, req, out _, IntPtr.Zero))
                    {
                        var p = Marshal.PtrToStringUni(b + 4) ?? "";
                        if (p.Contains(filtro, StringComparison.OrdinalIgnoreCase)) l.Add(p);
                    }
                }
                finally { Marshal.FreeHGlobal(b); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(s); }
        return l;
    }

    public static bool Escrever(SafeFileHandle h, byte[] b) =>
        WriteFile(h, b, (uint)b.Length, out var gravados, IntPtr.Zero) && gravados == (uint)b.Length;

    public static bool LerEntrada(SafeFileHandle h, byte[] b, int prazo)
    {
        using var evento = new EventWaitHandle(false, EventResetMode.ManualReset);
        var pino = GCHandle.Alloc(b, GCHandleType.Pinned);
        var sobreposto = Marshal.AllocHGlobal(Marshal.SizeOf<Sobreposto>());
        try
        {
            Marshal.StructureToPtr(new Sobreposto { Evento = evento.SafeWaitHandle.DangerousGetHandle() }, sobreposto, false);
            if (ReadFile(h, pino.AddrOfPinnedObject(), (uint)b.Length, out var lidos, sobreposto)) return lidos == (uint)b.Length;
            if (Marshal.GetLastWin32Error() != 997) return false;
            if (!evento.WaitOne(prazo))
            {
                CancelIoEx(h, sobreposto);
                GetOverlappedResult(h, sobreposto, out _, true);
                return false;
            }
            return GetOverlappedResult(h, sobreposto, out lidos, false) && lidos == (uint)b.Length;
        }
        finally
        {
            Marshal.FreeHGlobal(sobreposto);
            pino.Free();
        }
    }

    public static (SafeFileHandle? h, SafeFileHandle? entrada, int tamanho, int tamanhoEntrada) Abrir(string filtro, int tamanhoMinimo)
    {
        foreach (var c in Caminhos(filtro))
        {
            var h = CreateFileW(c, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); continue; }
            if (!HidD_GetPreparsedData(h, out var pp)) { h.Dispose(); continue; }
            HidP_GetCaps(pp, out var caps);
            HidD_FreePreparsedData(pp);
            if (caps.FeatLen >= tamanhoMinimo)
            {
                var entrada = CreateFileW(c, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if (entrada.IsInvalid) { entrada.Dispose(); return (h, null, caps.FeatLen, caps.InLen); }
                return (h, entrada, caps.FeatLen, caps.InLen);
            }
            h.Dispose();
        }
        return (null, null, 0, 0);
    }
}
