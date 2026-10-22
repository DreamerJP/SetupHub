using SetupHub.Nucleo;

namespace SetupHub.Servico;

// ADLX, RivaTuner, som e atalhos só funcionam na sessão do usuário: o serviço pede pela janela, registrada como agente.
sealed partial class Motor
{
    Conexao? agente;
    readonly List<Conexao> assinantes = new();
    LeituraGpu? gpuAgente;
    long gpuAgenteEm;
    double? fpsAgente;
    volatile float somAgenteF;
    double somAgente => somAgenteF;

    async Task AgenteEntrou(Conexao c)
    {
        agente = c;
        Registro.Info("Janela registrada como agente da sessão.");
        var r = await c.PedirAsync("agente.faixas", null, 30000);
        if (r.Ok && Json.Ler<Faixas>(r.Dados) is { } fg)
        {
            foreach (var (k, v) in fg.Campos) faixas.Campos[k] = v;
            foreach (var a in fg.AreasLidas) if (!faixas.AreasLidas.Contains(a)) faixas.AreasLidas.Add(a);
            faixas.Telas = fg.Telas; faixas.Gpu = fg.Gpu; faixas.AdlxDisponivel = fg.AdlxDisponivel; faixas.RivaTuner = fg.RivaTuner;
            SalvarFaixas();
            lock (trava)
            {
                var antes = Json.Texto(config);
                Padroes.AjustarAoHardware(config, faixas);
                if (Json.Texto(config) != antes) GravarConfig(config);
            }
        }
        Config cfg;
        lock (trava) cfg = config;
        var p = new PedidoAgente
        {
            PlacaDeVideo = cfg.PlacaDeVideo, Ventoinha = VentoinhasEfetivas(cfg).Gpu, Graficos = GraficosEfetivos(cfg), Telas = cfg.Telas,
            Sobreposicao = cfg.Jogos.Sobreposicao, LimiteQuadros = LimiteEfetivo(cfg), Windows = cfg.Windows,
        };
        var falhas = await PedirAgente(p);
        if (falhas.Count > 0) Avisar("Placa de vídeo e telas, ao abrir a janela: " + string.Join(", ", falhas));
        _ = Evento("faixas", null);
    }

    void AgenteSaiu()
    {
        agente = null;
        gpuAgente = null;
        fpsAgente = null;
        Registro.Info("Janela do SetupHub fechou: placa de vídeo e telas ficam como estão.");
    }

    // A leitura do ADLX tem prioridade; sem a janela vale a biblioteca de sensores.
    void MesclarGpu(Estado e)
    {
        var g = gpuAgente;
        if (g is null || Environment.TickCount64 - gpuAgenteEm > 3000) return;
        var d = e.Gpu;
        d.Nome = g.Nome.Length > 0 ? g.Nome : d.Nome;
        d.Temp = g.Temp ?? d.Temp; d.TempQuente = g.TempQuente ?? d.TempQuente; d.TempMemoria = g.TempMemoria ?? d.TempMemoria;
        d.Consumo = g.Consumo ?? d.Consumo; d.ConsumoTotal = g.ConsumoTotal ?? d.ConsumoTotal; d.Tensao = g.Tensao ?? d.Tensao;
        d.Clock = g.Clock ?? d.Clock; d.ClockMemoria = g.ClockMemoria ?? d.ClockMemoria; d.Uso = g.Uso ?? d.Uso;
        d.VramUsada = g.VramUsada ?? d.VramUsada; d.VramTotal = g.VramTotal ?? d.VramTotal;
        d.FanRpm = g.FanRpm ?? d.FanRpm; d.FanPorcentagem = g.FanPorcentagem ?? d.FanPorcentagem; d.TempEntrada = g.TempEntrada ?? d.TempEntrada;
    }

    void Transmitir(Estado e)
    {
        List<Conexao> lista;
        lock (assinantes) { if (assinantes.Count == 0) return; lista = assinantes.ToList(); }
        var dados = Json.Elemento(e);
        foreach (var c in lista) _ = EnviarSeguro(c, new Mensagem { Tipo = "evento", Comando = "estado", Dados = dados });
    }

    Task Evento(string tipo, System.Text.Json.JsonElement? dados)
    {
        List<Conexao> lista;
        lock (assinantes) lista = assinantes.ToList();
        return Task.WhenAll(lista.Select(c => EnviarSeguro(c, new Mensagem { Tipo = "evento", Comando = tipo, Dados = dados })));
    }

    static async Task EnviarSeguro(Conexao c, Mensagem m)
    {
        try { await c.EnviarAsync(m).ConfigureAwait(false); } catch { }
    }
}
