using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas.Secoes;

sealed class Jogos : Tela
{
    protected override string Id => "jogos";
    string? exe;
    StackPanel lista = null!;
    readonly Dictionary<string, ImageSource?> icones = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> iconesEmLeitura = new(StringComparer.OrdinalIgnoreCase);
    bool pinturaIconesPendente;
    TextBlock tituloAbrir = null!;
    Botao tirar = null!;
    Escolha perfil = null!, ventoinhas = null!, luzPlano = null!;
    CorEscolha corJogo = null!;
    CampoNum limite = null!;
    UIElement colPerfil = null!, colVentoinhas = null!, colLuz = null!, colLimite = null!;
    TextBlock notaLimite = null!;
    UIElement blocoLimite = null!, linhaTodos = null!;
    Grid gradeAbrir = null!;
    readonly Border vagaCab = new(), vagaGrade = new(), vagaAtalhos = new();
    Segmentado? atalhosLimite;
    List<string> presetsLimite = new();
    double hzAtalhos = -1;
    readonly Dictionary<string, (Chave c, TextBlock desc)> chaves = new();
    Deslizante nitidez = null!;
    TextBlock nitidezTxt = null!;
    Escolha vsync = null!, texturas = null!, suavizacao = null!, tesselacao = null!;
    Botao limpar = null!;
    Chave sobreLigada = null!;
    readonly Dictionary<string, Chave> sobreItens = new();
    Grid previa = null!;
    TextBlock previaTexto = null!;
    readonly List<(string pos, Botao b)> cantos = new();
    CampoAtalho atalho = null!;
    Escolha fonte = null!;
    CorEscolha corSobre = null!;
    Chave fundo = null!;
    TextBlock notaSobre = null!;
    readonly Dictionary<string, Chave> windows = new();
    readonly Dictionary<string, Border> linhasWindows = new();
    ConfigWindows windowsLido = new();

    protected override FrameworkElement Montar()
    {
        windowsLido = AcoesJogos.WindowsAgora();
        Cabecalho("Jogos", () => "perfil aplicado ao abrir o jogo e desfeito ao fechar");
        var direita = K.Col(12, MontarAbrir(), MontarMeio());
        var corpo = K.Grade("240 *", 12, MontarLista(), direita);
        var raiz = K.Col(12, corpo);
        raiz.Margin = new Thickness(20, 12, 20, 20);
        return raiz;
    }

    Border MontarLista()
    {
        lista = new StackPanel();
        var traco = new Rectangle { Stroke = P.BordaForte, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, RadiusX = P.RaioBotao, RadiusY = P.RaioBotao };
        var txt = K.T("Adicionar jogo", 14, P.Secundario); txt.HorizontalAlignment = HorizontalAlignment.Center; txt.VerticalAlignment = VerticalAlignment.Center;
        var adicionar = new Botao { Height = P.E(40), Child = new Grid { Children = { traco, txt } }, Margin = new Thickness(0, 4, 0, 0), CornerRadius = new CornerRadius(P.RaioBotao) };
        adicionar.FundoNormal = Brushes.Transparent; adicionar.Background = Brushes.Transparent; adicionar.FundoSobre = P.Poco;
        adicionar.Clique += async (_, _) => { if (await AcoesJogos.Adicionar() is string novo) { exe = novo; Atualizar(); } };
        return K.Cartao(K.Col(4, lista, adicionar), new Thickness(8));
    }

    void PintarLista()
    {
        lista.Children.Clear();
        var c = Cfg;
        if (c is null) return;
        var itens = c.Jogos.Lista.Select(j => ((string?)j.Executavel, j.Nome, Est?.Jogo == j.Nome ? "aberto agora" : j.Perfil is null ? "sem perfil" : "perfil " + j.Perfil)).ToList();
        itens.Add((null, "Todos os jogos", "jogos sem perfil próprio"));
        foreach (var (e, nome, sub) in itens)
        {
            bool on = e == exe;
            var tn = K.T(nome, 14, on ? P.TextoSelecao : P.Texto, FontWeights.SemiBold);
            var ts = K.T(sub, 12, on ? P.SubSel : P.Apagado);
            tn.TextWrapping = ts.TextWrapping = TextWrapping.NoWrap;
            tn.TextTrimming = ts.TextTrimming = TextTrimming.CharacterEllipsis;
            var texto = new Grid { VerticalAlignment = VerticalAlignment.Center };
            texto.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            texto.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(tn, 0); Grid.SetRow(ts, 1);
            texto.Children.Add(tn); texto.Children.Add(ts);
            var icone = e is null ? null : Icone(e);
            UIElement conteudo = icone is null ? texto : K.Grade("* auto", P.E(10), texto, new Image { Source = icone, Width = P.E(28), Height = P.E(28), Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
            var b = new Botao { Height = P.E(54), CornerRadius = new CornerRadius(P.RaioBotao), Padding = new Thickness(12, 0, 12, 0), Child = conteudo, Margin = new Thickness(0, 0, 0, 4), ToolTip = nome };
            b.Fundo(on ? P.Selecao : Brushes.Transparent); b.FundoSobre = on ? P.Selecao : P.Poco;
            b.Clique += (_, _) => { exe = e; Atualizar(); };
            lista.Children.Add(b);
        }
    }

    ImageSource? Icone(string arquivo)
    {
        if (icones.TryGetValue(arquivo, out var imagem)) return imagem;
        if (iconesEmLeitura.Add(arquivo)) _ = LerIcone(arquivo);
        return null;
    }

    async Task LerIcone(string arquivo)
    {
        ImageSource? imagem = null;
        try { imagem = await Task.Run(() => IconeArquivo.Ler(arquivo)); }
        catch { }
        icones[arquivo] = imagem;
        iconesEmLeitura.Remove(arquivo);
        if (Cfg?.Jogos.Lista.Any(j => string.Equals(j.Executavel, arquivo, StringComparison.OrdinalIgnoreCase)) == true) AgendarPinturaIcones();
    }

    void AgendarPinturaIcones()
    {
        if (pinturaIconesPendente) return;
        pinturaIconesPendente = true;
        _ = PintarIconesDepois();
    }

    async Task PintarIconesDepois()
    {
        await Task.Yield();
        pinturaIconesPendente = false;
        if (Cfg is not null) PintarLista();
    }

    Border MontarAbrir()
    {
        tituloAbrir = K.Titulo("");
        tirar = Botao.Contorno("Remover da lista", () => { if (exe is string e) _ = Tirar(e); }, 30);
        tirar.Width = 120;
        perfil = new Escolha(Array.Empty<(string, string)>(), null, 36);
        perfil.Escolheu += v => _ = Jogo(j => j.Perfil = v == "" ? null : v, "Perfil do jogo atualizado.");
        ventoinhas = new Escolha(new[] { ("", "Do perfil"), ("silencioso", "Silencioso"), ("equilibrado", "Equilibrado"), ("forte", "Forte"), ("maximo", "Máximo") }, "", 36);
        ventoinhas.Escolheu += v => _ = Jogo(j => j.Ventoinhas = v == "" ? null : v, "Ventoinhas do jogo atualizadas.");
        luzPlano = new Escolha(Array.Empty<(string, string)>(), null, 36);
        luzPlano.Escolheu += v => _ = Jogo(j => j.Luz = v == "" ? null : v, "Luz do jogo atualizada.");
        corJogo = new CorEscolha("#6FC3E8", 36);
        corJogo.Escolheu += v => _ = Jogo(j => j.Cor = v, "Cor do jogo atualizada.");
        limite = new CampoNum(null, "", double.NaN, 36) { Width = double.NaN };
        limite.Confirmou += v => _ = Limite(v);
        UIElement Coluna(string rotulo, UIElement controle) => K.Col(5, K.T(rotulo, 12.5, P.Apagado), controle);
        colPerfil = Coluna("Perfil do PC", perfil);
        colVentoinhas = Coluna("Ventoinhas", ventoinhas);
        colLuz = Coluna("Luz", K.Grade("* auto", 6, luzPlano, corJogo));
        // O mesmo campo muda de lugar: na grade do jogo escolhido, ou ao lado dos atalhos quando vale para todos os jogos.
        blocoLimite = K.Grade("* auto", 6, limite, K.T("por segundo", 13, P.Apagado));
        colLimite = Coluna("Limite de quadros", vagaGrade);
        vagaCab.Width = 170; vagaCab.VerticalAlignment = VerticalAlignment.Top;
        var ou = K.T("ou", 13, P.Apagado); ou.VerticalAlignment = VerticalAlignment.Top; ou.Margin = new Thickness(0, 8, 0, 0);
        linhaTodos = K.Col(6, K.T("Limite de quadros", 12.5, P.Apagado), K.Grade("auto auto auto", 12, vagaAtalhos, ou, vagaCab));
        notaLimite = K.Nota("");
        gradeAbrir = K.Iguais(4, 12, new[] { colPerfil, colVentoinhas, colLuz, colLimite });
        tituloAbrir.VerticalAlignment = VerticalAlignment.Center;
        return K.Cartao(K.Col(10, K.Grade("* auto", 10, tituloAbrir, tirar), linhaTodos, gradeAbrir, notaLimite));
    }

    // Os atalhos dependem da taxa da tela: refeitos só quando ela muda.
    void MontarAtalhos(double hz)
    {
        if (hz == hzAtalhos) return;
        hzAtalhos = hz;
        int tela = (int)Math.Round(hz), abaixo = Math.Max(30, tela - 3);
        var ops = new List<(string Valor, string Texto, string Desc)> { ("", "Sem limite", "desempenho máximo"), ("60", "60", "menor consumo e ruído") };
        if (tela > 63)
        {
            ops.Add((abaixo.ToString(), abaixo.ToString(), "abaixo da taxa da tela, recomendado com FreeSync"));
            ops.Add((tela.ToString(), tela.ToString(), $"igual à taxa da tela, {tela} Hz"));
        }
        presetsLimite = ops.Select(o => o.Valor).ToList();
        const double largura = 118;
        atalhosLimite = new Segmentado(ops.Select(o => (o.Valor, o.Texto)), 34, largura);
        atalhosLimite.Escolheu += v => _ = Limite(v.Length == 0 ? null : double.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
        var desc = new Grid { Margin = new Thickness(3, 0, 3, 0) };
        for (int i = 0; i < ops.Count; i++)
        {
            desc.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largura) });
            if (i < ops.Count - 1) desc.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
            var t = K.T(ops[i].Desc, 12, P.Apagado); t.TextWrapping = TextWrapping.Wrap; t.TextAlignment = TextAlignment.Center; t.VerticalAlignment = VerticalAlignment.Top; t.Margin = new Thickness(4, 0, 4, 0);
            Grid.SetColumn(t, i * 2); desc.Children.Add(t);
        }
        vagaAtalhos.Child = K.Col(6, atalhosLimite, desc);
    }

    async Task Tirar(string e)
    {
        if (await AcoesJogos.Remover(e)) { exe = null; Atualizar(); }
    }

    Task<bool> Jogo(Action<PerfilJogo> mudar, string ok) => exe is string e ? AcoesJogos.Jogo(e, mudar, ok) : Task.FromResult(false);

    Task<bool> Limite(double? v)
    {
        int? l = v is double d ? (int)Math.Round(d) : null;
        if (exe is null) return Gravar(c => c.Jogos.LimiteQuadros = l, l is null ? "Sem limite de quadros." : $"Limite de {l} quadros por segundo.");
        return Jogo(j => j.LimiteQuadros = l, l is null ? "O jogo usa o limite geral." : $"Limite de {l} quadros por segundo neste jogo.");
    }

    UIElement MontarMeio()
    {
        // A sobreposição é alta: o Windows para jogos ocupa o espaço que sobra embaixo dos ajustes da AMD.
        var g = K.Grade("1.5* 1*", 12, K.Col(12, MontarAmd(), MontarWindows()), MontarSobreposicao());
        foreach (UIElement x in g.Children) if (x is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Top;
        return g;
    }

    static Border Linha(UIElement conteudo, double altura = 50) => new() { MinHeight = P.E(altura), BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = conteudo };

    Border MontarAmd()
    {
        var esq = new StackPanel();
        foreach (var (chave, nome, desc) in AcoesJogos.Chaves)
        {
            var c = new Chave();
            var d = K.T(desc, 12, P.Apagado);
            var k = chave;
            c.Mudou += on => _ = Alternar(k, on);
            chaves[chave] = (c, d);
            var g = K.Grade("* 40", 10, K.Col(0, K.T(nome), d), c);
            g.VerticalAlignment = VerticalAlignment.Center;
            esq.Children.Add(Linha(g));
        }
        var dir = new StackPanel();
        nitidez = new Deslizante(0, 100, 1, 0);
        nitidezTxt = K.T("", 12, P.Apagado);
        nitidez.Movendo += v => nitidezTxt.Text = v < 1 ? "desligada" : $"{v:0}%";
        nitidez.Mudou += v => _ = Nitidez(v);
        dir.Children.Add(Linha(Centro(K.Grade("* 120", 10, K.Col(0, K.T("Nitidez"), nitidezTxt), nitidez))));
        vsync = Lista("Sincronia vertical", dir, new[] { ("desligado-salvo-app", "Do jogo"), ("sempre-desligado", "Sempre desligada"), ("sempre-ligado", "Sempre ligada") },
            v => g => g.Vsync = v);
        texturas = Lista("Filtro de texturas", dir, new[] { ("", "Do jogo"), ("2", "2x"), ("4", "4x"), ("8", "8x"), ("16", "16x") },
            v => g => { g.Anisotropica = v != ""; if (v != "") g.AnisotropicaNivel = int.Parse(v); });
        suavizacao = Lista("Suavização de bordas", dir, new[] { ("app", "Do jogo"), ("2", "2x"), ("4", "4x"), ("8", "8x") },
            v => g => { g.Suavizacao = v == "app" ? "app" : "substituir"; if (v != "app") g.SuavizacaoNivel = v; });
        tesselacao = Lista("Tesselação", dir, new[] { ("app", "Do jogo"), ("amd", "Pela AMD") },
            v => g => g.Tesselacao = v);
        limpar = Botao.Contorno("Limpar", () => _ = Fazer("jogos.limparCache", null, "Cache de sombreadores limpo. Os jogos recompilam o cache na próxima abertura."), 32);
        dir.Children.Add(Linha(Centro(K.Grade("* 120", 10, Rotulo("Cache de sombreadores"), limpar))));
        var colunas = K.Grade("* *", 24, esq, dir);
        foreach (UIElement x in colunas.Children) if (x is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Top;
        return K.Cartao(K.Col(6, K.Titulo("Ajustes gráficos da AMD"), colunas), new Thickness(16, 14, 16, 6));
    }

    static Grid Centro(Grid g) { g.VerticalAlignment = VerticalAlignment.Center; return g; }

    static TextBlock Rotulo(string s) { var t = K.T(s); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.VerticalAlignment = VerticalAlignment.Center; return t; }

    Escolha Lista(string nome, Panel destino, (string, string)[] ops, Func<string, Action<Ajustes3D>> mudar)
    {
        var e = new Escolha(ops, null, 32);
        e.Escolheu += v => _ = AcoesJogos.Graficos(exe, mudar(v), $"{nome}: {ops.First(o => o.Item1 == v).Item2.ToLowerInvariant()}.");
        destino.Children.Add(Linha(Centro(K.Grade("* 120", 10, Rotulo(nome), e))));
        return e;
    }

    // Chill não convive com Anti-Lag e Boost no driver: ligar um desliga o outro na mesma gravação.
    Task<bool> Alternar(string chave, bool on) => AcoesJogos.Graficos(exe, g =>
    {
        AcoesJogos.Chave(g, chave, on);
        if (on && chave == "chill") { g.AntiLag = false; g.Boost = false; }
        if (on && chave is "antiLag" or "boost") g.Chill = false;
    }, $"{AcoesJogos.Chaves.First(x => x.Chave == chave).Nome} {(on ? "ligado" : "desligado")}.");

    Task<bool> Nitidez(double v)
    {
        var f = Fx?.Campos.GetValueOrDefault("graficos.nitidezNivel");
        int nivel = (int)Math.Round(Math.Max(v, f?.Min ?? 0));
        return AcoesJogos.Graficos(exe, g => { g.Nitidez = v >= 1; if (v >= 1) g.NitidezNivel = nivel; }, v < 1 ? "Nitidez desligada." : $"Nitidez em {nivel}%.");
    }

    Border MontarSobreposicao()
    {
        sobreLigada = new Chave();
        sobreLigada.Mudou += on => _ = Gravar(c => c.Jogos.Sobreposicao.Ligada = on, on ? "Sobreposição ligada." : "Sobreposição desligada.");
        var cab = K.Grade("* 40", 10, K.Titulo("Sobreposição no jogo"), sobreLigada);
        var paisagem = new Path { Data = Geometry.Parse("M0 120 L80 96 L150 110 L230 84 L320 100 L320 180 L0 180 Z"), Fill = P.Poco, Stretch = Stretch.Fill };
        previaTexto = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Foreground = Brushes.White, LineHeight = 15 };
        var caixa = new Border { Background = new SolidColorBrush(Color.FromArgb(200, 6, 8, 10)), CornerRadius = new CornerRadius(4), Padding = new Thickness(9, 6, 9, 6), Child = previaTexto };
        previa = new Grid { ClipToBounds = true, Children = { new Border { Child = paisagem, VerticalAlignment = VerticalAlignment.Bottom, Height = double.NaN }, caixa } };
        var moldura = new Border { Background = P.Profundo, BorderBrush = P.Borda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = previa, ClipToBounds = true };
        moldura.SizeChanged += (_, e) => { if (Math.Abs(moldura.Height - e.NewSize.Width * 9 / 16) > 1) moldura.Height = e.NewSize.Width * 9 / 16; };
        paisagem.Height = double.NaN;
        previa.SizeChanged += (_, e) => paisagem.Height = e.NewSize.Height * 0.45;
        var itens = new List<UIElement>();
        foreach (var (leitura, nome) in AcoesJogos.Sobreposicao)
        {
            var c = new Chave();
            var l = leitura;
            c.Mudou += on => _ = Gravar(cf =>
            {
                var s = cf.Jogos.Sobreposicao.Leituras;
                if (on && !s.Contains(l)) s.Add(l); else if (!on) s.Remove(l);
                var ordem = AcoesJogos.Sobreposicao.Select(x => x.Leitura).ToList();
                cf.Jogos.Sobreposicao.Leituras = s.OrderBy(x => ordem.IndexOf(x) is int i && i >= 0 ? i : 99).ToList();
            });
            sobreItens[leitura] = c;
            var g = K.Grade("* 40", 8, Rotulo(nome), c); g.VerticalAlignment = VerticalAlignment.Center;
            itens.Add(Linha(g, 40));
        }
        var gradeItens = K.DuasColunas(itens, 18);
        var posicoes = new Grid { Width = 96, Height = 54 };
        var quadro = new Border { BorderBrush = P.BordaForte, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(3), Child = posicoes };
        posicoes.ColumnDefinitions.Add(new ColumnDefinition()); posicoes.ColumnDefinitions.Add(new ColumnDefinition());
        posicoes.RowDefinitions.Add(new RowDefinition()); posicoes.RowDefinitions.Add(new RowDefinition());
        var nomes = new[] { ("superior-esquerda", "em cima à esquerda"), ("superior-direita", "em cima à direita"), ("inferior-esquerda", "embaixo à esquerda"), ("inferior-direita", "embaixo à direita") };
        for (int i = 0; i < 4; i++)
        {
            var (pos, rot) = nomes[i];
            var b = new Botao { CornerRadius = new CornerRadius(2), Margin = new Thickness(1.5), ToolTip = "Sobreposição " + rot };
            Grid.SetColumn(b, i % 2); Grid.SetRow(b, i / 2);
            b.Clique += (_, _) => _ = Gravar(c => c.Jogos.Sobreposicao.Posicao = pos, "Sobreposição " + rot + ".");
            cantos.Add((pos, b));
            posicoes.Children.Add(b);
        }
        quadro.Width = 96; quadro.HorizontalAlignment = HorizontalAlignment.Right;
        atalho = new CampoAtalho(30) { AceitaVazio = true };
        atalho.Gravou += a => _ = Gravar(c => c.Jogos.Sobreposicao.Atalho = a ?? "", a is null ? "Sobreposição sem atalho." : $"Atalho da sobreposição: {a.Replace("+", " ")}.");
        fonte = new Escolha(AcoesJogos.Fontes, null, 30);
        fonte.Escolheu += v => _ = Gravar(c => c.Jogos.Sobreposicao.Fonte = v, $"Fonte da sobreposição: {v}.");
        corSobre = new CorEscolha("#FF8000", P.E(30)) { Width = P.E(30) };
        corSobre.Escolheu += v => _ = Gravar(c => c.Jogos.Sobreposicao.Cor = v, "Cor da sobreposição atualizada.");
        fundo = new Chave();
        fundo.Mudou += on => _ = Gravar(c => c.Jogos.Sobreposicao.Fundo = on, on ? "Sobreposição com sombra no texto." : "Sobreposição sem sombra.");
        var rivaTuner = Botao.Contorno("Abrir", () => _ = AcoesJogos.TelaRivaTuner(), 30);
        rivaTuner.Width = 96;
        notaSobre = K.Nota("");
        return K.Cartao(K.Col(10, cab, moldura, gradeItens,
            Linha(Centro(K.Grade("* 96", 10, K.T("Posição na tela"), quadro)), 46),
            Linha(Centro(K.Grade("* 96", 10, K.T("Atalho para mostrar e esconder"), atalho)), 46),
            Linha(Centro(K.Grade("* 120 auto", 8, K.T("Letra"), fonte, corSobre)), 46),
            Linha(Centro(K.Grade("* 40", 10, K.T("Sombra no texto"), fundo)), 46),
            Linha(Centro(K.Grade("* 96", 10, K.T("Configurações do RivaTuner"), rivaTuner)), 46),
            notaSobre), new Thickness(16, 14, 16, 10));
    }

    Border MontarWindows()
    {
        var itens = new List<UIElement>();
        foreach (var (campo, nome, desc) in AcoesJogos.Windows)
        {
            var c = new Chave();
            var cp = campo;
            c.Mudou += on => _ = Gravar(cf => DefinirWindows(cf.Windows, cp, on), $"{nome}: {(on ? "ligado" : "desligado")}.");
            windows[campo] = c;
            var g = K.Grade("* 40", 10, K.Col(0, K.T(nome), K.T(desc, 12, P.Apagado)), c);
            g.VerticalAlignment = VerticalAlignment.Center;
            var linha = Linha(g);
            linhasWindows[campo] = linha;
            itens.Add(linha);
        }
        return K.Cartao(K.Col(6, itens.Prepend(K.Titulo("Windows para jogos")).ToArray()), new Thickness(16, 14, 16, 6));
    }

    static void DefinirWindows(ConfigWindows w, string campo, bool v)
    {
        switch (campo)
        {
            case "modoJogo": w.ModoJogo = v; break;
            case "agendamentoGpu": w.AgendamentoGpu = v; break;
            case "otimizacaoJanela": w.OtimizacaoJanela = v; break;
            case "vrrJanela": w.VrrJanela = v; break;
            case "gravacaoFundo": w.GravacaoFundo = v; break;
            case "precisaoPonteiro": w.PrecisaoPonteiro = v; break;
            case "transparencia": w.Transparencia = v; break;
            case "naoPerturbeEmJogo": w.NaoPerturbeEmJogo = v; break;
        }
    }

    protected override void Config(Config c)
    {
        if (exe is not null && c.Jogos.Lista.All(j => j.Executavel != exe)) exe = null;
        Atualizar();
    }

    public override void NovasFaixas(Faixas f) => Atualizar();

    public override void Mostrada()
    {
        windowsLido = AcoesJogos.WindowsAgora();
        if (Cfg is not null) Atualizar();
        base.Mostrada();
    }

    void Atualizar()
    {
        var c = Cfg;
        if (c is null) return;
        PintarLista();
        var j = exe is null ? null : c.Jogos.Lista.FirstOrDefault(x => x.Executavel == exe);
        tituloAbrir.Text = j is null ? "Em qualquer jogo sem perfil próprio" : "Ao abrir " + j.Nome;
        bool todos = j is null;
        tirar.Visibility = gradeAbrir.Visibility = todos ? Visibility.Collapsed : Visibility.Visible;
        linhaTodos.Visibility = todos ? Visibility.Visible : Visibility.Collapsed;
        var vaga = todos ? vagaCab : vagaGrade;
        if (vaga.Child != blocoLimite) { vagaCab.Child = null; vagaGrade.Child = null; vaga.Child = blocoLimite; }
        if (j is not null)
        {
            perfil.Opcoes(c.Perfis.Select(p => (p.Nome, p.Nome)).Append(("", "Não muda")), j.Perfil ?? "");
            ventoinhas.Selecionar(j.Ventoinhas ?? "");
            luzPlano.Opcoes(c.Luz.Planos.Select(p => (p.Nome, p.Nome)).Prepend(("", "Não muda")), j.Luz ?? "");
            corJogo.Visibility = j.Luz == "Jogo" ? Visibility.Visible : Visibility.Collapsed;
            corJogo.Definir(j.Cor ?? c.Luz.Planos.FirstOrDefault(p => p.Nome == "Jogo")?.Efeito.Cores.FirstOrDefault() ?? "#6FC3E8");
            limite.Definir(j.LimiteQuadros ?? c.Jogos.LimiteQuadros);
        }
        var tela = Fx?.Telas.FirstOrDefault();
        var aplica = Fx?.RivaTuner == true ? "Limite aplicado pelo RivaTuner." : "Limite aplicado pelo driver da AMD (RivaTuner não instalado).";
        if (todos)
        {
            MontarAtalhos(tela?.Hz ?? 0);
            var atual = c.Jogos.LimiteQuadros?.ToString() ?? "";
            atalhosLimite?.Selecionar(atual);
            // Valor de atalho já aparece marcado nele; o campo fica para o valor fora dos atalhos.
            limite.Definir(presetsLimite.Contains(atual) ? null : c.Jogos.LimiteQuadros);
            notaLimite.Text = aplica;
        }
        else
        {
            var hz = tela is null ? "" : $"Tela de {tela.Hz:0} Hz. ";
            notaLimite.Text = hz + "Campo vazio: limite geral. " + aplica;
        }

        var f = Fx;
        foreach (var (chave, nome, desc) in AcoesJogos.Chaves)
        {
            var (ch, d) = chaves[chave];
            bool disp = AcoesJogos.Disponivel(f, chave);
            ch.Ligada = AcoesJogos.Efetivo(c, j, chave, f);
            ch.IsEnabled = disp;
            d.Text = disp || f is null ? desc : f.Campos.GetValueOrDefault("graficos." + chave)?.Motivo ?? "o driver não aceita";
        }
        var g = Efetivos(c, j);
        var fn = f?.Campos.GetValueOrDefault("graficos.nitidezNivel");
        nitidez.Max = fn is { Disponivel: true } ? fn.Max : 100;
        bool nitOn = g.Nitidez ?? f?.Campos.GetValueOrDefault("graficos.nitidez")?.Atual == 1;
        double nv = nitOn ? g.NitidezNivel ?? fn?.Atual ?? 0 : 0;
        nitidez.Valor = nv;
        nitidezTxt.Text = nv < 1 ? "desligada" : $"{nv:0}%";
        nitidez.IsEnabled = f?.Campos.GetValueOrDefault("graficos.nitidez") is { Disponivel: true };
        vsync.Selecionar(g.Vsync ?? (f?.Campos.GetValueOrDefault("graficos.vsync")?.Atual switch { 0 => "sempre-desligado", 3 => "sempre-ligado", _ => "desligado-salvo-app" }));
        vsync.IsEnabled = f?.Campos.GetValueOrDefault("graficos.vsync") is { Disponivel: true };
        bool af = g.Anisotropica ?? f?.Campos.GetValueOrDefault("graficos.anisotropica")?.Atual == 1;
        var afNivel = g.AnisotropicaNivel ?? (int?)f?.Campos.GetValueOrDefault("graficos.anisotropicaNivel")?.Atual ?? 16;
        texturas.Selecionar(af ? afNivel.ToString() : "");
        texturas.IsEnabled = f?.Campos.GetValueOrDefault("graficos.anisotropica") is { Disponivel: true };
        var aa = g.Suavizacao ?? (f?.Campos.GetValueOrDefault("graficos.suavizacao")?.Atual is double am && am > 0 ? "substituir" : "app");
        suavizacao.Selecionar(aa == "app" ? "app" : g.SuavizacaoNivel ?? "4");
        suavizacao.IsEnabled = f?.Campos.GetValueOrDefault("graficos.suavizacao") is { Disponivel: true };
        tesselacao.Selecionar(g.Tesselacao ?? (f?.Campos.GetValueOrDefault("graficos.tesselacao")?.Atual == 1 ? "app" : "amd"));
        tesselacao.IsEnabled = f?.Campos.GetValueOrDefault("graficos.tesselacao") is { Disponivel: true };
        limpar.IsEnabled = f?.Campos.GetValueOrDefault("graficos.limparCache") is { Disponivel: true };

        PintarSobre();
        foreach (var (campo, _, _) in AcoesJogos.Windows)
            windows[campo].Ligada = AcoesJogos.ValorWindows(c.Windows, campo) ?? AcoesJogos.ValorWindows(windowsLido, campo) ?? false;
        // O Windows só oferece a taxa variável em janela com uma tela de taxa variável ligada.
        linhasWindows["vrrJanela"].Visibility = f?.Telas.Any(t => t.Suporta.Contains("freesync")) == true ? Visibility.Visible : Visibility.Collapsed;
    }

    static Ajustes3D Efetivos(Config c, PerfilJogo? j)
    {
        var r = Json.Copia(c.Jogos.Global);
        if (j?.Graficos is not { } g) return r;
        r.Nitidez = g.Nitidez ?? r.Nitidez; r.NitidezNivel = g.NitidezNivel ?? r.NitidezNivel;
        r.Vsync = g.Vsync ?? r.Vsync;
        r.Anisotropica = g.Anisotropica ?? r.Anisotropica; r.AnisotropicaNivel = g.AnisotropicaNivel ?? r.AnisotropicaNivel;
        r.Suavizacao = g.Suavizacao ?? r.Suavizacao; r.SuavizacaoNivel = g.SuavizacaoNivel ?? r.SuavizacaoNivel;
        r.Tesselacao = g.Tesselacao ?? r.Tesselacao;
        return r;
    }

    void PintarSobre()
    {
        var c = Cfg;
        if (c is null) return;
        var s = c.Jogos.Sobreposicao;
        sobreLigada.Ligada = s.Ligada;
        foreach (var (l, ch) in sobreItens) ch.Ligada = s.Leituras.Contains(l);
        foreach (var (pos, b) in cantos) b.Fundo(pos == s.Posicao ? P.Texto : P.Borda);
        atalho.Definir(s.Atalho);
        fonte.Selecionar(s.Fonte);
        corSobre.Definir(s.Cor);
        fundo.Ligada = s.Fundo;
        var caixa = (Border)previa.Children[1];
        AcoesJogos.PintarPrevia(s, previaTexto, caixa);
        caixa.Visibility = s.Ligada ? Visibility.Visible : Visibility.Hidden;
        caixa.HorizontalAlignment = s.Posicao.EndsWith("direita") ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        caixa.VerticalAlignment = s.Posicao.StartsWith("inferior") ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        caixa.Margin = new Thickness(12, 10, 12, 10);
        notaSobre.Text = Fx?.RivaTuner == false ? "Sobreposição indisponível: RivaTuner não instalado." : "";
        notaSobre.Visibility = notaSobre.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        MontarPrevia(s);
    }

    void MontarPrevia(Sobreposicao s) => Previa(Est);

    void Previa(Estado? e)
    {
        if (Cfg?.Jogos.Sobreposicao is { } s) previaTexto.Text = AcoesJogos.TextoPrevia(s, e);
    }

    string? jogoAberto;
    public override void Estado(Estado e)
    {
        Previa(e);
        if (e.Jogo != jogoAberto) { jogoAberto = e.Jogo; PintarLista(); }
    }
}
