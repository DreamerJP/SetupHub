using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas.Secoes;

sealed class Aparencia : Tela
{
    protected override string Id => "aparencia";
    Segmentado tema = null!, densidade = null!;
    CorEscolha corCpu = null!, corGpu = null!;
    Deslizante texto = null!;
    TextBlock textoValor = null!;
    StackPanel quadros = null!;

    protected override FrameworkElement Montar()
    {
        Cabecalho("Aparência", () => "alterações aplicadas imediatamente");
        var linha = new Fluxo { EsticarAltura = false };
        linha.Children.Add(Fluxo.Item(MontarCores(), 360, 1));
        linha.Children.Add(Fluxo.Item(MontarQuadros(), 360, 1));
        var raiz = K.Col(12, linha);
        raiz.Margin = new Thickness(20, 12, 20, 20);
        return raiz;
    }

    Border MontarCores()
    {
        tema = new Segmentado(new[] { ("escuro", "Escuro"), ("claro", "Claro"), ("windows", "Do Windows") }, 30);
        tema.Escolheu += v => _ = Gravar(c => c.Aparencia.Tema = v);
        densidade = new Segmentado(new[] { ("compacta", "Compacto"), ("confortavel", "Confortável") }, 30);
        densidade.Escolheu += v => _ = Gravar(c => c.Aparencia.Densidade = v);
        corCpu = new CorEscolha("#6FC3E8", 30) { Width = 270 };
        corCpu.Escolheu += v => _ = Gravar(c => c.Aparencia.CorProcessador = v);
        corGpu = new CorEscolha("#B9A0FF", 30) { Width = 270 };
        corGpu.Escolheu += v => _ = Gravar(c => c.Aparencia.CorPlacaDeVideo = v);
        texto = new Deslizante(90, 130, 5, 100);
        textoValor = K.T("100%");
        textoValor.HorizontalAlignment = HorizontalAlignment.Right;
        texto.Movendo += v => textoValor.Text = $"{v:0}%";
        texto.Mudou += v => _ = Gravar(c => c.Aparencia.TamanhoTexto = (int)v);
        var titulo = K.Titulo("Cores e tamanho");
        titulo.Height = 34;
        titulo.Padding = new Thickness(0, 7, 0, 0);
        return K.Cartao(K.Col(0, titulo,
            K.LinhaAjuste("Tema", tema, 270, 46),
            K.LinhaAjuste("Espaçamento", densidade, 270, 46),
            K.LinhaAjuste("Cor do processador nos gráficos", corCpu, 270, 46),
            K.LinhaAjuste("Cor da placa de vídeo nos gráficos", corGpu, 270, 46),
            K.LinhaAjuste("Tamanho do texto", K.Grade("* 46", 10, texto, textoValor), 270, 46)), new Thickness(16, 12, 16, 10));
    }

    Border MontarQuadros()
    {
        var titulo = K.Titulo("Quadros da tela inicial");
        titulo.Margin = new Thickness(0, 0, 0, 4);
        quadros = new StackPanel();
        return K.Cartao(K.Col(4, titulo, quadros));
    }

    void PintarQuadros(Config c)
    {
        var nomes = Padroes.Quadros.ToDictionary(x => x.Tipo, x => x.Nome);
        var lista = c.Aparencia.Inicio;
        quadros.Children.Clear();
        for (int i = 0; i < lista.Count; i++)
        {
            var quadro = lista[i];
            int indice = i;
            var nome = K.T(nomes.GetValueOrDefault(quadro.Tipo, quadro.Tipo), 14, quadro.Visivel ? P.Texto : P.Tenue);
            var chave = new Chave(quadro.Visivel);
            chave.Mudou += ligado => _ = Mudar(x => x[indice].Visivel = ligado);
            Botao Seta(string desenho, int passo, string dica)
            {
                var botao = new Botao
                {
                    Height = 30,
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    ToolTip = dica,
                    Child = K.IconeCaixa(desenho, 12, P.Secundario, 1.6, 12),
                };
                botao.BordaNormal = P.BordaForte;
                botao.BorderBrush = P.BordaForte;
                botao.BordaSobre = P.SubSel;
                botao.FundoNormal = Brushes.Transparent;
                botao.Background = Brushes.Transparent;
                botao.FundoSobre = P.Poco;
                botao.IsEnabled = indice + passo >= 0 && indice + passo < lista.Count;
                botao.Clique += (_, _) => _ = Mudar(x => (x[indice], x[indice + passo]) = (x[indice + passo], x[indice]));
                return botao;
            }
            var grade = K.Grade("* 40 32 32", 8, nome, chave, Seta("M2 8l4-4 4 4", -1, "Subir"), Seta("M2 4l4 4 4-4", 1, "Descer"));
            grade.VerticalAlignment = VerticalAlignment.Center;
            quadros.Children.Add(new Border
            {
                MinHeight = P.E(44),
                BorderBrush = P.Linha,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = grade,
            });
        }
    }

    Task<bool> Mudar(Action<List<CartaoInicio>> mudar) => Gravar(c => mudar(c.Aparencia.Inicio));

    protected override void Config(Config c)
    {
        var aparencia = c.Aparencia;
        tema.Selecionar(aparencia.Tema);
        densidade.Selecionar(aparencia.Densidade);
        corCpu.Definir(aparencia.CorProcessador);
        corGpu.Definir(aparencia.CorPlacaDeVideo);
        texto.Valor = aparencia.TamanhoTexto;
        textoValor.Text = $"{aparencia.TamanhoTexto}%";
        PintarQuadros(c);
    }
}
