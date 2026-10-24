using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Kit;

// Coordenadas no quadro de 480 por 380, escalado para o tamanho da área.
sealed class DiagramaAr : Viewbox
{
    sealed class Fan
    {
        public string Id = "";
        public Canvas Grupo = null!;
        public Rectangle Moldura = null!;
        public RotateTransform Giro = new();
        public double Raio;
        public double Dur = -1;
        public TextBlock Rotulo = null!;
        public Run Valor = null!;
        public string Lado = "direita";
    }

    public const double Largura = 480, Altura = 380;
    readonly Canvas tela = new() { Width = Largura, Height = Altura };
    readonly Dictionary<string, Fan> fans = new();
    readonly List<RotateTransform> girosGpu = new();
    double durGpu = -1;
    readonly Rectangle cpu, gpu;
    sealed class Ar { public Path Linha = null!; public string[] Fans = Array.Empty<string>(); public double Dur = -1; }
    readonly List<Ar> ares = new();
    readonly Run cpuTemp, gpuTemp, gpuNota;
    readonly TextBlock rotCpu, rotGpu;
    public HashSet<string> Selecionadas { get; } = new();
    public event Action? MudouSelecao;
    public event Action<string, Point>? Moveu;
    public bool Posicionando;
    readonly Paleta P;

    static readonly Dictionary<string, (double x, double y, double r)> Padrao = new()
    {
        ["cpu"] = (262, 112, 30), ["sys1"] = (47, 104, 30), ["sys2"] = (47, 214, 30), ["sys3"] = (435, 96, 27),
        ["sys4"] = (47, 290, 24), ["bomba"] = (262, 182, 22), ["ez"] = (435, 150, 22),
    };

    // Contorno tirado do desenho de referência, raio 100 e centro na origem: as 6 pás numa peça só, o anel vazio e o miolo (preenchimento par-ímpar).
    static readonly Geometry Desenho = Congelar(Geometry.Parse(
        "M-7.7,-99.7 7.7,-99.7 15,-98.8 28.1,-95.9 33.4,-94.1 34.3,-93.3 34,-91.5 28.1,-86.9 21.4,-80.1 16.8,-74.6 11.8,-67.6 9.2,-63.2 4.8,-54.2 0.7,-42.2 1,-40.8 6.6,-40.5 11.5,-48.4 18.8,-55.9 24.1,-60 30.5,-64.1 42.1,-69.6 50,-72 54.4,-72.6 59.9,-72.3 64,-71.4 70.7,-68.5 74.5,-65.9 78.6,-61.8 82.7,-56.2 87.3,-48.6 91.1,-41.1 93.8,-34.6 96.4,-26.5 98.1,-18.9 98.1,-17.4 97.3,-16.6 95.8,-16.6 89.1,-19.2 79.2,-21.8 65.8,-23.6 55.6,-23.6 49.4,-23 39.8,-21.2 35.7,-19.8 38.3,-14.5 44.8,-14.5 49.4,-13.9 56.4,-12.2 63.1,-9.6 73.1,-4.3 81.2,1.5 87.6,7.6 90.6,11.7 92.6,15.8 93.8,19.3 94.6,24.6 94.3,31.3 92.6,37.7 87.9,47.6 84.1,54 79.2,61 71.3,70.1 64.6,76.2 63.4,76.2 62.6,75.6 61.4,68.6 59.3,60.4 56.1,51.4 51.8,42.4 43.6,30.1 37.8,23.4 35.1,21.1 31.6,26 34.3,30.1 36,33.9 38.9,43.5 40.1,52.9 40.1,64.2 39.2,71.5 37.8,77.9 36,82.6 33.7,86.7 29,91.9 24.6,95.1 19.7,97.5 15.6,98.7 5.7,99.8 -5.7,99.8 -16.2,98.7 -26.7,96.3 -33.1,94.3 -34,93.7 -34.3,92.2 -22.6,81.4 -17.1,75 -11.5,67.1 -4.8,54.3 -1.3,44.4 -0.7,42.1 -1,40.9 -6.6,40.3 -8,43.2 -13.6,50.8 -20,56.9 -28.7,63.1 -37.5,67.7 -42.4,69.8 -49.2,71.8 -52.7,72.4 -59.1,72.4 -64.6,71.2 -71,68.3 -73.9,66.3 -77.7,62.8 -82.4,56.6 -86.8,49.6 -90.3,42.9 -93.5,35.4 -96.1,27.5 -98.2,18.7 -98.2,17.6 -97,16.4 -82.4,21.1 -68.1,23.4 -58.5,23.7 -50.3,23.1 -41.6,21.6 -35.7,19.9 -38.4,14.4 -46.2,14.4 -52.9,13.2 -60.8,10.6 -69.9,6.2 -79.2,0.1 -88.2,-8.4 -90.9,-12.2 -93.2,-17.4 -94.4,-22.4 -94.7,-28.2 -93.2,-36.1 -87.4,-48.6 -81.2,-58.3 -75.7,-65.3 -65.2,-75.8 -63.7,-76.4 -63.2,-76.1 -62.3,-75.2 -61.7,-70.2 -59.4,-60.6 -55.6,-50.1 -52.7,-44 -49.4,-38.4 -44.2,-30.9 -39.8,-25.6 -35.2,-20.9 -31.7,-25.9 -35.4,-32.6 -38.1,-39.9 -39.2,-45.1 -40.1,-53.3 -39.8,-67 -38.7,-74.3 -36.6,-81.3 -34.9,-84.8 -31.9,-88.9 -28.4,-92.4 -25.2,-94.7 -21.4,-96.8 -17.4,-98.2 -13.3,-99.1 -8,-99.4Z"
        + "M32.3,0A32.3,32.3 0 1 1 -32.3,0A32.3,32.3 0 1 1 32.3,0Z"
        + "M21.3,0A21.3,21.3 0 1 1 -21.3,0A21.3,21.3 0 1 1 21.3,0Z"));

    static Geometry Congelar(Geometry g) { g.Freeze(); return g; }

    public static Geometry Pas(double R) =>
        Congelar(new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { Desenho }, Transform = new ScaleTransform(R / 100, R / 100) });

    public DiagramaAr(IEnumerable<string> ids, Dictionary<string, string>? posicoes)
    {
        P = K.P;
        Stretch = Stretch.Uniform;
        Child = tela;
        Brush Hx(string h) => Paleta.Cor(h);
        Shape R(double x, double y, double w, double h, double rx, Brush? fill, Brush stroke, double sw = 1)
        {
            var r = new Rectangle { Width = w, Height = h, RadiusX = rx, RadiusY = rx, Fill = fill, Stroke = stroke, StrokeThickness = sw };
            Canvas.SetLeft(r, x); Canvas.SetTop(r, y); tela.Children.Add(r); return r;
        }
        var fundoPeca = P.Claro ? P.PocoEscuro : Hx("#0E1318");
        // A fonte fica no fundo, do lado de trás, com o comprimento de uma fonte comum perto do gabinete.
        R(6, 6, 468, 368, 14, P.Poco, P.BordaForte, 1.5);
        R(168, 24, 236, 270, 3, P.Claro ? P.Poco : Hx("#0E141B"), P.Borda);
        R(318, 314, 148, 52, 6, fundoPeca, P.Trilho);
        var fonte = new TextBlock { Text = "fonte de energia", FontSize = 11, Foreground = P.Claro ? P.Tenue : Hx("#4F5B68"), FontFamily = P.Corpo, Width = 148, TextAlignment = TextAlignment.Center };
        Canvas.SetLeft(fonte, 318); Canvas.SetTop(fonte, 332); tela.Children.Add(fonte);
        cpu = (Rectangle)R(296, 52, 50, 118, 3, P.Poco, P.SubSel);
        var aletas = new Path { Data = Geometry.Parse("M300 62h42M300 72h42M300 82h42M300 92h42M300 102h42M300 112h42M300 122h42M300 132h42M300 142h42M300 152h42M300 162h42"), Stroke = P.Claro ? P.BordaForte : Hx("#2A333E") };
        tela.Children.Add(aletas);
        gpu = (Rectangle)R(104, 222, 300, 42, 5, P.Poco, P.SubSel);
        // Cada linha anda com o fan da frente que a empurra ou o de trás que a puxa.
        foreach (var (d, quem) in new[]
        {
            ("M84 96C150 92 200 96 248 104S380 100 420 92", new[] { "sys1", "sys3" }),
            ("M84 116C160 120 210 128 262 128S390 112 420 104", new[] { "sys1", "sys3" }),
            ("M84 206C150 202 230 196 300 188S400 142 424 110", new[] { "sys2", "sys3" }),
            ("M84 226C140 238 170 280 240 288S360 288 420 284", new[] { "sys2" }),
        })
        {
            var linha = new Path
            {
                Data = Geometry.Parse(d),
                Stroke = P.Tenue, StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeDashCap = PenLineCap.Round,
            };
            double esp = linha.StrokeThickness;
            linha.StrokeDashArray = new DoubleCollection { 3 / esp, 9 / esp };
            tela.Children.Add(linha);
            ares.Add(new Ar { Linha = linha, Fans = quem });
        }
        foreach (var x in new[] { 180.0, 254, 328 })
        {
            var g = new Canvas();
            Canvas.SetLeft(g, x); Canvas.SetTop(g, 243);
            var c = new Ellipse { Width = 30, Height = 30, Fill = P.Profundo, Stroke = P.BordaForte };
            Canvas.SetLeft(c, -15); Canvas.SetTop(c, -15); g.Children.Add(c);
            var giro = new RotateTransform();
            var pas = new Path { Data = Pas(13), RenderTransform = giro };
            pas.Fill = P.Gpu;
            g.Children.Add(pas);
            girosGpu.Add(giro);
            tela.Children.Add(g);
        }
        foreach (var id in ids) CriarFan(id, posicoes);
        (rotCpu, cpuTemp, _) = Rotulo(321, 34, "processador", "centro", false);
        (rotGpu, gpuTemp, gpuNota) = Rotulo(254, 272, "placa de vídeo", "centro", false);
        MouseMove += Arrastando;
        MouseLeftButtonUp += Soltou;
    }

    // lado: onde o rótulo fica em relação ao ponto (direita, esquerda, baixo) ou "centro" (centrado, começando no ponto).
    (TextBlock t, Run nome, Run valor) Rotulo(double x, double y, string valor, string lado, bool duasLinhas)
    {
        var t = new TextBlock { FontSize = 12.5, FontFamily = P.Corpo, IsHitTestVisible = false, LineHeight = 15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        var nome = new Run("–") { FontWeight = FontWeights.SemiBold, Foreground = P.Texto };
        var v = new Run((duasLinhas ? "" : " ") + valor) { Foreground = P.Apagado };
        t.Inlines.Add(nome);
        if (duasLinhas) t.Inlines.Add(new LineBreak());
        t.Inlines.Add(v);
        t.TextAlignment = lado switch { "esquerda" => TextAlignment.Right, "direita" => TextAlignment.Left, _ => TextAlignment.Center };
        Canvas.SetLeft(t, x); Canvas.SetTop(t, y);
        t.SizeChanged += (_, _) => t.RenderTransform = lado switch
        {
            "direita" => new TranslateTransform(0, -t.ActualHeight / 2),
            "esquerda" => new TranslateTransform(-t.ActualWidth, -t.ActualHeight / 2),
            _ => new TranslateTransform(-t.ActualWidth / 2, 0),
        };
        tela.Children.Add(t);
        return (t, nome, v);
    }

    void CriarFan(string id, Dictionary<string, string>? posicoes)
    {
        if (!Padrao.TryGetValue(id, out var p)) return;
        double x = p.x, y = p.y;
        if (posicoes is not null && posicoes.TryGetValue(id, out var s))
        {
            var xy = s.Split(',');
            if (xy.Length == 2 && double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var px) && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var py)) { x = Math.Clamp(px, 30, 450); y = Math.Clamp(py, 30, 300); }
        }
        double r = p.r;
        var g = new Canvas { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        var moldura = new Rectangle { Width = 2 * r, Height = 2 * r, RadiusX = 6, RadiusY = 6, Fill = P.Claro ? P.PocoEscuro : Paleta.Cor("#0E1318"), Stroke = P.BordaForte, StrokeThickness = 1 };
        Canvas.SetLeft(moldura, -r); Canvas.SetTop(moldura, -r); g.Children.Add(moldura);
        var aro = new Ellipse { Width = 2 * (r - 3), Height = 2 * (r - 3), Fill = P.Profundo, Stroke = P.BordaForte };
        Canvas.SetLeft(aro, -(r - 3)); Canvas.SetTop(aro, -(r - 3)); g.Children.Add(aro);
        if (id != "cpu")
            foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                var parafuso = new Ellipse { Width = 4, Height = 4, Fill = P.BordaForte };
                Canvas.SetLeft(parafuso, dx * (r - 6) - 2); Canvas.SetTop(parafuso, dy * (r - 6) - 2); g.Children.Add(parafuso);
            }
        var f = new Fan { Id = id, Grupo = g, Moldura = moldura, Raio = r };
        var pas = new Path { Data = Pas(r - 4), RenderTransform = f.Giro };
        pas.Fill = P.Pa;
        g.Children.Add(pas);
        Canvas.SetLeft(g, x); Canvas.SetTop(g, y);
        g.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (Posicionando) { arrastando = f; g.CaptureMouse(); return; }
            bool varias = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (varias) { if (!Selecionadas.Remove(id)) Selecionadas.Add(id); }
            else if (Selecionadas.Count == 1 && Selecionadas.Contains(id)) Selecionadas.Clear();
            else { Selecionadas.Clear(); Selecionadas.Add(id); }
            Pintar();
            MudouSelecao?.Invoke();
        };
        tela.Children.Add(g);
        var lado = Lado(x);
        var (rx, ry) = PosRotulo(lado, x, y, r);
        var (t, nome, valor) = Rotulo(rx, ry, "", lado, true);
        nome.Text = Padroes.Ventoinhas.First(v => v.Id == id).Nome;
        f.Rotulo = t; f.Valor = valor; f.Lado = lado;
        fans[id] = f;
    }

    // Fan na parede da frente tem o rótulo à direita, na de trás à esquerda, no meio embaixo.
    static string Lado(double x) => x < 150 ? "direita" : x > 330 ? "esquerda" : "baixo";

    static (double, double) PosRotulo(string lado, double x, double y, double r) => lado switch
    {
        "direita" => (x + r + 8, y),
        "esquerda" => (x - r - 8, y),
        _ => (x, y + r + 6),
    };

    Fan? arrastando;
    void Arrastando(object s, MouseEventArgs e)
    {
        if (arrastando is null) return;
        var p = e.GetPosition(tela);
        double x = Math.Clamp(p.X, 30, 450), y = Math.Clamp(p.Y, 30, 300);
        Canvas.SetLeft(arrastando.Grupo, x); Canvas.SetTop(arrastando.Grupo, y);
        var (rx, ry) = PosRotulo(arrastando.Lado, x, y, arrastando.Raio);
        Canvas.SetLeft(arrastando.Rotulo, rx); Canvas.SetTop(arrastando.Rotulo, ry);
    }
    void Soltou(object s, MouseButtonEventArgs e)
    {
        if (arrastando is null) return;
        arrastando.Grupo.ReleaseMouseCapture();
        Moveu?.Invoke(arrastando.Id, new Point(Canvas.GetLeft(arrastando.Grupo), Canvas.GetTop(arrastando.Grupo)));
        arrastando = null;
    }

    public void Pintar()
    {
        foreach (var f in fans.Values)
        {
            bool on = Selecionadas.Contains(f.Id);
            f.Moldura.Stroke = on ? P.Texto : P.BordaForte;
            f.Moldura.StrokeThickness = on ? 2 : 1;
            f.Moldura.StrokeDashArray = on ? new DoubleCollection { 2.5, 1.5 } : null;
        }
    }

    static double Dur(double rpm) => rpm < 60 ? 0 : Math.Round(Math.Max(0.25, 1.6 - rpm / 1900) * 10) / 10;

    static void Girar(RotateTransform g, double dur)
    {
        if (dur == 0) { g.BeginAnimation(RotateTransform.AngleProperty, null); return; }
        g.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(g.Angle, g.Angle + 360, TimeSpan.FromSeconds(dur)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    Brush Tinta(double? t, double lim)
    {
        double f = t is double v ? Math.Clamp((v - 35) / (lim - 35), 0, 1) : 0;
        return new SolidColorBrush(Color.FromArgb((byte)(255 * (0.12 + f * 0.25)), (byte)(111 + f * 131), (byte)(195 - f * 95), (byte)(232 - f * 171)));
    }

    public void Atualizar(Estado e)
    {
        foreach (var f in fans.Values)
        {
            var l = e.Ventoinhas.FirstOrDefault(v => v.Id == f.Id);
            double rpm = l?.Rpm ?? 0;
            var d = Dur(rpm);
            if (Math.Abs(d - f.Dur) > 0.05) { f.Dur = d; Girar(f.Giro, d); }
            f.Valor.Text = rpm < 60 ? (l?.Conectada == true ? "parada" : "sem ventoinha") : $"{rpm.ToString("N0", K.Br)} rpm";
        }
        double gr = e.Gpu.FanRpm ?? 0;
        var dg = Dur(gr);
        if (Math.Abs(dg - durGpu) > 0.05) { durGpu = dg; foreach (var g in girosGpu) Girar(g, dg); }
        foreach (var a in ares)
        {
            double rpm = a.Fans.Max(id => e.Ventoinhas.FirstOrDefault(v => v.Id == id)?.Rpm ?? 0);
            double da = rpm < 60 ? 0 : Math.Max(0.6, 4 - rpm / 700);
            a.Linha.Opacity = rpm < 60 ? 0.2 : 0.85;
            if (Math.Abs(da - a.Dur) <= 0.1) continue;
            a.Dur = da;
            if (da == 0) a.Linha.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            else a.Linha.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -120 / a.Linha.StrokeThickness, TimeSpan.FromSeconds(da)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        var c = App.Lig.Config;
        cpuTemp.Text = e.Cpu.TempMaisQuente is double ct ? $"{ct:0} °C" : "–";
        cpuTemp.Foreground = P.CalorCpu(e.Cpu.TempMaisQuente, c);
        gpuTemp.Text = e.Gpu.TempMaisQuente is double gt ? $"{gt:0} °C" : "–";
        gpuTemp.Foreground = P.CalorGpu(e.Gpu.TempMaisQuente, c);
        gpuNota.Text = $" placa de vídeo, {(gr < 60 ? 0 : gr).ToString("N0", K.Br)} rpm";
        cpu.Fill = Tinta(e.Cpu.TempMaisQuente, Paleta.LimitesCpu(c).quente);
        gpu.Fill = Tinta(e.Gpu.TempMaisQuente, Paleta.LimitesGpu(c).quente);
    }

    public void Parar()
    {
        foreach (var f in fans.Values) { f.Giro.BeginAnimation(RotateTransform.AngleProperty, null); f.Dur = -1; }
        foreach (var g in girosGpu) g.BeginAnimation(RotateTransform.AngleProperty, null);
        durGpu = -1;
        foreach (var a in ares) { a.Linha.BeginAnimation(Shape.StrokeDashOffsetProperty, null); a.Dur = -1; }
    }
}
