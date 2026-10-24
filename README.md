# SetupHub

Aplicação para Windows x64 que reúne controle de ventoinhas, iluminação, processador, placa de vídeo, telas, energia e perfis.

## Instalador

- Requer permissão de administrador.
- `--automatico`: instalação ou atualização sem interação; encerra ao concluir e abre o SetupHub.

## Estrutura

- `src/`: código-fonte dos aplicativos, serviço e instalador.
- `kits/`: componentes usados na montagem do instalador.
- `publicar.sh`: script de publicação; roda os testes de lógica antes e não publica com teste falhando. Sem a pasta de testes, publica com aviso; sem o .NET do projeto em `_temporario-SetupHub/ferramentas`, para e diz o que falta.

## Requisitos

- Windows x64.
- .NET SDK 10.
- Git Bash ou outro terminal Bash.