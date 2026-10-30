using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela;

// A janela invisível do ícone também hospeda a busca de atualizações.
sealed class Bandeja
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public int cbSize; public IntPtr hWnd; public int uID; public int uFlags; public int uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(int msg, ref NotifyIconData d);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImageW(IntPtr h, string nome, uint tipo, int w, int hh, uint f);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string s);
    [StructLayout(LayoutKind.Sequential)] struct InfoIcone { public bool fIcon; public int x, y; public IntPtr mascara, cor; }
    [StructLayout(LayoutKind.Sequential)] struct CabecalhoBmp { public int tamanho, largura, altura; public short planos, bits; public int compressao, tamImagem, xppm, yppm, usadas, importantes; }
    [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref InfoIcone i);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref CabecalhoBmp b, uint uso, out IntPtr bits, IntPtr secao, uint desloc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int w, int h, uint planos, uint bits, IntPtr dados);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

    const int WM_ICONE = 0x8000 + 77;
    readonly HwndSource janela;
    NotifyIconData dados;
    bool presente;
    Window? menu;
    readonly uint barraRecriada;

    public IntPtr Alca => janela.Handle;

    public Bandeja()
    {
        janela = new HwndSource(new HwndSourceParameters(Produto.Nome + "Bandeja") { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000), PositionX = -32000, PositionY = -32000 });
        barraRecriada = RegisterWindowMessageW("TaskbarCreated");
        janela.AddHook(Mensagem);
        int t = GetSystemMetrics(49);
        var icone = LoadImageW(IntPtr.Zero, IcoTemporario(), 1, t, t, 0x10);
        dados = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = janela.Handle, uID = 1, uFlags = 0x1 | 0x2 | 0x4, uCallbackMessage = WM_ICONE,
            hIcon = icone, szTip = Produto.Nome, szInfo = "", szInfoTitle = "", uVersion = 4,
        };
        Adicionar();
        App.Lig.AoEstado += e => App.Interface(() => { Dica(e); Temperaturas(e); if (menu?.IsVisible == true) PreencherMenu(e); });
    }

    // Um ícone por temperatura, com o número desenhado; uID 2 processador, 3 placa de vídeo.
    readonly Dictionary<int, (NotifyIconData d, IntPtr icone, string chave)> numeros = new();

    void Temperaturas(Estado e)
    {
        var c = App.Lig.Config;
        if (c is null) return;
        if (!c.Aparencia.TemperaturasNaBandeja) { RemoverNumeros(); Principal(true); return; }
        bool claro = BarraClara();
        var (cpuMorno, cpuQuente) = Paleta.LimitesCpu(c);
        var (gpuMorno, gpuQuente) = Paleta.LimitesGpu(c);
        Numero(2, e.Cpu.TempMaisQuente, $"Processador: ponto mais quente {Formato.Temp(e.Cpu.TempMaisQuente)}", c.Aparencia.CorProcessador, cpuMorno, cpuQuente, claro);
        Numero(3, e.Gpu.TempMaisQuente, $"Placa de vídeo: ponto mais quente {Formato.Temp(e.Gpu.TempMaisQuente)}, núcleo {Formato.Temp(e.Gpu.Temp)}", c.Aparencia.CorPlacaDeVideo, gpuMorno, gpuQuente, claro);
        if (!promovidos && numeros.Count == 2 && ++voltasPromover % 5 == 0) Promover();
        // Sem temperatura na bandeja o ícone principal sempre volta: é o único caminho até a janela escondida.
        Principal(!c.Aparencia.EsconderIconeBandeja || numeros.Count == 0);
    }

    void Principal(bool mostrar)
    {
        if (mostrar && !presente) Adicionar();
        else if (!mostrar && presente) { Shell_NotifyIconW(2, ref dados); presente = false; }
    }

    void Numero(int id, double? temp, string dica, string corBarra, double morno, double quente, bool claro)
    {
        var texto = temp is double t ? Math.Round(t).ToString("0") : "–";
        var cor = temp >= quente ? "#E5484D" : temp >= morno ? "#F2B045" : claro ? "#101418" : "#FFFFFF";
        var chave = $"{texto}|{cor}|{corBarra}";
        bool novo = !numeros.TryGetValue(id, out var atual);
        if (!novo && atual.chave == chave)
        {
            if (atual.d.szTip != dica) { var d0 = atual.d; d0.uFlags = 0x4; d0.szTip = dica; Shell_NotifyIconW(1, ref d0); numeros[id] = (d0, atual.icone, chave); }
            return;
        }
        int lado = GetSystemMetrics(49);
        var icone = Desenhar(texto, Paleta.Cor(cor).Color, Paleta.Cor(corBarra).Color, lado);
        var d = novo
            ? new NotifyIconData { cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = janela.Handle, uID = id, uFlags = 0x1 | 0x2 | 0x4, uCallbackMessage = WM_ICONE, szInfo = "", szInfoTitle = "", uVersion = 4 }
            : atual.d;
        d.hIcon = icone; d.szTip = dica;
        if (novo) { Shell_NotifyIconW(0, ref d); Shell_NotifyIconW(4, ref d); }
        else { d.uFlags = 0x2 | 0x4; Shell_NotifyIconW(1, ref d); DestroyIcon(atual.icone); }
        numeros[id] = (d, icone, chave);
    }

    void RemoverNumeros()
    {
        foreach (var (_, (d, icone, _)) in numeros) { var x = d; Shell_NotifyIconW(2, ref x); DestroyIcon(icone); }
        numeros.Clear();
    }

    static bool BarraClara()
    {
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is int v && v == 1; }
        catch { return false; }
    }

    // O Windows 11 manda ícone novo para a área escondida; os números aparecem ao lado do relógio até a pessoa escolher outra coisa.
    bool promovidos;
    int voltasPromover;
    void Promover()
    {
        try
        {
            using var raiz = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (raiz is null) return;
            int achados = 0;
            foreach (var nome in raiz.GetSubKeyNames())
            {
                using var k = raiz.OpenSubKey(nome, true);
                if (k?.GetValue("ExecutablePath") is not string exe || !exe.EndsWith($@"\{Produto.Nome}\{Produto.ExePainel}", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.GetValue("UID") is not int uid || uid is not (2 or 3)) continue;
                achados++;
                if (k.GetValue("IsPromoted") is null) k.SetValue("IsPromoted", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            promovidos = achados >= 2;
        }
        catch { }
    }

    static IntPtr Desenhar(string texto, Color cor, Color barra, int lado)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            var fonte = new Typeface(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.Bold, FontStretches.Condensed);
            double tamanho = lado * (texto.Length >= 3 ? 0.62 : 0.86);
            var ft = new FormattedText(texto, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, fonte, tamanho, new SolidColorBrush(cor), 1);
            dc.DrawText(ft, new Point((lado - ft.Width) / 2, (lado * 0.86 - ft.Height) / 2));
            dc.DrawRectangle(new SolidColorBrush(barra), null, new Rect(lado * 0.15, lado * 0.86, lado * 0.7, Math.Max(1, lado * 0.1)));
        }
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(lado, lado, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v);
        var pix = new byte[lado * lado * 4];
        bmp.CopyPixels(pix, lado * 4, 0);
        // O ícone do Windows usa alfa direto; o bitmap do WPF vem pré-multiplicado.
        for (int i = 0; i < pix.Length; i += 4)
        {
            byte a = pix[i + 3];
            if (a is 0 or 255) continue;
            pix[i] = (byte)Math.Min(255, pix[i] * 255 / a); pix[i + 1] = (byte)Math.Min(255, pix[i + 1] * 255 / a); pix[i + 2] = (byte)Math.Min(255, pix[i + 2] * 255 / a);
        }
        var cab = new CabecalhoBmp { tamanho = Marshal.SizeOf<CabecalhoBmp>(), largura = lado, altura = -lado, planos = 1, bits = 32 };
        var corBmp = CreateDIBSection(IntPtr.Zero, ref cab, 0, out var bits, IntPtr.Zero, 0);
        Marshal.Copy(pix, 0, bits, pix.Length);
        var mascara = CreateBitmap(lado, lado, 1, 1, IntPtr.Zero);
        var info = new InfoIcone { fIcon = true, mascara = mascara, cor = corBmp };
        var h = CreateIconIndirect(ref info);
        DeleteObject(corBmp); DeleteObject(mascara);
        return h;
    }

    // Recurso embutido gravado na pasta da conta: o Windows lê o ícone de arquivo.
    static string IcoTemporario()
    {
        var caminho = System.IO.Path.Combine(Caminhos.Usuario, "setuphub.ico");
        try
        {
            System.IO.Directory.CreateDirectory(Caminhos.Usuario);
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Recursos/setuphub.ico"));
            using var s = info!.Stream;
            using var f = System.IO.File.Create(caminho);
            s.CopyTo(f);
        }
        catch { }
        return caminho;
    }

    void Adicionar()
    {
        // A dica e o aviso deixam só o próprio campo marcado; para criar de novo o ícone vão imagem, clique e dica.
        dados.uFlags = 0x1 | 0x2 | 0x4;
        presente = Shell_NotifyIconW(0, ref dados);
        Shell_NotifyIconW(4, ref dados);
    }

    public void Remover()
    {
        if (presente) Shell_NotifyIconW(2, ref dados);
        presente = false;
        RemoverNumeros();
        menu?.Close();
    }

    string ultimaDica = "";
    void Dica(Estado e)
    {
        var t = $"{Produto.Nome}: processador {Formato.Temp(e.Cpu.TempMaisQuente)}, placa {Formato.Temp(e.Gpu.TempMaisQuente)}, perfil {e.Perfil}";
        if (t == ultimaDica || !presente) return;
        ultimaDica = t;
        dados.uFlags = 0x4; dados.szTip = t.Length > 127 ? t[..127] : t;
        Shell_NotifyIconW(1, ref dados);
    }

    // Com o ícone principal escondido, o aviso sai pelo ícone da temperatura do processador.
    public void Notificar(string titulo, string texto)
    {
        NotifyIconData d;
        if (presente) d = dados;
        else if (numeros.TryGetValue(2, out var n)) d = n.d;
        else return;
        d.uFlags = 0x10; d.szInfoTitle = titulo.Length > 63 ? titulo[..63] : titulo; d.szInfo = texto.Length > 255 ? texto[..255] : texto; d.dwInfoFlags = 0x1;
        Shell_NotifyIconW(1, ref d);
    }

    IntPtr Mensagem(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool tratado)
    {
        if (msg == barraRecriada) { Adicionar(); foreach (var (_, i, _) in numeros.Values) DestroyIcon(i); numeros.Clear(); return IntPtr.Zero; }
        if (msg != WM_ICONE) return IntPtr.Zero;
        int evento = l.ToInt32() & 0xFFFF;
        if (evento == 0x0203) { App.Mostrar(); tratado = true; }
        else if (evento is 0x0202 or 0x0205 or 0x007B) { AbrirMenu(); tratado = true; }
        return IntPtr.Zero;
    }

    public void Refazer() { menu?.Close(); menu = null; }

    Border? aviso;
    TextBlock? avisoTexto;
    Grid? leituras;
    Run? tCpu, tGpu;
    TextBlock? nCpu, nGpu;
    StackPanel? listaPerfis;
    string chavePerfis = "";
    readonly List<(string nome, Botao b, TextBlock texto, Run alterado, TextBlock atalho)> perfis = new();
    Chave? luz, sobreposicao;
    TextBlock? atalhoSobreposicao;
    Border? linhaSobreposicao;

    void AbrirMenu()
    {
        if (menu is null) Montar();
        PreencherMenu(App.Lig.Estado);
        Posicionar();
        menu!.Show();
        menu.Activate();
        SetForegroundWindow(new WindowInteropHelper(menu).Handle);
    }

    void Posicionar()
    {
        if (menu?.Content is not FrameworkElement conteudo) return;
        conteudo.Measure(new Size(menu.Width, double.PositiveInfinity));
        double altura = menu.ActualHeight > 0 ? menu.ActualHeight : conteudo.DesiredSize.Height;
        var area = SystemParameters.WorkArea;
        menu.Left = area.Right - menu.Width - 12;
        menu.Top = area.Bottom - altura - 12;
    }

    void Montar()
    {
        var P = K.P;
        menu = new Window
        {
            Width = 264, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
            AllowsTransparency = true, Background = Brushes.Transparent, Title = Produto.Nome,
        };
        menu.Deactivated += (_, _) => menu?.Hide();
        menu.KeyDown += (_, e) => { if (e.Key == Key.Escape) menu?.Hide(); };
        menu.SizeChanged += (_, _) => Posicionar();
        perfis.Clear();
        chavePerfis = "";

        avisoTexto = K.T("", 13);
        avisoTexto.TextWrapping = TextWrapping.Wrap; avisoTexto.TextTrimming = TextTrimming.None;
        aviso = new Border { Child = avisoTexto, Background = P.Poco, CornerRadius = new CornerRadius(P.RaioBotao), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };

        UIElement Leitura(string rotulo, Brush cor, out Run valor, out TextBlock nota)
        {
            var t = new TextBlock();
            valor = new Run("–") { FontFamily = P.Numero, FontWeight = FontWeights.SemiBold, FontSize = P.F(24), Foreground = cor };
            t.Inlines.Add(valor);
            nota = K.T("", 11.5, P.Apagado);
            return new Border { Child = K.Col(0, K.T(rotulo, 12, P.Apagado), t, nota), Padding = new Thickness(10, 8, 10, 8), Background = P.Poco, CornerRadius = new CornerRadius(P.RaioBotao) };
        }
        leituras = K.Iguais(2, 6, new[] { Leitura("Processador", P.Cpu, out tCpu, out nCpu), Leitura("Placa de vídeo", P.Gpu, out tGpu, out nGpu) });
        leituras.Margin = new Thickness(0, 0, 0, 10);
        leituras.Visibility = Visibility.Collapsed;

        var rotuloPerfil = K.T("Perfil", 12, P.Apagado);
        rotuloPerfil.Margin = new Thickness(10, 0, 0, 4);
        listaPerfis = new StackPanel();

        luz = new Chave();
        luz.Mudou += on => _ = App.Lig.Pedir("luz.ligar", Ligacao.Args(("ligada", on)));
        var linhaLuz = new Border
        {
            Child = K.Grade("* 40 64", 8, K.T("Luz"), luz, null), Padding = new Thickness(10, 10, 10, 0),
            BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 0),
        };
        sobreposicao = new Chave();
        sobreposicao.Mudou += on => _ = App.Lig.Definir("jogos.sobreposicao.ligada", on ? "true" : "false");
        atalhoSobreposicao = K.T("", 12, P.Apagado);
        atalhoSobreposicao.HorizontalAlignment = HorizontalAlignment.Right;
        linhaSobreposicao = new Border
        {
            Child = K.Grade("* 40 64", 8, K.T("Sobreposição"), sobreposicao, atalhoSobreposicao), Padding = new Thickness(10, 10, 10, 0),
        };

        var abrir = Botao.Contorno("Abrir o SetupHub", () => { menu?.Hide(); App.Mostrar(); }, 38);
        ((TextBlock)abrir.Child).Foreground = P.Texto;
        var sair = new Botao
        {
            Width = P.E(38), Height = P.E(38), CornerRadius = new CornerRadius(P.RaioBotao), BorderThickness = new Thickness(1),
            Child = K.IconeCaixa("M12 3v8M6.3 6.4a8 8 0 1 0 11.4 0", 16, P.Secundario, 2),
            ToolTip = "Sair do SetupHub: as ventoinhas voltam para a BIOS e a luz fica no efeito do controlador",
        };
        sair.FundoNormal = Brushes.Transparent; sair.Background = Brushes.Transparent; sair.FundoSobre = P.Poco;
        sair.BordaNormal = P.BordaForte; sair.BorderBrush = P.BordaForte; sair.BordaSobre = P.SubSel;
        sair.Clique += (_, _) => { menu?.Hide(); App.Sair(); };
        var rodape = K.Grade($"* {P.E(38).ToString(System.Globalization.CultureInfo.InvariantCulture)}", 8, abrir, sair);
        rodape.Margin = new Thickness(0, 14, 0, 0);

        menu.Content = new Border
        {
            Child = K.Col(0, aviso, leituras, rotuloPerfil, listaPerfis, linhaLuz, linhaSobreposicao, rodape), Background = P.Painel, BorderBrush = P.BordaForte, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(P.RaioCartao), Padding = new Thickness(12),
        };
        TextElement.SetFontFamily(menu, P.Corpo);
        TextElement.SetFontSize(menu, P.F(14));
        TextElement.SetForeground(menu, P.Texto);
    }

    void MontarPerfis(Config? c)
    {
        var lista = c?.Perfis.Take(6).ToList() ?? new List<Perfil>();
        var chave = string.Join("\n", lista.Select(p => p.Nome + "|" + p.Atalho));
        if (chave == chavePerfis || listaPerfis is null) return;
        chavePerfis = chave;
        perfis.Clear();
        listaPerfis.Children.Clear();
        var P = K.P;
        foreach (var p in lista)
        {
            var nome = p.Nome;
            var alterado = new Run("") { FontSize = P.F(12) };
            var tx = K.T("", 14);
            tx.Inlines.Add(new Run(nome));
            tx.Inlines.Add(alterado);
            var atalho = K.T((p.Atalho ?? "").Replace("+", " "), 12, P.Apagado);
            var bt = new Botao { Height = P.E(36), CornerRadius = new CornerRadius(P.RaioBotao), Padding = new Thickness(10, 0, 10, 0), Child = K.Grade("* auto", 8, tx, atalho), Background = Brushes.Transparent };
            bt.Margin = new Thickness(0, 0, 0, 2);
            bt.Clique += (_, _) => { PintarPerfis(nome, false); _ = App.Lig.Pedir("perfil.aplicar", Ligacao.Args(("nome", nome), ("origem", "ícone da bandeja"))); };
            perfis.Add((nome, bt, tx, alterado, atalho));
            listaPerfis.Children.Add(bt);
        }
    }

    void PintarPerfis(string? ativo, bool alteradoAtivo)
    {
        var P = K.P;
        foreach (var (nome, b, tx, alterado, atalho) in perfis)
        {
            bool on = nome == ativo;
            b.Fundo(on ? P.Selecao : Brushes.Transparent);
            b.FundoSobre = on ? P.Selecao : P.Poco;
            tx.Foreground = on ? P.TextoSelecao : P.Texto; tx.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            alterado.Text = on && alteradoAtivo ? "  alterado" : "";
            alterado.Foreground = P.SubSel; alterado.FontWeight = FontWeights.Normal;
            atalho.Foreground = on ? P.SubSel : P.Apagado;
        }
    }

    void PreencherMenu(Estado? e)
    {
        var P = K.P;
        if (menu is null || avisoTexto is null || luz is null) return;
        var c = App.Lig.Config;
        MontarPerfis(c);

        bool alerta = e?.Alertas.Count > 0;
        string? texto = alerta ? string.Join("\n", e!.Alertas.Select(a => a.Texto)) : e?.Jogo is string jogo ? $"{jogo} aberto" : null;
        avisoTexto.Text = texto ?? "";
        avisoTexto.Foreground = alerta ? P.Quente : P.Texto;
        aviso!.Visibility = texto is null ? Visibility.Collapsed : Visibility.Visible;

        bool temperaturas = c is not null && !c.Aparencia.TemperaturasNaBandeja;
        leituras!.Visibility = temperaturas ? Visibility.Visible : Visibility.Collapsed;
        if (temperaturas && e is not null)
        {
            tCpu!.Text = Formato.Temp(e.Cpu.TempMaisQuente); tCpu.Foreground = P.CalorCpu(e.Cpu.TempMaisQuente, c);
            tGpu!.Text = Formato.Temp(e.Gpu.TempMaisQuente); tGpu.Foreground = P.CalorGpu(e.Gpu.TempMaisQuente, c);
            nCpu!.Text = $"alerta em {Formato.Temp(Paleta.LimitesCpu(c).quente)}";
            nGpu!.Text = $"alerta em {Formato.Temp(Paleta.LimitesGpu(c).quente)}";
        }

        PintarPerfis(e?.Perfil ?? c?.PerfilAtivo, e?.PerfilAlterado == true);

        luz!.IsEnabled = e?.Luz.Disponivel != false;
        if (e is not null) luz.Ligada = e.Luz.Ligada;

        var s = c?.Jogos.Sobreposicao;
        bool rivaTuner = App.Lig.Faixas?.RivaTuner == true;
        atalhoSobreposicao!.Text = (s?.Atalho ?? "").Replace("+", " ");
        sobreposicao!.Ligada = s?.Ligada == true;
        sobreposicao.IsEnabled = rivaTuner;
        linhaSobreposicao!.Opacity = rivaTuner ? 1 : 0.38;
    }
}
