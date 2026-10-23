using System.Windows.Media;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Kit;

// Aproximação do que o controlador mostra: o efeito real roda nele.
static class PreviaLuz
{
    // Cores: 0 sem escolha de cor, -1 de 1 a 4 cores, n fixo com os rótulos dados.
    public sealed record Tipo(string Id, string Nome, string Descricao, int Cores, string[] Rotulos, bool ArcoIris, bool Velocidade, bool Direcao, bool Fonte, bool DoMsi);

    public static readonly Tipo[] Tipos =
    {
        new("desligado", "Desligado", "Saída apagada.", 0, [], false, false, false, false, true),
        new("fixo", "Fixo", "Cor fixa.", 1, ["Cor"], false, false, false, false, true),
        new("respiracao", "Respiração", "A cor acende e apaga gradualmente.", 1, ["Cor"], true, true, false, false, true),
        new("onda", "Onda", "As cores percorrem a fita.", -1, [], true, true, true, false, true),
        new("chama", "Chama", "Alterna entre duas cores, imitando chama.", 2, ["Cor 1", "Cor 2"], true, true, false, false, true),
        new("temperatura", "Temperatura", "Muda de cor conforme a temperatura, em três faixas.", 3, ["Até", "Em", "A partir de"], false, false, false, true, true),
        new("anel", "Anel de cor", "As cores giram em círculo.", -1, [], true, true, true, false, true),
        new("musica", "Música", "Intensidade conforme o volume do áudio do PC.", 1, ["Cor"], true, true, false, false, true),
        new("meteoro", "Rastro de luz", "Ponto de luz em movimento, com rastro.", -1, [], true, true, true, false, true),
        new("carga", "Carga", "Cor conforme o uso do processador ou da placa de vídeo.", 2, ["Uso baixo", "Uso alto"], false, false, false, true, false),
        new("jogo", "Jogo", "Uma cor fora de jogo e outra com jogo aberto.", 2, ["Sem jogo", "Com jogo aberto"], false, false, false, false, false),
        new("horario", "Cores do dia", "As cores mudam ao longo do dia.", -1, [], false, false, false, false, false),
        new("alerta", "Alerta", "Cor fixa que pulsa em vermelho durante um alerta.", 1, ["Cor"], false, false, false, false, false),
    };

    public static Tipo DoTipo(string id) => Tipos.FirstOrDefault(t => t.Id == id) ?? Tipos.First(t => t.Id == "fixo");

    static Color C((double r, double g, double b) c) => Color.FromRgb((byte)Math.Clamp(c.r, 0, 255), (byte)Math.Clamp(c.g, 0, 255), (byte)Math.Clamp(c.b, 0, 255));
    static Color Hex(string h) => C(CoresLuz.Rgb(h));
    static Color Escala(Color c, double k) => Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    static readonly Color Apagada = Color.FromRgb(0x1A, 0x20, 0x28);

    static Color Hsl(double h, double s, double l)
    {
        h = (h % 360 + 360) % 360 / 360; s /= 100; l /= 100;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double F(double t) { t = (t + 1) % 1; return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p; }
        return Color.FromRgb((byte)(F(h + 1.0 / 3) * 255), (byte)(F(h) * 255), (byte)(F(h - 1.0 / 3) * 255));
    }

    // Gradiente que volta à primeira cor, para a onda emendar o fim no começo.
    static Color Ciclo(IReadOnlyList<string> cores, double x)
    {
        if (cores.Count == 0) return Colors.White;
        if (cores.Count == 1) return Hex(cores[0]);
        x = (x % 1 + 1) % 1 * cores.Count;
        int i = (int)x % cores.Count;
        return C(CoresLuz.Misturar(cores[i], cores[(i + 1) % cores.Count], x - (int)x));
    }

    public static Color[] Cores(EfeitoLuz f, int n, double t, Estado? e)
    {
        var r = new Color[n];
        double vel = f.Velocidade switch { 0 => 0.5, 1 => 1, _ => 2 };
        double sentido = f.Direcao == "dentro" ? -1 : 1;
        var cores = f.Cores.Count == 0 ? new List<string> { "#FFFFFF" } : f.Cores;
        double arco = t * 40 * vel;
        for (int i = 0; i < n; i++)
        {
            double x = n == 1 ? 0 : i / (double)n;
            r[i] = f.Tipo switch
            {
                "desligado" => Apagada,
                "fixo" => Hex(cores[0]),
                "respiracao" => Escala(f.ArcoIris ? Hsl(Math.Floor(t * vel / 3) * 60, 100, 50) : Hex(cores[0]), 0.08 + 0.92 * (0.5 - 0.5 * Math.Cos(t * vel * Math.PI * 2 / 3))),
                "onda" => f.ArcoIris ? Hsl(x * 360 - sentido * arco * 3, 100, 50) : Ciclo(cores, x - sentido * t * 0.15 * vel),
                "anel" => f.ArcoIris ? Hsl(x * 360 + sentido * arco * 4, 100, 50) : Ciclo(cores, x + sentido * t * 0.2 * vel),
                "chama" => Escala(f.ArcoIris ? Hsl(20 + 15 * Math.Sin(t * 5 + i), 100, 50) : C(CoresLuz.Misturar(cores[0], cores[Math.Min(1, cores.Count - 1)], 0.5 + 0.5 * Math.Sin(t * 6 * vel + i * 1.7) * Math.Sin(t * 2.3 + i * 0.6))), 0.55 + 0.45 * Math.Abs(Math.Sin(t * 9 * vel + i * 2.1))),
                "temperatura" => Hex(CoresLuz.Temperatura(f, f.Fonte switch { "gpu" => e?.Gpu.TempMaisQuente, "maior" => Math.Max(e?.Cpu.TempMaisQuente ?? 0, e?.Gpu.TempMaisQuente ?? 0), _ => e?.Cpu.TempMaisQuente })),
                "musica" => i < n * Batida(t) ? (f.ArcoIris ? Hsl(x * 300, 100, 50) : Hex(cores[0])) : Apagada,
                "meteoro" => Meteoro(f, cores, x, t * vel * sentido),
                "carga" => C(CoresLuz.Gradiente(cores, ((f.Fonte == "gpu" ? e?.Gpu.Uso : e?.Cpu.Uso) ?? 20) / 100)),
                "jogo" => Hex(e?.Jogo is not null ? cores[Math.Min(1, cores.Count - 1)] : cores[0]),
                "horario" => C(CoresLuz.Gradiente(cores, DateTime.Now.TimeOfDay.TotalHours / 24)),
                "alerta" => e?.Alertas.Count > 0 ? Escala(Color.FromRgb(0xFF, 0x3B, 0x1F), 0.3 + 0.7 * Math.Abs(Math.Sin(t * 3))) : Hex(cores[0]),
                _ => Hex(cores[0]),
            };
        }
        return r;
    }

    static double Batida(double t) => Math.Clamp(0.25 + 0.45 * Math.Abs(Math.Sin(t * 2.1)) + 0.3 * Math.Abs(Math.Sin(t * 5.3)), 0, 1);

    static Color Meteoro(EfeitoLuz f, IReadOnlyList<string> cores, double x, double t)
    {
        double cabeca = ((t * 0.5) % 1 + 1) % 1;
        double d = cabeca - x;
        if (d < 0) d += 1;
        if (d > 0.35) return Apagada;
        var c = f.ArcoIris ? Hsl(t * 90, 100, 50) : Hex(cores[(int)Math.Floor(Math.Abs(t * 0.5)) % cores.Count]);
        return Escala(c, Math.Pow(1 - d / 0.35, 2));
    }

    public static EfeitoLuz? DoPlano(Config c, string? plano) =>
        plano is null ? null : c.Luz.Planos.FirstOrDefault(p => p.Nome == plano)?.Efeito;

    public static string Descricao(Config c, string plano) =>
        DoPlano(c, plano) is { } ef ? DoTipo(ef.Tipo).Descricao : "";
}
