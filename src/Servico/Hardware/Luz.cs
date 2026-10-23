using System.Globalization;
using SetupHub.Nucleo;
using Microsoft.Win32.SafeHandles;

namespace SetupHub.Servico.Hardware;

// Relatório 0x50: 18 áreas de 16 bytes, último byte guarda na memória do controlador; relatório 0x01 comando 0xC0: volume para o efeito Música.
sealed class ControladorLuz : IDisposable
{
    const int TamanhoComando = 64;
    SafeFileHandle? h;
    SafeFileHandle? entrada;
    int tamanho, tamanhoEntrada;
    readonly object trava = new();
    readonly HashSet<int> portasGen2Preparadas = new();
    public bool Pronto => h is not null && !h.IsInvalid;
    public string? Motivo { get; private set; }

    // A posição é o código do modo no controlador; o 6 fica sem uso e o 7 (Recreação no MSI Center) só acende com o volume enviado.
    public static readonly string?[] Modos ={ "desligado", "onda", "fixo", "chama", "respiracao", "anel", null, "musica", "meteoro" };

    public bool Abrir()
    {
        lock (trava)
        {
            if (Pronto) return true;
            var (handle, entradaHid, t, te) = Hid.Abrir("vid_0db0&pid_0076", 290);
            if (handle is null) { Motivo = "controlador de luz da placa não encontrado"; return false; }
            h = handle; entrada = entradaHid; tamanho = t; tamanhoEntrada = te; Motivo = null;
            return true;
        }
    }

    public byte[]? Ler()
    {
        lock (trava)
        {
            if (!Pronto && !Abrir()) return null;
            var b = new byte[tamanho];
            b[0] = 0x50;
            if (Hid.HidD_GetFeature(h!, b, b.Length)) return b;
            Fechar();
            return null;
        }
    }

    public bool Gravar(byte[] b, bool guardar)
    {
        lock (trava)
        {
            if (!Pronto && !Abrir()) return false;
            var c = (byte[])b.Clone();
            c[0] = 0x50;
            c[289] = (byte)(guardar ? 1 : 0);
            if (Hid.HidD_SetFeature(h!, c, c.Length)) return true;
            Fechar();
            return false;
        }
    }

    // Fitas de segunda geração (com identificador próprio) ligadas numa porta seguem o relatório 0x90 + porta, não a área comum.
    public byte[]? LerPorta(int porta)
    {
        lock (trava)
        {
            if (!Pronto && !Abrir()) return null;
            if (!Gen2Disponivel) return null;
            var b = new byte[tamanho];
            b[0] = (byte)(0x90 + porta);
            if (Hid.HidD_GetFeature(h!, b, b.Length)) return b;
            Fechar();
            return null;
        }
    }

    public bool GravarPorta(int porta, byte[] b, bool guardar)
    {
        lock (trava)
        {
            if (!Pronto && !Abrir()) return false;
            if (!Gen2Disponivel) return false;
            var c = (byte[])b.Clone();
            c[0] = (byte)(0x90 + porta);
            c[301] = (byte)(guardar ? 1 : 0);
            if (Hid.HidD_SetFeature(h!, c, c.Length)) return true;
            Fechar();
            return false;
        }
    }

    public bool PrepararPortaGen2(int porta)
    {
        lock (trava)
        {
            if (!Pronto && !Abrir()) return false;
            if (!Gen2Disponivel) return false;
            if (portasGen2Preparadas.Contains(porta)) return true;
            var b = new byte[TamanhoComando];
            b[0] = 1; b[1] = 0x84; b[6] = (byte)porta; b[7] = 1;
            if (entrada is null || entrada.IsInvalid || !Hid.Escrever(h!, b)) { Fechar(); return false; }
            Thread.Sleep(1);
            var resposta = new byte[tamanhoEntrada];
            if (!Hid.LerEntrada(entrada, resposta, 50) || resposta[0] != 1 || resposta[1] != 0x5A || resposta[6] != porta) { Fechar(); return false; }
            Thread.Sleep(10);
            portasGen2Preparadas.Add(porta);
            return true;
        }
    }

    public static int Fitas(byte[] porta)
    {
        int n = 0;
        while (n < 6 && !(porta[n * 20 + 1] == 0xFF && porta[n * 20 + 2] == 0xFF && porta[n * 20 + 3] == 0xFF && porta[n * 20 + 4] == 0xFF)) n++;
        return n;
    }

    public bool Volume(int geral, int esquerda, int direita)
    {
        lock (trava)
        {
            if (!Pronto && !Abrir()) return false;
            var b = new byte[TamanhoComando];
            b[0] = 1; b[1] = 0xC0; b[3] = (byte)geral; b[4] = (byte)esquerda; b[5] = (byte)direita;
            if (Hid.Escrever(h!, b)) return true;
            Fechar();
            return false;
        }
    }

    public bool Gen2Disponivel => tamanho >= 302 && tamanhoEntrada >= 7 && entrada is not null && !entrada.IsInvalid;

    void Fechar() { try { h?.Dispose(); entrada?.Dispose(); } catch { } h = null; entrada = null; tamanho = 0; tamanhoEntrada = 0; portasGen2Preparadas.Clear(); }
    public void Dispose() { lock (trava) Fechar(); }

    // Mesma montagem do LEDKeeper2 do MSI Center para as placas da série 800.
    public static void Area(byte[] b, int area, EfeitoLuz f)
    {
        int o = area * 16;
        Efeito(f).CopyTo(b, o + 1);
        b[o + 16] = (byte)(area < 4 ? 30 : 255);
    }

    // Modo, 4 cores, quantidade de cores e opções: os mesmos 15 bytes na área comum e em cada fita de segunda geração.
    public static byte[] Efeito(EfeitoLuz f)
    {
        var b = new byte[15];
        int modo = Array.IndexOf(Modos, f.Tipo);
        b[0] = (byte)(modo < 0 ? 2 : modo);
        var cores = f.Cores.Count == 0 ? new List<string> { "#000000" } : f.Cores;
        for (int i = 0; i < 4; i++)
        {
            var (r, g, bl) = CoresLuz.Rgb(cores[Math.Min(i, cores.Count - 1)]);
            b[1 + i * 3] = (byte)r; b[2 + i * 3] = (byte)g; b[3 + i * 3] = (byte)bl;
        }
        int quantas = f.Tipo switch { "onda" or "anel" or "meteoro" => Math.Clamp(cores.Count - 1, 0, 3), "chama" => 1, _ => 0 };
        int brilho = f.Tipo == "desligado" ? 0 : Nivel(f.Brilho);
        b[13] = (byte)quantas;
        b[14] = (byte)(((f.Direcao == "dentro" ? 1 : 0) << 6) | ((f.ArcoIris ? 0 : 1) << 5) | (brilho << 2) | Math.Clamp(f.Velocidade, 0, 2));
        return b;
    }

    // O controlador tem 5 níveis de brilho; zero apaga.
    public static int Nivel(int brilho) => brilho <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(brilho / 20.0), 1, 5);
}

sealed class Luz
{
    readonly ControladorLuz ctl = new();
    readonly object trava = new();
    Thread? laco;
    volatile bool rodando;
    ConfigLuz cfg = new();
    byte[]? ultimoGravado;
    readonly Dictionary<string, EfeitoLuz> efeitoAtual = new();
    Func<Dictionary<string, double>> leituras = () => new();
    public Func<double> NivelSom = () => 0;
    public Func<bool> AlertaAtivo = () => false;
    public Func<bool> JogoAberto = () => false;
    public Func<string?> CorJogo = () => null;
    readonly HashSet<int> semRetorno = new();
    bool volumeRecusado;
    public volatile bool TelaApagada;
    public string? Falha { get; private set; }
    public bool Disponivel => ctl.Pronto;

    // O SetupHub recalcula a cor e grava como Fixo, como o MSI Center faz com o efeito de temperatura.
    public static readonly HashSet<string> Calculados = new() { "temperatura", "carga", "jogo", "horario", "alerta" };

    static EfeitoLuz Apagado => new() { Tipo = "desligado", Cores = new() { "#000000" }, Brilho = 0 };

    public void Iniciar(Func<Dictionary<string, double>> fonteLeituras)
    {
        leituras = fonteLeituras;
        if (!ctl.Abrir()) { Falha = ctl.Motivo; Registro.Aviso("Luz: " + ctl.Motivo); return; }
        rodando = true;
        laco = new Thread(Laco) { IsBackground = true, Name = "luz", Priority = ThreadPriority.BelowNormal };
        laco.Start();
    }

    public static Dictionary<string, EfeitoLuz> Resolver(ConfigLuz c)
    {
        var r = new Dictionary<string, EfeitoLuz>();
        foreach (var s in c.Saidas)
        {
            EfeitoLuz f;
            var nome = s.Plano ?? c.Plano;
            var plano = c.Planos.FirstOrDefault(p => p.Nome == nome);
            if (!c.Ligada || !s.Ativa) f = Apagado;
            else if (plano is not null) f = plano.PorSaida.TryGetValue(s.Id, out var ps) ? ps : plano.Efeito;
            else f = s.Efeito;
            r[s.Id] = f;
        }
        return r;
    }

    public List<string> Aplicar(ConfigLuz c)
    {
        lock (trava)
        {
            cfg = c;
            ultimoGravado = null;
            efeitoAtual.Clear();
            foreach (var (k, v) in Resolver(c)) efeitoAtual[k] = v;
            return Gravar(false);
        }
    }

    bool Alerta(EfeitoLuz f) => (cfg.AlertaPisca || f.Tipo == "alerta") && AlertaAtivo();

    EfeitoLuz Final(EfeitoLuz f, Dictionary<string, double> l)
    {
        if (TelaApagada && cfg.ApagarComTela) return Apagado;
        if (Alerta(f)) return new EfeitoLuz { Tipo = "respiracao", Cores = new() { cfg.CorAlerta }, Brilho = 100, Velocidade = 2 };
        var n = Nativo(f, l, JogoAberto(), CorJogo());
        return NoiteQuente() && n.Tipo != "desligado" ? Aquecer(n) : n;
    }

    public static EfeitoLuz Nativo(EfeitoLuz f, Dictionary<string, double> l, bool jogo = false, string? corJogo = null)
    {
        if (!Calculados.Contains(f.Tipo)) return f;
        var cores = f.Cores.Count == 0 ? new List<string> { "#FFFFFF" } : f.Cores;
        string cor = f.Tipo switch
        {
            "temperatura" => CoresLuz.Temperatura(f, Temperatura(f.Fonte, l)),
            "carga" => CoresLuz.Hex(CoresLuz.Gradiente(cores, (l.TryGetValue(f.Fonte == "gpu" ? "gpu.uso" : "cpu.uso", out var u) ? u : 0) / 100)),
            "jogo" => jogo ? corJogo ?? cores[Math.Min(1, cores.Count - 1)] : cores[0],
            "horario" => CoresLuz.Hex(CoresLuz.Gradiente(cores, DateTime.Now.TimeOfDay.TotalHours / 24)),
            _ => cores[0],
        };
        return new EfeitoLuz { Tipo = "fixo", Cores = new() { cor }, Brilho = f.Brilho };
    }

    static double? Temperatura(string fonte, Dictionary<string, double> l)
    {
        double? Le(string k) => l.TryGetValue(k, out var v) ? v : null;
        return fonte switch
        {
            "gpu" => Le("gpu.quente") ?? Le("gpu.temp"),
            "maior" => Math.Max(Le("cpu.temp") ?? 0, Le("gpu.quente") ?? Le("gpu.temp") ?? 0) is var m && m > 0 ? m : null,
            _ => Le("cpu.temp"),
        };
    }

    // Lê o controlador e regrava só quando o que deveria estar nele mudou ou outro programa mexeu.
    List<string> Gravar(bool guardar, Dictionary<string, EfeitoLuz>? fixos = null)
    {
        var falhas = new List<string>();
        if (!ctl.Pronto && !ctl.Abrir()) { falhas.Add("controlador"); Falha = ctl.Motivo; return falhas; }
        var lido = ctl.Ler();
        if (lido is null) { falhas.Add("leitura"); Falha = "o controlador de luz não respondeu à leitura"; return falhas; }
        var b = (byte[])lido.Clone();
        var l = leituras();
        var finais = new Dictionary<string, EfeitoLuz>();
        foreach (var s in cfg.Saidas)
        {
            var p = Padroes.SaidasLuz.FirstOrDefault(x => x.Id == s.Id);
            if (p.Id is null || !efeitoAtual.TryGetValue(s.Id, out var f)) continue;
            finais[s.Id] = fixos?.GetValueOrDefault(s.Id) ?? Final(f, l);
            ControladorLuz.Area(b, p.Area, finais[s.Id]);
        }
        LimparSelecaoGeral(b);
        falhas.AddRange(GravarFitas(finais, guardar));
        bool precisaGravar = guardar || ultimoGravado is null || !Iguais(b, ultimoGravado) || !Iguais(lido, b);
        if (precisaGravar && !ctl.Gravar(b, guardar))
        {
            falhas.Add("gravação");
            Falha = "o controlador de luz recusou a gravação";
        }
        else if (precisaGravar)
        {
            // Logo depois da gravação o controlador às vezes ainda devolve o estado de antes: até 3 leituras, 40 ms entre elas.
            byte[]? conf = null;
            for (int tentativa = 0; tentativa < 3; tentativa++)
            {
                if (tentativa > 0) Thread.Sleep(40);
                conf = ctl.Ler();
                if (conf is not null && Iguais(conf, b)) break;
            }
            // Área que volta zerada depois de gravada não devolve leitura: fica fora da conferência para não regravar sem parar.
            if (conf is not null)
            {
                foreach (var a in Padroes.SaidasLuz.Select(x => x.Area).Distinct())
                    if (Enumerable.Range(a * 16 + 1, 16).All(i => conf[i] == 0) && Enumerable.Range(a * 16 + 1, 16).Any(i => b[i] != 0) && semRetorno.Add(a))
                        Registro.Info($"Luz: a área {a} não devolve na leitura o que foi gravado; fica fora da conferência.");
            }
            ultimoGravado = (byte[])b.Clone();
            if (conf is null || !Iguais(conf, b))
            {
                falhas.Add("conferência");
                Falha = "a luz gravada não conferiu na leitura (outro programa pode estar mexendo na luz)";
                if (conf is not null)
                    Registro.Aviso("Luz: posições diferentes na conferência " + string.Join(" ", Enumerable.Range(1, 288).Where(i => conf[i] != b[i] && Conferido(i)).Take(12).Select(i => $"{i}:{b[i]:X2}>{conf[i]:X2}")));
            }
            else Falha = null;
        }
        return falhas;
    }

    readonly HashSet<int> portasSemFita = new(), fitasAvisadas = new();
    int voltasFitas;

    // Porta sem fita de segunda geração só é relida a cada 30 voltas.
    List<string> GravarFitas(Dictionary<string, EfeitoLuz> finais, bool guardar)
    {
        var falhas = new List<string>();
        if (!ctl.Gen2Disponivel) return falhas;
        var gravadas = new List<(int porta, string id, int n, byte[] esperado)>();
        bool revisar = voltasFitas++ % 30 == 0 || guardar;
        for (int porta = 0; porta < 4; porta++)
        {
            var id = porta < 3 ? $"jargb{porta + 1}" : "ezconn";
            if (!finais.TryGetValue(id, out var ef) || (portasSemFita.Contains(porta) && !revisar)) continue;
            if (!ctl.PrepararPortaGen2(porta)) { falhas.Add($"ativação da fita de {id}"); continue; }
            var lido = ctl.LerPorta(porta);
            if (lido is null) continue;
            int n = ControladorLuz.Fitas(lido);
            if (n == 0) { portasSemFita.Add(porta); continue; }
            portasSemFita.Remove(porta);
            var b = (byte[])lido.Clone();
            var efeito = ControladorLuz.Efeito(ef);
            for (int j = 0; j < n; j++) efeito.CopyTo(b, j * 20 + 5);
            if (!guardar && Enumerable.Range(1, n * 20).All(i => b[i] == lido[i])) continue;
            if (!ctl.GravarPorta(porta, b, guardar)) { falhas.Add($"fita de {id}"); continue; }
            gravadas.Add((porta, id, n, b));
        }
        foreach (var (porta, id, n, esperado) in gravadas)
        {
            byte[]? conf = null;
            for (int tentativa = 0; tentativa < 3; tentativa++)
            {
                if (tentativa > 0) Thread.Sleep(40);
                conf = ctl.LerPorta(porta);
                if (conf is not null && Enumerable.Range(1, n * 20).All(i => conf[i] == esperado[i])) break;
            }
            if (conf is null || !Enumerable.Range(1, n * 20).All(i => conf[i] == esperado[i])) falhas.Add($"conferência da fita de {id}");
            else if (fitasAvisadas.Add(porta)) Registro.Info($"Luz: {n} fita(s) de segunda geração em {id}; recebem o efeito pelo relatório próprio delas.");
        }
        return falhas;
    }

    static void LimparSelecaoGeral(byte[] b) => b[17 * 16 + 15] &= 0x7F;

    // O último byte de cada área (tamanho do ciclo) volta zerado nas saídas que não são endereçáveis.
    bool Conferido(int i) => i % 16 != 0 && !semRetorno.Contains((i - 1) / 16);
    bool Iguais(byte[] a, byte[] b) => Enumerable.Range(1, 288).All(i => a[i] == b[i] || !Conferido(i));

    bool NoiteQuente() => cfg.NoiteQuente && Horario.Dentro(cfg.NoiteInicio, cfg.NoiteFim, DateTime.Now);

    static EfeitoLuz Aquecer(EfeitoLuz f)
    {
        var c = Json.Copia(f);
        c.Cores = f.Cores.Select(x => { var (r, g, b) = CoresLuz.Rgb(x); return CoresLuz.Hex((r, g * 0.72, b * 0.38)); }).ToList();
        return c;
    }

    void Laco()
    {
        int ciclo = 0;
        while (rodando)
        {
            bool musica = false;
            int volta;
            try
            {
                lock (trava)
                {
                    musica = efeitoAtual.Values.Any(f => f.Tipo == "musica") && !(TelaApagada && cfg.ApagarComTela);
                    volta = ++ciclo;
                    if (volta % 10 == 0) Gravar(false);
                }
                if (musica && volta % 2 == 0)
                {
                    int v = (int)Math.Round(Math.Clamp(NivelSom(), 0, 1) * 100);
                    if (!ctl.Volume(v, v, v) && !volumeRecusado) { volumeRecusado = true; Registro.Aviso("Luz: o controlador recusou o volume do efeito Música."); }
                }
            }
            catch (Exception e) { Registro.Erro("Efeito de luz", e); Thread.Sleep(1000); }
            Thread.Sleep(100);
        }
    }

    // Sem o SetupHub nada recalcula a cor nem manda o volume: o efeito fica parado na cor de agora (Música vira Respiração).
    public bool Encerrar()
    {
        rodando = false;
        laco?.Join(1500);
        lock (trava)
        {
            var l = leituras();
            var fixos = efeitoAtual.ToDictionary(kv => kv.Key, kv =>
            {
                var f = kv.Value.Tipo == "musica" ? new EfeitoLuz { Tipo = "respiracao", Cores = kv.Value.Cores.ToList(), Brilho = kv.Value.Brilho, Velocidade = kv.Value.Velocidade, ArcoIris = kv.Value.ArcoIris } : kv.Value;
                return Nativo(f, l);
            });
            var falhas = Gravar(true, fixos);
            ctl.Dispose();
            if (falhas.Count == 0) Registro.Info("Luz trocada por efeitos do próprio controlador e guardada.");
            return falhas.Count == 0;
        }
    }
}

static class Horario
{
    public static bool Dentro(string inicio, string fim, DateTime agora)
    {
        if (!TimeSpan.TryParseExact(inicio, @"hh\:mm", CultureInfo.InvariantCulture, out var a) || !TimeSpan.TryParseExact(fim, @"hh\:mm", CultureInfo.InvariantCulture, out var b)) return false;
        var t = agora.TimeOfDay;
        return a <= b ? t >= a && t < b : t >= a || t < b;
    }
}
