using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using SetupHub.Janela.Kit;

namespace SetupHub.Janela;

sealed class NotificacaoRapida : IDisposable
{
    const int EstiloExtra = -20;
    const int NaoAtivar = 0x08000000;
    const int Ferramenta = 0x00000080;
    const int Transparente = 0x00000020;
    const int MouseAtivar = 0x0021;
    const int TestarClique = 0x0084;
    const int SemAtivar = 3;
    const int CliqueAtravessa = -1;
    const uint MonitorMaisProximo = 2;
    const uint SemRedimensionar = 0x0001;
    const uint SemAtivarJanela = 0x0010;
    const uint MostrarJanela = 0x0040;
    static readonly IntPtr SempreNoTopo = new(-1);

    Window? janela;
    DispatcherTimer? relogio;
    string titulo = "", texto = "";
    long solicitacaoAtual;
    bool falhou;
    bool descartada;
    int exibicao;

    public void MostrarPlanoPendente(string plano, long solicitacao)
    {
        if (solicitacao <= solicitacaoAtual || !PodeMostrar()) return;
        AtualizarPlano(plano, solicitacao);
        relogio?.Stop();
        exibicao++;
    }

    public void ConfirmarPlano(string plano, long solicitacao)
    {
        if (descartada || solicitacao < solicitacaoAtual || (solicitacao > solicitacaoAtual && !PodeMostrar())) return;
        if (solicitacao > solicitacaoAtual || falhou) AtualizarPlano(plano, solicitacao);
        falhou = false;
        Manter(1400);
    }

    public void MostrarFalhaPlano(string plano, long solicitacao)
    {
        if (descartada || solicitacao < solicitacaoAtual || (solicitacao > solicitacaoAtual && !PodeMostrar())) return;
        solicitacaoAtual = solicitacao;
        titulo = "Não foi possível aplicar";
        texto = plano;
        falhou = true;
        AtualizarJanela();
        Manter(4000);
    }

    bool PodeMostrar() => !descartada && !(App.Lig.Config?.Windows.NaoPerturbeEmJogo == true && App.Lig.Estado?.Jogo is not null);

    void AtualizarPlano(string plano, long solicitacao)
    {
        solicitacaoAtual = solicitacao;
        titulo = "Plano de energia";
        texto = plano;
        falhou = false;
        AtualizarJanela();
    }

    void Criar()
    {
        if (janela is not null) return;
        janela = new Window
        {
            MinWidth = 190,
            MaxWidth = 340,
            Width = 190,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            Topmost = true,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Left = -10000,
            Top = -10000,
        };
        janela.SourceInitialized += (_, _) => PrepararAlca();
        relogio = new DispatcherTimer();
        relogio.Tick += (_, _) => Esconder();
    }

    void AtualizarJanela()
    {
        Criar();
        var conteudo = Conteudo();
        conteudo.Measure(new Size(340, double.PositiveInfinity));
        janela!.Width = Math.Clamp(Math.Ceiling(conteudo.DesiredSize.Width), 190, 340);
        janela.Content = conteudo;
        janela.BeginAnimation(UIElement.OpacityProperty, null);
        janela.Opacity = 1;
        if (!janela.IsVisible) janela.Show();
        janela.UpdateLayout();
        Posicionar(MonitorAtual());
    }

    UIElement Conteudo()
    {
        var p = K.P;
        var corTitulo = falhou ? p.Quente : p.Apagado;
        var titulo = K.T(this.titulo, 11.5, corTitulo, FontWeights.SemiBold);
        var texto = K.T(this.texto, 19, p.Texto, p.PesoNumero, p.Numero);
        texto.MaxWidth = 240;
        texto.TextTrimming = TextTrimming.CharacterEllipsis;
        var informacao = K.Col(1, titulo, texto);
        informacao.MaxWidth = 240;
        informacao.VerticalAlignment = VerticalAlignment.Center;
        var icone = IconeApp.Icone(28);
        icone.VerticalAlignment = VerticalAlignment.Center;
        var grade = K.Grade("28 auto", 11, icone, informacao);
        var cartao = new Border
        {
            Background = p.Painel,
            BorderBrush = p.Borda,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(p.RaioCartao),
            Padding = new Thickness(14, 9, 14, 9),
            Child = grade,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 7, ShadowDepth = 1, Opacity = p.Claro ? 0.06 : 0.14 },
        };
        AutomationProperties.SetName(cartao, $"{this.titulo}: {this.texto}");
        AutomationProperties.SetLiveSetting(cartao, AutomationLiveSetting.Polite);
        return new Border { Background = Brushes.Transparent, Padding = new Thickness(6), Child = cartao };
    }

    void Manter(int milissegundos)
    {
        exibicao++;
        relogio!.Stop();
        relogio.Interval = TimeSpan.FromMilliseconds(milissegundos);
        relogio.Start();
    }

    void PrepararAlca()
    {
        if (janela is null) return;
        var alca = new WindowInteropHelper(janela).Handle;
        var estilo = GetWindowLongPtr(alca, EstiloExtra).ToInt64() | NaoAtivar | Ferramenta | Transparente;
        SetWindowLongPtr(alca, EstiloExtra, new IntPtr(estilo));
        HwndSource.FromHwnd(alca)?.AddHook(Mensagem);
    }

    IntPtr Mensagem(IntPtr alca, int mensagem, IntPtr wParam, IntPtr lParam, ref bool tratado)
    {
        if (mensagem == MouseAtivar) { tratado = true; return new IntPtr(SemAtivar); }
        if (mensagem == TestarClique) { tratado = true; return new IntPtr(CliqueAtravessa); }
        return IntPtr.Zero;
    }

    void Posicionar(IntPtr monitor)
    {
        if (janela is null) return;
        var alca = new WindowInteropHelper(janela).Handle;
        if (alca == IntPtr.Zero || !GetWindowRect(alca, out var retangulo)) return;
        var info = new InfoMonitor { Tamanho = Marshal.SizeOf<InfoMonitor>() };
        if (!GetMonitorInfoW(monitor, ref info)) return;
        SetWindowPos(alca, SempreNoTopo, info.Trabalho.Direita - (retangulo.Direita - retangulo.Esquerda) - 16, info.Trabalho.Base - (retangulo.Base - retangulo.Topo) - 16, 0, 0, SemRedimensionar | SemAtivarJanela | MostrarJanela);
    }

    void Esconder()
    {
        relogio?.Stop();
        if (janela is not { IsVisible: true } atual) return;
        int serie = exibicao;
        var saida = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        saida.Completed += (_, _) =>
        {
            if (serie != exibicao || janela is not { IsVisible: true } visivel) return;
            visivel.Hide();
            visivel.Opacity = 1;
        };
        atual.BeginAnimation(UIElement.OpacityProperty, saida);
    }

    public void Refazer()
    {
        if (janela is null) return;
        var conteudo = Conteudo();
        conteudo.Measure(new Size(340, double.PositiveInfinity));
        janela.Width = Math.Clamp(Math.Ceiling(conteudo.DesiredSize.Width), 190, 340);
        janela.Content = conteudo;
        if (!janela.IsVisible) return;
        janela.UpdateLayout();
        Posicionar(MonitorAtual());
    }

    public void Dispose()
    {
        descartada = true;
        relogio?.Stop();
        janela?.Close();
        janela = null;
    }

    static IntPtr MonitorAtual()
    {
        var monitor = MonitorFromWindow(GetForegroundWindow(), MonitorMaisProximo);
        if (monitor != IntPtr.Zero) return monitor;
        GetCursorPos(out var ponto);
        return MonitorFromPoint(ponto, MonitorMaisProximo);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Ponto
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Retangulo
    {
        public int Esquerda, Topo, Direita, Base;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct InfoMonitor
    {
        public int Tamanho;
        public Retangulo Monitor, Trabalho;
        public uint Bandeiras;
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr janela, uint bandeiras);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Ponto ponto, uint bandeiras);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Ponto ponto);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr monitor, ref InfoMonitor info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr janela, out Retangulo retangulo);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr janela, IntPtr depois, int x, int y, int largura, int altura, uint bandeiras);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr janela, int indice);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr janela, int indice, IntPtr valor);
}
