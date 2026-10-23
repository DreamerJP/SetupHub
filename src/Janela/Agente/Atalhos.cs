using System.Runtime.InteropServices;
using System.Windows.Interop;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Agente;

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
