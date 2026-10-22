using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Kit;

sealed class GraficoCurva : FrameworkElement
{
    public List<Ponto> Pontos = new();
    public double? Agora;
    public Brush CorAgora = Brushes.White;
    public Brush CorLinha;
    readonly double tMin, tMax;
    readonly bool rotulosMeio;

    public GraficoCurva(double tMin = 20, double tMax = 100, bool rotulosMeio = false, Brush? cor = null)
    {
        this.tMin = tMin; this.tMax = tMax; this.rotulosMeio = rotulosMeio;
        CorLinha = cor ?? K.P.Texto;
        Height = 150;
    }

    public void Mostrar(IEnumerable<Ponto> pontos, double? agora, Brush corAgora)
    {
        Pontos = pontos.OrderBy(p => p.T).ToList();
        Agora = agora; CorAgora = corAgora;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0) return;
        double esq = 40, dir = w - 10, topo = 10, base_ = h - 26;
        double X(double t) => esq + (Math.Clamp(t, tMin, tMax) - tMin) / (tMax - tMin) * (dir - esq);
        double Y(double p) => base_ - Math.Clamp(p, 0, 100) / 100 * (base_ - topo);
        var pg = new Pen(K.P.Trilho, 1);
        dc.DrawLine(pg, new Point(esq, topo), new Point(esq, base_));
        dc.DrawLine(pg, new Point(esq, base_), new Point(dir, base_));
        dc.DrawLine(pg, new Point(esq, Y(50)), new Point(dir, Y(50)));
        var fonte = new Typeface(K.P.Corpo, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        void Txt(string s, double x, double y, TextAlignment al)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, fonte, 11, K.P.Apagado, dpi) { TextAlignment = al };
            dc.DrawText(ft, new Point(x, y));
        }
        Txt("100%", esq - 6, topo - 6, TextAlignment.Right);
        if (rotulosMeio) Txt("50%", esq - 6, Y(50) - 7, TextAlignment.Right);
        Txt("0%", esq - 6, base_ - 7, TextAlignment.Right);
        Txt($"{tMin:0} °C", esq, base_ + 6, TextAlignment.Left);
        if (rotulosMeio) Txt($"{(tMin + tMax) / 2:0} °C", (esq + dir) / 2, base_ + 6, TextAlignment.Center);
        Txt($"{tMax:0} °C", dir, base_ + 6, TextAlignment.Right);
        if (Pontos.Count > 0)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(esq, Y(Pontos[0].P)), false, false);
                foreach (var p in Pontos) c.LineTo(new Point(X(p.T), Y(p.P)), true, true);
                c.LineTo(new Point(dir, Y(Pontos[^1].P)), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(CorLinha, 2) { LineJoin = PenLineJoin.Round }, g);
            if (Agora is double t)
            {
                double x = X(t), y = Y(Curvas.Avaliar(Pontos, t));
                dc.DrawLine(new Pen(CorAgora, 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) }, new Point(x, topo), new Point(x, base_));
                dc.DrawEllipse(CorAgora, null, new Point(x, y), 5, 5);
            }
        }
    }
}

sealed class Marcador : System.Windows.Controls.Border
{
    bool marcado;
    public event Action<bool>? Mudou;
    readonly System.Windows.Shapes.Path visto;
    public bool Marcado { get => marcado; set { marcado = value; Pintar(); } }

    public Marcador()
    {
        Width = 17; Height = 17; CornerRadius = new CornerRadius(4); BorderThickness = new Thickness(1.5);
        VerticalAlignment = VerticalAlignment.Center; HorizontalAlignment = HorizontalAlignment.Left;
        Cursor = System.Windows.Input.Cursors.Hand;
        visto = new System.Windows.Shapes.Path { Data = Geometry.Parse("M3.5 8.5l3 3 6-7"), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        Child = visto;
        Pintar();
        MouseLeftButtonUp += (_, e) => { Marcado = !Marcado; Mudou?.Invoke(Marcado); e.Handled = true; };
    }

    void Pintar()
    {
        Background = marcado ? K.P.Selecao : Brushes.Transparent;
        BorderBrush = marcado ? K.P.Selecao : K.P.SubSel;
        visto.Stroke = marcado ? K.P.TextoSelecao : Brushes.Transparent;
    }
}
