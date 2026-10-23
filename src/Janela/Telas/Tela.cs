using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas;

abstract class Tela
{
    public FrameworkElement Raiz { get; private set; } = null!;
    public Estado? EstadoPendente;
    protected Config? Cfg => App.Lig.Config;
    protected Faixas? Fx => App.Lig.Faixas;
    protected double? ConsumoMaximoGpu => Fx?.Campos.GetValueOrDefault("placaDeVideo.consumoMaximo") is { Disponivel: true, Padrao: double w } && w > 0 ? w * (1 + (Cfg?.PlacaDeVideo.LimiteEnergia ?? 0) / 100.0) : null;
    protected double? LimiteTemperaturaCpu
    {
        get
        {
            double? limite = Cfg?.Processador.LimiteTemperatura;
            if (limite is null && Fx?.Campos.GetValueOrDefault("processador.limiteTemperatura") is { Disponivel: true, Atual: double atual }) limite = atual;
            return limite is double valor && valor > 0 ? valor : null;
        }
    }
    protected double? ClockMaximoCpu => Fx?.Campos.GetValueOrDefault("processador.fmax") is { Disponivel: true, Atual: double f } && f > 0 ? f : null;
    protected double? ClockMaximoGpu => Fx?.Campos.GetValueOrDefault("placaDeVideo.clockMax") is { Disponivel: true, Max: double f } && f > 0 ? f : null;
    protected double? VramTotalGpu(LeituraGpu gpu)
    {
        if (gpu.VramTotal is double total && total > 0) return total;
        return Fx?.Gpu is { VramMb: > 0 } info ? info.VramMb / 1024.0 : null;
    }
    protected static double? Proporcao(double? valor, double? limite) => valor is double atual && double.IsFinite(atual) && limite is double maximo && double.IsFinite(maximo) && maximo > 0 ? atual / maximo : null;
    protected Estado? Est => App.Lig.Estado;

    // Sem nenhuma ventoinha detectada valem todas as saídas da placa.
    protected List<string> VentoinhasLigadas()
    {
        var ids = Est?.Ventoinhas.Where(v => v.Conectada).Select(v => v.Id).ToList();
        return ids is { Count: > 0 } ? ids : Padroes.Ventoinhas.Select(v => v.Id).ToList();
    }
    protected static Paleta P => K.P;
    protected abstract string Id { get; }
    readonly List<Action> aoConfig = new();
    bool montada;

    public static Tela Criar(string id) => id switch
    {
        "inicio" => new Secoes.Inicio(),
        "ventoinhas" => new Secoes.Ventoinhas(),
        "luz" => new Secoes.Luz(),
        "processador" => new Secoes.Processador(),
        "placaDeVideo" => new Secoes.PlacaDeVideo(),
        "jogos" => new Secoes.Jogos(),
        "telas" => new Secoes.Telas(),
        "energia" => new Secoes.Energia(),
        "perfis" => new Secoes.Perfis(),
        "sistema" => new Secoes.Sistema(),
        "aparencia" => new Secoes.Aparencia(),
        _ => throw new ArgumentException(id),
    };

    public void Preparar()
    {
        if (montada) return;
        var conteudo = Montar();
        var rolagem = new ScrollViewer
        {
            Content = conteudo, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        rolagem.SizeChanged += (_, _) => conteudo.MinHeight = Math.Max(0, rolagem.ViewportHeight - conteudo.Margin.Top - conteudo.Margin.Bottom);
        Raiz = rolagem;
        montada = true;
    }

    protected abstract FrameworkElement Montar();
    public virtual void Estado(Estado e) { }
    public virtual void Mostrada() { if (EstadoPendente is { } e) { EstadoPendente = null; EstadoSeguro(e); } }
    public virtual void Escondida() { }
    public virtual void Descartar() { }

    public void NovaConfig(Config c)
    {
        foreach (var a in aoConfig) a();
        Config(c);
    }

    protected virtual void Config(Config c) { }
    public virtual void NovasFaixas(Faixas f) { }
    protected void AoConfig(Action a) => aoConfig.Add(a);

    // Falhas sempre aparecem, mesmo com mensagem de sucesso pedida.
    internal static async Task<bool> Fazer(string comando, JsonElement? args = null, string? ok = null)
    {
        var r = await App.Lig.Pedir(comando, args, 120000);
        Resultado(r, ok);
        return r.Ok;
    }

    protected static async Task<bool> Definir(string caminho, object? valor, string? ok = null)
    {
        string v = valor switch
        {
            null => "padrao",
            bool b => b ? "true" : "false",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => valor.ToString() ?? "",
        };
        var r = await App.Lig.Definir(caminho, v);
        Resultado(r, ok);
        return r.Ok;
    }

    internal static async Task<bool> Gravar(Action<Config> mudar, string? ok = null)
    {
        var c = App.Lig.Config;
        if (c is null) { App.Janela?.Aviso("Configuração ainda não recebida do serviço.", true); return false; }
        var n = Json.Copia(c);
        mudar(n);
        var r = await App.Lig.Pedir("config.gravar", Ligacao.Args(("config", Json.Elemento(n)), ("origem", "janela")));
        Resultado(r, ok);
        return r.Ok;
    }

    static void Resultado(Mensagem r, string? ok)
    {
        if (!r.Ok) { App.Janela?.Aviso(r.Erro ?? "Alteração recusada pelo SetupHub.", true); return; }
        var res = Json.Ler<ResultadoAplicar>(r.Dados);
        if (res is not null && res.Falhas.Count > 0) App.Janela?.Aviso("Falhou: " + string.Join("; ", res.Falhas), true);
        else if (ok is not null) App.Janela?.Aviso(ok);
    }

    public string Titulo { get; private set; } = "";
    public Func<string>? Subtitulo { get; private set; }

    // Título e subtítulo aparecem na barra fixa do alto da janela, junto dos perfis.
    protected void Cabecalho(string titulo, Func<string>? sub = null) { Titulo = titulo; Subtitulo = sub; }

    // A escolha fica lembrada por seção; "completo" diz como a tela abre.
    protected Segmentado RapidoCompleto(Action<bool> aoTrocar, out bool completo, double largura = 88)
    {
        var seg = new Segmentado(new[] { ("rapido", "Rápido"), ("completo", "Completo") }, 32, largura);
        seg.HorizontalAlignment = HorizontalAlignment.Right;
        string modo = Cfg?.Aparencia.ModoSecao.TryGetValue(Id, out var m) == true ? m : "rapido";
        seg.Selecionar(modo);
        completo = modo == "completo";
        seg.Escolheu += v =>
        {
            aoTrocar(v == "completo");
            _ = Gravar(c => c.Aparencia.ModoSecao[Id] = v);
        };
        return seg;
    }

    protected static Border Cartao(string? titulo, UIElement conteudo, UIElement? direita = null, double gap = 10, Thickness? pad = null)
    {
        UIElement corpo = conteudo;
        if (titulo is not null)
        {
            var cab = direita is null ? (UIElement)K.Titulo(titulo) : K.Grade("* auto", 10, K.Titulo(titulo), direita);
            if (cab is FrameworkElement fe) fe.MinHeight = 30;
            corpo = K.Col(gap, cab, conteudo);
        }
        return K.Cartao(corpo, pad);
    }

    protected static string Pct(double? v) => v is double d ? $"{d:0}%" : "–";

    // Falha numa tela não derruba a janela.
    public void EstadoSeguro(Estado e)
    {
        try { Estado(e); } catch (Exception x) { App.Registrar(x); }
    }
    protected static Brush CorCpu(double? t) => P.CalorCpu(t, App.Lig.Config);
    protected static Brush CorGpu(double? quente) => P.CalorGpu(quente, App.Lig.Config);
}
