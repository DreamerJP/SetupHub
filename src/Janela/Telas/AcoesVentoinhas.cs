using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas;

static class AcoesVentoinhas
{
    public static readonly (string Id, string Nome)[] Niveis = { ("silencioso", "Silencioso"), ("equilibrado", "Equilibrado"), ("forte", "Forte"), ("maximo", "Máximo") };

    // Força com o PC parado: a primeira de gabinete do nível guardado, a 50 °C.
    public static string Repouso(Config? c, string nivel)
    {
        var s = c?.NiveisVentoinhas.GetValueOrDefault(nivel)?.Saidas.FirstOrDefault(x => x.Id.StartsWith("sys") && x.Modo != "bios");
        if (s is null) return nivel == "maximo" ? "100% sempre" : Texto(Curvas.Avaliar(Padroes.CurvaNivel(nivel, false), 50));
        return s.Modo == "fixo" ? (s.Fixo == 0 ? "sempre paradas" : $"{s.Fixo}% sempre") : Texto(Curvas.Avaliar(s.Curva, 50));
        static string Texto(double p) => p < 0.5 ? "parada automática" : $"{p:0}% em repouso";
    }

    // O nível vale para todas as ventoinhas; o ajuste feito depois fica guardado nele.
    public static Task Nivel(string nivel) =>
        Fazer("ventoinhas.nivel", Ligacao.Args(("nivel", nivel)));

    public static Task Fixar(IEnumerable<string> ids, int pct) =>
        Fazer("ventoinha", Ligacao.Args(("ids", ids.ToList()), ("valor", pct)), $"Fixadas em {pct}%.");

    public static Task Parar(IEnumerable<string> ids) =>
        Fazer("ventoinha", Ligacao.Args(("ids", ids.ToList()), ("valor", 0)), "Fixadas em 0%. A desaceleração leva alguns segundos.");

    public static Task Bios(IEnumerable<string> ids) =>
        Fazer("ventoinha", Ligacao.Args(("ids", ids.ToList()), ("modo", "bios")), "Curva da BIOS em uso.");

    public static Task Calibrar(IEnumerable<string> ids) =>
        Fazer("ventoinhas.calibrar", Ligacao.Args(("ids", ids.ToList())), "Calibração começou. As ventoinhas vão subir e descer por alguns minutos.");

    public static async Task Curva(IEnumerable<string> ids, Action<SaidaVentoinha> mudar)
    {
        var lista = ids.ToHashSet();
        var c = App.Lig.Config;
        if (c is null) return;
        var n = Json.Copia(c);
        foreach (var s in n.Ventoinhas.Saidas.Where(s => lista.Contains(s.Id)))
        {
            if (s.Modo != "curva")
            {
                s.Modo = "curva";
                if (s.Curva.Count < 2) s.Curva = Padroes.CurvaNivel("equilibrado", s.Id == "cpu");
            }
            mudar(s);
        }
        var r = await App.Lig.Pedir("config.gravar", Ligacao.Args(("config", Json.Elemento(n)), ("origem", "curva de ventoinha")));
        if (!r.Ok) App.Janela?.Aviso(r.Erro ?? "Recusado.", true);
    }

    static async Task Fazer(string cmd, System.Text.Json.JsonElement args, string? ok = null)
    {
        var r = await App.Lig.Pedir(cmd, args, 60000);
        if (!r.Ok) { App.Janela?.Aviso(r.Erro ?? "Recusado.", true); return; }
        var res = Json.Ler<ResultadoAplicar>(r.Dados);
        if (res is { Falhas.Count: > 0 }) App.Janela?.Aviso("Falhou: " + string.Join("; ", res.Falhas), true);
        else if (ok is not null) App.Janela?.Aviso(ok);
    }

    public static string Controle(SaidaVentoinha s) => s.Modo switch
    {
        "fixo" => $"fixo em {s.Fixo}%",
        "curva" => "curva",
        _ => "BIOS",
    };

    public static readonly (string Id, string Nome, string[] Fontes)[] Fontes =
    {
        ("maior", "Maior entre processador e placa de vídeo", new[] { "cpu", "gpu" }),
        ("cpu", "Processador", new[] { "cpu" }),
        ("gpu", "Placa de vídeo", new[] { "gpu" }),
        ("sistema", "Placa-mãe", new[] { "sistema" }),
        ("vrm", "Reguladores da placa-mãe", new[] { "vrm" }),
        ("ssd", "SSD", new[] { "ssd" }),
    };

    public static string FonteId(SaidaVentoinha s)
    {
        var f = s.Fontes.OrderBy(x => x).ToArray();
        foreach (var (id, _, fs) in Fontes) if (fs.OrderBy(x => x).SequenceEqual(f)) return id;
        return "maior";
    }
}
