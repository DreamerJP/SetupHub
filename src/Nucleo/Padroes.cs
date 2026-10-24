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

    // A bomba fica na BIOS: a curva de ventoinha baixaria demais a rotação dela.
    static readonly string[] SaidasDoNivel = Ventoinhas.Where(v => v.Id != "bomba").Select(v => v.Id).Append("gpu").ToArray();

    // A ordem é a das colunas do arquivo de histórico.
    public static readonly string[] SensoresHistorico =
    {
        "cpu.temp", "cpu.consumo", "cpu.clock", "cpu.uso", "gpu.temp", "gpu.quente", "gpu.memoria", "gpu.consumo",
        "gpu.clock", "gpu.uso", "placa.sistema", "placa.vrm", "placa.chipset", "ssd.temp", "memoria.uso", "fps",
        "fan.cpu", "fan.bomba", "fan.sys1", "fan.sys2", "fan.sys3", "fan.sys4", "fan.ez", "fan.gpu",
        "pct.cpu", "pct.sys1", "pct.sys2", "pct.sys3", "pct.gpu", "cpu.tensao", "gpu.vram", "gpu.tensao",
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
        "economia" => new() { Id = id, ProcessadorMinimo = 5, ProcessadorMaximo = 30, NucleosMinimos = 10, PreferenciaEnergia = 100, Impulso = "desligado", SuspensaoUsb = false, EconomiaPcie = "maximo", DesligarTela = 0, Suspender = 0 },
        "desempenho" => new() { Id = id, ProcessadorMinimo = 100, ProcessadorMaximo = 100, NucleosMinimos = 100, PreferenciaEnergia = 10, Impulso = "agressivo", SuspensaoUsb = false, EconomiaPcie = "desligado", DesligarTela = 0, Suspender = 0 },
        _ => new() { Id = "equilibrado", ProcessadorMinimo = 5, ProcessadorMaximo = 100, NucleosMinimos = 100, PreferenciaEnergia = 80, Impulso = "agressivo", SuspensaoUsb = false, EconomiaPcie = "moderado", DesligarTela = 0, Suspender = 0 },
    };

    public const double CriticaCpu = 90;
    public const double CriticaGpu = 90;
    public const double AlertaCpu = 90;
    public const double AlertaGpu = 100;

    // Temperatura em que a leitura fica vermelha: a do alerta configurado para ela.
    public static double Alerta(Config? c, string sensor, double padrao) =>
        c?.Alertas.FirstOrDefault(a => a.Tipo == "temperatura" && a.Sensor == sensor)?.Acima ?? padrao;

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

    // Cores e faixas do efeito de temperatura do MSI Center.
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

    public static Config Config()
    {
        var c = new Config();
        foreach (var (id, nome, _) in Ventoinhas)
            c.Ventoinhas.Saidas.Add(new SaidaVentoinha { Id = id, Nome = nome, Modo = "bios" });
        AplicarNivel(c.Ventoinhas, "equilibrado", SaidasDoNivel);
        c.Ventoinhas.Nivel = "equilibrado";
        c.Ventoinhas.Grupos.Add(new Grupo { Nome = "Gabinete", Tipo = "ventoinhas", Itens = new() { "sys1", "sys2", "sys3" } });
        foreach (var (id, nome, _, leds) in SaidasLuz)
            c.Luz.Saidas.Add(new SaidaLuz { Id = id, Nome = nome, Leds = leds, Ativa = true });
        c.Luz.Planos = PlanosProntos();
        c.Luz.Plano = "Temperatura";
        c.Alertas.Add(new Alerta { Tipo = "temperatura", Sensor = "cpu.temp", Acima = AlertaCpu });
        c.Alertas.Add(new Alerta { Tipo = "temperatura", Sensor = "gpu.quente", Acima = AlertaGpu });
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
        // ppt: nulo deixa o padrão de fábrica; PptMaximo e PptEco viram watts quando a faixa do kit da AMD é lida (AjustarAoHardware).
        Perfil Montar(string nome, string nivel, string luz, string? energia, int? gpuEnergia, int? ppt, bool antiLag, string atalho)
        {
            var v = Json.Copia(baseConfig.Ventoinhas);
            AplicarNivel(v, nivel, SaidasDoNivel);
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
        lista.Add(Montar("Econômico", "silencioso", "Respiração", "economia", null, PptEco, false, "Ctrl+Alt+1"));
        lista.Add(Montar("Equilibrado", "equilibrado", "Temperatura", "equilibrado", null, null, false, "Ctrl+Alt+2"));
        lista.Add(Montar("Desempenho", "forte", "Foco", "desempenho", 10, PptMaximo, true, "Ctrl+Alt+3"));
        return lista;
    }

    // Só o perfil pronto que mantém o nome de fábrica tem para onde voltar.
    public static Perfil? Fabrica(Perfil p) => p.Pronto ? PerfisProntos(Config()).FirstOrDefault(x => x.Nome == p.Nome) : null;

    // Só ajusta campo lido e aceito: travado ou não lido fica como está, para não se perder numa partida em que o kit da AMD falhou.
    public static void AjustarAoHardware(Config c, Faixas f)
    {
        foreach (var p in c.Perfis)
        {
            if (p.Processador is { } pr) pr.Ppt = Ppt(f, pr.Ppt);
            if (p.PlacaDeVideo is { } g) g.LimiteEnergia = Dentro(f, "placaDeVideo.limiteEnergia", g.LimiteEnergia);
        }
    }

    public static int? Dentro(Faixas f, string chave, int? v)
    {
        if (v is null || !f.Campos.TryGetValue(chave, out var x) || !x.Disponivel) return v;
        return (int)Math.Clamp(v.Value, x.Min, x.Max);
    }

    // Marcas do perfil pronto que só viram watts com a faixa do processador lida.
    public const int PptMaximo = -1, PptEco = -2;

    public static int? Ppt(Faixas f, int? v)
    {
        if (v is null || !f.Campos.TryGetValue("processador.ppt", out var x) || !x.Disponivel) return v;
        return v switch { PptMaximo => (int)x.Max, PptEco => Eco(x), _ => (int)Math.Clamp(v.Value, x.Min, x.Max) };
    }

    // Modo Eco da AMD no AM5: 65 W de TDP, 88 W de PPT; quem já sai de fábrica nesse limite desce para 45 W, 61 W de PPT.
    public static int Eco(Faixa ppt) => (int)Math.Clamp(ppt.Padrao is double p && p <= 88 ? 61 : 88, ppt.Min, ppt.Max);

    // eco, padrao, maximo ou nulo para um limite escolhido à mão.
    public static string? ModoPpt(int? ppt, Faixa? f)
    {
        if (ppt is null) return "padrao";
        if (ppt == PptMaximo) return "maximo";
        if (ppt == PptEco) return "eco";
        if (f is not { Disponivel: true }) return null;
        return ppt == (int)f.Max ? "maximo" : ppt == Eco(f) ? "eco" : null;
    }

    // A do processador nunca para: mínimo é a porcentagem calibrada em que começa a girar, com folga.
    public static int MinimoCpu(Calibracao? c) => c is null || c.Inicio <= 0 ? 30 : Math.Min(60, c.Inicio + 6);

    // Curvas e valor fixo da ventoinha do processador sobem até o mínimo dela, em todo lugar que guarda ventoinhas.
    public static void AjustarMinimoCpu(Config c, int minimo)
    {
        void Subir(IEnumerable<SaidaVentoinha> saidas)
        {
            foreach (var s in saidas.Where(s => s.Id == "cpu"))
            {
                if (s.Fixo < minimo) s.Fixo = minimo;
                foreach (var p in s.Curva) if (p.P < minimo) p.P = minimo;
            }
        }
        Subir(c.Ventoinhas.Saidas);
        foreach (var n in c.NiveisVentoinhas.Values) Subir(n.Saidas);
        foreach (var p in c.Perfis) if (p.Ventoinhas is { } v) Subir(v.Saidas);
    }
}
