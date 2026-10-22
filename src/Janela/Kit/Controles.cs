using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SetupHub.Janela.Kit;

class Botao : Border
{
    public event Action<Botao, MouseButtonEventArgs?>? Clique;
    public Brush? FundoNormal, FundoSobre, BordaNormal, BordaSobre;
    bool apertado;
    public object? Dado;

    public Botao()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        SnapsToDevicePixels = true;
        MouseEnter += (_, _) => Pintar(true);
        MouseLeave += (_, _) => { apertado = false; Pintar(false); };
        MouseLeftButtonDown += (_, e) => { apertado = true; e.Handled = true; Focus(); };
        MouseLeftButtonUp += (_, e) => { if (apertado && IsEnabled) Clique?.Invoke(this, e); apertado = false; e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space && IsEnabled) { Clique?.Invoke(this, null); e.Handled = true; } };
        IsEnabledChanged += (_, _) => { Opacity = IsEnabled ? 1 : 0.38; Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow; };
        GotKeyboardFocus += (_, _) => { if (InputManager.Current.MostRecentInputDevice is KeyboardDevice) BorderBrush = K.P.Texto; };
        LostKeyboardFocus += (_, _) => Pintar(IsMouseOver);
    }

    public void Pintar(bool sobre)
    {
        if (FundoNormal is not null) Background = sobre && FundoSobre is not null ? FundoSobre : FundoNormal;
        if (BordaNormal is not null) BorderBrush = sobre && BordaSobre is not null ? BordaSobre : BordaNormal;
    }

    public void Fundo(Brush b) { FundoNormal = b; Background = b; }

    public static Botao Contorno(string texto, Action aoClicar, double altura = 32)
    {
        var b = new Botao
        {
            Height = K.P.E(altura), CornerRadius = new CornerRadius(K.P.RaioBotao), BorderThickness = new Thickness(1),
            Child = Texto(texto, K.P.Secundario, FontWeights.Normal),
        };
        b.FundoNormal = Brushes.Transparent; b.Background = Brushes.Transparent;
        b.FundoSobre = K.P.Poco;
        b.BordaNormal = K.P.BordaForte; b.BorderBrush = K.P.BordaForte; b.BordaSobre = K.P.SubSel;
        b.Clique += (_, _) => aoClicar();
        return b;
    }

    public static Botao Cheio(string texto, Action aoClicar, double altura = 32)
    {
        var b = new Botao
        {
            Height = K.P.E(altura), CornerRadius = new CornerRadius(K.P.RaioBotao), BorderThickness = new Thickness(1),
            Child = Texto(texto, K.P.TextoSelecao, FontWeights.SemiBold),
        };
        b.FundoNormal = K.P.Selecao; b.Background = K.P.Selecao; b.BordaNormal = K.P.Selecao; b.BorderBrush = K.P.Selecao;
        b.Clique += (_, _) => aoClicar();
        return b;
    }

    public static TextBlock Texto(string texto, Brush cor, FontWeight peso, double tamanho = 14) => new()
    {
        Text = texto, Foreground = cor, FontWeight = peso, FontFamily = K.P.Corpo, FontSize = K.P.F(tamanho),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(6, 0, 6, 0),
    };

    public string TextoBotao
    {
        get => (Child as TextBlock)?.Text ?? "";
        set { if (Child is TextBlock t) t.Text = value; }
    }
}

sealed class Segmentado : Border
{
    readonly List<Botao> botoes = new();
    readonly List<string> valores = new();
    public event Action<string>? Escolheu;
    public string? Valor { get; private set; }
    readonly double altura;
    readonly bool poco;

    public Segmentado(IEnumerable<(string valor, string texto)> opcoes, double altura = 34, double larguraBotao = double.NaN, bool? poco = null)
    {
        this.altura = altura;
        this.poco = poco ?? true;
        var itens = opcoes.ToList();
        var g = new Grid();
        for (int i = 0; i < itens.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(larguraBotao) ? new GridLength(1, GridUnitType.Star) : new GridLength(larguraBotao) });
            if (i < itens.Count - 1) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(this.poco ? 2 : 4) });
            var (v, t) = itens[i];
            var b = new Botao { Height = K.P.E(altura), CornerRadius = new CornerRadius(K.P.RaioBotao), Child = Botao.Texto(t, K.P.Secundario, FontWeights.Medium), Dado = v };
            if (!this.poco) { b.BorderThickness = new Thickness(1); b.BordaNormal = K.P.Borda; b.BorderBrush = K.P.Borda; }
            b.FundoNormal = Brushes.Transparent; b.Background = Brushes.Transparent; b.FundoSobre = K.P.Poco;
            b.Clique += (x, _) => { Selecionar((string)x.Dado!); Escolheu?.Invoke((string)x.Dado!); };
            Grid.SetColumn(b, i * 2);
            g.Children.Add(b);
            botoes.Add(b); valores.Add(v);
        }
        Child = g;
        if (this.poco)
        {
            Background = K.P.PocoEscuro; Padding = new Thickness(3); CornerRadius = new CornerRadius(K.P.Raio);
        }
        HorizontalAlignment = double.IsNaN(larguraBotao) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
    }

    public void Selecionar(string? valor)
    {
        Valor = valor;
        for (int i = 0; i < botoes.Count; i++)
        {
            bool on = valores[i] == valor;
            var b = botoes[i];
            var t = (TextBlock)b.Child;
            b.FundoNormal = on ? K.P.Selecao : Brushes.Transparent;
            b.FundoSobre = on ? K.P.Selecao : K.P.Poco;
            b.Background = b.FundoNormal;
            if (!poco) { b.BordaNormal = on ? K.P.Selecao : K.P.Borda; b.BorderBrush = b.BordaNormal; }
            t.Foreground = on ? K.P.TextoSelecao : K.P.Secundario;
            t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Medium;
        }
    }

    public void Habilitar(string valor, bool sim)
    {
        int i = valores.IndexOf(valor);
        if (i >= 0) botoes[i].IsEnabled = sim;
    }
}

sealed class Chave : Border
{
    readonly Border bola;
    public event Action<bool>? Mudou;
    bool ligada;
    public bool Ligada { get => ligada; set { ligada = value; Pintar(); } }

    public Chave(bool inicial = false)
    {
        Width = 40; Height = 22;
        CornerRadius = new CornerRadius(Height / 2);
        Padding = new Thickness(2);
        Cursor = Cursors.Hand;
        Focusable = true;
        FocusVisualStyle = null;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        bola = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9) };
        Child = bola;
        ligada = inicial;
        Pintar();
        MouseLeftButtonUp += (_, e) => { if (!IsEnabled) return; Ligada = !Ligada; Mudou?.Invoke(Ligada); e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter && IsEnabled) { Ligada = !Ligada; Mudou?.Invoke(Ligada); e.Handled = true; } };
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.38;
    }

    void Pintar()
    {
        Background = ligada ? K.P.Selecao : K.P.BordaForte;
        bola.Background = ligada ? K.P.TextoSelecao : K.P.Apagado;
        bola.HorizontalAlignment = ligada ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }
}

// Mudou só avisa ao soltar; Movendo avisa durante o arraste.
sealed class Deslizante : FrameworkElement
{
    public double Min, Max = 100, Passo = 1;
    double valor;
    bool arrastando;
    public event Action<double>? Mudou;
    public event Action<double>? Movendo;

    public Deslizante(double min, double max, double passo, double inicial)
    {
        Min = min; Max = max; Passo = passo; valor = inicial;
        Height = 22; Cursor = Cursors.Hand; Focusable = true; FocusVisualStyle = null;
        VerticalAlignment = VerticalAlignment.Center;
        MouseLeftButtonDown += (_, e) => { arrastando = true; CaptureMouse(); Mover(e.GetPosition(this).X); e.Handled = true; };
        MouseMove += (_, e) => { if (arrastando) Mover(e.GetPosition(this).X); };
        MouseLeftButtonUp += (_, e) => { if (!arrastando) return; Mover(e.GetPosition(this).X); arrastando = false; ReleaseMouseCapture(); Mudou?.Invoke(valor); };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Down) { Valor = valor - Passo; Mudou?.Invoke(valor); e.Handled = true; }
            if (e.Key is Key.Right or Key.Up) { Valor = valor + Passo; Mudou?.Invoke(valor); e.Handled = true; }
        };
        IsEnabledChanged += (_, _) => { Opacity = IsEnabled ? 1 : 0.38; InvalidateVisual(); };
    }

    public double Valor
    {
        get => valor;
        set { valor = Math.Clamp(Math.Round((value - Min) / Passo) * Passo + Min, Min, Max); InvalidateVisual(); }
    }

    void Mover(double x)
    {
        double w = Math.Max(1, ActualWidth - 14);
        Valor = Min + Math.Clamp((x - 7) / w, 0, 1) * (Max - Min);
        Movendo?.Invoke(valor);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, cy = ActualHeight / 2, x0 = 7, x1 = Math.Max(x0, w - 7);
        double f = Max > Min ? (valor - Min) / (Max - Min) : 0;
        double x = x0 + (x1 - x0) * f;
        dc.DrawRoundedRectangle(K.P.BordaForte, null, new Rect(x0, cy - 2, x1 - x0, 4), 2, 2);
        dc.DrawRoundedRectangle(K.P.Texto, null, new Rect(x0, cy - 2, x - x0, 4), 2, 2);
        dc.DrawEllipse(K.P.Texto, new Pen(K.P.Painel, 2), new Point(x, cy), 7, 7);
        if (IsKeyboardFocused) dc.DrawEllipse(null, new Pen(K.P.Secundario, 1), new Point(x, cy), 10, 10);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight));
    }
}

// Sem faixa lida do hardware fica travado.
sealed class CampoNum : Border
{
    readonly TextBox caixa;
    public event Action<double?>? Confirmou;
    double? ultimo;
    public bool AceitaVazio = true;

    public CampoNum(double? valor, string unidade = "", double largura = 90, double altura = 32)
    {
        caixa = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = K.P.Texto, CaretBrush = K.P.Texto,
            FontFamily = K.P.Corpo, FontSize = K.P.F(14), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 2, 0),
            SelectionBrush = K.P.SubSel,
        };
        var un = K.T(unidade, 12.5, K.P.Apagado);
        un.Margin = new Thickness(0, 0, 8, 0);
        Child = K.Grade("* auto", 0, caixa, unidade.Length > 0 ? un : null);
        Width = largura; Height = K.P.E(altura);
        CornerRadius = new CornerRadius(6);
        BorderBrush = K.P.BordaForte; BorderThickness = new Thickness(1); Background = K.P.PocoEscuro;
        Definir(valor);
        caixa.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Confirmar(); e.Handled = true; } if (e.Key == Key.Escape) { Definir(ultimo); e.Handled = true; } };
        caixa.LostKeyboardFocus += (_, _) => Confirmar();
        caixa.GotKeyboardFocus += (_, _) => { BorderBrush = K.P.Secundario; caixa.SelectAll(); };
        caixa.LostFocus += (_, _) => BorderBrush = K.P.BordaForte;
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.38;
    }

    public void Definir(double? v)
    {
        ultimo = v;
        if (!caixa.IsKeyboardFocused) caixa.Text = v is double d ? d.ToString("0.###", K.Br) : "";
    }

    void Confirmar()
    {
        var t = caixa.Text.Trim().Replace('.', ',');
        double? v = null;
        if (t.Length > 0)
        {
            if (!double.TryParse(t, NumberStyles.Float, K.Br, out var d)) { Definir(ultimo); return; }
            v = d;
        }
        else if (!AceitaVazio) { Definir(ultimo); return; }
        if (v == ultimo) return;
        ultimo = v;
        Confirmou?.Invoke(v);
    }

    public string Dica { set => ToolTip = value; }
}

// Texto que não passa na conferência volta ao anterior.
sealed class CampoTexto : Border
{
    readonly TextBox caixa;
    string ultimo = "";
    public event Action<string>? Confirmou;
    public event Action<string>? Editou;
    public Func<string, bool> Aceita = _ => true;

    public CampoTexto(string valor, double largura = 90, double altura = 32)
    {
        caixa = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = K.P.Texto, CaretBrush = K.P.Texto,
            FontFamily = K.P.Corpo, FontSize = K.P.F(14), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0),
            SelectionBrush = K.P.SubSel,
        };
        Child = caixa;
        Width = largura; Height = K.P.E(altura);
        CornerRadius = new CornerRadius(6);
        BorderBrush = K.P.BordaForte; BorderThickness = new Thickness(1); Background = K.P.PocoEscuro;
        Definir(valor);
        caixa.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Confirmar(); e.Handled = true; } if (e.Key == Key.Escape) { Definir(ultimo); e.Handled = true; } };
        caixa.LostKeyboardFocus += (_, _) => Confirmar();
        caixa.GotKeyboardFocus += (_, _) => { BorderBrush = K.P.Secundario; caixa.SelectAll(); };
        caixa.LostFocus += (_, _) => BorderBrush = K.P.BordaForte;
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.38;
        caixa.TextChanged += (_, _) => Editou?.Invoke(caixa.Text.Trim());
    }

    public void Definir(string v)
    {
        ultimo = v;
        if (!caixa.IsKeyboardFocused) caixa.Text = v;
    }

    public string Text() => caixa.Text.Trim();
    public new void Focus() { caixa.Focus(); caixa.SelectAll(); }

    void Confirmar()
    {
        var t = caixa.Text.Trim();
        if (t == ultimo) return;
        if (!Aceita(t)) { caixa.Text = ultimo; return; }
        ultimo = t;
        Confirmou?.Invoke(t);
    }
}

sealed class MenuAcoes : Botao
{
    readonly Popup lista;
    readonly StackPanel itens = new();

    public MenuAcoes(string texto, IEnumerable<(string texto, Action acao)> acoes, double altura = 32)
    {
        Height = K.P.E(altura);
        CornerRadius = new CornerRadius(K.P.RaioBotao);
        BorderThickness = new Thickness(1); BordaNormal = K.P.BordaForte; BorderBrush = K.P.BordaForte; BordaSobre = K.P.SubSel;
        FundoNormal = Brushes.Transparent; Background = Brushes.Transparent; FundoSobre = K.P.Poco;
        Child = Texto(texto, K.P.Secundario, FontWeights.Normal);
        foreach (var (t, a) in acoes)
        {
            var item = new Botao { Height = K.P.E(30), CornerRadius = new CornerRadius(K.P.RaioBotao - 2), FundoNormal = Brushes.Transparent, FundoSobre = K.P.Poco, Background = Brushes.Transparent };
            var tx = K.T(t, 14); tx.Margin = new Thickness(8, 0, 8, 0);
            item.Child = tx;
            item.Clique += (_, _) => { lista!.IsOpen = false; a(); };
            itens.Children.Add(item);
        }
        lista = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Background = K.P.Painel, BorderBrush = K.P.BordaForte, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(K.P.RaioBotao), Padding = new Thickness(3), Child = itens },
        };
        Clique += (_, _) => { if (lista.Child is Border b) b.MinWidth = ActualWidth; lista.IsOpen = !lista.IsOpen; };
    }
}

sealed class Escolha : Botao
{
    readonly TextBlock rotulo;
    readonly Popup lista;
    readonly StackPanel itens;
    public event Action<string>? Escolheu;
    public string? Valor { get; private set; }
    readonly List<(string valor, string texto)> opcoes = new();

    public Escolha(IEnumerable<(string valor, string texto)> ops, string? atual, double altura = 32)
    {
        Height = K.P.E(altura);
        CornerRadius = new CornerRadius(K.P.RaioBotao);
        BorderThickness = new Thickness(1); BordaNormal = K.P.BordaForte; BorderBrush = K.P.BordaForte; BordaSobre = K.P.SubSel;
        FundoNormal = K.P.PocoEscuro; Background = K.P.PocoEscuro;
        rotulo = K.T("", 14);
        rotulo.Margin = new Thickness(8, 0, 4, 0);
        var seta = K.IconeCaixa("M6 9l6 6 6-6", 14, K.P.Apagado, 2);
        seta.Margin = new Thickness(0, 0, 8, 0);
        Child = K.Grade("* auto", 0, rotulo, seta);
        itens = new StackPanel();
        lista = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Background = K.P.Painel, BorderBrush = K.P.BordaForte, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(K.P.RaioBotao), Padding = new Thickness(3), Child = new ScrollViewer { Content = itens, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } },
        };
        Opcoes(ops, atual);
        Clique += (_, _) => { if (lista.Child is Border b) b.MinWidth = ActualWidth; lista.IsOpen = !lista.IsOpen; };
    }

    public void Opcoes(IEnumerable<(string valor, string texto)> ops, string? atual)
    {
        opcoes.Clear(); opcoes.AddRange(ops);
        itens.Children.Clear();
        foreach (var (v, t) in opcoes)
        {
            var item = new Botao { Height = K.P.E(30), CornerRadius = new CornerRadius(K.P.RaioBotao - 2), Dado = v, FundoNormal = Brushes.Transparent, FundoSobre = K.P.Poco, Background = Brushes.Transparent };
            var tx = K.T(t, 14); tx.Margin = new Thickness(8, 0, 8, 0);
            item.Child = tx;
            item.Clique += (x, _) => { lista.IsOpen = false; Selecionar((string)x.Dado!); Escolheu?.Invoke((string)x.Dado!); };
            itens.Children.Add(item);
        }
        Selecionar(atual);
    }

    public void Selecionar(string? valor)
    {
        Valor = valor;
        var o = opcoes.FirstOrDefault(x => x.valor == valor);
        rotulo.Text = o.texto ?? (valor is null ? "vários" : valor);
    }
}

sealed class Barra : Border
{
    readonly Border cheio;
    public Barra(Brush cor, double altura = 4)
    {
        Height = altura; CornerRadius = new CornerRadius(altura / 2); Background = K.P.Trilho; ClipToBounds = true;
        cheio = new Border { Background = cor, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(altura / 2), Width = 0 };
        Child = cheio;
        SizeChanged += (_, _) => Aplicar(fracao, false);
    }
    double fracao;
    public void Valor(double? f, Brush? cor = null)
    {
        if (f is not double valor || !double.IsFinite(valor)) { Aplicar(0, false); return; }
        if (cor is not null) cheio.Background = cor;
        Aplicar(Math.Clamp(valor, 0, 1), true);
    }
    void Aplicar(double f, bool animar)
    {
        fracao = f;
        double alvo = ActualWidth * f;
        if (!animar || ActualWidth <= 0) { cheio.BeginAnimation(WidthProperty, null); cheio.Width = alvo; return; }
        cheio.BeginAnimation(WidthProperty, new DoubleAnimation(alvo, TimeSpan.FromMilliseconds(800)) { EasingFunction = new QuadraticEase() });
    }
}

sealed class Segmentos : FrameworkElement
{
    readonly int n;
    double f;
    public Segmentos(int n = 20, double altura = 6) { this.n = n; Height = altura; }
    public void Valor(double fracao) { f = Math.Clamp(double.IsNaN(fracao) ? 0 : fracao, 0, 1); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        double gap = 2, w = (ActualWidth - gap * (n - 1)) / n;
        int acesos = (int)Math.Round(f * n);
        for (int i = 0; i < n; i++) dc.DrawRectangle(i < acesos ? K.P.Texto : K.P.Linha, null, new Rect(i * (w + gap), 0, Math.Max(0, w), ActualHeight));
    }
}

sealed class Grafico : FrameworkElement
{
    public sealed class Serie { public List<double> V = new(); public Brush Cor = Brushes.White; public double Largura = 2; public DoubleCollection? Traco; }
    public readonly List<Serie> Series = new();
    public double Min, Max = 100;
    public int Capacidade = 120;
    public bool Grade = true;
    public double[] LinhasGrade = { 0, 0.5, 1 };

    public Grafico(double min, double max) { Min = min; Max = max; ClipToBounds = true; }

    public void Empurrar(int serie, double? v)
    {
        var s = Series[serie];
        s.V.Add(v ?? double.NaN);
        if (s.V.Count > Capacidade) s.V.RemoveRange(0, s.V.Count - Capacidade);
    }

    public void Atualizar() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        if (Grade)
        {
            var pg = new Pen(K.P.Trilho, 1);
            foreach (var g in LinhasGrade) { double y = Math.Round(1 + (h - 2) * g) + 0.5; dc.DrawLine(pg, new Point(0, y), new Point(w, y)); }
        }
        foreach (var s in Series)
        {
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                bool aberto = false;
                int n = s.V.Count;
                for (int i = 0; i < n; i++)
                {
                    var v = s.V[i];
                    if (double.IsNaN(v)) { aberto = false; continue; }
                    double x = (Capacidade - n + i) * w / (Capacidade - 1);
                    double y = h - 1 - (Math.Clamp(v, Min, Max) - Min) / (Max - Min) * (h - 2);
                    if (!aberto) { c.BeginFigure(new Point(x, y), false, false); aberto = true; }
                    else c.LineTo(new Point(x, y), true, true);
                }
            }
            geo.Freeze();
            var pen = new Pen(s.Cor, s.Largura) { LineJoin = PenLineJoin.Round, DashStyle = s.Traco is null ? DashStyles.Solid : new DashStyle(s.Traco, 0) };
            dc.DrawGeometry(null, pen, geo);
        }
    }
}

// Abaixo de 60 rpm conta como parada.
sealed class IconeVentoinha : Grid
{
    readonly RotateTransform giro = new();
    double durAtual = -1;
    public IconeVentoinha(double tamanho, Brush? cor = null, bool aro = true)
    {
        Width = tamanho; Height = tamanho;
        var cv = new Canvas { Width = 40, Height = 40 };
        if (aro) cv.Children.Add(new Ellipse { Width = 37, Height = 37, Stroke = K.P.BordaForte, StrokeThickness = 1, Margin = new Thickness(1.5) });
        var pas = new Path { Data = DiagramaAr.Pas(aro ? 16 : 19), Fill = cor ?? K.P.Pa, RenderTransform = giro };
        Canvas.SetLeft(pas, 20); Canvas.SetTop(pas, 20);
        cv.Children.Add(pas);
        Children.Add(new Viewbox { Child = cv });
    }

    public void Rpm(double? rpm)
    {
        double r = rpm ?? 0;
        double dur = r < 60 ? 0 : Math.Round(Math.Max(0.3, 1.8 - r / 1700) * 10) / 10;
        if (Math.Abs(dur - durAtual) < 0.05) return;
        durAtual = dur;
        if (dur == 0) { giro.BeginAnimation(RotateTransform.AngleProperty, null); return; }
        var a = new DoubleAnimation(giro.Angle, giro.Angle + 360, TimeSpan.FromSeconds(dur)) { RepeatBehavior = RepeatBehavior.Forever };
        giro.BeginAnimation(RotateTransform.AngleProperty, a);
    }
}

sealed class Medidor : FrameworkElement
{
    public static readonly DependencyProperty FracaoProperty = DependencyProperty.Register(nameof(Fracao), typeof(double), typeof(Medidor), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Fracao { get => (double)GetValue(FracaoProperty); set => SetValue(FracaoProperty, value); }
    Brush cor;
    public Medidor(Brush cor) { this.cor = cor; Width = 148; Height = 139; }

    public void Valor(double? fracao, Brush c)
    {
        if (fracao is not double valor || !double.IsFinite(valor))
        {
            BeginAnimation(FracaoProperty, null);
            Fracao = 0;
            return;
        }
        cor = c;
        BeginAnimation(FracaoProperty, new DoubleAnimation(Math.Clamp(valor, 0, 1), TimeSpan.FromMilliseconds(900)) { EasingFunction = new QuadraticEase() });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double k = ActualWidth / 160.0;
        dc.PushTransform(new ScaleTransform(k, k));
        var centro = new Point(80, 80);
        Arco(dc, centro, 64, 135, 270, new Pen(K.P.Trilho, 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        if (Fracao > 0.002) Arco(dc, centro, 64, 135, 270 * Fracao, new Pen(cor, 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var pm = new Pen(K.P.Grade, 1.5);
        for (int i = 0; i <= 30; i++)
        {
            double a = (135 + i * 9) * Math.PI / 180, r1 = i % 5 == 0 ? 47 : 51, r2 = 55;
            dc.DrawLine(pm, new Point(80 + r1 * Math.Cos(a), 80 + r1 * Math.Sin(a)), new Point(80 + r2 * Math.Cos(a), 80 + r2 * Math.Sin(a)));
        }
        dc.Pop();
    }

    static void Arco(DrawingContext dc, Point c, double r, double inicio, double varredura, Pen p)
    {
        double a0 = inicio * Math.PI / 180, a1 = (inicio + varredura) * Math.PI / 180;
        var p0 = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
        var p1 = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1));
        var g = new StreamGeometry();
        using (var x = g.Open())
        {
            x.BeginFigure(p0, false, false);
            x.ArcTo(p1, new Size(r, r), 0, varredura > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, p, g);
    }
}

sealed class FitaLed : FrameworkElement
{
    public Color[] Cores = Array.Empty<Color>();
    public double Brilho = 1;
    readonly bool quadrados;
    public FitaLed(bool quadrados) { this.quadrados = quadrados; if (!quadrados) Height = 8; }
    public void Mostrar(Color[] cores, double brilho) { Cores = cores; Brilho = brilho; InvalidateVisual(); if (quadrados) InvalidateMeasure(); }
    protected override Size MeasureOverride(Size s)
    {
        if (!quadrados) return new Size(0, 8);
        int n = Math.Max(1, Cores.Length);
        double w = double.IsInfinity(s.Width) ? 300 : s.Width;
        return new Size(0, (w - 2 * (n - 1)) / n);
    }
    protected override void OnRender(DrawingContext dc)
    {
        int n = Cores.Length;
        if (n == 0) return;
        double gap = quadrados ? 2 : 3, w = (ActualWidth - gap * (n - 1)) / n, h = quadrados ? w : ActualHeight;
        for (int i = 0; i < n; i++)
        {
            var c = Cores[i];
            var b = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(Brilho, 0.15, 1)), c.R, c.G, c.B));
            if (quadrados) dc.DrawRectangle(b, null, new Rect(i * (w + gap), 0, Math.Max(0, w), h));
            else dc.DrawRoundedRectangle(b, null, new Rect(i * (w + gap), 0, Math.Max(0, w), h), 2, 2);
        }
    }
}
