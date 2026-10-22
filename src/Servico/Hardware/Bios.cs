using System.Diagnostics;
using System.Text.RegularExpressions;
using SetupHub.Nucleo;
using Microsoft.Win32;

namespace SetupHub.Servico.Hardware;

// Só leitura: a ferramenta da AMI exporta as opções da BIOS para texto; nada é gravado nela.
static partial class Bios
{
    public static string? Ferramenta()
    {
        var copia = Path.Combine(Caminhos.Kits, "scewin", "SCEWIN_64.exe");
        return File.Exists(copia) ? copia : null;
    }

    public static async Task<List<OpcaoBios>> Ler(CancellationToken ct = default)
    {
        var exe = Ferramenta() ?? throw new ErroSetupHub("A cópia da ferramenta de leitura da BIOS não está na pasta do SetupHub (reinstale o SetupHub).");
        var tmp = Path.Combine(Caminhos.Dados, "bios_tmp.txt");
        try { File.Delete(tmp); } catch { }
        var psi = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("/o"); psi.ArgumentList.Add("/s"); psi.ArgumentList.Add(tmp);
        using var p = Process.Start(psi) ?? throw new ErroSetupHub("A ferramenta da BIOS não abriu.");
        _ = p.StandardOutput.ReadToEndAsync(ct);
        _ = p.StandardError.ReadToEndAsync(ct);
        var fim = p.WaitForExitAsync(ct);
        if (await Task.WhenAny(fim, Task.Delay(120000, ct)) != fim) { try { p.Kill(true); } catch { } throw new ErroSetupHub("A leitura da BIOS passou de 2 minutos e foi interrompida."); }
        if (!File.Exists(tmp)) throw new ErroSetupHub($"A ferramenta da BIOS terminou sem gerar o arquivo (código {p.ExitCode}).");
        File.Copy(tmp, Caminhos.Bios, true);
        File.Delete(tmp);
        return Interpretar(await File.ReadAllLinesAsync(Caminhos.Bios, ct));
    }

    public static List<OpcaoBios> Interpretar(string[] linhas)
    {
        var r = new List<OpcaoBios>();
        OpcaoBios? atual = null;
        bool emOpcoes = false;
        foreach (var bruta in linhas)
        {
            var l = bruta.TrimEnd();
            if (l.StartsWith("//")) { emOpcoes = false; continue; }
            if (l.StartsWith("Setup Question"))
            {
                atual = new OpcaoBios { Nome = Depois(l) };
                atual.Interfere = BiosAvisos.Afeta(atual.Nome);
                r.Add(atual);
                emOpcoes = false;
                continue;
            }
            if (atual is null) continue;
            if (l.StartsWith("BIOS Default")) { atual.Padrao = Limpar(Depois(l)); emOpcoes = false; }
            else if (l.StartsWith("Value"))
            {
                var v = Depois(l);
                atual.Valor = Limpar(v).Trim('<', '>', ' ');
                var nomes = Nomes(v);
                if (nomes.TryGetValue(atual.Valor, out var nv)) atual.Valor = nv;
                if (nomes.TryGetValue(atual.Padrao.Trim('<', '>', ' '), out var np)) atual.Padrao = np;
                emOpcoes = false;
            }
            else if (l.StartsWith("Options")) { emOpcoes = true; Opcao(atual, Depois(l)); }
            else if (emOpcoes && l.StartsWith(" ") && l.Contains('[')) Opcao(atual, l.Trim());
            else emOpcoes = false;
        }
        foreach (var o in r) if (o.Padrao.StartsWith('<')) o.Padrao = o.Padrao.Trim('<', '>');
        return r;
    }

    static string Depois(string l) { int i = l.IndexOf('='); return i < 0 ? "" : l[(i + 1)..].Trim(); }

    static string Limpar(string s)
    {
        int c = s.IndexOf("//", StringComparison.Ordinal);
        if (c >= 0) s = s[..c];
        return OpcaoRegex().Replace(s.Trim().TrimStart('*'), "").Trim();
    }

    // Valor numérico vem com a legenda "Enabled = 1, Disabled = 0" depois de "//": o número ganha o nome da legenda.
    static Dictionary<string, string> Nomes(string s)
    {
        var r = new Dictionary<string, string>();
        int c = s.IndexOf("//", StringComparison.Ordinal);
        if (c < 0) return r;
        foreach (var par in s[(c + 2)..].Split(','))
        {
            var p = par.Split('=');
            if (p.Length == 2 && p[0].Trim().Length > 0 && p[1].Trim().Length > 0) r.TryAdd(p[1].Trim(), p[0].Trim());
        }
        return r;
    }

    static void Opcao(OpcaoBios o, string texto)
    {
        var t = texto;
        int c = t.IndexOf("//", StringComparison.Ordinal);
        if (c >= 0) t = t[..c];
        t = t.Trim();
        bool marcado = t.StartsWith('*');
        var nome = Limpar(t);
        if (nome.Length == 0) return;
        o.Opcoes.Add(nome);
        if (marcado) o.Valor = nome;
    }

    [GeneratedRegex(@"^\[[0-9A-Fa-f]+\]")] private static partial Regex OpcaoRegex();

    public static void Basico(InfoSistema s)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            s.Placa = $"{k?.GetValue("BaseBoardManufacturer")} {k?.GetValue("BaseBoardProduct")}".Trim();
            s.Bios = k?.GetValue("BIOSVersion")?.ToString() ?? "";
            s.BiosData = k?.GetValue("BIOSReleaseDate")?.ToString() ?? "";
            // O Windows guarda a data no formato americano (mês/dia/ano).
            if (DateTime.TryParseExact(s.BiosData, "MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
                s.BiosData = d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { }
        try
        {
            using var w = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            s.Windows = $"{w?.GetValue("ProductName")} {w?.GetValue("DisplayVersion")} ({w?.GetValue("CurrentBuild")}.{w?.GetValue("UBR")})";
            if (int.TryParse(w?.GetValue("CurrentBuild")?.ToString(), out var b) && b >= 22000) s.Windows = s.Windows.Replace("Windows 10", "Windows 11");
        }
        catch { }
        try
        {
            var mods = new LibreHardwareMonitor.Hardware.SMBios().MemoryDevices.Where(m => m.Size > 0).ToList();
            if (mods.Count > 0)
            {
                s.MemoriaModulos = string.Join(", ", mods.GroupBy(m => (m.Size, Fab: m.ManufacturerName?.Trim(), Peca: m.PartNumber?.Trim()))
                    .Select(g => $"{g.Count()} × {g.Key.Size / 1024} GB {g.Key.Fab} {g.Key.Peca}".Trim()));
                var v = mods.Max(m => m.ConfiguredSpeed);
                if (v > 0 && s.MemoriaPerfil.Length == 0) s.MemoriaPerfil = $"{v} MT/s";
            }
        }
        catch { }
    }
}
