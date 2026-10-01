# floorplans-revit-extractor

Plugin Revit para extração de dados de plantas baixas. Este repositório
começa com um MVP mínimo — contar níveis e folhas montadas do documento
ativo — como base para o extrator de dataset completo (formato `struct7k`)
descrito em `spec-plugin-revit.md` (projeto `teste-2d-reading`).

## Status atual

`src/FloorplansRevitExtractor`: addin com dois botões de ribbon, ambos
somente leitura (nunca abrem transação de escrita nem salvam o `.rvt`):

- **"Diagnóstico" → "Contar Níveis e Folhas"**: mostra, para o documento
  aberto no Revit, quantos `Level` e quantas `ViewSheet` existem.
- **"Extração" → "Extrair Rooms (preview)"**: extrai os dados de `Room` de
  um nível escolhido (tipo, nome, zona, polígono, área, centróide) e grava
  um JSON de depuração em `floorplans-extractor-debug/` ao lado do `.rvt`.
  **Não é uma amostra `struct7k` completa** — é a base para os próximos
  incrementos (paredes, portas, janelas, imagens, máscaras), descritos em
  `spec-plugin-revit.md` (projeto `teste-2d-reading`).

A lógica pura (vocabulário de tipos de ambiente, geometria de polígonos,
serialização JSON) vive em `src/FloorplansRevitExtractor.Core`, sem
dependência da API do Revit, e tem cobertura de testes unitários em
`tests/FloorplansRevitExtractor.Core.Tests`.

`Core/Raster` e `Core/Png` (M1 do spec v2) pintam as máscaras a partir de
polígonos já em pixels: preenchimento por varredura sem antialiasing (centro
do pixel, par-ímpar para furos), a ordem de pintura das 10 classes com o
estêncil de móvel e arco de porta, a máscara de instância (`room_id + 1`) e a
escrita de PNG modo P e 16 bits (`I;16` no PIL), sem biblioteca de imagem.

A M2 liga metros a pixels: `ViewFrame` (modelo → referencial do CropBox da
vista, recusando base espelhada), `PixelTransform` (a fórmula do spec, com
px/m medido no PNG exportado), `OpeningRectangle` (vão = largura × espessura
da parede, sem estender as pontas) e o contrato `geom/NNNNNN.json`
(`GeomJsonWriter`/`GeomJsonReader`, leitura estrita, coordenadas sem perda de
precisão para servir de teste de regressão). `GeomRasterizer` pinta a máscara
direto de um `GeomDocument`. Nada disso está ligado a um comando do Revit
ainda.

## Build

Alvo: Revit 2024 (.NET Framework 4.8). Não é necessário ter o Revit
instalado para compilar — o build usa o pacote NuGet
`Revit_All_Main_Versions_API_x64` (já em cache local).

```
dotnet build
```

Em build `Debug`, o target `DeployToRevitAddinsFolder` copia
`FloorplansRevitExtractor.addin` e todas as DLLs de saída (o plugin,
`FloorplansRevitExtractor.Core.dll` e as dependências transitivas do
`System.Text.Json`) para:

```
%ProgramData%\Autodesk\Revit\Addins\2024\FloorplansRevitExtractor\
```

Depois de instalar o Revit 2024 e abrir um projeto, o plugin carrega
automaticamente (não precisa copiar nada manualmente).

## Testes automatizados

A lógica sem dependência da API do Revit (`FloorplansRevitExtractor.Core`)
tem testes unitários com xUnit + FluentAssertions:

```
dotnet test tests/FloorplansRevitExtractor.Core.Tests
```

A camada de integração com o Revit (`RevitInterop`, `UI`, `Commands`) não é
testável por unidade sem uma instância do Revit rodando — é verificada
manualmente, como descrito abaixo.

## Como testar

**Fluxo 1 — Contar Níveis e Folhas**

1. Instale o Revit 2024.
2. Rode `dotnet build` (com o Revit fechado, se já estiver com o addin
   carregado de uma execução anterior — o Revit trava o `.dll` em uso).
3. Abra o Revit, abra um projeto qualquer.
4. Na aba "Floorplans Extractor" → painel "Diagnóstico", clique em
   "Contar Níveis e Folhas".
5. Confira se os números batem com o Project Browser (seções Levels e
   Sheets).

**Fluxo 2 — Extrair Rooms (preview)**

1. Com o Revit aberto num projeto com ambientes (`Room`) nomeados em
   português, na aba "Floorplans Extractor" → painel "Extração", clique em
   "Extrair Rooms (preview)".
2. Se houver mais de um nível com ambientes, escolha um na lista; se houver
   só um, ele é selecionado automaticamente.
3. Confira no diálogo: contagem de ambientes válidos, descartados e nomes
   não mapeados; confirme que o Revit não pede para salvar nada ao fechar
   (prova de que o plugin é somente leitura).
4. Abra o JSON gerado em `floorplans-extractor-debug/` ao lado do `.rvt`
   (ou em `%TEMP%\floorplans-extractor-debug\` se o documento ainda não
   tiver sido salvo): confira que é UTF-8 sem BOM, que nomes acentuados
   aparecem corretos, e compare `area`/`centroid` de alguns ambientes
   contra as propriedades do Revit (tolerância de 2% — divergências maiores
   aparecem em `debug.area_mismatch_warnings`).
5. Teste um ambiente com nome fora do vocabulário fechado (ex.: "DEPÓSITO")
   e confirme que aparece em `debug.unmapped_names` com
   `type`/`zone`/`class_id` nulos — nunca `"office"`.

## Próximos passos

Ver `spec-plugin-revit.md` para o escopo completo. Extração de rooms feita
(preview); faltam: paredes (espessura, outline, envelope), portas/janelas
(`FromRoom`/`ToRoom`, mapeamento de família) + `connectivity_graph`,
exportação de imagem e rasterização das máscaras semântica/instância,
montagem da amostra `struct7k` completa (4 arquivos + `manifest.json` +
`classes.json`), gate de aceite e modo folha/batch.
