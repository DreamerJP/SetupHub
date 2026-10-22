using SetupHub.Nucleo;

namespace SetupHub.Servico.Hardware;

// Grava no chip só quando muda; ao soltar uma saída devolve exatamente o que a BIOS deixou.
sealed class Ventoinhas
{
    readonly ChipVentoinhas chip;
    readonly Func<EstadoMaquina> maquina;
    readonly Action salvarMaquina;
    readonly Dictionary<string, Controle> ctl = new();
    readonly HashSet<string> assumidas = new();
    byte modoOriginal;
    Dictionary<string, byte> manualOriginal = new();
    Dictionary<string, byte[]> curvasOriginais = new();
    bool originaisLidos;
    readonly long inicio = Environment.TickCount64;
    public volatile bool Calibrando;
    public HashSet<string> EmCalibracao { get; } = new();
    public string? Falha { get; private set; }

    sealed class Controle
    {
        public double? TempEfetiva;
        public double Suave = -1;
        public int Gravado = -1;
        public bool Parada;
        public long ZeroDesde;
        public bool Defeito;
        public bool Vista;
    }

    public Ventoinhas(ChipVentoinhas chip, Func<EstadoMaquina> maquina, Action salvarMaquina)
    {
        this.chip = chip; this.maquina = maquina; this.salvarMaquina = salvarMaquina;
        foreach (var s in ChipVentoinhas.Saidas) ctl[s.Id] = new Controle();
    }

    public bool Disponivel => chip.Pronto;
    public string? Motivo => chip.Motivo ?? Falha;

    // Se o SetupHub caiu sem devolver e o PC não reiniciou, o chip tem os valores dele: valem os originais guardados em disco.
    void LerOriginais()
    {
        if (originaisLidos || !chip.Pronto) return;
        var m = maquina();
        var (modo, manual, curvas) = chip.LerOriginais();
        bool aindaNossos = m.Assumido && m.CurvasGravadas.Count > 0 && m.CurvasGravadas.All(kv => curvas.TryGetValue(kv.Key, out var b) && Hex(b) == kv.Value);
        if (aindaNossos && m.CurvasBios.Count > 0 && m.ModoBios is int mb)
        {
            modoOriginal = (byte)mb;
            manualOriginal = m.ManualBios.ToDictionary(kv => kv.Key, kv => (byte)kv.Value);
            curvasOriginais = m.CurvasBios.ToDictionary(kv => kv.Key, kv => DeHex(kv.Value));
            Registro.Aviso("O chip ainda tinha valores gravados pelo SetupHub (queda sem reinício); usando as curvas da BIOS guardadas em disco.");
        }
        else
        {
            // Bits de modo manual ligados sem o SetupHub ter assumido seriam de outro programa; a BIOS deixa desligado.
            modoOriginal = m.Assumido && m.ModoBios is int mb2 ? (byte)mb2 : modo;
            manualOriginal = manual;
            curvasOriginais = curvas;
            m.ModoBios = modoOriginal;
            m.ManualBios = manualOriginal.ToDictionary(kv => kv.Key, kv => (int)kv.Value);
            m.CurvasBios = curvasOriginais.ToDictionary(kv => kv.Key, kv => Hex(kv.Value));
            m.CurvasGravadas.Clear();
            m.Assumido = false;
            salvarMaquina();
        }
        originaisLidos = true;
    }

    static string Hex(byte[] b) => Convert.ToHexString(b);
    static byte[] DeHex(string s) => Convert.FromHexString(s);

    public void Ciclo(ConfigVentoinhas cfg, Estado e, Dictionary<string, double> leituras, bool noite)
    {
        e.Ventoinhas.Clear();
        var m = maquina();
        foreach (var s in ChipVentoinhas.Saidas)
        {
            var conf = cfg.Saidas.FirstOrDefault(x => x.Id == s.Id);
            var nome = conf?.Nome ?? Padroes.Ventoinhas.First(x => x.Id == s.Id).Nome;
            var c = ctl[s.Id];
            var rpm = chip.Rpm(s);
            if (rpm > 0) c.Vista = true;
            m.Calibracao.TryGetValue(s.Id, out var cal);
            e.Ventoinhas.Add(new LeituraVentoinha
            {
                Id = s.Id, Nome = nome, Rpm = rpm, Porcentagem = chip.Aplicada(s),
                Alvo = c.Gravado >= 0 && assumidas.Contains(s.Id) ? c.Gravado : null,
                Modo = EmCalibracao.Contains(s.Id) ? "calibrando" : conf?.Modo ?? "bios",
                Conectada = c.Vista || cal is not null,
                Defeito = c.Defeito,
                RpmMax = cal?.RpmMax,
            });
        }
        if (!chip.Pronto) return;
        // Os primeiros segundos servem para os sensores estabilizarem; até lá a BIOS continua mandando.
        if (Environment.TickCount64 - inicio < 3000) return;
        LerOriginais();
        if (Calibrando) return;

        var pedidos = new Dictionary<string, int>();
        var soltar = new List<string>();
        foreach (var s in ChipVentoinhas.Saidas)
        {
            var conf = cfg.Saidas.FirstOrDefault(x => x.Id == s.Id);
            var c = ctl[s.Id];
            if (conf is null || conf.Modo == "bios")
            {
                if (assumidas.Contains(s.Id)) soltar.Add(s.Id);
                continue;
            }
            m.Calibracao.TryGetValue(s.Id, out var cal);
            int alvo = Calcular(s, conf, c, cal, leituras, noite);
            if (alvo != c.Gravado || !assumidas.Contains(s.Id)) pedidos[s.Id] = alvo;
            Defeito(s, c, cal, e);
        }
        if (soltar.Count > 0) Soltar(soltar);
        if (pedidos.Count > 0)
        {
            if (!m.Assumido) { m.Assumido = true; salvarMaquina(); }
            var falhas = chip.Gravar(pedidos);
            foreach (var (id, v) in pedidos)
            {
                if (falhas.Contains(id)) continue;
                ctl[id].Gravado = v;
                assumidas.Add(id);
            }
            Falha = falhas.Count > 0 ? "O chip não aceitou a gravação de " + string.Join(", ", falhas) : null;
            RegistrarGravadas();
        }
        foreach (var l in e.Ventoinhas)
            if (assumidas.Contains(l.Id) && ctl[l.Id].Gravado >= 0) l.Alvo = ctl[l.Id].Gravado;
    }

    void RegistrarGravadas()
    {
        var m = maquina();
        m.CurvasGravadas.Clear();
        foreach (var s in ChipVentoinhas.Saidas.Where(x => !x.Manual && assumidas.Contains(x.Id)))
            m.CurvasGravadas[s.Id] = Hex(chip.LerBloco(s.Registro));
        salvarMaquina();
    }

    int Calcular(ChipVentoinhas.Saida s, SaidaVentoinha conf, Controle c, Calibracao? cal, Dictionary<string, double> leituras, bool noite)
    {
        bool cpu = s.Id == "cpu";
        int minimo = cpu ? Padroes.MinimoCpu(cal) : 0;
        if (conf.Modo == "fixo") return Math.Clamp(conf.Fixo, minimo, 100);

        // Fonte que falha leva a saída a 100%.
        double? temp = null; bool faltou = false; double soma = 0; int n = 0;
        foreach (var f in conf.Fontes)
        {
            if (leituras.TryGetValue(Fonte(f), out var t) && t > 0 && t < 150) { temp = temp is null ? t : Math.Max(temp.Value, t); soma += t; n++; }
            else faltou = true;
        }
        if (faltou || temp is null) { c.Suave = 100; return 100; }
        if (conf.Combinar == "media" && n > 0) temp = soma / n;

        // Histerese: sobe na hora, desce só depois de cair a quantidade escolhida.
        if (c.TempEfetiva is null || temp > c.TempEfetiva || temp < c.TempEfetiva - conf.Histerese) c.TempEfetiva = temp;
        double alvo = Curvas.Avaliar(conf.Curva, c.TempEfetiva.Value);

        // Volta da parada já na porcentagem calibrada em que gira, para não ficar parada pedindo menos que o necessário.
        if (conf.PararAbaixo is int pa && !cpu)
        {
            if (!c.Parada && temp < pa) c.Parada = true;
            else if (c.Parada && temp >= pa + Math.Max(1, conf.Histerese)) c.Parada = false;
            if (c.Parada) { c.Suave = 0; return 0; }
            int partida = cal is null ? 25 : cal.Inicio + 4;
            alvo = Math.Max(alvo, partida);
        }

        // Desce no ritmo escolhido e sobe na metade do tempo; a de gabinete ainda sobe 2% por segundo no chip.
        if (c.Suave < 0) c.Suave = alvo;
        double tau = alvo > c.Suave ? conf.Suavizacao / 2 : conf.Suavizacao;
        c.Suave = tau <= 0.05 ? alvo : c.Suave + (alvo - c.Suave) * (1 - Math.Exp(-1.0 / tau));
        double v = c.Suave;

        // Modo noite: limita o máximo, mas nunca perto da temperatura crítica.
        if (noite && temp < Padroes.CriticaCpu - 10) v = Math.Min(v, NoiteMax);
        // Abaixo da porcentagem calibrada em que começa a girar, a ventoinha pode parar sem ninguém ter pedido.
        if (cal is not null && v > 0.5 && v < cal.Inicio && !cpu) v = cal.Inicio;
        return (int)Math.Clamp(Math.Round(v), minimo, 100);
    }

    public int NoiteMax { get; set; } = 100;

    static string Fonte(string f) => f switch
    {
        "cpu" => "cpu.temp",
        "gpu" or "gpu.quente" => "gpu.quente",
        "gpu.memoria" => "gpu.memoria",
        "sistema" => "placa.sistema",
        "vrm" => "placa.vrm",
        "chipset" => "placa.chipset",
        "ssd" => "ssd.temp",
        _ => f,
    };

    // Defeito: comando acima da porcentagem calibrada em que gira e rotação zero por 15 s.
    void Defeito(ChipVentoinhas.Saida s, Controle c, Calibracao? cal, Estado e)
    {
        if (cal is null) { c.Defeito = false; return; }
        var aplicada = chip.Aplicada(s);
        var rpm = chip.Rpm(s);
        if (aplicada >= cal.Inicio + 8 && (rpm ?? 0) < 1)
        {
            if (c.ZeroDesde == 0) c.ZeroDesde = Environment.TickCount64;
            else if (Environment.TickCount64 - c.ZeroDesde > 15000) c.Defeito = true;
        }
        else { c.ZeroDesde = 0; c.Defeito = false; }
    }

    void Soltar(List<string> ids)
    {
        if (!originaisLidos) return;
        var falhas = chip.Devolver(ids, modoOriginal, manualOriginal, curvasOriginais);
        foreach (var id in ids.Except(falhas))
        {
            assumidas.Remove(id);
            ctl[id].Gravado = -1; ctl[id].Suave = -1; ctl[id].TempEfetiva = null; ctl[id].Parada = false;
        }
        if (falhas.Count > 0) Registro.Erro("Saídas que não voltaram à BIOS: " + string.Join(", ", falhas));
        var m = maquina();
        if (assumidas.Count == 0) { m.Assumido = false; m.CurvasGravadas.Clear(); salvarMaquina(); }
        else RegistrarGravadas();
    }

    public bool DevolverTudo()
    {
        if (!chip.Pronto || !originaisLidos) return true;
        var ids = ChipVentoinhas.Saidas.Select(s => s.Id).ToList();
        var falhas = chip.Devolver(ids, modoOriginal, manualOriginal, curvasOriginais);
        assumidas.Clear();
        foreach (var c in ctl.Values) { c.Gravado = -1; c.Suave = -1; c.TempEfetiva = null; }
        var m = maquina();
        m.Assumido = falhas.Count > 0;
        if (falhas.Count == 0) m.CurvasGravadas.Clear();
        salvarMaquina();
        if (falhas.Count > 0) Registro.Erro("Devolução à BIOS não conferiu em: " + string.Join(", ", falhas));
        else Registro.Info("Ventoinhas devolvidas à BIOS e conferidas por leitura.");
        return falhas.Count == 0;
    }

    // Depois da suspensão o chip pode ter voltado aos valores da BIOS.
    public void Retomar()
    {
        foreach (var c in ctl.Values) c.Gravado = -1;
    }

    // Cada nível só é medido depois de o chip aplicar e a rotação parar de mudar.
    public async Task<Dictionary<string, Calibracao>> Calibrar(IReadOnlyList<string> ids, Func<double?> tempCpu, Action<string, double> progresso, CancellationToken ct)
    {
        var saidas = ids.Select(ChipVentoinhas.Por).Where(s => s is not null).Cast<ChipVentoinhas.Saida>().ToList();
        if (saidas.Count == 0) throw new ErroSetupHub("Nenhuma ventoinha conhecida para calibrar.");
        if (!chip.Pronto) throw new ErroSetupHub("O chip de ventoinhas não está disponível: " + chip.Motivo);
        LerOriginais();
        Calibrando = true;
        foreach (var s in saidas) EmCalibracao.Add(s.Id);
        var resultado = new Dictionary<string, Calibracao>();
        var tabela = saidas.ToDictionary(s => s.Id, _ => new SortedDictionary<int, (int sobe, int desce)>());
        int[] sobe = { 0, 6, 8, 10, 12, 14, 16, 18, 20, 25, 30, 40, 50, 60, 70, 80, 90, 100 };
        int total = sobe.Length * 2 + 2, feito = 0;
        try
        {
            foreach (var sentido in new[] { "sobe", "desce" })
            {
                var passos = sentido == "sobe" ? sobe : sobe.Reverse().ToArray();
                foreach (var p in passos)
                {
                    ct.ThrowIfCancellationRequested();
                    if (tempCpu() > 85) throw new ErroSetupHub("Processador acima de 85 °C: calibração interrompida e ventoinhas na curva da BIOS.");
                    progresso($"{(sentido == "sobe" ? "Subindo" : "Descendo")}: {p}%", (double)feito++ / total);
                    var falhas = chip.Gravar(saidas.ToDictionary(s => s.Id, s => s.Id == "cpu" ? Math.Max(p, 0) : p));
                    if (falhas.Count > 0) throw new ErroSetupHub("O chip não aceitou a gravação de " + string.Join(", ", falhas));
                    var r = await Estabilizar(saidas, p, ct);
                    foreach (var s in saidas)
                    {
                        var t = tabela[s.Id];
                        t.TryGetValue(p, out var par);
                        t[p] = sentido == "sobe" ? (r[s.Id], par.desce) : (par.sobe, r[s.Id]);
                    }
                }
            }
            progresso("Tempo de resposta: de 30% a 100%", (double)feito++ / total);
            chip.Gravar(saidas.ToDictionary(s => s.Id, _ => 30));
            await Estabilizar(saidas, 30, ct);
            var alvoRpm = saidas.ToDictionary(s => s.Id, s => tabela[s.Id].TryGetValue(100, out var x) ? Math.Max(x.sobe, x.desce) * 0.95 : 0);
            chip.Gravar(saidas.ToDictionary(s => s.Id, _ => 100));
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            var chegou = new Dictionary<string, double>();
            while (relogio.Elapsed.TotalSeconds < 120 && chegou.Count < saidas.Count)
            {
                await Task.Delay(250, ct);
                AtualizarSensores?.Invoke();
                foreach (var s in saidas)
                    if (!chegou.ContainsKey(s.Id) && (chip.Rpm(s) ?? 0) >= alvoRpm[s.Id] && alvoRpm[s.Id] > 0) chegou[s.Id] = relogio.Elapsed.TotalSeconds;
            }
            foreach (var s in saidas)
            {
                var t = tabela[s.Id];
                var cal = new Calibracao { Data = Formato.Agora };
                cal.Tabela = t.Select(kv => new Ponto(kv.Key, Math.Round((kv.Value.sobe + kv.Value.desce) / 2.0))).ToList();
                cal.Inicio = t.Where(kv => kv.Value.sobe > 0).Select(kv => kv.Key).DefaultIfEmpty(0).Min();
                cal.Para = t.Where(kv => kv.Value.desce == 0 && kv.Key < cal.Inicio + 20).Select(kv => kv.Key).DefaultIfEmpty(0).Max();
                cal.RpmMax = t.TryGetValue(100, out var x) ? Math.Max(x.sobe, x.desce) : 0;
                cal.Resposta = chegou.TryGetValue(s.Id, out var seg) ? Math.Round(seg, 1) : 120;
                if (cal.RpmMax > 0) resultado[s.Id] = cal;
            }
            progresso("Concluída", 1);
            return resultado;
        }
        finally
        {
            var falhas = chip.Devolver(saidas.Select(s => s.Id), modoOriginal, manualOriginal, curvasOriginais);
            if (falhas.Count > 0) Registro.Erro("Depois da calibração, não voltaram à BIOS: " + string.Join(", ", falhas));
            foreach (var s in saidas) { assumidas.Remove(s.Id); ctl[s.Id].Gravado = -1; ctl[s.Id].Suave = -1; EmCalibracao.Remove(s.Id); }
            Calibrando = false;
        }
    }

    public Action? AtualizarSensores;

    async Task<Dictionary<string, int>> Estabilizar(List<ChipVentoinhas.Saida> saidas, int pedido, CancellationToken ct)
    {
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        var hist = saidas.ToDictionary(s => s.Id, _ => new List<double>());
        var pronto = new Dictionary<string, int>();
        var chegou = new HashSet<string>();
        while (relogio.Elapsed.TotalSeconds < 130 && pronto.Count < saidas.Count)
        {
            await Task.Delay(500, ct);
            AtualizarSensores?.Invoke();
            foreach (var s in saidas)
            {
                if (pronto.ContainsKey(s.Id)) continue;
                if (!chegou.Contains(s.Id))
                {
                    if (Math.Abs((chip.Aplicada(s) ?? -100) - pedido) <= 1.5) chegou.Add(s.Id);
                    else if (relogio.Elapsed.TotalSeconds < 90) continue;
                    else chegou.Add(s.Id);
                }
                var h = hist[s.Id];
                h.Add(chip.Rpm(s) ?? 0);
                if (h.Count >= 10)
                {
                    var u = h.Skip(h.Count - 10).ToList();
                    double mx = u.Max(), mn = u.Min();
                    if (mx - mn <= Math.Max(20, mx * 0.02)) pronto[s.Id] = (int)Math.Round(u.Average());
                }
            }
        }
        foreach (var s in saidas) if (!pronto.ContainsKey(s.Id)) pronto[s.Id] = (int)Math.Round(chip.Rpm(s) ?? 0);
        return pronto;
    }
}
