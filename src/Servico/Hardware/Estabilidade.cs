using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using SetupHub.Nucleo;

namespace SetupHub.Servico.Hardware;

// Erro de cálculo, queda do Prime95 ou erro de hardware registrado pelo Windows contam como instável.
sealed class Estabilidade
{
    readonly Processador cpu;
    readonly Func<Estado> estado;
    public Estabilidade(Processador cpu, Func<Estado> estado) { this.cpu = cpu; this.estado = estado; }

    static string PastaPrime => Path.Combine(Caminhos.Dados, "prime95");

    static void PrepararPrime()
    {
        var origem = Path.Combine(Caminhos.Kits, "prime95");
        if (!File.Exists(Path.Combine(origem, "prime95.exe"))) throw new ErroSetupHub("A cópia do Prime95 não está na pasta do SetupHub (reinstale o SetupHub).");
        Directory.CreateDirectory(PastaPrime);
        foreach (var arq in Directory.GetFiles(origem))
        {
            var dest = Path.Combine(PastaPrime, Path.GetFileName(arq));
            if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(arq).Length) File.Copy(arq, dest, true);
        }
        // FFT pequena: mais calor e clock no núcleo.
        File.WriteAllText(Path.Combine(PastaPrime, "prime.txt"), string.Join("\r\n",
            "StressTester=1", "UsePrimenet=0", "TortureMem=0", "TortureTime=1", "TortureThreads=1", "TortureHyperthreading=0",
            "MinTortureFFT=36", "MaxTortureFFT=248", "TortureWeak=0", "ResultsFile=results.txt", "NumWorkers=1", "CoresPerTest=1",
            "EnableSetAffinity=0", "WGUID_version=2", "", "[Internals]", "V30OptionsConverted=1", ""));
        try { File.Delete(Path.Combine(PastaPrime, "results.txt")); } catch { }
    }

    public async Task<(bool ok, double clock, string motivo)> Testar(int nucleo, int segundos, CancellationToken ct)
    {
        PrepararPrime();
        var resultados = Path.Combine(PastaPrime, "results.txt");
        var inicio = DateTime.Now;
        var psi = new ProcessStartInfo(Path.Combine(PastaPrime, "prime95.exe"), "-t") { WorkingDirectory = PastaPrime, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi) ?? throw new ErroSetupHub("O Prime95 não abriu.");
        int logicos = Environment.ProcessorCount;
        int porNucleo = cpu.Nucleos > 0 && logicos >= cpu.Nucleos * 2 ? 2 : 1;
        long mascara = 1L << (nucleo * porNucleo);
        try { p.ProcessorAffinity = (IntPtr)mascara; p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        var clocks = new List<double>();
        string? motivo = null;
        try
        {
            var relogio = Stopwatch.StartNew();
            while (relogio.Elapsed.TotalSeconds < segundos)
            {
                await Task.Delay(1000, ct);
                if (p.HasExited) { motivo = $"o Prime95 parou sozinho (código {p.ExitCode})"; break; }
                var e = estado();
                if (nucleo < e.Cpu.Nucleos.Count && e.Cpu.Nucleos[nucleo].ClockEfetivo is double c) clocks.Add(c);
                if (e.Cpu.TempMaisQuente > 92) { motivo = "o processador passou de 92 °C"; break; }
                if (File.Exists(resultados))
                {
                    string texto;
                    try { texto = await File.ReadAllTextAsync(resultados, ct); } catch { continue; }
                    if (texto.Contains("FATAL ERROR", StringComparison.OrdinalIgnoreCase) || texto.Contains("Hardware failure", StringComparison.OrdinalIgnoreCase)
                        || texto.Contains("ROUNDING", StringComparison.OrdinalIgnoreCase) || texto.Contains("SUM(INPUTS)", StringComparison.OrdinalIgnoreCase) || texto.Contains("TORTURE TEST FAILED", StringComparison.OrdinalIgnoreCase))
                    { motivo = "o Prime95 encontrou erro de cálculo"; break; }
                }
                if (ErroHardware(inicio) is string w) { motivo = w; break; }
            }
        }
        finally
        {
            try { if (!p.HasExited) { p.Kill(true); p.WaitForExit(5000); } } catch { }
        }
        double media = clocks.Count > 3 ? clocks.Skip(2).Average() : 0;
        return (motivo is null, media, motivo ?? "");
    }

    static string? ErroHardware(DateTime desde)
    {
        try
        {
            var q = new EventLogQuery("System", PathType.LogName, $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[@SystemTime>='{desde.ToUniversalTime():o}']]]");
            using var r = new EventLogReader(q);
            using var ev = r.ReadEvent();
            return ev is null ? null : $"o Windows registrou erro de hardware (evento {ev.Id})";
        }
        catch { return null; }
    }

    // Uma etapa de folga: o Prime95 em carga total não pega a instabilidade em carga leve.
    public async Task<List<ResultadoCo>> Assistente(IReadOnlyList<int> nucleos, int piso, int segundos, Action<string, double> progresso, CancellationToken ct)
    {
        var faixa = (min: -50, max: 30);
        piso = Math.Clamp(piso, faixa.min, 0);
        var atuais = await cpu.LerCo();
        var res = new List<ResultadoCo>();
        var originais = new Dictionary<int, int>();
        int i = 0;
        try
        {
            foreach (var n in nucleos)
            {
                ct.ThrowIfCancellationRequested();
                int inicio = n < atuais.Count && atuais[n] is int a ? a : 0;
                originais[n] = inicio;
                int estavel = inicio, teto = inicio;
                int confirmacao = Math.Max(20, segundos / 2);
                progresso($"Núcleo {n}: medindo o valor atual ({inicio})", (double)i / nucleos.Count);
                var (okBase, clockBase, motivoBase) = await Testar(n, confirmacao, ct);
                if (!okBase)
                {
                    // O valor de partida já falha: sobe de 5 em 5 até passar ou chegar a zero.
                    Registro.Aviso($"Assistente: núcleo {n} falhou já no valor atual {inicio} ({motivoBase}).");
                    teto = 0;
                    for (int v = Math.Min(0, inicio + 5); ; v = Math.Min(0, v + 5))
                    {
                        await cpu.CoNucleo(n, v);
                        var (ok2, _, _) = await Testar(n, segundos, ct);
                        estavel = v;
                        if (ok2 || v >= 0) break;
                    }
                }
                else
                {
                    for (int v = inicio - 5; v >= piso; v -= 5)
                    {
                        ct.ThrowIfCancellationRequested();
                        progresso($"Núcleo {n}: testando {v} por {segundos} s", (i + (double)(inicio - v) / Math.Max(5, inicio - piso + 5)) / nucleos.Count);
                        if (!await cpu.CoNucleo(n, v)) { Registro.Aviso($"Assistente: o kit não aceitou {v} no núcleo {n}."); break; }
                        var (ok, _, motivo) = await Testar(n, segundos, ct);
                        if (!ok)
                        {
                            Registro.Info($"Assistente: núcleo {n} instável em {v} ({motivo}); volta para {estavel}.");
                            res.Add(new ResultadoCo { Nucleo = n, Valor = v, Estavel = false, Data = Formato.Agora });
                            break;
                        }
                        estavel = v;
                    }
                }
                int gravar = estavel < teto ? Math.Min(teto, estavel + 5) : estavel;
                progresso($"Núcleo {n}: confirmando {gravar}", (i + 0.95) / nucleos.Count);
                await cpu.CoNucleo(n, gravar);
                var (okFinal, clockFinal, _) = await Testar(n, confirmacao, ct);
                while (!okFinal && gravar < teto)
                {
                    gravar = Math.Min(teto, gravar + 5);
                    Registro.Info($"Assistente: núcleo {n} reprovado na confirmação; sobe para {gravar}.");
                    await cpu.CoNucleo(n, gravar);
                    (okFinal, clockFinal, _) = await Testar(n, confirmacao, ct);
                }
                res.Add(new ResultadoCo { Nucleo = n, Valor = gravar, Estavel = okFinal, Data = Formato.Agora });
                Registro.Info($"Assistente: núcleo {n} aprovado até {estavel}, gravado {gravar}; clock sob carga {clockBase:0} → {clockFinal:0} MHz.");
                progresso($"Núcleo {n}: aprovado até {estavel}, gravado {gravar} (clock sob carga {clockBase:0} → {clockFinal:0} MHz)", (double)++i / nucleos.Count);
            }
        }
        catch
        {
            // Qualquer falha devolve o valor de antes: o último testado pode ser instável.
            foreach (var (n, v) in originais) { try { await cpu.CoNucleo(n, v); } catch { } }
            throw;
        }
        return res;
    }
}
