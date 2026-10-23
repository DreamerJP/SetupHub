using System.Text.Json.Serialization;

namespace SetupHub.Nucleo;

// Campo de hardware nulo é padrão de fábrica: o SetupHub não mexe nele.
public sealed class Config
{
    public string PerfilAtivo { get; set; } = "Equilibrado";
    public ConfigVentoinhas Ventoinhas { get; set; } = new();
    // Fica fora de ConfigVentoinhas para não ir junto na cópia que cada perfil guarda.
    public Dictionary<string, NivelVentoinhas> NiveisVentoinhas { get; set; } = new();
    public ConfigLuz Luz { get; set; } = new();
    public ConfigProcessador Processador { get; set; } = new();
    public ConfigPlacaDeVideo PlacaDeVideo { get; set; } = new();
    public ConfigJogos Jogos { get; set; } = new();
    public ConfigTelas Telas { get; set; } = new();
    public ConfigEnergia Energia { get; set; } = new();
    public ConfigWindows Windows { get; set; } = new();
    public List<Perfil> Perfis { get; set; } = new();
    public List<Alerta> Alertas { get; set; } = new();
    public ConfigAparencia Aparencia { get; set; } = new();
}

public sealed class Ponto
{
    public double T { get; set; }
    public double P { get; set; }
    public Ponto() { }
    public Ponto(double t, double p) { T = t; P = p; }
}

public sealed class ConfigVentoinhas
{
    public string Nivel { get; set; } = "equilibrado";
    public List<SaidaVentoinha> Saidas { get; set; } = new();
    public VentoinhaGpu Gpu { get; set; } = new();
    public List<Grupo> Grupos { get; set; } = new();
    public ModoNoite Noite { get; set; } = new();
}

public sealed class NivelVentoinhas
{
    public List<SaidaVentoinha> Saidas { get; set; } = new();
    public VentoinhaGpu Gpu { get; set; } = new();
}

public sealed class SaidaVentoinha
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    // bios: a curva gravada na BIOS manda; fixo: porcentagem fixa; curva: pontos temperatura → porcentagem.
    public string Modo { get; set; } = "bios";
    public int Fixo { get; set; } = 50;
    public List<string> Fontes { get; set; } = new() { "cpu" };
    // maior ou media, quando há mais de uma fonte.
    public string Combinar { get; set; } = "maior";
    public List<Ponto> Curva { get; set; } = new();
    public double Suavizacao { get; set; } = 3;
    public double Histerese { get; set; } = 2;
    public int? PararAbaixo { get; set; }
}

public sealed class VentoinhaGpu
{
    // auto: o driver decide; curva: 5 pontos gravados no driver.
    public string Modo { get; set; } = "auto";
    public List<Ponto> Curva { get; set; } = new();
    public bool? ParadaEmRepouso { get; set; }
    public int? RuidoMinimo { get; set; }
}

public sealed class Grupo
{
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "ventoinhas";
    public List<string> Itens { get; set; } = new();
}

public sealed class ModoNoite
{
    public bool Ligado { get; set; }
    public string Inicio { get; set; } = "23:00";
    public string Fim { get; set; } = "07:00";
    public int Maximo { get; set; } = 40;
}

public sealed class ConfigLuz
{
    public bool Ligada { get; set; } = true;
    public string Plano { get; set; } = "Temperatura";
    // Ligado: escolher um plano vale para todas as saídas, no mesmo compasso. Desligado: só para as selecionadas.
    public bool Sincronizar { get; set; } = true;
    public List<SaidaLuz> Saidas { get; set; } = new();
    public List<PlanoLuz> Planos { get; set; } = new();
    public bool NoiteQuente { get; set; }
    public string NoiteInicio { get; set; } = "22:00";
    public string NoiteFim { get; set; } = "07:00";
    public bool ApagarComTela { get; set; } = true;
    public bool AlertaPisca { get; set; } = true;
    public string CorAlerta { get; set; } = "#FF3B1F";
}

public sealed class SaidaLuz
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public int Leds { get; set; } = 30;
    public bool Ativa { get; set; } = true;
    // Plano próprio desta saída; nulo segue o plano geral. "personalizado" usa o efeito abaixo.
    public string? Plano { get; set; }
    public EfeitoLuz Efeito { get; set; } = new();
}

public sealed class EfeitoLuz
{
    public string Tipo { get; set; } = "fixo";
    public List<string> Cores { get; set; } = new() { "#FFFFFF" };
    public bool ArcoIris { get; set; }
    public int Brilho { get; set; } = 100;
    public int Velocidade { get; set; } = 1;
    public string Direcao { get; set; } = "fora";
    public string Fonte { get; set; } = "cpu";
    // Temperatura: até a 1ª vale a 1ª cor, entre elas a cor passa de uma para a outra, acima da 3ª vale a 3ª cor.
    public List<int> Temperaturas { get; set; } = new() { 50, 70, 90 };
}

public sealed class PlanoLuz
{
    public string Nome { get; set; } = "";
    public bool Pronto { get; set; }
    public EfeitoLuz Efeito { get; set; } = new();
    // Efeito próprio de uma saída dentro do plano; a que não aparece usa o efeito do plano.
    public Dictionary<string, EfeitoLuz> PorSaida { get; set; } = new();
}

public sealed class ConfigProcessador
{
    public bool? PboEscala { get; set; }
    public int? Ppt { get; set; }
    public int? Tdc { get; set; }
    public int? Edc { get; set; }
    public int? LimiteTemperatura { get; set; }
    public int? CoTodos { get; set; }
    public List<int?> CoNucleos { get; set; } = new();
    public bool? Eco { get; set; }
    public int? NucleosEstacionados { get; set; }
    public bool? Smt { get; set; }
    public bool? X3dJogo { get; set; }
    public int? FmaxDeslocamento { get; set; }
}

public sealed class ConfigPlacaDeVideo
{
    public int? ClockMin { get; set; }
    public int? ClockMax { get; set; }
    public int? Tensao { get; set; }
    public int? LimiteEnergia { get; set; }
    public int? ClockMemoria { get; set; }
    public bool? MemoriaRapida { get; set; }
}

public sealed class Ajustes3D
{
    public bool? AntiLag { get; set; }
    public bool? Boost { get; set; }
    public int? BoostResolucaoMin { get; set; }
    public bool? Chill { get; set; }
    public int? ChillMin { get; set; }
    public int? ChillMax { get; set; }
    public bool? Nitidez { get; set; }
    public int? NitidezNivel { get; set; }
    public bool? EnhancedSync { get; set; }
    public bool? Rsr { get; set; }
    public int? RsrNitidez { get; set; }
    public bool? Afmf { get; set; }
    // sempre-desligado, desligado-salvo-app, ligado-salvo-app, sempre-ligado.
    public string? Vsync { get; set; }
    public string? Suavizacao { get; set; }
    public string? SuavizacaoNivel { get; set; }
    public string? SuavizacaoMetodo { get; set; }
    public bool? Anisotropica { get; set; }
    public int? AnisotropicaNivel { get; set; }
    public string? Tesselacao { get; set; }
    public int? TesselacaoNivel { get; set; }
    public bool? Frtc { get; set; }
    public int? FrtcFps { get; set; }
}

public sealed class ConfigJogos
{
    public Ajustes3D Global { get; set; } = new();
    public int? LimiteQuadros { get; set; }
    public Sobreposicao Sobreposicao { get; set; } = new();
    public List<PerfilJogo> Lista { get; set; } = new();
}

public sealed class Sobreposicao
{
    public bool Ligada { get; set; }
    public List<string> Leituras { get; set; } = new() { "hora", "fps", "cpu.temp", "cpu.clock", "cpu.ventoinha", "gpu.temp", "gpu.clock", "gpu.ventoinha", "memoria.uso" };
    public string Posicao { get; set; } = "superior-esquerda";
    public int Tamanho { get; set; } = 100;
    public string Atalho { get; set; } = "Ctrl+Alt+O";
    // coluna: um grupo (hora, quadros, processador, placa de vídeo, memória) por linha; linha: todos numa linha só.
    public string Layout { get; set; } = "coluna";
    public string Cor { get; set; } = "#FF8000";
    public bool Fundo { get; set; } = true;
    public string Fonte { get; set; } = "Unispace";
}

public sealed class PerfilJogo
{
    public string Nome { get; set; } = "";
    public string Executavel { get; set; } = "";
    public string? Perfil { get; set; }
    // Nível das ventoinhas enquanto o jogo estiver aberto; nulo segue o perfil.
    public string? Ventoinhas { get; set; }
    // Plano de luz enquanto o jogo estiver aberto; nulo não muda.
    public string? Luz { get; set; }
    // Cor da luz no plano "Jogo" enquanto este jogo estiver aberto.
    public string? Cor { get; set; }
    public Ajustes3D? Graficos { get; set; }
    public int? LimiteQuadros { get; set; }
    public ConfigProcessador? Processador { get; set; }
}

public sealed class ConfigTelas
{
    public List<AjusteTela> Lista { get; set; } = new();
}

public sealed class AjusteTela
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public double? Hz { get; set; }
    public bool? FreeSync { get; set; }
    public bool? Vsr { get; set; }
    public bool? EscalaGpu { get; set; }
    // preservar, tela-cheia, centralizado.
    public string? ModoEscala { get; set; }
    public bool? EscalaInteira { get; set; }
    public int? ProfundidadeCor { get; set; }
    public string? FormatoPixel { get; set; }
    public bool? CorPersonalizada { get; set; }
    public int? Brilho { get; set; }
    public int? Contraste { get; set; }
    public int? Saturacao { get; set; }
    public int? Matiz { get; set; }
    public int? Temperatura { get; set; }
    public bool? VariBright { get; set; }
    public int? VariBrightNivel { get; set; }
    public bool? AtualizacaoDinamica { get; set; }
    public string? Gamut { get; set; }
    public double? Gama { get; set; }
}

public sealed class ConfigEnergia
{
    // Plano do SetupHub em uso (economia, equilibrado, desempenho, maximo) ou nulo para não mexer.
    public string? Plano { get; set; }
    public List<RegraEnergia> Regras { get; set; } = new();
    // Cada plano guarda o próprio ajuste: o que muda com um plano escolhido fica nele.
    public List<PlanoEnergia> Planos { get; set; } = new();
}

// Quando a condição acaba, volta ao plano anterior.
public sealed class RegraEnergia
{
    // programa, horario, ocioso.
    public string Tipo { get; set; } = "horario";
    public string? Programa { get; set; }
    public string Inicio { get; set; } = "23:00";
    public string Fim { get; set; } = "07:00";
    public int Minutos { get; set; } = 20;
    public string Plano { get; set; } = "";
    public bool Ligada { get; set; } = true;
}

public sealed class PlanoEnergia
{
    public string Id { get; set; } = "";
    public int ProcessadorMinimo { get; set; } = 5;
    public int ProcessadorMaximo { get; set; } = 100;
    public int NucleosMinimos { get; set; } = 100;
    // 0 é desempenho total, 100 é economia total.
    public int PreferenciaEnergia { get; set; } = 25;
    // desligado, ligado, agressivo, eficiente-ligado, eficiente-agressivo.
    public string Impulso { get; set; } = "agressivo";
    public bool SuspensaoUsb { get; set; }
    // desligado, moderado, maximo.
    public string EconomiaPcie { get; set; } = "desligado";
    public int DesligarTela { get; set; }
    public int Suspender { get; set; }
}

public sealed class ConfigWindows
{
    public bool? ModoJogo { get; set; }
    public bool? AgendamentoGpu { get; set; }
    public bool? OtimizacaoJanela { get; set; }
    public bool? VrrJanela { get; set; }
    public bool? GravacaoFundo { get; set; }
    public bool? PrecisaoPonteiro { get; set; }
    public bool? Transparencia { get; set; }
    public bool? NaoPerturbeEmJogo { get; set; }
}

public sealed class Perfil
{
    public string Nome { get; set; } = "";
    public bool Pronto { get; set; }
    public List<string> Areas { get; set; } = new();
    public ConfigVentoinhas? Ventoinhas { get; set; }
    public ConfigLuz? Luz { get; set; }
    public ConfigProcessador? Processador { get; set; }
    public ConfigPlacaDeVideo? PlacaDeVideo { get; set; }
    public Ajustes3D? Graficos { get; set; }
    public Sobreposicao? Sobreposicao { get; set; }
    public ConfigEnergia? Energia { get; set; }
    public string? Atalho { get; set; }
    public string? Horario { get; set; }
    public List<string> Programas { get; set; } = new();
}

public sealed class Alerta
{
    // temperatura, ventoinha-parada, queda-clock.
    public string Tipo { get; set; } = "temperatura";
    public string Sensor { get; set; } = "cpu.temp";
    public double Acima { get; set; } = 90;
    public bool Ligado { get; set; } = true;
}

public sealed class ConfigAparencia
{
    public string Tema { get; set; } = "escuro";
    public string CorProcessador { get; set; } = "#6FC3E8";
    public string CorPlacaDeVideo { get; set; } = "#B9A0FF";
    public string Densidade { get; set; } = "compacta";
    public int TamanhoTexto { get; set; } = 100;
    public bool AbrirComWindows { get; set; } = true;
    public bool TemperaturasNaBandeja { get; set; } = true;
    // Só vale com as temperaturas na bandeja: o programa abre pelo ícone delas.
    public bool EsconderIconeBandeja { get; set; }
    public List<CartaoInicio> Inicio { get; set; } = new();
    public Dictionary<string, string> ModoSecao { get; set; } = new();
    // "x,y" no quadro de 480 por 380 do desenho do fluxo de ar.
    public Dictionary<string, string> PosicaoFluxo { get; set; } = new();
}

public sealed class CartaoInicio
{
    public string Tipo { get; set; } = "";
    public int Largura { get; set; } = 1;
    public bool Visivel { get; set; } = true;
}
