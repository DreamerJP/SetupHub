using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas.Secoes;

sealed class Ventoinhas : Tela
{
    protected override string Id => "ventoinhas";
    DiagramaAr diagrama = null!;
    Border hostDiagrama = null!;
    TextBlock tituloSel = null!;
    StackPanel corpoAjuste = null!;
    UIElement semSel = null!, rapido = null!, completo = null!;
    bool modoCompleto;
    readonly List<(string id, Botao b, TextBlock nome, TextBlock sub)> niveis = new();
    Deslizante fixo = null!;
    Botao pararFan = null!;
    TextBlock fixoTxt = null!, fixoNota = null!;
    Escolha fonte = null!;
    GraficoCurva curva = null!;
    readonly List<(CampoNum t, CampoNum p)> pontos = new();
    CampoNum suav = null!, hist = null!, parar = null!;
    Chave pararLigada = null!;
    Border linhaParar = null!;
    StackPanel tabela = null!;
    readonly Dictionary<string, (Marcador m, TextBlock rpm, TextBlock pct, TextBlock ctl, TextBlock ini, TextBlock max)> linhas = new();
    TextBlock rodapeTabela = null!;
    Botao calibrar = null!;
    Grafico grafico = null!;
    TextBlock eixoTopo = null!, eixoMeio = null!;
    readonly List<string> serieIds = new();
    StackPanel legenda = null!;
    Botao posicionar = null!;
    readonly HashSet<string> sel = new();

    protected override FrameworkElement Montar()
    {
        Cabecalho("Ventoinhas", () =>
        {
            var e = Est; int n = e?.Ventoinhas.Count(v => v.Conectada) ?? 0;
            return $"{n} ligadas, das {Padroes.Ventoinhas.Length} saídas da placa";
        });
        hostDiagrama = new Border();
        posicionar = Botao.Contorno("Posicionar", () => AlternarPosicionar());
        posicionar.Width = 120;
        var cartaoAr = Cartao(null, K.Col(8,
            K.Grade("* auto", 10, K.Titulo("Fluxo de ar"), K.T("Clique para selecionar, Ctrl para várias", 12.5, P.Apagado)),
            hostDiagrama,
            K.Grade("* 120", 10, K.Nota("Arraste cada ventoinha para a posição correspondente no gabinete."), posicionar)), null, 8, new Thickness(16, 12, 16, 12));
        MontarDiagrama();

        tituloSel = K.T("Ajuste", 15, P.Texto, FontWeights.SemiBold);
        var rc = RapidoCompleto(c => { modoCompleto = c; Corpo(); }, out modoCompleto);
        corpoAjuste = new StackPanel();
        semSel = new TextBlock { Text = "Nenhuma ventoinha selecionada. Selecione no desenho ou na lista.", Foreground = P.Apagado, FontSize = P.F(14), TextWrapping = TextWrapping.Wrap };
        rapido = MontarRapido();
        completo = MontarCompleto();
        var cartaoAjuste = Cartao(null, K.Col(10, K.Grade("* auto", 10, tituloSel, rc), corpoAjuste), null, 10, new Thickness(16, 12, 16, 12));

        var cartaoSaidas = MontarTabela();
        var direita = new Grid();
        direita.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        direita.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        direita.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(cartaoSaidas, 2);
        direita.Children.Add(cartaoAjuste);
        direita.Children.Add(cartaoSaidas);
        var meio = K.Grade("540 *", 12, cartaoAr, direita);

        var cartaoGrafico = MontarGrafico();
        var raiz = K.Col(12, meio, cartaoGrafico);
        raiz.Margin = new Thickness(20, 12, 20, 20);
        Corpo();
        return raiz;
    }

    string idsDiagrama = "";

    void MontarDiagrama()
    {
        bool posicionando = diagrama?.Posicionando ?? false;
        diagrama?.Parar();
        var ids = VentoinhasLigadas();
        idsDiagrama = string.Join(",", ids);
        diagrama = new DiagramaAr(ids, Cfg?.Aparencia.PosicaoFluxo) { Posicionando = posicionando };
        foreach (var s in sel) diagrama.Selecionadas.Add(s);
        diagrama.Pintar();
        diagrama.MudouSelecao += () => { sel.Clear(); foreach (var s in diagrama.Selecionadas) sel.Add(s); Sincronizar(); };
        diagrama.Moveu += (id, p) => _ = Gravar(c => c.Aparencia.PosicaoFluxo[id] = $"{p.X.ToString("0", CultureInfo.InvariantCulture)},{p.Y.ToString("0", CultureInfo.InvariantCulture)}");
        hostDiagrama.Child = diagrama;
    }

    void AlternarPosicionar()
    {
        diagrama.Posicionando = !diagrama.Posicionando;
        posicionar.TextoBotao = diagrama.Posicionando ? "Pronto" : "Posicionar";
        if (diagrama.Posicionando) App.Janela?.Aviso("Arraste as ventoinhas no desenho e clique em Pronto ao concluir.");
    }

    UIElement MontarRapido()
    {
        var itens = new List<UIElement>();
        foreach (var (id, nome) in AcoesVentoinhas.Niveis)
        {
            var tn = K.T(nome, 14, P.Texto, FontWeights.SemiBold);
            var ts = K.T(AcoesVentoinhas.Repouso(Cfg, id), 12, P.Apagado);
            var b = new Botao { Height = 54, CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 0, 10, 0), Child = K.Col(1, tn, ts) };
            ((StackPanel)b.Child).VerticalAlignment = VerticalAlignment.Center;
            b.Fundo(P.Poco);
            var nivel = id;
            b.Clique += (_, _) => _ = AcoesVentoinhas.Nivel(nivel);
            niveis.Add((id, b, tn, ts));
            itens.Add(b);
        }
        var grade = K.Iguais(4, 6, itens);
        grade.Margin = new Thickness(0, 0, 0, 10);
        fixo = new Deslizante(0, 100, 5, 60);
        fixoTxt = K.T("60%", 20, P.Texto, FontWeights.SemiBold, P.Numero);
        fixoTxt.HorizontalAlignment = HorizontalAlignment.Right;
        fixo.Movendo += v => fixoTxt.Text = $"{v:0}%";
        fixo.Mudou += v => _ = AcoesVentoinhas.Fixar(sel, (int)v);
        var linhaFixo = new Border { MinHeight = 42, BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = K.Grade("110 * 52", 10, K.T("Fixar em"), fixo, fixoTxt) };
        var devolver = Botao.Contorno("Curva da BIOS", () => _ = AcoesVentoinhas.Bios(sel));
        pararFan = Botao.Contorno("Parar", () => _ = AcoesVentoinhas.Parar(sel));
        var linhaBios = new Border { MinHeight = 42, BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = K.Grade("* 110 150", 10, K.T("Controle"), pararFan, devolver) };
        fixoNota = K.Nota("");
        fixoNota.Margin = new Thickness(0, 4, 0, 0);
        return K.Col(0, grade, linhaFixo, linhaBios, fixoNota);
    }

    UIElement MontarCompleto()
    {
        fonte = new Escolha(AcoesVentoinhas.Fontes.Select(f => (f.Id, f.Nome)), "maior");
        fonte.Escolheu += id => { var fs = AcoesVentoinhas.Fontes.First(f => f.Id == id).Fontes; _ = AcoesVentoinhas.Curva(sel, s => { s.Fontes = fs.ToList(); s.Combinar = "maior"; }); };
        var linhaFonte = K.Grade("110 *", 10, K.T("Temperatura de"), fonte);
        curva = new GraficoCurva(20, 100) { Height = 140 };
        var fundoCurva = new Border { Background = P.PocoEscuro, CornerRadius = new CornerRadius(8), Child = curva, Padding = new Thickness(0, 4, 0, 0) };
        var cols = new List<UIElement>();
        for (int i = 0; i < 5; i++)
        {
            var t = new CampoNum(null, "°C", double.NaN, 30) { HorizontalAlignment = HorizontalAlignment.Stretch, Width = double.NaN, AceitaVazio = false, ToolTip = $"Ponto {i + 1}, temperatura" };
            var p = new CampoNum(null, "%", double.NaN, 30) { HorizontalAlignment = HorizontalAlignment.Stretch, Width = double.NaN, AceitaVazio = false, ToolTip = $"Ponto {i + 1}, força" };
            int k = i;
            t.Confirmou += v => MudarPonto(k, v, null);
            p.Confirmou += v => MudarPonto(k, null, v);
            pontos.Add((t, p));
            cols.Add(K.Col(4, t, p));
        }
        var gradePontos = K.Iguais(5, 6, cols);
        suav = new CampoNum(null, "s", 90); suav.Confirmou += v => { if (v is double d) _ = AcoesVentoinhas.Curva(sel, s => s.Suavizacao = d); };
        hist = new CampoNum(null, "°C", 90); hist.Confirmou += v => { if (v is double d) _ = AcoesVentoinhas.Curva(sel, s => s.Histerese = d); };
        parar = new CampoNum(null, "°C", 90); parar.Confirmou += v => { if (v is double d) _ = AcoesVentoinhas.Curva(sel, s => s.PararAbaixo = (int)d); };
        pararLigada = new Chave();
        pararLigada.Mudou += on => _ = AcoesVentoinhas.Curva(sel, s => s.PararAbaixo = on ? (int)(parar.Tag as double? ?? 40) : null);
        linhaParar = K.LinhaAjuste(K.Col(0, K.T("Parar abaixo de"), K.T("retoma acima deste valor somado à histerese", 12, P.Apagado)), K.Grade("40 *", 10, pararLigada, parar), 150, 42);
        return K.Col(8, linhaFonte, fundoCurva, gradePontos,
            K.Nota("Temperatura em °C acima e rotação em % abaixo. As ventoinhas do gabinete variam no máximo 2% por segundo, limite do controlador da placa-mãe."),
            K.LinhaAjuste(K.Col(0, K.T("Suavização"), K.T("tempo até reduzir a rotação", 12, P.Apagado)), suav, 150, 42),
            K.LinhaAjuste(K.Col(0, K.T("Histerese"), K.T("queda de temperatura antes de reduzir", 12, P.Apagado)), hist, 150, 42),
            linhaParar);
    }

    void MudarPonto(int i, double? t, double? p)
    {
        _ = AcoesVentoinhas.Curva(sel, s =>
        {
            while (s.Curva.Count < 5) s.Curva.Add(new Ponto(s.Curva.Count > 0 ? s.Curva[^1].T + 5 : 40, 100));
            if (i >= s.Curva.Count) return;
            if (t is double tt) s.Curva[i].T = tt;
            if (p is double pp) s.Curva[i].P = pp;
            s.Curva = s.Curva.OrderBy(x => x.T).ToList();
        });
    }

    Border MontarTabela()
    {
        UIElement Cab(string t) => K.T(t, 12.5, P.Apagado);
        var cab = K.Grade("24 1.2* * * * * *", 10, new Border(), Cab("Saída"), Cab("Rotação"), Cab("Força"), Cab("Controle"), Cab("Começa em"), Cab("Máximo"));
        cab.Height = 34;
        tabela = new StackPanel();
        rodapeTabela = K.Nota("");
        calibrar = Botao.Contorno("Recalibrar", () =>
        {
            if (Est?.Tarefa is { Rodando: true }) _ = Fazer("tarefa.cancelar", null, "Cancelamento pedido.");
            else _ = AcoesVentoinhas.Calibrar(sel.Count > 0 ? sel : Est?.Ventoinhas.Where(v => v.Conectada).Select(v => v.Id) ?? Enumerable.Empty<string>());
        });
        var rodape = new Border { BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 0), Child = K.Grade("* 150", 10, rodapeTabela, calibrar) };
        return K.Cartao(K.Col(0, cab, tabela, rodape), new Thickness(16, 4, 16, 8));
    }

    void MontarLinhas(Estado e)
    {
        var mostrar = VentoinhasLigadas();
        if (mostrar.SequenceEqual(linhas.Keys)) return;
        bool primeira = linhas.Count == 0 && sel.Count == 0;
        if (primeira) foreach (var id in mostrar.Where(x => x.StartsWith("sys"))) sel.Add(id);
        linhas.Clear(); tabela.Children.Clear();
        foreach (var id in mostrar)
        {
            var info = Padroes.Ventoinhas.First(v => v.Id == id);
            var m = new Marcador { Marcado = sel.Contains(id) };
            var alvo = id;
            m.Mudou += on => { if (on) sel.Add(alvo); else sel.Remove(alvo); Sincronizar(); };
            var nome = K.Col(0, K.T(Cfg?.Ventoinhas.Saidas.FirstOrDefault(s => s.Id == id)?.Nome ?? info.Nome, 14, P.Texto, FontWeights.Medium), K.T(info.Conector, 11.5, P.Apagado));
            var rpm = K.T("–", 18, P.Texto, FontWeights.Normal, P.Numero);
            var pct = K.T("–"); var ctl = K.T("–"); var ini = K.T("–"); var max = K.T("–");
            var g = K.Grade("24 1.2* * * * * *", 10, m, nome, rpm, pct, ctl, ini, max);
            var linha = new Border { Height = 40, BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = g, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            linha.MouseLeftButtonUp += (_, _) => { m.Marcado = !m.Marcado; if (m.Marcado) sel.Add(alvo); else sel.Remove(alvo); Sincronizar(); };
            linhas[id] = (m, rpm, pct, ctl, ini, max);
            tabela.Children.Add(linha);
        }
        if (primeira) Sincronizar();
    }

    Border MontarGrafico()
    {
        grafico = new Grafico(0, 1) { Capacidade = 120, MinHeight = 90, LinhasGrade = new[] { 0.0, 0.5, 1 } };
        legenda = new StackPanel { Orientation = Orientation.Horizontal };
        var eixo = new Grid { Width = 44 };
        eixo.RowDefinitions.Add(new RowDefinition()); eixo.RowDefinitions.Add(new RowDefinition()); eixo.RowDefinitions.Add(new RowDefinition());
        var rotulos = new List<TextBlock>();
        foreach (var (txt, l, va) in new[] { ("", 0, VerticalAlignment.Top), ("", 1, VerticalAlignment.Center), ("0 rpm", 2, VerticalAlignment.Bottom) })
        {
            var t = K.T(txt, 11.5, P.Apagado); t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = va; Grid.SetRow(t, l); eixo.Children.Add(t);
            rotulos.Add(t);
        }
        eixoTopo = rotulos[0]; eixoMeio = rotulos[1];
        var corpo = K.Grade("44 *", 8, eixo, grafico);
        corpo.MinHeight = 100;
        return Cartao(null, K.Col(6, K.Grade("* auto", 10, K.Titulo("Rotação nos últimos 2 minutos"), legenda), corpo), null, 6, new Thickness(16, 12, 16, 12));
    }

    void MontarSeries(Estado e)
    {
        var ids = e.Ventoinhas.Where(v => v.Conectada).Select(v => v.Id).Take(5).ToList();
        if (ids.Count == 0 || ids.SequenceEqual(serieIds)) return;
        serieIds.Clear(); serieIds.AddRange(ids);
        grafico.Series.Clear(); legenda.Children.Clear();
        var estilos = new (Brush cor, DoubleCollection? traco)[] { (P.Texto, null), (P.Cpu, null), (P.Cpu, new DoubleCollection { 2.5, 2 }), (P.Apagado, null), (P.Gpu, null) };
        double max = 0;
        for (int i = 0; i < ids.Count; i++)
        {
            var (cor, traco) = estilos[i];
            grafico.Series.Add(new Grafico.Serie { Cor = cor, Largura = 1.8, Traco = traco });
            var amostra = new Border { Width = 16, Height = 2, Background = cor, VerticalAlignment = VerticalAlignment.Center };
            if (traco is not null) amostra = new Border { Width = 16, Height = 2, Background = new DrawingBrush(new GeometryDrawing(cor, null, new RectangleGeometry(new Rect(0, 0, 4, 2)))) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 7, 2), ViewportUnits = BrushMappingMode.Absolute }, VerticalAlignment = VerticalAlignment.Center };
            var item = K.Lin(6, amostra, K.T(e.Ventoinhas.First(v => v.Id == ids[i]).Nome, 12.5, P.Secundario));
            item.Margin = new Thickness(i == 0 ? 0 : 16, 0, 0, 0);
            legenda.Children.Add(item);
            max = Math.Max(max, e.Ventoinhas.First(v => v.Id == ids[i]).RpmMax ?? 0);
        }
        // Sem calibração a escala parte da rotação mais alta de agora.
        if (max <= 0) max = Math.Max(1000, ids.Max(id => e.Ventoinhas.First(v => v.Id == id).Rpm ?? 0) * 1.25);
        grafico.Max = Math.Ceiling(max / 100) * 100;
        eixoTopo.Text = grafico.Max.ToString("N0", K.Br);
        eixoMeio.Text = (grafico.Max / 2).ToString("N0", K.Br);
        _ = Preencher();
    }

    async Task Preencher()
    {
        long agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < serieIds.Count; i++)
        {
            var r = await App.Lig.Pedir("historico", Ligacao.Args(("sensor", "fan." + serieIds[i]), ("de", agora - 120), ("ate", agora), ("pontos", 120)));
            if (Json.Ler<SerieHistorico>(r.Dados) is { } s && i < grafico.Series.Count) { grafico.Series[i].V.Clear(); grafico.Series[i].V.AddRange(s.Valores); }
        }
        grafico.Atualizar();
    }

    // Desenho e lista mostram sempre a mesma seleção.
    void Sincronizar()
    {
        diagrama.Selecionadas.Clear();
        foreach (var s in sel) diagrama.Selecionadas.Add(s);
        diagrama.Pintar();
        foreach (var (id, l) in linhas) l.m.Marcado = sel.Contains(id);
        Corpo();
    }

    void Corpo()
    {
        corpoAjuste.Children.Clear();
        var c = Cfg;
        var nomes = sel.Select(id => c?.Ventoinhas.Saidas.FirstOrDefault(s => s.Id == id)?.Nome ?? id).ToList();
        tituloSel.Text = nomes.Count == 0 ? "Ajuste" : string.Join(", ", nomes);
        if (sel.Count == 0) { corpoAjuste.Children.Add(semSel); return; }
        corpoAjuste.Children.Add(modoCompleto ? completo : rapido);
        PreencherAjuste();
    }

    void PreencherAjuste()
    {
        var c = Cfg;
        if (c is null || sel.Count == 0) return;
        var saidas = c.Ventoinhas.Saidas.Where(s => sel.Contains(s.Id)).ToList();
        if (saidas.Count == 0) return;
        foreach (var (id, b, tn, ts) in niveis)
        {
            bool on = id == c.Ventoinhas.Nivel;
            ts.Text = AcoesVentoinhas.Repouso(c, id);
            b.Fundo(on ? P.Selecao : P.Poco); b.FundoSobre = on ? P.Selecao : P.PocoEscuro;
            tn.Foreground = on ? P.TextoSelecao : P.Texto; ts.Foreground = on ? P.SubSel : P.Apagado;
        }
        bool cpu = saidas.Any(s => s.Id == "cpu");
        int minimoCpu = Padroes.MinimoCpu(maquina?.Calibracao.GetValueOrDefault("cpu"));
        fixo.Min = cpu ? minimoCpu : 0;
        var fixos = saidas.Select(s => s.Fixo).Distinct().ToList();
        fixo.Valor = fixos[0];
        fixoTxt.Text = fixos.Count == 1 ? $"{fixos[0]}%" : "vários";
        pararFan.IsEnabled = !cpu;
        pararFan.ToolTip = cpu ? "A ventoinha do processador não pode parar. Remova-a da seleção para parar as demais." : "Fixa as selecionadas em 0%.";
        fixoNota.Text = !cpu ? "Em 0% as ventoinhas param. A rotação varia 2% por segundo, então a mudança leva alguns segundos." : $"Ventoinha do processador: mínimo de {minimoCpu}%. Ventoinhas do gabinete param em 0%.";

        var primeira = saidas[0];
        var curvaComum = saidas.All(s => Json.Texto(s.Curva) == Json.Texto(primeira.Curva));
        var fontes = saidas.Select(AcoesVentoinhas.FonteId).Distinct().ToList();
        fonte.Selecionar(fontes.Count == 1 ? fontes[0] : null);
        double? agora = Temperatura(primeira, Est);
        curva.Mostrar(curvaComum ? primeira.Curva : new List<Ponto>(), agora, CorCpu(Est?.Cpu.TempMaisQuente));
        for (int i = 0; i < 5; i++)
        {
            var p = curvaComum && i < primeira.Curva.Count ? primeira.Curva[i] : null;
            pontos[i].t.Definir(p?.T); pontos[i].p.Definir(p?.P);
            pontos[i].t.IsEnabled = pontos[i].p.IsEnabled = curvaComum && i < primeira.Curva.Count;
        }
        suav.Definir(saidas.Select(s => s.Suavizacao).Distinct().Count() == 1 ? primeira.Suavizacao : null);
        hist.Definir(saidas.Select(s => s.Histerese).Distinct().Count() == 1 ? primeira.Histerese : null);
        var pa = saidas.Select(s => s.PararAbaixo).Distinct().ToList();
        pararLigada.Ligada = pa.Count == 1 && pa[0] is not null;
        parar.Definir(pa.Count == 1 ? pa[0] : null);
        parar.Tag = (double?)(pa[0] ?? 40);
        linhaParar.Visibility = cpu ? Visibility.Collapsed : Visibility.Visible;
    }

    static double? Temperatura(SaidaVentoinha s, Estado? e)
    {
        if (e is null) return null;
        double? v = null;
        foreach (var f in s.Fontes)
        {
            double? t = f switch { "cpu" => e.Cpu.TempMaisQuente, "gpu" or "gpu.quente" => e.Gpu.TempMaisQuente, "sistema" => e.Placa.Sistema, "vrm" => e.Placa.Vrm, "chipset" => e.Placa.Chipset, "ssd" => e.Discos.Max(d => d.Temp), _ => null };
            if (t is double x) v = v is null ? x : Math.Max(v.Value, x);
        }
        return v;
    }

    protected override void Config(Config c)
    {
        PreencherAjuste();
        foreach (var (id, l) in linhas)
            if (c.Ventoinhas.Saidas.FirstOrDefault(s => s.Id == id) is { } s) l.ctl.Text = AcoesVentoinhas.Controle(s);
    }

    public override void Estado(Estado e)
    {
        MontarLinhas(e);
        MontarSeries(e);
        var m = App.Lig.Config;
        foreach (var (id, l) in linhas)
        {
            var v = e.Ventoinhas.FirstOrDefault(x => x.Id == id);
            if (v is null) continue;
            l.rpm.Text = v.Rpm is < 60 ? "parado" : K.N(v.Rpm);
            l.pct.Text = Pct(v.Porcentagem);
            if (m?.Ventoinhas.Saidas.FirstOrDefault(s => s.Id == id) is { } s) l.ctl.Text = v.Modo == "calibrando" ? "calibrando" : AcoesVentoinhas.Controle(s);
            l.max.Text = v.RpmMax is int mx ? $"{mx.ToString("N0", K.Br)} rpm" : "–";
            if (v.Defeito) l.rpm.Foreground = P.Quente; else l.rpm.Foreground = P.Texto;
        }
        _ = Calibracao(e);
        for (int i = 0; i < serieIds.Count && i < grafico.Series.Count; i++) grafico.Empurrar(i, e.Ventoinhas.FirstOrDefault(v => v.Id == serieIds[i])?.Rpm);
        grafico.Atualizar();
        if (string.Join(",", VentoinhasLigadas()) != idsDiagrama) MontarDiagrama();
        diagrama.Atualizar(e);
        if (modoCompleto && sel.Count > 0 && Cfg?.Ventoinhas.Saidas.FirstOrDefault(s => s.Id == sel.First()) is { } p)
            curva.Mostrar(curva.Pontos, Temperatura(p, e), CorCpu(e.Cpu.TempMaisQuente));
    }

    EstadoMaquina? maquina;
    long maquinaEm;

    // Do estado da máquina, lido a cada 30 s.
    async Task Calibracao(Estado e)
    {
        var t = e.Tarefa;
        if (t is { Rodando: true } && t.Nome.Contains("Calibra"))
        {
            rodapeTabela.Text = $"Calibrando: {t.Progresso * 100:0}%. {t.Etapa}";
            calibrar.TextoBotao = "Cancelar";
            return;
        }
        calibrar.TextoBotao = "Recalibrar";
        if (maquina is null || Environment.TickCount64 - maquinaEm > 30000)
        {
            maquinaEm = Environment.TickCount64;
            bool primeira = maquina is null;
            maquina = Json.Ler<EstadoMaquina>((await App.Lig.Pedir("maquina", null, 5000)).Dados);
            if (primeira && maquina is not null) PreencherAjuste();
        }
        if (maquina is null) return;
        foreach (var (id, l) in linhas)
            l.ini.Text = maquina.Calibracao.TryGetValue(id, out var cal) ? $"{cal.Inicio}%" : "–";
        var datas = maquina.Calibracao.Values.Where(c => c.Data > 0).Select(c => c.Data).ToList();
        int vazias = Padroes.Ventoinhas.Length - e.Ventoinhas.Count(v => v.Conectada);
        rodapeTabela.Text = (datas.Count > 0 ? $"Calibrados em {DateTimeOffset.FromUnixTimeMilliseconds(datas.Max()).ToLocalTime():dd/MM/yyyy}." : "Calibração sem data registrada.") + (vazias > 0 ? $" Mais {vazias} saídas sem ventoinha." : "");
    }

    public override void Escondida() => diagrama.Parar();
    public override void Descartar() => diagrama.Parar();
}
