using System.Runtime.InteropServices;
using Microsoft.Win32;
using SetupHub.Nucleo;

namespace SetupHub.Servico.Hardware;

// Direto pela powrprof, sem rodar o powercfg.
static class Energia
{
    [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr root, IntPtr sub, IntPtr set, uint acc, uint idx, byte[]? buf, ref uint size);
    [DllImport("powrprof.dll")] static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr set, byte[]? buf, ref uint size);
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] static extern uint PowerDuplicateScheme(IntPtr root, ref Guid source, ref IntPtr dest);
    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)] static extern uint PowerWriteFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr set, string name, uint size);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid set, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid set, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid set, out uint value);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

    // Identificadores fixos: acha cada plano de novo sem depender do nome.
    static readonly Dictionary<string, Guid> Ids = new()
    {
        ["economia"] = new("5e1f2a3b-6c7d-4e8f-9a0b-1c2d3e4f5a61"),
        ["equilibrado"] = new("5e1f2a3b-6c7d-4e8f-9a0b-1c2d3e4f5a62"),
        ["desempenho"] = new("5e1f2a3b-6c7d-4e8f-9a0b-1c2d3e4f5a63"),
    };
    // Planos que o SetupHub já criou e não usa mais: o plano único e o Máximo.
    static readonly Guid[] Aposentados = { new("c3a7e1f0-5b2d-4c8e-9a61-2f4d8b0e7c55"), new("5e1f2a3b-6c7d-4e8f-9a0b-1c2d3e4f5a64") };
    static readonly Guid AltoDesempenho = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    static readonly Guid EquilibradoWindows = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    static readonly Guid SubProc = new("54533251-82be-4824-96c1-47b60b740d00");
    static readonly Guid ProcMin = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    static readonly Guid ProcMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    static readonly Guid NucleosMin = new("0cc5b647-c1df-4637-891a-dec35c318583");
    static readonly Guid Impulso = new("be337238-0d82-4146-a960-4f3749d470c7");
    static readonly Guid Preferencia = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    static readonly Guid SubUsb = new("2a737441-1930-4402-8d77-b2bebba308a3");
    static readonly Guid UsbSuspensao = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
    static readonly Guid SubPcie = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    static readonly Guid Aspm = new("ee12f906-d277-404b-b6da-e5fa1a576df5");
    static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    static readonly Guid VideoOcioso = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    static readonly Guid SubSono = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    static readonly Guid SonoOcioso = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
    static readonly Guid HibernarOcioso = new("9d7815a6-7ee4-497e-8888-515a05f02364");
    static readonly Guid SubDisco = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    static readonly Guid DiscoOcioso = new("6738e2c4-e8a5-4a42-b16a-e040e769756e");

    // Ação rápida, modo de jogo e desempenho sustentado sobem o mínimo para 100% e passam por cima do máximo do plano.
    static readonly Guid[] PerfisQueSobem = { new("0da965dc-8fcf-4c0b-8efe-8dd5e7bc959a"), new("d4140c81-ebba-4e60-8561-6918290359cd"), new("0aabb002-a307-447e-9b81-1d819df6c6d0") };
    const string MinimoPorPerfil = @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\893dee8e-2bef-41e0-89c6-b55d0929964c";

    public static List<(Guid id, string nome)> Planos()
    {
        var r = new List<(Guid, string)>();
        for (uint i = 0; ; i++)
        {
            uint t = 16; var b = new byte[16];
            if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, b, ref t) != 0) break;
            var g = new Guid(b);
            r.Add((g, Nome(g)));
        }
        return r;
    }

    static string Nome(Guid g)
    {
        uint t = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, null, ref t);
        if (t == 0) return g.ToString();
        var b = new byte[t];
        if (PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, b, ref t) != 0) return g.ToString();
        return System.Text.Encoding.Unicode.GetString(b).TrimEnd('\0');
    }

    public static (Guid id, string nome) Ativo()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return (Guid.Empty, "");
        var g = Marshal.PtrToStructure<Guid>(p);
        LocalFree(p);
        return (g, Nome(g));
    }

    // Plano próprio em uso no Windows, ou nulo quando o ativo é outro.
    public static string? IdAtivo() { var a = Ativo().id; return Ids.FirstOrDefault(kv => kv.Value == a).Key; }

    // Os quatro planos ficam sempre criados e com o ajuste da configuração; o escolhido vira o ativo do Windows.
    public static List<string> Aplicar(ConfigEnergia c)
    {
        var falhas = new List<string>();
        foreach (var p in c.Planos)
            if (Garantir(p) == Guid.Empty) falhas.Add($"plano {Padroes.NomePlano(p.Id)} não pôde ser criado");
        RemoverAntigo();
        if (c.Plano is string id && Ids.TryGetValue(id, out var alvo) && Ativo().id != alvo)
        {
            PowerSetActiveScheme(IntPtr.Zero, ref alvo);
            if (Ativo().id != alvo) falhas.Add($"plano {Padroes.NomePlano(id)} não ficou ativo");
        }
        return falhas;
    }

    static bool Duplicar(Guid origem, Guid id)
    {
        IntPtr destino = Marshal.AllocHGlobal(16);
        Marshal.StructureToPtr(id, destino, false);
        uint r = PowerDuplicateScheme(IntPtr.Zero, ref origem, ref destino);
        Marshal.FreeHGlobal(destino);
        return r == 0;
    }

    static Guid Garantir(PlanoEnergia p)
    {
        if (!Ids.TryGetValue(p.Id, out var id)) return Guid.Empty;
        if (!Planos().Any(x => x.id == id))
        {
            // Sem o Alto desempenho, o de desempenho também parte do Equilibrado.
            var origem = p.Id == "desempenho" ? AltoDesempenho : EquilibradoWindows;
            if (!Duplicar(origem, id) && !Duplicar(EquilibradoWindows, id)) { Registro.Erro($"Não foi possível criar o plano de energia {Padroes.NomePlano(p.Id)}."); return Guid.Empty; }
        }
        var nome = Produto.Nome + " " + Padroes.NomePlano(p.Id);
        PowerWriteFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, nome, (uint)((nome.Length + 1) * 2));
        void W(Guid sub, Guid set, uint v) { var s = sub; var t = set; PowerWriteACValueIndex(IntPtr.Zero, ref id, ref s, ref t, v); PowerWriteDCValueIndex(IntPtr.Zero, ref id, ref s, ref t, v); }
        W(SubProc, ProcMin, (uint)p.ProcessadorMinimo);
        W(SubProc, ProcMax, (uint)p.ProcessadorMaximo);
        W(SubProc, NucleosMin, (uint)p.NucleosMinimos);
        W(SubProc, Preferencia, (uint)p.PreferenciaEnergia);
        W(SubProc, Impulso, p.Impulso switch { "desligado" => 0u, "ligado" => 1u, "agressivo" => 2u, "eficiente-ligado" => 3u, _ => 4u });
        W(SubUsb, UsbSuspensao, p.SuspensaoUsb ? 1u : 0u);
        W(SubPcie, Aspm, p.EconomiaPcie switch { "moderado" => 1u, "maximo" => 2u, _ => 0u });
        W(SubVideo, VideoOcioso, (uint)p.DesligarTela * 60);
        W(SubSono, SonoOcioso, (uint)p.Suspender * 60);
        // Fora da tela: hibernar e desligar o disco nunca acontecem sozinhos em nenhum plano.
        W(SubSono, HibernarOcioso, 0);
        W(SubDisco, DiscoOcioso, 0);
        LimitarPerfis(id, p.ProcessadorMaximo);
        // Regravar o plano ativo faz o Windows reler os valores sem trocar de plano.
        if (Ativo().id == id) PowerSetActiveScheme(IntPtr.Zero, ref id);
        return id;
    }

    static void LimitarPerfis(Guid plano, int maximo)
    {
        foreach (var perfil in PerfisQueSobem)
        {
            try
            {
                using var k = Registry.LocalMachine.CreateSubKey($@"{MinimoPorPerfil}\{perfil}\{plano}");
                k.SetValue("ProvAcSettingIndex", maximo, RegistryValueKind.DWord);
                k.SetValue("ProvDcSettingIndex", maximo, RegistryValueKind.DWord);
            }
            catch (Exception e) { Registro.Aviso($"Plano de energia: limite nos perfis do Windows não gravado ({e.Message})."); return; }
        }
    }

    static void TirarPerfis(Guid plano)
    {
        foreach (var perfil in PerfisQueSobem)
            try { Registry.LocalMachine.DeleteSubKeyTree($@"{MinimoPorPerfil}\{perfil}\{plano}", false); } catch { }
    }

    // Se um plano aposentado estava em uso, o Equilibrado próprio entra no lugar antes de ele sair.
    static void RemoverAntigo()
    {
        var existentes = Planos().Select(x => x.id).ToHashSet();
        foreach (var g in Aposentados.Where(existentes.Contains))
        {
            if (Ativo().id == g) { var eq = Ids["equilibrado"]; PowerSetActiveScheme(IntPtr.Zero, ref eq); }
            var a = g;
            PowerDeleteScheme(IntPtr.Zero, ref a);
            TirarPerfis(g);
        }
    }

    // Na remoção do programa o Equilibrado do Windows volta a ser o ativo.
    public static void RemoverPlanos()
    {
        try
        {
            var ativo = Ativo().id;
            if (Ids.ContainsValue(ativo) || Aposentados.Contains(ativo)) { var eq = EquilibradoWindows; PowerSetActiveScheme(IntPtr.Zero, ref eq); }
            foreach (var g in Ids.Values.Concat(Aposentados)) { var x = g; PowerDeleteScheme(IntPtr.Zero, ref x); TirarPerfis(g); }
        }
        catch { }
    }

    [DllImport("powrprof.dll")] static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
}
