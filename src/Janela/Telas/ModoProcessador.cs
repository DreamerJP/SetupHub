using System.Windows;
using System.Windows.Controls;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas;

// Cartão Eco, Padrão e Máximo do limite de consumo, usado no Início e no Processador.
sealed class ModoProcessador
{
    public readonly Border Cartao;
    readonly List<(string modo, Botao b, TextBlock nome, TextBlock lim, TextBlock desc)> modos = new();

    public ModoProcessador(string titulo, double alturaBotao)
    {
        var P = K.P;
        var lista = new StackPanel();
        foreach (var (id, nome, desc) in new[] { ("eco", "Eco", "Menor temperatura e ruído"), ("padrao", "Padrão", "Limites de fábrica"), ("maximo", "Máximo", "PBO no limite da placa-mãe") })
        {
            var tn = K.T(nome, 14, P.Texto, FontWeights.SemiBold);
            var tl = K.T("", 17, P.Texto, FontWeights.Normal, P.Numero);
            var td = K.T(desc, 12, P.Apagado);
            var b = new Botao { Height = alturaBotao, CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 6, 0, 0), Child = K.Col(1, K.Grade("* auto", 8, tn, tl), td) };
            ((StackPanel)b.Child).VerticalAlignment = VerticalAlignment.Center;
            b.Fundo(P.Poco);
            var m = id;
            b.Clique += (_, _) => _ = Aplicar(m);
            modos.Add((id, b, tn, tl, td));
            lista.Children.Add(b);
        }
        Cartao = K.Cartao(K.Col(0, K.Titulo(titulo), lista));
    }

    static async Task Aplicar(string modo)
    {
        var f = App.Lig.Faixas?.Campos.GetValueOrDefault("processador.ppt");
        if (f is not { Disponivel: true }) { App.Janela?.Aviso("Limite de consumo travado: " + (f?.Motivo ?? "faixa não lida do processador"), true); return; }
        int? ppt = modo switch { "eco" => Padroes.Eco(f), "maximo" => (int)f.Max, _ => null };
        await Tela.Gravar(c => { c.Processador.Ppt = ppt; c.Processador.Tdc = null; c.Processador.Edc = null; }, modo == "padrao" ? "Processador nos limites de fábrica." : $"Limite de consumo em {ppt} W.");
    }

    public void Pintar()
    {
        var P = K.P;
        var f = App.Lig.Faixas?.Campos.GetValueOrDefault("processador.ppt");
        var ativo = Padroes.ModoPpt(App.Lig.Config?.Processador.Ppt, f);
        bool lida = f is { Disponivel: true };
        foreach (var (id, b, tn, tl, td) in modos)
        {
            bool on = id == ativo;
            b.Fundo(on ? P.Selecao : P.Poco); b.FundoSobre = on ? P.Selecao : P.PocoEscuro;
            tn.Foreground = tl.Foreground = on ? P.TextoSelecao : P.Texto; td.Foreground = on ? P.SubSel : P.Apagado;
            tl.Text = id switch
            {
                "eco" => lida ? $"{Padroes.Eco(f!)} W" : "",
                "padrao" => f?.Padrao is double pd ? $"{pd:0} W" : "",
                _ => lida ? $"até {f!.Max:0} W" : "",
            };
            b.IsEnabled = lida;
        }
    }
}
