using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SetupHub.Nucleo;

// Valem para qualquer origem (tela, linha de comando, arquivo); faixa não lida do hardware trava o campo.
public static partial class Validacao
{
    // Área sem faixas lidas não aceita mudança em relação à anterior; o que já estava gravado continua.
    public static List<string> Conferir(Config c, Faixas f, EstadoMaquina m, Config? anterior = null)
    {
        var e = new List<string>();
        if (anterior is not null)
        {
            void Travada(string area, string nome, Func<Config, object?> secao, Func<Perfil, object?> dePerfil)
            {
                if (f.AreasLidas.Contains(area)) return;
                string J(object? o) => o switch { null => "", ConfigProcessador x => Json.Texto(x), ConfigPlacaDeVideo x => Json.Texto(x), Ajustes3D x => Json.Texto(x), ConfigTelas x => Json.Texto(x), _ => "" };
                bool mudou = J(secao(c)) != J(secao(anterior));
                foreach (var p in c.Perfis)
                {
                    var velho = anterior.Perfis.FirstOrDefault(x => x.Nome == p.Nome);
                    if (J(dePerfil(p)) != (velho is null ? J(null) : J(dePerfil(velho))) && dePerfil(p) is not null) mudou = true;
                }
                if (mudou) e.Add($"{nome}: as faixas do hardware ainda não foram lidas, então o campo está travado e a mudança foi recusada.");
            }
            Travada("processador", "Processador", x => x.Processador, p => p.Processador);
            Travada("placaDeVideo", "Placa de vídeo", x => x.PlacaDeVideo, p => p.PlacaDeVideo);
            Travada("graficos", "Ajustes gráficos", x => x.Jogos.Global, p => p.Graficos);
            Travada("telas", "Telas", x => x.Telas, p => null);
        }
        Ventoinhas(c.Ventoinhas, m, e, "");
        foreach (var k in c.NiveisVentoinhas.Keys) if (!Padroes.Niveis.Contains(k)) e.Add($"Ventoinhas: nível guardado \"{k}\" não existe.");
        Luz(c.Luz, c.Luz.Planos, e, "");
        Processador(SemTravados(c.Processador, f, "processador", anterior?.Processador ?? c.Processador), f, e, "");
        PlacaDeVideo(SemTravados(c.PlacaDeVideo, f, "placaDeVideo", anterior?.PlacaDeVideo ?? c.PlacaDeVideo), f, e, "");
        Graficos(SemTravados(c.Jogos.Global, f, "graficos", anterior?.Jogos.Global ?? c.Jogos.Global), f, e, "jogos.global");
        if (c.Jogos.LimiteQuadros is int lq && (lq < 20 || lq > 1000)) e.Add($"Limite de quadros {lq}: aceito de 20 a 1000 quadros por segundo, ou vazio para sem limite.");
        foreach (var j in c.Jogos.Lista)
        {
            if (string.IsNullOrWhiteSpace(j.Executavel)) e.Add($"Jogo \"{j.Nome}\": falta o programa.");
            if (j.Graficos is not null) Graficos(SemTravados(j.Graficos, f, "graficos"), f, e, $"jogo {j.Nome}");
            if (j.Processador is not null) Processador(SemTravados(j.Processador, f, "processador"), f, e, $"jogo {j.Nome}: ");
            if (j.LimiteQuadros is int l && (l < 20 || l > 1000)) e.Add($"Jogo \"{j.Nome}\": limite de quadros {l} fora de 20 a 1000.");
            if (j.Perfil is not null && !c.Perfis.Any(p => p.Nome == j.Perfil)) e.Add($"Jogo \"{j.Nome}\": o perfil \"{j.Perfil}\" não existe.");
            if (j.Ventoinhas is not null && !Padroes.Niveis.Contains(j.Ventoinhas)) e.Add($"Jogo \"{j.Nome}\": nível de ventoinhas \"{j.Ventoinhas}\" não existe (silencioso, equilibrado, forte ou maximo).");
            if (j.Luz is not null && !c.Luz.Planos.Any(p => p.Nome == j.Luz)) e.Add($"Jogo \"{j.Nome}\": o plano de luz \"{j.Luz}\" não existe.");
            if (j.Cor is not null && !CorValida(j.Cor)) e.Add($"Jogo \"{j.Nome}\": cor \"{j.Cor}\" inválida.");
        }
        Sobreposicao(c.Jogos.Sobreposicao, e);
        Telas(c.Telas, f, e);
        Energia(c.Energia, f, e, "");
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in c.Perfis)
        {
            if (string.IsNullOrWhiteSpace(p.Nome)) { e.Add("Há um perfil sem nome."); continue; }
            if (!nomes.Add(p.Nome)) e.Add($"Há dois perfis com o nome \"{p.Nome}\".");
            var pre = $"Perfil \"{p.Nome}\": ";
            if (p.Ventoinhas is not null) Ventoinhas(p.Ventoinhas, m, e, pre);
            if (p.Luz is not null) Luz(p.Luz, c.Luz.Planos.Concat(p.Luz.Planos).ToList(), e, pre);
            if (p.Processador is not null)
            {
                var pr = SemTravados(p.Processador, f, "processador");
                pr.Ppt = Padroes.Ppt(f, pr.Ppt);
                if (pr.Ppt < 0) pr.Ppt = null;
                Processador(pr, f, e, pre);
            }
            if (p.PlacaDeVideo is not null) PlacaDeVideo(SemTravados(p.PlacaDeVideo, f, "placaDeVideo"), f, e, pre);
            if (p.Graficos is not null) Graficos(SemTravados(p.Graficos, f, "graficos"), f, e, pre.TrimEnd(' ', ':'));
            if (p.Energia is not null) Energia(p.Energia, f, e, pre);
            if (p.Atalho is not null && p.Atalho.Length > 0 && !AtalhoValido(p.Atalho)) e.Add(pre + $"atalho \"{p.Atalho}\" não reconhecido (exemplo: Ctrl+Alt+1).");
            if (p.Horario is not null && p.Horario.Length > 0 && !p.Horario.Split('-', StringSplitOptions.TrimEntries).All(HorarioValido)) e.Add(pre + $"horário \"{p.Horario}\" inválido (exemplo: 22:30-07:00).");
            foreach (var a in p.Areas)
                if (!AreasPerfil.Contains(a)) e.Add(pre + $"área \"{a}\" não existe. Áreas: {string.Join(", ", AreasPerfil)}.");
        }
        if (!c.Perfis.Any(p => p.Nome == c.PerfilAtivo) && c.Perfis.Count > 0) e.Add($"O perfil ativo \"{c.PerfilAtivo}\" não existe.");
        foreach (var a in c.Alertas)
        {
            if (a.Tipo is not ("temperatura" or "ventoinha-parada" or "queda-clock")) e.Add($"Alerta de tipo \"{a.Tipo}\" não existe (temperatura, ventoinha-parada, queda-clock).");
            if (a.Tipo == "temperatura" && (a.Acima < 20 || a.Acima > 120)) e.Add($"Alerta de {a.Sensor} acima de {a.Acima} °C: aceito de 20 a 120 °C.");
            if (a.Tipo == "queda-clock" && (a.Acima < 50 || a.Acima > 110)) e.Add($"Alerta de queda de clock com {a.Acima} °C: aceito de 50 a 110 °C.");
        }
        Aparencia(c.Aparencia, e);
        return e;
    }

    public static readonly string[] AreasPerfil = { "ventoinhas", "processador", "placaDeVideo", "luz", "energia", "graficos", "sobreposicao" };

    static void Ventoinhas(ConfigVentoinhas v, EstadoMaquina m, List<string> e, string pre)
    {
        var ids = new HashSet<string>();
        foreach (var s in v.Saidas)
        {
            var nome = string.IsNullOrEmpty(s.Nome) ? s.Id : s.Nome;
            if (!Padroes.Ventoinhas.Any(x => x.Id == s.Id)) { e.Add(pre + $"ventoinha \"{s.Id}\" não existe nesta placa."); continue; }
            if (!ids.Add(s.Id)) e.Add(pre + $"ventoinha \"{nome}\" aparece duas vezes.");
            bool cpu = s.Id == "cpu";
            m.Calibracao.TryGetValue(s.Id, out var cal);
            int minimo = cpu ? Padroes.MinimoCpu(cal) : 0;
            // O valor fixo é conferido mesmo fora do modo fixo: guardado errado, viraria problema ao trocar de modo.
            if (s.Fixo < minimo || s.Fixo > 100) e.Add(pre + $"{nome} em {s.Fixo}%: aceito de {minimo}% a 100%" + (cpu ? " (a do processador nunca para)." : "."));
            switch (s.Modo)
            {
                case "bios": break;
                case "fixo": break;
                case "curva":
                    Curva(s.Curva, nome, pre, e, minimo, cpu ? Padroes.CriticaCpu : null, 2, 16);
                    if (s.Fontes.Count == 0) e.Add(pre + $"{nome}: escolha ao menos uma fonte de temperatura.");
                    foreach (var fo in s.Fontes) if (!FontesTemperatura.Contains(fo)) e.Add(pre + $"{nome}: fonte \"{fo}\" não existe ({string.Join(", ", FontesTemperatura)}).");
                    if (s.Combinar is not ("maior" or "media")) e.Add(pre + $"{nome}: combinar \"{s.Combinar}\" não existe (maior ou media).");
                    if (s.Suavizacao < 0 || s.Suavizacao > 30) e.Add(pre + $"{nome}: suavização de {s.Suavizacao} s, aceito de 0 a 30 s.");
                    if (s.Histerese < 0 || s.Histerese > 10) e.Add(pre + $"{nome}: histerese de {s.Histerese} °C, aceito de 0 a 10 °C.");
                    if (s.PararAbaixo is int pa)
                    {
                        if (cpu) e.Add(pre + "a ventoinha do processador nunca para: parada abaixo de uma temperatura não é aceita nela.");
                        else if (pa < 20 || pa > 60) e.Add(pre + $"{nome}: parada abaixo de {pa} °C, aceito de 20 a 60 °C.");
                    }
                    break;
                default: e.Add(pre + $"{nome}: modo \"{s.Modo}\" não existe (bios, fixo, curva)."); break;
            }
        }
        if (v.Gpu.Modo is not ("auto" or "curva")) e.Add(pre + $"Fan GPU: modo \"{v.Gpu.Modo}\" não existe (auto, curva).");
        if (v.Gpu.Modo == "curva") Curva(v.Gpu.Curva, "Fan GPU", pre, e, 0, Padroes.CriticaGpu, 5, 5);
        if (v.Gpu.RuidoMinimo is int rm && (rm < 0 || rm > 5000)) e.Add(pre + $"Fan GPU: limite mínimo de ruído {rm} fora da faixa.");
        foreach (var g in v.Grupos)
        {
            if (string.IsNullOrWhiteSpace(g.Nome)) e.Add(pre + "há um grupo sem nome.");
            foreach (var i in g.Itens)
                if (g.Tipo == "ventoinhas" && !Padroes.Ventoinhas.Any(x => x.Id == i) && i != "gpu") e.Add(pre + $"grupo \"{g.Nome}\": ventoinha \"{i}\" não existe.");
                else if (g.Tipo == "luz" && !Padroes.SaidasLuz.Any(x => x.Id == i)) e.Add(pre + $"grupo \"{g.Nome}\": saída de luz \"{i}\" não existe.");
        }
        if (v.Noite.Ligado)
        {
            if (!HorarioValido(v.Noite.Inicio) || !HorarioValido(v.Noite.Fim)) e.Add(pre + "modo noite: horário inválido (exemplo: 23:00).");
            if (v.Noite.Maximo < 20 || v.Noite.Maximo > 100) e.Add(pre + $"modo noite: máximo de {v.Noite.Maximo}%, aceito de 20% a 100%.");
        }
        if (!(Padroes.Niveis.Contains(v.Nivel) || v.Nivel == "personalizado")) e.Add(pre + $"nível \"{v.Nivel}\" não existe.");
    }

    public static readonly string[] FontesTemperatura = { "cpu", "gpu", "gpu.quente", "gpu.memoria", "sistema", "vrm", "chipset", "ssd" };

    static void Curva(List<Ponto> c, string nome, string pre, List<string> e, int minimo, double? critica, int minPontos, int maxPontos)
    {
        if (c.Count < minPontos || c.Count > maxPontos) { e.Add(pre + $"{nome}: a curva precisa de {(minPontos == maxPontos ? minPontos.ToString() : $"{minPontos} a {maxPontos}")} pontos."); return; }
        for (int i = 0; i < c.Count; i++)
        {
            var p = c[i];
            if (p.T < 0 || p.T > 110) e.Add(pre + $"{nome}: ponto em {p.T} °C fora de 0 a 110 °C.");
            if (p.P < minimo || p.P > 100) e.Add(pre + $"{nome}: ponto com {p.P}% fora de {minimo}% a 100%.");
            if (i > 0 && p.T <= c[i - 1].T) e.Add(pre + $"{nome}: as temperaturas da curva têm que subir de um ponto para o outro.");
            if (i > 0 && p.P < c[i - 1].P) e.Add(pre + $"{nome}: a porcentagem da curva não pode cair quando a temperatura sobe.");
        }
        if (critica is double t && Curvas.Avaliar(c, t) < 99.5)
            e.Add(pre + $"{nome}: a curva tem que chegar a 100% em {t:0} °C (temperatura crítica). Hoje fica em {Curvas.Avaliar(c, t):0}%.");
    }

    static readonly string[] TiposLuz = { "desligado", "fixo", "respiracao", "onda", "chama", "temperatura", "anel", "musica", "meteoro", "carga", "jogo", "horario", "alerta" };
    public static IReadOnlyList<string> EfeitosLuz => TiposLuz;

    // biblioteca: planos que o nome do plano ativo pode usar (no perfil, os da máquina mais os que o perfil trouxer).
    static void Luz(ConfigLuz l, List<PlanoLuz> biblioteca, List<string> e, string pre)
    {
        foreach (var s in l.Saidas)
        {
            if (!Padroes.SaidasLuz.Any(x => x.Id == s.Id)) { e.Add(pre + $"saída de luz \"{s.Id}\" não existe nesta placa."); continue; }
            if (s.Leds < 1 || s.Leds > 180) e.Add(pre + $"{s.Nome}: {s.Leds} LEDs, aceito de 1 a 180.");
            if (s.Plano is not null && s.Plano != "personalizado" && !biblioteca.Any(p => p.Nome == s.Plano)) e.Add(pre + $"{s.Nome}: o plano \"{s.Plano}\" não existe.");
            Efeito(s.Efeito, $"{s.Nome}", pre, e);
        }
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in l.Planos)
        {
            if (string.IsNullOrWhiteSpace(p.Nome)) { e.Add(pre + "há um plano de luz sem nome."); continue; }
            if (!nomes.Add(p.Nome)) e.Add(pre + $"há dois planos de luz com o nome \"{p.Nome}\".");
            Efeito(p.Efeito, $"plano \"{p.Nome}\"", pre, e);
            foreach (var (id, ef) in p.PorSaida)
            {
                if (!Padroes.SaidasLuz.Any(x => x.Id == id)) e.Add(pre + $"plano \"{p.Nome}\": saída \"{id}\" não existe.");
                Efeito(ef, $"plano \"{p.Nome}\", {id}", pre, e);
            }
        }
        if (l.Plano.Length > 0 && l.Plano != "personalizado" && !biblioteca.Any(p => p.Nome == l.Plano)) e.Add(pre + $"o plano de luz \"{l.Plano}\" não existe.");
        if (!CorValida(l.CorAlerta)) e.Add(pre + $"cor de alerta \"{l.CorAlerta}\" inválida (exemplo: #FF3B1F).");
        if (l.NoiteQuente && (!HorarioValido(l.NoiteInicio) || !HorarioValido(l.NoiteFim))) e.Add(pre + "luz da noite: horário inválido.");
    }

    static void Efeito(EfeitoLuz f, string nome, string pre, List<string> e)
    {
        if (!TiposLuz.Contains(f.Tipo)) e.Add(pre + $"{nome}: efeito \"{f.Tipo}\" não existe ({string.Join(", ", TiposLuz)}).");
        if (f.Cores.Count == 0 && f.Tipo != "desligado") e.Add(pre + $"{nome}: escolha ao menos uma cor.");
        if (f.Cores.Count > 4) e.Add(pre + $"{nome}: o controlador aceita até 4 cores.");
        if (f.Tipo == "temperatura" && f.Cores.Count != 3) e.Add(pre + $"{nome}: o efeito de temperatura usa 3 cores, uma para cada faixa.");
        foreach (var cor in f.Cores) if (!CorValida(cor)) e.Add(pre + $"{nome}: cor \"{cor}\" inválida (exemplo: #6FC3E8).");
        if (f.Brilho < 0 || f.Brilho > 100) e.Add(pre + $"{nome}: brilho {f.Brilho}%, aceito de 0% a 100%.");
        if (f.Velocidade < 0 || f.Velocidade > 2) e.Add(pre + $"{nome}: velocidade {f.Velocidade}, aceito 0 (lenta), 1 (média) ou 2 (rápida).");
        if (f.Direcao is not ("fora" or "dentro")) e.Add(pre + $"{nome}: direção \"{f.Direcao}\" não existe (fora ou dentro).");
        if (f.Tipo == "temperatura" && f.Fonte is not ("cpu" or "gpu" or "maior")) e.Add(pre + $"{nome}: a temperatura segue cpu, gpu ou maior, não \"{f.Fonte}\".");
        if (f.Tipo == "carga" && f.Fonte is not ("cpu" or "gpu")) e.Add(pre + $"{nome}: a carga segue cpu ou gpu, não \"{f.Fonte}\".");
        if (f.Temperaturas.Count != 3 || f.Temperaturas.Any(t => t < 20 || t > 110) || f.Temperaturas[0] >= f.Temperaturas[1] || f.Temperaturas[1] >= f.Temperaturas[2])
            e.Add(pre + $"{nome}: as 3 temperaturas vão de 20 a 110 °C, cada uma acima da anterior ({string.Join(", ", f.Temperaturas)}).");
    }

    static bool AreaLida(Faixas f, string chave) => f.AreasLidas.Contains(chave[..Math.Max(0, chave.IndexOf('.'))]);

    // Campo travado agora sai da cópia (com "iguaisA", só se não mudou): o valor guardado não se perde nem bloqueia outras mudanças.
    public static T SemTravados<T>(T valor, Faixas f, string area, T? iguaisA = null) where T : class
    {
        var no = JsonNode.Parse(Json.Texto(valor))!.AsObject();
        var antes = iguaisA is null ? null : JsonNode.Parse(Json.Texto(iguaisA))!.AsObject();
        foreach (var k in no.Select(x => x.Key).ToList())
        {
            var chave = area + "." + (k switch { "coNucleos" => "coTodos", "chillMin" or "chillMax" => "chillFps", _ => k });
            if (!AreaLida(f, chave) || !f.Campos.TryGetValue(chave, out var x) || x.Disponivel) continue;
            if (antes is not null && (no[k]?.ToJsonString() ?? "null") != (antes[k]?.ToJsonString() ?? "null")) continue;
            no[k] = k == "coNucleos" ? new JsonArray() : null;
        }
        return Json.DeTexto<T>(no.ToJsonString())!;
    }

    static void Campo(Faixas f, string chave, double? valor, string nome, string pre, List<string> e)
    {
        if (valor is not double v) return;
        if (!AreaLida(f, chave)) return;
        if (!f.Campos.TryGetValue(chave, out var x) || !x.Disponivel)
        {
            var motivo = x?.Motivo ?? "a faixa não foi lida do hardware";
            e.Add(pre + $"{nome}: campo travado ({motivo}). Deixe vazio para o padrão de fábrica.");
            return;
        }
        if (v < x.Min - 1e-9 || v > x.Max + 1e-9)
            e.Add(pre + $"{nome} {Numero(v)} {x.Unidade}: fora da faixa do hardware, de {Numero(x.Min)} a {Numero(x.Max)} {x.Unidade}.".Replace("  ", " "));
        else if (x.Passo > 0 && Math.Abs((v - x.Min) / x.Passo - Math.Round((v - x.Min) / x.Passo)) > 1e-6)
            e.Add(pre + $"{nome} {Numero(v)}: tem que andar de {Numero(x.Passo)} em {Numero(x.Passo)}.");
    }

    static void Opcao(Faixas f, string chave, object? valor, string nome, string pre, List<string> e)
    {
        if (valor is null) return;
        if (!AreaLida(f, chave)) return;
        if (!f.Campos.TryGetValue(chave, out var x) || !x.Disponivel)
            e.Add(pre + $"{nome}: não disponível nesta máquina ({x?.Motivo ?? "o kit não informou suporte"}).");
    }

    static void Processador(ConfigProcessador p, Faixas f, List<string> e, string pre)
    {
        Opcao(f, "processador.pboEscala", p.PboEscala, "Escala do PBO", pre, e);
        Campo(f, "processador.ppt", p.Ppt, "Limite PPT", pre, e);
        Campo(f, "processador.tdc", p.Tdc, "Limite TDC", pre, e);
        Campo(f, "processador.edc", p.Edc, "Limite EDC", pre, e);
        Campo(f, "processador.limiteTemperatura", p.LimiteTemperatura, "Limite de temperatura", pre, e);
        Campo(f, "processador.coTodos", p.CoTodos, "Curve Optimizer de todos os núcleos", pre, e);
        for (int i = 0; i < p.CoNucleos.Count; i++)
            Campo(f, "processador.coTodos", p.CoNucleos[i], $"Curve Optimizer do núcleo {i}", pre, e);
        if (p.CoNucleos.Count > 0 && f.Nucleos > 0 && p.CoNucleos.Count > f.Nucleos) e.Add(pre + $"Curve Optimizer por núcleo: este processador tem {f.Nucleos} núcleos.");
        Opcao(f, "processador.eco", p.Eco, "Modo Eco", pre, e);
        Campo(f, "processador.nucleosEstacionados", p.NucleosEstacionados, "Núcleos estacionados", pre, e);
        Opcao(f, "processador.smt", p.Smt, "SMT", pre, e);
        Opcao(f, "processador.x3dJogo", p.X3dJogo, "X3D Gaming Mode", pre, e);
        Campo(f, "processador.fmaxDeslocamento", p.FmaxDeslocamento, "Aumento do clock máximo", pre, e);
    }

    static void PlacaDeVideo(ConfigPlacaDeVideo g, Faixas f, List<string> e, string pre)
    {
        Campo(f, "placaDeVideo.clockMin", g.ClockMin, "Clock mínimo", pre, e);
        Campo(f, "placaDeVideo.clockMax", g.ClockMax, "Clock máximo", pre, e);
        if (g.ClockMin is int a && g.ClockMax is int b && b - a < 100) e.Add(pre + $"Clock máximo ({b} MHz) tem que ficar ao menos 100 MHz acima do mínimo ({a} MHz).");
        Campo(f, "placaDeVideo.tensao", g.Tensao, "Tensão", pre, e);
        Campo(f, "placaDeVideo.limiteEnergia", g.LimiteEnergia, "Limite de energia", pre, e);
        Campo(f, "placaDeVideo.clockMemoria", g.ClockMemoria, "Clock da memória de vídeo", pre, e);
        Opcao(f, "placaDeVideo.memoriaRapida", g.MemoriaRapida, "Tempos rápidos da memória", pre, e);
    }

    static void Graficos(Ajustes3D a, Faixas f, List<string> e, string onde)
    {
        var pre = onde.Length > 0 ? onde + ": " : "";
        Opcao(f, "graficos.antiLag", a.AntiLag, "Anti-Lag", pre, e);
        Opcao(f, "graficos.boost", a.Boost, "Boost", pre, e);
        Campo(f, "graficos.boostResolucaoMin", a.BoostResolucaoMin, "Resolução mínima do Boost", pre, e);
        Opcao(f, "graficos.chill", a.Chill, "Chill", pre, e);
        Campo(f, "graficos.chillFps", a.ChillMin, "Chill mínimo", pre, e);
        Campo(f, "graficos.chillFps", a.ChillMax, "Chill máximo", pre, e);
        if (a.ChillMin is int cm && a.ChillMax is int cx && cm > cx) e.Add(pre + "Chill: o mínimo não pode passar do máximo.");
        Opcao(f, "graficos.nitidez", a.Nitidez, "Nitidez de imagem", pre, e);
        Campo(f, "graficos.nitidezNivel", a.NitidezNivel, "Nível da nitidez", pre, e);
        Opcao(f, "graficos.enhancedSync", a.EnhancedSync, "Enhanced Sync", pre, e);
        Opcao(f, "graficos.rsr", a.Rsr, "Radeon Super Resolution", pre, e);
        Campo(f, "graficos.rsrNitidez", a.RsrNitidez, "Nitidez do RSR", pre, e);
        Opcao(f, "graficos.afmf", a.Afmf, "Fluid Motion Frames", pre, e);
        if (a.Vsync is not null && a.Vsync is not ("sempre-desligado" or "desligado-salvo-app" or "ligado-salvo-app" or "sempre-ligado")) e.Add(pre + $"sincronia vertical \"{a.Vsync}\" não existe.");
        Opcao(f, "graficos.vsync", a.Vsync, "Sincronia vertical", pre, e);
        Opcao(f, "graficos.suavizacao", a.Suavizacao, "Suavização de bordas", pre, e);
        Opcao(f, "graficos.anisotropica", a.Anisotropica, "Filtragem de texturas", pre, e);
        Campo(f, "graficos.anisotropicaNivel", a.AnisotropicaNivel, "Nível da filtragem", pre, e);
        Opcao(f, "graficos.tesselacao", a.Tesselacao, "Tesselação", pre, e);
        Campo(f, "graficos.tesselacaoNivel", a.TesselacaoNivel, "Nível da tesselação", pre, e);
        Opcao(f, "graficos.frtc", a.Frtc, "Limite de quadros da AMD", pre, e);
        Campo(f, "graficos.frtcFps", a.FrtcFps, "Limite de quadros da AMD", pre, e);
    }

    static readonly string[] LeiturasSobreposicao = { "fps", "frametime", "cpu.temp", "cpu.uso", "cpu.consumo", "cpu.clock", "gpu.temp", "gpu.quente", "gpu.uso", "gpu.consumo", "gpu.clock", "gpu.vram", "memoria.uso", "hora", "cpu.ventoinha", "gpu.ventoinha" };
    public static IReadOnlyList<string> LeiturasOverlay => LeiturasSobreposicao;
    public static readonly string[] FontesSobreposicao = { "Unispace", "Consolas", "Segoe UI", "Arial", "Verdana", "Tahoma" };

    static void Sobreposicao(Sobreposicao s, List<string> e)
    {
        foreach (var l in s.Leituras) if (!LeiturasSobreposicao.Contains(l)) e.Add($"Sobreposição: leitura \"{l}\" não existe ({string.Join(", ", LeiturasSobreposicao)}).");
        if (s.Posicao is not ("superior-esquerda" or "superior-direita" or "inferior-esquerda" or "inferior-direita")) e.Add($"Sobreposição: posição \"{s.Posicao}\" não existe.");
        if (s.Tamanho < 50 || s.Tamanho > 300) e.Add($"Sobreposição: tamanho {s.Tamanho}%, aceito de 50% a 300%.");
        if (s.Atalho.Length > 0 && !AtalhoValido(s.Atalho)) e.Add($"Sobreposição: atalho \"{s.Atalho}\" não reconhecido.");
        if (s.Layout is not ("linha" or "coluna")) e.Add($"Sobreposição: layout \"{s.Layout}\" não existe (linha ou coluna).");
        if (!CorValida(s.Cor)) e.Add($"Sobreposição: cor \"{s.Cor}\" inválida (exemplo: #FF8000).");
        if (!FontesSobreposicao.Contains(s.Fonte)) e.Add($"Sobreposição: fonte \"{s.Fonte}\" não existe ({string.Join(", ", FontesSobreposicao)}).");
    }

    static void Telas(ConfigTelas t, Faixas f, List<string> e)
    {
        foreach (var d in t.Lista)
        {
            var info = f.Telas.FirstOrDefault(x => x.Id == d.Id);
            var pre = $"Tela \"{(d.Nome.Length > 0 ? d.Nome : d.Id)}\": ";
            if (info is null) { if (f.AdlxDisponivel) e.Add(pre + "não encontrada."); continue; }
            void Sup(object? v, string chave, string nome) { if (v is not null && !info.Suporta.Contains(chave)) e.Add(pre + $"{nome} não é aceito por esta tela."); }
            Sup(d.FreeSync, "freesync", "FreeSync");
            Sup(d.Vsr, "vsr", "Resolução virtual");
            Sup(d.EscalaGpu, "escalaGpu", "Escala pela placa de vídeo");
            Sup(d.ModoEscala, "modoEscala", "Modo de escala");
            Sup(d.EscalaInteira, "escalaInteira", "Escala inteira");
            Sup(d.ProfundidadeCor, "profundidadeCor", "Profundidade de cor");
            Sup(d.FormatoPixel, "formatoPixel", "Formato de pixel");
            Sup(d.CorPersonalizada, "cor", "Cor personalizada");
            Sup(d.VariBright, "variBright", "Vari-Bright");
            Sup(d.AtualizacaoDinamica, "atualizacaoDinamica", "Taxa de atualização dinâmica");
            Sup(d.Gamut, "gamut", "Gamut");
            Sup(d.Gama, "gama", "Gama");
            Campo(f, $"telas.{d.Id}.brilho", d.Brilho, pre + "Brilho", "", e);
            Campo(f, $"telas.{d.Id}.contraste", d.Contraste, pre + "Contraste", "", e);
            Campo(f, $"telas.{d.Id}.saturacao", d.Saturacao, pre + "Saturação", "", e);
            Campo(f, $"telas.{d.Id}.matiz", d.Matiz, pre + "Matiz", "", e);
            Campo(f, $"telas.{d.Id}.temperatura", d.Temperatura, pre + "Temperatura de cor", "", e);
            Campo(f, $"telas.{d.Id}.variBrightNivel", d.VariBrightNivel, pre + "Nível do Vari-Bright", "", e);
            if (d.Hz is double hz && info.Taxas.Count > 0 && !info.Taxas.Any(t => Math.Abs(t - hz) < 0.6)) e.Add(pre + $"{Numero(hz)} Hz não é aceito nesta resolução ({string.Join(", ", info.Taxas.Select(Numero))} Hz).");
            if (d.ProfundidadeCor is int pc && pc is not (6 or 8 or 10 or 12 or 14 or 16)) e.Add(pre + $"profundidade de cor {pc} bits não existe.");
            else if (d.ProfundidadeCor is int pa && info.Profundidades.Count > 0 && !info.Profundidades.Contains(pa)) e.Add(pre + $"{pa} bits de cor não é aceito por esta tela ({string.Join(", ", info.Profundidades)} bits).");
            if (d.FormatoPixel is string fa && info.Formatos.Count > 0 && !info.Formatos.Contains(fa)) e.Add(pre + $"formato de cor \"{fa}\" não é aceito por esta tela ({string.Join(", ", info.Formatos)}).");
            if (d.ModoEscala is not null && d.ModoEscala is not ("preservar" or "tela-cheia" or "centralizado")) e.Add(pre + $"modo de escala \"{d.ModoEscala}\" não existe.");
            if (d.FormatoPixel is not null && d.FormatoPixel is not ("rgb-completo" or "rgb-limitado" or "ycbcr444" or "ycbcr422" or "ycbcr420")) e.Add(pre + $"formato de pixel \"{d.FormatoPixel}\" não existe.");
            if (d.Gama is double g && (g < 0.5 || g > 3.0)) e.Add(pre + $"gama {g} fora de 0,5 a 3,0.");
            if (d.Gamut is not null && d.Gamut is not ("bt709" or "bt601" or "adobe" or "cie" or "bt2020")) e.Add(pre + $"gamut \"{d.Gamut}\" não existe (bt709, bt601, adobe, cie, bt2020).");
        }
    }

    static void Energia(ConfigEnergia en, Faixas f, List<string> e, string pre)
    {
        bool Existe(string? id) => Padroes.PlanosEnergia.Any(x => x.Id == id);
        var ids = string.Join(", ", Padroes.PlanosEnergia.Select(x => x.Id));
        if (en.Plano is not null && !Existe(en.Plano)) e.Add(pre + $"plano de energia \"{en.Plano}\" não existe ({ids}).");
        foreach (var r in en.Regras)
        {
            if (r.Tipo is not ("programa" or "horario" or "ocioso")) { e.Add(pre + $"troca de energia \"{r.Tipo}\" não existe (programa, horario, ocioso)."); continue; }
            if (r.Tipo == "programa" && string.IsNullOrWhiteSpace(r.Programa)) e.Add(pre + "troca de energia por programa sem o programa.");
            if (r.Tipo == "horario" && (!HorarioValido(r.Inicio) || !HorarioValido(r.Fim))) e.Add(pre + $"troca de energia: horário {r.Inicio} a {r.Fim} inválido.");
            if (r.Tipo == "ocioso" && (r.Minutos < 1 || r.Minutos > 600)) e.Add(pre + $"troca de energia: {r.Minutos} min sem uso, aceito de 1 a 600.");
            if (!Existe(r.Plano)) e.Add(pre + $"troca de energia: plano \"{r.Plano}\" não existe ({ids}).");
        }
        foreach (var p in en.Planos)
        {
            if (!Existe(p.Id)) { e.Add(pre + $"plano de energia guardado \"{p.Id}\" não existe ({ids})."); continue; }
            var n = $"plano {Padroes.NomePlano(p.Id)}: ";
            if (p.ProcessadorMinimo < 0 || p.ProcessadorMinimo > 100) e.Add(pre + n + $"estado mínimo do processador {p.ProcessadorMinimo}% fora de 0% a 100%.");
            if (p.ProcessadorMaximo < 5 || p.ProcessadorMaximo > 100) e.Add(pre + n + $"estado máximo do processador {p.ProcessadorMaximo}% fora de 5% a 100%.");
            if (p.ProcessadorMinimo > p.ProcessadorMaximo) e.Add(pre + n + "o estado mínimo do processador não pode passar do máximo.");
            if (p.NucleosMinimos < 0 || p.NucleosMinimos > 100) e.Add(pre + n + $"núcleos sem estacionar {p.NucleosMinimos}% fora de 0% a 100%.");
            if (p.PreferenciaEnergia < 0 || p.PreferenciaEnergia > 100) e.Add(pre + n + $"preferência de energia {p.PreferenciaEnergia} fora de 0 a 100.");
            if (p.Impulso is not ("desligado" or "ligado" or "agressivo" or "eficiente-ligado" or "eficiente-agressivo")) e.Add(pre + n + $"impulso \"{p.Impulso}\" não existe.");
            if (p.EconomiaPcie is not ("desligado" or "moderado" or "maximo")) e.Add(pre + n + $"economia do PCIe \"{p.EconomiaPcie}\" não existe.");
            if (p.DesligarTela < 0 || p.DesligarTela > 300) e.Add(pre + n + $"desligar a tela em {p.DesligarTela} min, aceito de 0 (nunca) a 300.");
            if (p.Suspender < 0 || p.Suspender > 600) e.Add(pre + n + $"suspender em {p.Suspender} min, aceito de 0 (nunca) a 600.");
        }
    }

    static void Aparencia(ConfigAparencia a, List<string> e)
    {
        if (a.Tema is not ("escuro" or "claro" or "windows")) e.Add($"Aparência: tema \"{a.Tema}\" não existe (escuro, claro ou windows).");
        if (!CorValida(a.CorProcessador)) e.Add($"Aparência: cor do processador \"{a.CorProcessador}\" inválida.");
        if (!CorValida(a.CorPlacaDeVideo)) e.Add($"Aparência: cor da placa de vídeo \"{a.CorPlacaDeVideo}\" inválida.");
        if (a.Densidade is not ("compacta" or "confortavel")) e.Add($"Aparência: espaçamento \"{a.Densidade}\" não existe (compacta ou confortavel).");
        if (a.TamanhoTexto < 90 || a.TamanhoTexto > 130 || a.TamanhoTexto % 5 != 0) e.Add($"Aparência: tamanho do texto {a.TamanhoTexto}%, aceito de 90% a 130%, de 5 em 5.");
        foreach (var q in a.Inicio)
            if (!Padroes.Quadros.Any(x => x.Tipo == q.Tipo)) e.Add($"Aparência: quadro \"{q.Tipo}\" da tela inicial não existe.");
    }

    public static bool CorValida(string s) => CorRegex().IsMatch(s);
    public static bool HorarioValido(string s) => TimeSpan.TryParseExact(s, @"hh\:mm", CultureInfo.InvariantCulture, out var t) && t.TotalHours < 24;
    public static bool AtalhoValido(string s)
    {
        var partes = s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length < 2) return false;
        foreach (var p in partes[..^1]) if (p is not ("Ctrl" or "Alt" or "Shift" or "Win")) return false;
        var tecla = partes[^1];
        return tecla.Length == 1 && char.IsLetterOrDigit(tecla[0]) || TeclaRegex().IsMatch(tecla);
    }

    public static string Numero(double v) => v.ToString(Math.Abs(v - Math.Round(v)) < 1e-9 ? "0" : "0.##", CultureInfo.GetCultureInfo("pt-BR"));

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex CorRegex();
    [GeneratedRegex("^F([1-9]|1[0-2])$")] private static partial Regex TeclaRegex();
}

public static class Curvas
{
    public static double Avaliar(IReadOnlyList<Ponto> c, double t)
    {
        if (c.Count == 0) return 100;
        if (t <= c[0].T) return c[0].P;
        for (int i = 1; i < c.Count; i++)
            if (t <= c[i].T)
            {
                var a = c[i - 1]; var b = c[i];
                return a.P + (b.P - a.P) * (t - a.T) / Math.Max(1e-9, b.T - a.T);
            }
        return c[^1].P;
    }
}
