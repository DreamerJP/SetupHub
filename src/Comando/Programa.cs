using System.Text;
using SetupHub.Nucleo;

namespace SetupHub.Comando;

static class Programa
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        bool ajuda = args.Length == 0 || args[0] is "ajuda" or "--help" or "-h" or "/?";
        var canal = ajuda ? null : await Conexao.ConectarAsync(1500);
        if (canal is not null) _ = canal.IniciarLeitura();
        try { return await new Interprete(canal, Console.Out, Console.Error).Executar(args); }
        finally { if (canal is not null) await canal.DisposeAsync(); }
    }
}
