using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SetupHub.Janela.Kit;

sealed class CampoAtalho : Botao
{
    readonly TextBlock texto;
    string? atual;
    bool gravando;
    public bool AceitaVazio;
    public event Action<string?>? Gravou;

    public CampoAtalho(double altura = 32)
    {
        Height = K.P.E(altura);
        CornerRadius = new CornerRadius(K.P.RaioBotao);
        BorderThickness = new Thickness(1); BordaNormal = K.P.BordaForte; BorderBrush = K.P.BordaForte; BordaSobre = K.P.SubSel;
        FundoNormal = Brushes.Transparent; Background = Brushes.Transparent; FundoSobre = K.P.Poco;
        texto = K.T("", 14);
        texto.HorizontalAlignment = HorizontalAlignment.Center; texto.VerticalAlignment = VerticalAlignment.Center;
        Child = texto;
        ToolTip = "Clique e pressione o novo atalho. Esc cancela.";
        Clique += (_, _) => { gravando = true; texto.Text = "pressione o atalho…"; texto.Foreground = K.P.Secundario; Focus(); };
        PreviewKeyDown += Tecla;
        LostKeyboardFocus += (_, _) => { if (gravando) { gravando = false; Mostrar(); } };
    }

    public void Definir(string? a)
    {
        atual = a;
        if (!gravando) Mostrar();
    }

    void Mostrar()
    {
        texto.Text = string.IsNullOrEmpty(atual) ? "sem atalho" : atual.Replace("+", " ");
        texto.Foreground = string.IsNullOrEmpty(atual) ? K.P.Apagado : K.P.Texto;
    }

    void Tecla(object sender, KeyEventArgs e)
    {
        if (!gravando) return;
        e.Handled = true;
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        if (k == Key.Escape) { gravando = false; Mostrar(); return; }
        if (k is Key.Delete or Key.Back && AceitaVazio) { gravando = false; atual = null; Mostrar(); Gravou?.Invoke(null); return; }
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var m = Keyboard.Modifiers;
        if (m == ModifierKeys.None) { texto.Text = "requer Ctrl, Alt ou Shift"; return; }
        var partes = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) partes.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) partes.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) partes.Add("Shift");
        partes.Add(k is >= Key.D0 and <= Key.D9 ? ((int)(k - Key.D0)).ToString() : k is >= Key.NumPad0 and <= Key.NumPad9 ? ((int)(k - Key.NumPad0)).ToString() : k.ToString());
        var novo = string.Join("+", partes);
        if (!Nucleo.Validacao.AtalhoValido(novo)) { texto.Text = "requer letra, número ou F1 a F12"; return; }
        gravando = false;
        atual = novo;
        Mostrar();
        Gravou?.Invoke(atual);
    }
}
