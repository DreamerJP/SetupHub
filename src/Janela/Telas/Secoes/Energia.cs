using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas.Secoes;

sealed class Energia : Tela
{
    protected override string Id => "energia";
    Grid planos = null!;
    TextBlock total = null!;
    Grid horas = null!;
    StackPanel regras = null!;
    TextBlock semRegras = null!;
    CampoNum maximo = null!, minimo = null!, nucleos = null!, preferencia = null!, tela = null!, suspender = null!;
    Escolha impulso = null!, pcie = null!;
    Chave usb = null!;
    TextBlock tituloAjustes = null!;
    Botao restaurar = null!;
    DispatcherTimer? relogio;

    protected override FrameworkElement Montar()
    {
        Cabecalho("Energia", () => "plano de energia do SetupHub");
        var l1 = K.Grade("1.5* 1*", 12, MontarPlano(), MontarConsumo());
        var l2 = K.Grade("1.5* 1*", 12, MontarRegras(), MontarAjustes());
        // Na primeira linha os dois cartões têm a mesma altura; na segunda cada um fica com a sua.
        foreach (UIElement x in l2.Children) if (x is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Top;
        var raiz = K.Col(12, l1, l2);
        raiz.Margin = new Thickness(20, 12, 20, 20);
        return raiz;
    }

    static readonly List<(string, string)> Opcoes = Padroes.PlanosEnergia.Select(p => (p.Id, p.Nome)).ToList();

    // Com o SetupHub sem mexer no plano, os ajustes editam o que estiver em uso.
    string Alvo(Config c) => c.Energia.Plano ?? Est?.PlanoEnergia ?? "equilibrado";

    PlanoEnergia Ajustado(Config c)
    {
        var id = Alvo(c);
        var p = c.Energia.Planos.FirstOrDefault(x => x.Id == id);
        if (p is null) c.Energia.Planos.Add(p = Padroes.PlanoEnergiaPadrao(id));
        return p;
    }

    Border MontarPlano()
    {
        planos = new Grid();
        var titulo = K.Titulo("Plano de energia"); titulo.Height = 30; titulo.Padding = new Thickness(0, 5, 0, 0);
        return K.Cartao(K.Col(8, titulo, planos), new Thickness(16, 12, 16, 14));
    }

    void PintarPlanos(Config c)
    {
        var itens = new List<UIElement>();
        var emUso = Est?.PlanoEnergia;
        foreach (var (id, nome, desc) in Padroes.PlanosEnergia)
        {
            bool on = (c.Energia.Plano ?? emUso) == id;
            var tn = K.T(nome, 14, on ? P.TextoSelecao : P.Texto, FontWeights.SemiBold);
            // O destaque já marca o escolhido; a marca de ativo só aparece quando uma troca automática pôs outro plano.
            var td = K.T(desc + (emUso == id && !on ? ", ativo por troca automática" : ""), 12.5, on ? P.SubSel : P.Apagado);
            var b = new Botao { Height = P.E(58), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 0, 12, 0), Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { tn, td } } };
            b.Fundo(on ? P.Selecao : P.PocoEscuro); b.FundoSobre = on ? P.Selecao : P.Poco;
            var n = id;
            b.Clique += (_, _) => _ = Gravar(cf => cf.Energia.Plano = n, $"Plano {nome} em uso.");
            itens.Add(b);
        }
        planos.Children.Clear(); planos.ColumnDefinitions.Clear(); planos.RowDefinitions.Clear();
        planos.Children.Add(K.Col(8, itens.ToArray()));
    }

    Border MontarConsumo()
    {
        total = new TextBlock();
        horas = new Grid { Height = 74 };
        for (int i = 0; i < 24; i++)
        {
            horas.ColumnDefinitions.Add(new ColumnDefinition());
            var b = new Border { VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(2, 2, 0, 0), Margin = new Thickness(i == 0 ? 0 : 1.5, 0, i == 23 ? 0 : 1.5, 0), Height = 0 };
            Grid.SetColumn(b, i);
            horas.Children.Add(b);
        }
        var eixo = new Grid { Children = { K.T("0 h", 11.5, P.Apagado), Centro(K.T("12 h", 11.5, P.Apagado)), Direita(K.T("24 h", 11.5, P.Apagado)) } };
        var titulo = K.Titulo("Consumo hoje"); titulo.Height = 30; titulo.Padding = new Thickness(0, 5, 0, 0);
        return K.Cartao(K.Col(8, titulo, total, horas, eixo), new Thickness(16, 12, 16, 14));
    }

    static TextBlock Centro(TextBlock t) { t.HorizontalAlignment = HorizontalAlignment.Center; return t; }
    static TextBlock Direita(TextBlock t) { t.HorizontalAlignment = HorizontalAlignment.Right; return t; }

    async Task LerConsumo()
    {
        var r = await App.Lig.Pedir("energia.consumo", null, 10000);
        if (!r.Ok) return;
        var v = Json.Ler<double[]>(r.Dados);
        if (v is null || v.Length < 25) return;
        total.Inlines.Clear();
        total.Inlines.Add(new System.Windows.Documents.Run(v[24].ToString("N2", K.Br)) { FontFamily = P.Numero, FontSize = P.F(40), FontWeight = FontWeights.SemiBold });
        total.Inlines.Add(new System.Windows.Documents.Run(" kWh, processador e placa de vídeo") { Foreground = P.Apagado, FontSize = P.F(14) });
        double max = Math.Max(1, v.Take(24).Max());
        int agora = DateTime.Now.Hour;
        for (int i = 0; i < 24; i++)
        {
            var b = (Border)horas.Children[i];
            b.Height = i > agora ? 0 : Math.Max(2, v[i] / max * 74);
            b.Background = v[i] > max * 0.66 ? P.Gpu : P.BordaForte;
            b.ToolTip = $"{i} h: {v[i].ToString("N0", K.Br)} Wh";
        }
    }

    Border MontarRegras()
    {
        regras = new StackPanel();
        semRegras = K.Nota("Nenhuma troca automática. O plano selecionado permanece ativo.");
        semRegras.Margin = new Thickness(0, 4, 0, 10);
        var adicionar = new MenuAcoes("Adicionar troca", new (string, Action)[]
        {
            ("Quando um programa abrir", () => _ = NovaPrograma()),
            ("Num horário", () => _ = Nova(new RegraEnergia { Tipo = "horario", Plano = "economia" })),
            ("Após um período sem uso", () => _ = Nova(new RegraEnergia { Tipo = "ocioso", Minutos = 20, Plano = "economia" })),
        });
        var cab = K.Grade("* 150", 10, K.Titulo("Trocas automáticas"), adicionar);
        cab.MinHeight = 36;
        return K.Cartao(K.Col(0, cab, regras, semRegras), new Thickness(16, 12, 16, 6));
    }

    async Task NovaPrograma()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Programas (*.exe)|*.exe", Title = "Escolha o programa" };
        if (d.ShowDialog() != true) return;
        await Nova(new RegraEnergia { Tipo = "programa", Programa = d.FileName, Plano = "desempenho" });
    }

    Task<bool> Nova(RegraEnergia r) => Gravar(c => c.Energia.Regras.Add(r), "Troca automática adicionada.");

    Task<bool> Regra(int i, Action<RegraEnergia> mudar, string? ok = null) => Gravar(c => { if (i < c.Energia.Regras.Count) mudar(c.Energia.Regras[i]); }, ok);

    void PintarRegras(Config c)
    {
        regras.Children.Clear();
        for (int i = 0; i < c.Energia.Regras.Count; i++)
        {
            var r = c.Energia.Regras[i];
            int k = i;
            UIElement quando = r.Tipo switch
            {
                "programa" => K.T($"Quando {Path.GetFileNameWithoutExtension(r.Programa ?? "")} abrir"),
                "horario" => Horario(r, k),
                _ => Ocioso(r, k),
            };
            var plano = new Escolha(Opcoes, r.Plano);
            plano.Escolheu += v => _ = Regra(k, x => x.Plano = v, "Plano da troca atualizado.");
            var liga = new Chave(r.Ligada);
            liga.Mudou += on => _ = Regra(k, x => x.Ligada = on, on ? "Troca ligada." : "Troca desligada.");
            var tirar = new Botao { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), ToolTip = "Remover esta troca", Child = K.IconeCaixa("M7 7l10 10M17 7L7 17", 14, P.Apagado, 2) };
            tirar.FundoNormal = Brushes.Transparent; tirar.Background = Brushes.Transparent; tirar.FundoSobre = P.Poco;
            tirar.Clique += (_, _) => _ = Gravar(cf => { if (k < cf.Energia.Regras.Count) cf.Energia.Regras.RemoveAt(k); }, "Troca tirada.");
            var g = K.Grade("* 190 40 28", 12, quando, plano, liga, tirar);
            g.VerticalAlignment = VerticalAlignment.Center;
            regras.Children.Add(new Border { MinHeight = P.E(46), BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = g });
        }
        semRegras.Visibility = c.Energia.Regras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    UIElement Horario(RegraEnergia r, int k)
    {
        CampoTexto Campo(string v, Action<RegraEnergia, string> mudar)
        {
            var t = new CampoTexto(v, 64, 30) { Aceita = s => Validacao.HorarioValido(s) };
            t.Confirmou += s => _ = Regra(k, x => mudar(x, s), "Horário da troca atualizado.");
            return t;
        }
        return K.Lin(6, K.T("Das"), Campo(r.Inicio, (x, s) => x.Inicio = s), K.T("às"), Campo(r.Fim, (x, s) => x.Fim = s));
    }

    UIElement Ocioso(RegraEnergia r, int k)
    {
        var m = new CampoNum(r.Minutos, "", 56, 30) { AceitaVazio = false };
        m.Confirmou += v => _ = Regra(k, x => x.Minutos = (int)Math.Round(v ?? 20), "Tempo sem uso atualizado.");
        return K.Lin(6, K.T("Após"), m, K.T("minutos sem uso"));
    }

    Border MontarAjustes()
    {
        CampoNum Num(Action<PlanoEnergia, int> mudar, string ok)
        {
            var c = new CampoNum(null, "", 80, 32) { AceitaVazio = false };
            c.Confirmou += v => _ = Gravar(cf => mudar(Ajustado(cf), (int)Math.Round(v ?? 0)), ok);
            return c;
        }
        maximo = Num((p, v) => p.ProcessadorMaximo = v, "Processador no máximo atualizado.");
        minimo = Num((p, v) => p.ProcessadorMinimo = v, "Processador no mínimo atualizado.");
        nucleos = Num((p, v) => p.NucleosMinimos = v, "Núcleos ativos no mínimo atualizado.");
        preferencia = Num((p, v) => p.PreferenciaEnergia = v, "Preferência de energia atualizada.");
        tela = Num((p, v) => p.DesligarTela = v, "Tempo para desligar a tela atualizado.");
        suspender = Num((p, v) => p.Suspender = v, "Tempo para suspender atualizado.");
        Border Linha(string nome, string desc, UIElement campo, string un) =>
            new() { MinHeight = P.E(46), BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = Meio(K.Grade("* 80 34", 8, K.Col(0, K.T(nome), K.T(desc, 12, P.Apagado)), campo, K.T(un, 14, P.Apagado))) };
        impulso = new Escolha(new[] { ("desligado", "Desligado"), ("ligado", "Ligado"), ("agressivo", "Agressivo"), ("eficiente-agressivo", "Eficiente") }, null);
        impulso.Escolheu += v => _ = Gravar(c => Ajustado(c).Impulso = v, "Impulso do processador atualizado.");
        pcie = new Escolha(new[] { ("desligado", "Desligada"), ("moderado", "Moderada"), ("maximo", "Máxima") }, null);
        pcie.Escolheu += v => _ = Gravar(c => Ajustado(c).EconomiaPcie = v, "Economia do PCI Express atualizada.");
        usb = new Chave();
        usb.Mudou += on => _ = Gravar(c => Ajustado(c).SuspensaoUsb = on, on ? "Suspensão seletiva do USB ligada." : "Suspensão seletiva do USB desligada.");
        restaurar = Botao.Contorno("Restaurar padrão", () =>
        {
            if (Cfg is not { } atual) return;
            var id = Alvo(atual);
            _ = Gravar(cf => { var i = cf.Energia.Planos.FindIndex(x => x.Id == id); if (i >= 0) cf.Energia.Planos[i] = Padroes.PlanoEnergiaPadrao(id); else cf.Energia.Planos.Add(Padroes.PlanoEnergiaPadrao(id)); },
                $"Plano {Padroes.NomePlano(id)} de volta ao padrão.");
        });
        ((TextBlock)restaurar.Child).Foreground = P.Texto;
        tituloAjustes = K.Titulo("Ajustes do plano");
        var cab = K.Grade("* 150", 10, tituloAjustes, restaurar);
        cab.MinHeight = 36;
        return K.Cartao(K.Col(0, cab,
            Linha("Processador no máximo", "limite do clock; 100% permite o turbo", maximo, "%"),
            Linha("Processador no mínimo", "clock mínimo em repouso", minimo, "%"),
            Linha("Preferência de energia", "0 prioriza desempenho, 100 prioriza economia", preferencia, ""),
            Linha("Núcleos ativos no mínimo", "os demais entram em repouso sem carga", nucleos, "%"),
            Linha("Desligar a tela após", "0 desativa", tela, "min"),
            Linha("Suspender o PC após", "0 desativa", suspender, "min"),
            K.LinhaAjuste(K.Col(0, K.T("Impulso do processador"), K.T("clock acima do base sob carga", 12, P.Apagado)), impulso, 122, 46),
            K.LinhaAjuste(K.Col(0, K.T("Economia do PCI Express"), K.T("repouso da placa de vídeo e do SSD", 12, P.Apagado)), pcie, 122, 46),
            K.LinhaAjuste("Suspensão seletiva do USB", usb, 40, 46)), new Thickness(16, 12, 16, 6));
    }

    static Grid Meio(Grid g) { g.VerticalAlignment = VerticalAlignment.Center; return g; }

    protected override void Config(Config c)
    {
        PintarPlanos(c);
        PintarRegras(c);
        PintarAjustes(c);
    }

    void PintarAjustes(Config c)
    {
        var id = Alvo(c);
        var p = c.Energia.Planos.FirstOrDefault(x => x.Id == id) ?? Padroes.PlanoEnergiaPadrao(id);
        tituloAjustes.Text = "Ajustes do plano " + Padroes.NomePlano(id);
        maximo.Definir(p.ProcessadorMaximo); minimo.Definir(p.ProcessadorMinimo); nucleos.Definir(p.NucleosMinimos); preferencia.Definir(p.PreferenciaEnergia);
        tela.Definir(p.DesligarTela); suspender.Definir(p.Suspender);
        impulso.Selecionar(p.Impulso); pcie.Selecionar(p.EconomiaPcie);
        usb.Ligada = p.SuspensaoUsb;
        restaurar.IsEnabled = Json.Texto(p) != Json.Texto(Padroes.PlanoEnergiaPadrao(id));
        restaurar.ToolTip = restaurar.IsEnabled ? null : "Plano já com os valores de fábrica.";
    }

    public override void Mostrada()
    {
        base.Mostrada();
        _ = LerConsumo();
        relogio ??= new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => _ = LerConsumo(), Dispatcher.CurrentDispatcher);
        relogio.Start();
    }

    public override void Escondida() => relogio?.Stop();

    string? planoVisto;
    public override void Estado(Estado e)
    {
        if (e.PlanoEnergia == planoVisto || Cfg is not { } c) return;
        planoVisto = e.PlanoEnergia;
        PintarPlanos(c);
        PintarAjustes(c);
    }
}
