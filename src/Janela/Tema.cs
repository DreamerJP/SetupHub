using System.Windows;
using System.Windows.Media;
using SetupHub.Nucleo;
using Microsoft.Win32;

namespace SetupHub.Janela;

sealed class Paleta
{
    public bool Claro;
    public SolidColorBrush Fundo = null!, Painel = null!, Borda = null!, Poco = null!, PocoEscuro = null!, Profundo = null!;
    public SolidColorBrush Texto = null!, Secundario = null!, Apagado = null!, Tenue = null!, Linha = null!, Trilho = null!;
    public SolidColorBrush BordaForte = null!, SubSel = null!, Cpu = null!, Gpu = null!, Morno = null!, Quente = null!, Pa = null!;
    public SolidColorBrush Nav = null!, NavAtivo = null!, Selecao = null!, TextoSelecao = null!, Grade = null!;
    public FontFamily Corpo = null!, Numero = null!;
    public FontWeight PesoNumero;
    public double Escala = 1, Espaco = 1;
    public double Raio, RaioBotao, RaioCartao;

    static readonly Uri Base = new("pack://application:,,,/");
    static FontFamily Fonte(string familia) => new(Base, "./Fontes/#" + familia);

    public static SolidColorBrush Cor(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static SolidColorBrush Cor(byte r, byte g, byte b, byte a = 255)
    {
        var x = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        x.Freeze();
        return x;
    }

    public static bool WindowsClaro()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    public static Paleta De(ConfigAparencia a)
    {
        bool claro = a.Tema == "claro" || a.Tema == "windows" && WindowsClaro();
        var p = new Paleta { Claro = claro, Escala = a.TamanhoTexto / 100.0, Espaco = a.Densidade == "confortavel" ? 1.25 : 1 };
        if (!claro)
        {
            p.Fundo = Cor("#0F1317"); p.Painel = Cor("#151A20"); p.Borda = Cor("#232B34"); p.Poco = Cor("#11161C"); p.PocoEscuro = Cor("#0C1014");
            p.Profundo = Cor("#0A0E12"); p.Texto = Cor("#E9EDF2"); p.Secundario = Cor("#B3BCC8"); p.Apagado = Cor("#8994A2"); p.Tenue = Cor("#5B6774");
            p.Linha = Cor("#1B222A"); p.Trilho = Cor("#1F2731"); p.BordaForte = Cor("#2F3945"); p.SubSel = Cor("#3A4552"); p.Pa = Cor("#C9D2DC");
            p.Nav = Cor("#0C1014"); p.NavAtivo = Cor("#26313D"); p.Grade = Cor("#36414D");
        }
        else
        {
            p.Fundo = Cor("#EEF1F4"); p.Painel = Cor("#FFFFFF"); p.Borda = Cor("#D5DCE3"); p.Poco = Cor("#F2F4F7"); p.PocoEscuro = Cor("#E7EBEF");
            p.Profundo = Cor("#E1E6EB"); p.Texto = Cor("#141A20"); p.Secundario = Cor("#3E4955"); p.Apagado = Cor("#5F6B78"); p.Tenue = Cor("#8C97A3");
            p.Linha = Cor("#E3E8ED"); p.Trilho = Cor("#DDE3E9"); p.BordaForte = Cor("#C3CCD5"); p.SubSel = Cor("#AEB8C3"); p.Pa = Cor("#3E4955");
            p.Nav = Cor("#E7EBEF"); p.NavAtivo = Cor("#D3DAE1"); p.Grade = Cor("#B7C1CB");
        }
        p.Corpo = Fonte("Saira Semi Condensed"); p.Numero = Fonte("Saira Extra Condensed");
        p.PesoNumero = FontWeights.SemiBold;
        p.Raio = 9; p.RaioBotao = 7; p.RaioCartao = 12;
        p.Morno = Cor("#F2B045"); p.Quente = Cor("#F2643D");
        p.Selecao = p.Texto; p.TextoSelecao = p.Fundo;
        p.Cpu = Validacao.CorValida(a.CorProcessador) ? Cor(a.CorProcessador) : Cor("#6FC3E8");
        p.Gpu = Validacao.CorValida(a.CorPlacaDeVideo) ? Cor(a.CorPlacaDeVideo) : Cor("#B9A0FF");
        if (claro) { p.Cpu = Escurecer(p.Cpu, 0.62); p.Gpu = Escurecer(p.Gpu, 0.72); }
        return p;
    }

    static SolidColorBrush Escurecer(SolidColorBrush b, double k) => Cor((byte)(b.Color.R * k), (byte)(b.Color.G * k), (byte)(b.Color.B * k));

    // Vermelho a partir do alerta de temperatura configurado e amarelo 15 °C antes dele.
    public static (double morno, double quente) LimitesCpu(Config? c) { var q = Padroes.Alerta(c, "cpu.temp", Padroes.AlertaCpu); return (q - 15, q); }
    public static (double morno, double quente) LimitesGpu(Config? c) { var q = Padroes.Alerta(c, "gpu.quente", Padroes.AlertaGpu); return (q - 15, q); }

    public Brush CalorCpu(double? t, Config? c) => Calor(t, LimitesCpu(c), Cpu);
    public Brush CalorGpu(double? t, Config? c) => Calor(t, LimitesGpu(c), Gpu);
    Brush Calor(double? t, (double morno, double quente) l, Brush normal) => t is not double v ? normal : v >= l.quente ? Quente : v >= l.morno ? Morno : normal;

    public double F(double tamanho) => tamanho * Escala;
    public double E(double espaco) => espaco * Espaco;
}
