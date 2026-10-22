using System.Windows;
using System.Windows.Controls;
using SetupHub.Janela.Kit;

namespace SetupHub.Janela.Telas;

sealed class TelaComErro : Tela
{
    readonly string id, motivo;
    public TelaComErro(string id, string motivo) { this.id = id; this.motivo = motivo; }
    protected override string Id => id;
    protected override FrameworkElement Montar() => new Border
    {
        Padding = new Thickness(24),
        Child = K.Col(8, K.Titulo("Esta tela não abriu"), K.Nota(motivo), K.Nota("Detalhes registrados no arquivo erros.txt, na pasta do SetupHub do usuário.")),
    };
}
