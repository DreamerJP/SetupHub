using System.Globalization;
using System.Text;
using System.Text.Json;
using SetupHub.Nucleo;

namespace SetupHub.Nucleo;

// Código de saída: 0 certo, 1 recusado ou erro, 2 sem conexão, 3 alguma área não conferiu por leitura.
public sealed class Interprete
{
    bool json;
    readonly Conexao? canal;
    readonly TextWriter o, erro;
    static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public Interprete(Conexao? canal, TextWriter saida, TextWriter erro) { this.canal = canal; o = saida; this.erro = erro; }

    public static List<string> Palavras(string linha)
    {
        var r = new List<string>();
        var atual = new StringBuilder();
        bool aspas = false, tem = false;
        foreach (var ch in linha)
        {
            if (ch == '"') { aspas = !aspas; tem = true; continue; }
            if (char.IsWhiteSpace(ch) && !aspas) { if (tem) { r.Add(atual.ToString()); atual.Clear(); tem = false; } continue; }
            atual.Append(ch); tem = true;
        }
        if (tem) r.Add(atual.ToString());
        return r;
    }

    public async Task<int> Executar(IEnumerable<string> argumentos)
    {
        var args = argumentos.ToList();
        json = args.Remove("--json");
        if (args.Count == 0 || args[0] is "ajuda" or "--help" or "-h" or "/?") { Ajuda(); return 0; }
        if (canal is null)
        {
            Erro("O SetupHub não está rodando (o serviço não respondeu). Abra o SetupHub ou reinicie o PC.");
            return 2;
        }
        // Forma curta da barra de comando: "perfil jogo" aplica o perfil.
        if (args.Count >= 2 && args[0].Equals("perfil", StringComparison.OrdinalIgnoreCase) && args[1].ToLowerInvariant() is not ("aplicar" or "salvar" or "excluir" or "duplicar" or "renomear" or "restaurar"))
            args.Insert(1, "aplicar");
        try { return await Executar(args); }
        catch (ErroSetupHub e) { Erro(e.Message); return 1; }
        catch (FormatException) { Erro("Número inválido. Veja: setuphub ajuda"); return 1; }
    }

    void Erro(string texto)
    {
        if (json) o.WriteLine(JsonSerializer.Serialize(new Mensagem { Tipo = "resposta", Ok = false, Erro = texto }, SetupHubJson.Default.Mensagem));
        else erro.WriteLine(texto);
    }

    async Task<JsonElement?> Pedir(string comando, JsonElement? args = null, int ms = 120000)
    {
        var r = await canal!.PedirAsync(comando, args, ms);
        if (!r.Ok) throw new ErroSetupHub(r.Erro ?? "O SetupHub recusou sem dizer o motivo.");
        return r.Dados;
    }

    static JsonElement Args(params (string k, object? v)[] pares)
    {
        var sb = new StringBuilder("{");
        bool primeiro = true;
        foreach (var (k, v) in pares)
        {
            if (v is null) continue;
            if (!primeiro) sb.Append(',');
            primeiro = false;
            sb.Append(JsonSerializer.Serialize(k, SetupHubJson.Default.String)).Append(':');
            sb.Append(v switch
            {
                string s => JsonSerializer.Serialize(s, SetupHubJson.Default.String),
                bool b => b ? "true" : "false",
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                List<string> l => JsonSerializer.Serialize(l, SetupHubJson.Default.ListString),
                JsonElement e => e.GetRawText(),
                _ => JsonSerializer.Serialize(v.ToString()!, SetupHubJson.Default.String),
            });
        }
        return JsonDocument.Parse(sb.Append('}').ToString()).RootElement.Clone();
    }

    static T Ler<T>(JsonElement? e) => Json.Ler<T>(e) ?? throw new ErroSetupHub("Resposta vazia do SetupHub.");

    int Saida(JsonElement? dados, Action texto)
    {
        if (json) o.WriteLine(dados?.GetRawText() ?? "null");
        else texto();
        if (dados is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("falhas", out var f) && f.ValueKind == JsonValueKind.Array && f.GetArrayLength() > 0) return 3;
        return 0;
    }

    void Resultado(JsonElement? d)
    {
        var r = Json.Ler<ResultadoAplicar>(d);
        if (r is null) { o.WriteLine("Feito."); return; }
        if (r.Areas.Count == 0) { o.WriteLine("Nada mudou."); return; }
        foreach (var (area, sit) in r.Areas) o.WriteLine($"{area}: {sit}");
    }

    async Task<int> Executar(List<string> a)
    {
        string v = a[0].ToLowerInvariant();
        string Arg(int i) => i < a.Count ? a[i] : throw new ErroSetupHub("Faltou um valor. Veja: setuphub ajuda");
        string? Op(string nome)
        {
            int i = a.IndexOf(nome);
            if (i < 0 || i + 1 >= a.Count) return null;
            var r = a[i + 1]; a.RemoveAt(i + 1); a.RemoveAt(i);
            return r;
        }
        switch (v)
        {
            case "ler": return await Ler(a.Skip(1).ToList());
            case "definir":
            {
                var d = await Pedir("definir", Args(("caminho", Arg(1)), ("valor", string.Join(' ', a.Skip(2)))));
                return Saida(d, () => Resultado(d));
            }
            case "ventoinha":
            {
                if (a.Count < 3) throw new ErroSetupHub("Uso: setuphub ventoinha <nome>... <0-100|bios|curva>");
                var ultimo = a[^1];
                var nomes = a.Skip(1).Take(a.Count - 2).ToList();
                bool numero = int.TryParse(ultimo.TrimEnd('%'), out var pct);
                var d = await Pedir("ventoinha", Args(("ids", nomes), ("valor", numero ? pct : null), ("modo", numero ? null : ultimo.ToLowerInvariant())));
                return Saida(d, () => Resultado(d));
            }
            case "ventoinhas":
            {
                var d = await Pedir("ventoinhas.nivel", Args(("nivel", Arg(1)), ("ids", a.Skip(2).ToList())));
                return Saida(d, () => Resultado(d));
            }
            case "calibrar":
            {
                var d = await Pedir("ventoinhas.calibrar", Args(("ids", a.Skip(1).ToList())));
                return Saida(d, () => o.WriteLine("Calibração começou. Acompanhe com: setuphub ler tarefa"));
            }
            case "luz": return await Luz(a);
            case "aplicar":
            {
                if (a.Count >= 3 && a[1].Equals("perfil", StringComparison.OrdinalIgnoreCase)) { var d = await Pedir("perfil.aplicar", Args(("nome", string.Join(' ', a.Skip(2))))); return Saida(d, () => Resultado(d)); }
                throw new ErroSetupHub("Uso: setuphub aplicar perfil <nome>");
            }
            case "perfil":
            {
                var acao = Arg(1).ToLowerInvariant();
                JsonElement? d = acao switch
                {
                    "aplicar" => await Pedir("perfil.aplicar", Args(("nome", string.Join(' ', a.Skip(2))))),
                    "salvar" => await Pedir("perfil.salvar", Args(("nome", string.Join(' ', a.Skip(2))))),
                    "excluir" => await Pedir("perfil.excluir", Args(("nome", string.Join(' ', a.Skip(2))))),
                    "duplicar" => await Pedir("perfil.duplicar", Args(("nome", string.Join(' ', a.Skip(2))))),
                    "renomear" => await Pedir("perfil.renomear", Args(("nome", Arg(2)), ("novo", Arg(3)))),
                    "restaurar" => await Pedir("perfil.restaurar", Args(("nome", string.Join(' ', a.Skip(2))))),
                    _ => throw new ErroSetupHub("Uso: setuphub perfil aplicar|salvar|excluir|duplicar|restaurar <nome> | renomear <nome> <novo>"),
                };
                return Saida(d, () => { if (acao is "aplicar" or "restaurar") Resultado(d); else o.WriteLine(acao == "duplicar" ? $"Perfil criado: {Json.Ler<string>(d)}" : "Feito."); });
            }
            case "processador":
            {
                var acao = Arg(1).ToLowerInvariant();
                if (acao is "padrao" or "padrão") { var d = await Pedir("processador.padrao"); return Saida(d, () => Resultado(d)); }
                if (acao == "assistente")
                {
                    var piso = Op("--piso"); var seg = Op("--segundos");
                    var d = await Pedir("processador.assistente", Args(("nucleos", a.Skip(2).ToList()), ("piso", piso is null ? null : int.Parse(piso)), ("segundos", seg is null ? null : int.Parse(seg))));
                    return Saida(d, () => o.WriteLine("Assistente começou. Acompanhe com: setuphub ler tarefa"));
                }
                throw new ErroSetupHub("Uso: setuphub processador padrao | assistente [núcleos] [--piso -30] [--segundos 120]");
            }
            case "placa":
            {
                var acao = Arg(1).ToLowerInvariant();
                if (acao is "padrao" or "padrão") { var d = await Pedir("placaDeVideo.padrao"); return Saida(d, () => Resultado(d)); }
                if (acao == "assistente") { var d = await Pedir("placaDeVideo.assistente"); return Saida(d, () => o.WriteLine("Assistente começou. Acompanhe com: setuphub ler tarefa")); }
                throw new ErroSetupHub("Uso: setuphub placa padrao | assistente");
            }
            case "limpar":
            {
                var d = await Pedir("jogos.limparCache");
                return Saida(d, () => o.WriteLine(Json.Ler<string>(d)));
            }
            case "exportar":
            {
                var oque = Arg(1).ToLowerInvariant();
                if (oque == "tudo")
                {
                    var arq = Path.GetFullPath(Arg(2));
                    var d = await Pedir("config");
                    File.WriteAllText(arq, Json.Texto(Ler<Config>(d), true), new UTF8Encoding(false));
                    return Saida(JsonDocument.Parse(JsonSerializer.Serialize(arq, SetupHubJson.Default.String)).RootElement, () => o.WriteLine($"Configuração exportada para {arq}"));
                }
                if (oque is "historico" or "histórico")
                {
                    var arq = Path.GetFullPath(Arg(2));
                    double horas = a.Count > 3 ? double.Parse(a[3].Replace(',', '.'), CultureInfo.InvariantCulture) : 24;
                    long agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var d = await Pedir("historico.exportar", Args(("arquivo", arq), ("de", agora - (long)(horas * 3600)), ("ate", agora)), 300000);
                    var r = Ler<EventoTexto>(d);
                    File.Copy(r.Tipo, arq, true);
                    return Saida(d, () => o.WriteLine($"Histórico exportado para {arq} ({r.Texto} linhas)."));
                }
                throw new ErroSetupHub("Uso: setuphub exportar tudo <arquivo.json> | historico <arquivo.csv> [horas]");
            }
            case "importar":
            {
                var arq = Path.GetFullPath(Arg(1));
                if (!File.Exists(arq)) throw new ErroSetupHub($"Arquivo não encontrado: {arq}");
                Config? cfg;
                try { cfg = Json.DeTexto<Config>(File.ReadAllText(arq)); }
                catch (JsonException e) { throw new ErroSetupHub("O arquivo não é uma configuração válida: " + e.Message); }
                var d = await Pedir("config.gravar", Args(("config", Json.Elemento(cfg!)), ("origem", "importação de " + Path.GetFileName(arq))));
                return Saida(d, () => Resultado(d));
            }
            case "cancelar": { var d = await Pedir("tarefa.cancelar"); return Saida(d, () => o.WriteLine("Cancelamento pedido.")); }
            case "listar": return await Listar(a.Count > 1 ? a[1].ToLowerInvariant() : "comandos");
            case "sair": { var d = await Pedir("sair", Args(("origem", "linha de comando"))); return Saida(d, () => o.WriteLine(Json.Ler<string>(d))); }
        }
        throw new ErroSetupHub($"Comando \"{a[0]}\" não existe. Veja: setuphub ajuda");
    }

    async Task<int> Luz(List<string> a)
    {
        string acao = a.Count > 1 ? a[1].ToLowerInvariant() : "";
        string? Op(string nome)
        {
            int i = a.IndexOf(nome);
            if (i < 0 || i + 1 >= a.Count) return null;
            var r = a[i + 1]; a.RemoveAt(i + 1); a.RemoveAt(i);
            return r;
        }
        JsonElement? d;
        switch (acao)
        {
            case "plano": d = await Pedir("luz.plano", Args(("nome", string.Join(' ', a.Skip(2))))); break;
            case "ligar": d = await Pedir("luz.ligar", Args(("ligada", true))); break;
            case "desligar": d = await Pedir("luz.ligar", Args(("ligada", false))); break;
            case "salvar": d = await Pedir("luz.salvarPlano", Args(("nome", string.Join(' ', a.Skip(2))))); break;
            case "excluir": d = await Pedir("luz.excluirPlano", Args(("nome", string.Join(' ', a.Skip(2))))); break;
            case "restaurar": d = await Pedir("luz.restaurarPlano", Args(("nome", string.Join(' ', a.Skip(2))))); break;
            case "efeito":
            {
                var saidas = Op("--saidas");
                var brilho = Op("--brilho");
                var vel = Op("--velocidade");
                var fonte = Op("--fonte");
                var temps = Op("--temperaturas");
                if (a.Count < 3) throw new ErroSetupHub("Uso: setuphub luz efeito <tipo> [#cor...] [--saidas a,b] [--brilho 0-100] [--velocidade 0-2] [--temperaturas 50,70,90]");
                var cores = a.Skip(3).Where(x => x.StartsWith('#')).ToList();
                var ef = new EfeitoLuz { Tipo = a[2].ToLowerInvariant(), Cores = cores.Count > 0 ? cores : new() { "#FFFFFF" }, ArcoIris = a.Skip(3).Any(x => x.Equals("arco-iris", StringComparison.OrdinalIgnoreCase)) };
                if (ef.Tipo == "temperatura" && cores.Count == 0) ef.Cores = Padroes.CoresTemperatura();
                if (brilho is not null) ef.Brilho = int.Parse(brilho);
                if (vel is not null) ef.Velocidade = int.Parse(vel);
                if (fonte is not null) ef.Fonte = fonte;
                if (temps is not null) ef.Temperaturas = temps.Split(',', StringSplitOptions.TrimEntries).Select(int.Parse).ToList();
                d = await Pedir("luz.efeito", Args(("ids", saidas?.Split(',', StringSplitOptions.TrimEntries).ToList() ?? new List<string>()), ("efeito", JsonSerializer.SerializeToElement(ef, SetupHubJson.Default.EfeitoLuz))));
                break;
            }
            default: throw new ErroSetupHub("Uso: setuphub luz plano <nome> | ligar | desligar | salvar <nome> | efeito <tipo> [#cor...]");
        }
        return Saida(d, () => Resultado(d));
    }

    static string T(double? v) => Formato.Temp(v);

    async Task<int> Ler(List<string> a)
    {
        string area = a.Count > 0 ? a[0].ToLowerInvariant() : "sensores";
        switch (area)
        {
            case "sensores":
            case "estado":
            {
                var d = await Pedir("estado");
                return Saida(d, () =>
                {
                    var e = Ler<Estado>(d);
                    o.WriteLine($"Processador   {T(e.Cpu.Temp)}  {Formato.W(e.Cpu.Consumo)}  {Formato.Mhz(e.Cpu.ClockMax)} máx  uso {Formato.Pct(e.Cpu.Uso)}");
                    o.WriteLine($"Placa vídeo   {T(e.Gpu.Temp)}  ponto quente {T(e.Gpu.TempQuente)}  memória {T(e.Gpu.TempMemoria)}  {Formato.W(e.Gpu.ConsumoTotal ?? e.Gpu.Consumo)}  {Formato.Mhz(e.Gpu.Clock)}  uso {Formato.Pct(e.Gpu.Uso)}");
                    o.WriteLine($"Placa-mãe     sistema {T(e.Placa.Sistema)}  reguladores {T(e.Placa.Vrm)}  chipset {T(e.Placa.Chipset)}");
                    foreach (var dk in e.Discos) o.WriteLine($"Disco         {dk.Nome} {T(dk.Temp)}");
                    o.WriteLine($"Memória       {Formato.N(e.Memoria.UsadaGb, 1)} de {Formato.N(e.Memoria.TotalGb, 1)} GB ({Formato.Pct(e.Memoria.Uso)})");
                    foreach (var vt in e.Ventoinhas.Where(x => x.Conectada)) o.WriteLine($"{vt.Nome,-13} {Formato.Rpm(vt.Rpm)}  {Formato.Pct(vt.Porcentagem)}  ({vt.Modo})");
                    if (e.Gpu.FanRpm is not null) o.WriteLine($"{"Fan GPU",-13} {Formato.Rpm(e.Gpu.FanRpm)}  {Formato.Pct(e.Gpu.FanPorcentagem)}");
                    o.WriteLine($"Perfil        {e.Perfil}{(e.PerfilAlterado ? " (alterado)" : "")}; luz {(e.Luz.Ligada ? e.Luz.Plano : "desligada")}");
                    if (e.Jogo is not null) o.WriteLine($"Jogo          {e.Jogo}  {Formato.N(e.Fps)} fps");
                    foreach (var al in e.Alertas) o.WriteLine($"ALERTA        {al.Texto}");
                    foreach (var av in e.Avisos) o.WriteLine($"Aviso         {av}");
                    if (e.AvisoConfig is not null) o.WriteLine($"Aviso         {e.AvisoConfig}");
                });
            }
            case "ventoinhas":
            {
                var d = await Pedir("estado"); var c = Ler<Config>(await Pedir("config"));
                return Saida(d, () =>
                {
                    var e = Ler<Estado>(d);
                    o.WriteLine($"Nível: {c.Ventoinhas.Nivel}{(c.Ventoinhas.Noite.Ligado ? $"; modo noite {c.Ventoinhas.Noite.Inicio} a {c.Ventoinhas.Noite.Fim}, até {c.Ventoinhas.Noite.Maximo}%" : "")}");
                    foreach (var s in c.Ventoinhas.Saidas)
                    {
                        var l = e.Ventoinhas.FirstOrDefault(x => x.Id == s.Id);
                        string modo = s.Modo switch { "fixo" => $"fixo em {s.Fixo}%", "curva" => $"curva por {string.Join(" e ", s.Fontes)} ({string.Join(" ", s.Curva.Select(p => $"{p.T:0}°C→{p.P:0}%"))})", _ => "curva da BIOS" };
                        o.WriteLine($"{s.Nome,-9} {(l?.Conectada == true ? Formato.Rpm(l.Rpm) : "sem ventoinha"),-11} aplicando {Formato.Pct(l?.Porcentagem),-5} {modo}{(l?.Defeito == true ? "  DEFEITO" : "")}");
                    }
                    o.WriteLine($"{"Fan GPU",-9} {Formato.Rpm(e.Gpu.FanRpm),-11} {(c.Ventoinhas.Gpu.Modo == "curva" ? "curva " + string.Join(" ", c.Ventoinhas.Gpu.Curva.Select(p => $"{p.T:0}°C→{p.P:0}%")) : "automática pelo driver")}");
                    foreach (var g in c.Ventoinhas.Grupos) o.WriteLine($"Grupo \"{g.Nome}\": {string.Join(", ", g.Itens.Select(i => c.Ventoinhas.Saidas.FirstOrDefault(s => s.Id == i)?.Nome ?? i))}");
                });
            }
            case "luz":
            {
                var d = await Pedir("config", Args(("caminho", "luz")));
                return Saida(d, () =>
                {
                    var l = Ler<ConfigLuz>(JsonDocument.Parse(Ler<string>(d)).RootElement);
                    o.WriteLine($"Luz {(l.Ligada ? "ligada" : "desligada")}, plano {l.Plano}");
                    foreach (var s in l.Saidas) o.WriteLine($"  {s.Nome,-9} {s.Leds} LEDs {(s.Ativa ? "" : "(desativada)")}");
                    o.WriteLine("Planos: " + string.Join(", ", l.Planos.Select(p => p.Nome)));
                });
            }
            case "processador":
            {
                var d = await Pedir("estado"); var f = Ler<Faixas>(await Pedir("faixas")); var c = Ler<Config>(await Pedir("config"));
                return Saida(d, () =>
                {
                    var e = Ler<Estado>(d);
                    o.WriteLine($"{T(e.Cpu.Temp)}  {Formato.W(e.Cpu.Consumo)}  {Formato.Mhz(e.Cpu.ClockMax)} máx  uso {Formato.Pct(e.Cpu.Uso)}  {Formato.N(e.Cpu.Tensao, 3)} V");
                    for (int i = 0; i < e.Cpu.Nucleos.Count; i++) { var n = e.Cpu.Nucleos[i]; o.WriteLine($"  núcleo {i}: {Formato.Mhz(n.Clock)} (efetivo {Formato.Mhz(n.ClockEfetivo)}) uso {Formato.Pct(n.Uso)} {Formato.N(n.Consumo, 1)} W"); }
                    Campos(f, "processador.", c.Processador);
                });
            }
            case "placa":
            {
                var d = await Pedir("estado"); var f = Ler<Faixas>(await Pedir("faixas")); var c = Ler<Config>(await Pedir("config"));
                return Saida(d, () =>
                {
                    var e = Ler<Estado>(d);
                    var g = e.Gpu;
                    o.WriteLine($"{g.Nome}: {T(g.Temp)}, ponto quente {T(g.TempQuente)}, memória {T(g.TempMemoria)}, {Formato.W(g.ConsumoTotal ?? g.Consumo)}, {Formato.Mhz(g.Clock)}, memória {Formato.Mhz(g.ClockMemoria)}, uso {Formato.Pct(g.Uso)}, {Formato.N(g.VramUsada, 1)} de {Formato.N(g.VramTotal, 1)} GB");
                    if (f.Gpu is not null) o.WriteLine($"Driver {f.Gpu.Driver}; {(f.Gpu.DeFabrica ? "nos ajustes de fábrica" : "com ajustes")}");
                    Campos(f, "placaDeVideo.", c.PlacaDeVideo);
                });
            }
            case "telas":
            {
                var f = await Pedir("faixas"); var c = Ler<Config>(await Pedir("config"));
                return Saida(f, () =>
                {
                    foreach (var t in Ler<Faixas>(f).Telas) o.WriteLine($"{t.Nome} ({t.Id}): {t.Largura}x{t.Altura} {t.Hz} Hz; aceita {string.Join(", ", t.Suporta)}");
                    foreach (var t in c.Telas.Lista) o.WriteLine($"Ajustes de {t.Nome}: {Json.Texto(t)}");
                });
            }
            case "perfis":
            {
                var c = await Pedir("config");
                return Saida(c, () =>
                {
                    var cfg = Ler<Config>(c);
                    foreach (var p in cfg.Perfis) o.WriteLine($"{(p.Nome == cfg.PerfilAtivo ? "*" : " ")} {p.Nome,-14} áreas: {string.Join(", ", p.Areas)}{(p.Atalho is null ? "" : $"; atalho {p.Atalho}")}{(string.IsNullOrEmpty(p.Horario) ? "" : $"; horário {p.Horario}")}{(p.Programas.Count > 0 ? $"; ao abrir {string.Join(", ", p.Programas)}" : "")}");
                });
            }
            case "jogos":
            {
                var c = await Pedir("config", Args(("caminho", "jogos")));
                return Saida(c, () => o.WriteLine(Ler<string>(c)));
            }
            case "energia":
            {
                var t = await Pedir("config");
                return Saida(t, () =>
                {
                    var c = Ler<Config>(t);
                    o.WriteLine($"Plano: {(c.Energia.Plano is string id ? Padroes.NomePlano(id) : "não mexe")}");
                    foreach (var p in c.Energia.Planos)
                        o.WriteLine($"  {p.Id}: processador {p.ProcessadorMinimo} a {p.ProcessadorMaximo}%, núcleos acordados {p.NucleosMinimos}%, preferência de energia {p.PreferenciaEnergia}, turbo {p.Impulso}, suspensão USB {(p.SuspensaoUsb ? "ligada" : "desligada")}, economia PCIe {p.EconomiaPcie}, tela {p.DesligarTela} min, suspender {p.Suspender} min");
                    foreach (var r in c.Energia.Regras)
                        o.WriteLine($"  {(r.Ligada ? "ligada" : "desligada")}: " + r.Tipo switch
                        {
                            "programa" => $"quando {r.Programa} abrir",
                            "ocioso" => $"depois de {r.Minutos} minutos sem uso",
                            _ => $"das {r.Inicio} às {r.Fim}",
                        } + $", plano {Padroes.NomePlano(r.Plano)}");
                });
            }
            case "alertas":
            {
                var d = await Pedir("estado"); var c = Ler<Config>(await Pedir("config"));
                return Saida(d, () =>
                {
                    var e = Ler<Estado>(d);
                    foreach (var al in e.Alertas) o.WriteLine("ATIVO: " + al.Texto);
                    foreach (var al in c.Alertas) o.WriteLine($"{(al.Ligado ? "ligado " : "desligado")} {al.Tipo} {al.Sensor} {(al.Tipo == "ventoinha-parada" ? "" : $"acima de {al.Acima:0} °C")}");
                });
            }
            case "tarefa":
            {
                var d = await Pedir("estado");
                return Saida(d, () =>
                {
                    var t = Ler<Estado>(d).Tarefa;
                    if (t is null) { o.WriteLine("Nenhuma tarefa rodando."); return; }
                    o.WriteLine($"{t.Nome}: {(t.Rodando ? $"{t.Progresso * 100:0}%, {t.Etapa}" : t.Resultado)}");
                });
            }
            case "config":
            {
                var d = await Pedir("config", a.Count > 1 ? Args(("caminho", a[1])) : null);
                return Saida(d, () => o.WriteLine(a.Count > 1 ? Ler<string>(d) : Json.Texto(Ler<Config>(d), true)));
            }
            case "faixas":
            {
                var d = await Pedir("faixas");
                return Saida(d, () =>
                {
                    var f = Ler<Faixas>(d);
                    var pre = a.Count > 1 ? a[1] : "";
                    foreach (var (k, x) in f.Campos.Where(kv => kv.Key.StartsWith(pre, StringComparison.OrdinalIgnoreCase)).OrderBy(kv => kv.Key))
                        o.WriteLine($"{k}: " + (x.Disponivel ? $"{Validacao.Numero(x.Min)} a {Validacao.Numero(x.Max)} {x.Unidade}, passo {Validacao.Numero(x.Passo)}" + (x.Padrao is double p ? $", padrão {Validacao.Numero(p)}" : "") + (x.Atual is double at ? $", agora {Validacao.Numero(at)}" : "") : "travado (" + x.Motivo + ")"));
                });
            }
            case "sistema":
            {
                var d = await Pedir("sistema");
                return Saida(d, () =>
                {
                    var s = Ler<InfoSistema>(d);
                    o.WriteLine($"Placa: {s.Placa}; BIOS {s.Bios} ({s.BiosData})");
                    o.WriteLine($"Processador: {s.Processador}");
                    o.WriteLine($"Memória: {s.MemoriaPerfil} {string.Join(", ", s.MemoriaTempos)}");
                    o.WriteLine($"Windows: {s.Windows}");
                    o.WriteLine($"Opções da BIOS lidas: {s.OpcoesBios.Count}{(s.BiosLidaEm > 0 ? " em " + DateTimeOffset.FromUnixTimeMilliseconds(s.BiosLidaEm).ToLocalTime().ToString("dd/MM HH:mm", Br) : "")}");
                });
            }
            case "bios":
            {
                bool dif = a.Remove("--diferentes");
                var d = await Pedir("sistema");
                return Saida(d, () =>
                {
                    var s = Ler<InfoSistema>(d);
                    var busca = a.Count > 1 ? string.Join(' ', a.Skip(1)) : "";
                    var l = s.OpcoesBios.Where(x => (busca.Length == 0 || x.Nome.Contains(busca, StringComparison.OrdinalIgnoreCase)) && (!dif || x.Padrao.Length > 0 && x.Padrao != x.Valor)).ToList();
                    foreach (var x in l) o.WriteLine($"{(x.Interfere ? "!" : " ")} {x.Nome}: {x.Valor}{(x.Padrao.Length > 0 && x.Padrao != x.Valor ? $" (padrão {x.Padrao})" : "")}");
                    o.WriteLine($"{l.Count} de {s.OpcoesBios.Count} opções. \"!\" marca as que interferem no SetupHub.");
                });
            }
            case "historico":
            case "histórico":
            {
                var sensor = a.Count > 1 ? a[1] : "cpu.temp";
                double horas = a.Count > 2 ? double.Parse(a[2].Replace(',', '.'), CultureInfo.InvariantCulture) : 1;
                long agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var d = await Pedir("historico", Args(("sensor", sensor), ("de", agora - (long)(horas * 3600)), ("ate", agora), ("pontos", 60)));
                return Saida(d, () =>
                {
                    var s = Ler<SerieHistorico>(d);
                    var vals = s.Valores.Where(x => !double.IsNaN(x)).ToList();
                    if (vals.Count == 0) { o.WriteLine("Sem dados nesse período."); return; }
                    o.WriteLine($"{sensor}: mínimo {vals.Min():0.#}, média {vals.Average():0.#}, máximo {(s.Picos ?? s.Valores).Where(x => !double.IsNaN(x)).Max():0.#} (passo de {s.Passo} s)");
                    for (int i = 0; i < s.Valores.Length; i++)
                        if (!double.IsNaN(s.Valores[i])) o.WriteLine($"  {DateTimeOffset.FromUnixTimeSeconds(s.Inicio + (long)i * s.Passo).ToLocalTime():HH:mm:ss}  {s.Valores[i]:0.#}");
                });
            }
            case "registro":
            {
                var d = await Pedir("registro", Args(("linhas", a.Count > 1 ? int.Parse(a[1]) : 40)));
                return Saida(d, () => { foreach (var l in Ler<List<string>>(d)) o.WriteLine(l); });
            }
            case "atualizacoes":
            case "atualizações":
            {
                var d = await Pedir("atualizacoes");
                return Saida(d, () =>
                {
                    var l = Ler<List<Atualizacao>>(d);
                    if (l.Count == 0) { o.WriteLine("Nenhuma busca feita ainda (a janela do SetupHub busca uma vez por dia)."); return; }
                    foreach (var x in l) o.WriteLine($"{(x.Nova ? "NOVA " : "     ")}{x.Tipo}: {x.Nome} {x.Versao} ({x.Data}) {x.Endereco}");
                });
            }
        }
        throw new ErroSetupHub($"Leitura \"{area}\" não existe. Veja: setuphub ajuda");
    }

    void Campos(Faixas f, string prefixo, object atual)
    {
        var cfg = JsonDocument.Parse(atual switch { ConfigProcessador p => Json.Texto(p), ConfigPlacaDeVideo g => Json.Texto(g), _ => "{}" }).RootElement;
        foreach (var (k, x) in f.Campos.Where(kv => kv.Key.StartsWith(prefixo)).OrderBy(kv => kv.Key))
        {
            var campo = k[prefixo.Length..];
            string valor = cfg.TryGetProperty(campo, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetRawText() : "padrão";
            o.WriteLine($"  {campo,-20} {valor,-8} " + (x.Disponivel ? (x.Max > 1 || x.Min < 0 ? $"faixa {Validacao.Numero(x.Min)} a {Validacao.Numero(x.Max)} {x.Unidade}" : "disponível") + (x.Padrao is double p ? $", fábrica {Validacao.Numero(p)}" : "") + (x.Atual is double at ? $", agora {Validacao.Numero(at)}" : "") : "travado: " + x.Motivo));
        }
    }

    async Task<int> Listar(string qual)
    {
        switch (qual)
        {
            case "comandos": Ajuda(); return 0;
            case "campos":
            {
                var d = await Pedir("campos");
                return Saida(d, () => { foreach (var c in Ler<List<string>>(d)) o.WriteLine(c); });
            }
            case "perfis": return await Ler(new() { "perfis" });
            case "planos":
            {
                var c = Ler<Config>(await Pedir("config"));
                foreach (var p in c.Luz.Planos) o.WriteLine($"{(p.Nome == c.Luz.Plano ? "*" : " ")} {p.Nome,-18} {p.Efeito.Tipo} {string.Join(" ", p.Efeito.Cores)}{(p.Pronto ? "" : " (seu)")}");
                return 0;
            }
            case "sensores":
                foreach (var s in Padroes.SensoresHistorico) o.WriteLine(s);
                return 0;
            case "ventoinhas": return await Ler(new() { "ventoinhas" });
            case "saidas":
            case "saídas": return await Ler(new() { "luz" });
        }
        throw new ErroSetupHub("Uso: setuphub listar comandos|campos|perfis|planos|sensores|ventoinhas|saidas");
    }

    void Ajuda()
    {
        if (json) { o.WriteLine(JsonSerializer.Serialize(Comandos.Lista, SetupHubJson.Default.ListComandoInfo)); return; }
        o.WriteLine("No terminal: setuphub <comando>. Leituras aceitam --json.");
        foreach (var g in Comandos.Lista.GroupBy(c => c.Grupo))
        {
            o.WriteLine();
            o.WriteLine(g.Key);
            foreach (var c in g) o.WriteLine(c.Descricao.Length == 0 ? $"  {c.Uso}" : $"  {c.Uso.PadRight(34)}{c.Descricao}");
        }
        o.WriteLine();
        o.WriteLine("Valores");
        foreach (var (nome, valores) in Comandos.Valores)
        {
            o.WriteLine($"  {nome}");
            var linha = "  ";
            foreach (var v in valores)
            {
                if (linha.Length + 2 + v.Length > 70) { o.WriteLine(linha); linha = "  "; }
                linha += "  " + v;
            }
            o.WriteLine(linha);
        }
        o.WriteLine();
        o.WriteLine("Código de saída: 0 certo, 1 recusado, 2 sem conexão, 3 área não conferida.");
    }
}
