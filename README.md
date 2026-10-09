<div align="center">

<img src="docs/imagens/logo.png" alt="Pulso" width="140" />

<h1>Pulso</h1>

<p>
  <strong>O consumo do Claude Code e do Codex ao vivo, numa cápsula na borda da tela do Windows.</strong>
</p>

<p>
  App nativo para Windows que mostra, em anéis sempre à vista, quanto já foi usado de cada limite (sessão de 5 horas, semana, semana por modelo) e quando ele renova. A leitura do Codex chega no instante em que a resposta termina, direto dos arquivos de sessão; a do Claude combina a leitura exata do servidor com uma estimativa local que se move a cada resposta. Leve, sem navegador embutido e sem gravar nenhuma credencial.
</p>

<p>
  Uma versão melhorada do <a href="https://github.com/vinzdg/codenotch">Codenotch</a>, refeita como app nativo do Windows. <a href="#créditos">Créditos e licença</a>.
</p>

<br/>

<p>
  <a href="https://learn.microsoft.com/dotnet/csharp/"><img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/csharp/csharp-original.svg" height="50" alt="c# logo" /></a>
  <img width="12" />
  <a href="https://dotnet.microsoft.com/download/dotnet-framework/net48"><img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/dot-net/dot-net-original.svg" height="50" alt=".net logo" /></a>
  <img width="12" />
  <a href="https://learn.microsoft.com/windows/win32/"><img src="https://cdn.jsdelivr.net/gh/devicons/devicon/icons/windows11/windows11-original.svg" height="50" alt="windows logo" /></a>
</p>

<br/>

<p>
  <img alt="Windows 10 e 11" src="https://img.shields.io/badge/Windows-10_|_11-0078D4?style=for-the-badge" />
  <img alt=".NET Framework 4.8" src="https://img.shields.io/badge/.NET_Framework-4.8-512BD4?style=for-the-badge" />
  <img alt="Versão 1.3" src="https://img.shields.io/badge/Vers%C3%A3o-1.3-00c46a?style=for-the-badge" />
</p>

<br/>

<a href="https://github.com/LucasDias777/Pulso/releases/latest/download/Pulso.exe"><img alt="Instalar Pulso" src="https://img.shields.io/badge/INSTALAR_PULSO-0078D4?style=for-the-badge&logo=windows&logoColor=white" width="280" /></a>

<br/><br/>

<table>
  <tr>
    <td align="center"><img src="docs/imagens/cartao-claude.png" alt="Cartão do Claude ao passar o cursor" width="340" /></td>
    <td align="center"><img src="docs/imagens/cartao-codex.png" alt="Cartão do Codex ao passar o cursor" width="340" /></td>
  </tr>
  <tr>
    <td align="center"><sub>Cursor sobre o anel do Claude</sub></td>
    <td align="center"><sub>Cursor sobre o anel do Codex</sub></td>
  </tr>
</table>

</div>

---

## Sumário

- [O que o Pulso faz](#o-que-o-pulso-faz)
- [Provedores suportados](#provedores-suportados)
- [Como a leitura funciona](#como-a-leitura-funciona)
  - [Com que frequência cada leitura acontece](#com-que-frequência-cada-leitura-acontece)
- [Tecnologias](#tecnologias)
- [Privacidade e segurança](#privacidade-e-segurança)
- [Leveza](#leveza)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Instalação](#instalação)
- [Atualização](#atualização)
- [Desinstalação](#desinstalação)
- [Como usar](#como-usar)
- [Configurações](#configurações)
- [Créditos](#créditos)

## O que o Pulso faz

| Recurso | Como funciona |
|---|---|
| **Anéis ao vivo** | Um anel por provedor, encostado na borda da tela, com o percentual do limite principal. A cor passa de verde para amarelo e vermelho nos limites que você escolher. Um `~` antes do número indica que ele já inclui a estimativa local |
| **Cartão ao passar o cursor** | Cada limite com barra, percentual usado, horário de renovação, folga ou excesso em relação ao ritmo, velocidade e projeção de esgotar antes de renovar |
| **Projetos e sessões** | Quanto da sessão de 5 horas veio de cada pasta de projeto e quais sessões estão trabalhando ou esperando você; clicar numa sessão traz a janela dela para a frente |
| **Indicador de atividade** | Dentro do anel: arco girando enquanto o agente trabalha, círculo pulsando quando ele para e espera a sua resposta |
| **Relatórios de consumo** | Tokens e custo equivalente do dia, da semana ou do mês, com a fatia de cada provedor, modelo e projeto e um gráfico por dia. O histórico fica guardado por 13 meses, mesmo depois de o Claude Code apagar as sessões antigas, e sai em CSV ou HTML |
| **Avisos** | Cartão ao lado do anel, com som, quando um limite renova, chega a 80%, 95% e 100%, quando o ritmo vai esgotar a sessão antes de renovar e quando uma sessão termina ou espera você |
| **Exibição** | Sempre aberta, recolhida numa cápsula pequena que abre ao passar o cursor, ou oculta, só com o ícone da bandeja. Em qualquer borda e monitor, e some sozinha em tela cheia |
| **Atalho global** | `Win + Y` mostra e oculta a cápsula de qualquer lugar; dá para gravar outra combinação |
| **Três idiomas** | Português, inglês e espanhol; a troca vale na hora |
| **Instala como um app** | Atalho no Menu Iniciar e na área de trabalho (à escolha na instalação), Aplicativos do Windows com Desinstalar, início com o Windows e atualização pelo próprio app |

<div align="center">
  <img src="docs/imagens/notch.png" alt="Cápsula na borda direita" width="150" />
  <br/>
  <sub>A cápsula em repouso: um anel por provedor, com os arcos nas duas orelhas</sub>
</div>

## Provedores suportados

Claude e Codex vêm ligados. Os outros aparecem na aba **Contas** e só ganham anel quando você os ativa; um provedor que não está instalado no computador não ocupa espaço na cápsula.

| Provedor | De onde vem o número |
|---|---|
| **Claude Code** | Leitura exata do servidor da Anthropic (`/api/oauth/usage`) com o login que o Claude Code já mantém, mais a estimativa ao vivo calculada pelos tokens de cada resposta gravada em `~/.claude/projects` |
| **Codex** | Os arquivos de sessão do próprio Codex (`~/.codex/sessions`), que trazem o consumo exato a cada resposta. O servidor do ChatGPT só é consultado quando o Codex fica parado |
| **GitHub Copilot** | API do GitHub com o login do `gh` (ou `GH_TOKEN`): pedidos premium do mês |
| **Cursor** | Banco local do Cursor (SQLite) para o login e a API de uso do Cursor |
| **Grok** | Login salvo em `~/.grok` e a API de cobrança do Grok |
| **z.ai (GLM)** | Chave da API e o endpoint de cota da z.ai |
| **OpenCode** | Login do OpenCode e a API de uso do opencode.ai |
| **Antigravity** | Servidor local do Antigravity quando ele está aberto, ou o login guardado no Gerenciador de Credenciais do Windows; sem cota publicada, mostra a contagem de pedidos do dia |

## Como a leitura funciona

```mermaid
flowchart LR
    subgraph PC["Seu computador"]
        subgraph Locais["Arquivos que as próprias ferramentas gravam"]
            Rollout["~/.codex/sessions<br/>consumo exato do Codex a cada resposta"]
            Transcritos["~/.claude/projects<br/>tokens de cada resposta do Claude"]
            Sessoes["~/.claude/sessions<br/>sessões trabalhando ou esperando"]
            Credenciais["Logins já salvos pelas ferramentas<br/>lidos na hora, nunca gravados"]
        end
        Pulso["Pulso.exe<br/>cápsula, bandeja e avisos"]
        Dados["%APPDATA%/Pulso<br/>config, estado, índice de custo,<br/>livro de consumo e log"]
    end

    subgraph Servidores["Servidores oficiais"]
        Anthropic["Anthropic<br/>uso exato do Claude"]
        OpenAI["ChatGPT<br/>uso do Codex (reserva)"]
        Outros["Copilot, Cursor, Grok,<br/>z.ai, OpenCode, Antigravity"]
    end

    Rollout -->|na hora| Pulso
    Transcritos -->|estimativa ao vivo| Pulso
    Sessoes -->|atividade| Pulso
    Credenciais --> Pulso
    Pulso -->|a cada 5 a 15 min| Anthropic
    Pulso -->|só com o Codex parado| OpenAI
    Pulso -->|a cada 5 min, se ativado| Outros
    Pulso <--> Dados
```

**O livro de consumo.** A mesma leitura das sessões do Claude Code e do Codex alimenta um livro com os tokens de cada dia por modelo e projeto (entrada, cache lido e gravado, saída e raciocínio) e, no Claude, o custo equivalente de API. Ele fica em `%APPDATA%\Pulso\consumo`, um arquivo por provedor e mês, por 13 meses; cada resposta entra uma vez só, mesmo fechando e reabrindo o Pulso. Na primeira abertura ele recupera o histórico que ainda está no disco. É dele que saem os relatórios, e ele conta só o uso deste computador.

**A estimativa do Claude.** O servidor da Anthropic aceita poucas consultas (responde `429` se consultado demais). Para o anel não ficar parado entre uma leitura e outra, o Pulso mantém um índice do custo de cada minuto de uso, montado a partir dos tokens gravados nas sessões. Entre duas leituras exatas ele calcula quanto cada dólar gasto neste computador representa da janela (`pontos que a janela subiu ÷ custo gasto aqui entre as leituras`) e, a partir daí, soma o custo das respostas novas. Assim, o uso da mesma conta em outro computador entra no número exato e não infla a estimativa. O número estimado aparece com `~` e é corrigido a cada leitura exata.

### Com que frequência cada leitura acontece

| Fonte | Quando |
|---|---|
| Codex (arquivos de sessão) | Na hora: 0,1 a 0,2 s depois de a resposta ser gravada |
| Codex (servidor) | Só quando o Codex fica mais de 5 minutos parado |
| Claude, leitura exata | A cada 5 min em uso e 15 min parado, nunca menos de 2 min entre consultas; clicar no anel pede uma leitura nova |
| Claude, estimativa e atividade | A cada resposta gravada, e na hora em que uma sessão muda de estado (sem hooks) |
| Outros provedores | A cada 5 min, só se o provedor estiver ativado |

## Tecnologias

O Pulso não usa nenhum pacote externo: tudo vem do próprio Windows e do .NET Framework.

| Tecnologia | Função no projeto |
|---|---|
| **C# 5 e .NET Framework 4.8** | Linguagem e runtime, já presentes no Windows 10 1903+ e no 11; compilado pelo `csc.exe` do próprio .NET |
| **Windows Forms e GDI+** | Janelas, bandeja, menus e o desenho da cápsula, dos anéis e dos cartões |
| **DirectWrite** | Todo o texto, nítido em qualquer tamanho e monitor e igual no Windows 10 e no 11 |
| **Win32** | Janela transparente por pixel, atalho global, detecção de tela cheia e DPI por monitor |
| **winsqlite3.dll e WMI** | SQLite nativo do Windows (Cursor e OpenCode) e localização do servidor do Antigravity |

## Privacidade e segurança

| Ponto | Como o Pulso trata |
|---|---|
| **Credenciais** | Lê só os logins que cada ferramenta já mantém no seu usuário, em memória e na hora da consulta. Nada é gravado, copiado ou enviado para outro lugar além do servidor oficial de cada uma |
| **Token vencido** | Um token do Claude vencido nunca é enviado; o Pulso espera o Claude Code renová-lo |
| **O que fica salvo** | Só `%APPDATA%\Pulso`: suas escolhas, as últimas leituras, o uso por minuto e por projeto, o livro de consumo (tokens por dia, modelo e nome da pasta do projeto, nunca o conteúdo das conversas) e o log. Nenhuma credencial |
| **Atualização** | Consulta as Releases públicas deste repositório, sem login; o Pulso não guarda senha nem token do GitHub. O exe baixado só substitui o instalado se o tamanho e o SHA-256 conferirem com os publicados |
| **Barra de status (opcional)** | Ligada só nas Configurações: grava uma linha no `~/.claude/settings.json`, com backup, e desligar remove a linha. Fora isso, nada é instalado no Claude Code |
| **Cápsula adaptável** | Lê o brilho médio de uma faixa fina da tela ao lado da cápsula; nada da imagem é guardado |

## Leveza

Medido com o Pulso aberto, o Claude trabalhando e o indicador de atividade animando:

| | Pulso |
|---|---|
| Memória em uso | ~68 MB (44 MB próprios) |
| CPU | ~0,2% |
| Programa no disco | 296 KB |
| Dados salvos | ~0,1 MB |

A cápsula só redesenha quando algo muda: 60 quadros por segundo durante a animação de abrir, 10 por segundo com um agente trabalhando e nenhum quando está tudo parado.

## Estrutura do projeto

```bash
Pulso/
├── src/
│   ├── App.cs          # Ponto de entrada: instância única, liga leituras, cápsula, bandeja e avisos
│   ├── Core/           # Configurações, modelo, ritmo, avisos, idiomas, instalação, atualização e integrações
│   ├── Sources/        # De onde vêm os números: Claude, Codex e os outros provedores
│   └── Ui/             # O que aparece na tela: cápsula, cartões, Configurações, bandeja e texto
├── assets/pulso.ico    # Ícone do app (gerado por tools/gerar-icone.ps1)
├── docs/imagens/       # Imagens deste README
├── app.manifest        # DPI por monitor e controles visuais do Windows
├── build.cmd           # Compila o bin\Pulso.exe
├── instalar.cmd        # Compila e instala (ou reinstala por cima)
└── atualizar.cmd       # Baixa a versão nova do GitHub e reinstala
```

## Instalação

O Pulso roda em qualquer computador com **Windows 10 ou 11** e se instala como um app comum, só para o seu usuário, sem permissão de administrador. Cada computador mantém as próprias configurações em `%APPDATA%\Pulso`.

1. Clique em **[Instalar Pulso](https://github.com/LucasDias777/Pulso/releases/latest/download/Pulso.exe)** e abra o `Pulso.exe` baixado.
2. Se o Windows avisar que protegeu o computador (o Pulso não tem assinatura digital), clique em **Mais informações** › **Executar assim mesmo**.
3. Na janela **Instalar o Pulso**, escolha os atalhos (**Menu Iniciar** vem marcado; **área de trabalho**, desmarcado) e clique em **Instalar**: ele se copia para a pasta de programas do usuário, entra em Aplicativos do Windows, fica com o ícone na bandeja e abre.

O exe vem das [Releases](https://github.com/LucasDias777/Pulso/releases) deste repositório, e é por elas que o Pulso instalado assim se atualiza.

## Atualização

O Pulso procura versão nova sozinho, 2 minutos depois de abrir e a cada 12 horas, e também pelo botão **Procurar atualização** em Configurações › Geral. Ele compara a versão instalada com a última [Release](https://github.com/LucasDias777/Pulso/releases) deste repositório, que é público: não precisa de login nem de conta no GitHub.

Quando há versão nova, aparecem **Instalar atualização** em Configurações › Geral e **Atualizar o Pulso (versão nova)** na bandeja. O Pulso baixa o `Pulso.exe` novo, confere o tamanho e o SHA-256 que o GitHub publica, troca pelo instalado e reabre em alguns segundos. As configurações e o histórico ficam.

Baixar de novo pelo botão **Instalar Pulso** e abrir o `Pulso.exe` também atualiza: ele pergunta se substitui o Pulso instalado.

## Desinstalação

Por Configurações › Aplicativos do Windows, como qualquer app, ou pelo botão **Desinstalar…** em Configurações › Geral. O desinstalador fecha o Pulso, remove os atalhos, a entrada em Aplicativos, o início com o Windows e a barra de status do Claude Code (se estava ligada), e pergunta se apaga também as configurações e o histórico.

## Como usar

| Ação | Resultado |
|---|---|
| Passar o cursor num anel | Abre o cartão daquele provedor |
| Clicar num anel | Pede uma leitura nova (no Claude, respeitando o intervalo mínimo de 2 min) |
| Clicar numa sessão no cartão | Traz a janela daquela sessão para a frente |
| Passar o cursor na orelha de baixo e clicar na engrenagem | Abre as Configurações |
| Arrastar os pontinhos ou a engrenagem, ou segurar Alt e arrastar a cápsula | Leva a cápsula para outra posição, borda ou monitor (com **Arrastável** ligado) |
| Botão direito na cápsula | Atualizar, abrir a página de uso, manter aberto, mudar de borda, ocultar, Configurações e sair |
| `Win + Y` | Mostra e oculta a cápsula |
| Abrir o Pulso pelo Menu Iniciar com ele já aberto | Abre as Configurações |

## Configurações

<table>
  <tr>
    <td align="center"><img src="docs/imagens/configuracoes-contas.png" alt="Aba Contas" width="260" /></td>
    <td align="center"><img src="docs/imagens/configuracoes-aparencia.png" alt="Aba Aparência" width="260" /></td>
    <td align="center"><img src="docs/imagens/configuracoes-geral.png" alt="Aba Geral" width="260" /></td>
  </tr>
  <tr>
    <td align="center"><sub>Contas</sub></td>
    <td align="center"><sub>Aparência</sub></td>
    <td align="center"><sub>Geral</sub></td>
  </tr>
</table>

| Aba | O que dá para ajustar |
|---|---|
| **Contas** | Quais provedores têm anel, a estimativa ao vivo do Claude e a barra de status do Claude Code. Mostra o plano e a origem da última leitura de cada um |
| **Aparência** | Exibição, cápsula adaptável, tamanho (o Pequeno encolhe só a cápsula e os anéis; o texto dos cartões fica como no Médio), tema, anel semanal, ritmo do dia no Claude, consumo por projeto, borda, monitor, arrastável e os limites de cor |
| **Geral** | Idioma, abrir com o Windows, atalho global, cada tipo de aviso e o som, atualizações, pasta de dados e desinstalar |
| **Relatórios** | O consumo do dia, da semana ou do mês (‹ › volta aos anteriores), filtrado por provedor: resumo, gráfico por dia (clicar num dia abre só ele) e a fatia de cada provedor, modelo e projeto. **Salvar CSV…** abre direto no Excel; **Abrir no navegador** mostra o relatório completo em HTML |

## Créditos

O Pulso é uma versão melhorada do **[Codenotch](https://github.com/vinzdg/codenotch)**, criado por Vinz. O design da cápsula, dos cartões e das Configurações e a leitura de cada provedor partem dele, refeitos em C# como app nativo do Windows, sem navegador embutido, com mudanças e recursos próprios.

O Codenotch é distribuído sob a licença MIT, reproduzida abaixo como ela pede:

```text
MIT License

Copyright (c) 2026 Vinz

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
