using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SetupHub.Nucleo;

namespace SetupHub.Servico;

// Tamanho fixo, duas áreas circulares (segundos de 24 h, minutos de 30 dias); grava em blocos de um minuto.
sealed class Historico : IDisposable
{
    public static readonly string[] Canais =
    {
        "cpu.temp", "cpu.consumo", "cpu.clock", "cpu.uso", "gpu.temp", "gpu.quente", "gpu.memoria", "gpu.consumo",
        "gpu.clock", "gpu.uso", "placa.sistema", "placa.vrm", "placa.chipset", "ssd.temp", "memoria.uso", "fps",
        "fan.cpu", "fan.bomba", "fan.sys1", "fan.sys2", "fan.sys3", "fan.sys4", "fan.ez", "fan.gpu",
        "pct.cpu", "pct.sys1", "pct.sys2", "pct.sys3", "pct.gpu", "cpu.tensao", "gpu.vram", "gpu.tensao",
    };
    const int N = 32;
    const int Segundos = 86400, Minutos = 43200;
    const int TamSeg = 4 + N * 4, TamMin = 4 + N * 8;
    const long CabTam = 64, InicioSeg = CabTam, InicioMin = InicioSeg + (long)Segundos * TamSeg, Total = InicioMin + (long)Minutos * TamMin;
    const uint Assinatura = 0x43485354;

    readonly FileStream arq;
    readonly object trava = new();
    readonly List<(long ts, float[] v)> pendentes = new();
    long minutoAtual = -1;
    readonly double[] soma = new double[N];
    readonly float[] pico = new float[N];
    readonly int[] cont = new int[N];

    public Historico(string caminho)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        arq = new FileStream(caminho, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.RandomAccess);
        var cab = new byte[CabTam];
        bool valido = arq.Length == Total && arq.Read(cab, 0, (int)CabTam) == CabTam && BinaryPrimitives.ReadUInt32LittleEndian(cab) == Assinatura;
        if (!valido)
        {
            arq.SetLength(0);
            arq.SetLength(Total);
            BinaryPrimitives.WriteUInt32LittleEndian(cab, Assinatura);
            BinaryPrimitives.WriteInt32LittleEndian(cab.AsSpan(4), N);
            arq.Position = 0; arq.Write(cab); arq.Flush();
        }
    }

    public static int Indice(string canal) => Array.IndexOf(Canais, canal);

    public void Adicionar(long tsSeg, Dictionary<string, double> leituras)
    {
        var v = new float[N];
        for (int i = 0; i < N; i++) v[i] = leituras.TryGetValue(Canais[i], out var x) ? (float)x : float.NaN;
        lock (trava)
        {
            long min = tsSeg / 60;
            if (minutoAtual >= 0 && min != minutoAtual) Fechar();
            minutoAtual = min;
            pendentes.Add((tsSeg, v));
            for (int i = 0; i < N; i++)
            {
                if (float.IsNaN(v[i])) continue;
                soma[i] += v[i]; cont[i]++;
                if (cont[i] == 1 || v[i] > pico[i]) pico[i] = v[i];
            }
        }
    }

    void GravarSegundos()
    {
        var buf = new byte[TamSeg];
        foreach (var (ts, v) in pendentes)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf, (int)(ts - 1_700_000_000));
            for (int i = 0; i < N; i++) BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(4 + i * 4), v[i]);
            arq.Position = InicioSeg + (ts % Segundos) * TamSeg;
            arq.Write(buf);
        }
        pendentes.Clear();
    }

    void Fechar()
    {
        GravarSegundos();
        var m = new byte[TamMin];
        BinaryPrimitives.WriteInt32LittleEndian(m, (int)minutoAtual);
        for (int i = 0; i < N; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(m.AsSpan(4 + i * 4), cont[i] > 0 ? (float)(soma[i] / cont[i]) : float.NaN);
            BinaryPrimitives.WriteSingleLittleEndian(m.AsSpan(4 + N * 4 + i * 4), cont[i] > 0 ? pico[i] : float.NaN);
            soma[i] = 0; cont[i] = 0; pico[i] = 0;
        }
        arq.Position = InicioMin + (minutoAtual % Minutos) * TamMin;
        arq.Write(m);
        arq.Flush();
    }

    public void Descarregar() { lock (trava) { GravarSegundos(); arq.Flush(); } }

    // Cada ponto é média e pico do seu trecho.
    public SerieHistorico Serie(string sensor, long de, long ate, int pontos = 600)
    {
        int c = Indice(sensor);
        if (c < 0) throw new ErroSetupHub($"Sensor \"{sensor}\" não tem histórico. Sensores: {string.Join(", ", Canais)}.");
        pontos = Math.Clamp(pontos, 10, 5000);
        long agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        de = Math.Max(de, agora - Minutos * 60L); ate = Math.Min(ate, agora);
        if (ate <= de) return new SerieHistorico { Sensor = sensor, Inicio = de, Passo = 1 };
        bool porSegundo = de >= agora - Segundos + 60;
        var (tempos, medias, picos) = porSegundo ? LerSegundos(c, de, ate) : LerMinutos(c, de / 60, ate / 60);
        int passoBase = porSegundo ? 1 : 60;
        long n = (ate - de) / passoBase + 1;
        int grupo = (int)Math.Max(1, Math.Ceiling(n / (double)pontos));
        int saida = (int)Math.Ceiling(n / (double)grupo);
        var vals = new double[saida]; var pk = new double[saida];
        var qtd = new int[saida];
        for (int i = 0; i < saida; i++) { vals[i] = double.NaN; pk[i] = double.NaN; }
        for (int k = 0; k < tempos.Count; k++)
        {
            long idx = (tempos[k] * (porSegundo ? 1 : 60) - de) / passoBase / grupo;
            if (idx < 0 || idx >= saida) continue;
            int j = (int)idx;
            vals[j] = qtd[j] == 0 ? medias[k] : vals[j] + medias[k];
            pk[j] = double.IsNaN(pk[j]) ? picos[k] : Math.Max(pk[j], picos[k]);
            qtd[j]++;
        }
        for (int i = 0; i < saida; i++) if (qtd[i] > 0) vals[i] /= qtd[i];
        return new SerieHistorico { Sensor = sensor, Inicio = de, Passo = passoBase * grupo, Valores = vals, Picos = pk };
    }

    (List<long>, List<double>, List<double>) LerSegundos(int c, long de, long ate)
    {
        Descarregar();
        var t = new List<long>(); var m = new List<double>();
        var buf = new byte[TamSeg];
        lock (trava)
        {
            using var leitor = new FileStream(arq.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            for (long ts = de; ts <= ate; ts++)
            {
                leitor.Position = InicioSeg + (ts % Segundos) * TamSeg;
                if (leitor.Read(buf, 0, TamSeg) != TamSeg) continue;
                if (BinaryPrimitives.ReadInt32LittleEndian(buf) + 1_700_000_000L != ts) continue;
                var v = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(4 + c * 4));
                if (float.IsNaN(v)) continue;
                t.Add(ts); m.Add(v);
            }
            foreach (var (ts, v) in pendentes) if (ts >= de && ts <= ate && !float.IsNaN(v[c]) && !t.Contains(ts)) { t.Add(ts); m.Add(v[c]); }
        }
        return (t, m, m);
    }

    (List<long>, List<double>, List<double>) LerMinutos(int c, long de, long ate)
    {
        Descarregar();
        var t = new List<long>(); var m = new List<double>(); var p = new List<double>();
        var buf = new byte[TamMin];
        lock (trava)
        {
            using var leitor = new FileStream(arq.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            for (long mn = de; mn <= ate; mn++)
            {
                leitor.Position = InicioMin + (mn % Minutos) * TamMin;
                if (leitor.Read(buf, 0, TamMin) != TamMin) continue;
                if (BinaryPrimitives.ReadInt32LittleEndian(buf) != (int)mn) continue;
                var v = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(4 + c * 4));
                var k = BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(4 + N * 4 + c * 4));
                if (float.IsNaN(v)) continue;
                t.Add(mn); m.Add(v); p.Add(float.IsNaN(k) ? v : k);
            }
        }
        return (t, m, p);
    }

    // Ponto e vírgula e vírgula decimal: o Excel em português abre direto.
    public int Exportar(string caminho, long de, long ate)
    {
        long agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool porSegundo = de >= agora - Segundos + 60;
        var br = CultureInfo.GetCultureInfo("pt-BR");
        var series = Canais.Select(c => Serie(c, de, ate, porSegundo ? (int)Math.Min(5000, ate - de + 1) : (int)Math.Min(5000, (ate - de) / 60 + 1))).ToList();
        var sb = new StringBuilder();
        sb.Append("hora;").AppendLine(string.Join(';', Canais));
        int linhas = series.Max(s => s.Valores.Length);
        for (int i = 0; i < linhas; i++)
        {
            long ts = series[0].Inicio + (long)i * series[0].Passo;
            sb.Append(DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", br));
            foreach (var s in series) { sb.Append(';'); if (i < s.Valores.Length && !double.IsNaN(s.Valores[i])) sb.Append(s.Valores[i].ToString("0.##", br)); }
            sb.AppendLine();
        }
        File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(true));
        return linhas;
    }

    public void Dispose()
    {
        try { Descarregar(); arq.Dispose(); } catch { }
    }
}
