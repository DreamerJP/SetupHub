using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SetupHub.Janela.Kit;
using SetupHub.Janela.Telas;
using SetupHub.Nucleo;

namespace SetupHub.Janela;

sealed class Painel : Window
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int t);
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr a, IntPtr b);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

    public static readonly (string Id, string Nome, string Icone)[] Secoes =
    {
        ("inicio", "Início", "M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z"),
        ("ventoinhas", "Ventoinhas", ""),
        ("luz", "Luz", "M9 18h6M10 21h4M12 3a6 6 0 0 0-4 10.5c.8.8 1 1.5 1 2.5h6c0-1 .2-1.7 1-2.5A6 6 0 0 0 12 3z"),
        ("processador", "Processador", "M6 6h12v12H6zM9.5 9.5h5v5h-5zM9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"),
        ("placaDeVideo", "Placa de vídeo", "M2 6h20v11H2zM9 8.5a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM16.5 10a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3zM5 17v3"),
        ("jogos", "Jogos", "M7 4h10a5 5 0 0 1 5 5v3a4 4 0 0 1-7 2.6L14 13h-4l-1 1.6A4 4 0 0 1 2 12V9a5 5 0 0 1 5-5zM6 9h4M8 7v4"),
        ("telas", "Telas", "M3 4h18v12H3zM8 20h8M12 16v4"),
        ("energia", "Energia", "M13 2 4 14h7l-1 8 9-12h-7z"),
        ("perfis", "Perfis", "m12 3 9 5-9 5-9-5zM3 13l9 5 9-5"),
        ("sistema", "Sistema", "M3 3h18v18H3zM7 7h4v4H7zM15 7h2M15 11h2M7 15h10"),
        ("aparencia", "Aparência", "M12 3a9 9 0 1 0 0 18c1.1 0 1.6-.8 1.6-1.6 0-1.2-1-1.6-1-2.4 0-.9.7-1.6 1.6-1.6H16a5 5 0 0 0 5-5c0-4-4-7.4-9-7.4zM7.5 10.5h.01M10 7h.01M14.5 7h.01"),
    };

    readonly Dictionary<string, Tela> telas = new();
    string atual = "inicio";
    readonly ContentControl conteudo = new();
    readonly Dictionary<string, Botao> navegacao = new();
    Border? aviso;
    TextBlock? avisoTexto;
    DispatcherTimer? avisoRelogio;

    public Painel()
    {
        Title = Produto.Nome;
        Width = 1280; Height = 800; MinWidth = 1024; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Recursos/setuphub.ico")); } catch { }
        Restaurar();
        SourceInitialized += (_, _) => Barra();
        Closing += Fechando;
        KeyDown += Tecla;
        Refazer();
        App.Lig.AoEstado += e => App.Interface(() => Estado(e));
        App.Lig.AoConfig += c => App.Interface(() => { foreach (var t in telas.Values) t.NovaConfig(c); PintarPerfis(c); PintarCabecalho(); });
        App.Lig.AoFaixas += f => App.Interface(() => { foreach (var t in telas.Values) t.NovasFaixas(f); });
        App.Lig.AoLigacao += on => App.Interface(() => { if (!on) Aviso("Reconectando ao serviço do SetupHub…"); });
    }

    void Barra()
    {
        var h = new WindowInteropHelper(this).Handle;
        int escuro = K.P.Claro ? 0 : 1;
        DwmSetWindowAttribute(h, 20, ref escuro, 4);
        var c = K.P.Claro ? K.P.Fundo.Color : K.P.Nav.Color;
        int cor = c.R | c.G << 8 | c.B << 16;
        DwmSetWindowAttribute(h, 35, ref cor, 4);
        var t = K.P.Texto.Color;
        int texto = t.R | t.G << 8 | t.B << 16;
        DwmSetWindowAttribute(h, 36, ref texto, 4);
    }

    public void Refazer()
    {
        foreach (var t in telas.Values) t.Descartar();
        telas.Clear();
        navegacao.Clear();
        Background = K.P.Fundo;
        TextElement.SetFontFamily(this, K.P.Corpo);
        TextElement.SetForeground(this, K.P.Texto);
        TextElement.SetFontSize(this, K.P.F(14));
        var raiz = new Grid { Background = K.P.Fundo };
        Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = Estilos.BarraRolagem();
        if (conteudo.Parent is Panel antigo) antigo.Children.Remove(conteudo);
        perfisFixos = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        nomesPerfis = "";
        tituloTela = subTela = null;
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        raiz.Children.Add(MenuLateral());
        var direita = new Grid();
        direita.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        direita.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        direita.Children.Add(BarraFixa());
        Grid.SetRow(conteudo, 1);
        direita.Children.Add(conteudo);
        Grid.SetColumn(direita, 1);
        raiz.Children.Add(direita);
        PintarPerfis(App.Lig.Config);
        aviso = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16),
            Background = K.P.Painel, BorderBrush = K.P.BordaForte, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(K.P.Raio),
            Padding = new Thickness(14, 9, 14, 9), Visibility = Visibility.Collapsed, MaxWidth = 760,
        };
        avisoTexto = K.T("", 13.5); avisoTexto.TextWrapping = TextWrapping.Wrap; avisoTexto.TextTrimming = TextTrimming.None;
        aviso.Child = avisoTexto;
        aviso.MouseLeftButtonUp += (_, _) => aviso.Visibility = Visibility.Collapsed;
        Grid.SetRowSpan(aviso, 2); Grid.SetColumnSpan(aviso, 2);
        raiz.Children.Add(aviso);
        Content = raiz;
        if (IsLoaded) Barra();
        Ir(atual);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { foreach (var s in Secoes) Pegar(s.Id); });
    }

    Tela Pegar(string id)
    {
        if (telas.TryGetValue(id, out var t)) return t;
        t = Tela.Criar(id);
        try { t.Preparar(); }
        catch (Exception x)
        {
            App.Registrar(x);
            t = new Telas.TelaComErro(id, x.Message);
            t.Preparar();
        }
        telas[id] = t;
        if (App.Lig.Config is { } c) t.NovaConfig(c);
        if (App.Lig.Faixas is { } f) t.NovasFaixas(f);
        if (App.Lig.Estado is { } e) t.EstadoSeguro(e);
        return t;
    }

    public void Ir(string id)
    {
        atual = id;
        var t = Pegar(id);
        conteudo.Content = t.Raiz;
        foreach (var (k, b) in navegacao) Marcar(b, k == id);
        t.Mostrada();
        PintarCabecalho();
    }

    ContentControl perfisFixos = new();
    string nomesPerfis = "";
    TextBlock? tituloTela, subTela;

    void PintarPerfis(Config? c)
    {
        if (c is null) return;
        var nomes = c.Perfis.Select(p => p.Nome).Take(5).ToList();
        if (nomes.Count > 0 && !nomes.Contains(c.PerfilAtivo) && c.Perfis.Any(p => p.Nome == c.PerfilAtivo)) nomes[^1] = c.PerfilAtivo;
        var chave = string.Join("\n", nomes);
        if (chave != nomesPerfis || perfisFixos.Content is null)
        {
            nomesPerfis = chave;
            var seg = new Segmentado(nomes.Select(n => (n, n)), 34, 104)
            {
                BorderBrush = K.P.Borda,
                BorderThickness = new Thickness(1),
            };
            seg.Escolheu += n => _ = Tela.Fazer("perfil.aplicar", Ligacao.Args(("nome", n), ("origem", "janela")));
            perfisFixos.Content = seg;
        }
        ((Segmentado)perfisFixos.Content).Selecionar(c.PerfilAtivo);
    }

    void PintarCabecalho()
    {
        if (tituloTela is null || subTela is null || !telas.TryGetValue(atual, out var t)) return;
        tituloTela.Text = t.Titulo;
        string s;
        try { s = t.Subtitulo?.Invoke() ?? ""; } catch { s = ""; }
        if (subTela.Text != s) subTela.Text = s;
        subTela.Visibility = s.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    UIElement BarraFixa()
    {
        var P = K.P;
        tituloTela = K.T("", 30, P.Texto, FontWeights.SemiBold, P.Titulo);
        tituloTela.LineHeight = 30;
        subTela = K.T("", 13, P.Apagado);
        subTela.Margin = new Thickness(10, 6, 0, 0);
        var titulos = K.Grade("auto *", 0, tituloTela, subTela);
        titulos.Margin = new Thickness(0, 0, 18, 0);
        var cmd = new Border { Child = new BarraComando(), MinHeight = 40, Padding = new Thickness(12, 0, 12, 0), Background = P.PocoEscuro, BorderBrush = P.Borda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Margin = new Thickness(14, 0, 0, 0) };
        var g = K.Grade("* auto auto", 0, titulos, perfisFixos, cmd);
        g.SizeChanged += (_, _) => cmd.Width = Math.Clamp(g.ActualWidth - perfisFixos.ActualWidth - 14 - 470, 220, 420);
        return new Border { Child = g, Padding = new Thickness(20, 16, 20, 0) };
    }

    void Marcar(Botao b, bool on)
    {
        b.Fundo(on ? K.P.NavAtivo : Brushes.Transparent);
        b.FundoSobre = on ? K.P.NavAtivo : K.P.Poco;
        foreach (var t in ((StackPanel)b.Child).Children.OfType<TextBlock>()) t.Foreground = on ? (K.P.Claro ? K.P.Texto : Brushes.White) : K.P.Secundario;
        var cor = on ? (K.P.Claro ? K.P.Texto : Brushes.White) : K.P.Secundario;
        foreach (var v in ((StackPanel)b.Child).Children.OfType<Viewbox>())
            if (v.Child is Canvas c && c.Children[0] is System.Windows.Shapes.Path p) { if (p.Fill != null) p.Fill = cor; else p.Stroke = cor; }
    }

    UIElement MenuLateral()
    {
        var P = K.P;
        var pilha = new DockPanel { LastChildFill = false };
        var bMarca = new Botao { Width = 48, Height = 48, Child = IconeApp.Icone(34), Margin = new Thickness(0, 0, 0, 10), ToolTip = Produto.Nome + ", início" };
        bMarca.Clique += (_, _) => Ir("inicio");
        DockPanel.SetDock(bMarca, Dock.Top);
        pilha.Children.Add(bMarca);
        var versao = K.T("versão " + Produto.Versao, 10.5, P.Tenue);
        versao.HorizontalAlignment = HorizontalAlignment.Center; versao.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(versao, Dock.Bottom);
        pilha.Children.Add(versao);
        foreach (var (id, nome, icone) in Secoes)
        {
            var t = K.T(nome, 11.5, P.Secundario);
            t.TextAlignment = TextAlignment.Center; t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.LineHeight = 13;
            var conteudoBotao = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var ic = icone.Length > 0 ? K.IconeCaixa(icone, 22, P.Secundario) : new Viewbox { Width = 22, Height = 22, Child = new Canvas { Width = 24, Height = 24, Children =
                { new System.Windows.Shapes.Path { Data = DiagramaAr.Pas(10), Fill = P.Secundario, RenderTransform = new TranslateTransform(12, 12) } } } };
            ic.Margin = new Thickness(0, 0, 0, 3);
            conteudoBotao.Children.Add(ic);
            conteudoBotao.Children.Add(t);
            var b = new Botao { Width = 72, MinHeight = 52, CornerRadius = new CornerRadius(8), Child = conteudoBotao, Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(2, 4, 2, 4) };
            b.FundoNormal = Brushes.Transparent; b.Background = Brushes.Transparent; b.FundoSobre = P.Poco;
            var alvo = id;
            b.Clique += (_, _) => Ir(alvo);
            DockPanel.SetDock(b, id == "aparencia" ? Dock.Bottom : Dock.Top);
            pilha.Children.Add(b);
            navegacao[id] = b;
        }
        return new Border { Background = P.Nav, BorderBrush = P.Borda, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(6, 14, 6, 14), Child = pilha };
    }

    void Estado(Estado e)
    {
        if (!IsVisible) return;
        if (telas.TryGetValue(atual, out var t)) t.EstadoSeguro(e);
        PintarCabecalho();
        foreach (var (k, x) in telas) if (k != atual) x.EstadoPendente = e;
    }

    public void Aviso(string texto, bool importante = false)
    {
        if (aviso is null || avisoTexto is null) return;
        avisoTexto.Text = texto;
        avisoTexto.Foreground = importante ? K.P.Morno : K.P.Texto;
        aviso.Visibility = Visibility.Visible;
        avisoRelogio?.Stop();
        avisoRelogio = new DispatcherTimer { Interval = TimeSpan.FromSeconds(importante ? 12 : 6) };
        avisoRelogio.Tick += (_, _) => { aviso.Visibility = Visibility.Collapsed; avisoRelogio.Stop(); };
        avisoRelogio.Start();
    }

    void Tecla(object s, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { BarraComando.Focar(); e.Handled = true; }
    }

    public void Mostrar()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (App.Lig.Estado is { } e && telas.TryGetValue(atual, out var t)) t.EstadoSeguro(e);
    }

    void Fechando(object? s, CancelEventArgs e)
    {
        e.Cancel = true;
        Salvar();
        Hide();
        foreach (var t in telas.Values) t.Escondida();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            GC.Collect(2, GCCollectionMode.Optimized, false);
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        });
    }

    string ArquivoPosicao => Path.Combine(Caminhos.Usuario, "janela.txt");

    void Restaurar()
    {
        try
        {
            var p = File.ReadAllText(ArquivoPosicao).Split(';').Select(double.Parse).ToArray();
            if (p.Length == 5 && p[2] >= MinWidth && p[3] >= MinHeight)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = p[0]; Top = p[1]; Width = p[2]; Height = p[3];
                if (p[4] == 1) WindowState = WindowState.Maximized;
                if (Left > SystemParameters.VirtualScreenWidth - 100 || Top > SystemParameters.VirtualScreenHeight - 100) WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        catch { }
    }

    void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Caminhos.Usuario);
            var r = RestoreBounds;
            File.WriteAllText(ArquivoPosicao, string.Join(';', r.Left, r.Top, r.Width, r.Height, WindowState == WindowState.Maximized ? 1 : 0));
        }
        catch { }
    }
}

sealed class BarraComando : Grid
{
    readonly TextBox caixa;
    readonly Popup resposta;
    readonly TextBlock texto;
    static readonly List<BarraComando> todas = new();

    public BarraComando()
    {
        var P = K.P;
        caixa = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = P.Texto, CaretBrush = P.Texto, FontFamily = P.Corpo,
            FontSize = P.F(14), VerticalContentAlignment = VerticalAlignment.Center, SelectionBrush = P.SubSel,
        };
        var dica = K.T("Digite um comando, ex.: ajuda", 14, P.Apagado);
        dica.TextTrimming = TextTrimming.CharacterEllipsis;
        dica.IsHitTestVisible = false;
        caixa.TextChanged += (_, _) => dica.Visibility = caixa.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var area = new Grid { Children = { dica, caixa } };
        var icone = K.IconeCaixa("m5 8 4 4-4 4M12 17h7", 16, P.Apagado, 2);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        icone.Margin = new Thickness(0, 0, 8, 0);
        Children.Add(icone);
        SetColumn(area, 1);
        Children.Add(area);
        var kbd = new Border { BorderBrush = P.BordaForte, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 1), Child = K.T("Ctrl K", 11, P.Apagado), VerticalAlignment = VerticalAlignment.Center };
        SetColumn(kbd, 2);
        Children.Add(kbd);
        texto = K.T("", 13.5);
        texto.TextWrapping = TextWrapping.Wrap; texto.TextTrimming = TextTrimming.None; texto.FontFamily = new FontFamily("Consolas");
        resposta = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Background = P.Painel, BorderBrush = P.BordaForte, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(P.Raio), Padding = new Thickness(12, 10, 12, 10), MaxWidth = 640, Child = new ScrollViewer { Content = texto, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } },
        };
        caixa.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) { caixa.Text = ""; resposta.IsOpen = false; return; }
            if (e.Key != Key.Enter || caixa.Text.Trim().Length == 0) return;
            e.Handled = true;
            var linha = caixa.Text.Trim();
            if (linha.StartsWith(Produto.Comando + " ", StringComparison.OrdinalIgnoreCase)) linha = linha[(Produto.Comando.Length + 1)..];
            texto.Text = "…";
            resposta.IsOpen = true;
            var (codigo, saida) = await App.Lig.Comando(linha);
            texto.Text = saida.Length > 0 ? saida : codigo == 0 ? "Feito." : "Recusado.";
            texto.Foreground = codigo == 0 ? P.Texto : P.Morno;
            if (codigo == 0) caixa.Text = "";
        };
        Unloaded += (_, _) => todas.Remove(this);
        Loaded += (_, _) => { if (!todas.Contains(this)) todas.Add(this); };
        VerticalAlignment = VerticalAlignment.Center;
    }

    public static void Focar()
    {
        var b = todas.FirstOrDefault(x => x.IsVisible);
        b?.caixa.Focus();
    }
}
