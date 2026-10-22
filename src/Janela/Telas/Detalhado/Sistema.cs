using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;
using Microsoft.Win32;

namespace SetupHub.Janela.Telas.Detalhado;

sealed class Sistema : Tela
{
    protected override string Id => "sistema";
    InfoSistema? info;
    List<Atualizacao> atualizacoes = new();
    StackPanel chaveBios = null!;
    Border cartaoBios = null!;
    TextBlock biosInstalada = null!, biosNova = null!;
    Border linhaNova = null!;
    StackPanel memoria = null!;
    TextBlock lidas = null!, rotuloDif = null!, vazio = null!;
    TextBox busca = null!;
    Chave soDif = null!;
    StackPanel linhas = null!;
    Botao lerBios = null!, procurar = null!;
    TextBlock servico = null!, driver = null!, msi = null!;
    Border linhaMsi = null!;
    Chave comWindows = null!, numerosBandeja = null!, semIcone = null!;

    protected override FrameworkElement Montar()
    {
        Cabecalho("Sistema", () => info is { Placa.Length: > 0 } ? info.Placa.Replace("Micro-Star International Co., Ltd.", "MSI").Trim() + ", só leitura" : "só leitura");
        var esquerda = K.Col(12, MontarPrograma(), MontarChave(), MontarAtualizacoes(), MontarMemoria());
        var linha = new Fluxo { EsticarAltura = false };
        linha.Children.Add(Fluxo.Item(esquerda, 340, 1));
        linha.Children.Add(Fluxo.Item(MontarOpcoes(), 480, 2));
        linha.Margin = new Thickness(20, 12, 20, 20);
        return linha;
    }

    static Grid Par(string nome, TextBlock valor)
    {
        valor.HorizontalAlignment = HorizontalAlignment.Right; valor.TextAlignment = TextAlignment.Right;
        var g = K.Grade("auto *", 10, K.T(nome), valor);
        g.VerticalAlignment = VerticalAlignment.Center;
        return g;
    }

    Border MontarChave()
    {
        chaveBios = new StackPanel();
        var nota = K.Nota("O SetupHub não grava na BIOS. Alterações são feitas na própria BIOS, acessada pela tecla Delete na inicialização.");
        nota.Margin = new Thickness(0, 6, 0, 0);
        cartaoBios = K.Cartao(K.Col(2, K.Titulo("Conflitos da BIOS com o SetupHub"), chaveBios, nota));
        cartaoBios.Visibility = Visibility.Collapsed;
        return cartaoBios;
    }

    void PintarChave()
    {
        chaveBios.Children.Clear();
        var avisos = BiosAvisos.Lista(info?.OpcoesBios ?? new());
        cartaoBios.Visibility = avisos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var a in avisos)
        {
            var t = K.T(a, 14, P.Morno); t.TextWrapping = TextWrapping.Wrap;
            chaveBios.Children.Add(new Border { Padding = new Thickness(0, 6, 0, 6), BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = t });
        }
    }

    Border MontarAtualizacoes()
    {
        biosInstalada = K.T("–", 14, P.Secundario);
        biosNova = K.T("–", 14, P.Secundario);
        linhaNova = new Border { Child = Par("BIOS mais nova", biosNova) };
        procurar = Botao.Contorno("Procurar agora", () => _ = Procurar(), 34);
        var pagina = Botao.Contorno("Página de downloads", AbrirPagina, 34);
        foreach (var b in new[] { procurar, pagina }) ((TextBlock)b.Child).Foreground = P.Texto;
        var botoes = K.Iguais(2, 8, new UIElement[] { procurar, pagina });
        botoes.Margin = new Thickness(0, 4, 0, 0);
        return K.Cartao(K.Col(8, K.Titulo("Atualizações da placa"), Par("BIOS instalada", biosInstalada), linhaNova, botoes));
    }

    async Task Procurar()
    {
        procurar.IsEnabled = false;
        procurar.TextoBotao = "Procurando…";
        try
        {
            var r = await App.Agente.BuscarAtualizacoes();
            App.Janela?.Aviso(r);
            await Ler();
        }
        finally { procurar.IsEnabled = true; procurar.TextoBotao = "Procurar agora"; }
    }

    void AbrirPagina()
    {
        string? url = null;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var produto = k?.GetValue("BaseBoardProduct")?.ToString() ?? "";
            int p = produto.IndexOf('(');
            if (p > 0) produto = produto[..p];
            produto = produto.Trim().Replace(' ', '-');
            if (produto.Length > 0) url = $"https://www.msi.com/Motherboard/{produto}/support";
        }
        catch { }
        try { Process.Start(new ProcessStartInfo(url ?? "https://www.msi.com/support") { UseShellExecute = true }); }
        catch (Exception x) { App.Janela?.Aviso("Não foi possível abrir o navegador: " + x.Message, true); }
    }

    void PintarAtualizacoes()
    {
        biosInstalada.Text = info is { Bios.Length: > 0 } ? info.Bios + (info.BiosData.Length > 0 ? ", de " + info.BiosData : "") : "–";
        var nova = atualizacoes.FirstOrDefault(a => a.Tipo == "BIOS");
        linhaNova.Visibility = nova is null ? Visibility.Collapsed : Visibility.Visible;
        if (nova is null) return;
        biosNova.Text = nova.Nova ? $"{nova.Versao}, de {nova.Data}" : "é a instalada";
        biosNova.Foreground = nova.Nova ? P.Morno : P.Secundario;
        biosNova.FontWeight = nova.Nova ? FontWeights.SemiBold : FontWeights.Normal;
    }

    Border MontarMemoria()
    {
        memoria = new StackPanel();
        return K.Cartao(K.Col(6, K.Titulo("Memória"), memoria));
    }

    void PintarMemoria()
    {
        memoria.Children.Clear();
        void Linha(string nome, string valor) { if (valor.Length > 0) memoria.Children.Add(new Border { MinHeight = 26, Child = Par(nome, K.T(valor, 14, P.Secundario)) }); }
        if (info is null) return;
        Linha("Módulos", info.MemoriaModulos);
        var expo = info.OpcoesBios.FirstOrDefault(o => o.Nome.Contains("EXPO", StringComparison.OrdinalIgnoreCase) || o.Nome.Contains("Extended Memory Profile", StringComparison.OrdinalIgnoreCase));
        bool perfil = expo is not null && !expo.Valor.Contains("Disabled", StringComparison.OrdinalIgnoreCase) && !expo.Valor.Equals("Auto", StringComparison.OrdinalIgnoreCase);
        var mts = int.TryParse(info.MemoriaPerfil.Split(' ')[0], out var v) ? v.ToString("N0", K.Br) + " MT/s" : info.MemoriaPerfil;
        Linha("Velocidade", mts.Length == 0 ? "" : mts + (perfil ? " pelo perfil EXPO" : ""));
        Linha("Latência", string.Join(", ", info.MemoriaTempos));
        if (memoria.Children.Count == 0) memoria.Children.Add(K.Nota("Memória ainda não lida."));
    }

    Border MontarOpcoes()
    {
        lidas = K.T("", 12.5, P.Apagado); lidas.VerticalAlignment = VerticalAlignment.Center;
        lerBios = Botao.Contorno("Ler de novo", () => _ = LerBios(), 30);
        lerBios.Width = 110;
        var cab = K.Grade("* auto auto", 10, K.Titulo("Todas as opções da BIOS"), lidas, lerBios);
        busca = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = P.Texto, CaretBrush = P.Texto, FontFamily = P.Corpo, FontSize = P.F(14),
            VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0), SelectionBrush = P.SubSel,
        };
        var dica = K.T("Procurar opção", 14, P.Tenue); dica.Margin = new Thickness(10, 0, 0, 0); dica.VerticalAlignment = VerticalAlignment.Center; dica.IsHitTestVisible = false;
        busca.TextChanged += (_, _) => { dica.Visibility = busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; PintarOpcoes(); };
        var caixa = new Border { Height = P.E(34), CornerRadius = new CornerRadius(P.RaioBotao), BorderBrush = P.BordaForte, BorderThickness = new Thickness(1), Background = P.PocoEscuro, Child = new Grid { Children = { busca, dica } } };
        rotuloDif = K.T("Só as diferentes do padrão", 13.5); rotuloDif.VerticalAlignment = VerticalAlignment.Center;
        soDif = new Chave(true);
        soDif.Mudou += _ => PintarOpcoes();
        var filtros = K.Grade("* auto 40", 12, caixa, rotuloDif, soDif);
        var titulos = K.Grade("2* 1* 1*", 10, K.T("Opção", 12.5, P.Apagado), K.T("Padrão", 12.5, P.Apagado), K.T("Atual", 12.5, P.Apagado));
        var cabTabela = new Border { Padding = new Thickness(0, 6, 0, 6), BorderBrush = P.Borda, BorderThickness = new Thickness(0, 0, 0, 1), Child = titulos };
        linhas = new StackPanel();
        var rolagem = new ScrollViewer { Content = linhas, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        vazio = K.Nota("");
        return K.Cartao(K.Col(8, cab, filtros, cabTabela, rolagem, vazio));
    }

    async Task LerBios()
    {
        lerBios.IsEnabled = false;
        lerBios.TextoBotao = "Lendo…";
        App.Janela?.Aviso("Leitura da BIOS em andamento, até 1 minuto.");
        try
        {
            var r = await App.Lig.Pedir("bios.ler", null, 150000);
            if (!r.Ok) App.Janela?.Aviso(r.Erro ?? "A leitura da BIOS falhou.", true);
            else { info = Json.Ler<InfoSistema>(r.Dados) ?? info; App.Janela?.Aviso($"BIOS lida: {info?.OpcoesBios.Count ?? 0} opções."); Pintar(); }
        }
        finally { lerBios.IsEnabled = true; lerBios.TextoBotao = "Ler de novo"; }
    }

    const int Maximo = 300;

    void PintarOpcoes()
    {
        var todas = info?.OpcoesBios ?? new();
        var dif = todas.Where(o => o.Padrao.Length > 0 && !o.Padrao.Equals(o.Valor, StringComparison.OrdinalIgnoreCase)).ToList();
        rotuloDif.Text = $"Só as diferentes do padrão ({dif.Count.ToString("N0", K.Br)})";
        lidas.Text = info is { BiosLidaEm: > 0 } ? $"{todas.Count.ToString("N0", K.Br)} lidas {Quando(info.BiosLidaEm)}" : "";
        var b = busca.Text.Trim();
        var lista = (soDif.Ligada ? dif : todas).Where(o => b.Length == 0 || o.Nome.Contains(b, StringComparison.OrdinalIgnoreCase)).ToList();
        linhas.Children.Clear();
        foreach (var o in lista.Take(Maximo))
        {
            bool diferente = o.Padrao.Length > 0 && !o.Padrao.Equals(o.Valor, StringComparison.OrdinalIgnoreCase);
            var n = K.T(o.Nome); n.TextWrapping = TextWrapping.Wrap; n.TextTrimming = TextTrimming.None;
            var g = K.Grade("2* 1* 1*", 10, n, K.T(o.Padrao.Length > 0 ? o.Padrao : "sem padrão", 14, P.Apagado), K.T(o.Valor, 14, diferente ? P.Texto : P.Apagado));
            g.VerticalAlignment = VerticalAlignment.Center;
            linhas.Children.Add(new Border { MinHeight = 30, BorderBrush = P.Linha, BorderThickness = new Thickness(0, 0, 0, 1), Child = g, Padding = new Thickness(0, 3, 0, 3) });
        }
        vazio.Text = todas.Count == 0 ? "BIOS ainda não lida. A leitura requer o serviço do SetupHub instalado."
            : lista.Count == 0 ? "Nenhuma opção com esse nome."
            : lista.Count > Maximo ? $"Mostrando {Maximo} de {lista.Count.ToString("N0", K.Br)}; use a busca para encontrar as demais." : "";
        vazio.Visibility = vazio.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    static string Quando(long ms)
    {
        var d = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
        var dia = d.Date == DateTime.Today ? "hoje" : d.Date == DateTime.Today.AddDays(-1) ? "ontem" : "em " + d.ToString("dd/MM", K.Br);
        return $"{dia} às {d:HH:mm}";
    }

    Border MontarPrograma()
    {
        servico = K.T("–", 14, P.Secundario); driver = K.T("–", 14, P.Secundario); msi = K.T("instalado, pode ser removido", 14, P.Morno);
        msi.ToolTip = "Conflita com a iluminação do SetupHub; o serviço de luz do MSI Center é encerrado a cada minuto.";
        comWindows = new Chave();
        comWindows.Mudou += ComWindows;
        numerosBandeja = new Chave();
        numerosBandeja.Mudou += on => { semIcone.IsEnabled = on; _ = Gravar(c => c.Aparencia.TemperaturasNaBandeja = on, on ? "Temperatura do processador e da placa de vídeo na barra de tarefas." : "Temperaturas removidas da barra de tarefas."); };
        semIcone = new Chave();
        semIcone.Mudou += on => _ = Gravar(c => c.Aparencia.EsconderIconeBandeja = on, on ? "Ícone do SetupHub oculto. Acesso pelo ícone de temperatura." : "Ícone do SetupHub exibido na bandeja.");
        UIElement Rotulo(string nome, string desc) => K.Col(0, K.T(nome), K.T(desc, 12, P.Apagado));
        Border Linha(UIElement rotulo, UIElement valor)
        {
            if (valor is FrameworkElement f) { f.HorizontalAlignment = HorizontalAlignment.Right; f.VerticalAlignment = VerticalAlignment.Center; }
            var g = K.Grade("* auto", 10, rotulo, valor); g.VerticalAlignment = VerticalAlignment.Center;
            return new Border { MinHeight = 30, Child = g };
        }
        linhaMsi = Linha(K.T("MSI Center"), msi);
        return K.Cartao(K.Col(8, K.Titulo(Produto.Nome),
            Linha(Rotulo("Serviço em segundo plano", "controle de ventoinhas e luz com a janela fechada"), servico),
            Linha(K.T("Ventoinhas"), driver),
            linhaMsi,
            Linha(Rotulo("Abrir com o Windows", "inicia minimizado na bandeja"), comWindows),
            Linha(Rotulo("Temperaturas na barra de tarefas", "processador e placa de vídeo ao lado do relógio"), numerosBandeja),
            Linha(Rotulo("Esconder o ícone do SetupHub", "acesso pelo ícone de temperatura"), semIcone)));
    }

    void ComWindows(bool on) => _ = Gravar(c => c.Aparencia.AbrirComWindows = on,
        on ? "O SetupHub inicia com o Windows, minimizado na bandeja." : "O SetupHub não inicia mais com o Windows. O serviço mantém ventoinhas e luz; placa de vídeo, telas e sobreposição aguardam a abertura da janela.");

    static bool MsiCenterInstalado()
    {
        foreach (var raiz in new[] { Registry.LocalMachine, Registry.CurrentUser })
            foreach (var caminho in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                try
                {
                    using var k = raiz.OpenSubKey(caminho);
                    if (k is null) continue;
                    foreach (var n in k.GetSubKeyNames())
                    {
                        using var s = k.OpenSubKey(n);
                        if (s?.GetValue("DisplayName") is string d && d.StartsWith("MSI Center", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                catch { }
            }
        return false;
    }

    bool? msiInstalado;

    void PintarPrograma()
    {
        servico.Text = App.Lig.Ligada ? "ativo" : "parado";
        servico.Foreground = App.Lig.Ligada ? P.Secundario : P.Quente;
        var semControle = Est?.Avisos.FirstOrDefault(a => a.StartsWith("Ventoinhas sem controle"));
        driver.Text = Est is null ? "–" : semControle is null ? "controladas pelo SetupHub" : "sem acesso, controle pela BIOS";
        driver.Foreground = semControle is null ? P.Secundario : P.Morno;
        driver.ToolTip = semControle;
        msiInstalado ??= MsiCenterInstalado();
        linhaMsi.Visibility = msiInstalado.Value ? Visibility.Visible : Visibility.Collapsed;
        comWindows.Ligada = Cfg?.Aparencia.AbrirComWindows ?? true;
        numerosBandeja.Ligada = Cfg?.Aparencia.TemperaturasNaBandeja ?? true;
        semIcone.Ligada = Cfg?.Aparencia.EsconderIconeBandeja ?? false;
        semIcone.IsEnabled = numerosBandeja.Ligada;
    }

    async Task Ler()
    {
        var s = await App.Lig.Pedir("sistema", null, 10000);
        if (s.Ok) info = Json.Ler<InfoSistema>(s.Dados);
        var a = await App.Lig.Pedir("atualizacoes", null, 10000);
        if (a.Ok) atualizacoes = Json.Ler<List<Atualizacao>>(a.Dados) ?? new();
        Pintar();
    }

    void Pintar()
    {
        PintarChave();
        PintarAtualizacoes();
        PintarMemoria();
        PintarOpcoes();
        PintarPrograma();
        if (Cfg is { } c) NovaConfig(c);
    }

    public override async void Mostrada()
    {
        base.Mostrada();
        msiInstalado = null;
        try { await Ler(); } catch (Exception x) { App.Registrar(x); }
    }

    int ciclos;
    public override void Estado(Estado e)
    {
        if (ciclos++ % 5 == 0) PintarPrograma();
    }
}
