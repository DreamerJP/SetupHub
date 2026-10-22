using System.Runtime.InteropServices;

namespace SetupHub.Servico.Hardware;

// O clock da biblioteca de sensores sai errado com o limite do plano.
sealed class FrequenciaReal : IDisposable
{
    [StructLayout(LayoutKind.Explicit)]
    struct Valor { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Numero; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQueryW(string? fonte, IntPtr usuario, out IntPtr consulta);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounterW(IntPtr consulta, string caminho, IntPtr usuario, out IntPtr contador);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr consulta);
    [DllImport("pdh.dll")] static extern uint PdhGetFormattedCounterValue(IntPtr contador, uint formato, out uint tipo, out Valor valor);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr consulta);

    const uint Formato = 0x200;

    IntPtr consulta;
    readonly List<IntPtr> reais = new();

    public bool Abrir(int logicos)
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out consulta) != 0) return false;
        for (int i = 0; i < logicos; i++)
        {
            if (PdhAddEnglishCounterW(consulta, $@"\Processor Information(0,{i})\Actual Frequency", IntPtr.Zero, out var c) != 0) { Fechar(); return false; }
            reais.Add(c);
        }
        PdhCollectQueryData(consulta);
        return true;
    }

    public void Coletar() { if (consulta != IntPtr.Zero) PdhCollectQueryData(consulta); }

    public double? Mhz(int logico)
    {
        if (consulta == IntPtr.Zero || logico < 0 || logico >= reais.Count) return null;
        if (PdhGetFormattedCounterValue(reais[logico], Formato, out _, out var v) != 0 || v.Status > 1 || v.Numero <= 0) return null;
        return Math.Round(v.Numero);
    }

    void Fechar()
    {
        if (consulta != IntPtr.Zero) PdhCloseQuery(consulta);
        consulta = IntPtr.Zero;
        reais.Clear();
    }

    public void Dispose() => Fechar();
}
