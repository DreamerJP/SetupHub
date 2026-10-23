using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas;

static class AcoesLuz
{
    public static readonly string[] CoresIniciais = { "#FF0000", "#00FF00", "#0000FF", "#FFFFFF" };

    public static string Tipo(string id) => id switch
    {
        "jrgb1" => "fita de 12 V, uma cor por vez",
        "ezconn" => "conector combinado",
        _ => "fita endereçável de 5 V",
    };

    public static string PlanoDa(Config c, string id)
    {
        if (!c.Luz.Ligada) return "Desligada";
        var s = c.Luz.Saidas.FirstOrDefault(x => x.Id == id);
        var nome = s?.Plano ?? c.Luz.Plano;
        if (c.Luz.Planos.Any(p => p.Nome == nome)) return nome;
        return s is null ? "Efeito próprio" : PreviaLuz.DoTipo(s.Efeito.Tipo).Nome;
    }

    public static EfeitoLuz EfeitoDa(Config c, string id)
    {
        var s = c.Luz.Saidas.FirstOrDefault(x => x.Id == id);
        if (!c.Luz.Ligada || s is null || !s.Ativa) return new EfeitoLuz { Tipo = "desligado" };
        var nome = s.Plano ?? c.Luz.Plano;
        var p = c.Luz.Planos.FirstOrDefault(x => x.Nome == nome);
        if (p is null) return s.Efeito;
        return p.PorSaida.TryGetValue(id, out var ps) ? ps : p.Efeito;
    }

    // Plano que as saídas usam; nulo quando têm efeito próprio.
    public static string? PlanoDasSaidas(Config c, IEnumerable<string> ids)
    {
        var s = c.Luz.Saidas.FirstOrDefault(x => x.Id == ids.FirstOrDefault());
        var nome = s?.Plano ?? c.Luz.Plano;
        return c.Luz.Planos.Any(p => p.Nome == nome) ? nome : null;
    }

    public static Task Plano(IEnumerable<string> ids, string nome) =>
        Fazer("luz.plano", Ligacao.Args(("nome", nome), ("ids", ids.ToList())), $"Plano {nome}.");

    public static Task SalvarPlano(string nome) => Fazer("luz.salvarPlano", Ligacao.Args(("nome", nome)), $"Plano {nome} criado.");
    public static Task ExcluirPlano(string nome) => Fazer("luz.excluirPlano", Ligacao.Args(("nome", nome)), $"Plano {nome} excluído.");
    public static Task RestaurarPlano(string nome) => Fazer("luz.restaurarPlano", Ligacao.Args(("nome", nome)), $"Plano {nome} de volta ao padrão.");

    public static bool PlanoAlterado(Config c, string nome) =>
        c.Luz.Planos.FirstOrDefault(x => x.Nome == nome) is { } p && Padroes.PlanosProntos().FirstOrDefault(x => x.Nome == nome) is { } f && Json.Texto(p) != Json.Texto(f);

    public static Task Efeito(IEnumerable<string> ids, EfeitoLuz ef, string? ok = null) =>
        Fazer("luz.efeito", Ligacao.Args(("ids", ids.ToList()), ("efeito", Json.Elemento(ef))), ok);

    // Ajuste do efeito: muda o plano em uso (vale em toda saída com ele) ou o efeito próprio das saídas.
    public static Task Gravar(Config c, IEnumerable<string> ids, EfeitoLuz ef, string ok)
    {
        var plano = PlanoDasSaidas(c, ids);
        return plano is null ? Efeito(ids, ef, ok) : AjustarPlano(plano, e => Copiar(ef, e), $"{ok} Aplicado a todas as saídas do plano {plano}.");
    }

    static void Copiar(EfeitoLuz de, EfeitoLuz para)
    {
        para.Tipo = de.Tipo; para.Cores = de.Cores.ToList(); para.ArcoIris = de.ArcoIris; para.Brilho = de.Brilho;
        para.Velocidade = de.Velocidade; para.Direcao = de.Direcao; para.Fonte = de.Fonte; para.Temperaturas = de.Temperaturas.ToList();
    }

    // Efeito novo do tipo escolhido, aproveitando cores, brilho e velocidade do que estava.
    public static EfeitoLuz NovoEfeito(string tipo, EfeitoLuz? atual)
    {
        var t = PreviaLuz.DoTipo(tipo);
        var cores = atual is null || atual.Tipo is "temperatura" or "desligado" ? new List<string>() : atual.Cores.ToList();
        var ef = new EfeitoLuz
        {
            Tipo = tipo, Brilho = atual is { Brilho: > 0 } ? atual.Brilho : 100, Velocidade = atual?.Velocidade ?? 1,
            Direcao = atual?.Direcao ?? "fora", Fonte = "cpu", Temperaturas = atual?.Temperaturas.ToList() ?? new() { 50, 70, 90 },
        };
        ef.Cores = tipo switch
        {
            "temperatura" => Padroes.CoresTemperatura(),
            "desligado" => new() { "#000000" },
            _ when t.Cores <= 0 => cores.Count >= 2 ? cores.Take(4).ToList() : CoresIniciais.ToList(),
            _ => Enumerable.Range(0, t.Cores).Select(i => cores.ElementAtOrDefault(i) ?? CoresIniciais[i]).ToList(),
        };
        return ef;
    }

    public static async Task AjustarPlano(string nome, Action<EfeitoLuz> mudar, string? ok = null)
    {
        var c = App.Lig.Config;
        if (c is null) return;
        var n = Json.Copia(c);
        var p = n.Luz.Planos.FirstOrDefault(x => x.Nome == nome);
        if (p is null) return;
        mudar(p.Efeito);
        foreach (var e in p.PorSaida.Values) mudar(e);
        var r = await App.Lig.Pedir("config.gravar", Ligacao.Args(("config", Json.Elemento(n)), ("origem", "ajuste do plano de luz")));
        if (!r.Ok) App.Janela?.Aviso(r.Erro ?? "Recusado.", true);
        else if (Json.Ler<ResultadoAplicar>(r.Dados) is { Falhas.Count: > 0 } res) App.Janela?.Aviso("Falhou: " + string.Join("; ", res.Falhas), true);
        else if (ok is not null) App.Janela?.Aviso(ok);
    }

    static async Task Fazer(string cmd, System.Text.Json.JsonElement args, string? ok)
    {
        var r = await App.Lig.Pedir(cmd, args, 30000);
        if (!r.Ok) { App.Janela?.Aviso(r.Erro ?? "Recusado.", true); return; }
        var res = Json.Ler<ResultadoAplicar>(r.Dados);
        if (res is { Falhas.Count: > 0 }) App.Janela?.Aviso("Falhou: " + string.Join("; ", res.Falhas), true);
        else if (ok is not null) App.Janela?.Aviso(ok);
    }
}
