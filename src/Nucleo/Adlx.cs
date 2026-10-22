using System.Runtime.InteropServices;

namespace SetupHub.Nucleo;

// Chama direto a tabela de funções de cada interface do ADLX, sem biblioteca intermediária.
public sealed unsafe class Adlx : IDisposable
{
    [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr LoadLibraryA(string f);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr m, string n);
    [StructLayout(LayoutKind.Sequential)] struct Faixa { public int Min, Max, Passo; }

    IntPtr dll, sys, gpu, tuning, s3d, s3d1, dsrv, dsrv3, perf;
    IntPtr gfx, vram, fan, power;
    int gfxVersao, vramVersao, fanVersao, powerVersao;
    readonly object trava = new();
    public bool Aberto { get; private set; }
    public string? Motivo { get; private set; }
    public string NomeGpu { get; private set; } = "";
    public string Driver { get; private set; } = "";
    public int VramMb { get; private set; }

    // Campo nulo na configuração volta ao valor lido na abertura, só se o SetupHub o tiver gravado.
    public ConfigPlacaDeVideo Inicial { get; private set; } = new();
    public ConfigPlacaDeVideo Gravado { get; private set; } = new();
    public Ajustes3D Inicial3D { get; private set; } = new();
    public Ajustes3D Gravado3D { get; private set; } = new();
    public VentoinhaGpu InicialFan { get; private set; } = new();
    bool fanGravada;
    readonly Dictionary<string, AjusteTela> inicialTelas = new();
    readonly Dictionary<string, AjusteTela> gravadoTelas = new();

    static IntPtr Slot(IntPtr o, int i) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size);
    static int Rel(IntPtr o) => o == IntPtr.Zero ? 0 : ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(o, 1))(o);
    static int Qi(IntPtr o, string iid, out IntPtr r)
    {
        IntPtr x = IntPtr.Zero; int res;
        fixed (char* p = iid) res = ((delegate* unmanaged[Stdcall]<IntPtr, char*, IntPtr*, int>)Slot(o, 2))(o, p, &x);
        r = x; return res;
    }
    static int Ptr(IntPtr o, int i, out IntPtr r) { IntPtr x = IntPtr.Zero; int res = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(o, i))(o, &x); r = x; return res; }
    static int PtrDe(IntPtr o, int i, IntPtr arg, out IntPtr r) { IntPtr x = IntPtr.Zero; int res = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(o, i))(o, arg, &x); r = x; return res; }
    static bool Bool(IntPtr o, int i) { byte b = 0; return o != IntPtr.Zero && ((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)Slot(o, i))(o, &b) == 0 && b != 0; }
    static bool BoolDe(IntPtr o, int i, IntPtr arg) { byte b = 0; return o != IntPtr.Zero && ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, byte*, int>)Slot(o, i))(o, arg, &b) == 0 && b != 0; }
    static int SetBool(IntPtr o, int i, bool v) => ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Slot(o, i))(o, (byte)(v ? 1 : 0));
    static int? Int(IntPtr o, int i) { int v = 0; return o != IntPtr.Zero && ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(o, i))(o, &v) == 0 ? v : null; }
    static int SetInt(IntPtr o, int i, int v) => ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(o, i))(o, v);
    static double? Dbl(IntPtr o, int i) { double v = 0; return o != IntPtr.Zero && ((delegate* unmanaged[Stdcall]<IntPtr, double*, int>)Slot(o, i))(o, &v) == 0 ? v : null; }
    static Faixa? Rng(IntPtr o, int i) { Faixa f = default; return o != IntPtr.Zero && ((delegate* unmanaged[Stdcall]<IntPtr, Faixa*, int>)Slot(o, i))(o, &f) == 0 ? f : null; }
    static int Acao(IntPtr o, int i) => ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(o, i))(o);
    static uint Tamanho(IntPtr lista) => ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(lista, 3))(lista);
    static int Item(IntPtr lista, int slot, uint i, out IntPtr r) { IntPtr x = IntPtr.Zero; int res = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(lista, slot))(lista, i, &x); r = x; return res; }
    static string? Texto(IntPtr o, int i) { sbyte* s = null; return o != IntPtr.Zero && ((delegate* unmanaged[Stdcall]<IntPtr, sbyte**, int>)Slot(o, i))(o, &s) == 0 && s != null ? new string(s) : null; }

    public bool Abrir()
    {
        lock (trava)
        {
            if (Aberto) return true;
            try
            {
                dll = LoadLibraryA("amdadlx64.dll");
                if (dll == IntPtr.Zero) { Motivo = "o kit ADLX não está instalado (driver da AMD ausente)"; return false; }
                var qv = (delegate* unmanaged[Cdecl]<ulong*, int>)GetProcAddress(dll, "ADLXQueryFullVersion");
                var ini = (delegate* unmanaged[Cdecl]<ulong, IntPtr*, int>)GetProcAddress(dll, "ADLXInitialize");
                ulong ver = 0; qv(&ver);
                IntPtr s = IntPtr.Zero;
                int r = ini(ver, &s);
                if (r != 0 || s == IntPtr.Zero) { Motivo = $"o kit ADLX não iniciou (código {r})"; return false; }
                sys = s;
                if (Ptr(sys, 1, out var lista) != 0 || Tamanho(lista) == 0) { Motivo = "nenhuma placa de vídeo AMD encontrada"; return false; }
                Item(lista, 11, 0, out gpu);
                Rel(lista);
                NomeGpu = Texto(gpu, 7) ?? "";
                VramMb = Total(gpu);
                Ptr(sys, 8, out tuning);
                Ptr(sys, 7, out s3d);
                if (s3d != IntPtr.Zero) Qi(s3d, "IADLX3DSettingsServices1", out s3d1);
                Ptr(sys, 3, out dsrv);
                if (dsrv != IntPtr.Zero) Qi(dsrv, "IADLXDisplayServices3", out dsrv3);
                Ptr(sys, 9, out perf);
                AbrirAjustes();
                Driver = LerDriver();
                Aberto = true;
                Motivo = null;
                return true;
            }
            catch (Exception e) { Motivo = "falha no kit ADLX: " + e.Message; return false; }
        }
    }

    static int Total(IntPtr g) { uint v = 0; return ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(g, 11))(g, &v) == 0 ? (int)v : 0; }

    string LerDriver()
    {
        try
        {
            using var classe = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classe is null) return "";
            foreach (var sub in classe.GetSubKeyNames())
            {
                using var k = classe.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc")?.ToString() != NomeGpu) continue;
                var pacote = k.GetValue("RadeonSoftwareVersion")?.ToString();
                var driver = k.GetValue("DriverVersion")?.ToString();
                return string.IsNullOrEmpty(pacote) ? driver ?? "" : $"{pacote} ({driver})";
            }
        }
        catch { }
        return "";
    }

    void AbrirAjustes()
    {
        if (tuning == IntPtr.Zero) return;
        if (BoolDe(tuning, 8, gpu) && PtrDe(tuning, 14, gpu, out var g) == 0)
        {
            if (Qi(g, "IADLXManualGraphicsTuning2_1", out gfx) == 0) gfxVersao = 21;
            else if (Qi(g, "IADLXManualGraphicsTuning2", out gfx) == 0) gfxVersao = 2;
            Rel(g);
        }
        if (BoolDe(tuning, 9, gpu) && PtrDe(tuning, 15, gpu, out var v) == 0)
        {
            if (Qi(v, "IADLXManualVRAMTuning2_1", out vram) == 0) vramVersao = 21;
            else if (Qi(v, "IADLXManualVRAMTuning2", out vram) == 0) vramVersao = 2;
            Rel(v);
        }
        if (BoolDe(tuning, 10, gpu) && PtrDe(tuning, 16, gpu, out var fa) == 0)
        {
            if (Qi(fa, "IADLXManualFanTuning1", out fan) == 0) fanVersao = 1;
            else if (Qi(fa, "IADLXManualFanTuning", out fan) == 0) fanVersao = 0;
            Rel(fa);
        }
        if (BoolDe(tuning, 11, gpu) && PtrDe(tuning, 17, gpu, out var po) == 0)
        {
            if (Qi(po, "IADLXManualPowerTuning1", out power) == 0) powerVersao = 1;
            else if (Qi(po, "IADLXManualPowerTuning", out power) == 0) powerVersao = 0;
            Rel(po);
        }
    }

    public void Ler(LeituraGpu g)
    {
        lock (trava)
        {
            if (!Aberto || perf == IntPtr.Zero) return;
            if (PtrDe(perf, 18, gpu, out var m) != 0 || m == IntPtr.Zero) return;
            try
            {
                g.Nome = NomeGpu;
                g.Uso = Dbl(m, 4);
                g.Clock = Int(m, 5);
                g.ClockMemoria = Int(m, 6);
                g.Temp = Dbl(m, 7);
                g.TempQuente = Dbl(m, 8);
                g.ConsumoTotal = Dbl(m, 10);
                g.Consumo = Dbl(m, 9) ?? g.ConsumoTotal;
                g.FanRpm = Int(m, 11);
                var vr = Int(m, 12);
                if (vr is int mb) { g.VramUsada = mb / 1024.0; g.VramTotal = VramMb / 1024.0; }
                var t = Int(m, 13); if (t is int mv) g.Tensao = mv / 1000.0;
                g.TempEntrada = Dbl(m, 14);
                if (Qi(m, "IADLXGPUMetrics1", out var m1) == 0) { g.TempMemoria = Dbl(m1, 15); Rel(m1); }
                if (Qi(m, "IADLXGPUMetrics3", out var m3) == 0) { var d = Int(m3, 19); if (d is int duty && duty >= 0) g.FanPorcentagem = duty; Rel(m3); }
                if (g.TempMemoria is <= 0) g.TempMemoria = null;
                if (g.TempEntrada is <= 0) g.TempEntrada = null;
            }
            finally { Rel(m); }
        }
    }

    public double? Fps()
    {
        lock (trava)
        {
            if (!Aberto || perf == IntPtr.Zero) return null;
            if (Ptr(perf, 20, out var f) != 0 || f == IntPtr.Zero) return null;
            try { var v = Int(f, 4); return v is > 0 ? v : null; } finally { Rel(f); }
        }
    }

    static void Por(Faixas f, string chave, Faixa? r, int? atual, int? padrao, string unidade, string sem)
    {
        f.Campos[chave] = r is Faixa x && x.Max >= x.Min
            ? new Nucleo.Faixa { Min = x.Min, Max = x.Max, Passo = Math.Max(1, x.Passo), Atual = atual, Padrao = padrao, Unidade = unidade }
            : new Nucleo.Faixa { Disponivel = false, Motivo = sem, Unidade = unidade };
    }

    static void Opcao(Faixas f, string chave, bool sup, double? atual, string sem) =>
        f.Campos[chave] = new Nucleo.Faixa { Disponivel = sup, Motivo = sup ? null : sem, Min = 0, Max = 1, Atual = atual };

    public void Faixas(Faixas f)
    {
        lock (trava)
        {
            if (!Aberto)
            {
                foreach (var k in new[] { "clockMin", "clockMax", "tensao", "limiteEnergia", "clockMemoria", "memoriaRapida" })
                    f.Campos["placaDeVideo." + k] = new Nucleo.Faixa { Disponivel = false, Motivo = Motivo ?? "o kit ADLX não abriu" };
                return;
            }
            f.AdlxDisponivel = true;
            var info = new InfoGpu { Nome = NomeGpu, Driver = Driver, VramMb = VramMb, DeFabrica = BoolDe(tuning, 4, gpu) };
            const string semGpu = "o driver não informou esta faixa";
            Por(f, "placaDeVideo.clockMin", gfx != IntPtr.Zero ? Rng(gfx, 3) : null, Int(gfx, 4), gfxVersao == 21 ? Int(gfx, 12) : null, "MHz", semGpu);
            Por(f, "placaDeVideo.clockMax", gfx != IntPtr.Zero ? Rng(gfx, 6) : null, Int(gfx, 7), gfxVersao == 21 ? Int(gfx, 13) : null, "MHz", semGpu);
            Por(f, "placaDeVideo.tensao", gfx != IntPtr.Zero ? Rng(gfx, 9) : null, Int(gfx, 10), gfxVersao == 21 ? Int(gfx, 14) : null, "mV", semGpu);
            Por(f, "placaDeVideo.limiteEnergia", power != IntPtr.Zero ? Rng(power, 3) : null, Int(power, 4), powerVersao == 1 ? Int(power, 10) : 0, "%", semGpu);
            Por(f, "placaDeVideo.clockMemoria", vram != IntPtr.Zero ? Rng(vram, 7) : null, Int(vram, 8), vramVersao == 21 ? Int(vram, 10) : null, "MHz", semGpu);
            bool tempos = vram != IntPtr.Zero && Bool(vram, 3);
            Opcao(f, "placaDeVideo.memoriaRapida", tempos, tempos && TempoMemoria() is bool tr ? (tr ? 1 : 0) : null, "o driver não aceita mudar os tempos da memória");
            if (fan != IntPtr.Zero)
            {
                Faixa vel = default, temp = default;
                bool ok = ((delegate* unmanaged[Stdcall]<IntPtr, Faixa*, Faixa*, int>)Slot(fan, 3))(fan, &vel, &temp) == 0;
                f.Campos["ventoinhas.gpu.velocidade"] = ok ? new Nucleo.Faixa { Min = vel.Min, Max = vel.Max, Passo = Math.Max(1, vel.Passo), Unidade = "%" } : new Nucleo.Faixa { Disponivel = false, Motivo = semGpu };
                f.Campos["ventoinhas.gpu.temperatura"] = ok ? new Nucleo.Faixa { Min = temp.Min, Max = temp.Max, Passo = Math.Max(1, temp.Passo), Unidade = "°C" } : new Nucleo.Faixa { Disponivel = false, Motivo = semGpu };
                Opcao(f, "ventoinhas.gpu.paradaEmRepouso", Bool(fan, 8), Bool(fan, 9) ? 1 : 0, "o driver não aceita a ventoinha parada em repouso");
                if (Bool(fan, 11)) Por(f, "ventoinhas.gpu.ruidoMinimo", Rng(fan, 12), Int(fan, 13), fanVersao == 1 ? Int(fan, 24) : null, "MHz", semGpu);
                else f.Campos["ventoinhas.gpu.ruidoMinimo"] = new Nucleo.Faixa { Disponivel = false, Motivo = "o driver não aceita o limite mínimo de ruído" };
                info.Suporta.Add("ventoinha");
            }
            // O fim da faixa já inclui o acréscimo máximo do limite de energia.
            var acrescimo = power != IntPtr.Zero ? Rng(power, 3) : null;
            f.Campos["placaDeVideo.consumoMaximo"] = ConsumoMaximo() is int w
                ? new Nucleo.Faixa { Min = 0, Max = w, Padrao = Math.Round(w / (1 + Math.Max(0, acrescimo?.Max ?? 0) / 100.0)), Unidade = "W" }
                : new Nucleo.Faixa { Disponivel = false, Motivo = semGpu, Unidade = "W" };
            if (gfx != IntPtr.Zero) info.Suporta.Add("clock");
            if (power != IntPtr.Zero) info.Suporta.Add("energia");
            if (vram != IntPtr.Zero) info.Suporta.Add("memoria");
            f.Gpu = info;
            if (!f.AreasLidas.Contains("placaDeVideo")) f.AreasLidas.Add("placaDeVideo");
            Faixas3D(f);
            FaixasTelas(f);
        }
    }

    int? ConsumoMaximo()
    {
        if (perf == IntPtr.Zero || PtrDe(perf, 21, gpu, out var sup) != 0 || sup == IntPtr.Zero) return null;
        try
        {
            if (!Bool(sup, 9)) return null;
            int min = 0, max = 0;
            return ((delegate* unmanaged[Stdcall]<IntPtr, int*, int*, int>)Slot(sup, 22))(sup, &min, &max) == 0 && max > 0 ? max : null;
        }
        finally { Rel(sup); }
    }

    bool? TempoMemoria()
    {
        int d = 0;
        if (((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(vram, 5))(vram, &d) != 0) return null;
        return d is 1 or 2;
    }

    IntPtr Obter3D(int slot, bool comGpu)
    {
        if (s3d1 == IntPtr.Zero && s3d == IntPtr.Zero) return IntPtr.Zero;
        var srv = slot == 17 ? s3d1 : (s3d1 != IntPtr.Zero ? s3d1 : s3d);
        if (srv == IntPtr.Zero) return IntPtr.Zero;
        int r; IntPtr x;
        if (comGpu) r = PtrDe(srv, slot, gpu, out x); else r = Ptr(srv, slot, out x);
        return r == 0 ? x : IntPtr.Zero;
    }

    void Faixas3D(Faixas f)
    {
        void Liga(string chave, int slot, bool comGpu, int suporte, int ligado)
        {
            var o = Obter3D(slot, comGpu);
            bool sup = o != IntPtr.Zero && Bool(o, suporte);
            Opcao(f, "graficos." + chave, sup, sup ? (Bool(o, ligado) ? 1 : 0) : null, "o driver não aceita este ajuste nesta placa");
            Rel(o);
        }
        Liga("antiLag", 3, true, 3, 4);
        Liga("enhancedSync", 7, true, 3, 4);
        Liga("afmf", 17, false, 3, 4);
        var chill = Obter3D(4, true);
        Opcao(f, "graficos.chill", chill != IntPtr.Zero && Bool(chill, 3), Bool(chill, 4) ? 1 : 0, "o driver não aceita o Chill");
        Por(f, "graficos.chillFps", chill != IntPtr.Zero ? Rng(chill, 5) : null, Int(chill, 6), null, "fps", "o driver não informou a faixa do Chill");
        Rel(chill);
        var boost = Obter3D(5, true);
        Opcao(f, "graficos.boost", boost != IntPtr.Zero && Bool(boost, 3), Bool(boost, 4) ? 1 : 0, "o driver não aceita o Boost");
        Por(f, "graficos.boostResolucaoMin", boost != IntPtr.Zero ? Rng(boost, 5) : null, Int(boost, 6), null, "%", "o driver não informou a faixa do Boost");
        Rel(boost);
        var ris = Obter3D(6, true);
        Opcao(f, "graficos.nitidez", ris != IntPtr.Zero && Bool(ris, 3), Bool(ris, 4) ? 1 : 0, "o driver não aceita a nitidez de imagem");
        Por(f, "graficos.nitidezNivel", ris != IntPtr.Zero ? Rng(ris, 5) : null, Int(ris, 6), null, "%", "o driver não informou a faixa da nitidez");
        Rel(ris);
        var vs = Obter3D(8, true);
        Opcao(f, "graficos.vsync", vs != IntPtr.Zero && Bool(vs, 3), Int(vs, 5), "o driver não aceita mudar a sincronia vertical");
        Rel(vs);
        var frtc = Obter3D(9, true);
        Opcao(f, "graficos.frtc", frtc != IntPtr.Zero && Bool(frtc, 3), Bool(frtc, 4) ? 1 : 0, "o driver não aceita o limite de quadros próprio");
        Por(f, "graficos.frtcFps", frtc != IntPtr.Zero ? Rng(frtc, 5) : null, Int(frtc, 6), null, "fps", "o driver não informou a faixa do limite de quadros");
        Rel(frtc);
        var aa = Obter3D(10, true);
        Opcao(f, "graficos.suavizacao", aa != IntPtr.Zero && Bool(aa, 3), Int(aa, 4), "o driver não aceita mudar a suavização de bordas");
        Rel(aa);
        var af = Obter3D(12, true);
        Opcao(f, "graficos.anisotropica", af != IntPtr.Zero && Bool(af, 3), Bool(af, 4) ? 1 : 0, "o driver não aceita a filtragem de texturas");
        f.Campos["graficos.anisotropicaNivel"] = af != IntPtr.Zero && Bool(af, 3) ? new Nucleo.Faixa { Min = 2, Max = 16, Passo = 2, Atual = Int(af, 5), Unidade = "x" } : new Nucleo.Faixa { Disponivel = false, Motivo = "o driver não aceita a filtragem de texturas" };
        Rel(af);
        var tes = Obter3D(13, true);
        Opcao(f, "graficos.tesselacao", tes != IntPtr.Zero && Bool(tes, 3), Int(tes, 4), "o driver não aceita mudar a tesselação");
        f.Campos["graficos.tesselacaoNivel"] = tes != IntPtr.Zero && Bool(tes, 3) ? new Nucleo.Faixa { Min = 1, Max = 64, Passo = 1, Atual = Int(tes, 5), Unidade = "x" } : new Nucleo.Faixa { Disponivel = false, Motivo = "o driver não aceita mudar a tesselação" };
        Rel(tes);
        var rsr = Obter3D(14, false);
        Opcao(f, "graficos.rsr", rsr != IntPtr.Zero && Bool(rsr, 3), Bool(rsr, 4) ? 1 : 0, "o driver não aceita o Radeon Super Resolution");
        Por(f, "graficos.rsrNitidez", rsr != IntPtr.Zero ? Rng(rsr, 6) : null, Int(rsr, 7), null, "%", "o driver não informou a faixa do RSR");
        Rel(rsr);
        var cache = Obter3D(15, true);
        Opcao(f, "graficos.limparCache", cache != IntPtr.Zero && Bool(cache, 3), null, "o driver não aceita limpar o cache de sombreadores");
        Rel(cache);
        if (!f.AreasLidas.Contains("graficos")) f.AreasLidas.Add("graficos");
        Inicial3D = Ler3D();
    }

    List<(IntPtr d, string id, string nome)> Telas()
    {
        var r = new List<(IntPtr, string, string)>();
        var srv = dsrv3 != IntPtr.Zero ? dsrv3 : dsrv;
        if (srv == IntPtr.Zero || Ptr(srv, 4, out var lista) != 0) return r;
        uint n = Tamanho(lista);
        for (uint i = 0; i < n; i++)
        {
            if (Item(lista, 11, i, out var d) != 0) continue;
            nuint uid = 0;
            ((delegate* unmanaged[Stdcall]<IntPtr, nuint*, int>)Slot(d, 13))(d, &uid);
            r.Add((d, uid.ToString(), Texto(d, 6) ?? $"Tela {i + 1}"));
        }
        Rel(lista);
        return r;
    }

    IntPtr DaTela(int slot, IntPtr d)
    {
        var srv = dsrv3 != IntPtr.Zero ? dsrv3 : dsrv;
        if (srv == IntPtr.Zero) return IntPtr.Zero;
        if (slot >= 20 && dsrv3 == IntPtr.Zero) return IntPtr.Zero;
        return PtrDe(srv, slot, d, out var x) == 0 ? x : IntPtr.Zero;
    }

    void FaixasTelas(Faixas f)
    {
        f.Telas.Clear();
        foreach (var (d, id, nome) in Telas())
        {
            var info = new InfoTela { Id = id, Nome = nome };
            int w = 0, h = 0; double hz = 0;
            ((delegate* unmanaged[Stdcall]<IntPtr, int*, int*, int>)Slot(d, 8))(d, &w, &h);
            ((delegate* unmanaged[Stdcall]<IntPtr, double*, int>)Slot(d, 9))(d, &hz);
            info.Largura = w; info.Altura = h; info.Hz = Math.Round(hz, 2);
            var inicial = new AjusteTela { Id = id, Nome = nome };
            void Simples(int slot, string chave, Action<bool> guardar)
            {
                var o = DaTela(slot, d);
                if (o != IntPtr.Zero && Bool(o, 3)) { info.Suporta.Add(chave); guardar(Bool(o, 4)); }
                Rel(o);
            }
            Simples(9, "freesync", v => inicial.FreeSync = v);
            Simples(10, "vsr", v => inicial.Vsr = v);
            Simples(11, "escalaGpu", v => inicial.EscalaGpu = v);
            Simples(13, "escalaInteira", v => inicial.EscalaInteira = v);
            Simples(22, "atualizacaoDinamica", v => inicial.AtualizacaoDinamica = v);
            var esc = DaTela(12, d);
            if (esc != IntPtr.Zero && Bool(esc, 3)) { info.Suporta.Add("modoEscala"); inicial.ModoEscala = Int(esc, 4) switch { 1 => "tela-cheia", 2 => "centralizado", _ => "preservar" }; }
            Rel(esc);
            var cd = DaTela(14, d);
            if (cd != IntPtr.Zero && Bool(cd, 3))
            {
                info.Suporta.Add("profundidadeCor"); inicial.ProfundidadeCor = Int(cd, 4) switch { 1 => 6, 2 => 8, 3 => 10, 4 => 12, 5 => 14, 6 => 16, _ => null };
                foreach (var (bits, cod) in new[] { (6, 1), (8, 2), (10, 3), (12, 4) }) if (BoolDe(cd, 6, (IntPtr)cod)) info.Profundidades.Add(bits);
            }
            Rel(cd);
            var pf = DaTela(15, d);
            if (pf != IntPtr.Zero && Bool(pf, 3))
            {
                info.Suporta.Add("formatoPixel"); inicial.FormatoPixel = Formato(Int(pf, 4));
                for (int cod = 1; cod <= 5; cod++) if (BoolDe(pf, 6, (IntPtr)cod) && Formato(cod) is string fm) info.Formatos.Add(fm);
            }
            Rel(pf);
            var cc = DaTela(16, d);
            if (cc != IntPtr.Zero)
            {
                void Cor(string chave, int sup, int faixa, int atual, Action<int?> guardar)
                {
                    if (Bool(cc, sup)) { Por(f, $"telas.{id}.{chave}", Rng(cc, faixa), Int(cc, atual), null, "", ""); guardar(Int(cc, atual)); if (!info.Suporta.Contains("cor")) info.Suporta.Add("cor"); }
                    else f.Campos[$"telas.{id}.{chave}"] = new Nucleo.Faixa { Disponivel = false, Motivo = "a tela não aceita este ajuste de cor" };
                }
                Cor("matiz", 3, 4, 5, v => inicial.Matiz = v);
                Cor("saturacao", 7, 8, 9, v => inicial.Saturacao = v);
                Cor("brilho", 11, 12, 13, v => inicial.Brilho = v);
                Cor("contraste", 15, 16, 17, v => inicial.Contraste = v);
                Cor("temperatura", 19, 20, 21, v => inicial.Temperatura = v);
            }
            Rel(cc);
            var vb = DaTela(19, d);
            if (vb != IntPtr.Zero && Bool(vb, 3))
            {
                info.Suporta.Add("variBright");
                inicial.VariBright = Bool(vb, 4);
                f.Campos[$"telas.{id}.variBrightNivel"] = new Nucleo.Faixa { Min = 0, Max = 4, Passo = 1, Atual = NivelVb(vb) };
            }
            Rel(vb);
            var gm = DaTela(6, d);
            if (gm != IntPtr.Zero) info.Suporta.Add("gamut");
            Rel(gm);
            var ga = DaTela(7, d);
            if (ga != IntPtr.Zero) info.Suporta.Add("gama");
            Rel(ga);
            f.Telas.Add(info);
            inicialTelas.TryAdd(id, inicial);
            Rel(d);
        }
        if (!f.AreasLidas.Contains("telas")) f.AreasLidas.Add("telas");
    }

    static int? NivelVb(IntPtr vb)
    {
        for (int i = 0; i < 5; i++) if (Bool(vb, 6 + i)) return i;
        return null;
    }

    static string? Formato(int? v) => v switch { 1 => "rgb-completo", 2 => "ycbcr444", 3 => "ycbcr422", 4 => "rgb-limitado", 5 => "ycbcr420", _ => null };
    static int FormatoCodigo(string s) => s switch { "rgb-completo" => 1, "ycbcr444" => 2, "ycbcr422" => 3, "rgb-limitado" => 4, "ycbcr420" => 5, _ => 1 };

    public List<string> AplicarGpu(ConfigPlacaDeVideo alvo)
    {
        var falhas = new List<string>();
        lock (trava)
        {
            if (!Aberto) { falhas.Add("placa de vídeo: " + Motivo); return falhas; }
            if (Gravado.ClockMin is null && Inicial.ClockMin is null) Inicial = LerGpu();
            void Campo(string nome, IntPtr o, int? desejado, int? inicial, int? gravado, int set, int get, Action<int> marcar)
            {
                if (o == IntPtr.Zero) { if (desejado is not null) falhas.Add(nome + " (não disponível)"); return; }
                int? valor = desejado ?? (gravado is not null ? inicial : null);
                if (valor is not int v) return;
                if (Int(o, get) == v) { if (desejado is not null) marcar(v); return; }
                int r = SetInt(o, set, v);
                if (r == 0 && Int(o, get) == v) marcar(v);
                else falhas.Add($"{nome} (código {r})");
            }
            // Ordem do Adrenalin: energia, clock máximo antes do mínimo, tensão, memória.
            Campo("limite de energia", power, alvo.LimiteEnergia, Inicial.LimiteEnergia, Gravado.LimiteEnergia, 5, 4, v => Gravado.LimiteEnergia = v);
            Campo("clock máximo", gfx, alvo.ClockMax, Inicial.ClockMax, Gravado.ClockMax, 8, 7, v => Gravado.ClockMax = v);
            Campo("clock mínimo", gfx, alvo.ClockMin, Inicial.ClockMin, Gravado.ClockMin, 5, 4, v => Gravado.ClockMin = v);
            Campo("tensão", gfx, alvo.Tensao, Inicial.Tensao, Gravado.Tensao, 11, 10, v => Gravado.Tensao = v);
            Campo("clock da memória", vram, alvo.ClockMemoria, Inicial.ClockMemoria, Gravado.ClockMemoria, 9, 8, v => Gravado.ClockMemoria = v);
            if (vram != IntPtr.Zero && (alvo.MemoriaRapida ?? (Gravado.MemoriaRapida is not null ? Inicial.MemoriaRapida : null)) is bool rap && TempoMemoria() != rap)
            {
                int r = ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(vram, 6))(vram, rap ? 1 : 0);
                if (r == 0 && TempoMemoria() == rap) Gravado.MemoriaRapida = rap; else falhas.Add($"tempos da memória (código {r})");
            }
        }
        return falhas;
    }

    ConfigPlacaDeVideo LerGpu() => new()
    {
        ClockMin = Int(gfx, 4), ClockMax = Int(gfx, 7), Tensao = Int(gfx, 10),
        LimiteEnergia = Int(power, 4), ClockMemoria = Int(vram, 8),
        MemoriaRapida = vram != IntPtr.Zero && Bool(vram, 3) ? TempoMemoria() : null,
    };

    public List<string> AplicarVentoinha(VentoinhaGpu v)
    {
        var falhas = new List<string>();
        lock (trava)
        {
            if (!Aberto || fan == IntPtr.Zero) { if (v.Modo == "curva") falhas.Add("ventoinha da placa de vídeo (não disponível)"); return falhas; }
            if (!fanGravada) InicialFan = LerVentoinha();
            if (v.Modo == "curva" && v.Curva.Count == 5)
            {
                if (Ptr(fan, 4, out var lista) != 0) { falhas.Add("curva da placa de vídeo (lista)"); return falhas; }
                try
                {
                    uint n = Tamanho(lista);
                    for (uint i = 0; i < Math.Min(n, 5u); i++)
                    {
                        if (Item(lista, 11, i, out var st) != 0) continue;
                        SetInt(st, 6, (int)Math.Round(v.Curva[(int)i].T));
                        SetInt(st, 4, (int)Math.Round(v.Curva[(int)i].P));
                        Rel(st);
                    }
                    int erro = -1;
                    ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int*, int>)Slot(fan, 6))(fan, lista, &erro);
                    int r = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(fan, 7))(fan, lista);
                    if (r != 0) falhas.Add($"curva da placa de vídeo recusada pelo driver (ponto {erro + 1}, código {r})");
                    else
                    {
                        fanGravada = true;
                        var lida = LerVentoinha().Curva;
                        for (int i = 0; i < Math.Min(5, lida.Count); i++)
                            if (Math.Abs(lida[i].P - v.Curva[i].P) > 1 || Math.Abs(lida[i].T - v.Curva[i].T) > 1) { falhas.Add("curva da placa de vídeo não conferiu"); break; }
                    }
                }
                finally { Rel(lista); }
            }
            else if (v.Modo == "auto" && fanGravada)
            {
                AplicarVentoinha(new VentoinhaGpu { Modo = "curva", Curva = InicialFan.Curva });
                fanGravada = false;
            }
            if (v.ParadaEmRepouso is bool z && Bool(fan, 8) && Bool(fan, 9) != z)
            {
                SetBool(fan, 10, z);
                if (Bool(fan, 9) != z) falhas.Add("ventoinha parada em repouso");
            }
            if (v.RuidoMinimo is int rm && Bool(fan, 11) && Int(fan, 13) != rm)
            {
                SetInt(fan, 14, rm);
                if (Int(fan, 13) != rm) falhas.Add("limite mínimo de ruído");
            }
        }
        return falhas;
    }

    VentoinhaGpu LerVentoinha()
    {
        var r = new VentoinhaGpu { Modo = "curva" };
        if (fan == IntPtr.Zero || Ptr(fan, 4, out var lista) != 0) return r;
        uint n = Tamanho(lista);
        for (uint i = 0; i < n; i++)
        {
            if (Item(lista, 11, i, out var st) != 0) continue;
            r.Curva.Add(new Ponto(Int(st, 5) ?? 0, Int(st, 3) ?? 0));
            Rel(st);
        }
        Rel(lista);
        r.ParadaEmRepouso = Bool(fan, 8) ? Bool(fan, 9) : null;
        r.RuidoMinimo = Bool(fan, 11) ? Int(fan, 13) : null;
        return r;
    }

    public VentoinhaGpu CurvaAtual() { lock (trava) return LerVentoinha(); }

    public bool VoltarFabrica()
    {
        lock (trava)
        {
            if (!Aberto || tuning == IntPtr.Zero) return false;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(tuning, 5))(tuning, gpu);
            Gravado = new ConfigPlacaDeVideo();
            fanGravada = false;
            return BoolDe(tuning, 4, gpu);
        }
    }

    public bool DeFabrica() { lock (trava) return Aberto && tuning != IntPtr.Zero && BoolDe(tuning, 4, gpu); }

    Ajustes3D Ler3D()
    {
        var a = new Ajustes3D();
        IntPtr o;
        o = Obter3D(3, true); if (o != IntPtr.Zero && Bool(o, 3)) a.AntiLag = Bool(o, 4); Rel(o);
        o = Obter3D(4, true); if (o != IntPtr.Zero && Bool(o, 3)) { a.Chill = Bool(o, 4); a.ChillMin = Int(o, 6); a.ChillMax = Int(o, 7); } Rel(o);
        o = Obter3D(5, true); if (o != IntPtr.Zero && Bool(o, 3)) { a.Boost = Bool(o, 4); a.BoostResolucaoMin = Int(o, 6); } Rel(o);
        o = Obter3D(6, true); if (o != IntPtr.Zero && Bool(o, 3)) { a.Nitidez = Bool(o, 4); a.NitidezNivel = Int(o, 6); } Rel(o);
        o = Obter3D(7, true); if (o != IntPtr.Zero && Bool(o, 3)) a.EnhancedSync = Bool(o, 4); Rel(o);
        o = Obter3D(8, true); if (o != IntPtr.Zero && Bool(o, 3)) a.Vsync = Int(o, 5) switch { 0 => "sempre-desligado", 1 => "desligado-salvo-app", 2 => "ligado-salvo-app", 3 => "sempre-ligado", _ => null }; Rel(o);
        o = Obter3D(9, true); if (o != IntPtr.Zero && Bool(o, 3)) { a.Frtc = Bool(o, 4); a.FrtcFps = Int(o, 6); } Rel(o);
        o = Obter3D(10, true); if (o != IntPtr.Zero && Bool(o, 3)) { a.Suavizacao = Int(o, 4) switch { 0 => "app", 1 => "melhorar", 2 => "substituir", _ => null }; a.SuavizacaoNivel = Int(o, 5)?.ToString(); a.SuavizacaoMetodo = Int(o, 6) switch { 0 => "multiamostragem", 1 => "adaptativa", 2 => "superamostragem", _ => null }; } Rel(o);
        o = Obter3D(12, true); if (o != IntPtr.Zero && Bool(o, 3)) { a.Anisotropica = Bool(o, 4); a.AnisotropicaNivel = Int(o, 5); } Rel(o);
        o = Obter3D(13, true); if (o != IntPtr.Zero && Bool(o, 3)) { a.Tesselacao = Int(o, 4) switch { 0 => "amd", 1 => "app", 2 => "substituir", _ => null }; a.TesselacaoNivel = Int(o, 5); } Rel(o);
        o = Obter3D(14, false); if (o != IntPtr.Zero && Bool(o, 3)) { a.Rsr = Bool(o, 4); a.RsrNitidez = Int(o, 7); } Rel(o);
        o = Obter3D(17, false); if (o != IntPtr.Zero && Bool(o, 3)) a.Afmf = Bool(o, 4); Rel(o);
        return a;
    }

    public Ajustes3D Atual3D() { lock (trava) return Aberto ? Ler3D() : new Ajustes3D(); }

    public List<string> Aplicar3D(Ajustes3D alvo)
    {
        var falhas = new List<string>();
        lock (trava)
        {
            if (!Aberto) { falhas.Add("ajustes gráficos: " + Motivo); return falhas; }
            var atual = Ler3D();
            T? Valor<T>(T? desejado, T? inicial, T? gravado) where T : struct => desejado ?? (gravado is not null ? inicial : null);
            string? Texto(string? desejado, string? inicial, string? gravado) => desejado ?? (gravado is not null ? inicial : null);
            void Liga(string nome, int slot, bool comGpu, int set, bool? alvoV, bool? atualV, Action<bool> marcar)
            {
                if (alvoV is not bool v || v == atualV) return;
                var o = Obter3D(slot, comGpu);
                if (o == IntPtr.Zero) { falhas.Add(nome); return; }
                int r = SetBool(o, set, v);
                bool ok = r == 0 && Bool(o, 4) == v;
                Rel(o);
                if (ok) marcar(v); else falhas.Add($"{nome} (código {r})");
            }
            void Num(string nome, int slot, bool comGpu, int set, int get, int? alvoV, int? atualV, Action<int> marcar)
            {
                if (alvoV is not int v || v == atualV) return;
                var o = Obter3D(slot, comGpu);
                if (o == IntPtr.Zero) { falhas.Add(nome); return; }
                int r = SetInt(o, set, v);
                bool ok = r == 0 && Int(o, get) == v;
                Rel(o);
                if (ok) marcar(v); else falhas.Add($"{nome} (código {r})");
            }
            var i = Inicial3D; var g = Gravado3D;
            Liga("Anti-Lag", 3, true, 5, Valor(alvo.AntiLag, i.AntiLag, g.AntiLag), atual.AntiLag, v => g.AntiLag = v);
            Liga("Chill", 4, true, 8, Valor(alvo.Chill, i.Chill, g.Chill), atual.Chill, v => g.Chill = v);
            Num("Chill mínimo", 4, true, 9, 6, Valor(alvo.ChillMin, i.ChillMin, g.ChillMin), atual.ChillMin, v => g.ChillMin = v);
            Num("Chill máximo", 4, true, 10, 7, Valor(alvo.ChillMax, i.ChillMax, g.ChillMax), atual.ChillMax, v => g.ChillMax = v);
            Liga("Boost", 5, true, 7, Valor(alvo.Boost, i.Boost, g.Boost), atual.Boost, v => g.Boost = v);
            Num("resolução do Boost", 5, true, 8, 6, Valor(alvo.BoostResolucaoMin, i.BoostResolucaoMin, g.BoostResolucaoMin), atual.BoostResolucaoMin, v => g.BoostResolucaoMin = v);
            Liga("nitidez de imagem", 6, true, 7, Valor(alvo.Nitidez, i.Nitidez, g.Nitidez), atual.Nitidez, v => g.Nitidez = v);
            Num("nível da nitidez", 6, true, 8, 6, Valor(alvo.NitidezNivel, i.NitidezNivel, g.NitidezNivel), atual.NitidezNivel, v => g.NitidezNivel = v);
            Liga("Enhanced Sync", 7, true, 5, Valor(alvo.EnhancedSync, i.EnhancedSync, g.EnhancedSync), atual.EnhancedSync, v => g.EnhancedSync = v);
            var vs = Texto(alvo.Vsync, i.Vsync, g.Vsync);
            if (vs is not null && vs != atual.Vsync)
                Num("sincronia vertical", 8, true, 6, 5, vs switch { "sempre-desligado" => 0, "desligado-salvo-app" => 1, "ligado-salvo-app" => 2, _ => 3 }, -1, _ => g.Vsync = vs);
            Liga("limite de quadros da AMD", 9, true, 7, Valor(alvo.Frtc, i.Frtc, g.Frtc), atual.Frtc, v => g.Frtc = v);
            Num("limite de quadros da AMD", 9, true, 8, 6, Valor(alvo.FrtcFps, i.FrtcFps, g.FrtcFps), atual.FrtcFps, v => g.FrtcFps = v);
            var aa = Texto(alvo.Suavizacao, i.Suavizacao, g.Suavizacao);
            if (aa is not null && aa != atual.Suavizacao)
                Num("suavização de bordas", 10, true, 7, 4, aa switch { "melhorar" => 1, "substituir" => 2, _ => 0 }, -1, _ => g.Suavizacao = aa);
            var aaNivel = Texto(alvo.SuavizacaoNivel, i.SuavizacaoNivel, g.SuavizacaoNivel);
            if (aaNivel is not null && aaNivel != atual.SuavizacaoNivel && int.TryParse(aaNivel, out var nv))
                Num("nível da suavização", 10, true, 8, 5, nv, -1, _ => g.SuavizacaoNivel = aaNivel);
            var aaMet = Texto(alvo.SuavizacaoMetodo, i.SuavizacaoMetodo, g.SuavizacaoMetodo);
            if (aaMet is not null && aaMet != atual.SuavizacaoMetodo)
                Num("método da suavização", 10, true, 9, 6, aaMet switch { "adaptativa" => 1, "superamostragem" => 2, _ => 0 }, -1, _ => g.SuavizacaoMetodo = aaMet);
            Liga("filtragem de texturas", 12, true, 6, Valor(alvo.Anisotropica, i.Anisotropica, g.Anisotropica), atual.Anisotropica, v => g.Anisotropica = v);
            Num("nível da filtragem", 12, true, 7, 5, Valor(alvo.AnisotropicaNivel, i.AnisotropicaNivel, g.AnisotropicaNivel), atual.AnisotropicaNivel, v => g.AnisotropicaNivel = v);
            var tm = Texto(alvo.Tesselacao, i.Tesselacao, g.Tesselacao);
            if (tm is not null && tm != atual.Tesselacao)
                Num("tesselação", 13, true, 6, 4, tm switch { "app" => 1, "substituir" => 2, _ => 0 }, -1, _ => g.Tesselacao = tm);
            Num("nível da tesselação", 13, true, 7, 5, Valor(alvo.TesselacaoNivel, i.TesselacaoNivel, g.TesselacaoNivel), atual.TesselacaoNivel, v => g.TesselacaoNivel = v);
            Liga("Radeon Super Resolution", 14, false, 5, Valor(alvo.Rsr, i.Rsr, g.Rsr), atual.Rsr, v => g.Rsr = v);
            Num("nitidez do RSR", 14, false, 8, 7, Valor(alvo.RsrNitidez, i.RsrNitidez, g.RsrNitidez), atual.RsrNitidez, v => g.RsrNitidez = v);
            Liga("Fluid Motion Frames", 17, false, 5, Valor(alvo.Afmf, i.Afmf, g.Afmf), atual.Afmf, v => g.Afmf = v);
        }
        return falhas;
    }

    public bool LimparCache()
    {
        lock (trava)
        {
            var o = Obter3D(15, true);
            if (o == IntPtr.Zero || !Bool(o, 3)) { Rel(o); return false; }
            int r = Acao(o, 4);
            Rel(o);
            return r == 0;
        }
    }

    public List<string> AplicarTelas(ConfigTelas c)
    {
        var falhas = new List<string>();
        lock (trava)
        {
            if (!Aberto) { if (c.Lista.Count > 0) falhas.Add("telas: " + Motivo); return falhas; }
            foreach (var (d, id, nome) in Telas())
            {
                var alvo = c.Lista.FirstOrDefault(x => x.Id == id);
                inicialTelas.TryGetValue(id, out var ini);
                if (!gravadoTelas.TryGetValue(id, out var gr)) gravadoTelas[id] = gr = new AjusteTela { Id = id };
                ini ??= new AjusteTela();
                alvo ??= new AjusteTela();
                T? V<T>(T? a, T? i, T? g) where T : struct => a ?? (g is not null ? i : null);
                string? S(string? a, string? i, string? g) => a ?? (g is not null ? i : null);
                void Liga(string n, int slot, bool? v, Action<bool> marcar)
                {
                    if (v is not bool b) return;
                    var o = DaTela(slot, d);
                    if (o == IntPtr.Zero || !Bool(o, 3)) { Rel(o); falhas.Add($"{nome}: {n} (não aceito)"); return; }
                    if (Bool(o, 4) != b) { SetBool(o, 5, b); if (Bool(o, 4) != b) falhas.Add($"{nome}: {n}"); else marcar(b); }
                    Rel(o);
                }
                Liga("FreeSync", 9, V(alvo.FreeSync, ini.FreeSync, gr.FreeSync), v => gr.FreeSync = v);
                Liga("resolução virtual", 10, V(alvo.Vsr, ini.Vsr, gr.Vsr), v => gr.Vsr = v);
                Liga("escala pela placa de vídeo", 11, V(alvo.EscalaGpu, ini.EscalaGpu, gr.EscalaGpu), v => gr.EscalaGpu = v);
                Liga("escala inteira", 13, V(alvo.EscalaInteira, ini.EscalaInteira, gr.EscalaInteira), v => gr.EscalaInteira = v);
                Liga("taxa de atualização dinâmica", 22, V(alvo.AtualizacaoDinamica, ini.AtualizacaoDinamica, gr.AtualizacaoDinamica), v => gr.AtualizacaoDinamica = v);
                if (S(alvo.ModoEscala, ini.ModoEscala, gr.ModoEscala) is string me)
                {
                    var o = DaTela(12, d);
                    int cod = me switch { "tela-cheia" => 1, "centralizado" => 2, _ => 0 };
                    if (o != IntPtr.Zero && Int(o, 4) != cod) { SetInt(o, 5, cod); if (Int(o, 4) != cod) falhas.Add($"{nome}: modo de escala"); else gr.ModoEscala = me; }
                    Rel(o);
                }
                if (V(alvo.ProfundidadeCor, ini.ProfundidadeCor, gr.ProfundidadeCor) is int pc)
                {
                    var o = DaTela(14, d);
                    int cod = pc switch { 6 => 1, 8 => 2, 10 => 3, 12 => 4, 14 => 5, _ => 6 };
                    if (o != IntPtr.Zero && Int(o, 4) != cod) { SetInt(o, 5, cod); if (Int(o, 4) != cod) falhas.Add($"{nome}: profundidade de cor"); else gr.ProfundidadeCor = pc; }
                    Rel(o);
                }
                if (S(alvo.FormatoPixel, ini.FormatoPixel, gr.FormatoPixel) is string fp)
                {
                    var o = DaTela(15, d);
                    int cod = FormatoCodigo(fp);
                    if (o != IntPtr.Zero && Int(o, 4) != cod) { SetInt(o, 5, cod); if (Int(o, 4) != cod) falhas.Add($"{nome}: formato de pixel"); else gr.FormatoPixel = fp; }
                    Rel(o);
                }
                var cc = DaTela(16, d);
                if (cc != IntPtr.Zero)
                {
                    void Cor(string n, int sup, int set, int get, int? v, Action<int> marcar)
                    {
                        if (v is not int x || !Bool(cc, sup) || Int(cc, get) == x) return;
                        SetInt(cc, set, x);
                        if (Int(cc, get) != x) falhas.Add($"{nome}: {n}"); else marcar(x);
                    }
                    Cor("matiz", 3, 6, 5, V(alvo.Matiz, ini.Matiz, gr.Matiz), v => gr.Matiz = v);
                    Cor("saturação", 7, 10, 9, V(alvo.Saturacao, ini.Saturacao, gr.Saturacao), v => gr.Saturacao = v);
                    Cor("brilho", 11, 14, 13, V(alvo.Brilho, ini.Brilho, gr.Brilho), v => gr.Brilho = v);
                    Cor("contraste", 15, 18, 17, V(alvo.Contraste, ini.Contraste, gr.Contraste), v => gr.Contraste = v);
                    Cor("temperatura de cor", 19, 22, 21, V(alvo.Temperatura, ini.Temperatura, gr.Temperatura), v => gr.Temperatura = v);
                }
                Rel(cc);
                var vb = DaTela(19, d);
                if (vb != IntPtr.Zero && Bool(vb, 3))
                {
                    if (V(alvo.VariBright, ini.VariBright, gr.VariBright) is bool on && Bool(vb, 4) != on) { SetBool(vb, 5, on); if (Bool(vb, 4) != on) falhas.Add($"{nome}: Vari-Bright"); else gr.VariBright = on; }
                    if (alvo.VariBrightNivel is int nvb && NivelVb(vb) != nvb) { Acao(vb, 11 + Math.Clamp(nvb, 0, 4)); if (NivelVb(vb) != nvb) falhas.Add($"{nome}: nível do Vari-Bright"); }
                }
                Rel(vb);
                if (alvo.Gamut is string gm)
                {
                    var o = DaTela(6, d);
                    int espaco = gm switch { "bt601" => 1, "adobe" => 2, "cie" => 3, "bt2020" => 4, _ => 0 };
                    if (o != IntPtr.Zero)
                    {
                        int r = ((delegate* unmanaged[Stdcall]<IntPtr, int, int, int>)Slot(o, 30))(o, 1, espaco);
                        if (r != 0) falhas.Add($"{nome}: gamut (código {r})");
                    }
                    Rel(o);
                }
                if (alvo.Gama is double gama)
                {
                    var o = DaTela(7, d);
                    if (o != IntPtr.Zero)
                    {
                        var rampa = new ushort[256 * 3];
                        // A tela já trabalha em 2,2: a rampa é a identidade em 2,2 e curva o meio-tom nos outros valores.
                        for (int k = 0; k < 256; k++)
                        {
                            var v = (ushort)Math.Clamp(Math.Round(Math.Pow(k / 255.0, gama / 2.2) * 65535), 0, 65535);
                            rampa[k * 3] = rampa[k * 3 + 1] = rampa[k * 3 + 2] = v;
                        }
                        int r;
                        fixed (ushort* p = rampa) r = ((delegate* unmanaged[Stdcall]<IntPtr, ushort*, int>)Slot(o, 26))(o, p);
                        if (r != 0) falhas.Add($"{nome}: gama (código {r})");
                    }
                    Rel(o);
                }
                Rel(d);
            }
        }
        return falhas;
    }

    public List<AjusteTela> LerTelas()
    {
        lock (trava)
        {
            var r = new List<AjusteTela>();
            if (!Aberto) return r;
            foreach (var (d, id, nome) in Telas())
            {
                var t = new AjusteTela { Id = id, Nome = nome };
                IntPtr o;
                o = DaTela(9, d); if (o != IntPtr.Zero && Bool(o, 3)) t.FreeSync = Bool(o, 4); Rel(o);
                o = DaTela(10, d); if (o != IntPtr.Zero && Bool(o, 3)) t.Vsr = Bool(o, 4); Rel(o);
                o = DaTela(11, d); if (o != IntPtr.Zero && Bool(o, 3)) t.EscalaGpu = Bool(o, 4); Rel(o);
                o = DaTela(13, d); if (o != IntPtr.Zero && Bool(o, 3)) t.EscalaInteira = Bool(o, 4); Rel(o);
                o = DaTela(22, d); if (o != IntPtr.Zero && Bool(o, 3)) t.AtualizacaoDinamica = Bool(o, 4); Rel(o);
                o = DaTela(12, d); if (o != IntPtr.Zero && Bool(o, 3)) t.ModoEscala = Int(o, 4) switch { 1 => "tela-cheia", 2 => "centralizado", _ => "preservar" }; Rel(o);
                o = DaTela(14, d); if (o != IntPtr.Zero && Bool(o, 3)) t.ProfundidadeCor = Int(o, 4) switch { 1 => 6, 2 => 8, 3 => 10, 4 => 12, 5 => 14, 6 => 16, _ => null }; Rel(o);
                o = DaTela(15, d); if (o != IntPtr.Zero && Bool(o, 3)) t.FormatoPixel = Formato(Int(o, 4)); Rel(o);
                o = DaTela(16, d);
                if (o != IntPtr.Zero)
                {
                    if (Bool(o, 3)) t.Matiz = Int(o, 5);
                    if (Bool(o, 7)) t.Saturacao = Int(o, 9);
                    if (Bool(o, 11)) t.Brilho = Int(o, 13);
                    if (Bool(o, 15)) t.Contraste = Int(o, 17);
                    if (Bool(o, 19)) t.Temperatura = Int(o, 21);
                }
                Rel(o);
                o = DaTela(19, d); if (o != IntPtr.Zero && Bool(o, 3)) { t.VariBright = Bool(o, 4); t.VariBrightNivel = NivelVb(o); } Rel(o);
                r.Add(t);
                Rel(d);
            }
            return r;
        }
    }

    public void Dispose()
    {
        lock (trava)
        {
            foreach (var p in new[] { gfx, vram, fan, power, perf, dsrv3, dsrv, s3d1, s3d, tuning, gpu }) Rel(p);
            gfx = vram = fan = power = perf = dsrv3 = dsrv = s3d1 = s3d = tuning = gpu = IntPtr.Zero;
            if (dll != IntPtr.Zero && Aberto)
            {
                var term = (delegate* unmanaged[Cdecl]<int>)GetProcAddress(dll, "ADLXTerminate");
                if (term != null) term();
            }
            Aberto = false;
        }
    }
}
