using SetupHub.Nucleo;
using LibreHardwareMonitor.Hardware;

namespace SetupHub.Servico.Hardware;

// LibreHardwareMonitor carregada dentro do serviço: nenhum programa externo no ciclo.
sealed class Sensores : IDisposable
{
    readonly Computer pc = new()
    {
        IsCpuEnabled = true,
        IsMotherboardEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsStorageEnabled = true,
    };
    IHardware? cpu, placa, gpu, memoria;
    public IHardware? Chip { get; private set; }
    readonly List<IHardware> discos = new();
    readonly FrequenciaReal frequencia = new();
    bool comFrequencia;
    // A mesma trava protege a biblioteca e a gravação direta no chip, que dividem o mesmo acesso ao hardware.
    public readonly object Trava = new();
    long ultimoDisco;
    public bool Aberto { get; private set; }
    public string? Falha { get; private set; }

    public void Abrir()
    {
        try
        {
            lock (Trava)
            {
                pc.Open();
                foreach (var h in pc.Hardware)
                {
                    switch (h.HardwareType)
                    {
                        case HardwareType.Cpu: cpu ??= h; break;
                        case HardwareType.Motherboard:
                            placa = h;
                            Chip = h.SubHardware.FirstOrDefault(s => s.HardwareType == HardwareType.SuperIO);
                            break;
                        case HardwareType.GpuAmd: gpu ??= h; break;
                        case HardwareType.GpuNvidia: case HardwareType.GpuIntel: gpu ??= h; break;
                        // A biblioteca mostra a memória física e a virtual (com paginação); vale a física.
                        case HardwareType.Memory when !h.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase): memoria ??= h; break;
                        case HardwareType.Storage: discos.Add(h); break;
                    }
                }
                foreach (var h in new[] { cpu, Chip, gpu, memoria }) h?.Update();
                foreach (var d in discos) d.Update();
                comFrequencia = frequencia.Abrir(Environment.ProcessorCount);
            }
            if (!comFrequencia) Registro.Aviso("Contador de frequência do Windows indisponível: o clock dos núcleos vem da biblioteca de sensores.");
            Aberto = true;
            Registro.Info($"Sensores abertos: processador {cpu?.Name}, chip {Chip?.Name}, placa de vídeo {gpu?.Name}, {discos.Count} discos.");
        }
        catch (Exception e)
        {
            Falha = e.Message;
            Registro.Erro("Falha ao abrir os sensores", e);
        }
    }

    public void Atualizar()
    {
        if (!Aberto) return;
        lock (Trava)
        {
            try { cpu?.Update(); } catch { }
            try { frequencia.Coletar(); } catch { }
            try { Chip?.Update(); } catch { }
            try { gpu?.Update(); } catch { }
            try { memoria?.Update(); } catch { }
            if (Environment.TickCount64 - ultimoDisco > 10000)
            {
                ultimoDisco = Environment.TickCount64;
                foreach (var d in discos) try { d.Update(); } catch { }
            }
        }
    }

    static double? V(IHardware? h, SensorType t, string nome)
    {
        if (h is null) return null;
        foreach (var s in h.Sensors)
            if (s.SensorType == t && s.Name == nome && s.Value is float v && !float.IsNaN(v)) return Valido(t, v);
        return null;
    }

    // Sem o driver de acesso, a biblioteca devolve zero em temperatura: zero grau dentro do PC é leitura ausente.
    static double? Valido(SensorType t, float v) => t == SensorType.Temperature && (v <= 0 || v > 150) ? null : v;

    static double? Comeca(IHardware? h, SensorType t, string prefixo)
    {
        if (h is null) return null;
        foreach (var s in h.Sensors)
            if (s.SensorType == t && s.Name.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase) && s.Value is float v && !float.IsNaN(v)) return Valido(t, v);
        return null;
    }

    public string NomeCpu => cpu?.Name ?? "";
    public string NomeGpu => gpu?.Name ?? "";
    public bool TemGpu => gpu is not null;

    public void Preencher(Estado e)
    {
        lock (Trava)
        {
            var c = e.Cpu;
            c.Nome = cpu?.Name ?? "";
            c.Temp = V(cpu, SensorType.Temperature, "Core (Tctl/Tdie)") ?? Comeca(cpu, SensorType.Temperature, "Core") ?? V(placa is null ? null : Chip, SensorType.Temperature, "CPU");
            c.TempCcd = V(cpu, SensorType.Temperature, "CCD1 (Tdie)") ?? Comeca(cpu, SensorType.Temperature, "CCD");
            // Sem acesso ao processador a leitura vem zerada; processador ligado nunca consome 0 W.
            c.Consumo = V(cpu, SensorType.Power, "Package") is double w && w > 0 ? w : null;
            c.Clock = V(cpu, SensorType.Clock, "Cores (Average)");
            c.Uso = V(cpu, SensorType.Load, "CPU Total");
            c.Nucleos.Clear();
            double maxClock = 0, maxVid = 0;
            int nucleos = 0;
            while (nucleos < 64 && V(cpu, SensorType.Clock, $"Core #{nucleos + 1}") is not null) nucleos++;
            // A biblioteca dá o uso por processador lógico: com SMT ligado são dois por núcleo, desligado um.
            int logicos = 0;
            while (logicos < 256 && V(cpu, SensorType.Load, $"CPU Core #{logicos + 1}") is not null) logicos++;
            int porNucleo = nucleos > 0 && logicos >= nucleos * 2 ? 2 : 1;
            double somaClock = 0; int comClock = 0;
            for (int i = 1; i <= nucleos; i++)
            {
                var vid = V(cpu, SensorType.Voltage, $"Core #{i} VID");
                double? uso = null, real = null;
                for (int k = 0; k < porNucleo; k++)
                {
                    int logico = (i - 1) * porNucleo + k;
                    if (V(cpu, SensorType.Load, $"CPU Core #{logico + 1}") is double u) uso = Math.Max(uso ?? 0, u);
                    if (comFrequencia && frequencia.Mhz(logico) is double f) real = Math.Max(real ?? 0, f);
                }
                var clk = real ?? V(cpu, SensorType.Clock, $"Core #{i}");
                if (clk is double cv) { somaClock += cv; comClock++; }
                c.Nucleos.Add(new LeituraNucleo
                {
                    Clock = clk,
                    ClockEfetivo = V(cpu, SensorType.Clock, $"Core #{i} (Effective)"),
                    Consumo = V(cpu, SensorType.Power, $"Core #{i} (SMU)"),
                    Tensao = vid,
                    Uso = uso,
                });
                if (clk > maxClock) maxClock = clk.Value;
                if (vid > maxVid) maxVid = vid.Value;
            }
            c.ClockMax = maxClock > 0 ? maxClock : null;
            if (comClock > 0) c.Clock = Math.Round(somaClock / comClock);
            c.Tensao = maxVid > 0 ? maxVid : null;

            var p = e.Placa;
            p.Sistema = V(Chip, SensorType.Temperature, "System");
            p.Vrm = V(Chip, SensorType.Temperature, "VRM MOS");
            p.Chipset = V(Chip, SensorType.Temperature, "PCH");
            p.Soquete = V(Chip, SensorType.Temperature, "CPU Socket");

            if (gpu is not null)
            {
                var g = e.Gpu;
                g.Nome = gpu.Name;
                g.Temp ??= V(gpu, SensorType.Temperature, "GPU Core");
                g.TempQuente ??= V(gpu, SensorType.Temperature, "GPU Hot Spot");
                g.TempMemoria ??= V(gpu, SensorType.Temperature, "GPU Memory");
                g.Consumo ??= V(gpu, SensorType.Power, "GPU Package") ?? V(gpu, SensorType.Power, "GPU Core");
                g.Clock ??= V(gpu, SensorType.Clock, "GPU Core");
                g.ClockMemoria ??= V(gpu, SensorType.Clock, "GPU Memory");
                g.Uso ??= V(gpu, SensorType.Load, "GPU Core");
                g.Tensao ??= V(gpu, SensorType.Voltage, "GPU Core");
                g.FanRpm ??= V(gpu, SensorType.Fan, "GPU Fan");
                g.FanPorcentagem ??= V(gpu, SensorType.Control, "GPU Fan");
                g.VramUsada ??= V(gpu, SensorType.SmallData, "GPU Memory Used");
                g.VramTotal ??= V(gpu, SensorType.SmallData, "GPU Memory Total");
            }

            var m = e.Memoria;
            var usada = V(memoria, SensorType.Data, "Memory Used");
            var livre = V(memoria, SensorType.Data, "Memory Available");
            m.UsadaGb = usada;
            m.TotalGb = usada is not null && livre is not null ? usada + livre : null;
            m.Uso = V(memoria, SensorType.Load, "Memory");

            e.Discos.Clear();
            foreach (var d in discos)
            {
                var t = V(d, SensorType.Temperature, "Temperature") ?? V(d, SensorType.Temperature, "Composite Temperature") ?? Comeca(d, SensorType.Temperature, "Temperature");
                if (t is not null) e.Discos.Add(new LeituraDisco { Nome = d.Name, Temp = t });
            }
        }
    }

    // Índice da biblioteca: o identificador termina em /fan/N.
    public double? Rpm(int indice) => Indice(SensorType.Fan, indice);
    public double? Controle(int indice) => Indice(SensorType.Control, indice);

    double? Indice(SensorType t, int indice)
    {
        if (Chip is null) return null;
        foreach (var s in Chip.Sensors)
        {
            if (s.SensorType != t) continue;
            var id = s.Identifier.ToString();
            int barra = id.LastIndexOf('/');
            if (barra >= 0 && int.TryParse(id.AsSpan(barra + 1), out var n) && n == indice) return s.Value is float v && !float.IsNaN(v) ? v : null;
        }
        return null;
    }

    public void Dispose()
    {
        try { lock (Trava) pc.Close(); } catch { }
        frequencia.Dispose();
    }
}
