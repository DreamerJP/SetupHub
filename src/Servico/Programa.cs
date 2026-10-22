using System.Runtime.InteropServices;
using SetupHub.Nucleo;

namespace SetupHub.Servico;

static class Programa
{
    delegate bool Manipulador(int tipo);
    [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(Manipulador h, bool add);
    static Manipulador? manter;

    static int Main(string[] args)
    {
        var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        switch (cmd)
        {
            case "instalar": return Instalador.Instalar(args.Skip(1).ToArray());
            case "remover": return Instalador.Remover(args.Skip(1).ToArray());
            case "rivatuner-tela": return TelaRivaTuner.Mostrar();
            case "--console":
            {
                Registro.Console_ = true;
                var m = new Motor();
                var fim = new ManualResetEventSlim();
                m.SairProcesso = fim.Set;
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; fim.Set(); };
                manter = t => { m.Parar(t == 5 || t == 6); fim.Set(); return true; };
                SetConsoleCtrlHandler(manter, true);
                m.Iniciar();
                fim.Wait();
                m.Parar(false);
                return 0;
            }
        }
        if (!ServicoWindows.Rodar())
        {
            Console.WriteLine($"{Produto.ExeServico} roda como serviço do Windows. Para testar em primeiro plano: {Produto.ExeServico} --console");
            return 1;
        }
        return 0;
    }
}
