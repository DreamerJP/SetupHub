using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas.Detalhado;

sealed class PlacaDeVideo : Tela
{
    protected override string Id => "placaDeVideo";
    Medidor medidor = null!;
    Run quente = null!;
    readonly Dictionary<string, (Run v, Run refer, Barra b)> leituras = new();
    IconeVentoinha fan = null!;
    TextBlock fanTxt = null!;
    Segmentado modo = null!;
    readonly List<(string campo, string nome, string un, Deslizante d, TextBlock valor, TextBlock rot, Border linha)> campos = new();
    Chave rapidaMem = null!;
    Border linhaRapida = null!;
    StackPanel camposPainel = null!;
    Botao aplicar = null!;
    ConfigPlacaDeVideo rascunho = new();
    bool manual;
    Chave zero = null!;
    GraficoCurva curva = null!;
    Segmentado curvaModo = null!;
    readonly List<(CampoNum t, CampoNum p)> pontos = new();
    Grid gradePontos = null!;
    TextBlock assistTxt = null!;
    Botao assistBotao = null!;

    static readonly (string Campo, string Nome, string Un)[] Campos =
    {
        ("limiteEnergia", "Limite de energia", "%"), ("tensao", "Desvio de tensão", "mV"), ("clockMax", "Desvio do clock máximo", "MHz"),
        ("clockMin", "Clock mínimo", "MHz"), ("clockMemoria", "Clock da memória", "MHz"),
    };

    protected override FrameworkElement Montar()
    {
        Cabecalho("Placa de vídeo", () => Fx?.Gpu is { } g ? $"{Inicio.Simplificar(g.Nome)}, {Math.Round(g.VramMb / 1024.0):0} GB" : "");
        var agora = MontarAgora();
        var desempenho = MontarDesempenho();
        var direita = K.Col(12, MontarVentoinhas(), MontarAssistente());
        var linha = new Fluxo();
        linha.Children.Add(Fluxo.Item(desempenho, 520, 3));
        linha.Children.Add(Fluxo.Item(direita, 320, 2));
        linha.EsticarAltura = false;
        var raiz = K.Col(12, agora, linha);
        raiz.Margin = new Thickness(20, 12, 20, 20);
        return raiz;
    }

    Border MontarAgora()
    {
        medidor = new Medidor(P.Gpu) { Width = 132, Height = 124 };
        var med = K.Mostrador(medidor, 44, P.Gpu, out quente, out _, "ponto quente");
        var grade = new Fluxo { Espaco = 22, EsticarAltura = false, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (id, nome) in new[] { ("nucleo", "Núcleo"), ("consumo", "Consumo da placa"), ("clock", "Clock"), ("vram", "Memória de vídeo"), ("memoria", "Memória") })
        {
            var tx = new TextBlock();
            var v = new Run("–") { FontFamily = P.Numero, FontSize = 25, FontWeight = FontWeights.SemiBold };
            var r = new Run("") { FontSize = 12.5, Foreground = P.Apagado };
            tx.Inlines.Add(v); tx.Inlines.Add(r);
            var b = new Barra(P.Gpu, 3);
            leituras[id] = (v, r, b);
            grade.Children.Add(Fluxo.Item(K.Col(4, K.T(nome, 12.5, P.Apagado), tx, b), 120));
        }
        fan = new IconeVentoinha(40);
        fanTxt = K.T("–", 22, P.Texto, FontWeights.SemiBold, P.Numero);
        var caixaFan = new Border { Background = P.Poco, CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 10, 12, 10), Child = K.Grade("40 auto", 10, fan, K.Col(0, K.T("Ventoinhas", 12.5, P.Apagado), fanTxt)), VerticalAlignment = VerticalAlignment.Center };
        // A linha estica cada item à altura do mostrador; as leituras e as ventoinhas ficam centradas nela.
        var f = new Fluxo { Espaco = 22 };
        f.Children.Add(Fluxo.Item(med, 132, 0));
        f.Children.Add(Fluxo.Item(grade, 520, 1));
        f.Children.Add(Fluxo.Item(caixaFan, 170, 0));
        return K.Cartao(f);
    }

    Border MontarDesempenho()
    {
        modo = new Segmentado(new[] { ("auto", "Automático"), ("manual", "Manual") }, 32, 104);
        modo.Escolheu += v => _ = Modo(v == "manual");
        camposPainel = new StackPanel();
        foreach (var (campo, nome, un) in Campos)
        {
            var rot = K.T("", 12, P.Apagado);
            var d = new Deslizante(0, 100, 1, 0);
            var valor = K.T("–", 20, P.Texto, FontWeights.SemiBold, P.Numero);
            valor.HorizontalAlignment = HorizontalAlignment.Right;
            var nomeCampo = campo;
            d.Movendo += v => valor.Text = Texto(nomeCampo, v) + " " + un;
            d.Mudou += v => { Definir(nomeCampo, (int)v); Atualizar(); };
            var g = K.Grade("1* 1.3* 96", 12, K.Col(0, K.T(nome), rot), d, valor);
            var linha = new Border { MinHeight = 40, Child = g, Margin = new Thickness(0, 0, 0, 6) };
            campos.Add((campo, nome, un, d, valor, rot, linha));
            camposPainel.Children.Add(linha);
        }
        rapidaMem = new Chave();
        rapidaMem.Mudou += on => { rascunho.MemoriaRapida = on; Atualizar(); };
        linhaRapida = K.LinhaAjuste(K.Col(0, K.T("Tempos rápidos da memória"), K.T("temporizações reduzidas da memória de vídeo", 12, P.Apagado)), rapidaMem, 40, 44);
        camposPainel.Children.Add(linhaRapida);
        aplicar = Botao.Cheio("Aplicar", () => _ = Aplicar(), 36);
        aplicar.IsEnabled = false;
        var voltar = Botao.Contorno("Voltar ao padrão de fábrica", () => _ = Fazer("placaDeVideo.padrao", null, "Placa de vídeo restaurada aos ajustes de fábrica."), 36);
        ((TextBlock)voltar.Child).Foreground = P.Texto;
        var rodape = new Border { BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 0), Child = K.Grade("200 200 *", 10, aplicar, voltar, K.Nota("Se o Adrenalin também alterar estes ajustes, prevalece a última gravação.")) };
        return K.Cartao(K.Col(10, K.Grade("* auto", 10, K.Titulo("Desempenho"), modo), camposPainel, rodape));
    }

    string Texto(string campo, double v) => campo switch
    {
        "limiteEnergia" or "tensao" or "clockMax" => (v > 0 ? "+" : "") + v.ToString("N0", K.Br),
        _ => v.ToString("N0", K.Br),
    };

    void Definir(string campo, int v)
    {
        switch (campo)
        {
            case "limiteEnergia": rascunho.LimiteEnergia = v; break;
            case "tensao": rascunho.Tensao = v; break;
            case "clockMax": rascunho.ClockMax = v; break;
            case "clockMin": rascunho.ClockMin = v; break;
            case "clockMemoria": rascunho.ClockMemoria = v; break;
        }
    }

    int? Valor(ConfigPlacaDeVideo g, string campo) => campo switch
    {
        "limiteEnergia" => g.LimiteEnergia, "tensao" => g.Tensao, "clockMax" => g.ClockMax, "clockMin" => g.ClockMin, _ => g.ClockMemoria,
    };

    async Task Modo(bool m)
    {
        manual = m;
        if (!m)
        {
            if (Cfg?.PlacaDeVideo is { } g && Json.Texto(g) != Json.Texto(new ConfigPlacaDeVideo()))
                await Fazer("placaDeVideo.padrao", null, "Placa de vídeo em modo automático, padrão de fábrica.");
        }
        Atualizar();
    }

    async Task Aplicar()
    {
        var r = Json.Copia(rascunho);
        if (await Gravar(c => c.PlacaDeVideo = r, "Placa de vídeo ajustada e conferida.")) aplicar.IsEnabled = false;
    }

    void Atualizar()
    {
        var c = Cfg;
        foreach (var (campo, nome, un, d, valor, rot, linha) in campos)
        {
            var f = Fx?.Campos.GetValueOrDefault("placaDeVideo." + campo);
            linha.Visibility = f is null || !f.Disponivel && campo == "clockMin" ? Visibility.Collapsed : Visibility.Visible;
            if (f is null) continue;
            if (f.Disponivel) { d.Min = f.Min; d.Max = f.Max; d.Passo = Math.Max(1, f.Passo); }
            double v = Valor(rascunho, campo) ?? f.Atual ?? f.Padrao ?? 0;
            d.Valor = v;
            valor.Text = Texto(campo, v) + " " + un;
            rot.Text = f.Disponivel ? $"padrão {Validacao.Numero(f.Padrao ?? 0)}, de {Validacao.Numero(f.Min)} a {Validacao.Numero(f.Max)} {un}" : "travado: " + f.Motivo;
            d.IsEnabled = manual && f.Disponivel;
            linha.Opacity = manual ? 1 : 0.55;
        }
        var fr = Fx?.Campos.GetValueOrDefault("placaDeVideo.memoriaRapida");
        linhaRapida.Visibility = fr is { Disponivel: true } ? Visibility.Visible : Visibility.Collapsed;
        rapidaMem.Ligada = rascunho.MemoriaRapida ?? fr?.Atual == 1;
        rapidaMem.IsEnabled = manual;
        modo.Selecionar(manual ? "manual" : "auto");
        aplicar.IsEnabled = manual && c is not null && Json.Texto(rascunho) != Json.Texto(c.PlacaDeVideo);
    }

    Border MontarVentoinhas()
    {
        zero = new Chave();
        zero.Mudou += on => _ = Gravar(c => c.Ventoinhas.Gpu.ParadaEmRepouso = on, on ? "Ventoinhas da placa param em repouso." : "Ventoinhas da placa em rotação contínua.");
        curva = new GraficoCurva(30, 100, false, P.Gpu) { Height = 110 };
        var fundo = new Border { Background = P.PocoEscuro, CornerRadius = new CornerRadius(8), Child = curva, Padding = new Thickness(0, 4, 0, 0) };
        curvaModo = new Segmentado(new[] { ("auto", "Do driver"), ("curva", "Própria") }, 30);
        curvaModo.Escolheu += v => _ = ModoCurva(v);
        gradePontos = new Grid();
        var cols = new List<UIElement>();
        for (int i = 0; i < 5; i++)
        {
            var t = new CampoNum(null, "°C", double.NaN, 30) { Width = double.NaN, AceitaVazio = false };
            var p = new CampoNum(null, "%", double.NaN, 30) { Width = double.NaN, AceitaVazio = false };
            int k = i;
            t.Confirmou += v => _ = Ponto(k, v, null);
            p.Confirmou += v => _ = Ponto(k, null, v);
            pontos.Add((t, p));
            cols.Add(K.Col(4, t, p));
        }
        gradePontos.Children.Add(K.Iguais(5, 6, cols));
        return K.Cartao(K.Col(10, K.Titulo("Ventoinhas da placa"),
            K.LinhaAjuste("Parar em repouso", zero, 40, 40),
            K.LinhaAjuste("Curva", curvaModo, 196, 40),
            fundo, gradePontos));
    }

    async Task ModoCurva(string v)
    {
        await Gravar(c =>
        {
            c.Ventoinhas.Gpu.Modo = v;
            if (v == "curva" && c.Ventoinhas.Gpu.Curva.Count != 5) c.Ventoinhas.Gpu.Curva = Padroes.CurvaGpu("equilibrado");
        }, v == "curva" ? "Curva personalizada nas ventoinhas da placa." : "Ventoinhas da placa com a curva do driver.");
    }

    async Task Ponto(int i, double? t, double? p)
    {
        await Gravar(c =>
        {
            var cv = c.Ventoinhas.Gpu.Curva;
            if (cv.Count != 5) cv = c.Ventoinhas.Gpu.Curva = Padroes.CurvaGpu("equilibrado");
            if (t is double tt) cv[i].T = tt;
            if (p is double pp) cv[i].P = pp;
            c.Ventoinhas.Gpu.Curva = cv.OrderBy(x => x.T).ToList();
            c.Ventoinhas.Gpu.Modo = "curva";
        });
    }

    Border MontarAssistente()
    {
        assistTxt = new TextBlock { Text = "Clock mais alto sustentado, com menos calor e ruído no mesmo consumo. Reduz a tensão em etapas de 10 mV, testa a estabilidade em cada uma e grava uma etapa acima da última aprovada, como margem de segurança.", Foreground = P.Secundario, TextWrapping = TextWrapping.Wrap, FontSize = P.F(14), LineHeight = P.F(19.6) };
        assistBotao = Botao.Cheio("Começar", () => _ = Assistente(), 36);
        assistBotao.Width = 200; assistBotao.HorizontalAlignment = HorizontalAlignment.Left;
        return K.Cartao(K.Col(8, K.Titulo("Assistente de redução de tensão"), assistTxt, assistBotao));
    }

    async Task Assistente()
    {
        if (Est?.Tarefa is { Rodando: true }) { await Fazer("tarefa.cancelar", null, "Cancelamento pedido."); return; }
        await Fazer("placaDeVideo.assistente", null, "Assistente de tensão iniciado. A placa permanece em carga total por alguns minutos.");
    }

    protected override void Config(Config c)
    {
        rascunho = Json.Copia(c.PlacaDeVideo);
        manual = manual || Json.Texto(c.PlacaDeVideo) != Json.Texto(new ConfigPlacaDeVideo());
        Atualizar();
        zero.Ligada = c.Ventoinhas.Gpu.ParadaEmRepouso ?? Fx?.Campos.GetValueOrDefault("ventoinhas.gpu.paradaEmRepouso")?.Atual == 1;
        curvaModo.Selecionar(c.Ventoinhas.Gpu.Modo);
        var pts = c.Ventoinhas.Gpu.Modo == "curva" && c.Ventoinhas.Gpu.Curva.Count == 5 ? c.Ventoinhas.Gpu.Curva : null;
        for (int i = 0; i < 5; i++)
        {
            pontos[i].t.Definir(pts?[i].T); pontos[i].p.Definir(pts?[i].P);
            pontos[i].t.IsEnabled = pontos[i].p.IsEnabled = pts is not null;
        }
        gradePontos.Visibility = pts is null ? Visibility.Collapsed : Visibility.Visible;
        curva.Mostrar(pts ?? Padroes.CurvaGpu("equilibrado"), Est?.Gpu.TempMaisQuente, P.Gpu);
    }

    public override void NovasFaixas(Faixas f) => Atualizar();

    public override void Estado(Estado e)
    {
        var g = e.Gpu;
        var cor = CorGpu(g.TempQuente);
        quente.Text = g.TempQuente is double q ? $"{q:0}°" : "–"; quente.Foreground = cor;
        medidor.Valor(null, cor);
        var lim = ConsumoMaximoGpu;
        var clockMaximo = ClockMaximoGpu;
        var vramTotal = VramTotalGpu(g);
        void L(string id, double? v, string refer, double? fracao, int casas = 0) { var x = leituras[id]; x.v.Text = K.N(v, casas); x.refer.Text = " " + refer; x.b.Valor(fracao); }
        L("nucleo", g.Temp, "°C", null);
        L("consumo", g.ConsumoTotal ?? g.Consumo, lim is double limiteConsumo ? $"W de {limiteConsumo:0}" : "W", Proporcao(g.ConsumoTotal ?? g.Consumo, lim));
        L("clock", g.Clock, clockMaximo is double limiteClock ? $"MHz de {limiteClock:0}" : "MHz", Proporcao(g.Clock, clockMaximo));
        L("vram", g.VramUsada, $"GB de {K.N(vramTotal)}", Proporcao(g.VramUsada, vramTotal), 1);
        L("memoria", g.TempMemoria, "°C", null);
        fan.Rpm(g.FanRpm);
        fanTxt.Text = g.FanRpm is < 60 ? "paradas" : $"{K.N(g.FanRpm)} rpm";
        if (Cfg is { } c) curva.Mostrar(c.Ventoinhas.Gpu.Modo == "curva" && c.Ventoinhas.Gpu.Curva.Count == 5 ? c.Ventoinhas.Gpu.Curva : Padroes.CurvaGpu("equilibrado"), g.TempMaisQuente, P.Gpu);
        if (e.Tarefa is { } t && t.Nome.Contains("tensão"))
        {
            assistBotao.TextoBotao = t.Rodando ? "Cancelar" : "Começar";
            if (t.Rodando) assistTxt.Text = $"{t.Progresso * 100:0}%. {t.Etapa}";
            else if (t.Resultado is not null) assistTxt.Text = t.Resultado;
        }
    }
}
