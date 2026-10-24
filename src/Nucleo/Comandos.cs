namespace SetupHub.Nucleo;

public static class Comandos
{
    static ComandoInfo C(string grupo, string uso, string descricao = "") => new()
    {
        Grupo = grupo, Uso = uso, Descricao = descricao,
        Nome = string.Join(' ', uso.Split(' ').TakeWhile(p => p.Length > 0 && char.IsLetter(p[0]))),
    };

    public static readonly List<ComandoInfo> Lista = new()
    {
        C("Leitura", "ler <área>", "leituras e ajustes de uma área"),
        C("Leitura", "ler config [caminho]", "configuração inteira ou um campo"),
        C("Leitura", "ler faixas [área]", "faixa, padrão e atual de cada campo"),
        C("Leitura", "ler sistema", "placa, BIOS, memória e Windows"),
        C("Leitura", "ler bios [busca]", "opções da BIOS, só leitura"),
        C("Leitura", "ler bios --diferentes", "só as diferentes do padrão"),
        C("Leitura", "ler historico [sensor] [horas]", "padrão: cpu.temp, 1 hora"),
        C("Leitura", "ler registro [linhas]", "registro de eventos"),
        C("Leitura", "ler atualizacoes", "BIOS e drivers da última busca"),
        C("Leitura", "listar <lista>", "listas de referência"),
        C("Configuração", "definir <caminho> <valor>", "\"padrao\" volta ao de fábrica"),
        C("Configuração", "importar <arquivo.json>", "substitui a configuração inteira"),
        C("Configuração", "exportar tudo <arquivo.json>", "configuração inteira"),
        C("Configuração", "exportar historico <arq.csv> [h]", "padrão: 24 horas"),
        C("Ventoinhas", "ventoinha <nomes> <valor>", "0-100, bios ou curva"),
        C("Ventoinhas", "ventoinhas <nível> [nomes]", "sem nomes: todas"),
        C("Ventoinhas", "calibrar [nomes]", "sem nomes: todas"),
        C("Luz", "luz plano <nome>", "aplica um plano"),
        C("Luz", "luz ligar | desligar"),
        C("Luz", "luz efeito <tipo> [opções]", "efeito nas saídas"),
        C("Luz", "luz salvar <nome>", "plano a partir das saídas atuais"),
        C("Luz", "luz excluir <nome>", "só planos personalizados"),
        C("Luz", "luz restaurar <nome>", "plano pronto de volta ao padrão"),
        C("Perfis", "perfil <nome>", "aplica"),
        C("Perfis", "perfil salvar <nome>", "guarda os ajustes atuais"),
        C("Perfis", "perfil excluir | duplicar | restaurar <nome>"),
        C("Perfis", "perfil renomear <nome> <novo>"),
        C("Processador e placa de vídeo", "processador padrao", "valores de fábrica"),
        C("Processador e placa de vídeo", "processador assistente [opções]", "Curve Optimizer por núcleo"),
        C("Processador e placa de vídeo", "placa padrao", "valores de fábrica"),
        C("Processador e placa de vídeo", "placa assistente", "tensão da placa de vídeo"),
        C("Processador e placa de vídeo", "limpar cache", "cache de sombreadores da AMD"),
        C("Serviço", "cancelar", "calibração ou assistente"),
        C("Serviço", "sair", "ventoinhas na BIOS, luz no controlador"),
    };

    public static readonly (string nome, IReadOnlyList<string> valores)[] Valores =
    {
        ("<área>", new[] { "sensores", "ventoinhas", "luz", "processador", "placa", "telas", "perfis", "jogos", "energia", "alertas", "tarefa" }),
        ("<lista>", new[] { "comandos", "campos", "perfis", "planos", "sensores", "ventoinhas", "saidas" }),
        ("<nível>", Padroes.Niveis),
        ("<tipo> de luz efeito", Validacao.EfeitosLuz),
        ("[opções] de luz efeito", new[] { "#cor (até 4)", "--saidas a,b", "--brilho 0-100", "--velocidade 0-2", "--temperaturas 50,70,90" }),
        ("[opções] de processador assistente", new[] { "núcleos", "--piso -30", "--segundos 120" }),
    };
}
