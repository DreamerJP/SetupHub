using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Instalador;

static class Programa
{
    // --automatico: instala sem esperar clique e fecha no fim, com o código de saída da instalação.
    [STAThread]
    static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var janela = new Assistente(args.Contains("--automatico"));
        app.Run(janela);
        return janela.Codigo;
    }
}

static class Cor
{
    static SolidColorBrush C(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); b.Freeze(); return b; }
    public static readonly SolidColorBrush Fundo = C("#0F1317"), Painel = C("#151A20"), Borda = C("#232B34"), Poco = C("#11161C"), Linha = C("#1B222A");
    public static readonly SolidColorBrush Texto = C("#E9EDF2"), Secundario = C("#B3BCC8"), Apagado = C("#8994A2"), Quente = C("#F2643D"), BordaForte = C("#2F3945");
    static readonly Uri Base = new("pack://application:,,,/");
    public static readonly FontFamily Corpo = new(Base, "./Fontes/#Saira Semi Condensed"), Marca = new(Base, "./Fontes/#Saira Extra Condensed");
}

sealed class Assistente : Window
{
    readonly ContentControl conteudo = new();
    readonly StackPanel botoes = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    readonly string? versaoInstalada;
    readonly string? sid;
    readonly string conta;
    bool trabalhando;
    readonly bool automatico;
    public int Codigo { get; private set; } = 1;
    readonly string temporaria = Path.Combine(Path.GetTempPath(), "SetupHubInstalador", Environment.ProcessId.ToString());

    public Assistente(bool automatico)
    {
        this.automatico = automatico;
        if (automatico) Loaded += (_, _) => _ = Instalar();
        Title = $"{Produto.Nome} {Produto.Versao}";
        Width = 640; Height = 440; ResizeMode = ResizeMode.CanMinimize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Cor.Fundo; FontFamily = Cor.Corpo; UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/setuphub.ico")); } catch { }
        SourceInitialized += (_, _) => { int sim = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref sim, 4); };
        Closing += (_, e) => { if (trabalhando) e.Cancel = true; };

        using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + Produto.Nome))
            versaoInstalada = k?.GetValue("DisplayVersion") as string;
        (sid, conta) = ContaDaSessao();

        var marca = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 28, 32, 0) };
        marca.Children.Add(IconeApp.Icone(46));
        var nome = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        nome.Children.Add(new TextBlock { Text = Produto.Nome, FontFamily = Cor.Marca, FontSize = 24, Foreground = Cor.Texto });
        nome.Children.Add(T($"Versão {Produto.Versao}", 13, Cor.Apagado));
        marca.Children.Add(nome);

        conteudo.Margin = new Thickness(32, 26, 32, 20);
        var rodape = new Border { BorderBrush = Cor.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Background = Cor.Painel, Padding = new Thickness(24, 14, 24, 14), Child = botoes };

        var g = new Grid { Background = Cor.Fundo };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(conteudo, 1); Grid.SetRow(rodape, 2);
        g.Children.Add(marca); g.Children.Add(conteudo); g.Children.Add(rodape);
        Content = g;
        BoasVindas();
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int atributo, ref int valor, int tamanho);
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)] static extern bool WTSQuerySessionInformationW(IntPtr servidor, int sessao, int classe, out IntPtr dados, out int bytes);
    [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr p);
    [DllImport("kernel32.dll")] static extern uint GetOEMCP();

    // A conta liberada é a da sessão aberta, não a que digitou a senha de administrador no aviso do Windows.
    static (string? sid, string nome) ContaDaSessao()
    {
        string? Info(int classe)
        {
            if (!WTSQuerySessionInformationW(IntPtr.Zero, Process.GetCurrentProcess().SessionId, classe, out var p, out _)) return null;
            try { return Marshal.PtrToStringUni(p); } finally { WTSFreeMemory(p); }
        }
        try
        {
            if (Info(5) is { Length: > 0 } usuario)
            {
                var sid = (SecurityIdentifier)new NTAccount(Info(7) ?? "", usuario).Translate(typeof(SecurityIdentifier));
                return (sid.Value, usuario);
            }
        }
        catch { }
        var eu = WindowsIdentity.GetCurrent();
        return (eu.User?.Value, eu.Name.Split('\\').Last());
    }

    static TextBlock T(string texto, double tamanho, Brush cor, FontWeight? peso = null) =>
        new() { Text = texto, FontSize = tamanho, Foreground = cor, FontWeight = peso ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };

    static Border Botao(string texto, bool principal, Action clique)
    {
        var t = T(texto, 14, principal ? Cor.Fundo : Cor.Texto, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border
        {
            Child = t, MinWidth = 120, Height = 36, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(10, 0, 0, 0), CornerRadius = new CornerRadius(8),
            Background = principal ? Cor.Texto : Brushes.Transparent, BorderBrush = principal ? Cor.Texto : Cor.BordaForte, BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
        };
        b.MouseEnter += (_, _) => b.Opacity = 0.88;
        b.MouseLeave += (_, _) => b.Opacity = 1;
        b.MouseLeftButtonUp += (_, _) => clique();
        return b;
    }

    static CheckBox CaixaSelecao(string texto, bool marcada = true)
    {
        var raiz = new FrameworkElementFactory(typeof(StackPanel));
        raiz.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        raiz.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);

        var caixa = new FrameworkElementFactory(typeof(Border));
        caixa.Name = "caixa";
        caixa.SetValue(FrameworkElement.WidthProperty, 18.0);
        caixa.SetValue(FrameworkElement.HeightProperty, 18.0);
        caixa.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        caixa.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        caixa.SetValue(Border.BorderBrushProperty, Cor.BordaForte);
        caixa.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        caixa.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

        var marca = new FrameworkElementFactory(typeof(TextBlock));
        marca.Name = "marca";
        marca.SetValue(TextBlock.TextProperty, "✓");
        marca.SetValue(TextBlock.FontFamilyProperty, Cor.Corpo);
        marca.SetValue(TextBlock.FontSizeProperty, 13.0);
        marca.SetValue(TextBlock.ForegroundProperty, Cor.Fundo);
        marca.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        marca.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        marca.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        caixa.AppendChild(marca);
        raiz.AppendChild(caixa);

        var rotulo = new FrameworkElementFactory(typeof(TextBlock));
        rotulo.SetValue(TextBlock.TextProperty, texto);
        rotulo.SetValue(TextBlock.FontFamilyProperty, Cor.Corpo);
        rotulo.SetValue(TextBlock.FontSizeProperty, 14.0);
        rotulo.SetValue(TextBlock.ForegroundProperty, Cor.Texto);
        rotulo.SetValue(FrameworkElement.MarginProperty, new Thickness(9, 0, 0, 0));
        rotulo.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        raiz.AppendChild(rotulo);

        var modelo = new ControlTemplate(typeof(CheckBox)) { VisualTree = raiz };
        var marcado = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        marcado.Setters.Add(new Setter(Border.BackgroundProperty, Cor.Texto, "caixa"));
        marcado.Setters.Add(new Setter(Border.BorderBrushProperty, Cor.Texto, "caixa"));
        marcado.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "marca"));
        modelo.Triggers.Add(marcado);
        var sobre = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        sobre.Setters.Add(new Setter(Border.BorderBrushProperty, Cor.Texto, "caixa"));
        modelo.Triggers.Add(sobre);

        return new CheckBox { Content = texto, IsChecked = marcada, IsThreeState = false, Template = modelo, Cursor = Cursors.Hand, Focusable = true };
    }

    static Grid Par(string rotulo, string valor)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var v = T(valor, 14, Cor.Texto); Grid.SetColumn(v, 1);
        g.Children.Add(T(rotulo, 14, Cor.Apagado)); g.Children.Add(v);
        return g;
    }

    void Botoes(params Border[] b) { botoes.Children.Clear(); foreach (var x in b) botoes.Children.Add(x); }

    void BoasVindas()
    {
        bool atualiza = versaoInstalada is not null;
        var p = new StackPanel();
        p.Children.Add(T(atualiza ? $"Atualizar o {Produto.Nome}" : $"Instalar o {Produto.Nome}", 20, Cor.Texto, FontWeights.SemiBold));
        var desc = T("Ventoinhas, luz, processador, placa de vídeo, telas e energia num só programa.", 14, Cor.Secundario);
        desc.Margin = new Thickness(0, 6, 0, 22);
        p.Children.Add(desc);
        p.Children.Add(Par("Pasta", Caminhos.Instalado));
        p.Children.Add(Par("Conta", conta));
        if (atualiza) p.Children.Add(Par("Versão instalada", $"{versaoInstalada}, arquivos substituídos e configuração mantida"));
        if (sid is null) p.Children.Add(T("Conta da sessão não identificada: a instalação não pode continuar.", 13.5, Cor.Quente));
        conteudo.Content = p;
        var instalar = Botao(atualiza ? "Atualizar" : "Instalar", true, () => _ = Instalar());
        if (sid is null) { instalar.IsEnabled = false; instalar.Opacity = 0.4; }
        Botoes(Botao("Cancelar", false, Close), instalar);
    }

    TextBlock etapa = null!;
    Border barra = null!;
    Grid trilho = null!;
    StackPanel linhas = null!;
    ScrollViewer rolagem = null!;

    void TelaProgresso()
    {
        var p = new StackPanel();
        p.Children.Add(T(versaoInstalada is null ? "Instalando" : "Atualizando", 20, Cor.Texto, FontWeights.SemiBold));
        etapa = T("Preparando", 14, Cor.Secundario); etapa.Margin = new Thickness(0, 6, 0, 14);
        p.Children.Add(etapa);
        barra = new Border { Background = Cor.Texto, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        trilho = new Grid { Height = 6, Children = { new Border { Background = Cor.Borda, CornerRadius = new CornerRadius(3) }, barra } };
        p.Children.Add(trilho);
        linhas = new StackPanel();
        rolagem = new ScrollViewer { Content = linhas, Height = 120, Margin = new Thickness(0, 16, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        p.Children.Add(rolagem);
        conteudo.Content = p;
        Botoes();
    }

    void Andamento(double fracao, string? texto = null)
    {
        Dispatcher.Invoke(() =>
        {
            barra.Width = Math.Clamp(fracao, 0, 1) * trilho.ActualWidth;
            if (texto is not null) etapa.Text = texto;
        });
    }

    void Linha(string texto)
    {
        Dispatcher.Invoke(() =>
        {
            linhas.Children.Add(T(texto, 12.5, Cor.Apagado));
            rolagem.ScrollToEnd();
        });
    }

    async Task Instalar()
    {
        trabalhando = true;
        TelaProgresso();
        await Task.Yield();
        int codigo;
        var saida = new List<string>();
        try
        {
            await Task.Run(Extrair);
            Andamento(0.55, "Instalando o serviço, os kits e o RivaTuner");
            codigo = await Task.Run(() => RodarInstalacao(saida));
        }
        catch (Exception e) { saida.Add(e.Message); codigo = -1; }
        try { Directory.Delete(temporaria, true); } catch { }
        trabalhando = false;
        Codigo = codigo;
        if (codigo == 0) Concluido(); else Falhou(saida, codigo);
        if (automatico) { await Task.Delay(1500); Close(); }
    }

    void Extrair()
    {
        Andamento(0, "Extraindo os arquivos");
        Directory.CreateDirectory(temporaria);
        var raiz = Path.GetFullPath(temporaria) + Path.DirectorySeparatorChar;
        using var s = typeof(Assistente).Assembly.GetManifestResourceStream("pacote.zip") ?? throw new InvalidOperationException("O pacote do programa não está dentro deste instalador.");
        using var z = new ZipArchive(s, ZipArchiveMode.Read);
        long total = Math.Max(1, z.Entries.Sum(e => e.Length)), feito = 0;
        foreach (var e in z.Entries)
        {
            var destino = Path.GetFullPath(Path.Combine(temporaria, e.FullName));
            if (!destino.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) continue;
            if (e.FullName.EndsWith('/')) { Directory.CreateDirectory(destino); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            e.ExtractToFile(destino, true);
            feito += e.Length;
            Andamento(0.5 * feito / total);
        }
    }

    // As linhas do serviço viram o andamento; a quantidade usual de etapas dá a proporção da barra.
    int RodarInstalacao(List<string> saida)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var psi = new ProcessStartInfo(Path.Combine(temporaria, Produto.ExeServico))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = temporaria,
            StandardOutputEncoding = Encoding.GetEncoding((int)GetOEMCP()), StandardErrorEncoding = Encoding.GetEncoding((int)GetOEMCP()),
        };
        psi.ArgumentList.Add("instalar"); psi.ArgumentList.Add(sid!);
        if (!automatico) psi.ArgumentList.Add("--nao-abrir");
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("O instalador do serviço não abriu.");
        p.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l) lock (saida) saida.Add(l); };
        p.BeginErrorReadLine();
        int n = 0;
        while (p.StandardOutput.ReadLine() is string l)
        {
            if (l.Trim().Length == 0) continue;
            lock (saida) saida.Add(l);
            Linha(l.Trim());
            Andamento(0.55 + 0.43 * Math.Min(1, ++n / 18.0));
        }
        p.WaitForExit();
        Andamento(1);
        return p.ExitCode;
    }

    void Concluido()
    {
        var p = new StackPanel();
        p.Children.Add(T($"{Produto.Nome} {Produto.Versao} instalado", 20, Cor.Texto, FontWeights.SemiBold));
        var t = T("Serviço ativo. Você pode abrir o painel agora ou mais tarde pelo menu Iniciar.", 14, Cor.Secundario);
        t.Margin = new Thickness(0, 6, 0, 14);
        p.Children.Add(t);
        var abrir = CaixaSelecao($"Abrir o {Produto.Nome} ao concluir");
        abrir.Margin = new Thickness(0, 0, 0, 18);
        p.Children.Add(abrir);
        p.Children.Add(Par("Remoção", "Configurações do Windows, Aplicativos instalados"));
        conteudo.Content = p;
        Botoes(Botao("Concluir", true, () => { if (abrir.IsChecked == true) AbrirPainel(); Close(); }));
    }

    static void AbrirPainel()
    {
        try
        {
            var inicio = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            inicio.ArgumentList.Add(Path.Combine(Caminhos.Instalado, Produto.ExePainel));
            Process.Start(inicio);
        }
        catch { }
    }

    void Falhou(List<string> saida, int codigo)
    {
        var p = new StackPanel();
        p.Children.Add(T("A instalação não terminou", 20, Cor.Quente, FontWeights.SemiBold));
        var erro = saida.LastOrDefault(l => l.Contains("ERRO", StringComparison.OrdinalIgnoreCase)) ?? saida.LastOrDefault() ?? $"Código {codigo}.";
        var t = T(erro.Replace("ERRO: ", ""), 14, Cor.Secundario);
        t.Margin = new Thickness(0, 6, 0, 22);
        p.Children.Add(t);
        p.Children.Add(Par("Registro", Path.Combine(Caminhos.Dados, "instalacao.txt")));
        conteudo.Content = p;
        Botoes(Botao("Abrir registro", false, () => { try { Process.Start(new ProcessStartInfo(Path.Combine(Caminhos.Dados, "instalacao.txt")) { UseShellExecute = true }); } catch { } }), Botao("Fechar", true, Close));
    }
}
