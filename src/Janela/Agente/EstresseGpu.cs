using System.Runtime.InteropServices;
using System.Text;

namespace SetupHub.Janela.Agente;

// Cálculo determinístico comparado com a rodada feita na tensão de partida; resultado errado ou driver reiniciado é instável.
sealed unsafe class EstresseGpu : IDisposable
{
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adaptador, int tipo, IntPtr soft, uint flags, int* niveis, uint n, uint sdk, IntPtr* dev, int* nivel, IntPtr* ctx);
    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)] static extern int D3DCompile(byte* src, nuint tam, string? nome, IntPtr defs, IntPtr inc, string entrada, string alvo, uint f1, uint f2, IntPtr* codigo, IntPtr* erros);

    [StructLayout(LayoutKind.Sequential)] struct DescBuffer { public uint Tamanho, Uso, Ligacao, Cpu, Misc, Passo; }
    [StructLayout(LayoutKind.Sequential)] struct DadosIniciais { public void* Mem; public uint Linha, Fatia; }
    [StructLayout(LayoutKind.Sequential)] struct Mapeado { public void* Dados; public uint Linha, Fatia; }

    const int Elementos = 1 << 20;
    const string Hlsl = @"
RWStructuredBuffer<float> saida : register(u0);
cbuffer P : register(b0) { uint voltas; float semente; uint a0; uint a1; };
[numthreads(256,1,1)]
void main(uint3 id : SV_DispatchThreadID)
{
    float x = semente + id.x * 0.000001;
    float y = 0.5;
    uint h = id.x * 2654435761u;
    [loop] for (uint i = 0; i < voltas; i++)
    {
        x = mad(x, 1.000173, -0.000141); x = frac(x);
        y = mad(y, x, 0.25); y = frac(y);
        h = h * 1664525u + 1013904223u;
        x += (h >> 28) * 0.0000001;
    }
    saida[id.x] = x + y + (h & 255) * 0.001;
}";

    IntPtr dev, ctx, cs, buf, uav, leitura, cb;
    public string? Motivo { get; private set; }

    static IntPtr Slot(IntPtr o, int i) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(o), i * IntPtr.Size);
    static void Rel(IntPtr o) { if (o != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(o, 2))(o); }

    public bool Abrir()
    {
        IntPtr d, c; int nivel; int fl = 0xb000;
        int r = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0, &fl, 1, 7, &d, &nivel, &c);
        if (r < 0) { Motivo = $"a placa de vídeo não abriu para o teste (0x{r:X8})"; return false; }
        dev = d; ctx = c;
        var fonte = Encoding.ASCII.GetBytes(Hlsl);
        IntPtr codigo, erros;
        fixed (byte* p = fonte) r = D3DCompile(p, (nuint)fonte.Length, "teste", IntPtr.Zero, IntPtr.Zero, "main", "cs_5_0", 1 << 15, 0, &codigo, &erros);
        if (r < 0) { Motivo = "o teste não compilou"; Rel(erros); return false; }
        var ptr = ((delegate* unmanaged[Stdcall]<IntPtr, void*>)Slot(codigo, 3))(codigo);
        var tam = ((delegate* unmanaged[Stdcall]<IntPtr, nuint>)Slot(codigo, 4))(codigo);
        IntPtr sh;
        r = ((delegate* unmanaged[Stdcall]<IntPtr, void*, nuint, IntPtr, IntPtr*, int>)Slot(dev, 18))(dev, ptr, tam, IntPtr.Zero, &sh);
        Rel(codigo);
        if (r < 0) { Motivo = "o teste não carregou na placa"; return false; }
        cs = sh;
        var db = new DescBuffer { Tamanho = Elementos * 4, Uso = 0, Ligacao = 0x80, Cpu = 0, Misc = 0x40, Passo = 4 };
        IntPtr b;
        r = ((delegate* unmanaged[Stdcall]<IntPtr, DescBuffer*, DadosIniciais*, IntPtr*, int>)Slot(dev, 3))(dev, &db, null, &b);
        if (r < 0) { Motivo = "memória do teste não foi criada"; return false; }
        buf = b;
        IntPtr u;
        r = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void*, IntPtr*, int>)Slot(dev, 8))(dev, buf, null, &u);
        if (r < 0) { Motivo = "acesso à memória do teste não foi criado"; return false; }
        uav = u;
        var dl = new DescBuffer { Tamanho = Elementos * 4, Uso = 3, Ligacao = 0, Cpu = 0x20000, Misc = 0x40, Passo = 4 };
        IntPtr l;
        r = ((delegate* unmanaged[Stdcall]<IntPtr, DescBuffer*, DadosIniciais*, IntPtr*, int>)Slot(dev, 3))(dev, &dl, null, &l);
        if (r < 0) { Motivo = "leitura do teste não foi criada"; return false; }
        leitura = l;
        var dc = new DescBuffer { Tamanho = 16, Uso = 0, Ligacao = 0x4 };
        var parametros = stackalloc uint[4];
        parametros[0] = 2048; float semente = 0.123f; parametros[1] = *(uint*)&semente;
        var ini = new DadosIniciais { Mem = parametros };
        IntPtr k;
        r = ((delegate* unmanaged[Stdcall]<IntPtr, DescBuffer*, DadosIniciais*, IntPtr*, int>)Slot(dev, 3))(dev, &dc, &ini, &k);
        if (r < 0) { Motivo = "parâmetros do teste não foram criados"; return false; }
        cb = k;
        return true;
    }

    // Nulo se a placa falhou.
    public ulong? Rodada(int passadas = 60)
    {
        IntPtr s = cs, v = uav, c = cb;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, uint, void>)Slot(ctx, 69))(ctx, s, null, 0);
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, uint*, void>)Slot(ctx, 68))(ctx, 0, 1, &v, null);
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)Slot(ctx, 71))(ctx, 0, 1, &c);
        for (int i = 0; i < passadas; i++) ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, void>)Slot(ctx, 41))(ctx, Elementos / 256, 1, 1);
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)Slot(ctx, 47))(ctx, leitura, buf);
        Mapeado m;
        int r = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, Mapeado*, int>)Slot(ctx, 14))(ctx, leitura, 0, 1, 0, &m);
        if (r < 0) { Motivo = Removido(); return null; }
        ulong h = 1469598103934665603;
        var p = (ulong*)m.Dados;
        for (int i = 0; i < Elementos / 2; i++) { h ^= p[i]; h *= 1099511628211; }
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Slot(ctx, 15))(ctx, leitura, 0);
        int motivo = ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(dev, 39))(dev);
        if (motivo != 0) { Motivo = $"o driver da placa reiniciou (0x{motivo:X8})"; return null; }
        return h;
    }

    string Removido()
    {
        int motivo = dev == IntPtr.Zero ? 0 : ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(dev, 39))(dev);
        return $"o driver da placa reiniciou durante o teste (0x{motivo:X8})";
    }

    public void Dispose()
    {
        foreach (var o in new[] { cb, leitura, uav, buf, cs, ctx, dev }) Rel(o);
        cb = leitura = uav = buf = cs = ctx = dev = IntPtr.Zero;
    }
}
