using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas.Detalhado;

sealed class Luz : Tela
{
    protected override string Id => "luz";
    readonly HashSet<string> sel = new();
    readonly List<(string id, Marcador m, FitaLed fita, TextBlock plano)> saidas = new();
    StackPanel listaSaidas = null!;
    FitaLed previaUnica = null!;
    Botao todas = null!;
    Chave sinc = null!;
    Grid gradePlanos = null!;
    readonly List<(string nome, Botao b, FitaLed amostra, TextBlock tn, TextBlock td)> planos = new();
    Escolha tipoEfeito = null!;
    CampoTexto nomeNovo = null!;
    Botao criar = null!, acaoPlano = null!;
    TextBlock planoAtual = null!, descPlano = null!;
    EditorLuz editor = null!;
    Chave apagar = null!, noite = null!;
    TextBlock noiteTxt = null!;
    DispatcherTimer? animacao;
    double t;

    protected override FrameworkElement Montar()
    {
        Cabecalho("Luz", () => $"controlador da placa, {Cfg?.Luz.Saidas.Count ?? 5} saídas");
        gradePlanos = new Grid();
        nomeNovo = new CampoTexto("", 170);
        criar = Botao.Contorno("Criar", () => _ = Criar(), 32);
        criar.Width = 80; criar.IsEnabled = false;
        nomeNovo.Editou += t => criar.IsEnabled = t.Length > 0;
        nomeNovo.Confirmou += t => { if (t.Length > 0) _ = Criar(); };
        var novo = K.Lin(8, K.T("Novo plano com o efeito atual", 12.5, P.Apagado), nomeNovo, criar);
        novo.VerticalAlignment = VerticalAlignment.Center;
        var cartaoPlanos = K.Cartao(K.Col(10, K.Grade("* auto", 10, K.Titulo("Planos"), novo), gradePlanos), new Thickness(16, 12, 16, 14));
        var raiz = K.Col(12,
            K.Grade("1.4* *", 12, cartaoPlanos, MontarAjuste()),
            K.Grade("1.4* *", 12, MontarSaidas(), MontarGeral()));
        raiz.Margin = new Thickness(20, 12, 20, 20);
        animacao = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        animacao.Tick += (_, _) => { t += 0.1; Animar(); };
        return raiz;
    }

    Border MontarSaidas()
    {
        todas = Botao.Contorno("Selecionar todas", () => { foreach (var s in saidas) sel.Add(s.id); Pintar(); });
        todas.Width = 140;
        sinc = new Chave();
        sinc.Mudou += on => _ = Definir("luz.sincronizar", on);
        previaUnica = new FitaLed(false) { Height = 10, Margin = new Thickness(0, 8, 0, 6) };
        listaSaidas = new StackPanel();
        return K.Cartao(K.Col(0,
            K.Grade("* 140", 10, K.Titulo("Saídas"), todas),
            K.LinhaAjuste("Mesmo efeito em todas as saídas", sinc, 40, 42),
            previaUnica, listaSaidas), new Thickness(16, 12, 16, 10));
    }

    Border MontarGeral()
    {
        apagar = new Chave(); apagar.Mudou += on => _ = Definir("luz.apagarComTela", on);
        noite = new Chave(); noite.Mudou += on => _ = Definir("luz.noiteQuente", on);
        noiteTxt = K.T("Luz mais quente à noite");
        return K.Cartao(K.Col(0, K.Titulo("Geral"),
            K.LinhaAjuste("Apagar com a tela desligada", apagar, 40, 42),
            K.LinhaAjuste(noiteTxt, noite, 40, 42)), new Thickness(16, 12, 16, 10));
    }

    void MontarListaSaidas(Config c)
    {
        if (saidas.Select(s => s.id).SequenceEqual(c.Luz.Saidas.Select(s => s.Id))) return;
        saidas.Clear(); listaSaidas.Children.Clear();
        if (sel.Count == 0) foreach (var s in c.Luz.Saidas) sel.Add(s.Id);
        foreach (var s in c.Luz.Saidas)
        {
            var m = new Marcador { Marcado = sel.Contains(s.Id) };
            var id = s.Id;
            m.Mudou += on => { if (on) sel.Add(id); else sel.Remove(id); Pintar(); };
            var nome = K.Col(0, K.T(s.Nome, 14, P.Texto, FontWeights.Medium), K.T(AcoesLuz.Tipo(s.Id), 12, P.Apagado));
            var fita = new FitaLed(false) { Height = 10 };
            var plano = K.T("", 13, P.Secundario);
            plano.HorizontalAlignment = HorizontalAlignment.Right;
            var g = K.Grade("24 170 * 110", 12, m, nome, fita, plano);
            var linha = new Border { Height = 40, BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = g, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            linha.MouseLeftButtonUp += (_, _) => { m.Marcado = !m.Marcado; if (m.Marcado) sel.Add(id); else sel.Remove(id); Pintar(); };
            saidas.Add((s.Id, m, fita, plano));
            listaSaidas.Children.Add(linha);
        }
    }

    void MontarPlanos(Config c)
    {
        var nomes = c.Luz.Planos.Select(p => p.Nome).ToList();
        if (planos.Select(p => p.nome).SequenceEqual(nomes)) return;
        planos.Clear();
        var itens = new List<UIElement>();
        foreach (var nome in nomes)
        {
            var amostra = new FitaLed(false) { Height = 8 };
            var tn = K.T(nome, 14, P.Texto, FontWeights.SemiBold);
            var td = K.T(PreviaLuz.Descricao(c, nome), 12, P.Apagado);
            td.TextWrapping = TextWrapping.Wrap; td.TextTrimming = TextTrimming.CharacterEllipsis;
            td.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; td.LineHeight = P.F(15.6); td.MaxHeight = P.F(15.6) * 3 + 1;
            var b = new Botao { Height = 110, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = K.Col(6, amostra, tn, td) };
            ((StackPanel)b.Child).VerticalAlignment = VerticalAlignment.Top;
            var plano = nome;
            b.Clique += (_, _) => _ = AcoesLuz.Plano(Alvo(), plano);
            planos.Add((nome, b, amostra, tn, td));
            itens.Add(b);
        }
        gradePlanos.Children.Clear();
        gradePlanos.Children.Add(K.Iguais(4, 8, itens));
    }

    async Task Criar()
    {
        var nome = nomeNovo.Text();
        if (nome.Length == 0) return;
        await AcoesLuz.SalvarPlano(nome);
        nomeNovo.Definir("");
        criar.IsEnabled = false;
    }

    IEnumerable<string> Alvo() => Cfg?.Luz.Sincronizar == true || sel.Count == 0 ? (Cfg?.Luz.Saidas.Select(s => s.Id) ?? sel) : sel;

    async Task AplicarEfeito(string tipo)
    {
        var c = Cfg;
        if (c is null) return;
        var primeira = Alvo().FirstOrDefault();
        var ef = AcoesLuz.NovoEfeito(tipo, primeira is null ? null : AcoesLuz.EfeitoDa(c, primeira));
        await AcoesLuz.Gravar(c, Alvo(), ef, $"{PreviaLuz.DoTipo(tipo).Nome}.");
    }

    Border MontarAjuste()
    {
        planoAtual = K.Titulo("");
        planoAtual.MinHeight = 30;
        acaoPlano = Botao.Contorno("Restaurar padrão", () => _ = AcaoPlano(), 32);
        acaoPlano.Width = 150;
        descPlano = new TextBlock { Foreground = P.Secundario, TextWrapping = TextWrapping.Wrap, FontSize = P.F(13.5), Padding = new Thickness(0, 0, 0, 10) };
        tipoEfeito = new Escolha(PreviaLuz.Tipos.Select(x => (x.Id, x.Nome)), null);
        tipoEfeito.Escolheu += v => _ = AplicarEfeito(v);
        editor = new EditorLuz(Gravar, 200);
        var nomes = PreviaLuz.Tipos.Where(x => !x.DoMsi || x.Id is "temperatura" or "musica").Select(x => x.Nome).ToList();
        var doSetupHub = nomes.Count > 1 ? string.Join(", ", nomes.SkipLast(1)) + " e " + nomes[^1] : string.Join("", nomes);
        return K.Cartao(K.Col(0, K.Grade("* auto", 10, planoAtual, acaoPlano), descPlano, K.LinhaAjuste("Efeito", tipoEfeito, 200, 42), editor.Painel,
            K.Nota($"{doSetupHub} dependem do SetupHub em execução; os demais efeitos rodam no controlador da placa.")), new Thickness(16, 12, 16, 10));
    }

    Task AcaoPlano()
    {
        if (Cfg is not { } c || Alvo().FirstOrDefault() is not { } id || AcoesLuz.PlanoDasSaidas(c, new[] { id }) is not { } nome) return Task.CompletedTask;
        return c.Luz.Planos.Any(p => p.Nome == nome && p.Pronto) ? AcoesLuz.RestaurarPlano(nome) : AcoesLuz.ExcluirPlano(nome);
    }

    Task Gravar(EfeitoLuz ef, string ok) => Cfg is { } c ? AcoesLuz.Gravar(c, Alvo(), ef, ok) : Task.CompletedTask;

    void Pintar()
    {
        var c = Cfg;
        if (c is null) return;
        bool juntas = c.Luz.Sincronizar;
        previaUnica.Visibility = juntas ? Visibility.Visible : Visibility.Collapsed;
        listaSaidas.Visibility = juntas ? Visibility.Collapsed : Visibility.Visible;
        todas.Visibility = juntas ? Visibility.Collapsed : Visibility.Visible;
        foreach (var s in saidas)
        {
            s.m.Marcado = sel.Contains(s.id);
            s.plano.Text = AcoesLuz.PlanoDa(c, s.id);
        }
        var primeira = Alvo().FirstOrDefault();
        var nomeAtivo = primeira is null ? c.Luz.Plano : AcoesLuz.PlanoDa(c, primeira);
        bool usaPlano = primeira is not null && AcoesLuz.PlanoDasSaidas(c, new[] { primeira }) is not null;
        foreach (var p in planos)
        {
            bool on = usaPlano && p.nome == nomeAtivo;
            p.b.Fundo(on ? P.Selecao : P.Poco); p.b.FundoSobre = on ? P.Selecao : P.PocoEscuro;
            p.b.BordaNormal = on ? P.Selecao : P.Borda; p.b.BorderBrush = p.b.BordaNormal;
            p.tn.Foreground = on ? P.TextoSelecao : P.Texto; p.td.Foreground = on ? P.SubSel : P.Apagado;
        }
        var ef = primeira is null ? null : AcoesLuz.EfeitoDa(c, primeira);
        tipoEfeito.Selecionar(ef?.Tipo);
        var planoUsado = usaPlano ? c.Luz.Planos.FirstOrDefault(p => p.Nome == nomeAtivo) : null;
        acaoPlano.Visibility = planoUsado is null ? Visibility.Collapsed : Visibility.Visible;
        if (planoUsado is not null)
        {
            acaoPlano.TextoBotao = planoUsado.Pronto ? "Restaurar padrão" : "Excluir plano";
            acaoPlano.IsEnabled = !planoUsado.Pronto || AcoesLuz.PlanoAlterado(c, planoUsado.Nome);
            acaoPlano.ToolTip = planoUsado.Pronto && !acaoPlano.IsEnabled ? "Plano já com os valores de fábrica." : null;
        }
        var tipoAtual = ef is null ? null : PreviaLuz.DoTipo(ef.Tipo);
        planoAtual.Text = usaPlano && tipoAtual is not null && tipoAtual.Nome != nomeAtivo ? $"{nomeAtivo}: {tipoAtual.Nome}" : nomeAtivo;
        descPlano.Text = tipoAtual?.Descricao ?? "";
        editor.Mostrar(ef);
        apagar.Ligada = c.Luz.ApagarComTela;
        noite.Ligada = c.Luz.NoiteQuente;
        noiteTxt.Text = $"Luz mais quente à noite, das {c.Luz.NoiteInicio[..2].TrimStart('0')}h às {c.Luz.NoiteFim[..2].TrimStart('0')}h";
        sinc.Ligada = juntas;
        Animar();
    }

    static double Opacidade(EfeitoLuz ef) => ef.Tipo == "desligado" ? 1 : 0.25 + Math.Clamp(Math.Ceiling(ef.Brilho / 20.0), 1, 5) / 5 * 0.75;

    void Animar()
    {
        var c = Cfg;
        if (c is null) return;
        foreach (var s in saidas)
        {
            var ef = AcoesLuz.EfeitoDa(c, s.id);
            s.fita.Mostrar(PreviaLuz.Cores(ef, s.id == "jrgb1" ? 1 : 30, t, Est), Opacidade(ef));
        }
        if (c.Luz.Saidas.FirstOrDefault(s => s.Id != "jrgb1") is { } fita)
        {
            var ef = AcoesLuz.EfeitoDa(c, fita.Id);
            previaUnica.Mostrar(PreviaLuz.Cores(ef, 60, t, Est), Opacidade(ef));
        }
        foreach (var p in planos)
            if (PreviaLuz.DoPlano(c, p.nome) is { } efp) p.amostra.Mostrar(PreviaLuz.Cores(efp, 16, t, Est), Opacidade(efp));
    }

    protected override void Config(Config c)
    {
        MontarListaSaidas(c);
        MontarPlanos(c);
        Pintar();
    }

    public override void Mostrada() { base.Mostrada(); animacao?.Start(); }
    public override void Escondida() => animacao?.Stop();
    public override void Descartar() => animacao?.Stop();
}
