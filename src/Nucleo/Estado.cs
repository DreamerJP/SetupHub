namespace SetupHub.Nucleo;

// Leitura nula: sensor que não existe ou falhou.
public sealed class Estado
{
    public long Hora { get; set; }
    public LeituraCpu Cpu { get; set; } = new();
    public LeituraGpu Gpu { get; set; } = new();
    public LeituraPlaca Placa { get; set; } = new();
    public List<LeituraDisco> Discos { get; set; } = new();
    public LeituraMemoria Memoria { get; set; } = new();
    public List<LeituraVentoinha> Ventoinhas { get; set; } = new();
    public LeituraLuz Luz { get; set; } = new();
    public string Perfil { get; set; } = "";
    public bool PerfilAlterado { get; set; }
    public string? UltimaTroca { get; set; }
    public string? Jogo { get; set; }
    public double? Fps { get; set; }
    public List<AlertaAtivo> Alertas { get; set; } = new();
    public Tarefa? Tarefa { get; set; }
    public bool AgenteConectado { get; set; }
    public bool NoiteAtiva { get; set; }
    public string? PlanoEnergia { get; set; }
    public string? AvisoConfig { get; set; }
    public List<string> Avisos { get; set; } = new();
}

public sealed class LeituraCpu
{
    public string Nome { get; set; } = "";
    public double? Temp { get; set; }
    public double? TempCcd { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public double? TempMaisQuente => Temp is double t ? Math.Max(t, TempCcd ?? t) : TempCcd;
    public double? Consumo { get; set; }
    public double? Clock { get; set; }
    public double? ClockMax { get; set; }
    public double? Uso { get; set; }
    public double? Tensao { get; set; }
    public double? LimitePpt { get; set; }
    public List<LeituraNucleo> Nucleos { get; set; } = new();
}

public sealed class LeituraNucleo
{
    public double? Clock { get; set; }
    public double? ClockEfetivo { get; set; }
    public double? Uso { get; set; }
    public double? Tensao { get; set; }
    public double? Consumo { get; set; }
}

public sealed class LeituraGpu
{
    public string Nome { get; set; } = "";
    public double? Temp { get; set; }
    public double? TempQuente { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public double? TempMaisQuente => TempQuente ?? Temp;
    public double? TempMemoria { get; set; }
    public double? Consumo { get; set; }
    public double? ConsumoTotal { get; set; }
    public double? Tensao { get; set; }
    public double? Clock { get; set; }
    public double? ClockMemoria { get; set; }
    public double? Uso { get; set; }
    public double? VramUsada { get; set; }
    public double? VramTotal { get; set; }
    public double? FanRpm { get; set; }
    public double? FanPorcentagem { get; set; }
    public double? TempEntrada { get; set; }
}

public sealed class LeituraPlaca
{
    public double? Sistema { get; set; }
    public double? Vrm { get; set; }
    public double? Chipset { get; set; }
    public double? Soquete { get; set; }
}

public sealed class LeituraDisco
{
    public string Nome { get; set; } = "";
    public double? Temp { get; set; }
}

public sealed class LeituraMemoria
{
    public double? UsadaGb { get; set; }
    public double? TotalGb { get; set; }
    public double? Uso { get; set; }
}

public sealed class LeituraVentoinha
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public double? Rpm { get; set; }
    // Difere de Alvo enquanto o chip sobe 2% por segundo nas de gabinete.
    public double? Porcentagem { get; set; }
    public double? Alvo { get; set; }
    public string Modo { get; set; } = "bios";
    public bool Conectada { get; set; }
    public bool Defeito { get; set; }
    public int? RpmMax { get; set; }
}

public sealed class LeituraLuz
{
    public bool Ligada { get; set; }
    public string Plano { get; set; } = "";
    public bool Disponivel { get; set; }
    public string? Erro { get; set; }
}

public sealed class AlertaAtivo
{
    public string Tipo { get; set; } = "";
    public string Texto { get; set; } = "";
    public long Desde { get; set; }
}

public sealed class Tarefa
{
    public string Nome { get; set; } = "";
    public string Etapa { get; set; } = "";
    public double Progresso { get; set; }
    public bool Rodando { get; set; }
    public string? Resultado { get; set; }
    public bool Erro { get; set; }
}

// Campo sem faixa conhecida fica travado.
public sealed class Faixa
{
    public double Min { get; set; }
    public double Max { get; set; }
    public double Passo { get; set; } = 1;
    public double? Padrao { get; set; }
    public double? Atual { get; set; }
    public string Unidade { get; set; } = "";
    public bool Disponivel { get; set; } = true;
    public string? Motivo { get; set; }
}

public sealed class Faixas
{
    public Dictionary<string, Faixa> Campos { get; set; } = new();
    // processador, placaDeVideo, graficos, telas.
    public List<string> AreasLidas { get; set; } = new();
    public List<string> OpcoesProcessador { get; set; } = new();
    public List<InfoTela> Telas { get; set; } = new();
    public InfoGpu? Gpu { get; set; }
    public int Nucleos { get; set; }
    public bool RivaTuner { get; set; }
    public bool AdlxDisponivel { get; set; }
}

public sealed class InfoTela
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public int Largura { get; set; }
    public int Altura { get; set; }
    public double Hz { get; set; }
    public List<double> Taxas { get; set; } = new();
    public string Dispositivo { get; set; } = "";
    public List<string> Suporta { get; set; } = new();
    // O que o driver diz que esta tela aceita na ligação atual; vazio quando ele não respondeu.
    public List<int> Profundidades { get; set; } = new();
    public List<string> Formatos { get; set; } = new();
}

public sealed class InfoGpu
{
    public string Nome { get; set; } = "";
    public string Driver { get; set; } = "";
    public int VramMb { get; set; }
    public List<string> Suporta { get; set; } = new();
    public bool DeFabrica { get; set; }
}

// Fica fora do config.json: é da máquina, não preferência.
public sealed class EstadoMaquina
{
    public Dictionary<string, Calibracao> Calibracao { get; set; } = new();
    public Dictionary<string, string> CurvasBios { get; set; } = new();
    public Dictionary<string, string> CurvasGravadas { get; set; } = new();
    public int? ModoBios { get; set; }
    public Dictionary<string, int> ManualBios { get; set; } = new();
    public bool Assumido { get; set; }
    public ConfigProcessador? ProcessadorConfirmado { get; set; }
    public ConfigPlacaDeVideo? GpuConfirmada { get; set; }
    public ConfigProcessador? ProcessadorPendente { get; set; }
    public ConfigPlacaDeVideo? GpuPendente { get; set; }
    public long PendenteDesde { get; set; }
    public bool DesligouLimpo { get; set; } = true;
    public string? UsuarioSid { get; set; }
    public bool AbrirPainelNoProximoLogin { get; set; }
    public bool? ServicoLuzMsiEstavaAtivo { get; set; }
    public string? InicioServicoLuzMsi { get; set; }
    // Valores de partida só valem na mesma sessão do Windows (segundo Unix em que ligou).
    public long PartidaWindows { get; set; }
    public ConfigProcessador? ProcessadorPartida { get; set; }
    public List<int?> CoPartida { get; set; } = new();
    // Valor "início:rodando" de antes de o SetupHub parar o serviço, por exemplo "2:1".
    public Dictionary<string, string> ServicosLuzMsi { get; set; } = new();
    public List<ResultadoCo> Assistente { get; set; } = new();
    public List<Atualizacao> Atualizacoes { get; set; } = new();
    public long AtualizacoesEm { get; set; }
}

public sealed class Calibracao
{
    public int Inicio { get; set; }
    public int Para { get; set; }
    public int RpmMax { get; set; }
    public double Resposta { get; set; }
    public List<Ponto> Tabela { get; set; } = new();
    public long Data { get; set; }
}

public sealed class ResultadoCo
{
    public int Nucleo { get; set; }
    public int Valor { get; set; }
    public bool Estavel { get; set; }
    public long Data { get; set; }
}

public sealed class Atualizacao
{
    public string Tipo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Versao { get; set; } = "";
    public string Data { get; set; } = "";
    public string Endereco { get; set; } = "";
    public bool Nova { get; set; }
}

public sealed class OpcaoBios
{
    public string Nome { get; set; } = "";
    public string Valor { get; set; } = "";
    public string Padrao { get; set; } = "";
    public List<string> Opcoes { get; set; } = new();
    public bool Interfere { get; set; }
}

public sealed class InfoSistema
{
    public string Placa { get; set; } = "";
    public string Bios { get; set; } = "";
    public string BiosData { get; set; } = "";
    public string Processador { get; set; } = "";
    public string MemoriaPerfil { get; set; } = "";
    public string MemoriaModulos { get; set; } = "";
    public List<string> MemoriaTempos { get; set; } = new();
    public string Windows { get; set; } = "";
    public string DriverGpu { get; set; } = "";
    public List<OpcaoBios> OpcoesBios { get; set; } = new();
    public long BiosLidaEm { get; set; }
}
