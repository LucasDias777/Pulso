@echo off
chcp 65001 >nul
rem Baixa a versão nova do GitHub e reinstala o Pulso (as configurações ficam).
setlocal
cd /d "%~dp0"
echo Baixando a versão nova do GitHub...
git pull --ff-only
if errorlevel 1 (
  echo.
  echo Não foi possível baixar a versão nova.
  pause
  exit /b 1
)
call "%~dp0instalar.cmd"
