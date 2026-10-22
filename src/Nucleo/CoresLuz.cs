using System.Globalization;

namespace SetupHub.Nucleo;

// Cálculos de cor que o serviço grava no controlador e a janela mostra na prévia.
public static class CoresLuz
{
    public static (double r, double g, double b) Rgb(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#' && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, null, out var v))
            return ((v >> 16) & 255, (v >> 8) & 255, v & 255);
        return (255, 255, 255);
    }

    public static string Hex((double r, double g, double b) c) =>
        $"#{(int)Math.Clamp(Math.Round(c.r), 0, 255):X2}{(int)Math.Clamp(Math.Round(c.g), 0, 255):X2}{(int)Math.Clamp(Math.Round(c.b), 0, 255):X2}";

    public static (double r, double g, double b) Misturar(string a, string b, double x)
    {
        var (ar, ag, ab) = Rgb(a); var (br, bg, bb) = Rgb(b);
        x = Math.Clamp(x, 0, 1);
        return (ar + (br - ar) * x, ag + (bg - ag) * x, ab + (bb - ab) * x);
    }

    public static (double r, double g, double b) Gradiente(IReadOnlyList<string> cores, double x)
    {
        if (cores.Count == 0) return (255, 255, 255);
        if (cores.Count == 1) return Rgb(cores[0]);
        x = Math.Clamp(x, 0, 1) * (cores.Count - 1);
        int i = Math.Min((int)x, cores.Count - 2);
        return Misturar(cores[i], cores[i + 1], x - i);
    }

    // Mesmo cálculo do MSI Center: até a 1ª faixa a 1ª cor, entre as faixas a mistura das duas, acima da 3ª a 3ª cor.
    public static string Temperatura(EfeitoLuz f, double? temp)
    {
        var c = f.Cores.Count == 3 ? f.Cores : Padroes.CoresTemperatura();
        var t = f.Temperaturas.Count == 3 ? f.Temperaturas : new List<int> { 50, 70, 90 };
        double v = temp ?? t[0];
        if (v <= t[0]) return c[0];
        if (v <= t[1]) return Hex(Misturar(c[0], c[1], (v - t[0]) / Math.Max(1, t[1] - t[0])));
        if (v <= t[2]) return Hex(Misturar(c[1], c[2], (v - t[1]) / Math.Max(1, t[2] - t[1])));
        return c[2];
    }
}
