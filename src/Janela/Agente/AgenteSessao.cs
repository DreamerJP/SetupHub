using SetupHub.Nucleo;

namespace SetupHub.Janela.Agente;

sealed class AgenteSessao : IDisposable
{
    readonly Ligacao lig;
    readonly Adlx adlx = new();
    readonly Som som = new();
    Atalhos? atalhos;
    Timer? relogio, relogioSom, relogioAtualizacoes;
    Sobreposicao sobre = new();
    readonly SemaphoreSlim travaAtalho = new(1, 1);
    long proximaNotificacao;
    readonly IntPtr janela;
    public Action<string>? Avisar;
    bool adlxPronto;
    readonly SemaphoreSlim travaAdlx = new(1, 1);
    CancellationTokenSource? assistenteGpu;

    public AgenteSessao(Ligacao lig, IntPtr janela)
    {
        this.lig = lig;
        this.janela = janela;
    }

    public void Iniciar()
    {
        lig.Agente = Tratar;
        // O RivaTuner mede os quadros: fica aberto enquanto a janela existir.
        Rtss.Abrir();
        _ = Task.Run(() => { adlxPronto = adlx.Abrir(); });
        relogio = new Timer(_ => Ciclo(), null, 1000, 1000);
        relogioSom = new Timer(_ => CicloSom(), null, 1000, 50);
        relogioAtualizacoes = new Timer(_ => _ = AtualizacoesDoDia(), null, TimeSpan.FromMinutes(3), TimeSpan.FromHours(6));
        lig.AoConfig += c => App.Interface(() => DefinirAtalhos(c));
        lig.AoAviso += (tipo, texto) => { if (tipo == "jogo" && lig.Config?.Windows.NaoPerturbeEmJogo == true) WindowsUsuario.NaoPerturbe(texto.StartsWith("aberto:")); };
    }

    int voltas;

    void Ciclo()
    {
        try
        {
            if (voltas++ % 60 == 5) Rtss.Abrir();
            var g = new LeituraGpu();
            if (adlxPronto) adlx.Ler(g);
            var (fps, ms) = Rtss.Quadros();
            fps ??= adlxPronto ? adlx.Fps() : null;
            _ = lig.Pedir("agente.gpu", Ligacao.Args(("gpu", Json.Elemento(g)), ("fps", fps?.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("ocioso", WindowsUsuario.SegundosOcioso().ToString("0", System.Globalization.CultureInfo.InvariantCulture))), 3000);
            if (sobre.Ligada && lig.Estado is { } e && Rtss.Rodando)
            {
                var texto = Rtss.Montar(sobre, e, fps, ms);
                Rtss.Texto(texto.Length > 0 ? texto : " ");
            }
            if (voltas % 5 == 0 && Rtss.Rodando) Rtss.Mostrar(sobre.Ligada);
        }
        catch { }
    }

    // O efeito Música precisa do volume 20 vezes por segundo; só mede quando alguma saída usa o efeito.
    void CicloSom()
    {
        var c = lig.Config;
        if (c is null || !c.Luz.Ligada) return;
        if (!c.Luz.Saidas.Any(s => Telas.AcoesLuz.EfeitoDa(c, s.Id).Tipo == "musica")) return;
        var n = som.Nivel();
        _ = lig.Pedir("agente.som", Ligacao.Args(("nivel", n.ToString(System.Globalization.CultureInfo.InvariantCulture))), 1000);
    }

    // Valor real do driver, não o guardado.
    public async Task<List<AjusteTela>> LerTelas()
    {
        if (!adlxPronto) return new();
        await travaAdlx.WaitAsync();
        try { return await Task.Run(adlx.LerTelas); } finally { travaAdlx.Release(); }
    }

    void DefinirAtalhos(Config c)
    {
        atalhos ??= new Atalhos();
        var mapa = new Dictionary<string, Action>();
        foreach (var p in c.Perfis.Where(p => !string.IsNullOrEmpty(p.Atalho)))
        {
            var nome = p.Nome;
            var plano = p.Areas.Contains("energia") ? p.Energia?.Plano : null;
            mapa[p.Atalho!] = () => _ = AplicarPerfilAtalho(nome, plano);
        }
        if (c.Jogos.Sobreposicao.Atalho.Length > 0 && !mapa.ContainsKey(c.Jogos.Sobreposicao.Atalho))
            mapa[c.Jogos.Sobreposicao.Atalho] = () =>
            {
                bool ligar = !(lig.Config?.Jogos.Sobreposicao.Ligada ?? sobre.Ligada);
                Rtss.Mostrar(ligar);
                _ = lig.Pedir("definir", Ligacao.Args(("caminho", "jogos.sobreposicao.ligada"), ("valor", ligar ? "true" : "false")));
            };
        atalhos.Definir(mapa);
        if (atalhos.Recusados.Count > 0) Avisar?.Invoke("Atalhos em uso por outro programa: " + string.Join(", ", atalhos.Recusados));
    }

    async Task AplicarPerfilAtalho(string nome, string? plano)
    {
        var nomePlano = plano is null ? null : Padroes.NomePlano(plano);
        long solicitacao = nomePlano is null ? 0 : Interlocked.Increment(ref proximaNotificacao);
        if (nomePlano is not null) NotificarPlanoPendente(nomePlano, solicitacao);
        await travaAtalho.WaitAsync();
        try
        {
            var r = await lig.Pedir("perfil.aplicar", Ligacao.Args(("nome", nome), ("origem", "atalho de teclado")));
            if (nomePlano is null) return;
            if (!r.Ok) { NotificarFalhaPlano(nomePlano, solicitacao); return; }
            if (Json.Ler<ResultadoAplicar>(r.Dados)?.Areas.TryGetValue("energia", out var resultado) == true && resultado != "aplicado e conferido")
                NotificarFalhaPlano(nomePlano, solicitacao);
            else
                ConfirmarPlano(nomePlano, solicitacao);
        }
        catch
        {
            if (nomePlano is not null) NotificarFalhaPlano(nomePlano, solicitacao);
        }
        finally { travaAtalho.Release(); }
    }

    static void NotificarPlanoPendente(string plano, long solicitacao) => App.Interface(() => App.Notificacao.MostrarPlanoPendente(plano, solicitacao));
    static void ConfirmarPlano(string plano, long solicitacao) => App.Interface(() => App.Notificacao.ConfirmarPlano(plano, solicitacao));
    static void NotificarFalhaPlano(string plano, long solicitacao) => App.Interface(() => App.Notificacao.MostrarFalhaPlano(plano, solicitacao));

    async Task<Mensagem> Tratar(Mensagem m)
    {
        switch (m.Comando)
        {
            case "agente.faixas":
            {
                await EsperarAdlx();
                var f = new Faixas();
                await travaAdlx.WaitAsync();
                try { adlx.Faixas(f); } finally { travaAdlx.Release(); }
                f.RivaTuner = Rtss.Instalado;
                var telas = TelasWindows.Listar();
                for (int i = 0; i < f.Telas.Count; i++)
                {
                    var t = f.Telas[i];
                    var w = telas.FirstOrDefault(x => x.monitor.Contains(t.Nome, StringComparison.OrdinalIgnoreCase));
                    if (w.dispositivo is null && i < telas.Count) w = telas[i];
                    if (w.dispositivo is null) continue;
                    t.Dispositivo = w.dispositivo;
                    t.Taxas = w.taxas;
                    t.Hz = w.hz;
                    t.Suporta.Add("hz");
                }
                return new Mensagem { Ok = true, Dados = Json.Elemento(f) };
            }
            case "agente.aplicar":
            {
                var p = Json.Ler<PedidoAgente>(m.Args) ?? new PedidoAgente();
                var falhas = await Aplicar(p);
                return new Mensagem { Ok = true, Dados = Json.Elemento(falhas) };
            }
            case "agente.fabrica":
            {
                await EsperarAdlx();
                bool ok;
                await travaAdlx.WaitAsync();
                try { ok = adlx.VoltarFabrica(); } finally { travaAdlx.Release(); }
                return new Mensagem { Ok = ok, Erro = ok ? null : "O driver não confirmou a volta aos ajustes de fábrica." };
            }
            case "agente.limparCache":
            {
                await EsperarAdlx();
                bool ok = adlx.LimparCache();
                return new Mensagem { Ok = ok, Erro = ok ? null : "O driver não aceitou limpar o cache de sombreadores." };
            }
            case "agente.assistenteGpu":
                return await AssistenteGpu(m);
            case "agente.cancelarAssistente":
                assistenteGpu?.Cancel();
                return new Mensagem { Ok = true };
        }
        return new Mensagem { Ok = false, Erro = $"A janela não conhece o pedido \"{m.Comando}\"." };
    }

    async Task EsperarAdlx()
    {
        for (int i = 0; i < 100 && !adlxPronto && adlx.Motivo is null; i++) await Task.Delay(100);
    }

    async Task<List<string>> Aplicar(PedidoAgente p)
    {
        var f = new List<string>();
        await EsperarAdlx();
        await travaAdlx.WaitAsync();
        try
        {
            if (p.PlacaDeVideo is not null) f.AddRange(adlx.AplicarGpu(p.PlacaDeVideo));
            if (p.Ventoinha is not null) f.AddRange(adlx.AplicarVentoinha(p.Ventoinha));
            var graficos = p.Graficos is null ? null : Json.Copia(p.Graficos);
            // O limite de quadros usa o RivaTuner quando instalado e o da AMD quando não; os dois nunca ficam ligados juntos.
            if (p.LimiteQuadros is int lim)
            {
                if (Rtss.Instalado)
                {
                    f.AddRange(Rtss.Perfil(new() { ["FramerateLimit"] = Math.Max(0, lim) }));
                    graficos ??= new Ajustes3D();
                    graficos.Frtc = false;
                }
                else
                {
                    graficos ??= new Ajustes3D();
                    graficos.Frtc = lim > 0;
                    if (lim > 0) graficos.FrtcFps = lim;
                }
            }
            if (graficos is not null) f.AddRange(adlx.Aplicar3D(graficos));
            if (p.Telas is not null)
            {
                f.AddRange(adlx.AplicarTelas(p.Telas));
                var telas = TelasWindows.Listar();
                foreach (var t in p.Telas.Lista.Where(t => t.Hz is not null))
                {
                    var faixa = lig.Faixas?.Telas.FirstOrDefault(x => x.Id == t.Id);
                    var disp = faixa?.Dispositivo ?? telas.FirstOrDefault().dispositivo;
                    if (disp is null) continue;
                    if (TelasWindows.Taxa(disp, t.Hz!.Value) is string erro) f.Add($"{t.Nome}: {erro}");
                }
            }
        }
        finally { travaAdlx.Release(); }
        if (p.Sobreposicao is not null)
        {
            sobre = p.Sobreposicao;
            if (!sobre.Ligada) Rtss.Texto("");
            else if (Rtss.Instalado)
            {
                int x = sobre.Posicao.EndsWith("direita") ? -1 : 1, y = sobre.Posicao.StartsWith("inferior") ? -1 : 1;
                var falhasRtss = Rtss.Perfil(new()
                {
                    ["PositionX"] = x, ["PositionY"] = y, ["ZoomRatio"] = Math.Clamp((int)Math.Round(sobre.Tamanho / 50.0), 1, 6),
                    ["BaseColor"] = Rtss.Cor(sobre.Cor), ["EnableBgnd"] = sobre.Fundo ? 1 : 0,
                }, sobre.Fonte);
                if (falhasRtss.Count > 0) f.Add("posição da sobreposição: " + string.Join(", ", falhasRtss));
                if (!Rtss.Rodando) Rtss.Abrir();
            }
            else f.Add("sobreposição indisponível: RivaTuner não instalado");
            App.Interface(() => { if (lig.Config is { } c) DefinirAtalhos(c); });
        }
        if (p.Windows is not null) f.AddRange(WindowsUsuario.Aplicar(p.Windows));
        return f;
    }

    // Um passo de folga: 60 s de teste não pegam os picos de um jogo longo.
    async Task<Mensagem> AssistenteGpu(Mensagem m)
    {
        await EsperarAdlx();
        var f = new Faixas();
        adlx.Faixas(f);
        if (!f.Campos.TryGetValue("placaDeVideo.tensao", out var faixa) || !faixa.Disponivel)
            return new Mensagem { Ok = false, Erro = "O driver não aceita mudar a tensão desta placa." };
        int inicio = (int)(faixa.Atual ?? 0), piso = (int)faixa.Min, passo = 10, segundos = 60;
        int estavel = inicio;
        void Progresso(string etapa, double p) => _ = lig.Pedir("agente.tarefa", Ligacao.Args(("etapa", etapa), ("progresso", p.ToString(System.Globalization.CultureInfo.InvariantCulture))), 2000);
        using var t = new EstresseGpu();
        if (!t.Abrir()) return new Mensagem { Ok = false, Erro = t.Motivo };
        Progresso("Rodada de referência", 0);
        var referencia = await Task.Run(() => t.Rodada());
        if (referencia is null) return new Mensagem { Ok = false, Erro = "Falha já na tensão atual: " + t.Motivo };
        using var cancelar = new CancellationTokenSource();
        assistenteGpu = cancelar;
        try
        {
            for (int v = inicio - passo; v >= piso && !cancelar.IsCancellationRequested; v -= passo)
            {
                Progresso($"Testando {v} mV por {segundos} s", (double)(inicio - v) / Math.Max(passo, inicio - piso));
                List<string> falhas;
                await travaAdlx.WaitAsync();
                try { falhas = adlx.AplicarGpu(new ConfigPlacaDeVideo { Tensao = v }); } finally { travaAdlx.Release(); }
                if (falhas.Count > 0) break;
                bool ok = true;
                var relogioTeste = System.Diagnostics.Stopwatch.StartNew();
                while (relogioTeste.Elapsed.TotalSeconds < segundos && !cancelar.IsCancellationRequested)
                {
                    var h = await Task.Run(() => t.Rodada());
                    if (h != referencia) { ok = false; break; }
                    if (lig.Estado?.Gpu.TempQuente > 100) { ok = false; break; }
                }
                if (!ok || cancelar.IsCancellationRequested) break;
                estavel = v;
            }
        }
        finally { assistenteGpu = null; }
        if (cancelar.IsCancellationRequested)
        {
            await travaAdlx.WaitAsync();
            try { adlx.AplicarGpu(new ConfigPlacaDeVideo { Tensao = inicio }); } finally { travaAdlx.Release(); }
            return new Mensagem { Ok = false, Erro = "Assistente cancelado; tensão de volta a " + inicio + " mV." };
        }
        int gravar = estavel < inicio ? Math.Min(inicio, estavel + passo) : inicio;
        await travaAdlx.WaitAsync();
        try { adlx.AplicarGpu(new ConfigPlacaDeVideo { Tensao = gravar }); } finally { travaAdlx.Release(); }
        Progresso($"Aprovado até {estavel} mV; gravado {gravar} mV", 1);
        return new Mensagem { Ok = true, Dados = Json.Elemento(gravar) };
    }

    async Task AtualizacoesDoDia()
    {
        try
        {
            var m = await lig.Pedir("maquina", null, 5000);
            var maq = Json.Ler<EstadoMaquina>(m.Dados);
            if (maq is not null && Formato.Agora - maq.AtualizacoesEm < 20 * 3600 * 1000) return;
            await App.InterfaceAsync(BuscarAtualizacoes);
        }
        catch { }
    }

    public async Task<string> BuscarAtualizacoes()
    {
        try
        {
            var s = Json.Ler<InfoSistema>((await lig.Pedir("sistema")).Dados) ?? new InfoSistema();
            var lista = await Atualizacoes.Buscar(janela, s.Bios, s.BiosData);
            await lig.Pedir("agente.atualizacoes", Ligacao.Args(("lista", Json.Elemento(lista))));
            var nova = lista.FirstOrDefault(a => a.Tipo == "BIOS" && a.Nova);
            return nova is null ? "Nenhuma BIOS nova." : $"BIOS nova: {nova.Versao}, de {nova.Data}.";
        }
        catch (Exception e) { return "Não foi possível buscar: " + e.Message; }
    }

    public void Dispose()
    {
        relogio?.Dispose(); relogioSom?.Dispose(); relogioAtualizacoes?.Dispose();
        try { Rtss.Texto(""); } catch { }
        atalhos?.Dispose();
        som.Dispose();
        adlx.Dispose();
    }
}
