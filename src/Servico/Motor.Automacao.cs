using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using SetupHub.Nucleo;
using SetupHub.Servico.Hardware;

namespace SetupHub.Servico;

sealed partial class Motor
{
    PerfilJogo? jogoAtual;
    string? perfilAntesDoJogo;
    string? perfilAutomatico;
    string? perfilAntesAutomatico;
    string? perfilAntesHorario;
    string ultimoMinuto = "";
    readonly Dictionary<string, long> alertaDesde = new();
    readonly HashSet<string> alertasAvisados = new();

    void Automacao(Config c)
    {
        HashSet<string> abertos;
        try
        {
            abertos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) { try { abertos.Add(p.ProcessName + ".exe"); } catch { } finally { p.Dispose(); } }
        }
        catch { return; }

        var jogo = c.Jogos.Lista.FirstOrDefault(j => abertos.Contains(Path.GetFileName(j.Executavel)));
        if (jogo?.Executavel != jogoAtual?.Executavel)
        {
            var antes = jogoAtual;
            jogoAtual = jogo;
            _ = Task.Run(() => TrocarJogo(antes, jogo));
        }
        else if (jogo is null)
        {
            var prog = c.Perfis.FirstOrDefault(p => p.Programas.Any(x => abertos.Contains(Path.GetFileName(x))));
            if (prog?.Nome != perfilAutomatico)
            {
                if (prog is not null)
                {
                    perfilAntesAutomatico ??= c.PerfilAtivo;
                    perfilAutomatico = prog.Nome;
                    _ = Task.Run(() => AplicarPerfil(prog.Nome, "programa aberto"));
                }
                else if (perfilAutomatico is not null)
                {
                    var volta = perfilAntesAutomatico;
                    perfilAutomatico = null; perfilAntesAutomatico = null;
                    if (volta is not null) _ = Task.Run(() => AplicarPerfil(volta, "programa fechado"));
                }
            }
        }

        var minuto = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (minuto != ultimoMinuto)
        {
            ultimoMinuto = minuto;
            Horarios(c, minuto);
        }
        RegrasEnergia(c, abertos);
    }

    string? planoPorRegra;
    public double SegundosOcioso;

    void RegrasEnergia(Config c, HashSet<string> abertos)
    {
        string? alvo = null;
        foreach (var r in c.Energia.Regras.Where(x => x.Ligada))
        {
            bool ativa = r.Tipo switch
            {
                "programa" => r.Programa is not null && abertos.Contains(Path.GetFileName(r.Programa)),
                "horario" => Horario.Dentro(r.Inicio, r.Fim, DateTime.Now),
                "ocioso" => SegundosOcioso >= r.Minutos * 60,
                _ => false,
            };
            if (ativa) { alvo = r.Plano; break; }
        }
        if (alvo == planoPorRegra) return;
        var antes = planoPorRegra;
        planoPorRegra = alvo;
        var plano = alvo ?? c.Energia.Plano;
        if (plano is null) return;
        _ = Task.Run(() =>
        {
            var f = Energia.Aplicar(new ConfigEnergia { Plano = plano, Planos = c.Energia.Planos });
            Registro.Info($"Plano de energia {(alvo is null ? "de volta para" : "trocado para")} {Padroes.NomePlano(plano)}" + (f.Count > 0 ? ": " + string.Join(", ", f) : "."));
        });
    }

    // A entrada pode ter sido editada com o jogo aberto.
    PerfilJogo? JogoNaConfig(Config c) => jogoAtual is { } j ? c.Jogos.Lista.FirstOrDefault(x => x.Executavel == j.Executavel) ?? j : null;

    (Config? de, string? nivel, ConfigVentoinhas? v) ventoinhasJogo;

    ConfigVentoinhas VentoinhasEfetivas(Config c)
    {
        var nivel = JogoNaConfig(c)?.Ventoinhas;
        if (nivel is null) return c.Ventoinhas;
        var cache = ventoinhasJogo;
        if (ReferenceEquals(cache.de, c) && cache.nivel == nivel) return cache.v!;
        var v = Json.Copia(c.Ventoinhas);
        Padroes.AplicarNivel(c, v, nivel, v.Saidas.Where(s => s.Modo != "bios" || ConectadaOuCalibrada(s.Id)).Select(s => s.Id).Append("gpu"));
        ventoinhasJogo = (c, nivel, v);
        return v;
    }

    ConfigLuz LuzEfetiva(Config c)
    {
        var plano = JogoNaConfig(c)?.Luz;
        if (plano is null || !c.Luz.Planos.Any(p => p.Nome == plano)) return c.Luz;
        var l = Json.Copia(c.Luz);
        l.Plano = plano;
        foreach (var s in l.Saidas) s.Plano = null;
        return l;
    }

    // Campo vazio no envio devolve o valor do driver.
    Ajustes3D GraficosEfetivos(Config c)
    {
        var g = JogoNaConfig(c)?.Graficos;
        if (g is null) return c.Jogos.Global;
        var r = JsonNode.Parse(Json.Texto(c.Jogos.Global))!.AsObject();
        foreach (var (k, v) in JsonNode.Parse(Json.Texto(g))!.AsObject()) if (v is not null) r[k] = v.DeepClone();
        return Json.DeTexto<Ajustes3D>(r.ToJsonString()) ?? c.Jogos.Global;
    }

    int LimiteEfetivo(Config c) => JogoNaConfig(c)?.LimiteQuadros ?? c.Jogos.LimiteQuadros ?? 0;

    async Task TrocarJogo(PerfilJogo? antes, PerfilJogo? agora)
    {
        try
        {
            if (antes is not null)
            {
                Registro.Info($"Jogo fechado: {antes.Nome}.");
                var volta = perfilAntesDoJogo;
                perfilAntesDoJogo = null;
                if (volta is not null && antes.Perfil is not null) await AplicarPerfil(volta, "jogo fechado");
                Config c; lock (trava) c = config;
                if (antes.Processador is not null) await cpu.Aplicar(c.Processador);
                if (antes.Luz is not null && luz.Disponivel) luz.Aplicar(LuzEfetiva(c));
                if (agente is not null)
                    await PedirAgente(new PedidoAgente { Graficos = GraficosEfetivos(c), LimiteQuadros = LimiteEfetivo(c), Ventoinha = antes.Ventoinhas is null ? null : VentoinhasEfetivas(c).Gpu });
            }
            if (agora is not null)
            {
                Registro.Info($"Jogo aberto: {agora.Nome}.");
                Config c; lock (trava) c = config;
                if (agora.Perfil is not null && agora.Perfil != c.PerfilAtivo)
                {
                    perfilAntesDoJogo = c.PerfilAtivo;
                    await AplicarPerfil(agora.Perfil, $"jogo {agora.Nome} aberto");
                    lock (trava) c = config;
                }
                if (agora.Processador is not null) await cpu.Aplicar(agora.Processador);
                if (agora.Luz is not null && luz.Disponivel) luz.Aplicar(LuzEfetiva(c));
                if (agente is not null && (agora.Graficos is not null || agora.LimiteQuadros is not null || agora.Ventoinhas is not null))
                    await PedirAgente(new PedidoAgente { Graficos = GraficosEfetivos(c), LimiteQuadros = LimiteEfetivo(c), Ventoinha = agora.Ventoinhas is null ? null : VentoinhasEfetivas(c).Gpu });
            }
            await Evento("jogo", Json.Elemento(new EventoTexto { Tipo = agora is null ? "fechado" : "aberto", Texto = agora?.Nome ?? antes?.Nome ?? "" }));
        }
        catch (Exception e) { Registro.Erro("Troca de jogo", e); }
    }

    void Horarios(Config c, string minuto)
    {
        foreach (var p in c.Perfis)
        {
            if (string.IsNullOrEmpty(p.Horario)) continue;
            var partes = p.Horario.Split('-', StringSplitOptions.TrimEntries);
            if (partes[0] == minuto && c.PerfilAtivo != p.Nome && jogoAtual is null)
            {
                perfilAntesHorario = c.PerfilAtivo;
                _ = Task.Run(() => AplicarPerfil(p.Nome, $"horário {minuto}"));
            }
            else if (partes.Length > 1 && partes[1] == minuto && c.PerfilAtivo == p.Nome && perfilAntesHorario is not null)
            {
                var volta = perfilAntesHorario;
                perfilAntesHorario = null;
                _ = Task.Run(() => AplicarPerfil(volta, $"fim do horário de {p.Nome}"));
            }
        }
    }

    // Temperatura só alerta depois de 5 s acima do valor.
    void Alertas(Config c, Estado e, Dictionary<string, double> l)
    {
        long agora = Environment.TickCount64;
        foreach (var a in c.Alertas.Where(x => x.Ligado))
        {
            string chave = a.Tipo + ":" + a.Sensor;
            string? texto = null;
            switch (a.Tipo)
            {
                case "temperatura":
                    if (l.TryGetValue(a.Sensor, out var t))
                    {
                        bool ativo = alertasAvisados.Contains(chave) ? t > a.Acima - 3 : t > a.Acima;
                        if (ativo) texto = $"{NomeSensor(a.Sensor)} em {t:0} °C, acima de {a.Acima:0} °C";
                    }
                    break;
                case "ventoinha-parada":
                    var paradas = e.Ventoinhas.Where(v => v.Defeito).Select(v => v.Nome).ToList();
                    if (paradas.Count > 0) texto = $"{string.Join(", ", paradas)} sem girar com o comando ligado";
                    break;
                case "queda-clock":
                    if (e.Cpu.TempMaisQuente >= a.Acima && e.Cpu.Uso > 50) texto = $"Processador em {e.Cpu.TempMaisQuente:0} °C sob carga: o clock cai por temperatura";
                    break;
            }
            if (texto is null) { alertaDesde.Remove(chave); alertasAvisados.Remove(chave); continue; }
            if (!alertaDesde.TryGetValue(chave, out var desde)) { alertaDesde[chave] = desde = agora; }
            if (agora - desde < 5000 && a.Tipo == "temperatura") continue;
            e.Alertas.Add(new AlertaAtivo { Tipo = a.Tipo, Texto = texto, Desde = Formato.Agora - (agora - desde) });
            if (alertasAvisados.Add(chave))
            {
                Registro.Aviso("Alerta: " + texto);
                _ = Evento("alerta", Json.Elemento(new EventoTexto { Tipo = a.Tipo, Texto = texto }));
            }
        }
    }

    static string NomeSensor(string s) => s switch
    {
        "cpu.temp" => "Processador",
        "gpu.temp" => "Placa de vídeo",
        "gpu.quente" => "Ponto mais quente da placa de vídeo",
        "gpu.memoria" => "Memória da placa de vídeo",
        "placa.sistema" => "Sistema",
        "placa.vrm" => "Reguladores",
        "placa.chipset" => "Chipset",
        "ssd.temp" => "SSD",
        _ => s,
    };
}
