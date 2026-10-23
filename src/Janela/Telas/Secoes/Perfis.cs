using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas.Secoes;

sealed class Perfis : Tela
{
    protected override string Id => "perfis";
    string? sel;
    StackPanel lista = null!;
    TextBlock tituloAreas = null!;
    readonly Dictionary<string, (TextBlock valor, TextBlock conf, Chave c)> areas = new();
    CampoAtalho atalho = null!;
    Escolha programa = null!;
    CampoTexto inicio = null!, fim = null!;
    TextBlock ultima = null!;
    Botao aplicar = null!, excluir = null!, restaurar = null!;
    DateTime restaurarPedido;

    internal static readonly (string Id, string Nome)[] Areas =
    {
        ("ventoinhas", "Ventoinhas"), ("processador", "Processador"), ("placaDeVideo", "Placa de vídeo"), ("luz", "Luz"),
        ("energia", "Energia"), ("graficos", "Ajustes gráficos"), ("sobreposicao", "Sobreposição"),
    };

    protected override FrameworkElement Montar()
    {
        Cabecalho("Perfis", () => "alterações em qualquer tela são salvas no perfil ativo");
        var baixo = K.Grade("1.3* 1*", 12, MontarQuando(), MontarAcoes());
        foreach (UIElement x in baixo.Children) if (x is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Top;
        var corpo = K.Grade("240 *", 12, MontarLista(), K.Col(12, MontarAreas(), baixo));
        ((FrameworkElement)corpo.Children[0]).VerticalAlignment = VerticalAlignment.Top;
        var raiz = K.Col(12, corpo);
        raiz.Margin = new Thickness(20, 12, 20, 20);
        return raiz;
    }

    Perfil? Atual(Config? c = null) => (c ?? Cfg)?.Perfis.FirstOrDefault(p => p.Nome == sel);

    Border MontarLista()
    {
        lista = new StackPanel();
        var traco = new Rectangle { Stroke = P.BordaForte, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, RadiusX = P.RaioBotao, RadiusY = P.RaioBotao };
        var txt = K.T("Novo, com o estado atual", 14, P.Secundario); txt.HorizontalAlignment = HorizontalAlignment.Center; txt.VerticalAlignment = VerticalAlignment.Center;
        var novo = new Botao { Height = P.E(40), Child = new Grid { Children = { traco, txt } }, Margin = new Thickness(0, 4, 0, 0), CornerRadius = new CornerRadius(P.RaioBotao) };
        novo.FundoNormal = Brushes.Transparent; novo.Background = Brushes.Transparent; novo.FundoSobre = P.Poco;
        novo.Clique += (_, _) => _ = Novo();
        return K.Cartao(K.Col(4, lista, novo), new Thickness(8));
    }

    async Task Novo()
    {
        var c = Cfg;
        if (c is null) return;
        int k = 1; string nome;
        do { nome = $"Meu perfil {k++}"; } while (c.Perfis.Any(p => p.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase)));
        if (await Fazer("perfil.salvar", Ligacao.Args(("nome", nome)), $"{nome} criado com o estado atual. Clique duas vezes no nome para renomeá-lo.")) { sel = nome; Atualizar(); }
    }

    void PintarLista(Config c)
    {
        lista.Children.Clear();
        foreach (var p in c.Perfis)
        {
            bool on = p.Nome == sel;
            var tn = K.T(p.Nome, 14, on ? P.TextoSelecao : P.Texto, FontWeights.SemiBold);
            var ativo = K.T(p.Nome == c.PerfilAtivo ? (Est?.PerfilAlterado == true ? "ativo, alterado" : "ativo") : "", 12, on ? P.SubSel : P.Apagado);
            ativo.HorizontalAlignment = HorizontalAlignment.Right;
            var sub = K.T(string.IsNullOrEmpty(p.Atalho) ? "sem atalho" : p.Atalho.Replace("+", " "), 12, on ? P.SubSel : P.Apagado);
            var b = new Botao { Height = P.E(54), CornerRadius = new CornerRadius(P.RaioBotao), Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 4), ToolTip = "Clique duas vezes para renomear" };
            var nomeLinha = K.Grade("* auto", 8, tn, ativo);
            b.Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { nomeLinha, sub } };
            b.Fundo(on ? P.Selecao : Brushes.Transparent); b.FundoSobre = on ? P.Selecao : P.Poco;
            var nome = p.Nome;
            b.Clique += (_, e) => { if (e?.ClickCount == 2) Renomear(b, nome); else { sel = nome; Atualizar(); } };
            lista.Children.Add(b);
        }
    }

    void Renomear(Botao b, string nome)
    {
        var campo = new CampoTexto(nome, double.NaN, 34) { Aceita = s => s.Length is > 0 and <= 40 };
        var antes = b.Child;
        b.Child = campo;
        campo.Confirmou += async novo =>
        {
            if (await Fazer("perfil.renomear", Ligacao.Args(("nome", nome), ("novo", novo)), $"Perfil renomeado para {novo}.")) sel = novo;
            Atualizar();
        };
        campo.LostKeyboardFocus += (_, _) => { if (b.Child == campo) b.Child = antes; };
        campo.Dispatcher.BeginInvoke(campo.Focus);
    }

    Border MontarAreas()
    {
        tituloAreas = K.Titulo("");
        var dica = K.T("Áreas desligadas não são alteradas pelo perfil", 12.5, P.Apagado);
        var cab = K.Grade("* auto", 10, tituloAreas, dica);
        cab.MinHeight = 36;
        var corpo = new StackPanel();
        foreach (var (id, nome) in Areas)
        {
            var v = K.T("", 14);
            var cf = K.T("", 12.5, P.Apagado); cf.HorizontalAlignment = HorizontalAlignment.Right;
            var c = new Chave();
            var a = id;
            c.Mudou += on => _ = Area(a, on);
            areas[id] = (v, cf, c);
            var g = K.Grade("150 * 110 40", 12, K.T(nome, 14, P.Texto, FontWeights.Medium), v, cf, c);
            g.VerticalAlignment = VerticalAlignment.Center;
            corpo.Children.Add(new Border { MinHeight = P.E(42), BorderBrush = P.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = g });
        }
        return K.Cartao(K.Col(0, cab, corpo), new Thickness(16, 12, 16, 6));
    }

    Task<bool> Area(string area, bool on) => Gravar(c => { if (Atual(c) is { } p) LigarArea(c, p, area, on); },
        $"{sel}: {Areas.First(x => x.Id == area).Nome.ToLowerInvariant()} {(on ? "entra" : "fica de fora")}.");

    // Ligar uma área que o perfil nunca guardou grava nela o estado atual.
    internal static void LigarArea(Config c, Perfil p, string area, bool on)
    {
        if (!on) { p.Areas.Remove(area); return; }
        if (!p.Areas.Contains(area)) p.Areas.Add(area);
        switch (area)
        {
            case "ventoinhas" when p.Ventoinhas is null: p.Ventoinhas = Json.Copia(c.Ventoinhas); p.Ventoinhas.Grupos = new(); break;
            case "processador" when p.Processador is null: p.Processador = Json.Copia(c.Processador); break;
            case "placaDeVideo" when p.PlacaDeVideo is null: p.PlacaDeVideo = Json.Copia(c.PlacaDeVideo); break;
            case "luz" when p.Luz is null: p.Luz = new ConfigLuz { Plano = c.Luz.Plano, Ligada = c.Luz.Ligada, Saidas = new(), Planos = new() }; break;
            case "energia" when p.Energia is null: p.Energia = new ConfigEnergia { Plano = c.Energia.Plano }; break;
            case "graficos" when p.Graficos is null: p.Graficos = Json.Copia(c.Jogos.Global); break;
            case "sobreposicao" when p.Sobreposicao is null: p.Sobreposicao = Json.Copia(c.Jogos.Sobreposicao); break;
        }
    }

    static string Nivel(string n) => n switch
    {
        "silencioso" => "Silencioso", "equilibrado" => "Equilibrado", "forte" => "Forte", "maximo" => "Máximo", _ => "Personalizado",
    };

    internal static string Descrever(Perfil p, string area, Faixa? ppt) => area switch
    {
        "ventoinhas" when p.Ventoinhas is { } v => Nivel(v.Nivel) + (v.Saidas.FirstOrDefault(s => s.Id == "cpu") is { } cpu
            ? cpu.Modo == "fixo" ? $", processador fixo em {cpu.Fixo:0}%" : cpu.Modo == "curva" ? $", {Curvas.Avaliar(cpu.Curva, 75):0}% com o processador a 75 °C" : ", processador pela BIOS" : ""),
        "processador" when p.Processador is { } c => (c.Eco == true ? "Eco" : c.CoTodos is int co0 && co0 != 0 ? "Manual"
            : Padroes.ModoPpt(c.Ppt, ppt) switch { "eco" => "Eco", "maximo" => "Máximo", "padrao" => "Padrão", _ => "Manual" })
            + (c.Ppt is int w && w > 0 ? $", {w} W" : "") + (c.CoTodos is int co && co != 0 ? $", curva {co:+0;-0}" : ""),
        "placaDeVideo" when p.PlacaDeVideo is { } g => Json.Texto(g) == Json.Texto(new ConfigPlacaDeVideo()) ? "Automático"
            : "Manual" + (g.LimiteEnergia is int le && le != 0 ? $", energia {le:+0;-0}%" : "") + (g.Tensao is int t && t != 0 ? $", tensão {t:+0;-0} mV" : ""),
        "luz" when p.Luz is { } l => l.Ligada ? l.Plano : "Apagada",
        "energia" when p.Energia is { } e => e.Plano is string id ? "Plano " + Padroes.NomePlano(id) : "Não muda o plano",
        "graficos" when p.Graficos is { } a => Ligados(a),
        "sobreposicao" when p.Sobreposicao is { } s => s.Ligada ? $"Ligada, {s.Leituras.Count} leituras" : "Desligada",
        _ => "não guardado",
    };

    static string Ligados(Ajustes3D a)
    {
        var l = AcoesJogos.Chaves.Where(x => AcoesJogos.Chave(a, x.Chave) == true).Select(x => x.Nome).ToList();
        return l.Count == 0 ? "Nada ligado" : string.Join(", ", l) + (l.Count == 1 ? " ligado" : " ligados");
    }

    Border MontarQuando()
    {
        atalho = new CampoAtalho(32) { AceitaVazio = true };
        atalho.Gravou += a => _ = Gravar(c => { if (Atual(c) is { } p) p.Atalho = a; }, a is null ? "Perfil sem atalho." : $"Atalho {a.Replace("+", " ")} para {sel}.");
        programa = new Escolha(Array.Empty<(string, string)>(), null);
        programa.Escolheu += v => _ = Programa(v);
        inicio = new CampoTexto("", double.NaN, 32) { Aceita = s => s.Length == 0 || Validacao.HorarioValido(s) };
        fim = new CampoTexto("", double.NaN, 32) { Aceita = s => s.Length == 0 || Validacao.HorarioValido(s) };
        inicio.Confirmou += x => _ = Horario();
        fim.Confirmou += x => _ = Horario();
        var a = K.T("a", 14, P.Apagado); a.HorizontalAlignment = HorizontalAlignment.Center; a.VerticalAlignment = VerticalAlignment.Center;
        var horario = K.Grade("* 20 *", 4, inicio, a, fim);
        var titulo = K.Titulo("Quando entrar"); titulo.Height = 36; titulo.Padding = new Thickness(0, 8, 0, 0);
        var nota = K.Nota("Ao fechar o programa ou terminar o horário, o perfil anterior é restaurado.");
        nota.Margin = new Thickness(0, 6, 0, 6);
        return K.Cartao(K.Col(0, titulo,
            K.LinhaAjuste("Atalho de teclado", atalho, 210, 46),
            K.LinhaAjuste("Ao abrir um programa", programa, 210, 46),
            K.LinhaAjuste("Em um horário", horario, 210, 46),
            nota), new Thickness(16, 12, 16, 6));
    }

    async Task Programa(string v)
    {
        if (v == "+")
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Programas (*.exe)|*.exe", Title = "Escolha o programa" };
            if (d.ShowDialog() != true) { Atualizar(); return; }
            var arq = d.FileName;
            await Gravar(c => { if (Atual(c) is { } p && !p.Programas.Contains(arq, StringComparer.OrdinalIgnoreCase)) p.Programas = new() { arq }; }, $"{sel} é aplicado ao abrir {Path.GetFileNameWithoutExtension(arq)}.");
            return;
        }
        await Gravar(c => { if (Atual(c) is { } p) p.Programas = v == "" ? new() : new() { v }; }, v == "" ? $"{sel} não é mais aplicado por programa." : null);
    }

    async Task Horario()
    {
        string i = inicio.Text(), f = fim.Text();
        if (i.Length > 0 != f.Length > 0) return;
        await Gravar(c => { if (Atual(c) is { } p) p.Horario = i.Length == 0 ? null : $"{i}-{f}"; }, i.Length == 0 ? $"{sel} sem horário." : $"{sel} é aplicado das {i} às {f}.");
    }

    Border MontarAcoes()
    {
        var titulo = K.Titulo("Última troca"); titulo.Height = 36; titulo.Padding = new Thickness(0, 8, 0, 0);
        ultima = new TextBlock { Foreground = P.Secundario, TextWrapping = TextWrapping.Wrap, FontSize = P.F(14), MinHeight = 60 };
        aplicar = Botao.Cheio("", () => { if (sel is string s) _ = Fazer("perfil.aplicar", Ligacao.Args(("nome", s), ("origem", "janela"))); }, 38);
        var exportar = Botao.Contorno("Exportar", Exportar, 36);
        var importar = Botao.Contorno("Importar", () => _ = Importar(), 36);
        var duplicar = Botao.Contorno("Duplicar", () => _ = Duplicar(), 36);
        excluir = Botao.Contorno("Excluir", () => _ = Excluir(), 36);
        restaurar = Botao.Contorno("Restaurar padrão", () => _ = Restaurar(), 38);
        foreach (var b in new[] { exportar, importar, duplicar, excluir, restaurar }) ((TextBlock)b.Child).Foreground = P.Texto;
        return K.Cartao(K.Col(10, titulo, ultima, K.Iguais(2, 8, new UIElement[] { aplicar, restaurar }), K.Iguais(2, 8, new UIElement[] { exportar, importar, duplicar, excluir })), new Thickness(16, 12, 16, 14));
    }

    // Primeiro clique só pede confirmação: restaurar apaga os ajustes guardados no perfil.
    async Task Restaurar()
    {
        if (sel is null) return;
        if (DateTime.Now - restaurarPedido > TimeSpan.FromSeconds(5))
        {
            restaurarPedido = DateTime.Now;
            restaurar.TextoBotao = "Clique para confirmar";
            return;
        }
        restaurarPedido = default;
        if (await Fazer("perfil.restaurar", Ligacao.Args(("nome", sel)), $"{sel} restaurado aos valores de fábrica.")) Atualizar();
    }

    void Exportar()
    {
        if (Atual() is not { } p) return;
        var d = new Microsoft.Win32.SaveFileDialog { Filter = "Perfil do SetupHub (*.json)|*.json", FileName = p.Nome + ".json", Title = "Exportar perfil" };
        if (d.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(d.FileName, Json.Texto(p, true), new UTF8Encoding(false));
            App.Janela?.Aviso($"{p.Nome} exportado para {d.FileName}.");
        }
        catch (Exception x) { App.Janela?.Aviso("Não foi possível exportar: " + x.Message, true); }
    }

    async Task Importar()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Perfil do SetupHub (*.json)|*.json", Title = "Importar perfil" };
        if (d.ShowDialog() != true) return;
        Perfil? p;
        try { p = Json.DeTexto<Perfil>(File.ReadAllText(d.FileName)); }
        catch (Exception x) { App.Janela?.Aviso("O arquivo não é um perfil do SetupHub: " + x.Message, true); return; }
        if (p is null || string.IsNullOrWhiteSpace(p.Nome)) { App.Janela?.Aviso("O arquivo não é um perfil do SetupHub.", true); return; }
        p.Pronto = false;
        var c = Cfg;
        if (c is null) return;
        var nome = p.Nome; int k = 2;
        while (c.Perfis.Any(x => x.Nome.Equals(p.Nome, StringComparison.OrdinalIgnoreCase))) p.Nome = $"{nome} {k++}";
        if (await Gravar(cf => cf.Perfis.Add(p), $"Perfil {p.Nome} importado.")) { sel = p.Nome; Atualizar(); }
    }

    async Task Duplicar()
    {
        if (sel is null) return;
        var r = await App.Lig.Pedir("perfil.duplicar", Ligacao.Args(("nome", sel)));
        if (!r.Ok) { App.Janela?.Aviso(r.Erro ?? "Recusado.", true); return; }
        if (Json.Ler<string>(r.Dados) is string novo) { sel = novo; App.Janela?.Aviso($"{novo} criado como cópia."); Atualizar(); }
    }

    async Task Excluir()
    {
        if (sel is null) return;
        if (await Fazer("perfil.excluir", Ligacao.Args(("nome", sel)), $"{sel} excluído.")) { sel = Cfg?.PerfilAtivo; Atualizar(); }
    }

    protected override void Config(Config c) => Atualizar();

    void Atualizar()
    {
        var c = Cfg;
        if (c is null) return;
        if (sel is null || c.Perfis.All(p => p.Nome != sel)) sel = c.PerfilAtivo.Length > 0 && c.Perfis.Any(p => p.Nome == c.PerfilAtivo) ? c.PerfilAtivo : c.Perfis.FirstOrDefault()?.Nome;
        PintarLista(c);
        var p = Atual(c);
        if (p is null) return;
        tituloAreas.Text = p.Nome + " aplica";
        bool ativo = p.Nome == c.PerfilAtivo;
        foreach (var (id, _) in Areas)
        {
            var (v, cf, ch) = areas[id];
            bool on = p.Areas.Contains(id);
            ch.Ligada = on;
            v.Text = on ? Descrever(p, id, Fx?.Campos.GetValueOrDefault("processador.ppt")) : "não mexe";
            v.Foreground = on ? P.Texto : P.Tenue;
            bool dif = DiferencaPerfil.Area(p, c, id);
            cf.Text = ativo && on ? dif ? "mudou depois" : "conferido" : "";
            cf.Foreground = dif ? P.Morno : P.Apagado;
        }
        atalho.Definir(p.Atalho);
        var progs = p.Programas.Select(x => (x, Path.GetFileNameWithoutExtension(x))).ToList();
        programa.Opcoes(new[] { ("", "Nenhum") }.Concat(progs).Append(("+", "Escolher programa…")), p.Programas.FirstOrDefault() ?? "");
        var h = (p.Horario ?? "").Split('-', StringSplitOptions.TrimEntries);
        inicio.Definir(h.Length == 2 ? h[0] : ""); fim.Definir(h.Length == 2 ? h[1] : "");
        aplicar.TextoBotao = ativo ? "Este é o perfil ativo" : "Aplicar " + p.Nome;
        aplicar.IsEnabled = !ativo || Est?.PerfilAlterado == true;
        if (ativo && Est?.PerfilAlterado == true) aplicar.TextoBotao = "Reaplicar " + p.Nome;
        excluir.IsEnabled = !ativo;
        excluir.ToolTip = ativo ? "O perfil ativo não pode ser excluído. Aplique outro perfil antes." : null;
        restaurarPedido = default;
        restaurar.TextoBotao = "Restaurar padrão";
        restaurar.IsEnabled = Padroes.Fabrica(p) is not null;
        restaurar.ToolTip = restaurar.IsEnabled ? null : "Perfil personalizado: sem valores de fábrica.";
        ultima.Text = Est?.UltimaTroca ?? "Nenhuma troca de perfil desde o início do SetupHub.";
    }

    string? ultimaVista; bool alteradoVisto;
    public override void Estado(Estado e)
    {
        if (e.UltimaTroca == ultimaVista && e.PerfilAlterado == alteradoVisto) return;
        ultimaVista = e.UltimaTroca; alteradoVisto = e.PerfilAlterado;
        Atualizar();
    }
}
