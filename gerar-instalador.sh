# Gera na pasta do código só o instalador de um arquivo; o resto da compilação fica em _temporario-SetupHub/compilacao.
# Uso: bash gerar-instalador.sh
set -e
RAIZ=$(cd "$(dirname "$0")" && pwd)
FERRAMENTAS="$RAIZ/_temporario-SetupHub/ferramentas"
if [ ! -f "$FERRAMENTAS/ambiente.sh" ] || [ ! -f "$FERRAMENTAS/dotnet10/dotnet.exe" ]; then
  FW=$(cygpath -w "$FERRAMENTAS")
  echo "ERRO: .NET do projeto não encontrado; nenhum instalador foi gerado."
  [ -f "$FERRAMENTAS/dotnet10/dotnet.exe" ] || echo "  Falta: $FW\\dotnet10\\dotnet.exe (.NET SDK 10 x64 extraído nessa pasta; usado até agora: 10.0.402)."
  [ -f "$FERRAMENTAS/ambiente.sh" ] || echo "  Falta: $FW\\ambiente.sh (define DOTNET_ROOT=dotnet10, DOTNET_CLI_HOME=dotnet10-casa e NUGET_PACKAGES=nuget dentro dessa pasta e põe dotnet10 no PATH)."
  exit 1
fi
source "$FERRAMENTAS/ambiente.sh"
FALTA=""
for f in kits/scewin/SCEWIN_64.exe kits/scewin/amifldrv64.sys kits/scewin/amigendrv64.sys kits/AMDRyzenMasterSDK.msi kits/PawnIO_setup.exe kits/rtss/RTSS.exe kits/prime95/prime95.exe; do
  [ -f "$RAIZ/$f" ] || FALTA="$FALTA $f"
done
if [ -n "$FALTA" ]; then
  for f in $FALTA; do echo "  Falta: $(cygpath -w "$RAIZ/$f")"; done
  echo "ERRO: ferramenta ausente na pasta kits; nenhum instalador foi gerado."
  exit 1
fi
COMPILACAO="$RAIZ/_temporario-SetupHub/compilacao"
SAIDA="$COMPILACAO/pacote"
SAIDAW=$(cygpath -w "$SAIDA")
cd "$RAIZ/src"
# Teste que falha impede a geração, antes de a pasta do pacote anterior ser apagada; a aceitação mexe no PC e roda à parte.
TESTES="$RAIZ/_temporario-SetupHub/testes/SetupHub.Testes.csproj"
if [ -f "$TESTES" ]; then
  echo "Testando..."
  if ! dotnet test "$TESTES" -c Release -nologo -v q --filter "Tipo!=Aceitacao&Tipo!=AceitacaoAdmin" > /tmp/gerar-instalador_testes.txt 2>&1; then
    grep -E "\[FAIL\]| error " /tmp/gerar-instalador_testes.txt | sed 's/\[C:.*//' | sort -u
    echo "ERRO: testes falharam; nenhum instalador foi gerado."
    exit 1
  fi
else
  echo "AVISO: testes não encontrados em $(cygpath -w "$TESTES"); instalador gerado sem testes."
fi
# Pasta vazia a cada geração: arquivo que saiu do projeto não fica no instalador.
rm -rf "$SAIDA"
mkdir -p "$SAIDA"
for p in Servico Comando Janela; do
  echo "Compilando $p..."
  # Falha de compilação para tudo: senão a versão anterior entraria no instalador sem aviso.
  if ! dotnet publish $p/*.csproj -c Release -nologo -v q "-p:PublishDir=$SAIDAW\\" > /tmp/gerar-instalador_$p.txt 2>&1; then
    grep -E "error" /tmp/gerar-instalador_$p.txt | sed 's/\[C:.*//' | sort -u
    echo "ERRO: $p não compilou; nenhum instalador foi gerado."
    exit 1
  fi
done
for exe in SetupHubServico.exe setuphub.exe SetupHubPainel.exe; do
  [ -f "$SAIDA/$exe" ] || { echo "ERRO: $exe não foi gerado."; exit 1; }
done
mkdir -p "$SAIDA/kits"
cp -rp "$RAIZ/kits/." "$SAIDA/kits/"

# O instalador de um arquivo só leva a pasta acima dentro dele; a versão do nome vem do Directory.Build.props.
VERSAO=$(sed -n 's:.*<Version>\([0-9]*\.[0-9]*\)\..*</Version>.*:\1:p' "$RAIZ/Directory.Build.props")
INST="$COMPILACAO/Instalador/publicado"
echo "Montando o instalador $VERSAO..."
if ! dotnet publish Instalador/*.csproj -c Release -nologo -v q "-p:PublishDir=$(cygpath -w "$INST")\\" "-p:PastaPacote=$SAIDAW" > /tmp/gerar-instalador_Instalador.txt 2>&1; then
  grep -E "error" /tmp/gerar-instalador_Instalador.txt | sed 's/\[C:.*//' | sort -u
  echo "ERRO: o instalador não compilou."
  exit 1
fi
rm -f "$RAIZ/"SetupHub-*-Instalador.exe
cp "$INST/SetupHubInstalador.exe" "$RAIZ/SetupHub-$VERSAO-Instalador.exe"
rm -rf "$INST"
echo "Instalador: $(cygpath -w "$RAIZ/SetupHub-$VERSAO-Instalador.exe") ($(du -h "$RAIZ/SetupHub-$VERSAO-Instalador.exe" | cut -f1))"
