using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SetupHub.Nucleo;

// Os dois lados pedem: o serviço pede à janela o que só roda na sessão do usuário (placa de vídeo, sobreposição, som).
public sealed class Mensagem
{
    public string Tipo { get; set; } = "pedido";
    public long Id { get; set; }
    public string? Comando { get; set; }
    public JsonElement? Args { get; set; }
    public bool Ok { get; set; }
    public string? Erro { get; set; }
    public JsonElement? Dados { get; set; }
}

public sealed class ErroSetupHub : Exception
{
    public ErroSetupHub(string mensagem) : base(mensagem) { }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Mensagem))]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(Estado))]
[JsonSerializable(typeof(EstadoMaquina))]
[JsonSerializable(typeof(Faixas))]
[JsonSerializable(typeof(InfoSistema))]
[JsonSerializable(typeof(Tarefa))]
[JsonSerializable(typeof(List<Atualizacao>))]
[JsonSerializable(typeof(List<OpcaoBios>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(double[]))]
[JsonSerializable(typeof(List<double[]>))]
[JsonSerializable(typeof(SerieHistorico))]
[JsonSerializable(typeof(List<SerieHistorico>))]
[JsonSerializable(typeof(ComandoInfo))]
[JsonSerializable(typeof(List<ComandoInfo>))]
[JsonSerializable(typeof(EventoTexto))]
[JsonSerializable(typeof(ResultadoAplicar))]
[JsonSerializable(typeof(LeituraGpu))]
[JsonSerializable(typeof(Ajustes3D))]
[JsonSerializable(typeof(ConfigPlacaDeVideo))]
[JsonSerializable(typeof(ConfigTelas))]
[JsonSerializable(typeof(VentoinhaGpu))]
[JsonSerializable(typeof(Sobreposicao))]
[JsonSerializable(typeof(PedidoAgente))]
[JsonSerializable(typeof(Perfil))]
[JsonSerializable(typeof(PlanoLuz))]
[JsonSerializable(typeof(EfeitoLuz))]
[JsonSerializable(typeof(ConfigLuz))]
[JsonSerializable(typeof(ConfigProcessador))]
[JsonSerializable(typeof(ConfigVentoinhas))]
[JsonSerializable(typeof(ConfigEnergia))]
[JsonSerializable(typeof(ConfigJogos))]
[JsonSerializable(typeof(ConfigAparencia))]
[JsonSerializable(typeof(AjusteTela))]
[JsonSerializable(typeof(List<AjusteTela>))]
[JsonSerializable(typeof(List<ResultadoCo>))]
[JsonSerializable(typeof(ConfigWindows))]
[JsonSerializable(typeof(List<ProcessoInfo>))]
public partial class SetupHubJson : JsonSerializerContext
{
    static SetupHubJson? arquivo;

    // Recuo e acentos legíveis: é o formato do config.json editado à mão.
    public static SetupHubJson Arquivo => arquivo ??= new SetupHubJson(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    });
}

public sealed class SerieHistorico
{
    public string Sensor { get; set; } = "";
    public long Inicio { get; set; }
    public int Passo { get; set; }
    public double[] Valores { get; set; } = Array.Empty<double>();
    public double[]? Picos { get; set; }
}

public sealed class ComandoInfo
{
    public string Nome { get; set; } = "";
    public string Uso { get; set; } = "";
    public string Descricao { get; set; } = "";
}

public sealed class EventoTexto
{
    public string Tipo { get; set; } = "";
    public string Texto { get; set; } = "";
}

public sealed class ResultadoAplicar
{
    public Dictionary<string, string> Areas { get; set; } = new();
    public List<string> Falhas { get; set; } = new();
}

public sealed class PedidoAgente
{
    public ConfigPlacaDeVideo? PlacaDeVideo { get; set; }
    public VentoinhaGpu? Ventoinha { get; set; }
    public Ajustes3D? Graficos { get; set; }
    public ConfigTelas? Telas { get; set; }
    public Sobreposicao? Sobreposicao { get; set; }
    public int? LimiteQuadros { get; set; }
    public ConfigWindows? Windows { get; set; }
}

public sealed class ProcessoInfo
{
    public string Nome { get; set; } = "";
    public string Caminho { get; set; } = "";
    public string Titulo { get; set; } = "";
}

public static class Json
{
    public static JsonElement Elemento<T>(T valor) => JsonSerializer.SerializeToElement(valor, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)SetupHubJson.Default.GetTypeInfo(typeof(T))!);

    public static T? Ler<T>(JsonElement? e)
    {
        if (e is null || e.Value.ValueKind == JsonValueKind.Null || e.Value.ValueKind == JsonValueKind.Undefined) return default;
        return e.Value.Deserialize((System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)SetupHubJson.Default.GetTypeInfo(typeof(T))!);
    }

    public static string Texto<T>(T valor, bool arquivo = false)
    {
        var ctx = arquivo ? SetupHubJson.Arquivo : SetupHubJson.Default;
        return JsonSerializer.Serialize(valor, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)ctx.GetTypeInfo(typeof(T))!);
    }

    public static T? DeTexto<T>(string texto)
    {
        return JsonSerializer.Deserialize(texto, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)SetupHubJson.Arquivo.GetTypeInfo(typeof(T))!);
    }

    public static T Copia<T>(T valor) where T : class => DeTexto<T>(Texto(valor))!;
}

// Mensagem com tamanho na frente; resposta casada pelo número do pedido.
public sealed class Conexao : IAsyncDisposable
{
    public const string NomeCanal = Produto.Nome;
    readonly PipeStream fluxo;
    readonly SemaphoreSlim escrita = new(1, 1);
    readonly Dictionary<long, TaskCompletionSource<Mensagem>> aguardando = new();
    long proximoId;
    public Func<Mensagem, Task<Mensagem>>? AoPedir;
    public Action<Mensagem>? AoEvento;
    public Action? AoFechar;
    public object? Marca;
    public bool Aberta => fluxo.IsConnected;

    public Conexao(PipeStream fluxo) { this.fluxo = fluxo; }

    public static async Task<Conexao?> ConectarAsync(int msEspera = 1500, CancellationToken ct = default)
    {
        var cliente = new NamedPipeClientStream(".", NomeCanal, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await cliente.ConnectAsync(msEspera, ct).ConfigureAwait(false); }
        catch { await cliente.DisposeAsync(); return null; }
        if (!ServidorConfiavel(cliente)) { await cliente.DisposeAsync(); return null; }
        return new Conexao(cliente);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle h, out uint pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint acesso, bool herda, uint pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern bool QueryFullProcessImageNameW(IntPtr h, int f, char[] b, ref int n);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr OpenSCManagerW(string? maquina, string? banco, uint acesso);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr OpenServiceW(IntPtr scm, string nome, uint acesso);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)] static extern bool QueryServiceStatusEx(IntPtr s, int nivel, byte[] buf, int tam, out int precisa);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)] static extern bool QueryServiceConfigW(IntPtr s, IntPtr buf, int tam, out int precisa);
    [System.Runtime.InteropServices.DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr h);

    // Outro programa que abrisse um canal com o mesmo nome antes do serviço não recebe as ordens.
    static bool ServidorConfiavel(NamedPipeClientStream c)
    {
        try
        {
            if (!GetNamedPipeServerProcessId(c.SafePipeHandle, out var pid)) return false;
            var h = OpenProcess(0x1000, false, pid);
            // Conta comum não abre processo do sistema: confere pelo cadastro do serviço no Windows.
            if (h == IntPtr.Zero) return ServicoInstalado(pid);
            try
            {
                var b = new char[1024]; int n = b.Length;
                if (!QueryFullProcessImageNameW(h, 0, b, ref n)) return false;
                var caminho = new string(b, 0, n);
                var pasta = Path.GetDirectoryName(caminho) ?? "";
                return Path.GetFileName(caminho).Equals(Produto.ExeServico, StringComparison.OrdinalIgnoreCase)
                    && (pasta.Equals(Caminhos.Instalado, StringComparison.OrdinalIgnoreCase) || pasta.Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            }
            finally { CloseHandle(h); }
        }
        catch { return false; }
    }

    static bool ServicoInstalado(uint pid)
    {
        var scm = OpenSCManagerW(null, null, 0x0001);
        if (scm == IntPtr.Zero) return false;
        var s = OpenServiceW(scm, Produto.Nome, 0x0001 | 0x0004);
        try
        {
            if (s == IntPtr.Zero) return false;
            var st = new byte[36];
            if (!QueryServiceStatusEx(s, 0, st, st.Length, out _)) return false;
            if (BitConverter.ToUInt32(st, 28) != pid) return false;
            QueryServiceConfigW(s, IntPtr.Zero, 0, out int tam);
            if (tam <= 0) return false;
            var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(tam);
            try
            {
                if (!QueryServiceConfigW(s, buf, tam, out _)) return false;
                var binario = System.Runtime.InteropServices.Marshal.PtrToStringUni(System.Runtime.InteropServices.Marshal.ReadIntPtr(buf, 16)) ?? "";
                var exe = binario.Trim().Trim('"');
                int fim = exe.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (fim > 0) exe = exe[..(fim + 4)];
                return exe.Equals(Path.Combine(Caminhos.Instalado, Produto.ExeServico), StringComparison.OrdinalIgnoreCase);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
        }
        finally { if (s != IntPtr.Zero) CloseServiceHandle(s); CloseServiceHandle(scm); }
    }

    public Task IniciarLeitura() => Task.Run(LerLaco);

    async Task LerLaco()
    {
        var cab = new byte[4];
        try
        {
            while (fluxo.IsConnected)
            {
                if (!await LerExato(cab, 4).ConfigureAwait(false)) break;
                int n = BinaryPrimitives.ReadInt32LittleEndian(cab);
                if (n <= 0 || n > 64 * 1024 * 1024) break;
                var corpo = new byte[n];
                if (!await LerExato(corpo, n).ConfigureAwait(false)) break;
                Mensagem? m;
                try { m = JsonSerializer.Deserialize(corpo, SetupHubJson.Default.Mensagem); } catch { continue; }
                if (m is null) continue;
                Despachar(m);
            }
        }
        catch { }
        finally
        {
            lock (aguardando)
            {
                foreach (var t in aguardando.Values) t.TrySetResult(new Mensagem { Tipo = "resposta", Ok = false, Erro = "A conexão com o SetupHub fechou." });
                aguardando.Clear();
            }
            AoFechar?.Invoke();
        }
    }

    void Despachar(Mensagem m)
    {
        if (m.Tipo == "resposta")
        {
            TaskCompletionSource<Mensagem>? t;
            lock (aguardando) { if (aguardando.Remove(m.Id, out t)) { } }
            t?.TrySetResult(m);
        }
        else if (m.Tipo == "evento") AoEvento?.Invoke(m);
        else if (m.Tipo == "pedido")
        {
            _ = Task.Run(async () =>
            {
                Mensagem r;
                try { r = AoPedir is null ? new Mensagem { Ok = false, Erro = "Pedido não aceito por este lado do canal." } : await AoPedir(m).ConfigureAwait(false); }
                catch (ErroSetupHub e) { r = new Mensagem { Ok = false, Erro = e.Message }; }
                catch (Exception e) { r = new Mensagem { Ok = false, Erro = "Falha interna: " + e.Message }; }
                r.Tipo = "resposta"; r.Id = m.Id;
                try { await EnviarAsync(r).ConfigureAwait(false); } catch { }
            });
        }
    }

    async Task<bool> LerExato(byte[] b, int n)
    {
        int lidos = 0;
        while (lidos < n)
        {
            int r = await fluxo.ReadAsync(b.AsMemory(lidos, n - lidos)).ConfigureAwait(false);
            if (r <= 0) return false;
            lidos += r;
        }
        return true;
    }

    public async Task EnviarAsync(Mensagem m)
    {
        var corpo = JsonSerializer.SerializeToUtf8Bytes(m, SetupHubJson.Default.Mensagem);
        var cab = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(cab, corpo.Length);
        await escrita.WaitAsync().ConfigureAwait(false);
        try
        {
            await fluxo.WriteAsync(cab).ConfigureAwait(false);
            await fluxo.WriteAsync(corpo).ConfigureAwait(false);
            await fluxo.FlushAsync().ConfigureAwait(false);
        }
        finally { escrita.Release(); }
    }

    public async Task<Mensagem> PedirAsync(string comando, JsonElement? args = null, int msLimite = 30000)
    {
        var id = Interlocked.Increment(ref proximoId);
        var t = new TaskCompletionSource<Mensagem>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (aguardando) aguardando[id] = t;
        try { await EnviarAsync(new Mensagem { Tipo = "pedido", Id = id, Comando = comando, Args = args }).ConfigureAwait(false); }
        catch (Exception e) { lock (aguardando) aguardando.Remove(id); return new Mensagem { Tipo = "resposta", Ok = false, Erro = "Não foi possível falar com o SetupHub: " + e.Message }; }
        var pronto = await Task.WhenAny(t.Task, Task.Delay(msLimite)).ConfigureAwait(false);
        if (pronto != t.Task)
        {
            lock (aguardando) aguardando.Remove(id);
            return new Mensagem { Tipo = "resposta", Ok = false, Erro = "O SetupHub não respondeu a tempo." };
        }
        return t.Task.Result;
    }

    public Task EventoAsync(string tipo, JsonElement? dados) => EnviarAsync(new Mensagem { Tipo = "evento", Comando = tipo, Dados = dados });

    public async ValueTask DisposeAsync()
    {
        try { await fluxo.DisposeAsync(); } catch { }
    }
}
