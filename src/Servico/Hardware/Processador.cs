using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SetupHub.Nucleo;

namespace SetupHub.Servico.Hardware;

// A ferramenta do kit leva 0,7 s por chamada: só roda em ajuste ou consulta, nunca no ciclo de leitura.
sealed partial class Processador
{
    public string? Ferramenta { get; private set; }
    public string? Motivo { get; private set; }
    public bool Disponivel => Ferramenta is not null;
    readonly SemaphoreSlim fila = new(1, 1);
    // O que a BIOS deixou ao ligar: é o que "padrão" devolve.
    public ConfigProcessador Inicial { get; private set; } = new();
    // Gravar antes da primeira leitura não teria para onde voltar: "padrão" ainda não é conhecido.
    public bool Lido { get; private set; }
    public ConfigProcessador Gravado { get; private set; } = new();
    public (ConfigProcessador cfg, List<int?> co)? PartidaGuardada;
    public int Nucleos { get; private set; }
    public List<int?> CoInicial { get; private set; } = new();
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public void Localizar()
    {
        var copia = Path.Combine(Caminhos.Kits, "ryzenmaster", "AMDRyzenMasterCLI.exe");
        var kit = @"C:\Program Files\AMD\RyzenMasterSDK\AMDRyzenMasterCLI\bin-prebuilt\AMDRyzenMasterCLI.exe";
        Ferramenta = File.Exists(copia) ? copia : File.Exists(kit) ? kit : null;
        if (Ferramenta is null) Motivo = "o kit AMD Ryzen Master SDK não está instalado";
    }

    public async Task<string> Chamar(params string[] args)
    {
        if (Ferramenta is null) throw new ErroSetupHub("Processador: " + Motivo);
        await fila.WaitAsync();
        try
        {
            var psi = new ProcessStartInfo(Ferramenta)
            {
                WorkingDirectory = Path.GetDirectoryName(Ferramenta)!,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("--api");
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new ErroSetupHub("A ferramenta do kit da AMD não abriu.");
            var saida = p.StandardOutput.ReadToEndAsync();
            var erro = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30000)) { try { p.Kill(true); } catch { } throw new ErroSetupHub("A ferramenta do kit da AMD não respondeu em 30 s."); }
            await Task.WhenAll(saida, erro);
            return saida.Result.Replace("\r", "");
        }
        finally { fila.Release(); }
    }

    static int? Inteiro(double? v) => v is double d ? (int)Math.Round(d) : null;

    static double? Palavra(string texto, string rotulo)
    {
        foreach (var linha in texto.Split('\n'))
        {
            var p = System.Text.RegularExpressions.Regex.Match(linha, $@"\b{rotulo}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!p.Success) continue;
            var m = NumeroRegex().Match(linha, p.Index + p.Length);
            if (m.Success && double.TryParse(m.Value, NumberStyles.Float, Inv, out var v)) return v;
        }
        return null;
    }

    // Valor depois da linha de pontos: "GetPMTableData ..... PPT Current Limit : 300.000000 W".
    static double? Numero(string texto, string rotulo)
    {
        foreach (var linha in texto.Split('\n'))
        {
            int i = linha.IndexOf(rotulo, StringComparison.Ordinal);
            if (i < 0) continue;
            var m = NumeroRegex().Match(linha, i + rotulo.Length);
            if (m.Success && double.TryParse(m.Value, NumberStyles.Float, Inv, out var v)) return v;
        }
        return null;
    }

    static string Resultado(string texto, string api)
    {
        foreach (var linha in texto.Split('\n'))
            if (linha.StartsWith(api, StringComparison.Ordinal))
            {
                int p = linha.LastIndexOf("..", StringComparison.Ordinal);
                return p >= 0 ? linha[(p + 2)..].Trim().TrimStart('.').Trim() : linha[api.Length..].Trim();
            }
        return texto.Trim();
    }

    static bool Falhou(string r) => r.Contains("Failure", StringComparison.OrdinalIgnoreCase) || r.Contains("Unsupported", StringComparison.OrdinalIgnoreCase)
        || r.Contains("Invalid", StringComparison.OrdinalIgnoreCase) || r.Contains("not implemented", StringComparison.OrdinalIgnoreCase) || r.Contains("Deprecated", StringComparison.OrdinalIgnoreCase)
        || r.Contains("Error", StringComparison.OrdinalIgnoreCase) || r.Contains("not supported", StringComparison.OrdinalIgnoreCase);

    async Task<(double min, double max, double passo)?> MinMax(int parametro)
    {
        try
        {
            var r = await Chamar("GetMinMaxStepBuffer", parametro.ToString(Inv));
            // O nome do comando ("GetMinMaxStepBuffer") também contém Min e Max: só vale a palavra solta.
            var mn = Palavra(r, "Min"); var mx = Palavra(r, "Max"); var st = Palavra(r, "Step");
            if (mn is not null && mn == mx) Registro.Aviso($"Kit da AMD, faixa {parametro} sem largura: " + System.Text.RegularExpressions.Regex.Replace(r, @"\s+", " ").Trim());
            if (mn is null || mx is null || mx < mn || r.Contains("IsWritable = No", StringComparison.Ordinal)) return null;
            return (mn.Value, mx.Value, st is > 0 ? st.Value : 1);
        }
        catch { return null; }
    }

    public async Task LerFaixas(Faixas f)
    {
        if (!Disponivel) { Marcar(f, Motivo!); return; }
        try
        {
            var tabela = await Chamar("GetPMTableData");
            var co = await Chamar("GetCurveOptimizerStatus");
            var nucleos = Numero(await Chamar("GetCoreCount"), "GetCoreCount");
            Nucleos = (int)(nucleos ?? 0);
            f.Nucleos = Nucleos;
            var pbo = Resultado(await Chamar("GetPBOScalarStatus"), "GetPBOScalarStatus");
            var eco = Resultado(await Chamar("GetEcoModeStatus"), "GetEcoModeStatus");
            var jogo = Resultado(await Chamar("GetGamingMode"), "GetGamingMode");
            var smtDisp = Resultado(await Chamar("IsSMTToggleAvailable"), "IsSMTToggleAvailable");
            var smt = Resultado(await Chamar("IsSMTEnabled"), "IsSMTEnabled");
            var park = Resultado(await Chamar("GetCorePark"), "GetCorePark");

            double? pptFab = Numero(tabela, "PPT Fused Limit"), pptAtual = Numero(tabela, "PPT Current Limit");
            double? tdcFab = Numero(tabela, "TDC Fused Limit"), tdcAtual = Numero(tabela, "TDC Current Limit");
            double? edcFab = Numero(tabela, "EDC Fused Limit"), edcAtual = Numero(tabela, "EDC Current Limit");
            double? htc = Numero(tabela, "cHTC Current Limit");
            double? fmaxFab = Numero(tabela, "CCLK Fmax Fused Value"), fmaxAtual = Numero(tabela, "CCLK Fmax Current Value");

            // O kit responde em mW e mA.
            var mmPpt = await MinMax(53); var mmTdc = await MinMax(52); var mmEdc = await MinMax(51);
            var mmCo = await MinMax(63); var mmHtc = await MinMax(82); var mmFmax = await MinMax(65); var mmPark = await MinMax(12);
            Faixa? Montar((double, double, double)? mm, double div, double? padrao, double? atual, string unidade, string sem)
            {
                if (mm is not { } x) return new Faixa { Disponivel = false, Motivo = sem, Unidade = unidade, Padrao = padrao, Atual = atual };
                return new Faixa { Min = Math.Round(x.Item1 / div), Max = Math.Round(x.Item2 / div), Passo = Math.Max(1, Math.Round(x.Item3 / div)), Padrao = padrao, Atual = atual, Unidade = unidade };
            }
            f.Campos["processador.ppt"] = Montar(mmPpt, 1000, pptFab, pptAtual, "W", "o kit não informou a faixa do PPT")!;
            f.Campos["processador.tdc"] = Montar(mmTdc, 1000, tdcFab, tdcAtual, "A", "o kit não informou a faixa do TDC")!;
            f.Campos["processador.edc"] = Montar(mmEdc, 1000, edcFab, edcAtual, "A", "o kit não informou a faixa do EDC")!;
            var fco = Montar(mmCo, 1, 0, CoTodos(co), "", "o kit não informou a faixa do Curve Optimizer")!;
            // O kit aceita até -255, valor que não sobe estável em nenhum Zen 5: a faixa útil é de -50 a +30.
            if (fco.Disponivel) { fco.Min = Math.Max(fco.Min, -50); fco.Max = Math.Min(fco.Max, 30); }
            f.Campos["processador.coTodos"] = fco;
            f.Campos["processador.limiteTemperatura"] = Montar(mmHtc, 1, 95, htc, "°C", "o kit não informou a faixa do limite de temperatura")!;
            f.Campos["processador.fmaxDeslocamento"] = Montar(mmFmax, 1, 0, fmaxAtual - fmaxFab, "MHz", "o kit não informou a faixa do clock máximo")!;
            f.Campos["processador.fmax"] = new Faixa { Disponivel = fmaxAtual is > 0, Motivo = "o kit não informou o clock máximo", Padrao = fmaxFab, Atual = fmaxAtual, Unidade = "MHz" };
            f.Campos["processador.nucleosEstacionados"] = Montar(mmPark, 1, 0, Numero(park, ""), "núcleos", "o kit não informou a faixa do estacionamento")!;
            f.Campos["processador.pboEscala"] = new Faixa { Disponivel = !Falhou(pbo), Motivo = Falhou(pbo) ? "o kit não aceita a escala do PBO nesta máquina" : null, Atual = pbo.Contains("Enable") ? 1 : 0 };
            f.Campos["processador.eco"] = new Faixa { Disponivel = !Falhou(eco) && eco.Contains("supported? : Yes"), Motivo = "o modo Eco não é aceito por este processador pelo kit", Atual = eco.Contains("Enabled") ? 1 : 0 };
            f.Campos["processador.x3dJogo"] = new Faixa { Disponivel = !Falhou(jogo), Motivo = "o kit não aceita o X3D Gaming Mode nesta máquina", Atual = jogo.Contains("Enabled") ? 1 : 0 };
            f.Campos["processador.smt"] = new Faixa { Disponivel = smtDisp.Contains("Yes"), Motivo = "o kit não aceita ligar e desligar o SMT", Atual = smt.Contains("Yes") ? 1 : 0 };
            foreach (var k in new[] { "processador.pboEscala", "processador.eco", "processador.x3dJogo", "processador.smt" })
                if (f.Campos[k].Disponivel) f.Campos[k].Motivo = null;
            f.OpcoesProcessador = f.Campos.Where(kv => kv.Key.StartsWith("processador.") && kv.Value.Disponivel).Select(kv => kv.Key[12..]).ToList();

            var agora = new ConfigProcessador
            {
                Ppt = Inteiro(pptAtual), Tdc = Inteiro(tdcAtual), Edc = Inteiro(edcAtual), LimiteTemperatura = Inteiro(htc),
                CoTodos = CoTodos(co), FmaxDeslocamento = fmaxAtual is not null && fmaxFab is not null ? (int)(fmaxAtual - fmaxFab) : null,
                PboEscala = f.Campos["processador.pboEscala"].Disponivel ? pbo.Contains("Enable") : null,
            };
            var coAgora = CoPorNucleo(co, Nucleos);
            if (PartidaGuardada is { } pg)
            {
                // Mesma sessão do Windows: o que está no processador agora foi gravado pelo SetupHub, não é de fábrica.
                Inicial = Json.Copia(pg.cfg);
                CoInicial = pg.co.ToList();
                Gravado = agora;
                Gravado.CoNucleos = coAgora;
            }
            else { Inicial = agora; CoInicial = coAgora; }
            Lido = true;
            if (!f.AreasLidas.Contains("processador")) f.AreasLidas.Add("processador");
            Registro.Info($"Processador: PPT {pptAtual:0} W (fábrica {pptFab:0}), TDC {tdcAtual:0} A, EDC {edcAtual:0} A, Curve Optimizer {(CoTodos(co) is int c0 ? c0.ToString(Inv) : "desligado")}, {Nucleos} núcleos.");
        }
        catch (Exception e)
        {
            Marcar(f, "o kit da AMD não respondeu: " + e.Message);
            Registro.Erro("Leitura das faixas do processador", e);
        }
    }

    static void Marcar(Faixas f, string motivo)
    {
        foreach (var k in new[] { "ppt", "tdc", "edc", "coTodos", "limiteTemperatura", "fmaxDeslocamento", "nucleosEstacionados", "pboEscala", "eco", "x3dJogo", "smt" })
            f.Campos["processador." + k] = new Faixa { Disponivel = false, Motivo = motivo };
    }

    // Saída do kit: "All Core Mode, PSM Offset Value : -5", "... All PSM values are zero" ou "PSM Offset of Core  3 :... -12" por núcleo.
    static int? CoTodos(string status)
    {
        if (status.Contains("All PSM values are zero", StringComparison.OrdinalIgnoreCase)) return 0;
        var m = CoRegex().Match(status);
        return status.Contains("All Core", StringComparison.OrdinalIgnoreCase) && m.Success ? int.Parse(m.Groups[1].Value, Inv) : null;
    }

    static List<int?> CoPorNucleo(string status, int n)
    {
        var r = new List<int?>();
        var todos = CoTodos(status);
        if (todos is not null) { for (int i = 0; i < n; i++) r.Add(todos); return r; }
        for (int i = 0; i < n; i++) r.Add(null);
        foreach (Match m in CoNucleoRegex().Matches(status))
        {
            int i = int.Parse(m.Groups[1].Value, Inv);
            if (i >= 0 && i < n) r[i] = int.Parse(m.Groups[2].Value, Inv);
        }
        return r;
    }

    // Nulo devolve o valor que a BIOS deixou ao ligar.
    public async Task<List<string>> Aplicar(ConfigProcessador alvo)
    {
        var falhas = new List<string>();
        if (!Disponivel) { falhas.Add("processador: " + Motivo); return falhas; }
        async Task Passo(string nome, Func<Task<bool>> f)
        {
            try { if (!await f()) falhas.Add(nome); else Registro.Info($"Processador: {nome} gravado e conferido."); }
            catch (Exception e) { falhas.Add($"{nome} ({e.Message})"); }
        }

        int? Valor(int? desejado, int? inicial) => desejado ?? inicial;

        if (Valor(alvo.Ppt, Inicial.Ppt) is int ppt && ppt != (Gravado.Ppt ?? Inicial.Ppt))
            await Passo($"limite PPT {ppt} W", async () => { await Chamar("SetPPTLimit", ppt.ToString(Inv)); var v = Numero(await Chamar("GetPMTableData"), "PPT Current Limit"); bool ok = v is not null && Math.Abs(v.Value - ppt) < 1.5; if (ok) Gravado.Ppt = ppt; return ok; });
        if (Valor(alvo.Tdc, Inicial.Tdc) is int tdc && tdc != (Gravado.Tdc ?? Inicial.Tdc))
            await Passo($"limite TDC {tdc} A", async () => { await Chamar("SetTDCLimit", tdc.ToString(Inv)); var v = Numero(await Chamar("GetPMTableData"), "TDC Current Limit"); bool ok = v is not null && Math.Abs(v.Value - tdc) < 1.5; if (ok) Gravado.Tdc = tdc; return ok; });
        if (Valor(alvo.Edc, Inicial.Edc) is int edc && edc != (Gravado.Edc ?? Inicial.Edc))
            await Passo($"limite EDC {edc} A", async () => { await Chamar("SetEDCLimit", edc.ToString(Inv)); var v = Numero(await Chamar("GetPMTableData"), "EDC Current Limit"); bool ok = v is not null && Math.Abs(v.Value - edc) < 1.5; if (ok) Gravado.Edc = edc; return ok; });
        if (Valor(alvo.LimiteTemperatura, Inicial.LimiteTemperatura) is int htc && htc != (Gravado.LimiteTemperatura ?? Inicial.LimiteTemperatura))
            await Passo($"limite de temperatura {htc} °C", async () => { await Chamar("SetcHTCLimit", htc.ToString(Inv)); var v = Numero(await Chamar("GetPMTableData"), "cHTC Current Limit"); bool ok = v is not null && Math.Abs(v.Value - htc) < 1.5; if (ok) Gravado.LimiteTemperatura = htc; return ok; });
        if (Valor(alvo.FmaxDeslocamento, Inicial.FmaxDeslocamento) is int fm && fm != (Gravado.FmaxDeslocamento ?? Inicial.FmaxDeslocamento))
            await Passo("clock máximo", async () => { var r = await Chamar("SetAutoOverClockCPU", fm.ToString(Inv)); bool ok = !Falhou(Resultado(r, "SetAutoOverClockCPU")); if (ok) Gravado.FmaxDeslocamento = fm; return ok; });
        if ((alvo.PboEscala ?? Inicial.PboEscala) is bool pbo && pbo != (Gravado.PboEscala ?? Inicial.PboEscala))
            await Passo("escala do PBO", async () => { await Chamar("EnableDisablePBOScalar", pbo ? "1" : "0"); var r = Resultado(await Chamar("GetPBOScalarStatus"), "GetPBOScalarStatus"); bool ok = r.Contains(pbo ? "Enable" : "Disable"); if (ok) Gravado.PboEscala = pbo; return ok; });

        // Curve Optimizer: por núcleo quando há valor próprio em algum núcleo, senão o de todos.
        var desejadoCo = new List<int?>();
        for (int i = 0; i < Nucleos; i++) desejadoCo.Add(i < alvo.CoNucleos.Count && alvo.CoNucleos[i] is int v ? v : alvo.CoTodos ?? (i < CoInicial.Count ? CoInicial[i] : null));
        var gravadoCo = new List<int?>();
        for (int i = 0; i < Nucleos; i++) gravadoCo.Add(i < Gravado.CoNucleos.Count && Gravado.CoNucleos[i] is int g ? g : i < CoInicial.Count ? CoInicial[i] : null);
        if (Nucleos > 0 && !desejadoCo.SequenceEqual(gravadoCo))
        {
            await Passo("Curve Optimizer", async () =>
            {
                if (desejadoCo.All(x => x == desejadoCo[0]) && desejadoCo[0] is int todos)
                    await Chamar("SetCurveOptimizerForAllCores", todos.ToString(Inv));
                else
                    for (int i = 0; i < Nucleos; i++)
                        if (desejadoCo[i] is int v && v != gravadoCo[i]) await Chamar("SetCurveOptimizerForEachCore", "0", i.ToString(Inv), v.ToString(Inv));
                var lido = CoPorNucleo(await Chamar("GetCurveOptimizerStatus"), Nucleos);
                bool ok = lido.Count == Nucleos && Enumerable.Range(0, Nucleos).All(i => desejadoCo[i] is null || lido[i] == desejadoCo[i]);
                if (ok) Gravado.CoNucleos = desejadoCo.ToList();
                return ok;
            });
        }
        if (alvo.Smt is bool smt && smt != Gravado.Smt)
            await Passo("SMT", async () => { var r = Resultado(await Chamar("EnableDisableSMT", smt ? "1" : "0"), "EnableDisableSMT"); bool ok = !Falhou(r); if (ok) Gravado.Smt = smt; return ok; });
        if (alvo.X3dJogo is bool x3 && x3 != Gravado.X3dJogo)
            await Passo("X3D Gaming Mode", async () => { var r = Resultado(await Chamar("SetGamingMode", x3 ? "1" : "0"), "SetGamingMode"); bool ok = !Falhou(r); if (ok) Gravado.X3dJogo = x3; return ok; });
        if (alvo.Eco is bool eco && eco != Gravado.Eco)
            await Passo("modo Eco", async () => { var r = Resultado(await Chamar("SetEcoMode", eco ? "1" : "0"), "SetEcoMode"); bool ok = !Falhou(r); if (ok) Gravado.Eco = eco; return ok; });
        if (Valor(alvo.NucleosEstacionados, 0) is int park && park != (Gravado.NucleosEstacionados ?? 0))
            await Passo("estacionamento de núcleos", async () => { var r = Resultado(await Chamar("SetCorePark", park.ToString(Inv)), "SetCorePark"); bool ok = !Falhou(r); if (ok) Gravado.NucleosEstacionados = park; return ok; });
        return falhas;
    }

    public async Task<bool> CoNucleo(int nucleo, int valor)
    {
        await Chamar("SetCurveOptimizerForEachCore", "0", nucleo.ToString(Inv), valor.ToString(Inv));
        var lido = CoPorNucleo(await Chamar("GetCurveOptimizerStatus"), Nucleos);
        bool ok = nucleo < lido.Count && lido[nucleo] == valor;
        if (ok)
        {
            while (Gravado.CoNucleos.Count < Nucleos) Gravado.CoNucleos.Add(Gravado.CoNucleos.Count < CoInicial.Count ? CoInicial[Gravado.CoNucleos.Count] : null);
            Gravado.CoNucleos[nucleo] = valor;
        }
        return ok;
    }

    public async Task<List<int?>> LerCo() => CoPorNucleo(await Chamar("GetCurveOptimizerStatus"), Nucleos);

    [GeneratedRegex(@"-?\d+(\.\d+)?")] private static partial Regex NumeroRegex();
    [GeneratedRegex(@"Offset Value\s*:\s*(-?\d+)")] private static partial Regex CoRegex();
    [GeneratedRegex(@"PSM Offset of Core\s+(\d+)\s*:\.*\s*(-?\d+)")] private static partial Regex CoNucleoRegex();
}
