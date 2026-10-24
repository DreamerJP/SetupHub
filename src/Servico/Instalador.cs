using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Serialization;
using SetupHub.Nucleo;
using Microsoft.Win32;

namespace SetupHub.Servico;

sealed class TrocaPendente
{
    public string Nova { get; set; } = "";
    public string Anterior { get; set; } = "";
    public bool Concluida { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(TrocaPendente))]
internal partial class JsonInstalador : JsonSerializerContext
{
}

static class Instalador
{
    public static readonly string Destino = Caminhos.Instalado;
    static readonly string PastaNova = Destino + ".nova";
    static readonly string PastaAnterior = Destino + ".anterior";
    const string Chave = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + Produto.Nome;
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileExW(string de, string? para, int flags);
    static readonly List<string> log = new();
    readonly record struct EstadoServico(bool Existe, bool EstavaAtivo);
    readonly record struct Troca(string Nova, string Anterior);
    static string ArquivoTroca => Path.Combine(Caminhos.Dados, "troca-pendente.json");

    static void Log(string s)
    {
        log.Add(s);
        Console.WriteLine(s);
    }

    static void GravarLog(string nome)
    {
        try
        {
            Directory.CreateDirectory(Caminhos.Dados);
            File.WriteAllLines(Path.Combine(Caminhos.Dados, nome), log);
        }
        catch { }
    }

    static int Rodar(string exe, string args, int ms = 120000)
    {
        var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(ms)) { try { p.Kill(true); } catch { } return -1; }
        var saida = (o.Result + e.Result).Trim();
        if (p.ExitCode != 0 && saida.Length > 0) Log($"  {Path.GetFileName(exe)} {args}: {saida}");
        return p.ExitCode;
    }

    // Pelo Explorer, para a janela abrir sem privilégio de administrador.
    static void AbrirPainel()
    {
        try
        {
            var inicio = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            inicio.ArgumentList.Add(Path.Combine(Destino, Produto.ExePainel));
            Process.Start(inicio);
        }
        catch { }
    }

    public static int Instalar(string[] args)
    {
        bool trocada = false, trocaTentada = false, tinhaInstalacao = false, configuracaoNovaIniciada = false;
        EstadoServico servicoAtualAntes = default;
        EstadoMaquina? estadoNova = null;
        string? sid = null;
        Troca troca = default;
        bool abrirPainel = !args.Any(a => string.Equals(a, "--nao-abrir", StringComparison.OrdinalIgnoreCase));
        using var exclusao = new System.Threading.Mutex(false, @"Global\SetupHub.Instalacao");
        bool exclusiva = false;
        try
        {
            try { exclusiva = exclusao.WaitOne(0); }
            catch (System.Threading.AbandonedMutexException) { exclusiva = true; }
            if (!exclusiva) { Log("ERRO: outra instalação do SetupHub já está em andamento."); GravarLog("instalacao.txt"); return 3; }
            if (!Environment.IsPrivilegedProcess) { Log("ERRO: a instalação precisa de administrador."); GravarLog("instalacao.txt"); return 5; }
            sid = args.FirstOrDefault(a => a.StartsWith("S-1-", StringComparison.Ordinal));
            if (sid is null) { Log("ERRO: falta a conta do usuário (SID)."); GravarLog("instalacao.txt"); return 2; }
            var origem = AppContext.BaseDirectory;
            Log($"Instalando o {Produto.Nome} de {origem} em {Destino} para a conta {sid}.");

            troca = CriarTroca();
            PrepararNovaPasta(origem, troca);
            servicoAtualAntes = EstadoDoServico(ServicoWindows.Nome);
            PararTudo();
            CopiarPerfis(Destino, troca.Nova);
            trocaTentada = true;
            tinhaInstalacao = AtivarNovaPasta(troca);
            trocada = true;
            Log("Arquivos do programa substituídos por uma instalação limpa.");
            Kits(origem);
            PawnIo();
            KitAmd();

            Directory.CreateDirectory(Caminhos.Dados);
            var m = Ler<EstadoMaquina>(Caminhos.EstadoMaquina) ?? new EstadoMaquina();
            estadoNova = m;
            m.UsuarioSid = sid;
            m.AbrirPainelNoProximoLogin = !abrirPainel;
            configuracaoNovaIniciada = true;
            ServicoLuzMsi(m);
            File.WriteAllText(Caminhos.EstadoMaquina, Json.Texto(m, true));
            ProtegerDados(sid);

            Servico(sid);
            Caminho(true);
            Atalho(true, sid);
            // Quem abre a janela no login é o serviço: o Explorer pulava a entrada de inicialização.
            AbrirComWindows(sid, false);
            RivaTuner(sid);
            Desinstalacao();

            using (var s = new ServiceController(ServicoWindows.Nome))
            {
                // O serviço pode já ter sido religado pela janela aberta; isso não é falha da instalação.
                s.Refresh();
                if (s.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending))
                    try { s.Start(); } catch (InvalidOperationException e) when (e.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 1056 }) { }
                s.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                Log($"Serviço do {Produto.Nome} rodando.");
            }
            FinalizarTroca(tinhaInstalacao, troca);
            trocada = false;
            try { if (File.Exists(Path.Combine(PastaRtss, "RTSS.exe"))) Rodar("schtasks.exe", $"/run /tn \"{Produto.TarefaRivaTuner}\""); }
            catch (Exception e) { Log("Aviso: RivaTuner não iniciou: " + e.Message); }
            if (abrirPainel) AbrirPainel();
            Log("Instalação concluída.");
            GravarLog("instalacao.txt");
            return 0;
        }
        catch (Exception e)
        {
            if (trocada) ReverterTroca(tinhaInstalacao, servicoAtualAntes, troca);
            else
            {
                if (!trocaTentada) ApagarPastaDepois(troca.Nova, "Instalação nova incompleta");
                RestaurarServico(ServicoWindows.Nome, servicoAtualAntes, "Serviço anterior reiniciado.");
            }
            if (!tinhaInstalacao && !servicoAtualAntes.Existe && configuracaoNovaIniciada && sid is not null) LimparInstalacaoIncompleta(sid, estadoNova);
            Log("ERRO: " + e);
            GravarLog("instalacao.txt");
            return 1;
        }
        finally
        {
            if (exclusiva) exclusao.ReleaseMutex();
        }
    }

    static T? Ler<T>(string caminho) where T : class
    {
        try { return File.Exists(caminho) ? Json.DeTexto<T>(File.ReadAllText(caminho)) : null; } catch { return null; }
    }

    static void PararTudo()
    {
        try
        {
            using var s = new ServiceController(ServicoWindows.Nome);
            if (s.Status != ServiceControllerStatus.Stopped)
            {
                s.Stop();
                s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                Log("Serviço anterior parado (ventoinhas devolvidas à BIOS).");
            }
        }
        catch (InvalidOperationException) { }
        foreach (var nome in new[] { Path.GetFileNameWithoutExtension(Produto.ExePainel), Produto.Comando })
            foreach (var p in Process.GetProcessesByName(nome)) { try { p.Kill(true); p.WaitForExit(5000); } catch { } }
        PararRtss();
        EsperarProcessosDaPasta();
    }

    // Ferramentas abertas pelo serviço a partir da pasta a seguram até terminar; sem esperar, a troca da pasta falha.
    static void EsperarProcessosDaPasta()
    {
        var limite = Stopwatch.StartNew();
        while (true)
        {
            var restantes = ProcessosDaPasta();
            if (restantes.Count == 0) return;
            bool fechar = limite.Elapsed.TotalSeconds >= 5;
            foreach (var p in restantes)
            {
                if (fechar) { try { p.Kill(true); p.WaitForExit(5000); Log($"{p.ProcessName} fechado: ainda rodava da pasta do programa."); } catch { } }
                p.Dispose();
            }
            if (fechar) return;
            Thread.Sleep(100);
        }
    }

    static List<Process> ProcessosDaPasta()
    {
        var prefixo = Path.GetFullPath(Destino).TrimEnd('\\') + "\\";
        var r = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            if (p.Id != Environment.ProcessId && CaminhoDoProcesso(p.Id) is string f && f.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)) r.Add(p);
            else p.Dispose();
        }
        return r;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(int acesso, bool herdar, int id);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageNameW(IntPtr processo, int formato, System.Text.StringBuilder nome, ref int tamanho);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    // Ler os módulos de cada processo custa segundos.
    static string? CaminhoDoProcesso(int id)
    {
        var h = OpenProcess(0x1000, false, id);
        if (h == IntPtr.Zero) return null;
        try
        {
            var nome = new System.Text.StringBuilder(1024);
            int tamanho = nome.Capacity;
            return QueryFullProcessImageNameW(h, 0, nome, ref tamanho) ? nome.ToString(0, tamanho) : null;
        }
        finally { CloseHandle(h); }
    }

    static EstadoServico EstadoDoServico(string nome)
    {
        try
        {
            using var s = new ServiceController(nome);
            return new EstadoServico(true, s.Status != ServiceControllerStatus.Stopped);
        }
        catch (InvalidOperationException) { return default; }
    }

    static Troca CriarTroca()
    {
        var id = Guid.NewGuid().ToString("N");
        return new Troca(PastaNova + "-" + id, PastaAnterior + "-" + id);
    }

    static void PrepararNovaPasta(string origem, Troca troca)
    {
        if (MesmoCaminho(origem, Destino)) throw new InvalidOperationException("Use o instalador do SetupHub para reinstalar o programa.");
        RecuperarTrocaIncompleta();
        Directory.CreateDirectory(troca.Nova);
        try
        {
            CopiarPacote(origem, troca.Nova);
            ConferirNovaPasta(troca.Nova);
        }
        catch
        {
            ApagarPastaDepois(troca.Nova, "Instalação nova incompleta");
            throw;
        }
    }

    static bool MesmoCaminho(string a, string b) => Path.GetFullPath(a).TrimEnd('\\').Equals(Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    static void RecuperarTrocaIncompleta()
    {
        if (!TentarLerTrocaPendente(out var pendente))
        {
            Log("Aviso: marcador de troca inválido preservado; nenhuma cópia de segurança foi removida.");
            if (!Directory.Exists(Destino)) throw new IOException("Não foi possível confirmar a recuperação de uma troca interrompida.");
            return;
        }
        if (pendente is not null)
        {
            if (!TrocaPendenteValida(pendente))
            {
                Log("Aviso: marcador de troca inválido preservado; nenhuma cópia de segurança foi removida.");
                if (!Directory.Exists(Destino)) throw new IOException("Não foi possível confirmar a recuperação de uma troca interrompida.");
                return;
            }
            RecuperarTrocaPendente(pendente);
        }
    }

    static bool TentarLerTrocaPendente(out TrocaPendente? pendente)
    {
        pendente = null;
        if (!File.Exists(ArquivoTroca)) return true;
        try
        {
            pendente = JsonSerializer.Deserialize(File.ReadAllText(ArquivoTroca), JsonInstalador.Default.TrocaPendente);
            return pendente is not null;
        }
        catch { return false; }
    }

    static void RecuperarTrocaPendente(TrocaPendente pendente)
    {
        var troca = new Troca(pendente.Nova, pendente.Anterior);
        if (!Directory.Exists(Destino))
        {
            if (!Directory.Exists(troca.Anterior)) throw new IOException("Não foi possível recuperar uma troca interrompida: a instalação anterior não está disponível.");
            Directory.Move(troca.Anterior, Destino);
            ApagarPastaDepois(troca.Nova, "Instalação nova interrompida");
            RemoverTrocaPendente();
            Log("Instalação anterior recuperada depois de uma troca interrompida.");
            return;
        }
        if (pendente.Concluida)
        {
            ApagarPastaDepois(troca.Nova, "Instalação nova pendente");
            ApagarPastaDepois(troca.Anterior, "Instalação anterior pendente");
            RemoverTrocaPendente();
            return;
        }
        if (!Directory.Exists(troca.Anterior))
        {
            ApagarPastaDepois(troca.Nova, "Instalação nova pendente");
            RemoverTrocaPendente();
            Log("Aviso: a cópia anterior não está disponível; a instalação atual foi mantida.");
            return;
        }
        var servicoAntes = EstadoDoServico(ServicoWindows.Nome);
        bool atualMovida = false;
        try
        {
            PararTudo();
            Directory.Move(Destino, troca.Nova);
            atualMovida = true;
            Directory.Move(troca.Anterior, Destino);
            RemoverTrocaPendente();
            ApagarPastaDepois(troca.Nova, "Instalação nova interrompida");
            Log("Instalação anterior recuperada depois de uma troca interrompida.");
        }
        catch (Exception erro)
        {
            if (!Directory.Exists(Destino) && atualMovida && Directory.Exists(troca.Nova))
            {
                try { Directory.Move(troca.Nova, Destino); }
                catch (Exception restauracao) { throw new InvalidOperationException("Não foi possível recuperar nem preservar a instalação interrompida.", new AggregateException(erro, restauracao)); }
            }
            throw;
        }
        finally { RestaurarServico(ServicoWindows.Nome, servicoAntes, "Serviço anterior reiniciado."); }
    }

    static void GravarTrocaPendente(Troca troca, bool concluida = false)
    {
        Directory.CreateDirectory(Caminhos.Dados);
        var temporario = ArquivoTroca + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporario, JsonSerializer.Serialize(new TrocaPendente { Nova = troca.Nova, Anterior = troca.Anterior, Concluida = concluida }, JsonInstalador.Default.TrocaPendente));
            File.Move(temporario, ArquivoTroca, true);
        }
        finally
        {
            try { File.Delete(temporario); } catch { }
        }
    }

    static void RemoverTrocaPendente()
    {
        try { File.Delete(ArquivoTroca); } catch { }
    }

    static bool TrocaPendenteValida(TrocaPendente troca)
    {
        var nova = IdDaTroca(troca.Nova, PastaNova);
        var anterior = IdDaTroca(troca.Anterior, PastaAnterior);
        return nova is not null && string.Equals(nova, anterior, StringComparison.OrdinalIgnoreCase);
    }

    static string? IdDaTroca(string pasta, string basePasta)
    {
        try
        {
            var caminho = Path.GetFullPath(pasta).TrimEnd('\\');
            var baseCompleta = Path.GetFullPath(basePasta).TrimEnd('\\');
            var prefixo = baseCompleta + "-";
            if (!caminho.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetDirectoryName(caminho), Path.GetDirectoryName(baseCompleta), StringComparison.OrdinalIgnoreCase)) return null;
            var id = caminho[prefixo.Length..];
            return Guid.TryParseExact(id, "N", out _) ? id : null;
        }
        catch { return null; }
    }

    static void CopiarPacote(string origem, string destino)
    {
        int n = 0;
        foreach (var arq in Directory.EnumerateFiles(origem, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(destino, Path.GetRelativePath(origem, arq));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(arq, dest, true);
            n++;
        }
        Log($"{n} arquivos preparados numa instalação limpa.");
    }

    static void ConferirNovaPasta(string pasta)
    {
        var obrigatorios = new[] { Produto.ExeServico, Produto.ExePainel, Produto.Comando + ".exe", Path.Combine("kits", "rtss") };
        var faltando = obrigatorios.Where(x => !File.Exists(Path.Combine(pasta, x)) && !Directory.Exists(Path.Combine(pasta, x))).ToList();
        if (faltando.Count > 0) throw new InvalidOperationException("O pacote está incompleto: " + string.Join(", ", faltando) + ".");
        Log("Instalação nova conferida antes da troca.");
    }

    // Os perfis do RivaTuner passam para a instalação nova: ele grava neles os ajustes criados por jogo.
    static void CopiarPerfis(string origem, string destino)
    {
        var de = Path.Combine(origem, "kits", "rtss", "Profiles");
        if (!Directory.Exists(de)) return;
        var para = Path.Combine(destino, "kits", "rtss", "Profiles");
        int n = 0;
        foreach (var arq in Directory.EnumerateFiles(de, "*", SearchOption.AllDirectories))
        {
            var alvo = Path.Combine(para, Path.GetRelativePath(de, arq));
            Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);
            File.Copy(arq, alvo, true);
            n++;
        }
        if (n > 0) Log($"{n} perfis do RivaTuner preservados.");
    }

    static bool AtivarNovaPasta(Troca troca)
    {
        bool tinha = Directory.Exists(Destino);
        try
        {
            if (tinha)
            {
                GravarTrocaPendente(troca);
                Directory.Move(Destino, troca.Anterior);
            }
            Directory.Move(troca.Nova, Destino);
            return tinha;
        }
        catch (Exception erro)
        {
            if (!Directory.Exists(Destino) && tinha && Directory.Exists(troca.Anterior))
            {
                try
                {
                    Directory.Move(troca.Anterior, Destino);
                    RemoverTrocaPendente();
                    Log("Instalação anterior restaurada depois de uma falha na troca.");
                }
                catch (Exception restauracao)
                {
                    throw new InvalidOperationException("Não foi possível concluir a troca nem restaurar a instalação anterior.", new AggregateException(erro, restauracao));
                }
            }
            else if (tinha && !Directory.Exists(Destino))
                throw new InvalidOperationException("A instalação anterior não está disponível para restauração.", erro);
            if (Directory.Exists(Destino)) RemoverTrocaPendente();
            ApagarPastaDepois(troca.Nova, "Instalação nova incompleta");
            throw;
        }
    }

    static void FinalizarTroca(bool tinhaInstalacao, Troca troca)
    {
        if (tinhaInstalacao)
        {
            GravarTrocaPendente(troca, true);
            ApagarPastaDepois(troca.Anterior, "Instalação anterior");
        }
        RemoverTrocaPendente();
    }

    static void ReverterTroca(bool tinhaInstalacao, EstadoServico servicoAtualAntes, Troca troca)
    {
        if (tinhaInstalacao && !Directory.Exists(troca.Anterior))
        {
            Log("ERRO: a instalação anterior não está disponível; a nova foi mantida.");
            return;
        }
        try
        {
            PararTudo();
            bool novaMovida = false;
            if (Directory.Exists(Destino))
            {
                Directory.Move(Destino, troca.Nova);
                novaMovida = true;
            }
            if (tinhaInstalacao && Directory.Exists(troca.Anterior))
            {
                Directory.Move(troca.Anterior, Destino);
                RemoverTrocaPendente();
                Log("Instalação anterior restaurada.");
            }
            if (novaMovida) ApagarPastaDepois(troca.Nova, "Instalação nova que falhou");
            RestaurarServico(ServicoWindows.Nome, servicoAtualAntes, "Serviço anterior reiniciado.");
        }
        catch (Exception e) { Log("ERRO: não foi possível restaurar a instalação anterior: " + e.Message); }
    }

    static void RestaurarServico(string nome, EstadoServico antes, string sucesso)
    {
        if (!antes.Existe || !antes.EstavaAtivo) return;
        try
        {
            using var s = new ServiceController(nome);
            s.Refresh();
            if (s.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)) s.Start();
            s.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            Log(sucesso);
        }
        catch (Exception e) { Log("Aviso: o serviço anterior não reiniciou: " + e.Message); }
    }

    static void LimparInstalacaoIncompleta(string sid, EstadoMaquina? estadoNova)
    {
        try { DevolverServicosLuz(estadoNova ?? Ler<EstadoMaquina>(Caminhos.EstadoMaquina)); } catch { }
        try { AbrirComWindows(sid, false); } catch { }
        try { Caminho(false); } catch { }
        try { Atalho(false); } catch { }
        try { Rodar("schtasks.exe", $"/delete /f /tn \"{Produto.TarefaRivaTuner}\""); } catch { }
        try { Rodar("schtasks.exe", $"/delete /f /tn \"{Produto.TarefaTelaRivaTuner}\""); } catch { }
        try { RemoverRivaTuner(); } catch { }
        try { Rodar("sc.exe", $"delete {ServicoWindows.Nome}"); } catch { }
        try { Registry.LocalMachine.DeleteSubKeyTree(Chave, false); } catch { }
        try { Hardware.Energia.RemoverPlanos(); } catch { }
        Log("Registros da instalação incompleta removidos.");
    }

    static void ApagarPastaDepois(string pasta, string nome)
    {
        if (!Directory.Exists(pasta)) return;
        try
        {
            Directory.Delete(pasta, true);
            Log($"{nome} removida.");
        }
        catch
        {
            // Biblioteca do RivaTuner presa num programa aberto: o Windows apaga o resto no próximo reinício.
            try
            {
                foreach (var arq in Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories).ToList()) MoveFileExW(arq, null, 4);
                foreach (var dir in Directory.EnumerateDirectories(pasta, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length).ToList()) MoveFileExW(dir, null, 4);
                MoveFileExW(pasta, null, 4);
            }
            catch { }
            Log($"{nome} está em uso e será removida na próxima reinicialização.");
        }
    }

    static void Kits(string origem)
    {
        var kits = Path.Combine(Destino, "kits");
        Directory.CreateDirectory(kits);
        var msi = @"C:\Program Files (x86)\MSI\MSI Center\Lib\SCEWIN";
        var scewin = Path.Combine(kits, "scewin");
        if (Directory.Exists(msi))
        {
            var pasta = Directory.GetDirectories(msi).OrderByDescending(d => d).FirstOrDefault(d => File.Exists(Path.Combine(d, "SCEWIN_64.exe")));
            if (pasta is not null)
            {
                Directory.CreateDirectory(scewin);
                foreach (var f in new[] { "SCEWIN_64.exe", "amifldrv64.sys", "amigendrv64.sys" })
                    if (File.Exists(Path.Combine(pasta, f))) File.Copy(Path.Combine(pasta, f), Path.Combine(scewin, f), true);
                Log("Leitura da BIOS: ferramenta copiada do MSI Center.");
            }
        }
        if (!File.Exists(Path.Combine(scewin, "SCEWIN_64.exe"))) Log("Aviso: a ferramenta de leitura da BIOS não foi encontrada; a tela Sistema fica sem as opções da BIOS.");
        var cli = @"C:\Program Files\AMD\RyzenMasterSDK\AMDRyzenMasterCLI\bin-prebuilt";
        if (Directory.Exists(cli))
        {
            var rm = Path.Combine(kits, "ryzenmaster");
            Directory.CreateDirectory(rm);
            foreach (var f in Directory.GetFiles(cli)) File.Copy(f, Path.Combine(rm, Path.GetFileName(f)), true);
            Log("Kit da AMD: ferramenta copiada.");
        }
        // Instalador do kit da AMD guardado pelo Windows: copiado para reinstalar se o kit sumir.
        try
        {
            using var prods = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products");
            foreach (var p in prods?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var ip = prods!.OpenSubKey(p + @"\InstallProperties");
                if (ip?.GetValue("DisplayName")?.ToString() != "AMD Ryzen Master SDK") continue;
                var pacote = ip.GetValue("LocalPackage")?.ToString();
                if (pacote is not null && File.Exists(pacote)) { File.Copy(pacote, Path.Combine(kits, "AMDRyzenMasterSDK.msi"), true); Log("Instalador do kit da AMD guardado."); }
            }
        }
        catch (Exception e) { Log("Aviso: instalador do kit da AMD não copiado: " + e.Message); }
    }

    static void PawnIo()
    {
        bool tem;
        try { using var s = new ServiceController("PawnIO"); tem = s.Status != 0; } catch { tem = false; }
        if (tem) { Log("PawnIO já instalado."); return; }
        var setup = Path.Combine(Destino, "kits", "PawnIO_setup.exe");
        if (!File.Exists(setup)) { Log("ERRO: PawnIO ausente e o instalador dele não está na pasta do SetupHub."); return; }
        int r = Rodar(setup, "-install -silent", 180000);
        Log(r == 0 ? "PawnIO instalado." : $"PawnIO: instalador terminou com código {r}.");
    }

    static void KitAmd()
    {
        if (File.Exists(@"C:\Program Files\AMD\RyzenMasterSDK\AMDRyzenMasterCLI\bin-prebuilt\AMDRyzenMasterCLI.exe")) { Log("Kit da AMD já instalado."); return; }
        var msi = Path.Combine(Destino, "kits", "AMDRyzenMasterSDK.msi");
        if (!File.Exists(msi)) { Log("Aviso: kit da AMD ausente e sem instalador guardado; o processador fica só em leitura."); return; }
        int r = Rodar("msiexec.exe", $"/i \"{msi}\" /qn /norestart", 600000);
        Log(r is 0 or 3010 ? "Kit da AMD instalado." : $"Kit da AMD: instalação terminou com código {r}.");
    }

    public static readonly string[] ServicosLuz = { "Mystic_Light_Service", "LightKeeperService" };

    // Os serviços de luz da MSI (e o LEDKeeper2) desfazem a luz do SetupHub: ficam desativados até a remoção.
    static void ServicoLuzMsi(EstadoMaquina m)
    {
        foreach (var nome in ServicosLuz)
        {
            try
            {
                using var s = ServiceController.GetServices().FirstOrDefault(x => x.ServiceName == nome);
                if (s is null) continue;
                using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{nome}");
                int inicio = k?.GetValue("Start") is int v ? v : 2;
                if (!m.ServicosLuzMsi.ContainsKey(nome)) m.ServicosLuzMsi[nome] = $"{inicio}:{(s.Status == ServiceControllerStatus.Running ? 1 : 0)}";
                if (s.Status != ServiceControllerStatus.Stopped) { s.Stop(); s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); }
                Rodar("sc.exe", $"config {nome} start= disabled");
                Log($"Serviço {nome} da MSI parado e desativado (antes: {m.ServicosLuzMsi[nome]}).");
            }
            catch (Exception e) { Log($"Aviso: serviço {nome}: " + e.Message); }
        }
        foreach (var p in Process.GetProcessesByName("LEDKeeper2")) { try { p.Kill(true); Log("LEDKeeper2 da MSI fechado."); } catch { } }
    }

    static void DevolverServicosLuz(EstadoMaquina? m)
    {
        if (m is null) return;
        foreach (var (nome, antes) in m.ServicosLuzMsi)
        {
            var partes = antes.Split(':');
            var tipo = partes[0] switch { "2" => "auto", "3" => "demand", "4" => "disabled", _ => "auto" };
            Rodar("sc.exe", $"config {nome} start= {tipo}");
            if (partes.Length > 1 && partes[1] == "1") Rodar("sc.exe", $"start {nome}");
            Log($"Serviço {nome} da MSI devolvido como estava.");
        }
    }

    static void ProtegerDados(string sid)
    {
        var di = new DirectoryInfo(Caminhos.Dados);
        var s = new DirectorySecurity();
        s.SetAccessRuleProtection(true, false);
        const InheritanceFlags herda = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, herda, PropagationFlags.None, AccessControlType.Allow));
        s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, herda, PropagationFlags.None, AccessControlType.Allow));
        s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, herda, PropagationFlags.None, AccessControlType.Allow));
        s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.Modify, herda, PropagationFlags.None, AccessControlType.Allow));
        di.SetAccessControl(s);
    }

    static void Servico(string sid)
    {
        var exe = Path.Combine(Destino, Produto.ExeServico);
        bool existe;
        try { using var s = new ServiceController(ServicoWindows.Nome); existe = s.Status != 0; } catch { existe = false; }
        if (!existe) Rodar("sc.exe", $"create {ServicoWindows.Nome} binPath= \"\\\"{exe}\\\"\" start= auto DisplayName= \"{Produto.Nome}\"");
        else Rodar("sc.exe", $"config {ServicoWindows.Nome} binPath= \"\\\"{exe}\\\"\" start= auto");
        Rodar("sc.exe", $"description {ServicoWindows.Nome} \"Ventoinhas, luz, processador, placa de vídeo e energia deste PC.\"");
        // Se cair, o Windows reinicia em 1 s (três vezes; a contagem zera em um dia).
        Rodar("sc.exe", $"failure {ServicoWindows.Nome} reset= 86400 actions= restart/1000/restart/1000/restart/5000");
        Rodar("sc.exe", $"failureflag {ServicoWindows.Nome} 1");
        using (var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServicoWindows.Nome}", true))
            k?.SetValue("PreshutdownTimeout", 20000, RegistryValueKind.DWord);
        // A conta do usuário inicia, para e consulta o serviço sem administrador.
        Rodar("sc.exe", $"sdset {ServicoWindows.Nome} D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;CCLCSWRPWPDTLOCRRC;;;{sid})");
        Log("Serviço registrado: inicia com o Windows, reinicia sozinho se cair.");
    }

    static void Caminho(bool incluir)
    {
        AjustarCaminho(Destino, incluir);
        Log(incluir ? $"Comando \"{Produto.Comando}\" disponível em qualquer terminal novo." : $"Pasta do {Produto.Nome} retirada do caminho de comandos.");
    }

    static void AjustarCaminho(string pasta, bool incluir)
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", true)!;
        var atual = k.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
        var partes = atual.Split(';', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.TrimEnd('\\').Equals(pasta, StringComparison.OrdinalIgnoreCase)).ToList();
        if (incluir) partes.Add(pasta);
        k.SetValue("Path", string.Join(';', partes), RegistryValueKind.ExpandString);
        Avisar();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr SendMessageTimeoutW(IntPtr h, int msg, IntPtr w, string l, int flags, int ms, out IntPtr r);
    static void Avisar() { try { SendMessageTimeoutW((IntPtr)0xffff, 0x1A, IntPtr.Zero, "Environment", 2, 3000, out _); } catch { } }

    static string Menu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Produto.Nome + ".lnk");
    static string AreaDeTrabalho => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Produto.Nome + ".lnk");

    static void Atalho(bool criar, string? sid = null)
    {
        try
        {
            if (!criar) { File.Delete(Menu); File.Delete(AreaDeTrabalho); return; }
            CriarAtalho(Menu);
            Log("Atalho no menu Iniciar criado.");
            bool doUsuario = AreaDoUsuario(sid) is string d && File.Exists(Path.Combine(d, Produto.Nome + ".lnk"));
            if (!doUsuario && !File.Exists(AreaDeTrabalho)) { CriarAtalho(AreaDeTrabalho); Log("Atalho na área de trabalho criado."); }
        }
        catch (Exception e) { Log("Aviso: atalho não criado: " + e.Message); }
    }

    static void CriarAtalho(string caminho)
    {
        var tipo = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(tipo)!;
        dynamic lnk = shell.CreateShortcut(caminho);
        lnk.TargetPath = Path.Combine(Destino, Produto.ExePainel);
        lnk.WorkingDirectory = Destino;
        lnk.Description = Produto.Nome;
        lnk.Save();
    }

    static string? AreaDoUsuario(string? sid)
    {
        if (sid is null) return null;
        using var k = Registry.Users.OpenSubKey($@"{sid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders");
        return k?.GetValue("Desktop") as string;
    }

    static void AbrirComWindows(string sid, bool ligar)
    {
        try
        {
            using var k = Registry.Users.OpenSubKey($@"{sid}\Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (k is null) return;
            if (ligar) k.SetValue(Produto.Nome, $"\"{Path.Combine(Destino, Produto.ExePainel)}\" --bandeja");
            else k.DeleteValue(Produto.Nome, false);
        }
        catch (Exception e) { Log("Aviso: abertura com o Windows: " + e.Message); }
    }

    // Camada Vulkan e lugar do RivaTuner no Windows apontam para a cópia em kits\rtss; a conta do usuário grava os perfis.
    static string PastaRtss => Path.Combine(Destino, "kits", "rtss");
    const string Avulso = @"C:\Program Files (x86)\RivaTuner Statistics Server";
    static readonly (RegistryView vista, string arquivo)[] CamadasVulkan = { (RegistryView.Registry64, "RTSSVkLayer64.json"), (RegistryView.Registry32, "RTSSVkLayer32.json") };
    const string ChaveCamadas = @"SOFTWARE\Khronos\Vulkan\ImplicitLayers";
    static readonly string[] ProcessosRtss = { "RTSS", "RTSSHooksLoader", "RTSSHooksLoader64", "EncoderServer", "EncoderServer64", "DesktopOverlayHost", "DesktopOverlayHost64" };

    static void PararRtss()
    {
        foreach (var n in ProcessosRtss)
            foreach (var p in Process.GetProcessesByName(n)) { try { p.Kill(true); p.WaitForExit(5000); } catch { } finally { p.Dispose(); } }
    }

    static void RivaTuner(string sid)
    {
        var pasta = PastaRtss;
        if (!File.Exists(Path.Combine(pasta, "RTSS.exe"))) { Log("RivaTuner não veio no pacote: sobreposição desligada, limite de quadros pela AMD."); return; }
        try
        {
            var perfis = new DirectoryInfo(Path.Combine(pasta, "Profiles"));
            var s = perfis.GetAccessControl();
            s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            perfis.SetAccessControl(s);
            foreach (var (vista, arquivo) in CamadasVulkan)
            {
                using var raiz = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, vista);
                using var k = raiz.CreateSubKey(ChaveCamadas);
                // Camada do RivaTuner avulso sai; vale a da cópia.
                foreach (var nome in k.GetValueNames().Where(n => n.StartsWith(Avulso, StringComparison.OrdinalIgnoreCase) && !File.Exists(n))) k.DeleteValue(nome, false);
                k.SetValue(Path.Combine(pasta, "Vulkan", arquivo), 0, RegistryValueKind.DWord);
            }
            using (var raiz = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (var k = raiz.CreateSubKey(@"SOFTWARE\Unwinder\RTSS"))
            {
                var atual = k.GetValue("InstallDir") as string;
                // A pasta do avulso desinstalado pode esperar o reinício para sumir: sem o programa nela, vale a cópia.
                if (atual is null || !File.Exists(Path.Combine(atual, "RTSS.exe")) || atual.StartsWith(Destino, StringComparison.OrdinalIgnoreCase))
                {
                    k.SetValue("InstallDir", pasta);
                    k.SetValue("InstallPath", Path.Combine(pasta, "RTSS.exe"));
                }
            }
            var conta = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
            // /h tira o ícone do RivaTuner da bandeja (o mesmo que o Afterburner usa); as configurações abrem pela tela Jogos.
            Rodar("schtasks.exe", $"/create /f /tn \"{Produto.TarefaRivaTuner}\" /tr \"\\\"{Path.Combine(pasta, "RTSS.exe")}\\\" /h\" /sc onlogon /ru \"{conta}\" /it /rl highest");
            // Só roda quando a janela pede (botão da tela Jogos); a data de início no passado evita execução sozinha.
            Rodar("schtasks.exe", $"/create /f /tn \"{Produto.TarefaTelaRivaTuner}\" /tr \"\\\"{Path.Combine(Destino, Produto.ExeServico)}\\\" rivatuner-tela\" /sc once /st 00:00 /ru \"{conta}\" /it /rl highest");
            Log($"RivaTuner do {Produto.Nome}: camada Vulkan registrada, perfis liberados para a conta do usuário e abertura no login.");
        }
        catch (Exception e) { Log("Aviso: RivaTuner: " + e.Message); }
    }

    static void RemoverRivaTuner()
    {
        PararRtss();
        try
        {
            foreach (var (vista, arquivo) in CamadasVulkan)
            {
                using var raiz = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, vista);
                using var k = raiz.OpenSubKey(ChaveCamadas, true);
                k?.DeleteValue(Path.Combine(PastaRtss, "Vulkan", arquivo), false);
            }
            using var r32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var u = r32.OpenSubKey(@"SOFTWARE\Unwinder\RTSS", true);
            if (u?.GetValue("InstallDir") is string dir && dir.StartsWith(Destino, StringComparison.OrdinalIgnoreCase)) r32.DeleteSubKeyTree(@"SOFTWARE\Unwinder\RTSS", false);
            Log($"RivaTuner do {Produto.Nome} retirado do Windows.");
        }
        catch (Exception e) { Log("Aviso: RivaTuner: " + e.Message); }
    }

    static void Desinstalacao()
    {
        using var k = Registry.LocalMachine.CreateSubKey(Chave);
        var exe = Path.Combine(Destino, Produto.ExeServico);
        k.SetValue("DisplayName", Produto.Nome);
        k.SetValue("DisplayVersion", Produto.Versao);
        k.SetValue("Publisher", Produto.Nome);
        try { k.SetValue("EstimatedSize", (int)(new DirectoryInfo(Destino).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024), RegistryValueKind.DWord); } catch { }
        k.SetValue("DisplayIcon", Path.Combine(Destino, Produto.ExePainel));
        k.SetValue("InstallLocation", Destino);
        k.SetValue("UninstallString", $"\"{exe}\" remover");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static int Remover(string[] args)
    {
        try
        {
            if (!Environment.IsPrivilegedProcess)
            {
                // Chamado pela lista de programas sem administrador: pede elevação e sai.
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "remover") { UseShellExecute = true, Verb = "runas" });
                return 0;
            }
            Log($"Removendo o {Produto.Nome}.");
            PararTudo();
            Rodar("sc.exe", $"delete {ServicoWindows.Nome}");
            var m = Ler<EstadoMaquina>(Caminhos.EstadoMaquina);
            DevolverServicosLuz(m);
            if (m?.UsuarioSid is string sid) AbrirComWindows(sid, false);
            Caminho(false);
            Atalho(false);
            Rodar("schtasks.exe", $"/delete /f /tn \"{Produto.TarefaRivaTuner}\"");
            Rodar("schtasks.exe", $"/delete /f /tn \"{Produto.TarefaTelaRivaTuner}\"");
            RemoverRivaTuner();
            Hardware.Energia.RemoverPlanos();
            try { Registry.LocalMachine.DeleteSubKeyTree(Chave, false); } catch { }
            try { Directory.Delete(Caminhos.Dados, true); } catch (Exception e) { Log("Aviso: dados: " + e.Message); }
            // A pasta do programa sai depois que este processo terminar.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{Destino}\"") { UseShellExecute = false, CreateNoWindow = true });
            Log($"{Produto.Nome} removido.");
            // Os dados e a pasta somem; o registro da remoção fica na pasta temporária da conta.
            try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), Produto.Comando + "_remocao.txt"), log); } catch { }
            return 0;
        }
        catch (Exception e)
        {
            Log("ERRO: " + e);
            try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), Produto.Comando + "_remocao.txt"), log); } catch { }
            return 1;
        }
    }
}
