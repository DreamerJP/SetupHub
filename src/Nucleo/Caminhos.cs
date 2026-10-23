namespace SetupHub.Nucleo;

// Nome do programa no Windows: serviço, pastas, canal, tarefas e programas.
public static class Produto
{
    public const string Nome = "SetupHub";
    public const string ExePainel = "SetupHubPainel.exe";
    public const string ExeServico = "SetupHubServico.exe";
    public const string Comando = "setuphub";
    public const string TarefaRivaTuner = Nome + @"\RivaTuner";
    public const string TarefaTelaRivaTuner = Nome + @"\RivaTunerTela";
    // Vem do Version do Directory.Build.props: o mesmo número no instalador, na janela e em Aplicativos do Windows.
    public static string Versao => typeof(Produto).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "";
}

public static class Caminhos
{
    public static string Programa => AppContext.BaseDirectory;
    public static string Instalado => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Produto.Nome);
    public static string Dados => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Produto.Nome);
    public static string Config => Path.Combine(Dados, "config.json");
    public static string EstadoMaquina => Path.Combine(Dados, "estado.json");
    public static string Faixas => Path.Combine(Dados, "faixas.json");
    public static string Historico => Path.Combine(Dados, "historico.bin");
    public static string Registro => Path.Combine(Dados, "registro.txt");
    public static string Sistema => Path.Combine(Dados, "sistema.json");
    public static string Bios => Path.Combine(Dados, "bios.txt");
    public static string Usuario => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Produto.Nome);
    public static string Kits => Path.Combine(Programa, "kits");
}

public static class Formato
{
    static readonly System.Globalization.CultureInfo Br = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
    public static string N(double? v, int casas = 0) => v is double d ? d.ToString("N" + casas, Br) : "–";
    public static string Temp(double? v) => v is double d ? d.ToString("0", Br) + " °C" : "–";
    public static string Pct(double? v) => v is double d ? d.ToString("0", Br) + "%" : "–";
    public static string Rpm(double? v) => v is double d ? d.ToString("N0", Br) + " rpm" : "–";
    public static string W(double? v) => v is double d ? d.ToString("0", Br) + " W" : "–";
    public static string Mhz(double? v) => v is double d ? d.ToString("N0", Br) + " MHz" : "–";
    public static string Hora(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm", Br);
    public static long Agora => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
