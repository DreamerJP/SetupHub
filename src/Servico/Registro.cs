using System.Text;
using SetupHub.Nucleo;

namespace SetupHub.Servico;

// Escrita em segundo plano: nunca atrasa o laço principal.
static class Registro
{
    static readonly object trava = new();
    static readonly Queue<string> fila = new();
    static Thread? escritor;
    static readonly AutoResetEvent sinal = new(false);
    public static bool Console_;

    public static void Info(string texto) => Escrever("info", texto);
    public static void Aviso(string texto) => Escrever("aviso", texto);
    public static void Erro(string texto, Exception? e = null) => Escrever("erro", e is null ? texto : $"{texto}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");

    static void Escrever(string nivel, string texto)
    {
        var linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{nivel}] {texto}";
        if (Console_) Console.WriteLine(linha);
        lock (trava)
        {
            fila.Enqueue(linha);
            if (escritor is null)
            {
                escritor = new Thread(Laco) { IsBackground = true, Name = "registro", Priority = ThreadPriority.BelowNormal };
                escritor.Start();
            }
        }
        sinal.Set();
    }

    public static void Descarregar()
    {
        string[] linhas;
        lock (trava) { linhas = fila.ToArray(); fila.Clear(); }
        if (linhas.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Caminhos.Dados);
            var arq = new FileInfo(Caminhos.Registro);
            if (arq.Exists && arq.Length > 4 * 1024 * 1024)
            {
                var velho = Caminhos.Registro + ".1";
                File.Copy(Caminhos.Registro, velho, true);
                File.WriteAllText(Caminhos.Registro, "");
            }
            File.AppendAllLines(Caminhos.Registro, linhas, Encoding.UTF8);
        }
        catch { }
    }

    static void Laco()
    {
        while (true)
        {
            sinal.WaitOne(2000);
            Descarregar();
        }
    }
}
