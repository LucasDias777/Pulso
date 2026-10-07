@echo off
rem Compila o Pulso com o csc.exe do .NET Framework 4.8, que ja vem no Windows.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (echo csc.exe nao encontrado & exit /b 1)
if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:bin\Pulso.exe /win32manifest:app.manifest /win32icon:assets\pulso.ico /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.Security.dll /r:System.Management.dll /recurse:src\*.cs
exit /b %ERRORLEVEL%
