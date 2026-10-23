using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SetupHub.Janela.Kit;

static class IconeArquivo
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Info
    {
        public IntPtr Alca;
        public int Indice;
        public uint Atributos;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? Nome;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string? Tipo;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfoW(string arquivo, uint atributos, out Info info, uint tamanho, uint opcoes);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr alca);

    const uint Icone = 0x00000100;

    public static BitmapSource? Ler(string arquivo)
    {
        if (!File.Exists(arquivo)) return null;
        if (SHGetFileInfoW(arquivo, 0, out var info, (uint)Marshal.SizeOf<Info>(), Icone) == IntPtr.Zero || info.Alca == IntPtr.Zero) return null;
        try
        {
            var imagem = Imaging.CreateBitmapSourceFromHIcon(info.Alca, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            imagem.Freeze();
            return imagem;
        }
        catch { return null; }
        finally { DestroyIcon(info.Alca); }
    }
}
