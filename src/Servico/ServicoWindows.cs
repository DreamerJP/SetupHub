using System.Runtime.InteropServices;
using SetupHub.Nucleo;

namespace SetupHub.Servico;

// Direto pela API do Windows, sem pacote extra.
static unsafe class ServicoWindows
{
    public const string Nome = Produto.Nome;

    const int SERVICE_WIN32_OWN_PROCESS = 0x10;
    const int SERVICE_RUNNING = 4, SERVICE_STOP_PENDING = 3, SERVICE_STOPPED = 1, SERVICE_START_PENDING = 2;
    const int ACCEPT_STOP = 1, ACCEPT_PRESHUTDOWN = 0x100, ACCEPT_POWEREVENT = 0x40, ACCEPT_SESSIONCHANGE = 0x80;
    const int CONTROL_STOP = 1, CONTROL_INTERROGATE = 4, CONTROL_SHUTDOWN = 5, CONTROL_POWEREVENT = 13, CONTROL_SESSIONCHANGE = 14, CONTROL_PRESHUTDOWN = 15;
    const int PBT_APMSUSPEND = 4, PBT_APMRESUMEAUTOMATIC = 0x12, PBT_POWERSETTINGCHANGE = 0x8013;
    static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    [StructLayout(LayoutKind.Sequential)]
    struct Status { public int Tipo, Estado, Aceita, SaidaWin32, SaidaServico, Marca, Espera; }
    [StructLayout(LayoutKind.Sequential)]
    struct Entrada { public IntPtr Nome; public IntPtr Funcao; }
    [StructLayout(LayoutKind.Sequential)]
    struct AjusteEnergia { public Guid Ajuste; public uint Tamanho; public uint Dado; }

    [DllImport("advapi32.dll", SetLastError = true)] static extern bool StartServiceCtrlDispatcherW(Entrada* tabela);
    [DllImport("advapi32.dll", SetLastError = true)] static extern IntPtr RegisterServiceCtrlHandlerExW(char* nome, delegate* unmanaged<int, int, IntPtr, IntPtr, int> f, IntPtr ctx);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool SetServiceStatus(IntPtr h, ref Status s);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid g, int flags);

    static IntPtr alca;
    static Status status;
    static Motor? nucleo;
    static readonly ManualResetEvent parado = new(false);
    static GCHandle nomeFixo;

    public static bool Rodar()
    {
        var nome = Marshal.StringToHGlobalUni(Nome);
        var tabela = stackalloc Entrada[2];
        tabela[0] = new Entrada { Nome = nome, Funcao = (IntPtr)(delegate* unmanaged<int, IntPtr, void>)&Principal };
        tabela[1] = default;
        return StartServiceCtrlDispatcherW(tabela);
    }

    [UnmanagedCallersOnly]
    static void Principal(int argc, IntPtr argv)
    {
        nomeFixo = GCHandle.Alloc((Nome + "\0").ToCharArray(), GCHandleType.Pinned);
        alca = RegisterServiceCtrlHandlerExW((char*)nomeFixo.AddrOfPinnedObject(), &Controle, IntPtr.Zero);
        status = new Status { Tipo = SERVICE_WIN32_OWN_PROCESS, Estado = SERVICE_START_PENDING, Espera = 30000 };
        SetServiceStatus(alca, ref status);
        try
        {
            var g = GUID_CONSOLE_DISPLAY_STATE;
            RegisterPowerSettingNotification(alca, ref g, 1);
            nucleo = new Motor();
            nucleo.AoSair = () => Informar(SERVICE_STOP_PENDING, 20000);
            nucleo.SairProcesso = () => parado.Set();
            nucleo.Iniciar();
            Informar(SERVICE_RUNNING, 0);
            parado.WaitOne();
        }
        catch (Exception e)
        {
            Registro.Erro("O serviço não iniciou", e);
            status.SaidaWin32 = 1064;
        }
        Registro.Descarregar();
        Informar(SERVICE_STOPPED, 0);
    }

    static void Informar(int estado, int espera)
    {
        status.Estado = estado;
        status.Aceita = estado == SERVICE_RUNNING ? ACCEPT_STOP | ACCEPT_PRESHUTDOWN | ACCEPT_POWEREVENT | ACCEPT_SESSIONCHANGE : 0;
        status.Espera = espera;
        if (estado is SERVICE_START_PENDING or SERVICE_STOP_PENDING) status.Marca++;
        SetServiceStatus(alca, ref status);
    }

    [UnmanagedCallersOnly]
    static int Controle(int codigo, int tipo, IntPtr dados, IntPtr ctx)
    {
        try
        {
            switch (codigo)
            {
                case CONTROL_STOP:
                    Informar(SERVICE_STOP_PENDING, 20000);
                    Task.Run(() => { nucleo?.Parar(desligando: false); parado.Set(); });
                    return 0;
                case CONTROL_PRESHUTDOWN:
                case CONTROL_SHUTDOWN:
                    Informar(SERVICE_STOP_PENDING, 20000);
                    nucleo?.Parar(desligando: true);
                    parado.Set();
                    return 0;
                case CONTROL_POWEREVENT:
                    if (tipo == PBT_APMSUSPEND) nucleo?.Suspendendo();
                    else if (tipo == PBT_APMRESUMEAUTOMATIC) nucleo?.Voltou();
                    else if (tipo == PBT_POWERSETTINGCHANGE && dados != IntPtr.Zero)
                    {
                        var a = Marshal.PtrToStructure<AjusteEnergia>(dados);
                        if (a.Ajuste == GUID_CONSOLE_DISPLAY_STATE) nucleo?.Tela(a.Dado != 0);
                    }
                    return 0;
                case CONTROL_SESSIONCHANGE:
                    nucleo?.Sessao(tipo, dados != IntPtr.Zero ? Marshal.ReadInt32(dados, 4) : -1);
                    return 0;
                case CONTROL_INTERROGATE:
                    SetServiceStatus(alca, ref status);
                    return 0;
            }
        }
        catch (Exception e) { Registro.Erro("Controle do serviço", e); }
        return 120;
    }
}
