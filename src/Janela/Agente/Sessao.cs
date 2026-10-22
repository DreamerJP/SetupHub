using System.Runtime.InteropServices;
using System.Windows.Interop;
using SetupHub.Nucleo;
using Microsoft.Win32;

namespace SetupHub.Janela.Agente;

sealed class Som : IDisposable
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int fluxo, int estado, out IntPtr dispositivos);
        int GetDefaultAudioEndpoint(int fluxo, int papel, out IMMDevice dispositivo);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int ctx, IntPtr parametros, [MarshalAs(UnmanagedType.IUnknown)] out object interfaceAtivada);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        int GetPeakValue(out float pico);
    }

    IAudioMeterInformation? medidor;
    long ultimaTroca;

    public float Nivel()
    {
        try
        {
            if (medidor is null || Environment.TickCount64 - ultimaTroca > 10000)
            {
                ultimaTroca = Environment.TickCount64;
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                en.GetDefaultAudioEndpoint(0, 1, out var disp);
                var iid = typeof(IAudioMeterInformation).GUID;
                disp.Activate(ref iid, 23, IntPtr.Zero, out var m);
                medidor = (IAudioMeterInformation)m;
            }
            medidor.GetPeakValue(out var p);
            return p;
        }
        catch { medidor = null; return 0; }
    }

    public void Dispose() { medidor = null; }
}

sealed class Atalhos : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    readonly HwndSource janela;
    readonly Dictionary<int, Action> acoes = new();
    readonly Dictionary<string, int> registrados = new();
    int proximo = 1;
    public List<string> Recusados { get; } = new();

    public Atalhos()
    {
        janela = new HwndSource(new HwndSourceParameters(Produto.Nome + "Atalhos") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
        janela.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool tratado) =>
        {
            if (msg == 0x0312 && acoes.TryGetValue(w.ToInt32(), out var a)) { a(); tratado = true; }
            return IntPtr.Zero;
        });
    }

    public void Definir(Dictionary<string, Action> novos)
    {
        foreach (var id in registrados.Values) UnregisterHotKey(janela.Handle, id);
        registrados.Clear(); acoes.Clear(); Recusados.Clear();
        foreach (var (texto, acao) in novos)
        {
            if (!Interpretar(texto, out var mod, out var vk)) continue;
            int id = proximo++;
            if (RegisterHotKey(janela.Handle, id, mod | 0x4000, vk)) { registrados[texto] = id; acoes[id] = acao; }
            else Recusados.Add(texto);
        }
    }

    static bool Interpretar(string texto, out uint mod, out uint vk)
    {
        mod = 0; vk = 0;
        var partes = texto.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length < 2) return false;
        foreach (var p in partes[..^1])
            mod |= p switch { "Alt" => 1u, "Ctrl" => 2u, "Shift" => 4u, "Win" => 8u, _ => 0u };
        var t = partes[^1].ToUpperInvariant();
        if (t.Length == 1 && char.IsLetterOrDigit(t[0])) vk = t[0];
        else if (t.StartsWith('F') && int.TryParse(t[1..], out var f) && f is >= 1 and <= 12) vk = (uint)(0x70 + f - 1);
        return vk != 0;
    }

    public void Dispose()
    {
        foreach (var id in registrados.Values) UnregisterHotKey(janela.Handle, id);
        janela.Dispose();
    }
}

static class WindowsUsuario
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfoW(uint acao, uint p, int[] v, uint f);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfoW(uint acao, uint p, IntPtr v, uint f);

    static void Dword(string chave, string nome, int valor, List<string> falhas, string rotulo)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(chave);
            if (k.GetValue(nome) is int atual && atual == valor) return;
            k.SetValue(nome, valor, RegistryValueKind.DWord);
            if (k.GetValue(nome) is not int lido || lido != valor) falhas.Add(rotulo);
        }
        catch (Exception e) { falhas.Add($"{rotulo}: {e.Message}"); }
    }

    public static List<string> Aplicar(ConfigWindows w)
    {
        var f = new List<string>();
        if (w.ModoJogo is bool mj) { Dword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", mj ? 1 : 0, f, "modo de jogo"); Dword(@"Software\Microsoft\GameBar", "AllowAutoGameMode", mj ? 1 : 0, f, "modo de jogo"); }
        if (w.GravacaoFundo is bool gf) { Dword(@"System\GameConfigStore", "GameDVR_Enabled", gf ? 1 : 0, f, "gravação em segundo plano"); Dword(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", gf ? 1 : 0, f, "gravação em segundo plano"); }
        if (w.Transparencia is bool tr) Dword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", tr ? 1 : 0, f, "transparência");
        if (w.OtimizacaoJanela is not null || w.VrrJanela is not null)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
                var atual = k.GetValue("DirectXUserGlobalSettings")?.ToString() ?? "";
                var partes = atual.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=')).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
                if (w.OtimizacaoJanela is bool oj) partes["SwapEffectUpgradeEnable"] = oj ? "1" : "0";
                if (w.VrrJanela is bool vj) partes["VRROptimizeEnable"] = vj ? "1" : "0";
                var novo = string.Join("", partes.Select(kv => $"{kv.Key}={kv.Value};"));
                if (novo != atual) k.SetValue("DirectXUserGlobalSettings", novo, RegistryValueKind.String);
            }
            catch (Exception e) { f.Add("otimização em janela: " + e.Message); }
        }
        if (w.PrecisaoPonteiro is bool pp)
        {
            var v = pp ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 };
            if (!SystemParametersInfoW(0x0004, 0, v, 3)) f.Add("precisão do ponteiro");
        }
        return f;
    }

    public static ConfigWindows Ler()
    {
        int? D(string chave, string nome) { try { using var k = Registry.CurrentUser.OpenSubKey(chave); return k?.GetValue(nome) as int?; } catch { return null; } }
        var w = new ConfigWindows
        {
            ModoJogo = D(@"Software\Microsoft\GameBar", "AutoGameModeEnabled") is int mj ? mj != 0 : true,
            GravacaoFundo = D(@"System\GameConfigStore", "GameDVR_Enabled") is int gf ? gf != 0 : null,
            Transparencia = D(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency") is int tr ? tr != 0 : null,
        };
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            var s = k?.GetValue("DirectXUserGlobalSettings")?.ToString() ?? "";
            // Sem escolha gravada vale o padrão do Windows, que liga a otimização a partir do 24H2.
            w.OtimizacaoJanela = s.Contains("SwapEffectUpgradeEnable=") ? s.Contains("SwapEffectUpgradeEnable=1") : Environment.OSVersion.Version.Build >= 26100;
            w.VrrJanela = s.Contains("VRROptimizeEnable=1");
        }
        catch { }
        try
        {
            var v = new int[3];
            var h = GCHandle.Alloc(v, GCHandleType.Pinned);
            try { if (SystemParametersInfoW(0x0003, 0, h.AddrOfPinnedObject(), 0)) w.PrecisaoPonteiro = v[2] != 0; } finally { h.Free(); }
        }
        catch { }
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            w.AgendamentoGpu = k?.GetValue("HwSchMode") is int hs ? hs == 2 : AgendamentoEmUso();
        }
        catch { }
        return w;
    }

    [StructLayout(LayoutKind.Sequential)] struct InfoAdaptador { public uint Handle; public uint LuidBaixo; public int LuidAlto; public uint Fontes; public int Preciso; }
    [StructLayout(LayoutKind.Sequential)] struct ListaAdaptadores { public uint Quantos; public IntPtr Itens; }
    [StructLayout(LayoutKind.Sequential)] struct ConsultaAdaptador { public uint Handle; public int Tipo; public IntPtr Dados; public uint Tamanho; }
    [StructLayout(LayoutKind.Sequential)] struct FecharAdaptador { public uint Handle; }
    [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref ListaAdaptadores l);
    [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref ConsultaAdaptador c);
    [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref FecharAdaptador f);

    // Sem escolha gravada no registro vale o padrão do Windows, que só o driver informa (consulta 70: capacidades do WDDM 2.7).
    static bool? AgendamentoEmUso()
    {
        var l = new ListaAdaptadores();
        if (D3DKMTEnumAdapters2(ref l) != 0 || l.Quantos == 0) return null;
        int tam = Marshal.SizeOf<InfoAdaptador>();
        var buf = Marshal.AllocHGlobal(tam * (int)l.Quantos);
        try
        {
            l.Itens = buf;
            if (D3DKMTEnumAdapters2(ref l) != 0) return null;
            bool? r = null;
            for (int i = 0; i < l.Quantos; i++)
            {
                var a = Marshal.PtrToStructure<InfoAdaptador>(buf + i * tam);
                var dado = Marshal.AllocHGlobal(4);
                try
                {
                    Marshal.WriteInt32(dado, 0);
                    var c = new ConsultaAdaptador { Handle = a.Handle, Tipo = 70, Dados = dado, Tamanho = 4 };
                    int v = D3DKMTQueryAdapterInfo(ref c) == 0 ? Marshal.ReadInt32(dado) : 0;
                    if ((v & 1) != 0) r = r == true || (v & 2) != 0;
                }
                finally { Marshal.FreeHGlobal(dado); var f = new FecharAdaptador { Handle = a.Handle }; D3DKMTCloseAdapter(ref f); }
            }
            return r;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static int? notificacoesAntes;
    public static void NaoPerturbe(bool jogo)
    {
        const string chave = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(chave);
            if (jogo)
            {
                notificacoesAntes ??= k.GetValue("ToastEnabled") as int? ?? 1;
                k.SetValue("ToastEnabled", 0, RegistryValueKind.DWord);
            }
            else if (notificacoesAntes is int antes)
            {
                k.SetValue("ToastEnabled", antes, RegistryValueKind.DWord);
                notificacoesAntes = null;
            }
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)] struct UltimaEntrada { public uint Tamanho; public uint Tempo; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref UltimaEntrada u);

    public static double SegundosOcioso()
    {
        var u = new UltimaEntrada { Tamanho = (uint)Marshal.SizeOf<UltimaEntrada>() };
        return GetLastInputInfo(ref u) ? ((uint)Environment.TickCount - u.Tempo) / 1000.0 : 0;
    }
}

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
