using SetupHub.Nucleo;
using SetupHub.Servico.Hardware;
using Microsoft.Win32;

namespace SetupHub.Servico;

sealed partial class Motor
{
    // Áreas em paralelo: o kit da AMD leva 0,7 s por chamada e a falha de uma não segura as outras.
    async Task<ResultadoAplicar> AplicarAreas(Config novo, Config? anterior, bool inicioServico)
    {
        var r = new ResultadoAplicar();
        bool Mudou<T>(Func<Config, T> secao) => anterior is null || Json.Texto(secao(novo)) != Json.Texto(secao(anterior));
        var tarefas = new List<Task>();
        void Area(string nome, Func<Task<List<string>>> f)
        {
            tarefas.Add(Task.Run(async () =>
            {
                List<string> falhas;
                try { falhas = await f(); }
                catch (Exception e) { falhas = new() { e.Message }; }
                lock (r)
                {
                    r.Areas[nome] = falhas.Count == 0 ? "aplicado e conferido" : "falhou: " + string.Join(", ", falhas);
                    r.Falhas.AddRange(falhas.Select(x => $"{nome}: {x}"));
                }
            }));
        }
        bool jogo = jogoAtual is not null && (anterior is null || Json.Texto(JogoNaConfig(novo)) != Json.Texto(JogoNaConfig(anterior)));
        if (Mudou(c => c.Ventoinhas.Saidas) || Mudou(c => c.Ventoinhas.Noite))
            lock (r) r.Areas["ventoinhas"] = chip.Pronto ? "aplicado no próximo segundo" : "sem controle: " + chip.Motivo;
        if ((Mudou(c => c.Luz) || jogo) && luz.Disponivel) Area("luz", () => Task.FromResult(luz.Aplicar(LuzEfetiva(novo))));
        if (Mudou(c => c.Processador))
        {
            if (cpu.Disponivel && cpu.Lido && faixas.AreasLidas.Contains("processador"))
                Area("processador", () => AplicarProcessador(novo.Processador, inicioServico));
            else
            {
                // Aplicado só depois, na leitura do processador: uma queda antes disso também tem que voltar ao confirmado.
                if (!inicioServico) MarcarPendenteProcessador(novo.Processador);
                if (cpu.Disponivel && !cpu.Lido) lock (r) r.Areas["processador"] = "aplicado quando a leitura do processador terminar, em alguns segundos";
            }
        }
        if (Mudou(c => c.Energia)) Area("energia", () => Task.FromResult(Energia.Aplicar(novo.Energia)));
        if (Mudou(c => c.Windows)) Area("windows", () => Task.FromResult(WindowsSistema(novo.Windows)));
        if (agente is not null)
        {
            var p = new PedidoAgente();
            bool algo = false;
            if (Mudou(c => c.PlacaDeVideo)) { p.PlacaDeVideo = novo.PlacaDeVideo; algo = true; MarcarPendenteGpu(novo.PlacaDeVideo); }
            if (Mudou(c => c.Ventoinhas.Gpu) || jogo) { p.Ventoinha = VentoinhasEfetivas(novo).Gpu; algo = true; }
            if (Mudou(c => c.Jogos.Global) || jogo) { p.Graficos = GraficosEfetivos(novo); algo = true; }
            if (Mudou(c => c.Telas)) { p.Telas = novo.Telas; algo = true; }
            if (Mudou(c => c.Jogos.Sobreposicao)) { p.Sobreposicao = novo.Jogos.Sobreposicao; algo = true; }
            if (Mudou(c => c.Jogos.LimiteQuadros) || jogo) { p.LimiteQuadros = LimiteEfetivo(novo); algo = true; }
            if (Mudou(c => c.Windows)) { p.Windows = novo.Windows; algo = true; }
            if (algo) Area("placa de vídeo e telas", () => PedirAgente(p));
        }
        else if (Mudou(c => c.PlacaDeVideo) || Mudou(c => c.Jogos) || Mudou(c => c.Telas) || Mudou(c => c.Ventoinhas.Gpu))
            lock (r) r.Areas["placa de vídeo e telas"] = "aplica quando a janela do SetupHub abrir nesta sessão";
        await Task.WhenAll(tarefas);
        if (r.Falhas.Count > 0) Registro.Aviso("Falhas ao aplicar: " + string.Join(" | ", r.Falhas));
        return r;
    }

    async Task<List<string>> PedirAgente(PedidoAgente p)
    {
        var a = agente;
        if (a is null) return new() { "a janela do SetupHub não está aberta nesta sessão" };
        var resp = await a.PedirAsync("agente.aplicar", Json.Elemento(p), 60000);
        if (!resp.Ok) return new() { resp.Erro ?? "sem resposta" };
        return Json.Ler<List<string>>(resp.Dados) ?? new();
    }

    // Só é reaplicado ao ligar depois de confirmado: teste de estabilidade, 30 minutos sem queda ou desligamento normal.
    async Task<List<string>> AplicarProcessador(ConfigProcessador alvo, bool partida)
    {
        if (!partida) MarcarPendenteProcessador(alvo);
        return await cpu.Aplicar(alvo);
    }

    void MarcarPendenteProcessador(ConfigProcessador alvo)
    {
        var confirmado = maquina.ProcessadorConfirmado ?? new ConfigProcessador();
        if (Json.Texto(alvo) != Json.Texto(confirmado))
        {
            maquina.ProcessadorPendente = Json.Copia(alvo);
            maquina.PendenteDesde = Formato.Agora;
        }
        else maquina.ProcessadorPendente = null;
        SalvarMaquina();
    }

    void MarcarPendenteGpu(ConfigPlacaDeVideo alvo)
    {
        var confirmado = maquina.GpuConfirmada ?? new ConfigPlacaDeVideo();
        if (Json.Texto(alvo) != Json.Texto(confirmado)) { maquina.GpuPendente = Json.Copia(alvo); maquina.PendenteDesde = Formato.Agora; }
        else maquina.GpuPendente = null;
        SalvarMaquina();
    }

    void ConferirPendentes(bool queda)
    {
        bool mudou = false;
        if (maquina.ProcessadorPendente is not null)
        {
            if (queda)
            {
                config.Processador = Json.Copia(maquina.ProcessadorConfirmado ?? new ConfigProcessador());
                foreach (var p in config.Perfis.Where(p => p.Processador is not null && Json.Texto(p.Processador) == Json.Texto(maquina.ProcessadorPendente)))
                    p.Processador = Json.Copia(config.Processador);
                avisos.Add("O PC desligou de forma inesperada logo depois de um ajuste do processador: a partida voltou ao último ajuste confirmado.");
                mudou = true;
            }
            else maquina.ProcessadorConfirmado = maquina.ProcessadorPendente;
            maquina.ProcessadorPendente = null;
        }
        if (maquina.GpuPendente is not null)
        {
            if (queda)
            {
                config.PlacaDeVideo = Json.Copia(maquina.GpuConfirmada ?? new ConfigPlacaDeVideo());
                avisos.Add("O PC desligou de forma inesperada logo depois de um ajuste da placa de vídeo: voltou ao último ajuste confirmado.");
                mudou = true;
            }
            else maquina.GpuConfirmada = maquina.GpuPendente;
            maquina.GpuPendente = null;
        }
        if (queda) Registro.Aviso("A sessão anterior terminou sem desligamento normal do SetupHub.");
        if (mudou) { GravarConfig(config); foreach (var a in avisos) Registro.Aviso(a); }
    }

    void Confirmar()
    {
        if (maquina.PendenteDesde == 0) return;
        if (Formato.Agora - maquina.PendenteDesde < 30 * 60 * 1000) return;
        bool algo = false;
        if (maquina.ProcessadorPendente is not null) { maquina.ProcessadorConfirmado = maquina.ProcessadorPendente; maquina.ProcessadorPendente = null; algo = true; }
        if (maquina.GpuPendente is not null) { maquina.GpuConfirmada = maquina.GpuPendente; maquina.GpuPendente = null; algo = true; }
        maquina.PendenteDesde = 0;
        if (algo) { SalvarMaquina(); Registro.Info("Ajustes de processador e placa de vídeo confirmados por 30 minutos de uso."); }
    }

    static List<string> WindowsSistema(ConfigWindows w)
    {
        var falhas = new List<string>();
        if (w.AgendamentoGpu is bool ag)
        {
            try
            {
                using var k = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                int v = ag ? 2 : 1;
                if (k.GetValue("HwSchMode") is not int atual || atual != v) k.SetValue("HwSchMode", v, RegistryValueKind.DWord);
                if (k.GetValue("HwSchMode") is not int lido || lido != v) falhas.Add("agendamento da placa de vídeo");
            }
            catch (Exception e) { falhas.Add("agendamento da placa de vídeo: " + e.Message); }
        }
        return falhas;
    }

    Config ComPerfil(Config atual, Perfil p)
    {
        var n = Json.Copia(atual);
        foreach (var a in p.Areas)
        {
            switch (a)
            {
                // O nível guardado manda sobre a cópia do perfil: ajuste feito no nível vale em todo perfil que usa ele.
                case "ventoinhas" when p.Ventoinhas is not null && n.NiveisVentoinhas.ContainsKey(p.Ventoinhas.Nivel):
                    Padroes.AplicarNivel(n, n.Ventoinhas, p.Ventoinhas.Nivel, n.Ventoinhas.Saidas.Select(s => s.Id).Append("gpu"));
                    n.Ventoinhas.Nivel = p.Ventoinhas.Nivel;
                    break;
                case "ventoinhas" when p.Ventoinhas is not null:
                    foreach (var s in p.Ventoinhas.Saidas)
                    {
                        var alvo = n.Ventoinhas.Saidas.FirstOrDefault(x => x.Id == s.Id);
                        if (alvo is null) continue;
                        alvo.Modo = s.Modo; alvo.Fixo = s.Fixo; alvo.Fontes = s.Fontes.ToList(); alvo.Combinar = s.Combinar;
                        alvo.Curva = s.Curva.Select(x => new Ponto(x.T, x.P)).ToList(); alvo.Suavizacao = s.Suavizacao; alvo.Histerese = s.Histerese; alvo.PararAbaixo = s.PararAbaixo;
                    }
                    n.Ventoinhas.Gpu = Json.Copia(p.Ventoinhas.Gpu);
                    n.Ventoinhas.Nivel = p.Ventoinhas.Nivel;
                    break;
                case "luz" when p.Luz is not null:
                    // Saídas e biblioteca de planos são da máquina, não do perfil.
                    n.Luz.Plano = p.Luz.Plano; n.Luz.Ligada = p.Luz.Ligada;
                    if (!n.Luz.Planos.Any(x => x.Nome == p.Luz.Plano)) { var pl = p.Luz.Planos.FirstOrDefault(x => x.Nome == p.Luz.Plano); if (pl is not null) n.Luz.Planos.Add(Json.Copia(pl)); }
                    break;
                // Campo travado agora fica de fora; o perfil continua com ele para a próxima vez.
                case "processador" when p.Processador is not null:
                    n.Processador = Validacao.SemTravados(p.Processador, faixas, "processador");
                    n.Processador.Ppt = Padroes.Ppt(faixas, n.Processador.Ppt);
                    if (n.Processador.Ppt < 0) n.Processador.Ppt = null;
                    break;
                case "placaDeVideo" when p.PlacaDeVideo is not null: n.PlacaDeVideo = Validacao.SemTravados(p.PlacaDeVideo, faixas, "placaDeVideo"); break;
                case "graficos" when p.Graficos is not null: n.Jogos.Global = Validacao.SemTravados(p.Graficos, faixas, "graficos"); break;
                case "sobreposicao" when p.Sobreposicao is not null: n.Jogos.Sobreposicao = Json.Copia(p.Sobreposicao); break;
                case "energia" when p.Energia is not null: n.Energia.Plano = p.Energia.Plano; break;
            }
        }
        n.PerfilAtivo = p.Nome;
        return n;
    }

    string ultimaTroca = "";

    static string Origem(string o) => o switch
    {
        "janela" => "pela janela",
        "ícone da bandeja" => "pelo ícone da bandeja",
        "atalho de teclado" => "pelo atalho de teclado",
        "comando" or "linha de comando" => "pela linha de comando",
        "programa aberto" => "porque um programa abriu",
        "programa fechado" => "porque o programa fechou",
        "jogo fechado" => "porque o jogo fechou",
        _ when o.StartsWith("jogo ") => "porque o " + o.Replace(" aberto", " abriu"),
        _ when o.StartsWith("horário") || o.StartsWith("fim do horário") => "pelo " + o,
        _ => o,
    };

    async Task<ResultadoAplicar> AplicarPerfil(string nome, string origem)
    {
        Config atual;
        lock (trava) atual = config;
        var p = atual.Perfis.FirstOrDefault(x => string.Equals(x.Nome, nome, StringComparison.OrdinalIgnoreCase))
            ?? throw new ErroSetupHub($"O perfil \"{nome}\" não existe. Perfis: {string.Join(", ", atual.Perfis.Select(x => x.Nome))}.");
        var r = await Trocar(ComPerfil(atual, p), origem, guardarNoPerfil: false);
        int ok = r.Areas.Count(kv => !kv.Value.StartsWith("falhou"));
        ultimaTroca = $"{p.Nome} às {DateTime.Now:HH:mm}, {Origem(origem)}. {ok} de {r.Areas.Count} áreas aplicadas e conferidas" + (r.Falhas.Count > 0 ? $"; falhou: {string.Join(", ", r.Falhas)}" : ".");
        Registro.Info("Perfil " + ultimaTroca);
        return r;
    }

    async Task SalvarPerfil(string nome, List<string>? areas)
    {
        Config atual;
        lock (trava) atual = config;
        var n = Json.Copia(atual);
        var p = n.Perfis.FirstOrDefault(x => string.Equals(x.Nome, nome, StringComparison.OrdinalIgnoreCase));
        if (p is null) { p = new Perfil { Nome = nome, Areas = areas ?? new() { "ventoinhas", "processador", "placaDeVideo", "luz", "energia" } }; n.Perfis.Add(p); }
        else if (areas is not null) p.Areas = areas;
        p.Ventoinhas = Json.Copia(n.Ventoinhas);
        p.Ventoinhas.Grupos = new();
        p.Luz = GuardaPerfil.Luz(n);
        p.Processador = Json.Copia(n.Processador);
        p.PlacaDeVideo = Json.Copia(n.PlacaDeVideo);
        p.Graficos = Json.Copia(n.Jogos.Global);
        p.Sobreposicao = Json.Copia(n.Jogos.Sobreposicao);
        p.Energia = new ConfigEnergia { Plano = n.Energia.Plano };
        n.PerfilAtivo = p.Nome;
        await Trocar(n, "perfil salvo");
    }
}
