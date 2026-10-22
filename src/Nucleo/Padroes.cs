namespace SetupHub.Nucleo;

public static class Padroes
{
    // Saídas de ventoinha do chip NCT6687D-R desta placa, com o índice do chip e o nome do conector.
    public static readonly (string Id, string Nome, string Conector)[] Ventoinhas =
    {
        ("cpu", "Fan CPU", "CPU_FAN1"),
        ("bomba", "Bomba", "PUMP_SYS_FAN1"),
        ("sys1", "Fan 1", "SYS_FAN1"),
        ("sys2", "Fan 2", "SYS_FAN2"),
        ("sys3", "Fan 3", "SYS_FAN3"),
        ("sys4", "Fan 4", "SYS_FAN4"),
        ("ez", "EZ Conn", "EZ_CONN"),
    };

    public static readonly (string Id, string Nome, int Area, int LedsPadrao)[] SaidasLuz =
    {
        ("jrgb1", "JRGB 1", 9, 1),
        ("jargb1", "JARGB 1", 0, 30),
        ("jargb2", "JARGB 2", 1, 30),
        ("jargb3", "JARGB 3", 2, 30),
        ("ezconn", "EZ Conn", 3, 30),
    };

    public static readonly string[] Niveis = { "silencioso", "equilibrado", "forte", "maximo" };

    // Planos de energia que o SetupHub cria no Windows; os do próprio Windows ficam de fora da tela.
    public static readonly (string Id, string Nome, string Descricao)[] PlanosEnergia =
    {
        ("economia", "Economia", "Processador limitado a 30%, sem turbo"),
        ("equilibrado", "Equilibrado", "Turbo sob demanda, economia em repouso"),
        ("desempenho", "Desempenho", "Clock alto constante, sem economia"),
    };

    public static string NomePlano(string? id) => PlanosEnergia.FirstOrDefault(p => p.Id == id).Nome ?? id ?? "";

    // Nenhum plano desliga a tela nem suspende o PC sozinho; quem quiser, liga no próprio plano.
    // 30% de teto: abaixo de uns 26% o Ryzen sai do degrau mais baixo e passa a pausar, caindo para uns 450 MHz efetivos.
    public static PlanoEnergia PlanoEnergiaPadrao(string id) => id switch
    {
        "economia" => new() { Id = id, ProcessadorMinimo = 5, ProcessadorMaximo = 30, NucleosMinimos = 10, PreferenciaEnergia = 25, Impulso = "desligado", SuspensaoUsb = false, EconomiaPcie = "maximo", DesligarTela = 0, Suspender = 0 },
        "desempenho" => new() { Id = id, ProcessadorMinimo = 100, ProcessadorMaximo = 100, NucleosMinimos = 100, PreferenciaEnergia = 10, Impulso = "agressivo", SuspensaoUsb = false, EconomiaPcie = "desligado", DesligarTela = 0, Suspender = 0 },
        _ => new() { Id = "equilibrado", ProcessadorMinimo = 5, ProcessadorMaximo = 100, NucleosMinimos = 100, PreferenciaEnergia = 80, Impulso = "agressivo", SuspensaoUsb = false, EconomiaPcie = "moderado", DesligarTela = 0, Suspender = 0 },
    };

    public const double CriticaCpu = 90;
    public const double CriticaGpu = 90;

    public static List<Ponto> CurvaNivel(string nivel, bool processador) => (nivel, processador) switch
    {
        ("silencioso", true) => new() { new(40, 22), new(60, 30), new(75, 50), new(85, 80), new(90, 100) },
        ("silencioso", false) => new() { new(60, 0), new(65, 20), new(75, 40), new(85, 70), new(90, 100) },
        ("forte", true) => new() { new(40, 35), new(55, 50), new(70, 75), new(80, 90), new(88, 100) },
        ("forte", false) => new() { new(40, 30), new(55, 45), new(70, 70), new(80, 85), new(88, 100) },
        ("maximo", _) => new() { new(20, 100), new(90, 100) },
        (_, true) => new() { new(40, 25), new(60, 40), new(75, 65), new(85, 90), new(90, 100) },
        _ => new() { new(40, 20), new(60, 35), new(75, 45), new(85, 75), new(90, 100) },
    };

    public static List<Ponto> CurvaGpu(string nivel) => nivel switch
    {
        "silencioso" => new() { new(40, 20), new(55, 25), new(70, 40), new(80, 65), new(90, 100) },
        "forte" => new() { new(40, 35), new(55, 45), new(65, 60), new(75, 80), new(85, 100) },
        "maximo" => new() { new(30, 100), new(45, 100), new(60, 100), new(75, 100), new(90, 100) },
        _ => new() { new(40, 25), new(55, 35), new(70, 50), new(80, 75), new(90, 100) },
    };

    public static void AplicarNivel(ConfigVentoinhas v, string nivel, IEnumerable<string>? ids = null)
    {
        var alvo = ids is null ? null : new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        foreach (var s in v.Saidas)
        {
            if (alvo is not null && !alvo.Contains(s.Id)) continue;
            bool cpu = s.Id == "cpu";
            if (nivel == "maximo") { s.Modo = "fixo"; s.Fixo = 100; continue; }
            s.Modo = "curva";
            s.Curva = CurvaNivel(nivel, cpu);
            s.Fontes = cpu ? new() { "cpu" } : new() { "cpu", "gpu" };
            s.Combinar = "maior";
        }
        if (alvo is null || alvo.Contains("gpu"))
        {
            v.Gpu.Modo = "curva";
            v.Gpu.Curva = CurvaGpu(nivel);
        }
        if (alvo is null) v.Nivel = nivel;
    }

    public static NivelVentoinhas Guardar(ConfigVentoinhas v) => new() { Saidas = v.Saidas.Select(Json.Copia).ToList(), Gpu = Json.Copia(v.Gpu) };

    // O nível guardado manda; sem ele (nível nunca usado) valem as curvas de fábrica.
    public static void AplicarNivel(Config c, ConfigVentoinhas v, string nivel, IEnumerable<string> ids)
    {
        if (!c.NiveisVentoinhas.TryGetValue(nivel, out var g)) { AplicarNivel(v, nivel, ids); return; }
        var alvo = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        foreach (var s in v.Saidas)
        {
            var o = g.Saidas.FirstOrDefault(x => x.Id == s.Id);
            if (o is null || !alvo.Contains(s.Id)) continue;
            s.Modo = o.Modo; s.Fixo = o.Fixo; s.Fontes = o.Fontes.ToList(); s.Combinar = o.Combinar;
            s.Curva = o.Curva.Select(x => new Ponto(x.T, x.P)).ToList(); s.Suavizacao = o.Suavizacao; s.Histerese = o.Histerese; s.PararAbaixo = o.PararAbaixo;
        }
        if (alvo.Contains("gpu")) v.Gpu = Json.Copia(g.Gpu);
    }

    public static EfeitoLuz Efeito(string tipo, string[] cores, int brilho = 100, int velocidade = 1, bool arcoIris = false, string fonte = "cpu")
        => new() { Tipo = tipo, Cores = cores.ToList(), Brilho = brilho, Velocidade = velocidade, ArcoIris = arcoIris, Fonte = fonte };

    public static readonly (string Tipo, string Nome)[] Quadros =
    {
        ("processador", "Processador"), ("placaDeVideo", "Placa de vídeo"), ("ventoinhas", "Ventoinhas"),
        ("modoProcessador", "Modo do processador"), ("temperatura", "Temperatura nos últimos minutos"), ("luz", "Luz"), ("fluxoDeAr", "Fluxo de ar"),
    };

    public static List<CartaoInicio> QuadrosInicio() => Quadros.Select(q => new CartaoInicio { Tipo = q.Tipo, Visivel = q.Tipo != "fluxoDeAr" }).ToList();

    public static List<string> CoresTemperatura() => new() { "#00FF00", "#0000FF", "#FF0000" };

    public static List<PlanoLuz> PlanosProntos() => new()
    {
        new() { Nome = "Temperatura", Pronto = true, Efeito = new EfeitoLuz { Tipo = "temperatura", Cores = CoresTemperatura(), Brilho = 100, Fonte = "cpu" } },
        new() { Nome = "Arco-íris", Pronto = true, Efeito = Efeito("onda", new[] { "#FF0000" }, 60, 0, true) },
        new() { Nome = "Foco", Pronto = true, Efeito = Efeito("fixo", new[] { "#DDE8FF" }, 40) },
        new() { Nome = "Respiração", Pronto = true, Efeito = Efeito("respiracao", new[] { "#FF7A2E" }, 20, 0) },
        new() { Nome = "Chama", Pronto = true, Efeito = Efeito("chama", new[] { "#FF3B1F", "#FFD166" }, 80) },
        new() { Nome = "Anel de cor", Pronto = true, Efeito = Efeito("anel", new[] { "#FF0000" }, 60, 0, true) },
        new() { Nome = "Rastro de luz", Pronto = true, Efeito = Efeito("meteoro", new[] { "#FF0000" }, 60, 0, true) },
        new() { Nome = "Cores do dia", Pronto = true, Efeito = Efeito("horario", new[] { "#1E4F9A", "#F7FBFF", "#FF8A3D" }, 70) },
        new() { Nome = "Jogo", Pronto = true, Efeito = Efeito("jogo", new[] { "#3E7BFA", "#8BB2FF" }, 80) },
        new() { Nome = "Carga", Pronto = true, Efeito = Efeito("carga", new[] { "#2E7BD6", "#FF3B1F" }, 80) },
        new() { Nome = "Música", Pronto = true, Efeito = Efeito("musica", new[] { "#A8E06C" }, 100, 1, true) },
        new() { Nome = "Alerta", Pronto = true, Efeito = Efeito("alerta", new[] { "#3E7BFA" }, 20) },
        new() { Nome = "Desligada", Pronto = true, Efeito = Efeito("desligado", new[] { "#000000" }, 0) },
    };

    static readonly Dictionary<string, string> PlanosRenomeados = new() { ["Temperatura viva"] = "Temperatura", ["Arco-íris suave"] = "Arco-íris", ["Som"] = "Música" };
    static readonly HashSet<string> PlanosLuzNovos = new(StringComparer.OrdinalIgnoreCase) { "Chama", "Anel de cor", "Rastro de luz", "Cores do dia" };

    // Config de antes de 16/10/2026: efeitos imitados viram os do MSI Center, e o bloco de perfil do Início sai porque os perfis ficam fixos no alto.
    public static bool Migrar(Config c)
    {
        var antes = Json.Texto(c);
        // A versão garante que a devolução rode uma vez só: depois disso, limite vazio é escolha do usuário.
        if (c.Versao < 2) { DevolverLimitesPerdidos(c); c.Versao = 2; }
        if (c.Versao < 3) { RenomearPlanoProprio(c); c.Versao = 3; }
        if (c.Versao < 4) { PlanosDoWindowsParaProprios(c); c.Versao = 4; }
        if (c.Versao < 5) { TresPlanosDeEnergia(c); c.Versao = 5; }
        if (c.Versao < 6) { UmPerfilDeDesempenho(c); c.Versao = 6; }
        if (c.Versao < 7) { PerfilEconomico(c); c.Versao = 7; }
        if (c.Versao < 8) { foreach (var p in c.Energia.Planos) p.PreferenciaEnergia = PlanoEnergiaPadrao(p.Id).PreferenciaEnergia; c.Versao = 8; }
        if (c.Versao < 9) { PlanosDeLuzRevistos(c); c.Versao = 9; }
        if (c.Versao < 10) { PreservarPlanosLuz(c); c.Versao = 10; }
        if (c.Versao < 11) c.Versao = 11;
        if (c.Versao < 12) c.Versao = 12;
        foreach (var (id, _, _) in PlanosEnergia)
            if (!c.Energia.Planos.Any(p => p.Id == id)) c.Energia.Planos.Add(PlanoEnergiaPadrao(id));
        string Nome(string? n) => n is not null && PlanosRenomeados.TryGetValue(n, out var novo) ? novo : n ?? "";
        var l = c.Luz;
        foreach (var p in l.Planos) p.Nome = Nome(p.Nome);
        l.Plano = Nome(l.Plano);
        foreach (var s in l.Saidas) if (s.Plano is not null) s.Plano = Nome(s.Plano);
        foreach (var p in c.Perfis) if (p.Luz is not null) p.Luz.Plano = Nome(p.Luz.Plano);
        foreach (var j in c.Jogos.Lista) if (j.Luz is not null) j.Luz = Nome(j.Luz);
        RemoverRelampago(c);
        MigrarEfeitos(l);
        foreach (var p in c.Perfis) if (p.Luz is not null) MigrarEfeitos(p.Luz);

        var prontos = PlanosProntos();
        var ordem = new List<PlanoLuz>();
        foreach (var novo in prontos)
        {
            var velho = l.Planos.FirstOrDefault(p => p.Nome == novo.Nome);
            if (velho is not null && velho.Efeito.Tipo == novo.Efeito.Tipo) { velho.Pronto = true; ordem.Add(velho); }
            else ordem.Add(novo);
        }
        ordem.AddRange(l.Planos.Where(p => !prontos.Any(n => n.Nome == p.Nome)));
        l.Planos = ordem;
        return Json.Texto(c) != antes;
    }

    static void MigrarEfeito(EfeitoLuz f)
    {
        bool relampago = f.Tipo == "relampago";
        f.Tipo = f.Tipo switch { "recreacao" or "som" => "musica", "godlike" => "padrao", "degrade" => "onda", "relampago" => "fixo", _ => f.Tipo };
        if (relampago) f.ArcoIris = false;
        if (f.Temperaturas.Count != 3) f.Temperaturas = new() { 50, 70, 90 };
        if (f.Tipo == "temperatura" && f.Cores.Count != 3) f.Cores = CoresTemperatura();
        if (f.Tipo is "musica" && f.Cores.Count > 1) f.Cores = new() { f.Cores[^1] };
        if (f.Cores.Count > 4) f.Cores = f.Cores.Take(4).ToList();
    }

    static void RemoverRelampago(Config c)
    {
        RemoverPlanoRelampago(c.Luz);
        foreach (var perfil in c.Perfis)
            if (perfil.Luz is { } luz) RemoverPlanoRelampago(luz);
        if (!TemRelampago(c.Luz.Planos))
        {
            TrocarPlanoRelampago(c.Luz);
            foreach (var jogo in c.Jogos.Lista)
                if (EhRelampago(jogo.Luz)) jogo.Luz = "Temperatura";
        }
        foreach (var perfil in c.Perfis)
            if (perfil.Luz is { } luz && !TemRelampago(c.Luz.Planos.Concat(luz.Planos))) TrocarPlanoRelampago(luz);
    }

    static void RemoverPlanoRelampago(ConfigLuz luz) => luz.Planos.RemoveAll(p => p.Pronto && EhRelampago(p.Nome));

    static void TrocarPlanoRelampago(ConfigLuz luz)
    {
        if (EhRelampago(luz.Plano)) luz.Plano = "Temperatura";
        foreach (var saida in luz.Saidas)
            if (EhRelampago(saida.Plano)) saida.Plano = "Temperatura";
    }

    static bool EhRelampago(string? nome) => string.Equals(nome, "Relâmpago", StringComparison.OrdinalIgnoreCase);

    static bool TemRelampago(IEnumerable<PlanoLuz> planos) => planos.Any(p => EhRelampago(p.Nome));

    static void MigrarEfeitos(ConfigLuz luz)
    {
        foreach (var efeito in luz.Planos.SelectMany(p => p.PorSaida.Values.Prepend(p.Efeito)).Concat(luz.Saidas.Select(s => s.Efeito)))
            MigrarEfeito(efeito);
    }

    public static Config Config()
    {
        var c = new Config { Versao = 12 };
        foreach (var (id, nome, _) in Ventoinhas)
            c.Ventoinhas.Saidas.Add(new SaidaVentoinha { Id = id, Nome = nome, Modo = "bios" });
        AplicarNivel(c.Ventoinhas, "equilibrado", new[] { "cpu", "sys1", "sys2", "sys3", "gpu" });
        c.Ventoinhas.Nivel = "equilibrado";
        c.Ventoinhas.Grupos.Add(new Grupo { Nome = "Gabinete", Tipo = "ventoinhas", Itens = new() { "sys1", "sys2", "sys3" } });
        foreach (var (id, nome, _, leds) in SaidasLuz)
            c.Luz.Saidas.Add(new SaidaLuz { Id = id, Nome = nome, Leds = leds, Ativa = true });
        c.Luz.Planos = PlanosProntos();
        c.Luz.Plano = "Temperatura";
        c.Alertas.Add(new Alerta { Tipo = "temperatura", Sensor = "cpu.temp", Acima = 90 });
        c.Alertas.Add(new Alerta { Tipo = "temperatura", Sensor = "gpu.quente", Acima = 100 });
        c.Alertas.Add(new Alerta { Tipo = "ventoinha-parada", Sensor = "ventoinhas", Acima = 0 });
        c.Alertas.Add(new Alerta { Tipo = "queda-clock", Sensor = "cpu.clock", Acima = 95, Ligado = false });
        c.Aparencia.Inicio = QuadrosInicio();
        c.Energia.Plano = "equilibrado";
        c.Energia.Planos = PlanosEnergia.Select(p => PlanoEnergiaPadrao(p.Id)).ToList();
        c.Perfis = PerfisProntos(c);
        c.PerfilAtivo = "Equilibrado";
        return c;
    }

    public static List<Perfil> PerfisProntos(Config baseConfig)
    {
        var lista = new List<Perfil>();
        // ppt: nulo deixa o padrão de fábrica; -1 vira o máximo que o kit da AMD aceitar (resolvido em AjustarAoHardware).
        Perfil Montar(string nome, string nivel, string luz, string? energia, int? gpuEnergia, int? ppt, bool antiLag, string atalho)
        {
            var v = Json.Copia(baseConfig.Ventoinhas);
            AplicarNivel(v, nivel, new[] { "cpu", "sys1", "sys2", "sys3", "gpu" });
            v.Nivel = nivel;
            v.Grupos = new();
            // O perfil guarda só o plano de luz; saídas e biblioteca de planos são da máquina.
            var l = new ConfigLuz { Plano = luz, Saidas = new(), Planos = new() };
            var p = new Perfil
            {
                Nome = nome, Pronto = true, Atalho = atalho,
                Areas = new() { "ventoinhas", "processador", "placaDeVideo", "luz", "energia" },
                Ventoinhas = v, Luz = l,
                Processador = new ConfigProcessador { Ppt = ppt },
                PlacaDeVideo = new ConfigPlacaDeVideo { LimiteEnergia = gpuEnergia },
                Energia = new ConfigEnergia { Plano = energia },
            };
            if (antiLag)
            {
                p.Areas.Add("graficos");
                p.Graficos = new Ajustes3D { AntiLag = true };
            }
            return p;
        }
        lista.Add(Montar("Econômico", "silencioso", "Respiração", "economia", null, 88, false, "Ctrl+Alt+1"));
        lista.Add(Montar("Equilibrado", "equilibrado", "Temperatura", "equilibrado", null, null, false, "Ctrl+Alt+2"));
        lista.Add(Montar("Desempenho", "forte", "Foco", "desempenho", 10, -1, true, "Ctrl+Alt+3"));
        return lista;
    }

    // Só o perfil pronto que mantém o nome de fábrica tem para onde voltar.
    public static Perfil? Fabrica(Perfil p) => p.Pronto ? PerfisProntos(Config()).FirstOrDefault(x => x.Nome == p.Nome) : null;

    // Só ajusta campo lido e aceito: travado ou não lido fica como está, para não se perder numa partida em que o kit da AMD falhou.
    public static void AjustarAoHardware(Config c, Faixas f)
    {
        foreach (var p in c.Perfis)
        {
            if (p.Processador is { } pr) pr.Ppt = Dentro(f, "processador.ppt", pr.Ppt);
            if (p.PlacaDeVideo is { } g) g.LimiteEnergia = Dentro(f, "placaDeVideo.limiteEnergia", g.LimiteEnergia);
        }
    }

    // -1 é "o máximo que o hardware aceitar".
    public static int? Dentro(Faixas f, string chave, int? v)
    {
        if (v is null || !f.Campos.TryGetValue(chave, out var x) || !x.Disponivel) return v;
        return v == -1 ? (int)x.Max : (int)Math.Clamp(v.Value, x.Min, x.Max);
    }

    // O plano de energia próprio tinha o nome antigo do programa.
    static void RenomearPlanoProprio(Config c)
    {
        string N(string? p) => p == Produto.Antigo ? Produto.Nome : p!;
        if (c.Energia.Plano is not null) c.Energia.Plano = N(c.Energia.Plano);
        foreach (var r in c.Energia.Regras) r.Plano = N(r.Plano);
        foreach (var p in c.Perfis) if (p.Energia?.Plano is not null) p.Energia.Plano = N(p.Energia.Plano);
    }

    // Antes de 17/10/2026 a energia usava os planos do Windows pelo nome: cada um vira o plano próprio equivalente.
    static void PlanosDoWindowsParaProprios(Config c)
    {
        static string? N(string? nome) => nome?.ToLowerInvariant() switch
        {
            "economia de energia" or "power saver" => "economia",
            "equilibrado" or "balanced" => "equilibrado",
            "alto desempenho" or "high performance" => "desempenho",
            "desempenho máximo" or "ultimate performance" or "setuphub" => "maximo",
            _ => null,
        };
        c.Energia.Plano = N(c.Energia.Plano);
        foreach (var r in c.Energia.Regras) r.Plano = N(r.Plano) ?? "economia";
        foreach (var p in c.Perfis) if (p.Energia is not null) p.Energia.Plano = N(p.Energia.Plano);
    }

    // Até a versão 1.1 havia o plano Máximo e os planos desligavam a tela e suspendiam o PC: o Máximo vira Desempenho e os ajustes voltam aos de fábrica novos.
    static void TresPlanosDeEnergia(Config c)
    {
        static string? N(string? id) => id == "maximo" ? "desempenho" : id;
        c.Energia.Plano = N(c.Energia.Plano);
        foreach (var r in c.Energia.Regras) r.Plano = N(r.Plano) ?? "economia";
        foreach (var p in c.Perfis) if (p.Energia is not null) p.Energia.Plano = N(p.Energia.Plano);
        c.Energia.Planos = PlanosEnergia.Select(p => PlanoEnergiaPadrao(p.Id)).ToList();
    }

    // Até a versão 1.3 os perfis prontos Jogo e Desempenho eram quase iguais: fica só o Desempenho, no formato de fábrica novo.
    static void UmPerfilDeDesempenho(Config c)
    {
        var novo = PerfisProntos(c).First(p => p.Nome == "Desempenho");
        int i = c.Perfis.FindIndex(p => p.Nome == "Desempenho");
        if (i >= 0) c.Perfis[i] = novo; else c.Perfis.Add(novo);
        c.Perfis.RemoveAll(p => p.Nome == "Jogo" && p.Pronto);
        if (c.PerfilAtivo == "Jogo") c.PerfilAtivo = "Desempenho";
        foreach (var j in c.Jogos.Lista) if (j.Perfil == "Jogo") j.Perfil = "Desempenho";
    }

    static void PlanosDeLuzRevistos(Config c)
    {
        var l = c.Luz;
        var trocas = new Dictionary<string, string>();
        if (l.Planos.RemoveAll(p => p.Pronto && p.Nome == "Padrão MSI") > 0) trocas["Padrão MSI"] = "Arco-íris";
        foreach (var p in l.Planos.Where(p => p.Pronto && p.Nome == "Noite")) { p.Nome = "Respiração"; trocas["Noite"] = "Respiração"; }
        if (trocas.Count == 0) return;
        string? T(string? n) => n is not null && trocas.TryGetValue(n, out var novo) ? novo : n;
        l.Plano = T(l.Plano) ?? l.Plano;
        foreach (var s in l.Saidas) s.Plano = T(s.Plano);
        foreach (var p in c.Perfis) if (p.Luz is not null) p.Luz.Plano = T(p.Luz.Plano) ?? p.Luz.Plano;
        foreach (var j in c.Jogos.Lista) j.Luz = T(j.Luz);
    }

    static void PreservarPlanosLuz(Config c)
    {
        var l = c.Luz;
        var usados = l.Planos.Select(p => p.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trocas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in l.Planos.Where(p => !p.Pronto && PlanosLuzNovos.Contains(p.Nome)).ToList())
        {
            var antigo = p.Nome;
            var baseNome = $"{antigo} personalizado";
            var novo = baseNome;
            for (int numero = 2; usados.Contains(novo); numero++) novo = $"{baseNome} {numero}";
            usados.Remove(antigo);
            usados.Add(novo);
            p.Nome = novo;
            trocas[antigo] = novo;
        }
        if (trocas.Count == 0) return;
        string? Trocar(string? nome) => nome is not null && trocas.TryGetValue(nome, out var novo) ? novo : nome;
        l.Plano = Trocar(l.Plano) ?? l.Plano;
        foreach (var s in l.Saidas) s.Plano = Trocar(s.Plano);
        foreach (var p in c.Perfis) if (p.Luz is not null) p.Luz.Plano = Trocar(p.Luz.Plano) ?? p.Luz.Plano;
        foreach (var j in c.Jogos.Lista) j.Luz = Trocar(j.Luz);
    }

    static void PerfilEconomico(Config c)
    {
        const string antigo = "Silencioso", novo = "Econômico";
        if (c.Perfis.FirstOrDefault(p => p.Nome == antigo && p.Pronto) is not { } p || c.Perfis.Any(x => x.Nome == novo)) return;
        p.Nome = novo;
        if (c.PerfilAtivo == antigo) c.PerfilAtivo = novo;
        foreach (var j in c.Jogos.Lista) if (j.Perfil == antigo) j.Perfil = novo;
    }

    // Perfis prontos de antes de 17/10/2026 perderam os limites numa partida sem o kit da AMD: voltam os de fábrica.
    static void DevolverLimitesPerdidos(Config c)
    {
        foreach (var p in c.Perfis)
        {
            if (Fabrica(p) is not { } f) continue;
            if (f.Processador?.Ppt is int ppt && p.Processador?.Ppt is null) (p.Processador ??= new()).Ppt = ppt;
            if (f.PlacaDeVideo?.LimiteEnergia is int le && p.PlacaDeVideo?.LimiteEnergia is null) (p.PlacaDeVideo ??= new()).LimiteEnergia = le;
            if (f.Graficos?.AntiLag is bool al && p.Graficos?.AntiLag is null) (p.Graficos ??= new()).AntiLag = al;
        }
    }

    // Calibração medida nesta máquina em 10/10/2026 (desenvolvimento\testes\v13_calibrar_ventoinhas.ps1), usada até a primeira calibração do SetupHub.
    public static Dictionary<string, Calibracao> CalibracaoInicial()
    {
        static List<Ponto> T(params (int p, int r)[] v) => v.Select(x => new Ponto(x.p, x.r)).ToList();
        return new()
        {
            ["cpu"] = new() { Inicio = 14, Para = 12, RpmMax = 1794, Resposta = 3.2, Tabela = T((0, 0), (12, 0), (14, 336), (16, 380), (18, 443), (20, 480), (30, 720), (40, 920), (50, 1091), (60, 1264), (70, 1418), (80, 1558), (90, 1677), (100, 1794)) },
            ["sys1"] = new() { Inicio = 10, Para = 0, RpmMax = 3022, Resposta = 36, Tabela = T((0, 0), (10, 628), (12, 676), (14, 730), (16, 780), (18, 836), (20, 886), (30, 1153), (40, 1420), (50, 1679), (60, 1956), (70, 2219), (80, 2492), (90, 2740), (100, 3022)) },
            ["sys2"] = new() { Inicio = 10, Para = 0, RpmMax = 3066, Resposta = 37, Tabela = T((0, 0), (10, 631), (12, 675), (14, 733), (16, 782), (18, 835), (20, 885), (30, 1147), (40, 1422), (50, 1686), (60, 1981), (70, 2220), (80, 2492), (90, 2797), (100, 3066)) },
            ["sys3"] = new() { Inicio = 10, Para = 0, RpmMax = 3055, Resposta = 37, Tabela = T((0, 0), (10, 637), (12, 688), (14, 740), (16, 788), (18, 838), (20, 894), (30, 1163), (40, 1448), (50, 1702), (60, 1999), (70, 2272), (80, 2550), (90, 2778), (100, 3055)) },
        };
    }

    // A do processador nunca para: mínimo é a porcentagem calibrada em que começa a girar, com folga.
    public static int MinimoCpu(Calibracao? c) => c is null || c.Inicio <= 0 ? 30 : Math.Min(60, c.Inicio + 6);
}
