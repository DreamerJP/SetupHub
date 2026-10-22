using System.Reflection;
using LibreHardwareMonitor.Hardware;

namespace SetupHub.Servico.Hardware;

// Da biblioteca só a escrita de byte: a devolução dela apaga a curva da BIOS e o ajuste às vezes não chega ao chip.
sealed class ChipVentoinhas
{
    readonly Sensores sensores;
    Func<ushort, byte>? ler;
    Action<ushort, byte>? escrever;
    Mutex? travaGlobal;
    public bool Pronto { get; private set; }
    public string? Motivo { get; private set; }

    public sealed record Saida(string Id, int Indice, bool Manual, ushort Registro, int BitModo);

    public static readonly Saida[] Saidas =
    {
        new("cpu", 0, true, 0xA28, 0),
        new("bomba", 1, true, 0xA29, 1),
        new("ez", 3, true, 0xA2B, 3),
        new("sys1", 10, false, 0xC70, -1),
        new("sys2", 11, false, 0xC58, -1),
        new("sys3", 12, false, 0xC40, -1),
        new("sys4", 13, false, 0xC28, -1),
    };

    public static Saida? Por(string id) => Saidas.FirstOrDefault(s => s.Id == id);

    public ChipVentoinhas(Sensores s) { sensores = s; }

    public void Abrir()
    {
        var chip = sensores.Chip;
        if (chip is null) { Motivo = "o chip de ventoinhas não foi encontrado (o PawnIO está instalado e o serviço roda como sistema?)"; return; }
        if (!chip.Name.Contains("6687", StringComparison.Ordinal)) { Motivo = $"chip {chip.Name} não é o NCT6687D-R desta placa; controle desligado por segurança"; return; }
        const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
        var campo = chip.GetType().GetFields(bf).FirstOrDefault(f => f.FieldType.Name == "ISuperIO");
        var sio = campo?.GetValue(chip);
        if (sio is null) { Motivo = "acesso ao chip não encontrado na biblioteca de sensores"; return; }
        var mLer = sio.GetType().GetMethod("ReadByte", bf, null, new[] { typeof(ushort) }, null);
        var mEsc = sio.GetType().GetMethod("WriteByte", bf, null, new[] { typeof(ushort), typeof(byte) }, null);
        if (mLer is null || mEsc is null) { Motivo = "leitura ou escrita do chip não encontrada na biblioteca"; return; }
        ler = (Func<ushort, byte>)mLer.CreateDelegate(typeof(Func<ushort, byte>), sio);
        escrever = (Action<ushort, byte>)mEsc.CreateDelegate(typeof(Action<ushort, byte>), sio);
        try { travaGlobal = Mutex.OpenExisting(@"Global\Access_ISABUS.HTP.Method"); }
        catch
        {
            try { travaGlobal = new Mutex(false, @"Global\Access_ISABUS.HTP.Method"); } catch { travaGlobal = null; }
        }
        Pronto = true;
    }

    // Mesma trava da biblioteca e de outros programas: não grava no meio do acesso de outro.
    T ComTrava<T>(Func<T> f)
    {
        lock (sensores.Trava)
        {
            bool pegou = false;
            try
            {
                if (travaGlobal is not null)
                {
                    try { pegou = travaGlobal.WaitOne(3000); } catch (AbandonedMutexException) { pegou = true; }
                }
                return f();
            }
            finally { if (pegou) try { travaGlobal!.ReleaseMutex(); } catch { } }
        }
    }

    byte L(ushort a) => ler!(a);
    void E(ushort a, byte v) => escrever!(a, v);

    void AbrirConfig()
    {
        E(0xA01, 0x80);
        for (int k = 0; k < 1000; k++)
        {
            var e = L(0xCF8);
            if ((e & 0x40) == 0 && (e & 0x08) != 0) return;
            Thread.SpinWait(2000);
        }
        throw new IOException("o chip não abriu a configuração");
    }

    void FecharConfig()
    {
        E(0xA01, 0x40);
        for (int k = 0; k < 1000; k++)
        {
            if ((L(0xCF8) & 0x20) != 0) return;
            Thread.SpinWait(2000);
        }
        throw new IOException("o chip não confirmou o fim da configuração");
    }

    public byte LerByte(ushort a) => ComTrava(() => L(a));

    public byte[] LerBloco(ushort inicio, int n = 14) => ComTrava(() =>
    {
        var b = new byte[n];
        for (int i = 0; i < n; i++) b[i] = L((ushort)(inicio + i));
        return b;
    });

    public (byte modo, Dictionary<string, byte> manual, Dictionary<string, byte[]> curvas) LerOriginais() => ComTrava(() =>
    {
        var modo = L(0xA00);
        var manual = new Dictionary<string, byte>();
        var curvas = new Dictionary<string, byte[]>();
        foreach (var s in Saidas)
        {
            if (s.Manual) manual[s.Id] = L(s.Registro);
            else
            {
                var b = new byte[14];
                for (int i = 0; i < 14; i++) b[i] = L((ushort)(s.Registro + i));
                curvas[s.Id] = b;
            }
        }
        return (modo, manual, curvas);
    });

    // Uma abertura só da configuração do chip para todas as saídas.
    public List<string> Gravar(IReadOnlyDictionary<string, int> porcentagens)
    {
        var falhas = new List<string>();
        if (!Pronto || porcentagens.Count == 0) return falhas;
        for (int tentativa = 1; tentativa <= 3; tentativa++)
        {
            falhas = ComTrava(() =>
            {
                var f = new List<string>();
                AbrirConfig();
                try
                {
                    byte modo = L(0xA00);
                    foreach (var (id, pct) in porcentagens)
                    {
                        var s = Por(id); if (s is null) continue;
                        byte v = (byte)Math.Clamp((int)Math.Round(pct * 2.55), 0, 255);
                        if (s.Manual)
                        {
                            modo |= (byte)(1 << s.BitModo);
                            E(0xA00, modo);
                            E(s.Registro, v);
                        }
                        else for (int o = 0; o < 14; o += 2) E((ushort)(s.Registro + o), v);
                    }
                }
                finally { FecharConfig(); }
                byte modoLido = L(0xA00);
                foreach (var (id, pct) in porcentagens)
                {
                    var s = Por(id); if (s is null) continue;
                    byte v = (byte)Math.Clamp((int)Math.Round(pct * 2.55), 0, 255);
                    bool ok;
                    if (s.Manual) ok = L(s.Registro) == v && (modoLido & (1 << s.BitModo)) != 0;
                    else { ok = true; for (int o = 0; o < 14; o += 2) if (L((ushort)(s.Registro + o)) != v) ok = false; }
                    if (!ok) f.Add(id);
                }
                return f;
            });
            if (falhas.Count == 0) return falhas;
            Registro.Aviso($"Gravação de ventoinha não conferiu ({string.Join(", ", falhas)}), tentativa {tentativa}.");
        }
        return falhas;
    }

    // Manual volta pelo bit de modo (a BIOS retoma sozinha); gabinete tem a curva original regravada byte a byte.
    public List<string> Devolver(IEnumerable<string> ids, byte modoOriginal, IReadOnlyDictionary<string, byte> manualOriginal, IReadOnlyDictionary<string, byte[]> curvasOriginais)
    {
        var lista = ids.Select(Por).Where(s => s is not null).Cast<Saida>().ToList();
        var falhas = new List<string>();
        if (!Pronto || lista.Count == 0) return falhas;
        for (int tentativa = 1; tentativa <= 3; tentativa++)
        {
            falhas = ComTrava(() =>
            {
                var f = new List<string>();
                AbrirConfig();
                try
                {
                    byte modo = L(0xA00);
                    foreach (var s in lista)
                    {
                        if (s.Manual)
                        {
                            if (manualOriginal.TryGetValue(s.Id, out var pv)) E(s.Registro, pv);
                            byte bit = (byte)(1 << s.BitModo);
                            modo = (byte)((modo & ~bit) | (modoOriginal & bit));
                        }
                        else if (curvasOriginais.TryGetValue(s.Id, out var b))
                            for (int o = 0; o < 14; o += 2) E((ushort)(s.Registro + o), b[o]);
                    }
                    E(0xA00, modo);
                }
                finally { FecharConfig(); }
                byte modoLido = L(0xA00);
                foreach (var s in lista)
                {
                    bool ok = true;
                    if (s.Manual)
                    {
                        byte bit = (byte)(1 << s.BitModo);
                        if ((modoLido & bit) != (modoOriginal & bit)) ok = false;
                    }
                    else if (curvasOriginais.TryGetValue(s.Id, out var b))
                        for (int o = 0; o < 14; o++) if (L((ushort)(s.Registro + o)) != b[o]) ok = false;
                    if (!ok) f.Add(s.Id);
                }
                return f;
            });
            if (falhas.Count == 0) return falhas;
            Registro.Aviso($"Devolução à BIOS não conferiu ({string.Join(", ", falhas)}), tentativa {tentativa}.");
        }
        return falhas;
    }

    public double? Aplicada(Saida s) => sensores.Controle(s.Indice);
    public double? Rpm(Saida s) => sensores.Rpm(s.Indice);
}
