namespace SetupHub.Nucleo;

// Só o que, na BIOS, atrapalha o SetupHub: com tudo normal a lista sai vazia.
public static class BiosAvisos
{
    const StringComparison Sem = StringComparison.OrdinalIgnoreCase;
    static readonly string[] Nomes = { "MSI Driver Utility Installer", "Game Boost", "Global C-state Control" };

    public static bool Afeta(string nome) => Nomes.Any(x => nome.Trim().Equals(x, Sem));

    static bool Mudada(OpcaoBios o) => o.Padrao.Length > 0 && !o.Padrao.Equals(o.Valor, Sem);

    // A BIOS repete o mesmo ajuste em vários menus; entre as cópias, a mudada é a que o usuário mexeu.
    static string? Valor(List<OpcaoBios> ops, string nome)
    {
        var l = ops.Where(o => o.Nome.Trim().Equals(nome, Sem)).ToList();
        return (l.FirstOrDefault(Mudada) ?? l.FirstOrDefault())?.Valor;
    }

    public static List<string> Lista(List<OpcaoBios> ops)
    {
        var r = new List<string>();
        if (Valor(ops, "MSI Driver Utility Installer") is string msi && msi.StartsWith("Enable", Sem))
            r.Add("Instalador automático da MSI ligado: a placa instala pelo Windows o utilitário da MSI, que oferece o MSI Center.");
        if (Valor(ops, "Game Boost") is string gb && !gb.Equals("Auto", Sem) && !gb.StartsWith("Disable", Sem))
            r.Add("Game Boost ligado: a BIOS faz overclock automático, que vira o padrão do processador no SetupHub.");
        if (Valor(ops, "Global C-state Control") is string cs && cs.StartsWith("Disable", Sem))
            r.Add("Estados de descanso do processador desligados: os planos Economia e Equilibrado economizam bem menos.");
        return r;
    }
}
