@echo off
rem Remove o SetupHub: ventoinhas voltam para a BIOS, luz fica no efeito do controlador e o servico de luz da MSI volta como estava.
setlocal
cd /d "%~dp0"
if exist "%TEMP%\setuphub_remocao.txt" del "%TEMP%\setuphub_remocao.txt"
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $p = Start-Process -FilePath '%~dp0SetupHubServico.exe' -ArgumentList 'remover' -Verb RunAs -Wait -PassThru; exit $p.ExitCode } catch { Write-Host 'Remocao cancelada.'; exit 1223 }"
set "CODIGO=%ERRORLEVEL%"
if exist "%TEMP%\setuphub_remocao.txt" type "%TEMP%\setuphub_remocao.txt"
if not "%CODIGO%"=="0" echo Terminou com codigo %CODIGO%.
if /i not "%~1"=="/sem-pausa" pause
exit /b %CODIGO%
