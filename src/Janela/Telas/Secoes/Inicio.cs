using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas.Secoes;

sealed class Inicio : Tela
{
    protected override string Id => "inicio";
    Medidor mCpu = null!, mGpu = null!;
    Run tCpu = null!, tGpu = null!, cpuW = null!, cpuUso = null!, cpuPico = null!, gpuW = null!, vram = null!, gpuTemp = null!;
    TextBlock cpuLimite = null!, cpuWDe = null!, gpuWDe = null!, vramDe = null!, gpuClock = null!, gpuFanNota = null!, cpuNome = null!, gpuNome = null!, cpuPicoDe = null!;
    Barra bCpuW = null!, bCpuUso = null!, bGpuW = null!, bVram = null!;
    readonly List<(Border trilho, Border barra, TextBlock ghz)> nucleos = new();
    Grid gradeNucleos = null!;
    IconeVentoinha fanGpu = null!;
    Run gpuFanRpm = null!;
    Grid gradeFans = null!;
    readonly Dictionary<string, (IconeVentoinha icone, Run rpm, Run pct, Barra barra, Border caixa)> fans = new();
    Segmentado niveis = null!;
    ModoProcessador modo = null!;
    Grafico grafico = null!;
    FitaLed fita = null!;
    readonly List<(string plano, Botao b)> botoesLuz = new();
    Fluxo quadros = null!;
    readonly Dictionary<string, FrameworkElement> porTipo = new();
    DiagramaAr? diagrama;
    DispatcherTimer? animacaoLuz;
    double tLuz;

    protected override FrameworkElement Montar()
    {
        Cabecalho("Início");

        porTipo["processador"] = Fluxo.Item(CartaoCpu(), 400);
        porTipo["placaDeVideo"] = Fluxo.Item(CartaoGpu(), 400);
        porTipo["ventoinhas"] = Fluxo.Item(CartaoFans(), 520, 3);
        modo = new ModoProcessador("Modo do processador", 46);
        porTipo["modoProcessador"] = Fluxo.Item(modo.Cartao, 230);
        porTipo["temperatura"] = Fluxo.Item(CartaoGrafico(), 520, 3);
        porTipo["luz"] = Fluxo.Item(CartaoLuz(), 260);
        quadros = new Fluxo { Espaco = 12 };
        Ordenar();
        var raiz = K.Col(10, quadros);
        raiz.Margin = new Thickness(20, 12, 20, 16);
        return raiz;
    }

    void Ordenar()
    {
        quadros.Children.Clear();
        var lista = Cfg?.Aparencia.Inicio ?? Padroes.QuadrosInicio();
        bool comFluxo = false;
        foreach (var q in lista)
        {
            if (!q.Visivel) continue;
            if (q.Tipo == "fluxoDeAr")
            {
                comFluxo = true;
                var ids = VentoinhasLigadas();
                if (diagrama is null || string.Join(",", ids) != idsDiagrama)
                {
                    diagrama?.Parar();
                    diagrama = new DiagramaAr(ids, Cfg?.Aparencia.PosicaoFluxo) { MaxHeight = 330 };
                    idsDiagrama = string.Join(",", ids);
                    porTipo["fluxoDeAr"] = Fluxo.Item(Cartao("Fluxo de ar", diagrama), 420, 2);
                }
            }
            if (porTipo.TryGetValue(q.Tipo, out var e)) { if (e.Parent is Panel pai) pai.Children.Remove(e); quadros.Children.Add(e); }
        }
        if (!comFluxo && diagrama is not null) { diagrama.Parar(); diagrama = null; idsDiagrama = ""; porTipo.Remove("fluxoDeAr"); }
    }

    string idsDiagrama = "";

    UIElement Medidor(out Medidor m, out Run valor, out TextBlock nota, Brush cor, string notaTxt)
    {
        m = new Medidor(cor) { Width = 148, Height = 139 };
        return K.Mostrador(m, 52, cor, out valor, out nota, notaTxt);
    }

    static UIElement Linha(string rotulo, UIElement valor, Barra? barra)
    {
        var cab = K.Grade("* auto", 8, K.T(rotulo, 12.5, P.Apagado), valor);
        return barra is null ? cab : K.Col(5, cab, barra);
    }

    TextBlock Valor(out Run num, out TextBlock unidadeRef, string unidade)
    {
        var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        num = new Run("–") { FontFamily = P.Numero, FontSize = 23, FontWeight = FontWeights.SemiBold };
        t.Inlines.Add(num);
        var u = new Run(" " + unidade) { FontSize = 12.5, Foreground = P.Apagado };
        t.Inlines.Add(u);
        unidadeRef = t;
        return t;
    }

    Border CartaoCpu()
    {
        var link = Link("Processador", "processador");
        cpuNome = K.T("", 12.5, P.Apagado);
        var cab = K.Grade("* auto", 10, link, cpuNome);
        var med = Medidor(out mCpu, out tCpu, out cpuLimite, P.Cpu, "limite não lido");
        bCpuW = new Barra(P.Cpu); bCpuUso = new Barra(P.Cpu);
        cpuWDe = Valor(out cpuW, out _, "W de –");
        var uso = new TextBlock(); cpuUso = new Run("–") { FontFamily = P.Numero, FontSize = 23, FontWeight = FontWeights.SemiBold }; uso.Inlines.Add(cpuUso); uso.Inlines.Add(new Run(" %") { FontSize = 12.5, Foreground = P.Apagado });
        cpuPicoDe = Valor(out cpuPico, out _, "GHz");
        var direita = K.Col(13,
            Linha("Consumo", cpuWDe, bCpuW),
            K.Col(5, K.Grade("* auto", 8, K.T("Uso do processador", 12.5, P.Apagado), uso), bCpuUso),
            Linha("Clock mais alto", cpuPicoDe, null));
        direita.VerticalAlignment = VerticalAlignment.Center;
        var corpo = K.Grade("148 *", 18, med, direita);
        gradeNucleos = new Grid { Height = 56 };
        var clock = K.Col(6, K.T("Clock por núcleo", 12.5, P.Apagado), gradeNucleos);
        return K.Cartao(K.Col(10, cab, corpo, clock));
    }

    void MontarNucleos(int n)
    {
        if (nucleos.Count == n) return;
        nucleos.Clear();
        gradeNucleos.Children.Clear(); gradeNucleos.ColumnDefinitions.Clear();
        for (int i = 0; i < n; i++)
        {
            gradeNucleos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (i < n - 1) gradeNucleos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            var ghz = K.T("–", 11.5, P.Secundario); ghz.HorizontalAlignment = HorizontalAlignment.Center;
            var barra = new Border { Background = P.Cpu, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
            var trilho = new Border { Background = P.Poco, CornerRadius = new CornerRadius(3), Child = barra, ClipToBounds = true };
            var col = new Grid();
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            trilho.Margin = new Thickness(0, 3, 0, 0);
            Grid.SetRow(trilho, 1);
            col.Children.Add(ghz); col.Children.Add(trilho);
            Grid.SetColumn(col, i * 2);
            gradeNucleos.Children.Add(col);
            nucleos.Add((trilho, barra, ghz));
        }
    }

    Border CartaoGpu()
    {
        var link = Link("Placa de vídeo", "placaDeVideo");
        gpuNome = K.T("", 12.5, P.Apagado);
        var cab = K.Grade("* auto", 10, link, gpuNome);
        var med = Medidor(out mGpu, out tGpu, out _, P.Gpu, "ponto quente");
        bGpuW = new Barra(P.Gpu); bVram = new Barra(P.Gpu);
        gpuWDe = Valor(out gpuW, out _, "W");
        vramDe = Valor(out vram, out _, "GB");
        var nucleo = new TextBlock(); gpuTemp = new Run("–") { FontFamily = P.Numero, FontSize = 23, FontWeight = FontWeights.SemiBold }; nucleo.Inlines.Add(gpuTemp);
        gpuClock = K.T(" °C", 12.5, P.Apagado);
        var direita = K.Col(13,
            Linha("Consumo da placa", gpuWDe, bGpuW),
            Linha("Memória de vídeo", vramDe, bVram),
            Linha("Núcleo", K.Lin(0, nucleo, gpuClock), null));
        direita.VerticalAlignment = VerticalAlignment.Center;
        var corpo = K.Grade("148 *", 18, med, direita);
        fanGpu = new IconeVentoinha(36);
        var rpm = new TextBlock(); gpuFanRpm = new Run("–") { FontFamily = P.Numero, FontSize = 23, FontWeight = FontWeights.SemiBold }; rpm.Inlines.Add(gpuFanRpm);
        gpuFanNota = K.T(" rpm", 12.5, P.Apagado);
        var caixaFan = new Border { Background = P.Poco, CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 10, 12, 10), Child = K.Grade("36 *", 12, fanGpu, K.Col(0, K.T("Ventoinhas da placa", 12.5, P.Apagado), K.Lin(0, rpm, gpuFanNota))) };
        return K.Cartao(K.Col(10, cab, corpo, caixaFan));
    }

    static UIElement Link(string texto, string secao)
    {
        var t = K.T(texto, 15, P.Texto, FontWeights.SemiBold);
        var b = new Botao { Child = t, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left };
        b.MouseEnter += (_, _) => t.TextDecorations = TextDecorations.Underline;
        b.MouseLeave += (_, _) => t.TextDecorations = null;
        b.Clique += (_, _) => App.Janela?.Ir(secao);
        return b;
    }

    Border CartaoFans()
    {
        niveis = new Segmentado(new[] { ("silencioso", "Silencioso"), ("equilibrado", "Equilibrado"), ("forte", "Forte"), ("maximo", "Máximo") }, 34, 96);
        niveis.Escolheu += n => _ = Fazer("ventoinhas.nivel", Ligacao.Args(("nivel", n)));
        AoConfig(() => niveis.Selecionar(Cfg?.Ventoinhas.Nivel));
        var cab = K.Grade("* auto", 10, Link("Ventoinhas", "ventoinhas"), niveis);
        gradeFans = new Grid();
        return K.Cartao(K.Col(12, cab, gradeFans));
    }

    void MontarFans(Estado e)
    {
        var ligadas = e.Ventoinhas.Where(v => v.Conectada).ToList();
        if (ligadas.Count == 0) ligadas = e.Ventoinhas;
        if (ligadas.Select(v => v.Id).SequenceEqual(fans.Keys)) return;
        fans.Clear();
        var itens = new List<UIElement>();
        foreach (var v in ligadas)
        {
            var ic = new IconeVentoinha(38);
            var t = new TextBlock(); var rpm = new Run("–") { FontFamily = P.Numero, FontSize = 25, FontWeight = FontWeights.SemiBold }; var pct = new Run(" rpm") { FontSize = 12, Foreground = P.Apagado };
            t.Inlines.Add(rpm); t.Inlines.Add(pct);
            var barra = new Barra(P.Pa, 3);
            var col = K.Col(2, K.T(v.Nome, 12.5, P.Secundario), t, barra);
            var caixa = new Border { Background = P.Poco, CornerRadius = new CornerRadius(9), Padding = new Thickness(10), Child = K.Grade("38 *", 10, ic, col) };
            fans[v.Id] = (ic, rpm, pct, barra, caixa);
            itens.Add(caixa);
        }
        gradeFans.Children.Clear();
        var f = new Fluxo { Espaco = 10 };
        foreach (var i in itens) f.Children.Add(Fluxo.Item((UIElement)i, 150));
        gradeFans.Children.Add(f);
    }

    Border CartaoGrafico()
    {
        grafico = new Grafico(30, 90) { Height = 100, Capacidade = 120 };
        grafico.Series.Add(new Grafico.Serie { Cor = P.Gpu, Largura = 2 });
        grafico.Series.Add(new Grafico.Serie { Cor = P.Cpu, Largura = 2 });
        UIElement Legenda(string nome, Brush cor) => K.Lin(6, new Border { Width = 14, Height = 3, CornerRadius = new CornerRadius(2), Background = cor, VerticalAlignment = VerticalAlignment.Center }, K.T(nome, 12.5, P.Secundario));
        var cab = K.Grade("* auto", 10, K.Titulo("Temperatura nos últimos 2 minutos"), K.Lin(14, Legenda("Processador", P.Cpu), Legenda("Placa de vídeo", P.Gpu)));
        var eixo = new Grid { Width = 22, Height = 100 };
        eixo.RowDefinitions.Add(new RowDefinition()); eixo.RowDefinitions.Add(new RowDefinition()); eixo.RowDefinitions.Add(new RowDefinition());
        foreach (var (txt, linha, va) in new[] { ("90°", 0, VerticalAlignment.Top), ("60°", 1, VerticalAlignment.Center), ("30°", 2, VerticalAlignment.Bottom) })
        {
            var t = K.T(txt, 11.5, P.Apagado); t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = va; Grid.SetRow(t, linha); eixo.Children.Add(t);
        }
        return K.Cartao(K.Col(8, cab, K.Grade("22 *", 8, eixo, grafico)));
    }

    Border CartaoLuz()
    {
        fita = new FitaLed(false);
        var faixa = new Border { Background = P.Claro ? P.Profundo : Paleta.Cor("#0A0D10"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 7, 8, 7), Child = fita };
        var botoes = new List<UIElement>();
        foreach (var (plano, curto) in new[] { ("Temperatura", "Temperatura"), ("Arco-íris", "Arco-íris"), ("Foco", "Foco"), ("Respiração", "Respiração"), ("Música", "Música"), ("Desligada", "Desligada") })
        {
            var b = new Botao { Height = 30, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Child = Botao.Texto(curto, P.Secundario, FontWeights.Normal, 13) };
            var nome = plano;
            b.Clique += (_, _) => _ = Fazer("luz.plano", Ligacao.Args(("nome", nome)));
            botoesLuz.Add((plano, b));
            botoes.Add(b);
        }
        animacaoLuz = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        animacaoLuz.Tick += (_, _) => { tLuz += 0.25; PintarFita(); };
        return K.Cartao(K.Col(10, Link("Luz", "luz"), faixa, K.Iguais(3, 6, botoes)));
    }

    void PintarLuz()
    {
        var c = Cfg;
        if (c is null) return;
        var ativo = c.Luz.Ligada ? c.Luz.Plano : "Desligada";
        foreach (var (plano, b) in botoesLuz)
        {
            bool on = plano == ativo;
            b.Fundo(on ? P.Selecao : Brushes.Transparent);
            b.FundoSobre = on ? P.Selecao : P.Poco;
            b.BordaNormal = on ? P.Selecao : P.BordaForte; b.BorderBrush = b.BordaNormal;
            ((TextBlock)b.Child).Foreground = on ? P.TextoSelecao : P.Secundario;
        }
    }

    void PintarFita()
    {
        var c = Cfg;
        if (c is null) return;
        var ef = c.Luz.Ligada ? PreviaLuz.DoPlano(c, c.Luz.Plano) ?? c.Luz.Saidas.FirstOrDefault()?.Efeito : new EfeitoLuz { Tipo = "desligado" };
        if (ef is null) return;
        fita.Mostrar(PreviaLuz.Cores(ef, 24, tLuz, Est), ef.Tipo == "desligado" ? 1 : 0.35 + ef.Brilho / 100.0 * 0.65);
    }

    protected override void Config(Config c)
    {
        modo.Pintar();
        PintarLuz();
        PintarFita();
        if (quadros is not null) Ordenar();
    }

    public override void NovasFaixas(Faixas f) => modo.Pintar();

    public override void Mostrada()
    {
        base.Mostrada();
        animacaoLuz?.Start();
        if (grafico.Series[0].V.Count == 0) _ = Preencher();
    }

    public override void Escondida()
    {
        animacaoLuz?.Stop();
        foreach (var f in fans.Values) f.icone.Rpm(0);
        fanGpu.Rpm(0);
        diagrama?.Parar();
    }

    async Task Preencher()
    {
        long agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var (sensor, serie) in new[] { ("gpu.quente", 0), ("cpu.temp", 1) })
        {
            var r = await App.Lig.Pedir("historico", Ligacao.Args(("sensor", sensor), ("de", agora - 120), ("ate", agora), ("pontos", 120)));
            var s = Json.Ler<SerieHistorico>(r.Dados);
            if (s is null) continue;
            grafico.Series[serie].V.Clear();
            grafico.Series[serie].V.AddRange(s.Valores);
        }
        grafico.Atualizar();
    }

    public override void Estado(Estado e)
    {
        var lim = LimiteTemperaturaCpu;
        cpuNome.Text = Simplificar(e.Cpu.Nome);
        var corC = CorCpu(e.Cpu.TempMaisQuente);
        tCpu.Text = K.N(e.Cpu.TempMaisQuente); tCpu.Foreground = corC;
        mCpu.Valor(Proporcao(e.Cpu.TempMaisQuente, lim), corC);
        cpuLimite.Text = lim is double limiteCpu ? $"limite {limiteCpu:0} °C" : "limite não lido";
        double? pptLim = Cfg?.Processador.Ppt ?? Fx?.Campos.GetValueOrDefault("processador.ppt")?.Atual;
        cpuW.Text = K.N(e.Cpu.Consumo);
        ((Run)cpuWDe.Inlines.LastInline).Text = pptLim is double pw ? $" W de {pw:0}" : " W";
        bCpuW.Valor(Proporcao(e.Cpu.Consumo, pptLim));
        cpuUso.Text = K.N(e.Cpu.Uso);
        bCpuUso.Valor(Proporcao(e.Cpu.Uso, 100));
        var fmax = ClockMaximoCpu;
        cpuPico.Text = K.N(e.Cpu.ClockMax / 1000, 2);
        ((Run)cpuPicoDe.Inlines.LastInline).Text = fmax is double fm ? $" GHz de {(fm / 1000).ToString("0.00", K.Br)}" : " GHz";
        MontarNucleos(e.Cpu.Nucleos.Count);
        for (int i = 0; i < nucleos.Count && i < e.Cpu.Nucleos.Count; i++)
        {
            var c = e.Cpu.Nucleos[i].Clock;
            nucleos[i].ghz.Text = K.N(c / 1000, 1);
            double h = nucleos[i].trilho.ActualHeight > 0 ? nucleos[i].trilho.ActualHeight : 40;
            nucleos[i].barra.Height = Proporcao(c, fmax) is double proporcao ? Math.Clamp(proporcao, 0, 1) * h : 0;
        }

        gpuNome.Text = Simplificar(e.Gpu.Nome);
        var corG = CorGpu(e.Gpu.TempMaisQuente);
        tGpu.Text = K.N(e.Gpu.TempMaisQuente); tGpu.Foreground = corG;
        mGpu.Valor(null, corG);
        var tbp = ConsumoMaximoGpu;
        var gw = e.Gpu.ConsumoTotal ?? e.Gpu.Consumo;
        gpuW.Text = K.N(gw); ((Run)gpuWDe.Inlines.LastInline).Text = tbp is double tw ? $" W de {tw:0}" : " W";
        bGpuW.Valor(Proporcao(gw, tbp));
        var vramTotal = VramTotalGpu(e.Gpu);
        vram.Text = K.N(e.Gpu.VramUsada, 1); ((Run)vramDe.Inlines.LastInline).Text = $" GB de {K.N(vramTotal, 0)}";
        bVram.Valor(Proporcao(e.Gpu.VramUsada, vramTotal));
        gpuTemp.Text = K.N(e.Gpu.Temp);
        gpuClock.Text = $" °C, {K.N(e.Gpu.Clock)} MHz";
        gpuFanRpm.Text = K.N(e.Gpu.FanRpm);
        gpuFanNota.Text = e.Gpu.FanRpm is < 60 ? " rpm, parada automática" : " rpm";
        fanGpu.Rpm(e.Gpu.FanRpm);

        MontarFans(e);
        foreach (var (id, f) in fans)
        {
            var v = e.Ventoinhas.FirstOrDefault(x => x.Id == id);
            if (v is null) continue;
            f.rpm.Text = v.Rpm is < 60 ? "parada" : K.N(v.Rpm);
            f.pct.Text = v.Rpm is < 60 ? $", {Pct(v.Porcentagem)}" : $" rpm, {Pct(v.Porcentagem)}";
            f.barra.Valor(Proporcao(v.Porcentagem, 100));
            f.icone.Rpm(v.Rpm);
        }

        grafico.Empurrar(0, e.Gpu.TempMaisQuente);
        grafico.Empurrar(1, e.Cpu.TempMaisQuente);
        grafico.Atualizar();
        if (diagrama is not null && string.Join(",", VentoinhasLigadas()) != idsDiagrama) Ordenar();
        diagrama?.Atualizar(e);
    }

    public static string Simplificar(string nome) => System.Text.RegularExpressions.Regex.Replace(nome.Replace("AMD ", ""), @"\s+\d+-Core Processor", "").Trim();

    public override void Descartar() { animacaoLuz?.Stop(); diagrama?.Parar(); }
}
