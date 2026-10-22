using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Agente;

// Só funciona na sessão do usuário: a memória compartilhada do RivaTuner é desta sessão.
static class Rtss
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)] static extern IntPtr OpenFileMappingA(uint acc, bool inh, string name);
    [DllImport("kernel32", SetLastError = true)] static extern IntPtr MapViewOfFile(IntPtr h, uint acc, uint hi, uint lo, UIntPtr n);
    [DllImport("kernel32")] static extern bool UnmapViewOfFile(IntPtr p);
    [DllImport("kernel32")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryW(string f);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr m, string n);

    // O RivaTuner avulso só vale enquanto a cópia do SetupHub não existir.
    public static readonly string Pasta = File.Exists(Path.Combine(Caminhos.Kits, "rtss", "RTSS.exe")) ? Path.Combine(Caminhos.Kits, "rtss") : @"C:\Program Files (x86)\RivaTuner Statistics Server";
    const string Dono = Produto.Nome;
    public static bool Instalado => File.Exists(Path.Combine(Pasta, "RTSS.exe"));
    public static bool Rodando => System.Diagnostics.Process.GetProcessesByName("RTSS").Length > 0;

    // Pela tarefa da instalação: o RivaTuner pede administrador e assim não pergunta de novo.
    public static void Abrir()
    {
        if (!Instalado || Rodando) return;
        try
        {
            WritePrivateProfileStringW("Settings", "StartMinimized", "1", Path.Combine(Pasta, "Profiles", "Config"));
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("schtasks.exe", $"/run /tn \"{Produto.TarefaRivaTuner}\"") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { }
    }
    static readonly Encoding Latin1 = Encoding.Latin1;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FnFlags(uint e, uint x);

    // Chave "Show On-Screen Display" do RivaTuner (bit 1 das flags dele): desligada, nada aparece em jogo nenhum.
    public static void Mostrar(bool sim)
    {
        if (!Instalado) return;
        if (ganchos == IntPtr.Zero) ganchos = LoadLibraryW(Path.Combine(Pasta, "RTSSHooks64.dll"));
        var p = ganchos == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(ganchos, "SetFlags");
        if (p == IntPtr.Zero) return;
        Marshal.GetDelegateForFunctionPointer<FnFlags>(p)(0xFFFFFFFE, sim ? 1u : 0u);
    }

    static IntPtr Abrir(out IntPtr h)
    {
        h = OpenFileMappingA(0xF001F, false, "RTSSSharedMemoryV2");
        if (h == IntPtr.Zero) return IntPtr.Zero;
        var v = MapViewOfFile(h, 0xF001F, 0, 0, UIntPtr.Zero);
        if (v == IntPtr.Zero) { CloseHandle(h); h = IntPtr.Zero; }
        return v;
    }

    static string Ler(IntPtr p, int off, int max)
    {
        var b = new byte[max];
        Marshal.Copy(p + off, b, 0, max);
        int z = Array.IndexOf(b, (byte)0);
        return Latin1.GetString(b, 0, z < 0 ? max : z);
    }

    static void Escrever(IntPtr p, int off, int max, string s)
    {
        var b = new byte[max];
        var t = Latin1.GetBytes(s);
        Array.Copy(t, b, Math.Min(t.Length, max - 1));
        Marshal.Copy(b, 0, p + off, max);
    }

    // Texto vazio apaga; falso se o RivaTuner não estiver aberto.
    public static bool Texto(string texto)
    {
        var v = Abrir(out var h);
        if (v == IntPtr.Zero) return false;
        try
        {
            int tam = Marshal.ReadInt32(v, 20), inicio = Marshal.ReadInt32(v, 24), n = Marshal.ReadInt32(v, 28);
            int livre = -1, meu = -1;
            for (int i = 1; i < n; i++)
            {
                int e = inicio + i * tam;
                var dono = Ler(v, e + 256, 256);
                if (dono == Dono) { meu = i; break; }
                if (dono.Length == 0 && livre < 0) livre = i;
            }
            int slot = meu >= 0 ? meu : livre;
            if (slot < 0) return false;
            int en = inicio + slot * tam;
            if (texto.Length == 0)
            {
                if (meu < 0) return true;
                Escrever(v, en, 256, ""); Escrever(v, en + 512, 4096, ""); Escrever(v, en + 256, 256, "");
            }
            else
            {
                // O RivaTuner desenha o campo curto e o longo em sequência: só o longo, senão o texto sai duas vezes.
                Escrever(v, en + 256, 256, Dono);
                Escrever(v, en, 256, "");
                Escrever(v, en + 512, 4096, texto);
            }
            Marshal.WriteInt32(v, 32, Marshal.ReadInt32(v, 32) + 1);
            return true;
        }
        finally { UnmapViewOfFile(v); CloseHandle(h); }
    }

    // Do jogo que o RivaTuner atualizou por último.
    public static (double? fps, double? ms) Quadros()
    {
        var v = Abrir(out var h);
        if (v == IntPtr.Zero) return (null, null);
        try
        {
            int tam = Marshal.ReadInt32(v, 8), inicio = Marshal.ReadInt32(v, 12), n = Marshal.ReadInt32(v, 16);
            uint melhorT = 0; double? fps = null, ms = null;
            for (int i = 0; i < n; i++)
            {
                int e = inicio + i * tam;
                if (Marshal.ReadInt32(v, e) == 0) continue;
                uint t0 = (uint)Marshal.ReadInt32(v, e + 268), t1 = (uint)Marshal.ReadInt32(v, e + 272), quadros = (uint)Marshal.ReadInt32(v, e + 276), tq = (uint)Marshal.ReadInt32(v, e + 280);
                if (t1 <= t0 || t1 < melhorT) continue;
                if ((uint)Environment.TickCount - t1 > 3000) continue;
                melhorT = t1;
                fps = 1000.0 * quadros / (t1 - t0);
                ms = tq > 0 ? tq / 1000.0 : null;
            }
            return (fps, ms);
        }
        catch { return (null, null); }
        finally { UnmapViewOfFile(v); CloseHandle(h); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] delegate void FnPerfil(string p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] delegate bool FnPropriedade(string n, byte[] d, uint s);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FnVazio();
    static IntPtr ganchos;

    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool WritePrivateProfileStringW(string secao, string chave, string valor, string arquivo);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern int GetPrivateProfileStringW(string secao, string chave, string padrao, StringBuilder valor, int tamanho, string arquivo);

    public static int Cor(string hex) => Convert.ToInt32(hex.TrimStart('#'), 16);

    // A fonte é texto e não passa pela função de propriedades: vai direto no arquivo do perfil.
    public static List<string> Perfil(Dictionary<string, int> valores, string? fonte = null)
    {
        var falhas = new List<string>();
        if (!Instalado) { if (valores.Count > 0) falhas.Add("RivaTuner não instalado"); return falhas; }
        bool fonteMudou = false;
        if (fonte is not null)
        {
            var global = Path.Combine(Pasta, "Profiles", "Global");
            var atual = new StringBuilder(64);
            GetPrivateProfileStringW("Font", "Face", "", atual, atual.Capacity, global);
            if (atual.ToString() != fonte)
            {
                if (WritePrivateProfileStringW("Font", "Face", fonte, global)) fonteMudou = true;
                else falhas.Add("fonte da sobreposição: sem permissão de gravação na pasta de perfis do RivaTuner");
            }
        }
        if (ganchos == IntPtr.Zero) ganchos = LoadLibraryW(Path.Combine(Pasta, "RTSSHooks64.dll"));
        if (ganchos == IntPtr.Zero) { falhas.Add("biblioteca do RivaTuner não carregou"); return falhas; }
        var carregar = Marshal.GetDelegateForFunctionPointer<FnPerfil>(GetProcAddress(ganchos, "LoadProfile"));
        var salvar = Marshal.GetDelegateForFunctionPointer<FnPerfil>(GetProcAddress(ganchos, "SaveProfile"));
        var ler = Marshal.GetDelegateForFunctionPointer<FnPropriedade>(GetProcAddress(ganchos, "GetProfileProperty"));
        var gravar = Marshal.GetDelegateForFunctionPointer<FnPropriedade>(GetProcAddress(ganchos, "SetProfileProperty"));
        var atualizar = Marshal.GetDelegateForFunctionPointer<FnVazio>(GetProcAddress(ganchos, "UpdateProfiles"));
        carregar("");
        bool mudou = fonteMudou;
        var d = new byte[4];
        foreach (var (k, v) in valores)
        {
            if (ler(k, d, 4) && BitConverter.ToInt32(d, 0) == v) continue;
            if (!gravar(k, BitConverter.GetBytes(v), 4)) { falhas.Add($"RivaTuner recusou {k}"); continue; }
            mudou = true;
        }
        if (mudou)
        {
            salvar("");
            atualizar();
            carregar("");
            foreach (var (k, v) in valores)
                if (!ler(k, d, 4) || BitConverter.ToInt32(d, 0) != v) falhas.Add($"{k} não confirmado pelo RivaTuner: sem permissão de gravação na pasta de perfis");
        }
        return falhas;
    }

    // Agrupado por peça como no Afterburner; em coluna, um grupo por linha.
    public static string Montar(Sobreposicao s, Estado e, double? fps, double? ms)
    {
        var br = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        bool Quer(string k) => s.Leituras.Contains(k);
        string? N(string k, double? v, string formato, string antes, string depois) => Quer(k) && v is double d ? antes + d.ToString(formato, br) + depois : null;
        var grupos = new List<string>();
        void Grupo(string? rotulo, params string?[] partes)
        {
            var p = partes.Where(x => x is not null).ToList();
            if (p.Count > 0) grupos.Add((rotulo is null ? "" : rotulo + "  ") + string.Join("  ", p));
        }
        if (Quer("hora")) Grupo(null, DateTime.Now.ToString("HH:mm"));
        Grupo(null, N("fps", fps, "0", "", " fps"), N("frametime", ms, "0.0", "", " ms"));
        var fanCpu = e.Ventoinhas.FirstOrDefault(v => v.Id == "cpu")?.Porcentagem;
        Grupo("CPU", N("cpu.temp", e.Cpu.TempMaisQuente, "0", "", "°C"), N("cpu.clock", e.Cpu.ClockMax, "0", "", " MHz"), N("cpu.uso", e.Cpu.Uso, "0", "uso ", "%"),
            N("cpu.consumo", e.Cpu.Consumo, "0", "", " W"), N("cpu.ventoinha", fanCpu, "0", "fan ", "%"));
        Grupo("GPU", N("gpu.temp", e.Gpu.TempMaisQuente, "0", "", "°C"), N("gpu.quente", e.Gpu.TempQuente, "0", "quente ", "°C"), N("gpu.clock", e.Gpu.Clock, "0", "", " MHz"),
            N("gpu.uso", e.Gpu.Uso, "0", "uso ", "%"), N("gpu.consumo", e.Gpu.ConsumoTotal ?? e.Gpu.Consumo, "0", "", " W"), N("gpu.vram", e.Gpu.VramUsada, "0.0", "VRAM ", " GB"),
            N("gpu.ventoinha", e.Gpu.FanPorcentagem, "0", "fan ", "%"));
        Grupo("RAM", N("memoria.uso", e.Memoria.UsadaGb, "0.0", "", " GB"));
        return string.Join(s.Layout == "coluna" ? "\n" : "     ", grupos);
    }
}
