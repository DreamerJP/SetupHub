using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas.Secoes;

sealed class Processador : Tela
{
    protected override string Id => "processador";
    Medidor medidor = null!;
    Run temp = null!, consumo = null!, ccd = null!, tensao = null!, memoria = null!;
    TextBlock limite = null!, consumoDe = null!, memoriaDe = null!;
    Grid nucleosGrade = null!;
    readonly List<(Border trilho, Border barra, TextBlock ghz)> nucleos = new();
    const double AlturaBarra = 52;
    ModoProcessador modo = null!;
    ContentControl corpo = null!;
    UIElement rapido = null!, completo = null!;
    bool modoCompleto;
    TextBlock assistTxt = null!;
    Botao assistBotao = null!;
    // Rascunho do modo completo: só vai para o processador ao clicar em Aplicar.
    ConfigProcessador rascunho = new();
    readonly List<(string campo, CampoNum c, TextBlock rot)> campos = new();
    readonly List<CampoNum> coCampos = new();
    ContentControl coGrade = null!;
    Run coFaixa = null!;
    CampoNum coTodos = null!;
    Chave pbo = null!;
    Border linhaPbo = null!;
    TextBlock aviso = null!, smtTxt = null!, x3dTxt = null!, parkTxt = null!;
    Botao aplicar = null!;
    string? memoriaInfo;

    static readonly (string Campo, string Nome, string Unidade)[] Limites =
    {
        ("ppt", "Limite de consumo (PPT)", "W"), ("tdc", "Corrente contínua (TDC)", "A"), ("edc", "Corrente de pico (EDC)", "A"),
        ("limiteTemperatura", "Limite de temperatura", "°C"), ("fmaxDeslocamento", "Aumento do clock máximo", "MHz"),
    };

    protected override FrameworkElement Montar()
    {
        Cabecalho("Processador", () => Est is { } e ? $"{Inicio.Simplificar(e.Cpu.Nome)}, {e.Cpu.Nucleos.Count} núcleos e {Environment.ProcessorCount} linhas" : "");
        var agora = MontarAgora();
        modo = new ModoProcessador("Modo", 54);
        var linha1 = new Fluxo();
        linha1.Children.Add(Fluxo.Item(agora, 560, 3));
        linha1.Children.Add(Fluxo.Item(modo.Cartao, 240, 1));
        var rc = RapidoCompleto(c => { modoCompleto = c; corpo.Content = c ? completo : rapido; }, out modoCompleto);
        rc.HorizontalAlignment = HorizontalAlignment.Left;
        var linha2 = K.Grade("auto *", 10, rc, K.Nota("Faixas e padrões lidos do próprio processador. Valor fora da faixa não é gravado."));
        ((TextBlock)linha2.Children[1]).TextAlignment = TextAlignment.Right;
        corpo = new ContentControl();
        rapido = MontarAssistente();
        completo = MontarCompleto();
        corpo.Content = modoCompleto ? completo : rapido;
        var raiz = K.Col(12, linha1, linha2, corpo);
        raiz.Margin = new Thickness(20, 12, 20, 20);
        return raiz;
    }

    UIElement Leitura(string rotulo, out Run valor, out TextBlock t, string unidade)
    {
        t = new TextBlock();
        valor = new Run("–") { FontFamily = P.Numero, FontSize = 24, FontWeight = FontWeights.SemiBold };
        t.Inlines.Add(valor);
        t.Inlines.Add(new Run(" " + unidade) { FontSize = 12.5, Foreground = P.Apagado });
        return K.Col(0, K.T(rotulo, 12.5, P.Apagado), t);
    }

    Border MontarAgora()
    {
        medidor = new Medidor(P.Cpu) { Width = 132, Height = 124 };
        var med = K.Mostrador(medidor, 44, P.Cpu, out temp, out limite, "limite não lido");
        med.VerticalAlignment = VerticalAlignment.Center;
        var leituras = new Fluxo { Espaco = 22, EsticarAltura = false };
        leituras.Children.Add(Fluxo.Item(Leitura("Consumo", out consumo, out consumoDe, "W"), 120));
        leituras.Children.Add(Fluxo.Item(Leitura("Temperatura do CCD", out ccd, out _, "°C"), 130));
        leituras.Children.Add(Fluxo.Item(Leitura("Tensão do núcleo", out tensao, out _, "V"), 120));
        leituras.Children.Add(Fluxo.Item(Leitura("Memória", out memoria, out memoriaDe, "MT/s"), 170));
        nucleosGrade = new Grid();
        var direita = K.Col(16, leituras, K.Col(6, K.T("Clock por núcleo", 12.5, P.Apagado), nucleosGrade));
        direita.VerticalAlignment = VerticalAlignment.Center;
        return K.Cartao(K.Grade("132 *", 24, med, direita));
    }

    void MontarNucleos(int n)
    {
        if (nucleos.Count == n) return;
        nucleos.Clear(); nucleosGrade.Children.Clear(); nucleosGrade.ColumnDefinitions.Clear();
        for (int i = 0; i < n; i++)
        {
            nucleosGrade.ColumnDefinitions.Add(new ColumnDefinition());
            if (i < n - 1) nucleosGrade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            var ghz = K.T("–", 11.5, P.Secundario); ghz.HorizontalAlignment = HorizontalAlignment.Center;
            var barra = new Border { Background = P.Cpu, VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
            var trilho = new Border { Height = AlturaBarra, Background = P.Poco, CornerRadius = new CornerRadius(3), Child = barra, ClipToBounds = true, Margin = new Thickness(0, 3, 0, 3) };
            var nome = K.T(i.ToString(), 11, P.Apagado); nome.HorizontalAlignment = HorizontalAlignment.Center;
            var col = new StackPanel { Children = { ghz, trilho, nome } };
            Grid.SetColumn(col, i * 2);
            nucleosGrade.Children.Add(col);
            nucleos.Add((trilho, barra, ghz));
        }
    }

    UIElement MontarAssistente()
    {
        assistTxt = new TextBlock
        {
            Text = "Clock mais alto sustentado, com menos calor no mesmo consumo. Reduz a tensão de um núcleo por vez em etapas de 5, testa com o Prime95 fixado nesse núcleo e grava uma etapa acima da última aprovada, como margem de segurança. Outros programas podem ficar lentos durante o teste.",
            Foreground = P.Secundario, TextWrapping = TextWrapping.Wrap, FontSize = P.F(14), LineHeight = P.F(19.6),
        };
        assistBotao = Botao.Cheio("Iniciar ajuste automático", () => _ = Assistente(), 38);
        assistBotao.Width = 200;
        var f = new Fluxo { Espaco = 16, EsticarAltura = false };
        f.Children.Add(Fluxo.Item(K.Col(4, K.Titulo("Assistente de Curve Optimizer"), assistTxt), 420, 1));
        f.Children.Add(Fluxo.Item(assistBotao, 200, 0));
        return K.Cartao(f, new Thickness(16));
    }

    async Task Assistente()
    {
        if (Est?.Tarefa is { Rodando: true }) { await Fazer("tarefa.cancelar", null, "Cancelamento solicitado. Os ajustes anteriores serão restaurados."); return; }
        await Fazer("processador.assistente", Ligacao.Args(("piso", -30), ("segundos", 120)), "Ajuste automático iniciado.");
    }

    UIElement MontarCompleto()
    {
        var lim = new StackPanel();
        foreach (var (campo, nome, un) in Limites)
        {
            var rot = K.T("", 12, P.Apagado);
            var titulo = new TextBlock { TextWrapping = TextWrapping.Wrap };
            titulo.Inlines.Add(new Run(nome + " ") { Foreground = P.Texto, FontSize = P.F(14) });
            var c = new CampoNum(null, "", 76, 32);
            var nomeCampo = campo;
            c.Confirmou += v => Mudar(nomeCampo, v);
            var linha = K.Grade("* 76 30", 8, K.Col(0, titulo, rot), c, K.T(un, 12.5, P.Apagado));
            linha.MinHeight = 34; linha.Margin = new Thickness(0, 8, 0, 0);
            campos.Add((campo, c, rot));
            lim.Children.Add(linha);
        }
        pbo = new Chave();
        pbo.Mudou += on => { rascunho.PboEscala = on; Atualizar(); };
        linhaPbo = K.LinhaAjuste(K.Col(0, K.T("Escala do PBO"), K.T("mantém o clock alto por mais tempo", 12, P.Apagado)), pbo, 40, 44);
        aviso = K.Nota("Campos com borda clara diferem do padrão.");
        var cartaoLim = K.Cartao(K.Col(8, K.Titulo("Limites do PBO"), lim, linhaPbo, aviso));

        coGrade = new ContentControl();
        coTodos = new CampoNum(null, "", 76, 32);
        coTodos.Confirmou += v => { if (v is double d && Dentro("coTodos", d, "Curve Optimizer")) { rascunho.CoTodos = (int)d; rascunho.CoNucleos = new(); Atualizar(); } };
        var coTitulo = new TextBlock();
        coTitulo.Inlines.Add(new Run("Curve Optimizer ") { FontWeight = FontWeights.SemiBold, FontSize = P.F(15) });
        coFaixa = new Run("") { FontSize = P.F(12.5), Foreground = P.Apagado };
        coTitulo.Inlines.Add(coFaixa);
        var cartaoCo = K.Cartao(K.Col(8, coTitulo, coGrade,
            K.LinhaAjuste("Definir todos os núcleos em", K.Grade("76 30", 8, coTodos, new Border()), 114, 40)));

        smtTxt = K.T("–", 14, P.Secundario); x3dTxt = K.T("–", 14, P.Secundario); parkTxt = K.T("–", 14, P.Secundario);
        Border Info(string rotulo, TextBlock valor) => new() { Height = 40, BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = K.Grade("* auto", 10, K.T(rotulo, 13.5), valor) };
        var mudarSmt = Botao.Contorno("Alterar SMT na próxima reinicialização", () => _ = TrocarSmt());
        mudarSmt.Height = 34;
        var cartaoNuc = K.Cartao(K.Col(10, K.Titulo("Núcleos"), Info("SMT, 2 linhas por núcleo", smtTxt), Info("X3D Gaming Mode", x3dTxt), Info("Núcleos estacionados agora", parkTxt), mudarSmt));

        var cartoes = new Fluxo();
        cartoes.Children.Add(Fluxo.Item(cartaoLim, 340));
        cartoes.Children.Add(Fluxo.Item(cartaoCo, 340));
        cartoes.Children.Add(Fluxo.Item(cartaoNuc, 260));
        aplicar = Botao.Cheio("Aplicar", () => _ = Aplicar(), 38);
        aplicar.IsEnabled = false;
        var voltar = Botao.Contorno("Voltar ao padrão de fábrica", () => _ = Fazer("processador.padrao", null, "Processador restaurado ao padrão de fábrica."), 38);
        ((TextBlock)voltar.Child).Foreground = P.Texto;
        var rodape = K.Grade("200 200 *", 10, aplicar, voltar, K.Nota("Ajuste novo só é reaplicado ao ligar o PC depois de confirmado estável."));
        return K.Col(12, cartoes, rodape);
    }

    bool Dentro(string campo, double v, string nome)
    {
        var f = Fx?.Campos.GetValueOrDefault("processador." + campo);
        if (f is not { Disponivel: true }) { Aviso($"{nome}: campo travado ({f?.Motivo ?? "faixa não lida"}).", true); return false; }
        if (v < f.Min || v > f.Max) { Aviso($"{nome}: {Validacao.Numero(v)} {f.Unidade} fora da faixa ({Validacao.Numero(f.Min)} a {Validacao.Numero(f.Max)} {f.Unidade}). Valor recusado.".Replace("  ", " "), true); return false; }
        return true;
    }

    void Aviso(string t, bool erro) { aviso.Text = t; aviso.Foreground = erro ? P.Morno : P.Apagado; }

    void Mudar(string campo, double? v)
    {
        var nome = Limites.First(l => l.Campo == campo).Nome;
        if (v is double d && !Dentro(campo, d, nome)) { Atualizar(); return; }
        int? i = v is double x ? (int)Math.Round(x) : null;
        switch (campo)
        {
            case "ppt": rascunho.Ppt = i; break;
            case "tdc": rascunho.Tdc = i; break;
            case "edc": rascunho.Edc = i; break;
            case "limiteTemperatura": rascunho.LimiteTemperatura = i; break;
            case "fmaxDeslocamento": rascunho.FmaxDeslocamento = i; break;
        }
        Aviso("Campos com borda clara diferem do padrão.", false);
        Atualizar();
    }

    void MudarCo(int nucleo, double? v)
    {
        if (v is double d && !Dentro("coTodos", d, $"Curve Optimizer do núcleo {nucleo}")) { Atualizar(); return; }
        while (rascunho.CoNucleos.Count < coCampos.Count) rascunho.CoNucleos.Add(rascunho.CoTodos);
        rascunho.CoNucleos[nucleo] = v is double x ? (int)x : null;
        Atualizar();
    }

    async Task TrocarSmt()
    {
        var smt = Fx?.Campos.GetValueOrDefault("processador.smt");
        if (smt is not { Disponivel: true }) { App.Janela?.Aviso("O kit da AMD não permite alterar o SMT nesta máquina.", true); return; }
        bool ligado = smt.Atual == 1;
        await Gravar(c => c.Processador.Smt = !ligado, $"SMT {(ligado ? "desligado" : "ligado")} a partir do próximo reinício.");
    }

    async Task Aplicar()
    {
        var r = Json.Copia(rascunho);
        if (await Gravar(c => c.Processador = r, "Processador ajustado e confirmado. Aplicado na inicialização após confirmação de estabilidade.")) aplicar.IsEnabled = false;
    }

    void Atualizar()
    {
        var c = Cfg;
        if (c is null) return;
        aplicar.IsEnabled = Json.Texto(rascunho) != Json.Texto(c.Processador);
        foreach (var (campo, cn, rot) in campos)
        {
            var f = Fx?.Campos.GetValueOrDefault("processador." + campo);
            int? v = campo switch { "ppt" => rascunho.Ppt, "tdc" => rascunho.Tdc, "edc" => rascunho.Edc, "limiteTemperatura" => rascunho.LimiteTemperatura, _ => rascunho.FmaxDeslocamento };
            cn.Definir(v is int vi ? vi : f?.Atual is double a ? Math.Round(a) : null);
            cn.IsEnabled = f is { Disponivel: true };
            cn.BorderBrush = v is not null ? P.Texto : P.BordaForte;
            cn.Dica = f is { Disponivel: true } ? "" : f?.Motivo ?? "faixa não lida do processador";
            rot.Text = f is { Disponivel: true } ? $"padrão {(f.Padrao is double p ? Validacao.Numero(p) : "–")}, de {Validacao.Numero(f.Min)} a {Validacao.Numero(f.Max)}" : "travado: " + (f?.Motivo ?? "faixa não lida");
        }
        var fpbo = Fx?.Campos.GetValueOrDefault("processador.pboEscala");
        linhaPbo.Visibility = fpbo is { Disponivel: true } ? Visibility.Visible : Visibility.Collapsed;
        pbo.Ligada = rascunho.PboEscala ?? fpbo?.Atual == 1;
        var fco = Fx?.Campos.GetValueOrDefault("processador.coTodos");
        for (int i = 0; i < coCampos.Count; i++)
        {
            int? v = i < rascunho.CoNucleos.Count ? rascunho.CoNucleos[i] : null;
            coCampos[i].Definir((v ?? rascunho.CoTodos) is int ci ? ci : fco?.Atual is double a ? a : null);
            coCampos[i].IsEnabled = fco is { Disponivel: true };
            coCampos[i].BorderBrush = (v ?? rascunho.CoTodos) is int x && x != 0 ? P.Texto : P.BordaForte;
        }
        coTodos.Definir(rascunho.CoTodos);
        coTodos.IsEnabled = fco is { Disponivel: true };
    }

    void MontarCo(int n)
    {
        if (n <= 0 || coCampos.Count == n) return;
        coCampos.Clear();
        var grade = new List<UIElement>();
        for (int i = 0; i < n; i++)
        {
            var c = new CampoNum(null, "", double.NaN, 32) { Width = double.NaN };
            int k = i;
            c.Confirmou += v => MudarCo(k, v);
            coCampos.Add(c);
            grade.Add(K.Col(3, K.T($"Núcleo {i}", 12.5, P.Apagado), c));
        }
        coGrade.Content = K.Iguais(4, 8, grade);
    }

    protected override void Config(Config c)
    {
        rascunho = Json.Copia(c.Processador);
        modo.Pintar();
        Atualizar();
    }

    public override void NovasFaixas(Faixas f)
    {
        MontarCo(f.Nucleos);
        modo.Pintar();
        Atualizar();
        var fco = f.Campos.GetValueOrDefault("processador.coTodos");
        coFaixa.Text = fco is { Disponivel: true } ? $"padrão 0, de {Validacao.Numero(fco.Min)} a +{Validacao.Numero(fco.Max)}" : "travado: " + (fco?.Motivo ?? "faixa não lida");
        smtTxt.Text = f.Campos.GetValueOrDefault("processador.smt") is { } smt ? (smt.Atual == 1 ? "ligado" : "desligado") : "–";
        x3dTxt.Text = f.Campos.GetValueOrDefault("processador.x3dJogo") is { Disponivel: true } x ? (x.Atual == 1 ? "ligado" : "desligado") : "não suportado pelo kit nesta máquina";
        parkTxt.Text = f.Campos.GetValueOrDefault("processador.nucleosEstacionados") is { } pk ? $"{pk.Atual ?? 0:0} de {Math.Max(1, f.Nucleos)}" : "–";
    }

    public override void Mostrada()
    {
        base.Mostrada();
        if (memoriaInfo is null) _ = Memoria();
    }

    async Task Memoria()
    {
        var s = Json.Ler<InfoSistema>((await App.Lig.Pedir("sistema", null, 5000)).Dados);
        if (s is null) return;
        memoriaInfo = s.MemoriaPerfil;
        var mts = System.Text.RegularExpressions.Regex.Match(s.MemoriaPerfil, @"\d+").Value;
        memoria.Text = double.TryParse(mts, out var v) ? v.ToString("N0", K.Br) : "–";
        bool expo = s.OpcoesBios.Any(o => o.Nome.Contains("EXPO", StringComparison.OrdinalIgnoreCase) || o.Nome.Contains("Memory Profile", StringComparison.OrdinalIgnoreCase) && !o.Valor.Contains("Disabled"));
        ((Run)memoriaDe.Inlines.LastInline).Text = expo ? " MT/s, EXPO" : " MT/s";
    }

    public override void Estado(Estado e)
    {
        var lim = LimiteTemperaturaCpu;
        var cor = CorCpu(e.Cpu.TempMaisQuente);
        temp.Text = e.Cpu.TempMaisQuente is double t ? $"{t:0}°" : "–"; temp.Foreground = cor;
        medidor.Valor(Proporcao(e.Cpu.TempMaisQuente, lim), cor);
        limite.Text = lim is double limiteCpu ? $"limite {limiteCpu:0} °C" : "limite não lido";
        consumo.Text = K.N(e.Cpu.Consumo);
        double? ppt = Cfg?.Processador.Ppt ?? Fx?.Campos.GetValueOrDefault("processador.ppt")?.Atual;
        ((Run)consumoDe.Inlines.LastInline).Text = ppt is double pw ? $" W de {pw:0}" : " W";
        var topo = ClockMaximoCpu;
        ccd.Text = K.N(e.Cpu.TempCcd);
        tensao.Text = K.N(e.Cpu.Tensao, 2);
        MontarNucleos(e.Cpu.Nucleos.Count);
        for (int i = 0; i < nucleos.Count; i++)
        {
            var n = e.Cpu.Nucleos[i];
            nucleos[i].ghz.Text = K.N(n.Clock / 1000, 1);
            nucleos[i].barra.Height = Proporcao(n.Clock, topo) is double proporcao ? Math.Clamp(proporcao, 0, 1) * AlturaBarra : 0;
        }
        if (e.Tarefa is { } ta && ta.Nome.Contains("Curve"))
        {
            assistBotao.TextoBotao = ta.Rodando ? "Cancelar teste" : "Iniciar ajuste automático";
            if (ta.Rodando) assistTxt.Text = $"{ta.Progresso * 100:0}%. {ta.Etapa}";
            else if (ta.Resultado is not null) assistTxt.Text = ta.Resultado;
        }
    }
}
