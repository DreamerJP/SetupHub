using System.Windows;
using System.Windows.Media;

namespace SetupHub.Janela.Kit;

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
