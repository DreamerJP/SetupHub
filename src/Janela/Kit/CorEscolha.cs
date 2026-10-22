using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Kit;

sealed class CorEscolha : Botao
{
    readonly Border amostra;
    readonly Popup paleta;
    readonly TextBox codigo;
    readonly Canvas roda;
    readonly Grid marcador;
    public event Action<string>? Escolheu;
    public string Valor { get; private set; } = "#FFFFFF";

    const int Lado = 168;
    static readonly Lazy<BitmapSource> Imagem = new(DesenharRoda);

    static readonly string[] Cores =
    {
        "#FFFFFF", "#DDE8FF", "#6FC3E8", "#2E7BD6", "#3E7BFA", "#8BB2FF", "#B9A0FF", "#7A5CE6",
        "#FF3D7F", "#E5484D", "#F2643D", "#FF7A2E", "#F2B045", "#FFD84A", "#A8E06C", "#2BD9C9",
        "#1FAA59", "#00C2A8", "#FF00FF", "#FF0000", "#00FF00", "#0000FF", "#FFA500", "#000000",
    };

    public CorEscolha(string cor, double altura = 28)
    {
        Height = altura; CornerRadius = new CornerRadius(6); BorderThickness = new Thickness(1); BorderBrush = K.P.BordaForte; BordaNormal = K.P.BordaForte;
        Padding = new Thickness(3);
        amostra = new Border { CornerRadius = new CornerRadius(3) };
        Child = amostra;

        roda = new Canvas { Width = Lado, Height = Lado, Background = Brushes.Transparent, Cursor = Cursors.Cross };
        roda.Children.Add(new Image { Source = Imagem.Value, Width = Lado, Height = Lado, IsHitTestVisible = false });
        marcador = new Grid { Width = 16, Height = 16, IsHitTestVisible = false };
        marcador.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), StrokeThickness = 1 });
        marcador.Children.Add(new Ellipse { Margin = new Thickness(1), Stroke = Brushes.White, StrokeThickness = 2 });
        roda.Children.Add(marcador);
        roda.MouseLeftButtonDown += (_, e) => { roda.CaptureMouse(); Mover(e.GetPosition(roda)); e.Handled = true; };
        roda.MouseMove += (_, e) => { if (roda.IsMouseCaptured) Mover(e.GetPosition(roda)); };

        var grade = new UniformGrid { Columns = 8, VerticalAlignment = VerticalAlignment.Top };
        foreach (var c in Cores)
        {
            var b = new Botao { Width = 24, Height = 24, Margin = new Thickness(2), CornerRadius = new CornerRadius(4), Background = Paleta.Cor(c), BorderBrush = K.P.BordaForte, BorderThickness = new Thickness(1), Dado = c };
            b.Clique += (x, _) => Gravar((string)x.Dado!, true);
            grade.Children.Add(b);
        }
        codigo = new TextBox { Width = 90, Background = K.P.PocoEscuro, Foreground = K.P.Texto, CaretBrush = K.P.Texto, BorderBrush = K.P.BordaForte, FontFamily = new FontFamily("Consolas"), Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(2, 0, 0, 0) };
        roda.MouseLeftButtonUp += (_, e) => { if (!roda.IsMouseCaptured) return; roda.ReleaseMouseCapture(); Mover(e.GetPosition(roda)); Gravar(codigo.Text, false); };
        codigo.KeyDown += (_, e) => { if (e.Key == Key.Enter && Validacao.CorValida(codigo.Text.Trim().ToUpperInvariant())) Gravar(codigo.Text.Trim().ToUpperInvariant(), true); };
        var lado = K.Col(10, grade, K.Lin(8, K.T("Código", 12.5, K.P.Apagado), codigo));
        lado.VerticalAlignment = VerticalAlignment.Top;

        paleta = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Custom, StaysOpen = false, AllowsTransparency = true,
            // Os tamanhos chegam em pixels da tela e o ponto devolvido é lido em unidades da janela.
            CustomPopupPlacementCallback = (caixa, alvo, _) =>
            {
                var e = VisualTreeHelper.GetDpi(this);
                double x = (alvo.Width - caixa.Width) / e.DpiScaleX, abaixo = (alvo.Height + 4) / e.DpiScaleY, acima = (-caixa.Height - 4) / e.DpiScaleY;
                return new[] { new CustomPopupPlacement(new Point(x, abaixo), PopupPrimaryAxis.Horizontal), new CustomPopupPlacement(new Point(x, acima), PopupPrimaryAxis.Horizontal) };
            },
            Child = new Border { Background = K.P.Painel, BorderBrush = K.P.BordaForte, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Child = K.Lin(14, roda, lado) },
        };
        Clique += (_, _) => { codigo.Text = Valor; Marcar(Valor); paleta.IsOpen = true; };
        Definir(cor);
    }

    void Mover(Point p)
    {
        double r0 = Lado / 2.0, dx = p.X - r0, dy = p.Y - r0;
        double s = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / r0), h = Math.Atan2(-dy, dx) * 180 / Math.PI;
        var hex = Hex(Hsv(h, s));
        Posicionar(h, s);
        codigo.Text = hex;
        amostra.Background = Paleta.Cor(hex);
    }

    void Marcar(string hex)
    {
        var (h, s) = TomSaturacao(Paleta.Cor(hex).Color);
        Posicionar(h, s);
    }

    void Posicionar(double h, double s)
    {
        double r0 = Lado / 2.0, a = h * Math.PI / 180;
        Canvas.SetLeft(marcador, r0 + Math.Cos(a) * s * r0 - marcador.Width / 2);
        Canvas.SetTop(marcador, r0 - Math.Sin(a) * s * r0 - marcador.Height / 2);
    }

    void Gravar(string c, bool fechar)
    {
        if (fechar) paleta.IsOpen = false;
        Definir(c);
        Escolheu?.Invoke(Valor);
    }

    public void Definir(string c)
    {
        Valor = Validacao.CorValida(c) ? c.ToUpperInvariant() : "#FFFFFF";
        amostra.Background = Paleta.Cor(Valor);
    }

    static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    static Color Hsv(double h, double s)
    {
        h = (h % 360 + 360) % 360;
        double x = s * (1 - Math.Abs(h / 60 % 2 - 1)), m = 1 - s;
        var (r, g, b) = h < 60 ? (s, x, 0d) : h < 120 ? (x, s, 0d) : h < 180 ? (0d, s, x) : h < 240 ? (0d, x, s) : h < 300 ? (x, 0d, s) : (s, 0d, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    static (double h, double s) TomSaturacao(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max);
    }

    static BitmapSource DesenharRoda()
    {
        var px = new byte[Lado * Lado * 4];
        double r0 = Lado / 2.0;
        for (int y = 0; y < Lado; y++)
            for (int x = 0; x < Lado; x++)
            {
                double dx = x + 0.5 - r0, dy = y + 0.5 - r0, dist = Math.Sqrt(dx * dx + dy * dy);
                double alfa = Math.Clamp(r0 - dist, 0, 1);
                if (alfa <= 0) continue;
                var c = Hsv(Math.Atan2(-dy, dx) * 180 / Math.PI, Math.Min(1, dist / r0));
                int i = (y * Lado + x) * 4;
                px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = (byte)Math.Round(alfa * 255);
            }
        var bmp = BitmapSource.Create(Lado, Lado, 96, 96, PixelFormats.Bgra32, null, px, Lado * 4);
        bmp.Freeze();
        return bmp;
    }
}
