using System.Windows;
using System.Windows.Controls;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas;

// Ajustes do efeito que está nas saídas escolhidas. Os dois visuais usam o mesmo; cada mudança vai na hora.
sealed class EditorLuz
{
    public readonly StackPanel Painel = new();
    readonly Func<EfeitoLuz, string, Task> gravar;
    EfeitoLuz? atual;
    readonly Segmentado quantas;
    readonly Border linhaQuantas, linhaCores, linhaArco, linhaFonte, linhaVel, linhaDir, linhaBrilho;
    readonly List<CorEscolha> livres = new();
    readonly List<(Border linha, TextBlock rotulo, CampoNum temp, CorEscolha cor)> rotuladas = new();
    readonly Chave arco;
    readonly Escolha fonte;
    readonly Segmentado vel, dir;
    readonly Deslizante brilho;
    readonly TextBlock brilhoTxt;

    static readonly string[] Paleta = { "#FF0000", "#00FF00", "#0000FF", "#FFFFFF" };

    public EditorLuz(Func<EfeitoLuz, string, Task> gravar, double largura)
    {
        this.gravar = gravar;
        quantas = new Segmentado(new[] { ("1", "1"), ("2", "2"), ("3", "3"), ("4", "4") }, 30);
        quantas.Escolheu += v => Mudar(ef =>
        {
            int n = int.Parse(v);
            while (ef.Cores.Count < n) ef.Cores.Add(Paleta[ef.Cores.Count]);
            if (ef.Cores.Count > n) ef.Cores = ef.Cores.Take(n).ToList();
        }, $"{v} {(v == "1" ? "cor" : "cores")}.");
        linhaQuantas = K.LinhaAjuste("Quantas cores", quantas, largura, 42);

        var painelCores = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 0; i < 4; i++)
        {
            var c = new CorEscolha(Paleta[i], K.P.E(30)) { Width = K.P.E(42), Margin = new Thickness(0, 0, 6, 0) };
            int k = i;
            c.Escolheu += cor => Mudar(ef => { while (ef.Cores.Count <= k) ef.Cores.Add(cor); ef.Cores[k] = cor; }, "Cor atualizada.");
            livres.Add(c);
            painelCores.Children.Add(c);
        }
        linhaCores = K.LinhaAjuste("Cores", painelCores, largura, 42);

        for (int i = 0; i < 3; i++)
        {
            int k = i;
            var rotulo = K.T("");
            var temp = new CampoNum(null, "°C", 76, 30) { AceitaVazio = false };
            temp.Confirmou += v => { if (v is double d) Mudar(ef => ef.Temperaturas[k] = (int)Math.Round(d), "Faixa de temperatura atualizada."); };
            var cor = new CorEscolha("#FFFFFF", K.P.E(30)) { Width = K.P.E(42) };
            cor.Escolheu += x => Mudar(ef => { while (ef.Cores.Count <= k) ef.Cores.Add(x); ef.Cores[k] = x; }, "Cor atualizada.");
            var controle = K.Grade("* auto", 8, temp, cor);
            controle.HorizontalAlignment = HorizontalAlignment.Left;
            rotuladas.Add((K.LinhaAjuste(rotulo, controle, largura, 42), rotulo, temp, cor));
        }

        arco = new Chave();
        arco.Mudou += on => Mudar(ef => ef.ArcoIris = on, on ? "Cores do arco-íris." : "Cores escolhidas.");
        linhaArco = K.LinhaAjuste(K.Col(0, K.T("Arco-íris"), K.T("cores definidas pelo controlador", 12, K.P.Apagado)), arco, 40, 42);

        fonte = new Escolha(Array.Empty<(string, string)>(), null, 32);
        fonte.Escolheu += v => Mudar(ef => ef.Fonte = v, "Leitura atualizada.");
        linhaFonte = K.LinhaAjuste("Seguir", fonte, largura, 42);

        vel = new Segmentado(new[] { ("0", "Lenta"), ("1", "Média"), ("2", "Rápida") }, 30);
        vel.Escolheu += v => Mudar(ef => ef.Velocidade = int.Parse(v), "Velocidade atualizada.");
        linhaVel = K.LinhaAjuste("Velocidade", vel, largura, 42);

        dir = new Segmentado(new[] { ("fora", "Para fora"), ("dentro", "Para dentro") }, 30);
        dir.Escolheu += v => Mudar(ef => ef.Direcao = v, "Direção atualizada.");
        linhaDir = K.LinhaAjuste("Direção", dir, largura, 42);

        brilho = new Deslizante(1, 5, 1, 5);
        brilhoTxt = K.T("5 de 5");
        brilhoTxt.HorizontalAlignment = HorizontalAlignment.Right;
        brilho.Movendo += v => brilhoTxt.Text = $"{v:0} de 5";
        brilho.Mudou += v => Mudar(ef => ef.Brilho = (int)v * 20, $"Brilho em {v:0} de 5.");
        linhaBrilho = K.LinhaAjuste("Brilho", K.Grade("* 52", 10, brilho, brilhoTxt), largura, 42);

        Painel.Children.Add(linhaQuantas);
        Painel.Children.Add(linhaCores);
        foreach (var r in rotuladas) Painel.Children.Add(r.linha);
        foreach (var l in new[] { linhaArco, linhaFonte, linhaVel, linhaDir, linhaBrilho }) Painel.Children.Add(l);
    }

    void Mudar(Action<EfeitoLuz> m, string ok)
    {
        if (atual is null) return;
        var n = Json.Copia(atual);
        m(n);
        atual = n;
        Mostrar(n);
        _ = gravar(n, ok);
    }

    public void Mostrar(EfeitoLuz? ef)
    {
        atual = ef is null ? null : Json.Copia(ef);
        Painel.Visibility = ef is null || ef.Tipo is "desligado" ? Visibility.Collapsed : Visibility.Visible;
        if (ef is null) return;
        var t = PreviaLuz.DoTipo(ef.Tipo);
        static Visibility V(bool v) => v ? Visibility.Visible : Visibility.Collapsed;
        bool corEscolhida = !(t.ArcoIris && ef.ArcoIris);

        linhaQuantas.Visibility = linhaCores.Visibility = V(t.Cores == -1);
        quantas.Selecionar(Math.Clamp(ef.Cores.Count, 1, 4).ToString());
        for (int i = 0; i < livres.Count; i++)
        {
            livres[i].Visibility = V(i < Math.Max(1, ef.Cores.Count));
            livres[i].Definir(ef.Cores.ElementAtOrDefault(i) ?? Paleta[i]);
        }
        linhaQuantas.IsEnabled = linhaCores.IsEnabled = corEscolhida;

        bool temperatura = ef.Tipo == "temperatura";
        for (int i = 0; i < rotuladas.Count; i++)
        {
            var (linha, rotulo, temp, cor) = rotuladas[i];
            linha.Visibility = V(i < t.Cores);
            linha.IsEnabled = corEscolhida;
            if (i >= t.Cores) continue;
            rotulo.Text = t.Rotulos[i];
            temp.Visibility = V(temperatura);
            temp.Definir(temperatura ? ef.Temperaturas.ElementAtOrDefault(i) : null);
            cor.Definir(ef.Cores.ElementAtOrDefault(i) ?? ef.Cores.LastOrDefault() ?? "#FFFFFF");
        }

        linhaArco.Visibility = V(t.ArcoIris);
        arco.Ligada = ef.ArcoIris;
        linhaFonte.Visibility = V(t.Fonte);
        if (t.Fonte)
            fonte.Opcoes(ef.Tipo == "carga"
                ? new[] { ("cpu", "Uso do processador"), ("gpu", "Uso da placa de vídeo") }
                : new[] { ("cpu", "Processador"), ("gpu", "Placa de vídeo"), ("maior", "A maior das duas") }, ef.Fonte);
        linhaVel.Visibility = V(t.Velocidade);
        vel.Selecionar(ef.Velocidade.ToString());
        linhaDir.Visibility = V(t.Direcao);
        dir.Selecionar(ef.Direcao);
        linhaBrilho.Visibility = V(ef.Tipo != "padrao");
        int nivel = Math.Clamp((int)Math.Ceiling(ef.Brilho / 20.0), 1, 5);
        brilho.Valor = nivel;
        brilhoTxt.Text = $"{nivel} de 5";
    }
}
