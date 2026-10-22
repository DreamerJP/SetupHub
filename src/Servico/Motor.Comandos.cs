using System.Text.Json;
using SetupHub.Nucleo;
using SetupHub.Servico.Hardware;

namespace SetupHub.Servico;

sealed partial class Motor
{
    Tarefa? tarefa;
    CancellationTokenSource? tarefaCts;

    static string? S(JsonElement? a, string k) => a is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(k, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : v.GetRawText() : null;
    static int? I(JsonElement? a, string k) => int.TryParse(S(a, k), out var v) ? v : null;
    static long? L(JsonElement? a, string k) => long.TryParse(S(a, k), out var v) ? v : null;
    static bool? B(JsonElement? a, string k) => S(a, k) is string s ? s is "true" or "1" or "sim" or "ligado" : null;
    static List<string> Lista(JsonElement? a, string k)
    {
        if (a is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(k, out var v))
        {
            if (v.ValueKind == JsonValueKind.Array) return v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : x.GetRawText()).ToList();
            if (v.ValueKind == JsonValueKind.String) return v.GetString()!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return new();
    }
    static JsonElement? E(JsonElement? a, string k) => a is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(k, out var v) ? v : null;

    static Mensagem Ok<T>(T dados) => new() { Ok = true, Dados = Json.Elemento(dados) };
    static Mensagem Ok() => new() { Ok = true };

    // Aceita id, nome ou grupo ("sys1", "Fan 1", "Gabinete"); vazio vale para todas.
    List<string> Ventoinhas_(Config c, List<string> nomes)
    {
        if (nomes.Count == 0 || nomes.Any(n => n.Equals("todas", StringComparison.OrdinalIgnoreCase))) return c.Ventoinhas.Saidas.Select(s => s.Id).Append("gpu").ToList();
        var r = new List<string>();
        foreach (var n in nomes)
        {
            var g = c.Ventoinhas.Grupos.FirstOrDefault(x => x.Tipo == "ventoinhas" && x.Nome.Equals(n, StringComparison.OrdinalIgnoreCase));
            if (g is not null) { r.AddRange(g.Itens); continue; }
            if (n.Equals("gpu", StringComparison.OrdinalIgnoreCase) || n.Equals("Fan GPU", StringComparison.OrdinalIgnoreCase)) { r.Add("gpu"); continue; }
            var s = c.Ventoinhas.Saidas.FirstOrDefault(x => x.Id.Equals(n, StringComparison.OrdinalIgnoreCase) || x.Nome.Equals(n, StringComparison.OrdinalIgnoreCase))
                ?? throw new ErroSetupHub($"Ventoinha \"{n}\" não existe. Ventoinhas: {string.Join(", ", c.Ventoinhas.Saidas.Select(x => x.Nome))}, Fan GPU; grupos: {string.Join(", ", c.Ventoinhas.Grupos.Select(x => x.Nome))}.");
            r.Add(s.Id);
        }
        return r.Distinct().ToList();
    }

    List<string> SaidasLuz(Config c, List<string> nomes)
    {
        if (nomes.Count == 0 || nomes.Any(n => n.Equals("todas", StringComparison.OrdinalIgnoreCase))) return c.Luz.Saidas.Select(s => s.Id).ToList();
        var r = new List<string>();
        foreach (var n in nomes)
        {
            var g = c.Ventoinhas.Grupos.FirstOrDefault(x => x.Tipo == "luz" && x.Nome.Equals(n, StringComparison.OrdinalIgnoreCase));
            if (g is not null) { r.AddRange(g.Itens); continue; }
            var s = c.Luz.Saidas.FirstOrDefault(x => x.Id.Equals(n, StringComparison.OrdinalIgnoreCase) || x.Nome.Equals(n, StringComparison.OrdinalIgnoreCase))
                ?? throw new ErroSetupHub($"Saída de luz \"{n}\" não existe. Saídas: {string.Join(", ", c.Luz.Saidas.Select(x => x.Nome))}.");
            r.Add(s.Id);
        }
        return r.Distinct().ToList();
    }

    Config Copia() { lock (trava) return Json.Copia(config); }

    async Task<Mensagem> Tratar(Conexao c, Mensagem m)
    {
        var a = m.Args;
        switch (m.Comando)
        {
            case "estado": return Ok(estado);
            case "faixas": return Ok(faixas);
            case "config":
                var cam = S(a, "caminho");
                if (string.IsNullOrEmpty(cam)) return Ok(Copia());
                return Ok(Caminho.Ler(Copia(), cam));
            case "campos": return Ok(Caminho.Campos(Copia()));
            case "sistema": return Ok(sistema);
            case "maquina": lock (travaMaquina) return Ok(maquina);
            case "historico":
            {
                if (historico is null) throw new ErroSetupHub("O histórico não está disponível.");
                long agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return Ok(historico.Serie(S(a, "sensor") ?? "cpu.temp", L(a, "de") ?? agora - 3600, L(a, "ate") ?? agora, I(a, "pontos") ?? 600));
            }
            case "historico.exportar":
            {
                if (historico is null) throw new ErroSetupHub("O histórico não está disponível.");
                var arq = S(a, "arquivo") ?? throw new ErroSetupHub("Falta o arquivo.");
                long agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var tmp = Path.Combine(Caminhos.Dados, "historico_exportado.csv");
                int linhas = historico.Exportar(tmp, L(a, "de") ?? agora - 86400, L(a, "ate") ?? agora);
                return Ok(new EventoTexto { Tipo = tmp, Texto = linhas.ToString() });
            }
            case "atualizacoes": lock (travaMaquina) return Ok(maquina.Atualizacoes);
            case "registro":
            {
                Registro.Descarregar();
                int n = I(a, "linhas") ?? 50;
                var linhas = File.Exists(Caminhos.Registro) ? File.ReadLines(Caminhos.Registro).TakeLast(n).ToList() : new List<string>();
                return Ok(linhas);
            }
            case "comandos": return Ok(Comandos.Lista);

            case "definir":
            {
                var caminho = S(a, "caminho") ?? throw new ErroSetupHub("Falta o caminho do campo.");
                var valor = S(a, "valor") ?? "";
                var novo = Caminho.Definir(Copia(), caminho, valor);
                return Ok(await Trocar(novo, "comando definir " + caminho));
            }
            case "config.gravar":
            {
                var cfg = Json.Ler<Config>(E(a, "config")) ?? throw new ErroSetupHub("Configuração vazia.");
                Padroes.Migrar(cfg);
                return Ok(await Trocar(cfg, S(a, "origem") ?? "importação"));
            }
            case "assinar":
                lock (assinantes) if (!assinantes.Contains(c)) assinantes.Add(c);
                return Ok(estado);

            case "perfil.aplicar": return Ok(await AplicarPerfil(S(a, "nome") ?? throw new ErroSetupHub("Falta o nome do perfil."), S(a, "origem") ?? "comando"));
            case "perfil.salvar":
                await SalvarPerfil(S(a, "nome") ?? throw new ErroSetupHub("Falta o nome do perfil."), E(a, "areas") is null ? null : Lista(a, "areas"));
                return Ok();
            case "perfil.excluir":
            {
                var n = Copia(); var nome = S(a, "nome");
                var p = n.Perfis.FirstOrDefault(x => x.Nome == nome) ?? throw new ErroSetupHub($"O perfil \"{nome}\" não existe.");
                if (n.PerfilAtivo == p.Nome) throw new ErroSetupHub("O perfil ativo não pode ser excluído. Aplique outro perfil antes.");
                n.Perfis.Remove(p);
                foreach (var j in n.Jogos.Lista.Where(j => j.Perfil == p.Nome)) j.Perfil = null;
                return Ok(await Trocar(n, "perfil excluído"));
            }
            case "perfil.renomear":
            {
                var n = Copia(); var nome = S(a, "nome"); var novo = S(a, "novo") ?? throw new ErroSetupHub("Falta o novo nome.");
                var p = n.Perfis.FirstOrDefault(x => x.Nome == nome) ?? throw new ErroSetupHub($"O perfil \"{nome}\" não existe.");
                if (n.Perfis.Any(x => x.Nome.Equals(novo, StringComparison.OrdinalIgnoreCase))) throw new ErroSetupHub($"Já existe um perfil \"{novo}\".");
                if (n.PerfilAtivo == p.Nome) n.PerfilAtivo = novo;
                foreach (var j in n.Jogos.Lista.Where(j => j.Perfil == p.Nome)) j.Perfil = novo;
                p.Nome = novo;
                return Ok(await Trocar(n, "perfil renomeado"));
            }
            case "perfil.duplicar":
            {
                var n = Copia(); var nome = S(a, "nome");
                var p = n.Perfis.FirstOrDefault(x => x.Nome == nome) ?? throw new ErroSetupHub($"O perfil \"{nome}\" não existe.");
                var copia = Json.Copia(p); copia.Pronto = false; copia.Atalho = null;
                int k = 2; do { copia.Nome = $"{p.Nome} {k++}"; } while (n.Perfis.Any(x => x.Nome == copia.Nome));
                n.Perfis.Add(copia);
                await Trocar(n, "perfil duplicado");
                return Ok(copia.Nome);
            }
            case "perfil.restaurar":
            {
                var n = Copia(); var nome = S(a, "nome");
                var p = n.Perfis.FirstOrDefault(x => x.Nome == nome) ?? throw new ErroSetupHub($"O perfil \"{nome}\" não existe.");
                var fabrica = Padroes.Fabrica(p) ?? throw new ErroSetupHub($"\"{p.Nome}\" é um perfil personalizado, sem valores de fábrica.");
                Padroes.AjustarAoHardware(new Config { Perfis = new() { fabrica } }, faixas);
                GuardaPerfil.Restaurar(p, fabrica);
                return Ok(await Trocar(n.PerfilAtivo == p.Nome ? ComPerfil(n, p) : n, "perfil restaurado", guardarNoPerfil: false));
            }

            case "ventoinhas.nivel":
            {
                var n = Copia();
                var nivel = (S(a, "nivel") ?? "").ToLowerInvariant().Replace("á", "a");
                if (!Padroes.Niveis.Contains(nivel)) throw new ErroSetupHub($"Nível \"{nivel}\" não existe: silencioso, equilibrado, forte ou maximo.");
                var ids = Lista(a, "ids");
                Padroes.AplicarNivel(n, n.Ventoinhas, nivel, ids.Count == 0 ? n.Ventoinhas.Saidas.Where(s => s.Modo != "bios" || ConectadaOuCalibrada(s.Id)).Select(s => s.Id).Append("gpu") : Ventoinhas_(n, ids));
                if (ids.Count == 0) n.Ventoinhas.Nivel = nivel;
                return Ok(await Trocar(n, "nível " + nivel));
            }
            case "ventoinha":
            {
                var n = Copia();
                var ids = Ventoinhas_(n, Lista(a, "ids"));
                var modo = S(a, "modo");
                var valor = I(a, "valor");
                foreach (var id in ids)
                {
                    if (id == "gpu")
                    {
                        if (modo is "auto" or "bios") n.Ventoinhas.Gpu.Modo = "auto";
                        else if (valor is int vg) { n.Ventoinhas.Gpu.Modo = "curva"; n.Ventoinhas.Gpu.Curva = new() { new(30, vg), new(50, vg), new(65, vg), new(75, Math.Max(vg, 80)), new(Padroes.CriticaGpu, 100) }; }
                        continue;
                    }
                    var s = n.Ventoinhas.Saidas.First(x => x.Id == id);
                    if (valor is int v) { s.Modo = "fixo"; s.Fixo = v; }
                    else if (modo is "bios" or "curva" or "fixo") s.Modo = modo;
                    else throw new ErroSetupHub("Informe a porcentagem (0 a 100) ou o modo (bios, curva, fixo).");
                }
                return Ok(await Trocar(n, "comando ventoinha"));
            }
            case "ventoinhas.calibrar":
            {
                var n = Copia();
                var ids = Ventoinhas_(n, Lista(a, "ids")).Where(x => x != "gpu").ToList();
                IniciarTarefa("Calibração das ventoinhas", async (prog, ct) =>
                {
                    var res = await ventoinhas.Calibrar(ids, () => estado.Cpu.TempMaisQuente, prog, ct);
                    foreach (var (id, cal) in res) maquina.Calibracao[id] = cal;
                    SalvarMaquina();
                    return string.Join("; ", res.Select(kv => $"{n.Ventoinhas.Saidas.First(s => s.Id == kv.Key).Nome}: começa em {kv.Value.Inicio}%, máximo {kv.Value.RpmMax} rpm, resposta {kv.Value.Resposta:0.#} s"));
                });
                return Ok(tarefa);
            }

            case "luz.plano":
            {
                var n = Copia(); var nome = S(a, "nome");
                var p = n.Luz.Planos.FirstOrDefault(x => x.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase)) ?? throw new ErroSetupHub($"Plano de luz \"{nome}\" não existe. Planos: {string.Join(", ", n.Luz.Planos.Select(x => x.Nome))}.");
                var ids = Lista(a, "ids");
                if (n.Luz.Sincronizar || ids.Count == 0)
                {
                    n.Luz.Plano = p.Nome;
                    foreach (var s in n.Luz.Saidas) s.Plano = null;
                }
                else foreach (var id in SaidasLuz(n, ids)) n.Luz.Saidas.First(s => s.Id == id).Plano = p.Nome;
                n.Luz.Ligada = p.Efeito.Tipo != "desligado" || n.Luz.Ligada;
                return Ok(await Trocar(n, "plano de luz " + p.Nome));
            }
            case "luz.ligar":
            {
                var n = Copia(); n.Luz.Ligada = B(a, "ligada") ?? !n.Luz.Ligada;
                return Ok(await Trocar(n, n.Luz.Ligada ? "luz ligada" : "luz desligada"));
            }
            case "luz.efeito":
            {
                var n = Copia();
                var ef = Json.Ler<EfeitoLuz>(E(a, "efeito")) ?? throw new ErroSetupHub("Falta o efeito.");
                var ids = n.Luz.Sincronizar ? n.Luz.Saidas.Select(s => s.Id).ToList() : SaidasLuz(n, Lista(a, "ids"));
                foreach (var s in n.Luz.Saidas.Where(s => ids.Contains(s.Id))) { s.Efeito = Json.Copia(ef); s.Plano = "personalizado"; }
                if (ids.Count == n.Luz.Saidas.Count) { n.Luz.Plano = "personalizado"; foreach (var s in n.Luz.Saidas) s.Plano = null; }
                n.Luz.Ligada = true;
                return Ok(await Trocar(n, "efeito de luz"));
            }
            case "luz.salvarPlano":
            {
                var n = Copia(); var nome = S(a, "nome") ?? throw new ErroSetupHub("Falta o nome do plano.");
                var efeitos = Luz.Resolver(new ConfigLuz { Ligada = true, Plano = n.Luz.Plano, Saidas = n.Luz.Saidas, Planos = n.Luz.Planos });
                var primeiro = efeitos.Values.FirstOrDefault() ?? new EfeitoLuz();
                var novo = new PlanoLuz { Nome = nome, Efeito = Json.Copia(primeiro) };
                foreach (var (id, ef) in efeitos) if (Json.Texto(ef) != Json.Texto(primeiro)) novo.PorSaida[id] = Json.Copia(ef);
                var existente = n.Luz.Planos.FirstOrDefault(x => x.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
                if (existente is { Pronto: true }) throw new ErroSetupHub($"\"{nome}\" é um plano pronto; salve com outro nome.");
                if (existente is not null) n.Luz.Planos.Remove(existente);
                n.Luz.Planos.Add(novo); n.Luz.Plano = nome;
                return Ok(await Trocar(n, "plano de luz salvo"));
            }
            case "luz.excluirPlano":
            {
                var n = Copia(); var nome = S(a, "nome") ?? throw new ErroSetupHub("Falta o nome do plano.");
                var p = n.Luz.Planos.FirstOrDefault(x => x.Nome == nome) ?? throw new ErroSetupHub($"Plano de luz \"{nome}\" não existe.");
                if (p.Pronto) throw new ErroSetupHub($"\"{nome}\" é um plano pronto e não pode ser excluído.");
                var usam = n.Perfis.Where(x => x.Luz?.Plano == nome && x.Nome != n.PerfilAtivo).Select(x => "perfil " + x.Nome).Concat(n.Jogos.Lista.Where(j => j.Luz == nome).Select(j => j.Nome)).ToList();
                if (usam.Count > 0) throw new ErroSetupHub($"O plano \"{nome}\" está em uso: {string.Join(", ", usam)}.");
                var efeitos = Luz.Resolver(new ConfigLuz { Ligada = true, Plano = n.Luz.Plano, Saidas = n.Luz.Saidas, Planos = n.Luz.Planos });
                foreach (var s in n.Luz.Saidas.Where(s => (s.Plano ?? n.Luz.Plano) == nome)) { s.Efeito = Json.Copia(efeitos[s.Id]); s.Plano = "personalizado"; }
                if (n.Luz.Plano == nome) { n.Luz.Plano = "personalizado"; foreach (var s in n.Luz.Saidas.Where(s => s.Plano == "personalizado")) s.Plano = null; }
                n.Luz.Planos.Remove(p);
                return Ok(await Trocar(n, "plano de luz excluído"));
            }
            case "luz.restaurarPlano":
            {
                var n = Copia(); var nome = S(a, "nome") ?? throw new ErroSetupHub("Falta o nome do plano.");
                var fabrica = Padroes.PlanosProntos().FirstOrDefault(x => x.Nome == nome) ?? throw new ErroSetupHub($"\"{nome}\" não é um plano pronto.");
                var i = n.Luz.Planos.FindIndex(x => x.Nome == nome);
                if (i < 0) n.Luz.Planos.Add(fabrica); else n.Luz.Planos[i] = fabrica;
                return Ok(await Trocar(n, "plano de luz de volta ao padrão"));
            }

            case "processador.padrao":
            {
                var n = Copia(); n.Processador = new ConfigProcessador();
                return Ok(await Trocar(n, "processador no padrão"));
            }
            case "processador.assistente":
            {
                if (!cpu.Disponivel) throw new ErroSetupHub("Processador: " + cpu.Motivo);
                var nucleos = Lista(a, "nucleos").Select(int.Parse).ToList();
                if (nucleos.Count == 0) nucleos = Enumerable.Range(0, Math.Max(1, cpu.Nucleos)).ToList();
                int piso = I(a, "piso") ?? -30, seg = I(a, "segundos") ?? 120;
                IniciarTarefa("Assistente de Curve Optimizer", async (prog, ct) =>
                {
                    var res = await estabilidade.Assistente(nucleos, piso, seg, prog, ct);
                    maquina.Assistente.AddRange(res);
                    var finais = res.Where(r => r.Estavel).GroupBy(r => r.Nucleo).Select(g => g.Last()).ToList();
                    var n = Copia();
                    while (n.Processador.CoNucleos.Count < cpu.Nucleos) n.Processador.CoNucleos.Add(n.Processador.CoTodos);
                    foreach (var r in finais) n.Processador.CoNucleos[r.Nucleo] = r.Valor;
                    lock (trava) { GuardaPerfil.NoAtivo(n, config); config = n; }
                    GravarConfig(n);
                    maquina.ProcessadorConfirmado = Json.Copia(n.Processador);
                    maquina.ProcessadorPendente = null;
                    SalvarMaquina();
                    _ = Evento("config", null);
                    return "Gravado com uma etapa de margem: " + string.Join(", ", finais.Select(r => $"núcleo {r.Nucleo} em {r.Valor}"));
                });
                return Ok(tarefa);
            }
            case "placaDeVideo.padrao":
            {
                var n = Copia(); n.PlacaDeVideo = new ConfigPlacaDeVideo(); n.Ventoinhas.Gpu = new VentoinhaGpu();
                var r = await Trocar(n, "placa de vídeo no padrão");
                if (agente is not null) await agente.PedirAsync("agente.fabrica", null, 30000);
                return Ok(r);
            }
            case "placaDeVideo.assistente":
            {
                var ag = agente ?? throw new ErroSetupHub("O assistente da placa de vídeo requer a janela do SetupHub aberta nesta sessão.");
                IniciarTarefa("Assistente de tensão da placa de vídeo", async (prog, ct) =>
                {
                    using var aoCancelar = ct.Register(() => _ = ag.PedirAsync("agente.cancelarAssistente", null, 5000));
                    var resp = await ag.PedirAsync("agente.assistenteGpu", a, 60 * 60 * 1000);
                    ct.ThrowIfCancellationRequested();
                    if (!resp.Ok) throw new ErroSetupHub(resp.Erro ?? "falhou");
                    var tensao = Json.Ler<int>(resp.Dados);
                    var n = Copia(); n.PlacaDeVideo.Tensao = tensao;
                    lock (trava) { GuardaPerfil.NoAtivo(n, config); config = n; }
                    GravarConfig(n);
                    maquina.GpuConfirmada = Json.Copia(n.PlacaDeVideo); maquina.GpuPendente = null; SalvarMaquina();
                    _ = Evento("config", null);
                    return $"Tensão gravada: {tensao} mV, 10 mV acima do último valor aprovado.";
                });
                return Ok(tarefa);
            }
            case "jogos.limparCache":
            {
                var ag = agente ?? throw new ErroSetupHub("A limpeza do cache requer a janela do SetupHub aberta nesta sessão.");
                var r = await ag.PedirAsync("agente.limparCache", null, 60000);
                return r.Ok ? Ok("Cache de sombreadores limpo.") : throw new ErroSetupHub(r.Erro ?? "falhou");
            }

            case "bios.ler":
                await LerBios();
                return Ok(sistema);
            case "tarefa.cancelar":
                tarefaCts?.Cancel();
                return Ok();
            case "sair":
                Registro.Info("Sair pedido pela " + (S(a, "origem") ?? "linha de comando") + ".");
                // Sem avisar a janela, ela religaria o serviço logo depois de "Sair".
                _ = Task.Run(async () => { await Evento("saindo", null); await Task.Delay(200); Parar(false); AoSair?.Invoke(); SairProcesso?.Invoke(); });
                return Ok("O SetupHub está saindo: ventoinhas de volta à BIOS e luz no efeito do controlador.");

            case "agente.registrar":
                _ = Task.Run(() => AgenteEntrou(c));
                return Ok(estado);
            case "agente.gpu":
                gpuAgente = Json.Ler<LeituraGpu>(E(a, "gpu"));
                gpuAgenteEm = Environment.TickCount64;
                fpsAgente = double.TryParse(S(a, "fps"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fps) && fps > 0 ? fps : null;
                if (float.TryParse(S(a, "som"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var som)) somAgenteF = som;
                if (double.TryParse(S(a, "ocioso"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var oc)) SegundosOcioso = oc;
                return Ok();
            case "agente.som":
                if (float.TryParse(S(a, "nivel"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nv)) somAgenteF = nv;
                return Ok();
            case "energia.consumo":
                return Ok(ConsumoHoje());
            case "agente.atualizacoes":
                lock (travaMaquina) { maquina.Atualizacoes = Json.Ler<List<Atualizacao>>(E(a, "lista")) ?? new(); maquina.AtualizacoesEm = Formato.Agora; }
                SalvarMaquina();
                _ = Evento("atualizacoes", null);
                return Ok();
            case "agente.tarefa":
                if (tarefa is not null) { tarefa.Etapa = S(a, "etapa") ?? tarefa.Etapa; if (double.TryParse(S(a, "progresso"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pr)) tarefa.Progresso = pr; }
                return Ok();
        }
        throw new ErroSetupHub($"Comando \"{m.Comando}\" não existe. Veja: setuphub listar comandos");
    }

    public Action? SairProcesso;

    // Wh por hora de hoje; o último valor é o total em kWh.
    double[] ConsumoHoje()
    {
        var r = new double[25];
        if (historico is null) return r;
        var meiaNoite = new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds();
        long agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var sensor in new[] { "cpu.consumo", "gpu.consumo" })
        {
            var s = historico.Serie(sensor, meiaNoite, agora, 5000);
            for (int i = 0; i < s.Valores.Length; i++)
            {
                if (double.IsNaN(s.Valores[i])) continue;
                long ts = s.Inicio + (long)i * s.Passo;
                int hora = (int)((ts - meiaNoite) / 3600);
                if (hora is >= 0 and < 24) r[hora] += s.Valores[i] * s.Passo / 3600.0;
            }
        }
        r[24] = r.Take(24).Sum() / 1000.0;
        return r;
    }

    bool ConectadaOuCalibrada(string id) => maquina.Calibracao.ContainsKey(id) || estado.Ventoinhas.Any(v => v.Id == id && v.Conectada);

    void IniciarTarefa(string nome, Func<Action<string, double>, CancellationToken, Task<string>> f)
    {
        if (tarefa is { Rodando: true }) throw new ErroSetupHub($"Tarefa em andamento: {tarefa.Nome}. Cancele antes com: setuphub cancelar");
        tarefaCts = new CancellationTokenSource();
        var t = new Tarefa { Nome = nome, Etapa = "Começando", Rodando = true };
        tarefa = t;
        var ct = tarefaCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                t.Resultado = await f((etapa, p) => { t.Etapa = etapa; t.Progresso = p; }, ct);
                t.Progresso = 1;
                Registro.Info($"{nome}: {t.Resultado}");
            }
            catch (OperationCanceledException) { t.Resultado = "Cancelada; tudo voltou ao estado anterior."; t.Erro = true; }
            catch (Exception e) { t.Resultado = e is ErroSetupHub ? e.Message : "Falhou: " + e.Message; t.Erro = true; Registro.Erro(nome, e); }
            finally { t.Rodando = false; _ = Evento("tarefa", Json.Elemento(t)); }
        });
    }
}
