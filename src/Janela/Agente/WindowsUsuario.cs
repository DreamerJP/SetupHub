using System.IO;
using System.Runtime.InteropServices;
using SetupHub.Nucleo;
using Microsoft.Win32;

namespace SetupHub.Janela.Agente;

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

    // O valor anterior fica em arquivo: se a janela cair durante o jogo, a próxima abertura ainda sabe o que devolver.
    static string ArquivoNotificacoes => Path.Combine(Caminhos.Usuario, "notificacoes.txt");
    static readonly object travaNotificacoes = new();

    public static void NaoPerturbe(bool jogo)
    {
        const string chave = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
        lock (travaNotificacoes)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(chave);
                if (jogo)
                {
                    if (!File.Exists(ArquivoNotificacoes))
                    {
                        Directory.CreateDirectory(Caminhos.Usuario);
                        File.WriteAllText(ArquivoNotificacoes, (k.GetValue("ToastEnabled") as int? ?? 1).ToString());
                    }
                    k.SetValue("ToastEnabled", 0, RegistryValueKind.DWord);
                }
                else if (File.Exists(ArquivoNotificacoes))
                {
                    k.SetValue("ToastEnabled", int.TryParse(File.ReadAllText(ArquivoNotificacoes), out var antes) ? antes : 1, RegistryValueKind.DWord);
                    File.Delete(ArquivoNotificacoes);
                }
            }
            catch { }
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct UltimaEntrada { public uint Tamanho; public uint Tempo; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref UltimaEntrada u);

    public static double SegundosOcioso()
    {
        var u = new UltimaEntrada { Tamanho = (uint)Marshal.SizeOf<UltimaEntrada>() };
        return GetLastInputInfo(ref u) ? ((uint)Environment.TickCount - u.Tempo) / 1000.0 : 0;
    }
}
