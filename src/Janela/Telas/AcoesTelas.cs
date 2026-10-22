using System.Windows.Media;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas;

static class AcoesTelas
{
    public static readonly (string Chave, string Nome, string Desc)[] Chaves =
    {
        ("freesync", "FreeSync", "taxa variável; elimina o rasgo de imagem"),
        ("vsr", "Resolução virtual", "renderiza acima da resolução da tela e reduz"),
        ("escalaGpu", "Escala pela placa de vídeo", "dimensionamento feito pela placa, não pela tela"),
        ("escalaInteira", "Escala inteira", "ampliação por múltiplos inteiros, sem borrão"),
        ("atualizacaoDinamica", "Atualização dinâmica", "o Windows reduz a taxa com imagem estática"),
        ("variBright", "Vari-Bright", "brilho adaptativo ao conteúdo, para economia de energia"),
    };

    public static readonly (string Chave, string Nome, double Padrao, string Un)[] Cores =
    {
        ("brilho", "Brilho", 0, ""), ("contraste", "Contraste", 100, ""), ("saturacao", "Saturação", 100, ""), ("temperatura", "Temperatura da cor", 6500, " K"),
    };

    public static readonly (string Valor, string Texto)[] Profundidades = { ("6", "6 bits"), ("8", "8 bits"), ("10", "10 bits"), ("12", "12 bits") };
    public static readonly (string Valor, string Texto)[] Formatos = { ("rgb-completo", "RGB completo"), ("rgb-limitado", "RGB limitado"), ("ycbcr444", "YCbCr 4:4:4"), ("ycbcr422", "YCbCr 4:2:2"), ("ycbcr420", "YCbCr 4:2:0") };
    // Sem a lista do driver, mostra todas: o aviso de falha diz qual a tela recusou.
    public static IEnumerable<(string, string)> Aceitas((string Valor, string Texto)[] todas, IEnumerable<string>? aceitas)
    {
        var l = aceitas?.ToList() ?? new();
        return todas.Where(o => l.Count == 0 || l.Contains(o.Valor)).Select(o => (o.Valor, o.Texto));
    }

    public static readonly string[] Barras = { "#C0C0C0", "#C0C000", "#00C0C0", "#00C000", "#C000C0", "#C00000", "#0000C0" };
    public static readonly string[] Amostras = { "#E8B28A", "#3A6E3A", "#6A8FC7" };

    public static void Definir(AjusteTela a, string chave, bool v)
    {
        switch (chave)
        {
            case "freesync": a.FreeSync = v; break;
            case "vsr": a.Vsr = v; break;
            case "escalaGpu": a.EscalaGpu = v; break;
            case "escalaInteira": a.EscalaInteira = v; break;
            case "atualizacaoDinamica": a.AtualizacaoDinamica = v; break;
            case "variBright": a.VariBright = v; break;
        }
    }

    public static bool? Valor(AjusteTela? a, string chave) => a is null ? null : chave switch
    {
        "freesync" => a.FreeSync, "vsr" => a.Vsr, "escalaGpu" => a.EscalaGpu, "escalaInteira" => a.EscalaInteira,
        "atualizacaoDinamica" => a.AtualizacaoDinamica, "variBright" => a.VariBright, _ => null,
    };

    public static int? Cor(AjusteTela? a, string chave) => a is null ? null : chave switch
    {
        "brilho" => a.Brilho, "contraste" => a.Contraste, "saturacao" => a.Saturacao, "temperatura" => a.Temperatura, _ => null,
    };

    public static void Cor(AjusteTela a, string chave, int v)
    {
        switch (chave)
        {
            case "brilho": a.Brilho = v; break;
            case "contraste": a.Contraste = v; break;
            case "saturacao": a.Saturacao = v; break;
            case "temperatura": a.Temperatura = v; break;
        }
    }

    public static AjusteTela? Ajuste(Config? c, string? id) => c?.Telas.Lista.FirstOrDefault(x => x.Id == id);

    public static async Task<bool> Mudar(InfoTela? info, Action<AjusteTela> mudar, string ok)
    {
        var c = App.Lig.Config;
        if (info is null || c is null) return false;
        var n = Json.Copia(c);
        var a = n.Telas.Lista.FirstOrDefault(x => x.Id == info.Id);
        if (a is null) n.Telas.Lista.Add(a = new AjusteTela { Id = info.Id, Nome = info.Nome });
        mudar(a);
        var r = await App.Lig.Pedir("config.gravar", Ligacao.Args(("config", Json.Elemento(n)), ("origem", "telas")));
        if (!r.Ok) { App.Janela?.Aviso(r.Erro ?? "Recusado.", true); return false; }
        var res = Json.Ler<ResultadoAplicar>(r.Dados);
        if (res is { Falhas.Count: > 0 }) App.Janela?.Aviso("Falhou: " + string.Join("; ", res.Falhas), true);
        else App.Janela?.Aviso(ok);
        return true;
    }

    // Aproximação: brilho multiplica, contraste afasta do meio, saturação afasta do cinza, temperatura puxa para vermelho ou azul.
    public static Color Transformar(Color c, double bri, double con, double sat, double tem)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double f = 1 + bri / 200; r *= f; g *= f; b *= f;
        double k = con / 100; r = (r - 0.5) * k + 0.5; g = (g - 0.5) * k + 0.5; b = (b - 0.5) * k + 0.5;
        double l = 0.2126 * r + 0.7152 * g + 0.0722 * b, s = sat / 100;
        r = l + (r - l) * s; g = l + (g - l) * s; b = l + (b - l) * s;
        double q = (6500 - tem) / 2500;
        r *= 1 + 0.10 * q; g *= 1 - 0.02 * q; b *= 1 - 0.22 * q;
        static byte B(double x) => (byte)Math.Round(Math.Clamp(x, 0, 1) * 255);
        return Color.FromRgb(B(r), B(g), B(b));
    }

    public static LinearGradientBrush Gradiente(Func<Color, Color> t)
    {
        var gr = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
        for (int i = 0; i <= 8; i++) { byte v = (byte)(i * 255 / 8); gr.GradientStops.Add(new GradientStop(t(Color.FromRgb(v, v, v)), i / 8.0)); }
        return gr;
    }
}
