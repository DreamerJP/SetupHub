using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using SetupHub.Nucleo;

namespace SetupHub.Janela;

// Avisos chegam na linha de leitura; as telas os recebem na linha da interface.
sealed class Ligacao
{
    Conexao? c;
    public Estado? Estado { get; private set; }
    public Config? Config { get; private set; }
    public Faixas? Faixas { get; private set; }
    public bool Ligada => c?.Aberta == true;
    public event Action<Estado>? AoEstado;
    public event Action<Config>? AoConfig;
    public event Action<Faixas>? AoFaixas;
    public event Action<string, string>? AoAviso;
    // "Sair" por qualquer caminho: a janela fecha sem religar o serviço.
    public event Action? AoSaindo;
    public event Action<bool>? AoLigacao;
    public Func<Mensagem, Task<Mensagem>>? Agente;
    volatile bool parar;

    public void Iniciar() => _ = Task.Run(Laco);

    async Task Laco()
    {
        int tentativas = 0;
        while (!parar)
        {
            var nova = await Conexao.ConectarAsync(1000);
            if (nova is null)
            {
                if (tentativas++ % 10 == 0) IniciarServico();
                await Task.Delay(1000);
                continue;
            }
            tentativas = 0;
            var fim = new TaskCompletionSource();
            nova.AoPedir = m => Agente?.Invoke(m) ?? Task.FromResult(new Mensagem { Ok = false, Erro = "A janela não atende este pedido." });
            nova.AoEvento = Evento;
            nova.AoFechar = () => fim.TrySetResult();
            c = nova;
            _ = nova.IniciarLeitura();
            try
            {
                var r = await nova.PedirAsync("assinar");
                if (r.Ok && Json.Ler<Estado>(r.Dados) is { } e) { Estado = e; AoEstado?.Invoke(e); }
                await Recarregar();
                AoLigacao?.Invoke(true);
                await nova.PedirAsync("agente.registrar");
            }
            catch { }
            await fim.Task;
            c = null;
            AoLigacao?.Invoke(false);
            if (!parar) await Task.Delay(500);
        }
    }

    public async Task Recarregar()
    {
        var x = c;
        if (x is null) return;
        var rc = await x.PedirAsync("config");
        if (rc.Ok && Json.Ler<Config>(rc.Dados) is { } cfg) { Config = cfg; AoConfig?.Invoke(cfg); }
        var rf = await x.PedirAsync("faixas");
        if (rf.Ok && Json.Ler<Faixas>(rf.Dados) is { } f) { Faixas = f; AoFaixas?.Invoke(f); }
    }

    void Evento(Mensagem m)
    {
        switch (m.Comando)
        {
            case "estado":
                if (Json.Ler<Estado>(m.Dados) is { } e) { Estado = e; AoEstado?.Invoke(e); }
                break;
            case "config":
                _ = Recarregar();
                break;
            case "faixas":
                _ = Recarregar();
                break;
            case "saindo":
                AoSaindo?.Invoke();
                break;
            case "aviso":
            case "alerta":
            case "jogo":
            case "tarefa":
            case "atualizacoes":
                var ev = m.Comando == "tarefa" ? null : Json.Ler<EventoTexto>(m.Dados);
                var t = m.Comando == "tarefa" ? (Json.Ler<Tarefa>(m.Dados) is { } tf ? $"{tf.Nome}: {tf.Resultado}" : "")
                    : m.Comando == "jogo" ? $"{ev?.Tipo}:{ev?.Texto}" : ev?.Texto ?? "";
                AoAviso?.Invoke(m.Comando!, t);
                break;
        }
    }

    public async Task<Mensagem> Pedir(string comando, JsonElement? args = null, int ms = 60000)
    {
        var x = c;
        if (x is null) return new Mensagem { Ok = false, Erro = "Serviço do SetupHub desconectado. Reconectando." };
        return await x.PedirAsync(comando, args, ms);
    }

    public Task<Mensagem> Definir(string caminho, string valor) => Pedir("definir", Args(("caminho", caminho), ("valor", valor)));

    public async Task<(int codigo, string texto)> Comando(string linha)
    {
        var saida = new StringWriter();
        var erro = new StringWriter();
        int r = await new Interprete(c, saida, erro).Executar(Interprete.Palavras(linha));
        var txt = (saida.ToString() + erro.ToString()).Trim();
        return (r, txt);
    }

    public static JsonElement Args(params (string k, object? v)[] pares)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var (k, v) in pares)
            {
                if (v is null) continue;
                w.WritePropertyName(k);
                switch (v)
                {
                    case string s: w.WriteStringValue(s); break;
                    case bool b: w.WriteBooleanValue(b); break;
                    case int i: w.WriteNumberValue(i); break;
                    case long l: w.WriteNumberValue(l); break;
                    case double d: w.WriteNumberValue(d); break;
                    case IEnumerable<string> lista: w.WriteStartArray(); foreach (var x in lista) w.WriteStringValue(x); w.WriteEndArray(); break;
                    case JsonElement e: e.WriteTo(w); break;
                    default: w.WriteStringValue(v.ToString()); break;
                }
            }
            w.WriteEndObject();
        }
        return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }

    public void Parar() { parar = true; _ = c?.DisposeAsync(); }

    // A instalação dá à conta do usuário permissão de ligar o serviço.
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr OpenSCManagerW(string? m, string? d, uint a);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr OpenServiceW(IntPtr h, string n, uint a);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool StartServiceW(IntPtr h, int n, IntPtr a);
    [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr h);

    public static bool IniciarServico()
    {
        var scm = OpenSCManagerW(null, null, 0x0001);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var s = OpenServiceW(scm, Produto.Nome, 0x0010 | 0x0004);
            if (s == IntPtr.Zero) return false;
            try { return StartServiceW(s, 0, IntPtr.Zero); } finally { CloseServiceHandle(s); }
        }
        finally { CloseServiceHandle(scm); }
    }
}
