using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas.Detalhado;

// Valor mostrado: o guardado no SetupHub ou, sem ele, o que o driver tem agora.
sealed class Telas : Tela
{
    protected override string Id => "telas";
    string? telaId;
    StackPanel cartoesTelas = null!;
    readonly List<AjusteTela> lidas = new();
    Escolha hz = null!;
    Border linhaHz = null!;
    readonly Dictionary<string, (Chave c, Border linha)> chaves = new();
    Escolha escala = null!, profundidade = null!, formato = null!;
    Border linhaEscala = null!, linhaProfundidade = null!, linhaFormato = null!;
    Border linhaVariNivel = null!;
    Deslizante variNivel = null!;
    readonly Dictionary<string, (Deslizante d, TextBlock valor, TextBlock padrao)> cores = new();
    readonly List<(Color baseCor, Border b)> barras = new();
    Border gradiente = null!;
    Botao zerar = null!;
    StackPanel corpoCor = null!;
    TextBlock semCor = null!;

    static (string Chave, string Nome, string Desc)[] Chaves => AcoesTelas.Chaves;
    static (string Chave, string Nome, double Padrao, string Un)[] Cores => AcoesTelas.Cores;

    protected override FrameworkElement Montar()
    {
        Cabecalho("Telas", () => { int n = Fx?.Telas.Count ?? 0; return n == 1 ? "1 tela ligada" : $"{n} telas ligadas"; });
        cartoesTelas = new StackPanel();
        var g = K.Grade("1* 1.4*", 12, MontarImagem(), MontarCor());
        foreach (UIElement x in g.Children) if (x is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Top;
        var raiz = K.Col(12, cartoesTelas, g);
        raiz.Margin = new Thickness(20, 12, 20, 20);
        return raiz;
    }

    void PintarTelas()
    {
        cartoesTelas.Children.Clear();
        var telas = Fx?.Telas ?? new();
        if (telas.Count == 0)
        {
            cartoesTelas.Children.Add(K.Cartao(K.Nota(Fx?.AdlxDisponivel == false ? "Driver da AMD sem resposta. Ajustes de tela indisponíveis." : "Lendo as telas pelo driver da AMD…")));
            return;
        }
        if (telaId is null || telas.All(t => t.Id != telaId)) telaId = telas[0].Id;
        for (int i = 0; i < telas.Count; i++)
        {
            var t = telas[i];
            bool on = t.Id == telaId;
            var icone = new Canvas { Width = 74, Height = 56 };
            icone.Children.Add(new Rectangle { Width = 70, Height = 42, RadiusX = 3, RadiusY = 3, Fill = P.PocoEscuro, Stroke = on && telas.Count > 1 ? P.Texto : P.BordaForte, StrokeThickness = 1.5, Margin = new Thickness(2, 2, 0, 0) });
            icone.Children.Add(new Path { Data = Geometry.Parse("M30 44v6h14v-6M24 52h26"), Stroke = P.BordaForte, StrokeThickness = 1.5 });
            var nome = K.Col(0, K.T(t.Nome, 17, P.Texto, FontWeights.SemiBold), K.T(i == 0 ? "tela principal, conectada à placa de vídeo" : "tela secundária, conectada à placa de vídeo", 12.5, P.Apagado));
            UIElement Dado(string rotulo, string valor) => K.Col(0, K.T(rotulo, 12.5, P.Apagado), K.T(valor, 24, P.Texto, P.PesoNumero, P.Numero));
            var ajuste = Ajuste(Cfg, t.Id);
            var lida = lidas.FirstOrDefault(x => x.Id == t.Id);
            bool? fs = ajuste?.FreeSync ?? lida?.FreeSync;
            var grade = K.Grade("74 * 150 150 150", 18, icone, nome,
                Dado("Resolução", $"{t.Largura} × {t.Altura}"),
                Dado("Atualização", $"{K.N(ajuste?.Hz ?? t.Hz)} Hz"),
                Dado("FreeSync", !t.Suporta.Contains("freesync") ? "não aceita" : fs is null ? "–" : fs.Value ? "ligado" : "desligado"));
            grade.VerticalAlignment = VerticalAlignment.Center;
            var cartao = K.Cartao(grade, new Thickness(16, 14, 16, 14));
            cartao.Margin = new Thickness(0, 0, 0, i < telas.Count - 1 ? 12 : 0);
            if (telas.Count > 1)
            {
                var id = t.Id;
                cartao.Cursor = K.Mao;
                if (on) cartao.BorderBrush = P.Texto;
                cartao.MouseLeftButtonUp += (_, _) => { telaId = id; Atualizar(); };
            }
            cartoesTelas.Children.Add(cartao);
        }
    }

    static Border Linha(UIElement rotulo, UIElement controle, double largura) => K.LinhaAjuste(rotulo, controle, largura, 46);

    Border MontarImagem()
    {
        var corpo = new StackPanel();
        hz = new Escolha(Array.Empty<(string, string)>(), null);
        hz.Escolheu += v => _ = Mudar(a => a.Hz = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture), $"Tela em {v.Replace('.', ',')} Hz.");
        linhaHz = Linha(K.T("Taxa de atualização"), hz, 150);
        corpo.Children.Add(linhaHz);
        foreach (var (chave, nome, desc) in Chaves)
        {
            var c = new Chave();
            var k = chave;
            c.Mudou += on => _ = Mudar(a => Definir(a, k, on), $"{nome} {(on ? "ligado" : "desligado")}.");
            var linha = Linha(K.Col(0, K.T(nome), K.T(desc, 12, P.Apagado)), c, 40);
            chaves[chave] = (c, linha);
            corpo.Children.Add(linha);
            if (chave == "escalaGpu")
            {
                escala = new Escolha(new[] { ("preservar", "Manter proporção"), ("tela-cheia", "Tela cheia"), ("centralizado", "Centralizada") }, null);
                escala.Escolheu += v => _ = Mudar(a => a.ModoEscala = v, "Modo de escala atualizado.");
                linhaEscala = Linha(K.Col(0, K.T("Modo de escala"), K.T("ajuste de imagens em resolução menor", 12, P.Apagado)), escala, 150);
                corpo.Children.Add(linhaEscala);
            }
            if (chave == "variBright")
            {
                variNivel = new Deslizante(0, 4, 1, 2);
                variNivel.Mudou += v => _ = Mudar(a => a.VariBrightNivel = (int)v, $"Vari-Bright no nível {v + 1:0} de 5.");
                linhaVariNivel = Linha(K.Col(0, K.T("Intensidade do Vari-Bright"), K.T("da maior fidelidade à maior economia", 12, P.Apagado)), variNivel, 150);
                corpo.Children.Add(linhaVariNivel);
            }
        }
        profundidade = new Escolha(AcoesTelas.Profundidades, null);
        profundidade.Escolheu += v => _ = Mudar(a => a.ProfundidadeCor = int.Parse(v), $"Profundidade de cor em {v} bits.");
        linhaProfundidade = Linha(K.T("Profundidade de cor"), profundidade, 150);
        formato = new Escolha(AcoesTelas.Formatos, null);
        formato.Escolheu += v => _ = Mudar(a => a.FormatoPixel = v, "Formato de cor atualizado.");
        linhaFormato = Linha(K.T("Formato de cor"), formato, 150);
        corpo.Children.Add(linhaProfundidade);
        corpo.Children.Add(linhaFormato);
        var titulo = K.Titulo("Imagem"); titulo.MinHeight = 34; titulo.VerticalAlignment = VerticalAlignment.Center;
        var tit = new Border { Height = 34, Child = titulo };
        titulo.Padding = new Thickness(0, 7, 0, 0);
        return K.Cartao(K.Col(0, tit, corpo), new Thickness(16, 12, 16, 6));
    }

    static void Definir(AjusteTela a, string chave, bool v) => AcoesTelas.Definir(a, chave, v);
    static bool? Valor(AjusteTela? a, string chave) => AcoesTelas.Valor(a, chave);
    static int? Cor(AjusteTela? a, string chave) => AcoesTelas.Cor(a, chave);
    static void Cor(AjusteTela a, string chave, int v) => AcoesTelas.Cor(a, chave, v);
    static AjusteTela? Ajuste(Config? c, string? id) => AcoesTelas.Ajuste(c, id);
    Task<bool> Mudar(Action<AjusteTela> mudar, string ok) => AcoesTelas.Mudar(Fx?.Telas.FirstOrDefault(t => t.Id == telaId), mudar, ok);

    Border MontarCor()
    {
        zerar = Botao.Contorno("Voltar ao padrão", () => _ = Mudar(a => { foreach (var (k, _, p, _) in Cores) Cor(a, k, (int)(Padrao(k) ?? p)); }, "Cor da tela de volta ao padrão."), 32);
        var cab = K.Grade("* 150", 10, K.Titulo("Cor personalizada"), zerar);
        cab.MinHeight = 34;
        var barrasGrade = new Grid { Height = 64 };
        foreach (var hex in new[] { "#C0C0C0", "#C0C000", "#00C0C0", "#00C000", "#C000C0", "#C00000", "#0000C0" })
        {
            barrasGrade.ColumnDefinitions.Add(new ColumnDefinition());
            var b = new Border();
            Grid.SetColumn(b, barrasGrade.ColumnDefinitions.Count - 1);
            barrasGrade.Children.Add(b);
            barras.Add(((Color)ColorConverter.ConvertFromString(hex), b));
        }
        gradiente = new Border { Height = 22 };
        var amostras = new Grid { Height = 30 };
        foreach (var hex in new[] { "#E8B28A", "#3A6E3A", "#6A8FC7" })
        {
            amostras.ColumnDefinitions.Add(new ColumnDefinition());
            var b = new Border();
            Grid.SetColumn(b, amostras.ColumnDefinitions.Count - 1);
            amostras.Children.Add(b);
            barras.Add(((Color)ColorConverter.ConvertFromString(hex), b));
        }
        var previa = new Border { CornerRadius = new CornerRadius(8), BorderBrush = P.BordaForte, BorderThickness = new Thickness(1), ClipToBounds = true, Child = K.Col(0, barrasGrade, gradiente, amostras) };
        previa.Clip = null;
        var linhas = new StackPanel();
        foreach (var (chave, nome, padrao, un) in Cores)
        {
            var d = new Deslizante(0, 100, 1, 0);
            var v = K.T("", 19, P.Texto, P.PesoNumero, P.Numero); v.HorizontalAlignment = HorizontalAlignment.Right;
            var pd = K.T("", 11.5, P.Apagado);
            var k = chave; var u = un;
            d.Movendo += x => { v.Text = x.ToString("N0", K.Br) + u; PintarPrevia(); };
            d.Mudou += x => _ = Mudar(a => Cor(a, k, (int)x), $"{nome}: {x.ToString("N0", K.Br)}{u}.");
            cores[chave] = (d, v, pd);
            var g = K.Grade("150 * 70", 12, K.Col(0, K.T(nome), pd), d, v);
            g.VerticalAlignment = VerticalAlignment.Center;
            linhas.Children.Add(new Border { MinHeight = P.E(44), BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = g });
        }
        semCor = K.Nota("Ajuste de cor não suportado pelo driver nesta tela.");
        corpoCor = K.Col(10, previa, linhas, K.Nota("Prévia aproximada. O resultado real é exibido na tela ao aplicar."));
        return K.Cartao(K.Col(10, cab, corpoCor, semCor), new Thickness(16, 12, 16, 14));
    }

    double? Padrao(string chave) => Fx?.Campos.GetValueOrDefault($"telas.{telaId}.{chave}")?.Padrao;

    void PintarPrevia()
    {
        double bri = cores["brilho"].d.Valor, con = cores["contraste"].d.Valor, sat = cores["saturacao"].d.Valor, tem = cores["temperatura"].d.Valor;
        Color T(Color c) => AcoesTelas.Transformar(c, bri, con, sat, tem);
        foreach (var (c, b) in barras) b.Background = new SolidColorBrush(T(c));
        gradiente.Background = AcoesTelas.Gradiente(T);
    }

    protected override void Config(Config c) => Atualizar();
    public override void NovasFaixas(Faixas f) => Atualizar();

    public override async void Mostrada()
    {
        base.Mostrada();
        try
        {
            var l = await App.Agente.LerTelas();
            lidas.Clear(); lidas.AddRange(l);
            Atualizar();
        }
        catch (Exception x) { App.Registrar(x); }
    }

    void Atualizar()
    {
        PintarTelas();
        var info = Fx?.Telas.FirstOrDefault(t => t.Id == telaId);
        var a = Ajuste(Cfg, telaId);
        var lida = lidas.FirstOrDefault(x => x.Id == telaId);
        bool S(string chave) => info?.Suporta.Contains(chave) == true;
        Visibility V(bool sim) => sim ? Visibility.Visible : Visibility.Collapsed;

        linhaHz.Visibility = V(info is { Taxas.Count: > 1 });
        if (info is not null)
        {
            var atual = a?.Hz ?? info.Hz;
            var taxas = info.Taxas.OrderByDescending(x => x).ToList();
            hz.Opcoes(taxas.Select(x => (x.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{K.N(x)} Hz")),
                taxas.OrderBy(x => Math.Abs(x - atual)).Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault());
        }
        foreach (var (chave, _, _) in Chaves)
        {
            var (c, linha) = chaves[chave];
            linha.Visibility = V(S(chave));
            var v = Valor(a, chave) ?? Valor(lida, chave);
            c.Ligada = v == true;
            c.IsEnabled = v is not null || a is not null;
        }
        bool escalaGpu = (a?.EscalaGpu ?? lida?.EscalaGpu) == true;
        linhaEscala.Visibility = V(S("modoEscala"));
        escala.Selecionar(a?.ModoEscala ?? lida?.ModoEscala ?? "preservar");
        escala.IsEnabled = escalaGpu;
        linhaVariNivel.Visibility = V(S("variBright") && (a?.VariBright ?? lida?.VariBright) == true);
        variNivel.Valor = a?.VariBrightNivel ?? lida?.VariBrightNivel ?? Fx?.Campos.GetValueOrDefault($"telas.{telaId}.variBrightNivel")?.Atual ?? 2;
        linhaProfundidade.Visibility = V(S("profundidadeCor"));
        profundidade.Opcoes(AcoesTelas.Aceitas(AcoesTelas.Profundidades, info?.Profundidades.Select(x => x.ToString())), (a?.ProfundidadeCor ?? lida?.ProfundidadeCor)?.ToString());
        linhaFormato.Visibility = V(S("formatoPixel"));
        formato.Opcoes(AcoesTelas.Aceitas(AcoesTelas.Formatos, info?.Formatos), a?.FormatoPixel ?? lida?.FormatoPixel);

        bool temCor = S("cor");
        corpoCor.Visibility = zerar.Visibility = V(temCor);
        semCor.Visibility = V(!temCor && info is not null);
        foreach (var (chave, nome, padrao, un) in Cores)
        {
            var (d, valor, pd) = cores[chave];
            var f = Fx?.Campos.GetValueOrDefault($"telas.{telaId}.{chave}");
            double p = f?.Padrao ?? padrao;
            if (f is { Disponivel: true }) { d.Min = f.Min; d.Max = f.Max; d.Passo = Math.Max(1, f.Passo); }
            else if (chave == "temperatura") { d.Min = 4000; d.Max = 10000; d.Passo = 100; }
            else { d.Min = chave == "brilho" ? -100 : 0; d.Max = chave == "brilho" ? 100 : 200; }
            double v = Cor(a, chave) ?? Cor(lida, chave) ?? f?.Atual ?? p;
            d.Valor = v;
            d.IsEnabled = f is { Disponivel: true };
            valor.Text = v.ToString("N0", K.Br) + un;
            pd.Text = f is { Disponivel: false } ? f.Motivo ?? "travado" : "padrão " + p.ToString("N0", K.Br) + un;
        }
        PintarPrevia();
    }
}
