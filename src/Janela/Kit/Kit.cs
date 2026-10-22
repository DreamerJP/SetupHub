using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SetupHub.Janela.Kit;

// Montado em código, sem XAML, para abrir rápido.
static class K
{
    public static Paleta P = null!;
    public static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static TextBlock T(string texto, double tamanho = 14, Brush? cor = null, FontWeight? peso = null, FontFamily? fonte = null)
        => new()
        {
            Text = texto, FontSize = P.F(tamanho), Foreground = cor ?? P.Texto, FontWeight = peso ?? FontWeights.Normal,
            FontFamily = fonte ?? P.Corpo, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };

    public static TextBlock Titulo(string texto, double tamanho = 15) => T(texto, tamanho, P.Texto, FontWeights.SemiBold);
    public static TextBlock Nota(string texto, double tamanho = 12.5) { var t = T(texto, tamanho, P.Apagado); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; return t; }
    public static TextBlock Num(string texto, double tamanho = 23, Brush? cor = null) => T(texto, tamanho, cor, P.PesoNumero, P.Numero);

    public static TextBlock NumUn(out Run valor, string unidade, double tamanho = 23, double tamUnidade = 12.5, Brush? cor = null)
    {
        var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        valor = new Run("–") { FontFamily = P.Numero, FontWeight = P.PesoNumero, FontSize = P.F(tamanho), Foreground = cor ?? P.Texto };
        t.Inlines.Add(valor);
        t.Inlines.Add(new Run(" " + unidade) { FontFamily = P.Corpo, FontSize = P.F(tamUnidade), Foreground = P.Apagado });
        return t;
    }

    public static string N(double? v, int casas = 0) => v is double d && !double.IsNaN(d) ? d.ToString("N" + casas, Br) : "–";

    public static Border Cartao(UIElement filho, Thickness? pad = null)
    {
        var b = new Border
        {
            Background = P.Painel, BorderBrush = P.Borda, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(P.RaioCartao), Padding = pad ?? new Thickness(16, P.E(14), 16, P.E(14)), Child = filho,
        };
        return b;
    }

    public static StackPanel Col(double gap, params UIElement?[] filhos)
    {
        var s = new StackPanel { Orientation = Orientation.Vertical };
        bool primeiro = true;
        foreach (var f in filhos)
        {
            if (f is null) continue;
            if (!primeiro && f is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + P.E(gap), fe.Margin.Right, fe.Margin.Bottom);
            s.Children.Add(f);
            primeiro = false;
        }
        return s;
    }

    public static StackPanel Lin(double gap, params UIElement?[] filhos)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal };
        bool primeiro = true;
        foreach (var f in filhos)
        {
            if (f is null) continue;
            if (!primeiro && f is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left + gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            s.Children.Add(f);
            primeiro = false;
        }
        return s;
    }

    // Grade com colunas: "*" divide o resto, número fixa a largura, "auto" ajusta ao conteúdo.
    public static Grid Grade(string colunas, double gap, params UIElement?[] filhos)
    {
        var g = new Grid();
        var cs = colunas.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < cs.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = Largura(cs[i]) });
            if (i < cs.Length - 1 && gap > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
        }
        int col = 0;
        foreach (var f in filhos)
        {
            if (f is null) { col += gap > 0 ? 2 : 1; continue; }
            Grid.SetColumn(f, Math.Min(col, g.ColumnDefinitions.Count - 1));
            g.Children.Add(f);
            col += gap > 0 ? 2 : 1;
        }
        return g;
    }

    static GridLength Largura(string s) => s switch
    {
        "*" => new GridLength(1, GridUnitType.Star),
        "auto" => GridLength.Auto,
        _ when s.EndsWith('*') => new GridLength(double.Parse(s.TrimEnd('*'), CultureInfo.InvariantCulture), GridUnitType.Star),
        _ => new GridLength(double.Parse(s, CultureInfo.InvariantCulture)),
    };

    public static Grid Iguais(int colunas, double gap, IEnumerable<UIElement> itens, double alturaLinha = double.NaN)
    {
        var g = new Grid();
        for (int i = 0; i < colunas; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (i < colunas - 1) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
        }
        int n = 0;
        foreach (var it in itens)
        {
            int linha = n / colunas, c = n % colunas;
            while (g.RowDefinitions.Count <= linha * 2)
            {
                if (g.RowDefinitions.Count > 0) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(gap) });
                g.RowDefinitions.Add(new RowDefinition { Height = double.IsNaN(alturaLinha) ? GridLength.Auto : new GridLength(alturaLinha) });
            }
            Grid.SetColumn(it, c * 2);
            Grid.SetRow(it, linha * 2);
            g.Children.Add(it);
            n++;
        }
        return g;
    }

    // Ordem de leitura por linha: esquerda, direita, esquerda...
    public static Grid DuasColunas(IReadOnlyList<UIElement> itens, double gap)
    {
        var a = new StackPanel(); var b = new StackPanel();
        for (int i = 0; i < itens.Count; i++) (i % 2 == 0 ? a : b).Children.Add(itens[i]);
        var g = Grade("* *", gap, a, b);
        a.VerticalAlignment = b.VerticalAlignment = VerticalAlignment.Top;
        return g;
    }

    // Número no centro do arco do medidor (uma unidade invisível à esquerda equilibra a de verdade) e a nota na abertura de baixo.
    public static Grid Mostrador(Medidor m, double fonte, Brush cor, out Run valor, out TextBlock nota, string notaTxt)
    {
        valor = new Run("–") { FontFamily = P.Grande, FontSize = fonte, FontWeight = FontWeights.SemiBold, Foreground = cor };
        TextBlock Unidade(bool fantasma) => new()
        {
            Text = "°C", FontSize = Math.Round(fonte * 0.27), Foreground = P.Secundario, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, fonte * 0.2, 2, 0), Visibility = fantasma ? Visibility.Hidden : Visibility.Visible,
        };
        var numero = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        numero.Inlines.Add(valor);
        var linha = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, fonte * 0.12) };
        linha.Children.Add(Unidade(true)); linha.Children.Add(numero); linha.Children.Add(Unidade(false));
        var centro = new Grid { Height = m.Width, VerticalAlignment = VerticalAlignment.Top, Children = { linha } };
        nota = T(notaTxt, 11.5, P.Apagado);
        nota.HorizontalAlignment = HorizontalAlignment.Center; nota.VerticalAlignment = VerticalAlignment.Bottom;
        nota.TextAlignment = TextAlignment.Center; nota.TextWrapping = TextWrapping.Wrap; nota.MaxWidth = m.Width * 0.62;
        return new Grid { Width = m.Width, Height = m.Height, Children = { m, centro, nota } };
    }

    public static Border LinhaAjuste(object rotulo, UIElement controle, double larguraControle, double altura = 46, bool separador = true)
    {
        var r = rotulo as UIElement ?? T(rotulo.ToString() ?? "");
        var g = Grade($"* {larguraControle.ToString(CultureInfo.InvariantCulture)}", 10, r, controle);
        g.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Child = g, MinHeight = P.E(altura), BorderBrush = P.Linha, BorderThickness = new Thickness(0, separador ? 1 : 0, 0, 0) };
    }

    public static Border Separador() => new() { Height = 1, Background = P.Linha };

    // Caminho desenhado numa caixa de 24, escalado para o tamanho pedido.
    public static Viewbox IconeCaixa(string dados, double tamanho, Brush cor, double traco = 1.7, double caixa = 24, bool preencher = false)
    {
        var path = new Path { Data = Geometry.Parse(dados), StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        if (preencher) path.Fill = cor; else { path.Stroke = cor; path.StrokeThickness = traco; }
        return new Viewbox { Width = tamanho, Height = tamanho, Child = new Canvas { Width = caixa, Height = caixa, Children = { path } } };
    }

    public static Cursor Mao => Cursors.Hand;
}
