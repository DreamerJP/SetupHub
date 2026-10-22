@echo off
rem Instala o SetupHub desta pasta. Pede administrador uma vez; a conta que abriu este arquivo fica autorizada no canal.
setlocal
cd /d "%~dp0"
for /f "tokens=2 delims=," %%s in ('whoami /user /fo csv /nh') do set "SID=%%~s"
if "%SID%"=="" (
  echo Nao foi possivel ler a conta do usuario.
  pause
  exit /b 2
)
if exist "%~dp0instalacao.txt" del "%~dp0instalacao.txt"
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $p = Start-Process -FilePath '%~dp0SetupHubServico.exe' -ArgumentList 'instalar','%SID%' -Verb RunAs -Wait -PassThru; exit $p.ExitCode } catch { Write-Host 'Instalacao cancelada.'; exit 1223 }"
set "CODIGO=%ERRORLEVEL%"
if exist "%~dp0instalacao.txt" type "%~dp0instalacao.txt"
if not "%CODIGO%"=="0" echo Terminou com codigo %CODIGO%.
if /i not "%~1"=="/sem-pausa" pause
exit /b %CODIGO%
