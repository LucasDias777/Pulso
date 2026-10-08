# Publica a versão atual nas Releases deste repositório (privado), de onde vem o botão "Baixar o Pulso" do README.
# Compila o commit atual (precisa estar no GitHub e sem mudanças pendentes) e anexa o bin\Pulso.exe a uma release
# com a tag "<versão>-<commit>". Baixar exige estar logado no GitHub com acesso ao repositório.
# Uso (o token fica só na variável de ambiente): $env:GH_TOKEN = "<token com acesso ao repositório>"; tools\publicar.ps1
param([string]$Repo = "LucasDias777/Pulso")
$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent
if (-not $env:GH_TOKEN) { throw "Defina GH_TOKEN com um token que possa publicar em $Repo." }
if (git -C $raiz status --porcelain) { throw "Há mudanças sem commit: publique só o que já está no GitHub." }
$h = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = "application/vnd.github+json"; "User-Agent" = "Pulso-publicar" }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$sha = (git -C $raiz rev-parse HEAD).Trim()
try { Invoke-RestMethod "https://api.github.com/repos/$Repo/commits/$sha" -Headers $h | Out-Null }
catch { throw "O commit atual ainda não foi enviado ao GitHub (git push)." }

& (Join-Path $raiz "build.cmd")
if ($LASTEXITCODE -ne 0) { throw "Falha na compilação." }
$commit = (git -C $raiz rev-parse --short HEAD).Trim()
$versao = [regex]::Match((Get-Content (Join-Path $raiz "src\Core\Instalacao.cs") -Raw), 'Versao = "([^"]+)"').Groups[1].Value
$tag = "$versao-$commit"

try
{
    Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/tags/$tag" -Headers $h | Out-Null
    "Já publicada: $tag"
    return
}
catch { if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw } }

$notas = (git -C $raiz log -1 --format=%B | Out-String).Trim()
$corpo = @{ tag_name = $tag; name = "Pulso $versao ($commit)"; body = $notas; target_commitish = $sha } | ConvertTo-Json
$release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases" -Method Post -Headers $h -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($corpo))
Invoke-RestMethod "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=Pulso.exe" -Method Post -Headers $h -ContentType "application/vnd.microsoft.portable-executable" -InFile (Join-Path $raiz "bin\Pulso.exe") | Out-Null
"Publicada: $tag"
"Download: https://github.com/$Repo/releases/latest/download/Pulso.exe"
