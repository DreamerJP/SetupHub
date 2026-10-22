using System.Windows;
using System.Windows.Controls;

namespace SetupHub.Janela.Kit;

// Cada cartão tem largura mínima e peso; quebra a linha quando não cabe, e a linha toma a altura do mais alto.
sealed class Fluxo : Panel
{
    public static readonly DependencyProperty BaseProperty = DependencyProperty.RegisterAttached("Base", typeof(double), typeof(Fluxo), new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static readonly DependencyProperty CresceProperty = DependencyProperty.RegisterAttached("Cresce", typeof(double), typeof(Fluxo), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static void SetBase(UIElement e, double v) => e.SetValue(BaseProperty, v);
    public static void SetCresce(UIElement e, double v) => e.SetValue(CresceProperty, v);
    static double Base(UIElement e) => (double)e.GetValue(BaseProperty);
    static double Cresce(UIElement e) => (double)e.GetValue(CresceProperty);

    public double Espaco = 12;
    public bool EsticarAltura = true;
    public bool PreencherUltima;

    public static T Item<T>(T e, double baseLargura, double cresce = 1) where T : UIElement
    {
        SetBase(e, baseLargura); SetCresce(e, cresce);
        return e;
    }

    List<List<UIElement>> Linhas(double largura)
    {
        var linhas = new List<List<UIElement>>();
        var atual = new List<UIElement>();
        double soma = 0;
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) continue;
            double b = Math.Min(Base(c), largura);
            if (atual.Count > 0 && soma + Espaco + b > largura) { linhas.Add(atual); atual = new(); soma = 0; }
            soma += (atual.Count > 0 ? Espaco : 0) + b;
            atual.Add(c);
        }
        if (atual.Count > 0) linhas.Add(atual);
        return linhas;
    }

    List<double> Larguras(List<UIElement> linha, double largura)
    {
        double bases = linha.Sum(c => Math.Min(Base(c), largura)) + Espaco * (linha.Count - 1);
        double sobra = Math.Max(0, largura - bases);
        double pesos = linha.Sum(Cresce);
        return linha.Select(c => Math.Min(Base(c), largura) + (pesos > 0 ? sobra * Cresce(c) / pesos : 0)).ToList();
    }

    protected override Size MeasureOverride(Size disp)
    {
        double largura = double.IsInfinity(disp.Width) ? 1200 : disp.Width;
        double altura = 0;
        var linhas = Linhas(largura);
        foreach (var l in linhas)
        {
            var ws = Larguras(l, largura);
            double h = 0;
            for (int i = 0; i < l.Count; i++) { l[i].Measure(new Size(ws[i], double.PositiveInfinity)); h = Math.Max(h, l[i].DesiredSize.Height); }
            altura += h;
        }
        altura += Math.Max(0, linhas.Count - 1) * Espaco;
        return new Size(largura, altura);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        var linhas = Linhas(final.Width);
        foreach (var l in linhas)
        {
            var ws = Larguras(l, final.Width);
            double h = l.Max(c => c.DesiredSize.Height);
            if (PreencherUltima && l == linhas[^1]) h = Math.Max(h, final.Height - y);
            double x = 0;
            for (int i = 0; i < l.Count; i++)
            {
                double hi = EsticarAltura ? h : l[i].DesiredSize.Height;
                l[i].Arrange(new Rect(x, y, ws[i], hi));
                x += ws[i] + Espaco;
            }
            y += h + Espaco;
        }
        return final;
    }
}
