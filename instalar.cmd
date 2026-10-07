@echo off
chcp 65001 >nul
rem Compila e instala o Pulso neste computador, na pasta do usuário (sem administrador).
rem Também serve para reinstalar por cima: as configurações ficam.
setlocal
cd /d "%~dp0"
set "DESTINO=%LOCALAPPDATA%\Programs\Pulso"

echo Fechando o Pulso, se estiver aberto...
if exist "%DESTINO%\Pulso.exe" "%DESTINO%\Pulso.exe" --sair
if exist "%~dp0bin\Pulso.exe" "%~dp0bin\Pulso.exe" --sair

echo Compilando...
call "%~dp0build.cmd"
if errorlevel 1 goto falhou

if not exist "%DESTINO%" mkdir "%DESTINO%"
set TENTATIVAS=0
:copiar
copy /y "%~dp0bin\Pulso.exe" "%DESTINO%\Pulso.exe" >nul 2>&1 && goto copiado
set /a TENTATIVAS+=1
if %TENTATIVAS% geq 10 goto falhou
timeout /t 1 /nobreak >nul
goto copiar

:copiado
"%DESTINO%\Pulso.exe" --registrar "%~dp0."
if errorlevel 1 goto falhou
start "" "%DESTINO%\Pulso.exe"
echo.
echo Pronto: o Pulso está instalado e aberto.
echo Ele aparece no Menu Iniciar e em Configurações ^> Aplicativos do Windows.
timeout /t 4 >nul
exit /b 0

:falhou
echo.
echo Não foi possível instalar o Pulso.
if exist "%DESTINO%\Pulso.exe" start "" "%DESTINO%\Pulso.exe"
pause
exit /b 1
