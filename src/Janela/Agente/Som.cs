using System.Runtime.InteropServices;

namespace SetupHub.Janela.Agente;

sealed class Som : IDisposable
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int fluxo, int estado, out IntPtr dispositivos);
        int GetDefaultAudioEndpoint(int fluxo, int papel, out IMMDevice dispositivo);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int ctx, IntPtr parametros, [MarshalAs(UnmanagedType.IUnknown)] out object interfaceAtivada);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        int GetPeakValue(out float pico);
    }

    IAudioMeterInformation? medidor;
    long ultimaTroca;

    public float Nivel()
    {
        try
        {
            if (medidor is null || Environment.TickCount64 - ultimaTroca > 10000)
            {
                ultimaTroca = Environment.TickCount64;
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                en.GetDefaultAudioEndpoint(0, 1, out var disp);
                var iid = typeof(IAudioMeterInformation).GUID;
                disp.Activate(ref iid, 23, IntPtr.Zero, out var m);
                medidor = (IAudioMeterInformation)m;
            }
            medidor.GetPeakValue(out var p);
            return p;
        }
        catch { medidor = null; return 0; }
    }

    public void Dispose() { medidor = null; }
}
