using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SetupHub.Servico;

// Roda pela tarefa elevada "SetupHub\RivaTunerTela". A janela do RivaTuner só fica clicável quando ele mesmo a abre:
// mostrada de fora ela não se redesenha. Por isso ele reinicia com a janela aberta e a opção volta a "começar escondido".
static class TelaRivaTuner
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool WritePrivateProfileStringW(string secao, string chave, string valor, string arquivo);

    public static int Mostrar()
    {
        var pasta = Path.Combine(AppContext.BaseDirectory, "kits", "rtss");
        var exe = Path.Combine(pasta, "RTSS.exe");
        var config = Path.Combine(pasta, "Profiles", "Config");
        if (!File.Exists(exe)) return 1;
        foreach (var nome in new[] { "RTSS", "RTSSHooksLoader64", "RTSSHooksLoader", "EncoderServer", "EncoderServer64" })
            foreach (var p in Process.GetProcessesByName(nome))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
                finally { p.Dispose(); }
            }
        WritePrivateProfileStringW("Settings", "StartMinimized", "0", config);
        using var rtss = Process.Start(new ProcessStartInfo(exe, "/h") { UseShellExecute = false, WorkingDirectory = pasta });
        Thread.Sleep(4000);
        WritePrivateProfileStringW("Settings", "StartMinimized", "1", config);
        // Sem ícone na bandeja, fechar a janela encerra o RivaTuner: ele volta na hora, escondido, para a sobreposição e o limite não pararem.
        // Ao fechar, ele grava de novo "começar com a janela aberta": a opção volta a escondida depois que ele sai.
        rtss?.WaitForExit();
        WritePrivateProfileStringW("Settings", "StartMinimized", "1", config);
        using var escondido = Process.Start(new ProcessStartInfo(exe, "/h") { UseShellExecute = false, WorkingDirectory = pasta });
        return 0;
    }
}
