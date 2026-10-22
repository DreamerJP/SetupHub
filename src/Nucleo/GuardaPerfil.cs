namespace SetupHub.Nucleo;

// Só as áreas que o perfil guarda e que mudaram nesta troca, para não desfazer o que veio editado no próprio perfil.
public static class GuardaPerfil
{
    public static void NoAtivo(Config n, Config? anterior)
    {
        if (anterior is null || n.PerfilAtivo != anterior.PerfilAtivo) return;
        var p = n.Perfis.FirstOrDefault(x => x.Nome == n.PerfilAtivo);
        if (p is null) return;
        bool Mudou(Func<Config, string> f) => f(n) != f(anterior);
        foreach (var a in p.Areas)
        {
            switch (a)
            {
                case "ventoinhas" when Mudou(c => DiferencaPerfil.CurvasVentoinhas(c.Ventoinhas) + "|" + c.Ventoinhas.Nivel):
                    p.Ventoinhas = Json.Copia(n.Ventoinhas);
                    p.Ventoinhas.Grupos = new();
                    break;
                case "luz" when Mudou(c => c.Luz.Plano + "|" + c.Luz.Ligada):
                    p.Luz = Luz(n);
                    break;
                case "processador" when Mudou(c => Json.Texto(c.Processador)): p.Processador = Json.Copia(n.Processador); break;
                case "placaDeVideo" when Mudou(c => Json.Texto(c.PlacaDeVideo)): p.PlacaDeVideo = Json.Copia(n.PlacaDeVideo); break;
                case "graficos" when Mudou(c => Json.Texto(c.Jogos.Global)): p.Graficos = Json.Copia(n.Jogos.Global); break;
                case "sobreposicao" when Mudou(c => Json.Texto(c.Jogos.Sobreposicao)): p.Sobreposicao = Json.Copia(n.Jogos.Sobreposicao); break;
                case "energia" when Mudou(c => c.Energia.Plano ?? ""):
                    p.Energia = new ConfigEnergia { Plano = n.Energia.Plano };
                    break;
            }
        }
    }

    // O nível das ventoinhas em uso guarda o ajuste atual; troca de perfil geral não grava nos níveis.
    public static void NoNivel(Config n, Config? anterior)
    {
        if (anterior is null || n.PerfilAtivo != anterior.PerfilAtivo || !Padroes.Niveis.Contains(n.Ventoinhas.Nivel)) return;
        bool mudou = n.Ventoinhas.Nivel != anterior.Ventoinhas.Nivel || DiferencaPerfil.CurvasVentoinhas(n.Ventoinhas) != DiferencaPerfil.CurvasVentoinhas(anterior.Ventoinhas);
        if (!mudou && n.NiveisVentoinhas.ContainsKey(n.Ventoinhas.Nivel)) return;
        n.NiveisVentoinhas[n.Ventoinhas.Nivel] = Padroes.Guardar(n.Ventoinhas);
    }

    // O perfil leva uma cópia do plano escolhido para continuar valendo se o plano sair da biblioteca.
    public static ConfigLuz Luz(Config n) => new()
    {
        Plano = n.Luz.Plano, Ligada = n.Luz.Ligada, Saidas = new(),
        Planos = n.Luz.Plano == "personalizado" ? new() : n.Luz.Planos.Where(x => x.Nome == n.Luz.Plano).Select(Json.Copia).ToList(),
    };

    // Valores de fábrica entram no lugar do que o perfil guarda; nome, atalho, horário e programas ficam.
    public static void Restaurar(Perfil p, Perfil fabrica)
    {
        p.Areas = fabrica.Areas.ToList();
        p.Ventoinhas = fabrica.Ventoinhas; p.Luz = fabrica.Luz; p.Processador = fabrica.Processador; p.PlacaDeVideo = fabrica.PlacaDeVideo;
        p.Graficos = fabrica.Graficos; p.Sobreposicao = fabrica.Sobreposicao; p.Energia = fabrica.Energia;
    }
}
