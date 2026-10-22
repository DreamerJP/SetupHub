using System.IO;
using System.Windows;
using System.Windows.Threading;
using SetupHub.Janela.Agente;
using SetupHub.Nucleo;

namespace SetupHub.Janela;

static class Programa
{
    [STAThread]
    static int Main(string[] args)
    {
        int i = Array.IndexOf(args, "--captura");
        bool captura = i >= 0 && i + 1 < args.Length;
        var nome = $@"Local\{Produto.Nome}.Painel" + (captura ? ".Captura" : "");
        using var unica = new Mutex(true, nome, out bool primeira);
        using var mostrar = new EventWaitHandle(false, EventResetMode.AutoReset, nome + ".Mostrar");
        bool bandeja = args.Contains("--bandeja");
        // Abertura escondida (login) com a janela já rodando não traz a janela para a frente.
        if (!primeira) { if (!bandeja) mostrar.Set(); return 0; }
        var app = new App(bandeja, mostrar);
        if (captura) app.Captura = (args[i + 1], i + 2 < args.Length ? args[i + 2] : "");
        return app.Run();
    }
}

sealed class App : Application
{
    public static App Atual = null!;
    public static Ligacao Lig = null!;
    public static AgenteSessao Agente = null!;
    public static Bandeja Bandeja = null!;
    public static NotificacaoRapida Notificacao = null!;
    public static Painel? Janela;
    readonly bool iniciarEscondido;
    readonly EventWaitHandle sinal;

    public App(bool bandeja, EventWaitHandle sinal)
    {
        Atual = this;
        iniciarEscondido = bandeja;
        this.sinal = sinal;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, e) => { Registrar(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Registrar(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Registrar(e.Exception); e.SetObserved(); };
    }

    public static void Registrar(Exception? e)
    {
        try
        {
            Directory.CreateDirectory(Caminhos.Usuario);
            File.AppendAllText(Path.Combine(Caminhos.Usuario, "erros.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {e}\n\n");
        }
        catch { }
    }

    public static void Interface(Action a) => Atual.Dispatcher.BeginInvoke(a);
    public static Task InterfaceAsync(Func<Task> f) => Atual.Dispatcher.InvokeAsync(f).Task.Unwrap();
    public static Task<T> InterfaceAsync<T>(Func<Task<T>> f) => Atual.Dispatcher.InvokeAsync(f).Task.Unwrap();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Kit.K.P = Paleta.De(new ConfigAparencia());
        Lig = new Ligacao();
        Bandeja = new Bandeja();
        Notificacao = new NotificacaoRapida();
        Agente = new AgenteSessao(Lig, Bandeja.Alca);
        Agente.Avisar = t => Interface(() => Janela?.Aviso(t));
        Lig.AoConfig += c => Interface(() => Aparencia(c.Aparencia));
        Lig.AoAviso += (tipo, texto) => Interface(() => Avisos(tipo, texto));
        Lig.AoSaindo += () => Interface(() => { Notificacao.Dispose(); Bandeja.Remover(); Agente.Dispose(); Lig.Parar(); Shutdown(); });
        Lig.Iniciar();
        Agente.Iniciar();
        ThreadPool.RegisterWaitForSingleObject(sinal, (_, _) => Interface(Mostrar), null, -1, false);
        if (Captura is { } cap) { _ = Capturar(cap.pasta, cap.secoes); return; }
        if (!iniciarEscondido) Mostrar();
        else Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => { Janela ??= new Painel(); });
    }

    public (string pasta, string secoes)? Captura;

    async Task Capturar(string pasta, string secoes)
    {
        try
        {
            for (int i = 0; i < 100 && (Lig.Config is null || Lig.Estado is null); i++) await Task.Delay(100);
            Mostrar();
            System.IO.Directory.CreateDirectory(pasta);
            var lista = secoes.Length > 0 ? secoes.Split(',') : Painel.Secoes.Select(s => s.Id).ToArray();
            foreach (var s in lista)
            {
                Janela!.Ir(s);
                await Task.Delay(2500);
                Janela.UpdateLayout();
                var alvo = (FrameworkElement)Janela.Content;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)alvo.ActualWidth, (int)alvo.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(alvo);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var f = System.IO.File.Create(System.IO.Path.Combine(pasta, $"tela_{s}.png"));
                png.Save(f);
            }
        }
        catch (Exception e) { Registrar(e); }
        Bandeja.Remover();
        Shutdown();
    }

    string? aparenciaAtual;

    void Aparencia(ConfigAparencia a)
    {
        var chave = $"{a.Tema}|{a.CorProcessador}|{a.CorPlacaDeVideo}|{a.Densidade}|{a.TamanhoTexto}|{(a.Tema == "windows" ? Paleta.WindowsClaro() : false)}";
        if (chave == aparenciaAtual) return;
        bool primeira = aparenciaAtual is null;
        aparenciaAtual = chave;
        Kit.K.P = Paleta.De(a);
        Janela?.Refazer();
        Bandeja.Refazer();
        Notificacao.Refazer();
    }

    void Avisos(string tipo, string texto)
    {
        if (texto.Length == 0) return;
        switch (tipo)
        {
            case "alerta": Bandeja.Notificar("Alerta do SetupHub", texto); Janela?.Aviso(texto, true); break;
            case "aviso": Janela?.Aviso(texto, true); break;
            case "tarefa": Bandeja.Notificar(Produto.Nome, texto); Janela?.Aviso(texto); break;
            case "atualizacoes": break;
        }
    }

    public static void Mostrar()
    {
        Janela ??= new Painel();
        Janela.Mostrar();
    }

    public static async void Sair()
    {
        Notificacao.Dispose();
        Bandeja.Remover();
        try { await Lig.Pedir("sair", Ligacao.Args(("origem", "bandeja")), 15000); } catch { }
        Agente.Dispose();
        Lig.Parar();
        Atual.Shutdown();
    }
}
