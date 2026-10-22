using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using SetupHub.Nucleo;
using SetupHub.Servico.Hardware;

namespace SetupHub.Servico;

sealed partial class Motor
{
    Config config = new();
    EstadoMaquina maquina = new();
    Faixas faixas = new();
    Estado estado = new();
    readonly object trava = new();
    readonly object travaMaquina = new();
    Sensores sensores = null!;
    ChipVentoinhas chip = null!;
    Ventoinhas ventoinhas = null!;
    readonly Luz luz = new();
    readonly Processador cpu = new();
    Estabilidade estabilidade = null!;
    Historico? historico;
    Servidor servidor = null!;
    Thread? laco;
    volatile bool rodando;
    FileSystemWatcher? vigia;
    string ultimoTextoConfig = "";
    Dictionary<string, double> leituras = new();
    public Action? AoSair;
    readonly long inicio = Environment.TickCount64;
    InfoSistema sistema = new();
    bool parado;

    public void Iniciar()
    {
        Directory.CreateDirectory(Caminhos.Dados);
        Registro.Info($"{Produto.Nome} iniciando ({(Environment.IsPrivilegedProcess ? "com" : "sem")} privilégio de administrador, conta {WindowsIdentity.GetCurrent().Name}).");
        maquina = Carregar<EstadoMaquina>(Caminhos.EstadoMaquina) ?? new EstadoMaquina();
        if (maquina.Calibracao.Count == 0) maquina.Calibracao = Padroes.CalibracaoInicial();
        ProtegerPasta();
        bool queda = !maquina.DesligouLimpo;
        maquina.DesligouLimpo = false;
        faixas = Carregar<Faixas>(Caminhos.Faixas) ?? new Faixas();
        CarregarConfig();
        ConferirPendentes(queda);
        SalvarMaquina();

        servidor = new Servidor(Tratar, () => maquina.UsuarioSid);
        servidor.AoFechar = c => { if (ReferenceEquals(c, agente)) AgenteSaiu(); lock (assinantes) assinantes.Remove(c); };
        servidor.Iniciar();

        try { historico = new Historico(Caminhos.Historico); } catch (Exception e) { Registro.Erro("Histórico indisponível", e); }

        sensores = new Sensores();
        sensores.Abrir();
        chip = new ChipVentoinhas(sensores);
        chip.Abrir();
        if (!chip.Pronto) Registro.Aviso("Ventoinhas: " + chip.Motivo);
        ventoinhas = new Ventoinhas(chip, () => maquina, SalvarMaquina) { AtualizarSensores = sensores.Atualizar };
        estabilidade = new Estabilidade(cpu, () => estado);

        luz.AlertaAtivo = () => estado.Alertas.Count > 0;
        luz.JogoAberto = () => estado.Jogo is not null;
        luz.CorJogo = () => jogoAtual?.Cor;
        luz.NivelSom = () => somAgente;
        luz.Iniciar(() => leituras);
        if (luz.Disponivel) luz.Aplicar(config.Luz);

        rodando = true;
        laco = new Thread(Laco) { IsBackground = true, Name = "laço", Priority = ThreadPriority.AboveNormal };
        laco.Start();

        _ = Task.Run(Preparar);
        Vigiar();
        Registro.Info($"{Produto.Nome} pronto em {Environment.TickCount64 - inicio} ms.");
    }

    // O que leva segundos fica fora do caminho de partida.
    async Task Preparar()
    {
        try
        {
            PararLuzMsi();
            Bios.Basico(sistema);
            sistema.Processador = sensores.NomeCpu;
            cpu.Localizar();
            await LerFaixasProcessador();
            SalvarFaixas();
            lock (trava)
            {
                var antes = Json.Texto(config);
                Padroes.AjustarAoHardware(config, faixas);
                if (Json.Texto(config) != antes) GravarConfig(config);
            }
            var r = await AplicarAreas(config, null, inicioServico: true);
            Registro.Info("Ajustes da partida: " + string.Join("; ", r.Areas.Select(kv => $"{kv.Key} {kv.Value}")));
            await LerMemoria();
            var s = Carregar<InfoSistema>(Caminhos.Sistema);
            if (s is not null && s.OpcoesBios.Count > 0)
            {
                // O texto exportado é interpretado de novo: a leitura guardada não fica presa a uma interpretação antiga.
                try { sistema.OpcoesBios = File.Exists(Caminhos.Bios) ? Bios.Interpretar(File.ReadAllLines(Caminhos.Bios)) : s.OpcoesBios; }
                catch { sistema.OpcoesBios = s.OpcoesBios; }
                sistema.BiosLidaEm = s.BiosLidaEm;
            }
            else _ = Task.Run(async () => { try { await LerBios(); } catch (Exception e) { Registro.Aviso("Leitura da BIOS: " + e.Message); } });
        }
        catch (Exception e) { Registro.Erro("Preparação", e); }
    }

    // Partida guardada por sessão: serviço que cai e volta não toma o que o SetupHub gravou por valor de fábrica.
    async Task LerFaixasProcessador()
    {
        long boot = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Environment.TickCount64 / 1000;
        bool mesma = Math.Abs(maquina.PartidaWindows - boot) < 120 && maquina.ProcessadorPartida is not null;
        cpu.PartidaGuardada = mesma ? (maquina.ProcessadorPartida!, maquina.CoPartida) : null;
        await cpu.LerFaixas(faixas);
        if (mesma || !faixas.AreasLidas.Contains("processador")) return;
        lock (travaMaquina) { maquina.PartidaWindows = boot; maquina.ProcessadorPartida = Json.Copia(cpu.Inicial); maquina.CoPartida = cpu.CoInicial.ToList(); }
        SalvarMaquina();
    }

    async Task LerMemoria()
    {
        if (!cpu.Disponivel) return;
        try
        {
            var clk = await cpu.Chamar("GetCurrentMemClock");
            var tempos = new List<string>();
            foreach (var (api, nome) in new[] { ("GetMemCtrlTcl", "CL"), ("GetMemCtrlTrcdrd", "tRCD"), ("GetMemCtrlTrp", "tRP"), ("GetMemCtrlTras", "tRAS") })
            {
                var r = await cpu.Chamar(api);
                var m = System.Text.RegularExpressions.Regex.Match(r, @"\.\.\s*(\d+)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
                if (m.Success) tempos.Add($"{nome} {m.Groups[1].Value}");
            }
            var mhz = System.Text.RegularExpressions.Regex.Match(clk, @"([\d.]+)\s*MHz");
            sistema.MemoriaPerfil = mhz.Success && double.TryParse(mhz.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? $"{f * 2:0} MT/s" : "";
            sistema.MemoriaTempos = tempos;
        }
        catch (Exception e) { Registro.Aviso("Leitura da memória: " + e.Message); }
    }

    async Task LerBios()
    {
        var opcoes = await Bios.Ler();
        sistema.OpcoesBios = opcoes;
        sistema.BiosLidaEm = Formato.Agora;
        Salvar(Caminhos.Sistema, sistema);
        Registro.Info($"BIOS lida: {opcoes.Count} opções.");
    }

    void Laco()
    {
        var relogio = Stopwatch.StartNew();
        long proximo = 0;
        int ciclo = 0;
        while (rodando)
        {
            long agora = relogio.ElapsedMilliseconds;
            if (agora < proximo) { Thread.Sleep((int)Math.Min(250, proximo - agora)); continue; }
            proximo = agora + 1000 - agora % 1000 + (agora % 1000 > 900 ? 1000 : 0);
            try { Ciclo(ciclo++); }
            catch (Exception e) { Registro.Erro("Laço principal", e); }
        }
    }

    string? planoEnergia;

    void Ciclo(int n)
    {
        var e = new Estado { Hora = Formato.Agora };
        sensores.Atualizar();
        sensores.Preencher(e);
        MesclarGpu(e);
        Config c;
        lock (trava) c = config;
        var l = Plano(e);
        leituras = l;
        bool noite = c.Ventoinhas.Noite.Ligado && Horario.Dentro(c.Ventoinhas.Noite.Inicio, c.Ventoinhas.Noite.Fim, DateTime.Now);
        ventoinhas.NoiteMax = c.Ventoinhas.Noite.Maximo;
        if (!parado) ventoinhas.Ciclo(VentoinhasEfetivas(c), e, l, noite);
        foreach (var v in e.Ventoinhas)
        {
            if (v.Rpm is double r) l["fan." + v.Id] = r;
            if (v.Porcentagem is double p) l["pct." + v.Id] = p;
        }
        if (e.Gpu.FanRpm is double gr) l["fan.gpu"] = gr;
        if (e.Gpu.FanPorcentagem is double gp) l["pct.gpu"] = gp;
        e.NoiteAtiva = noite;
        e.Perfil = c.PerfilAtivo;
        e.PerfilAlterado = DiferencaPerfil.Alterado(c);
        e.UltimaTroca = ultimaTroca.Length > 0 ? ultimaTroca : null;
        e.Luz = new LeituraLuz { Ligada = c.Luz.Ligada, Plano = JogoNaConfig(c)?.Luz ?? c.Luz.Plano, Disponivel = luz.Disponivel, Erro = luz.Falha };
        e.AgenteConectado = agente is not null;
        e.Jogo = jogoAtual?.Nome;
        if (n % 5 == 0) { try { planoEnergia = Energia.IdAtivo(); } catch { } }
        if (n % 60 == 30) _ = Task.Run(PararLuzMsi);
        if (n % 30 == 20 && !AbrirPainelNoProximoLogin()) _ = Task.Run(() => AbrirJanela(null, "janela ausente"));
        e.PlanoEnergia = planoEnergia;
        e.Fps = fpsAgente;
        if (e.Fps is double fps) l["fps"] = fps;
        e.Tarefa = tarefa;
        e.AvisoConfig = avisoConfig;
        lock (avisos) e.Avisos = avisos.ToList();
        if (!ventoinhas.Disponivel) e.Avisos.Add("Ventoinhas sem controle: " + ventoinhas.Motivo);
        else if (ventoinhas.Falha is string vf) e.Avisos.Add(vf);
        Alertas(c, e, l);
        estado = e;
        historico?.Adicionar(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), l);
        if (n % 2 == 0) Automacao(c);
        if (n % 30 == 0) Confirmar();
        Transmitir(e);
    }

    static Dictionary<string, double> Plano(Estado e)
    {
        var l = new Dictionary<string, double>();
        void P(string k, double? v) { if (v is double x && !double.IsNaN(x)) l[k] = x; }
        P("cpu.temp", e.Cpu.TempMaisQuente); P("cpu.consumo", e.Cpu.Consumo); P("cpu.clock", e.Cpu.ClockMax ?? e.Cpu.Clock); P("cpu.uso", e.Cpu.Uso); P("cpu.tensao", e.Cpu.Tensao);
        P("gpu.temp", e.Gpu.Temp); P("gpu.quente", e.Gpu.TempMaisQuente); P("gpu.memoria", e.Gpu.TempMemoria); P("gpu.consumo", e.Gpu.Consumo);
        P("gpu.clock", e.Gpu.Clock); P("gpu.uso", e.Gpu.Uso); P("gpu.vram", e.Gpu.VramUsada); P("gpu.tensao", e.Gpu.Tensao);
        P("placa.sistema", e.Placa.Sistema); P("placa.vrm", e.Placa.Vrm); P("placa.chipset", e.Placa.Chipset);
        P("ssd.temp", e.Discos.Count > 0 ? e.Discos.Max(d => d.Temp) : null);
        P("memoria.uso", e.Memoria.Uso);
        return l;
    }

    string? avisoConfig;
    readonly List<string> avisos = new();

    void Avisar(string texto)
    {
        lock (avisos) { avisos.Remove(texto); avisos.Add(texto); if (avisos.Count > 5) avisos.RemoveAt(0); }
        Registro.Aviso(texto);
        _ = Evento("aviso", Json.Elemento(new EventoTexto { Tipo = "aviso", Texto = texto }));
    }

    void CarregarConfig()
    {
        if (File.Exists(Caminhos.Config))
        {
            try
            {
                var texto = File.ReadAllText(Caminhos.Config);
                var c = Json.DeTexto<Config>(texto) ?? throw new ErroSetupHub("arquivo vazio");
                bool migrou = Padroes.Migrar(c);
                Padroes.AjustarAoHardware(c, faixas);
                var erros = Validacao.Conferir(c, faixas, maquina);
                if (erros.Count == 0)
                {
                    config = c; ultimoTextoConfig = texto;
                    if (migrou) { GravarConfig(c); Registro.Info("Configuração de uma versão anterior atualizada."); }
                    return;
                }
                avisoConfig = "O arquivo de configuração tinha erros e não foi usado: " + string.Join(" ", erros.Take(3));
            }
            catch (Exception e) { avisoConfig = "O arquivo de configuração não pôde ser lido e não foi usado: " + e.Message; }
            Registro.Aviso(avisoConfig);
            var reserva = Caminhos.Config + ".valida";
            if (File.Exists(reserva))
            {
                try { var c = Json.DeTexto<Config>(File.ReadAllText(reserva)); if (c is not null) { Padroes.Migrar(c); config = c; return; } } catch { }
            }
        }
        config = Padroes.Config();
        Padroes.AjustarAoHardware(config, faixas);
        GravarConfig(config);
    }

    void GravarConfig(Config c)
    {
        var texto = Json.Texto(c, true);
        ultimoTextoConfig = texto;
        Escrever(Caminhos.Config, texto);
        Escrever(Caminhos.Config + ".valida", texto);
    }

    static void Escrever(string caminho, string texto)
    {
        var tmp = caminho + ".tmp";
        File.WriteAllText(tmp, texto, new System.Text.UTF8Encoding(false));
        File.Move(tmp, caminho, true);
    }

    // Arquivo inválido é recusado sem mudar nada.
    void Vigiar()
    {
        try
        {
            vigia = new FileSystemWatcher(Caminhos.Dados, "config.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            Timer? atraso = null;
            void Mudou(object? _, FileSystemEventArgs __)
            {
                atraso?.Dispose();
                atraso = new Timer(_ => _ = Recarregar(), null, 300, Timeout.Infinite);
            }
            vigia.Changed += Mudou; vigia.Created += Mudou; vigia.Renamed += (s, e) => Mudou(s, e);
            vigia.EnableRaisingEvents = true;
        }
        catch (Exception e) { Registro.Erro("Vigia do arquivo de configuração", e); }
    }

    async Task Recarregar()
    {
        string texto;
        try { texto = await File.ReadAllTextAsync(Caminhos.Config); } catch { return; }
        if (texto == ultimoTextoConfig) return;
        try
        {
            var novo = Json.DeTexto<Config>(texto) ?? throw new ErroSetupHub("arquivo vazio");
            Padroes.Migrar(novo);
            var r = await Trocar(novo, "arquivo", gravar: false);
            ultimoTextoConfig = texto;
            Escrever(Caminhos.Config + ".valida", texto);
            avisoConfig = null;
            Registro.Info("Arquivo de configuração recarregado: " + (r.Areas.Count == 0 ? "nenhum ajuste mudou." : string.Join("; ", r.Areas.Select(kv => $"{kv.Key} {kv.Value}"))));
            await Evento("config", null);
        }
        catch (Exception e)
        {
            var motivo = e is ErroSetupHub ? e.Message : "JSON inválido: " + e.Message;
            avisoConfig = "Arquivo de configuração recusado, nada mudou: " + motivo;
            ultimoTextoConfig = texto;
            Registro.Aviso(avisoConfig);
            await Evento("aviso", Json.Elemento(new EventoTexto { Tipo = "config", Texto = avisoConfig }));
        }
    }

    // Aplicar um perfil não grava de volta nele: com campo travado, a cópia aplicada vem sem o valor que o perfil guarda.
    async Task<ResultadoAplicar> Trocar(Config novo, string origem, bool gravar = true, bool guardarNoPerfil = true)
    {
        Config anterior;
        lock (trava) anterior = config;
        if (guardarNoPerfil)
        {
            GuardaPerfil.NoNivel(novo, anterior);
            GuardaPerfil.NoAtivo(novo, anterior);
        }
        var erros = Validacao.Conferir(novo, faixas, maquina, anterior);
        if (erros.Count > 0)
        {
            Registro.Aviso($"Recusado ({origem}): " + string.Join(" | ", erros));
            throw new ErroSetupHub(string.Join("\n", erros));
        }
        lock (trava) config = novo;
        if (gravar) GravarConfig(novo);
        var r = await AplicarAreas(novo, anterior, inicioServico: false);
        _ = Evento("config", null);
        return r;
    }

    static T? Carregar<T>(string caminho) where T : class
    {
        try { return File.Exists(caminho) ? Json.DeTexto<T>(File.ReadAllText(caminho)) : null; }
        catch (Exception e) { Registro.Aviso($"{Path.GetFileName(caminho)} ilegível: {e.Message}"); return null; }
    }

    static void Salvar<T>(string caminho, T valor)
    {
        try { Escrever(caminho, Json.Texto(valor, true)); } catch (Exception e) { Registro.Erro("Gravar " + Path.GetFileName(caminho), e); }
    }

    void SalvarMaquina() { lock (travaMaquina) Salvar(Caminhos.EstadoMaquina, maquina); }
    void SalvarFaixas() => Salvar(Caminhos.Faixas, faixas);

    // A conta do usuário do SetupHub edita o config.json à mão; as demais contas só leem.
    void ProtegerPasta()
    {
        try
        {
            if (!Environment.IsPrivilegedProcess) return;
            var di = new DirectoryInfo(Caminhos.Dados);
            var s = new DirectorySecurity();
            s.SetAccessRuleProtection(true, false);
            const InheritanceFlags herda = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, herda, PropagationFlags.None, AccessControlType.Allow));
            s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, herda, PropagationFlags.None, AccessControlType.Allow));
            s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, herda, PropagationFlags.None, AccessControlType.Allow));
            if (!string.IsNullOrEmpty(maquina.UsuarioSid))
                s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(maquina.UsuarioSid), FileSystemRights.Modify, herda, PropagationFlags.None, AccessControlType.Allow));
            di.SetAccessControl(s);
        }
        catch (Exception e) { Registro.Aviso("Permissões da pasta de dados: " + e.Message); }
    }

    // Conferido a cada minuto: o MSI Center pode abrir o LEDKeeper2 de novo.
    void PararLuzMsi()
    {
        bool parou = false;
        foreach (var nome in Instalador.ServicosLuz)
        {
            try
            {
                using var s = ServiceController.GetServices().FirstOrDefault(x => x.ServiceName == nome);
                if (s is null || s.Status == ServiceControllerStatus.Stopped) continue;
                s.Stop();
                s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                Registro.Info($"Serviço {nome} da MSI parado (ele desfaz a luz do SetupHub).");
                parou = true;
            }
            catch (Exception e) { Registro.Aviso($"Não foi possível parar o serviço {nome} da MSI: " + e.Message); }
        }
        foreach (var p in Process.GetProcessesByName("LEDKeeper2"))
        {
            try { p.Kill(); Registro.Info("LEDKeeper2 da MSI fechado (ele desfaz a luz do SetupHub)."); parou = true; } catch { }
            finally { p.Dispose(); }
        }
        if (parou && luz.Disponivel) luz.Aplicar(LuzEfetiva(config));
    }

    public void Parar(bool desligando)
    {
        if (parado) return;
        parado = true;
        Registro.Info(desligando ? "Windows desligando: devolvendo ventoinhas e guardando a luz." : "Parando: devolvendo ventoinhas à BIOS e trocando a luz por efeito do controlador.");
        try { tarefaCts?.Cancel(); } catch { }
        rodando = false;
        laco?.Join(3000);
        try { ventoinhas?.DevolverTudo(); } catch (Exception e) { Registro.Erro("Devolver ventoinhas", e); }
        try { luz.Encerrar(); } catch (Exception e) { Registro.Erro("Encerrar luz", e); }
        try { historico?.Dispose(); } catch { }
        try { vigia?.Dispose(); } catch { }
        try { servidor?.Parar(); } catch { }
        try { sensores?.Dispose(); } catch { }
        maquina.DesligouLimpo = true;
        SalvarMaquina();
        Registro.Info($"{Produto.Nome} parado.");
        Registro.Descarregar();
    }

    public void Suspendendo()
    {
        Registro.Info("Suspensão: luz guardada no controlador.");
        historico?.Descarregar();
        SalvarMaquina();
    }

    public void Voltou()
    {
        Registro.Info("Voltou da suspensão: reaplicando ventoinhas, luz e processador.");
        ventoinhas.Retomar();
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            if (luz.Disponivel) luz.Aplicar(LuzEfetiva(config));
            await LerFaixasProcessador();
            await AplicarAreas(config, null, inicioServico: true);
        });
    }

    public void Tela(bool ligada) { luz.TelaApagada = !ligada; }

    // Tipos de evento de sessão: conexão ao console 1, logon 5, desbloqueio 8.
    public void Sessao(int tipo, int sessao)
    {
        if (tipo is not (1 or 5 or 8)) return;
        bool adiada;
        lock (travaMaquina)
        {
            adiada = maquina.AbrirPainelNoProximoLogin;
            maquina.AbrirPainelNoProximoLogin = false;
        }
        if (adiada) SalvarMaquina();
        _ = Task.Run(async () => { await Task.Delay(3000); AbrirJanela(sessao > 0 ? (uint)sessao : null, "login"); });
    }

    bool AbrirPainelNoProximoLogin()
    {
        lock (travaMaquina) return maquina.AbrirPainelNoProximoLogin;
    }

    void AbrirJanela(uint? sessao, string motivo)
    {
        try
        {
            if (parado || agente is not null || !WindowsIdentity.GetCurrent().IsSystem) return;
            Config c; lock (trava) c = config;
            if (!c.Aparencia.AbrirComWindows) return;
            var s = sessao ?? SessaoUsuario.Ativa();
            if (s is null) return;
            var abertas = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Produto.ExePainel));
            bool ja = abertas.Any(p => p.SessionId == s);
            foreach (var p in abertas) p.Dispose();
            if (ja) return;
            var exe = Path.Combine(AppContext.BaseDirectory, Produto.ExePainel);
            if (!File.Exists(exe)) return;
            if (SessaoUsuario.Abrir(s.Value, exe, "--bandeja", out var erro)) Registro.Info($"Janela aberta na sessão do usuário ({motivo}).");
            else Registro.Aviso($"Não foi possível abrir a janela ({motivo}): {erro}");
        }
        catch (Exception e) { Registro.Erro("Abrir a janela", e); }
    }
}
