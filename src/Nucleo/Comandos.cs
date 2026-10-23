namespace SetupHub.Nucleo;

public static class Comandos
{
    static ComandoInfo C(string nome, string uso, string descricao) => new() { Nome = nome, Uso = uso, Descricao = descricao };

    public static readonly List<ComandoInfo> Lista = new()
    {
        C("ler", "setuphub ler sensores|ventoinhas|luz|processador|placa|telas|perfis|jogos|energia|alertas|tarefa", "Mostra as leituras e ajustes de uma área."),
        C("ler config", "setuphub ler config [caminho]", "Mostra a configuração inteira ou um campo, por exemplo: setuphub ler config ventoinhas.saidas[sys1]"),
        C("ler faixas", "setuphub ler faixas [área]", "Faixa, padrão e valor atual de cada campo ajustável, lidos do hardware."),
        C("ler sistema", "setuphub ler sistema", "Placa, BIOS, memória, Windows e driver."),
        C("ler bios", "setuphub ler bios [busca] [--diferentes]", "Opções da BIOS (só leitura), com busca e filtro das diferentes do padrão."),
        C("ler historico", "setuphub ler historico <sensor> [horas]", "Valores de um sensor nas últimas horas (padrão 1)."),
        C("ler registro", "setuphub ler registro [linhas]", "Últimas linhas do registro de eventos do SetupHub."),
        C("ler atualizacoes", "setuphub ler atualizacoes", "BIOS e drivers novos para esta placa, da última busca."),
        C("definir", "setuphub definir <caminho> <valor>", "Muda qualquer campo da configuração, conferido pelas travas. Valor \"padrao\" volta ao de fábrica."),
        C("ventoinha", "setuphub ventoinha <nome|grupo>... <0-100|bios|curva>", "Valor fixo ou modo de uma ou várias ventoinhas, por exemplo: setuphub ventoinha \"Fan 1\" \"Fan 2\" 60"),
        C("ventoinhas", "setuphub ventoinhas silencioso|equilibrado|forte|maximo [nomes...]", "Nível rápido para todas ou para as escolhidas."),
        C("calibrar", "setuphub calibrar [nomes...]", "Calibra as ventoinhas (todas, se nenhuma for dita). Leva alguns minutos."),
        C("luz", "setuphub luz plano <nome> | ligar | desligar | efeito <tipo> [#cor...] [--saidas a,b] [--brilho n] [--velocidade 0-2] [--temperaturas 50,70,90]", "Plano de luz, liga e desliga, efeito por saída. Efeitos: desligado, fixo, respiracao, onda, chama, temperatura, anel, musica, meteoro, carga, jogo, horario, alerta."),
        C("luz salvar", "setuphub luz salvar <nome>", "Guarda o que as saídas mostram agora como um plano de luz."),
        C("luz excluir", "setuphub luz excluir <nome> | restaurar <nome>", "Exclui um plano criado por você ou volta um plano pronto ao padrão."),
        C("aplicar perfil", "setuphub aplicar perfil <nome>", "Aplica um perfil e confere cada área por leitura."),
        C("perfil", "setuphub perfil salvar|excluir|duplicar|restaurar <nome> | renomear <nome> <novo>", "Gerencia perfis; restaurar volta um perfil pronto aos valores de fábrica."),
        C("processador", "setuphub processador padrao | assistente [núcleos] [--piso -30] [--segundos 120]", "Volta ao padrão ou roda o assistente de Curve Optimizer."),
        C("placa", "setuphub placa padrao | assistente", "Volta a placa de vídeo aos ajustes de fábrica ou roda o assistente de tensão."),
        C("limpar cache", "setuphub limpar cache", "Limpa o cache de sombreadores da AMD."),
        C("exportar", "setuphub exportar tudo <arquivo.json> | historico <arquivo.csv> [horas]", "Exporta a configuração ou o histórico."),
        C("importar", "setuphub importar <arquivo.json>", "Importa uma configuração inteira, conferida pelas travas antes de aplicar."),
        C("cancelar", "setuphub cancelar", "Cancela a calibração ou o assistente em andamento."),
        C("listar", "setuphub listar comandos|campos|perfis|planos|sensores|ventoinhas|saidas", "Listas de referência."),
        C("sair", "setuphub sair", "Encerra tudo: ventoinhas voltam à BIOS e a luz fica no efeito do controlador."),
        C("--json", "setuphub ler sensores --json", "Qualquer leitura em JSON; o código de saída diz se deu certo (0 certo, 1 recusado, 2 sem conexão, 3 aceito mas alguma área não conferiu)."),
    };
}
