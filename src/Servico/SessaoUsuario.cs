using System.Runtime.InteropServices;
using System.Text;

namespace SetupHub.Servico;

// Do serviço do sistema para a sessão do usuário, com a conta dele e sem privilégio.
static class SessaoUsuario
{
    [DllImport("kernel32.dll")] static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32.dll", SetLastError = true)] static extern bool WTSQueryUserToken(uint sessao, out IntPtr token);
    [DllImport("userenv.dll", SetLastError = true)] static extern bool CreateEnvironmentBlock(out IntPtr ambiente, IntPtr token, bool herdar);
    [DllImport("userenv.dll")] static extern bool DestroyEnvironmentBlock(IntPtr ambiente);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessAsUserW(IntPtr token, string? app, StringBuilder linha, IntPtr pa, IntPtr ta, bool herda, uint flags, IntPtr ambiente, string? pasta, ref Inicio si, out Processo pi);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Inicio
    {
        public int cb; public string? reservado, area, titulo;
        public int x, y, l, a, cx, cy, cor, flags; public short mostrar, r2; public IntPtr r3, entrada, saida, erro;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Processo { public IntPtr processo, linha; public int pid, tid; }

    public static uint? Ativa()
    {
        var s = WTSGetActiveConsoleSessionId();
        return s is 0 or 0xFFFFFFFF ? null : s;
    }

    public static bool Abrir(uint sessao, string exe, string argumentos, out string erro)
    {
        erro = "";
        if (!WTSQueryUserToken(sessao, out var token)) { erro = $"sem conta logada na sessão {sessao} (código {Marshal.GetLastWin32Error()})"; return false; }
        IntPtr ambiente = IntPtr.Zero;
        try
        {
            CreateEnvironmentBlock(out ambiente, token, false);
            var si = new Inicio { cb = Marshal.SizeOf<Inicio>(), area = @"winsta0\default" };
            var linha = new StringBuilder($"\"{exe}\" {argumentos}");
            const uint CREATE_UNICODE_ENVIRONMENT = 0x400;
            if (!CreateProcessAsUserW(token, null, linha, IntPtr.Zero, IntPtr.Zero, false, CREATE_UNICODE_ENVIRONMENT, ambiente, Path.GetDirectoryName(exe), ref si, out var pi))
            {
                erro = $"o Windows recusou abrir (código {Marshal.GetLastWin32Error()})";
                return false;
            }
            CloseHandle(pi.processo); CloseHandle(pi.linha);
            return true;
        }
        finally
        {
            if (ambiente != IntPtr.Zero) DestroyEnvironmentBlock(ambiente);
            CloseHandle(token);
        }
    }
}
