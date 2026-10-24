namespace SetupHub.Nucleo;

// Critério único do serviço (perfil alterado) e da janela (o que ainda vale do perfil).
public static class DiferencaPerfil
{
    static readonly Dictionary<string, Func<Perfil, Config, bool>> Diferente = new()
    {
        ["ventoinhas"] = (p, c) => p.Ventoinhas is not null && CurvasVentoinhas(VentoinhasDoPerfil(p, c)) != CurvasVentoinhas(c.Ventoinhas),
        ["luz"] = (p, c) => p.Luz is not null && (p.Luz.Plano != c.Luz.Plano || p.Luz.Ligada != c.Luz.Ligada),
        ["processador"] = (p, c) => p.Processador is not null && Json.Texto(p.Processador) != Json.Texto(c.Processador),
        ["placaDeVideo"] = (p, c) => p.PlacaDeVideo is not null && Json.Texto(p.PlacaDeVideo) != Json.Texto(c.PlacaDeVideo),
        ["graficos"] = (p, c) => p.Graficos is not null && Json.Texto(p.Graficos) != Json.Texto(c.Jogos.Global),
        ["sobreposicao"] = (p, c) => p.Sobreposicao is not null && Json.Texto(p.Sobreposicao) != Json.Texto(c.Jogos.Sobreposicao),
        ["energia"] = (p, c) => p.Energia is not null && p.Energia.Plano != c.Energia.Plano,
    };

    // Nomes, grupos e modo noite são da máquina e ficam fora da comparação.
    public static string CurvasVentoinhas(ConfigVentoinhas v) =>
        string.Join("|", v.Saidas.OrderBy(s => s.Id).Select(s =>
            $"{s.Id}:{s.Modo}:{s.Fixo}:{string.Join(",", s.Fontes)}:{s.Combinar}:{string.Join(",", s.Curva.Select(p => $"{p.T}/{p.P}"))}:{s.Suavizacao}:{s.Histerese}:{s.PararAbaixo}"))
        + "|" + Json.Texto(v.Gpu);

    // Com nível guardado, aplicar o perfil usa o nível e não a cópia de curvas que o perfil guarda.
    static ConfigVentoinhas VentoinhasDoPerfil(Perfil p, Config c)
    {
        if (!c.NiveisVentoinhas.ContainsKey(p.Ventoinhas!.Nivel)) return p.Ventoinhas;
        var v = Json.Copia(c.Ventoinhas);
        Padroes.AplicarNivel(c, v, p.Ventoinhas.Nivel, v.Saidas.Select(s => s.Id).Append("gpu"));
        return v;
    }

    public static bool Area(Perfil p, Config c, string area) => Diferente.TryGetValue(area, out var f) && f(p, c);

    public static bool Alterado(Config c)
    {
        var p = c.Perfis.FirstOrDefault(x => x.Nome == c.PerfilAtivo);
        if (p is null) return false;
        return p.Areas.Any(a => Area(p, c, a));
    }
}
