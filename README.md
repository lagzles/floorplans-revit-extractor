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

A M3 monta o label (`Core/Label`, `LabelJsonWriter`): o schema do dataset de
treino campo por campo, na ordem de chaves de um label real do
`struct7k_v4`, com `source` logo após `seed`. `instances_px` é calculado
como o `labels.py` do gerador (conferido bit a bit em 999 instâncias), com o
`round()` do Python. Onde o texto do spec v1 e o gerador divergem, vale o
gerador, porque a amostra se junta ao dado dele: `envelope` pela contagem de
vértices (4 rect, 6 L, 8 T, senão U), `exterior` = porta em parede externa,
`room_a` sempre preenchido. As chaves de `params` e `render_style` vêm do v4;
trocar quando houver um label do v7.

### Exportar vista ativa (M4, primeira entrega do spec v2)

Botão **"Extração" → "Exportar vista ativa"**: transforma a planta baixa
aberta numa amostra de treino com as 17 classes do spec v4
(`teste-2d-reading/docs/spec-plugin-revit-v4.md`). As classes 12 a 16
refinam uma classe antiga, então a máscara dobra exatamente para 12 e para
10 classes:

| classe | de onde vem |
|---|---|
| 1 room | ambientes cortados pelo plano de corte, face acabada; todo piso modelado do pavimento, com ou sem ambiente (exceto sob pátio descoberto); rampa |
| 2 wall | seção da parede no plano de corte |
| 3 door | porta com folha: retângulo largura do vão × espessura da parede, no eixo da parede |
| 4 window | janela com menos de 3,0 m, mesmo retângulo |
| 5 pillar | seção do pilar no plano de corte, forma real |
| 6 stair | projeção de lances e patamares; ambiente "escada"/"stair" |
| 7 furniture | caixa da família girada com a instância: móvel solto e eletrodoméstico de pé, só sobre piso |
| 8 swing | arco e folha da porta desenhados na vista, traço de 4 cm, só sobre piso |
| 9 void | shafts e furos de laje; ambiente "elevador"/"shaft" |
| 10 grid | traço do eixo como a vista o mostra, círculo da bolha e letra da bolha |
| 11 dimension | cota linear: linha, chamadas, tiques e o número |
| 12 passage | porta sem folha desenhada (vão, "cased opening"); abertura de parede até o piso |
| 13 text | caixa justa das letras de notas e tags, apertada pela tinta da imagem |
| 14 low_wall | guarda-corpo de terraço/varanda (faixa de 10 cm); parede abaixo do plano de corte |
| 15 curtain_wall | parede-cortina; janela com 3,0 m ou mais |
| 16 fixture | louça, bancada, marcenaria, divisória sanitária, equipamento fixo |

Ordem de pintura (spec v4 §5): ambiente, escada, vazio, parede e pele de
vidro, mureta (só sobre fundo/piso), pilar, móvel e louça (só sobre piso),
arco, janela, porta e passagem, e por último eixo, cota e texto, cada um só
sobre fundo ou piso. Pátio descoberto fica fundo. Famílias sem sólido
(símbolos 2D) e zonas de folga não são pintadas. Ainda fora: elementos de
modelos vinculados e cotas de nível — cada caso aparece em `review.json`.
Em `qa/` vão três imagens por amostra: a máscara a 40%, `_anotacao` (eixo,
cota e texto) e `_novas` (passagem, mureta, pele de vidro e louça).

- A vista precisa de **região de recorte ativa e retangular**; com o recorte
  de anotação ativo, cota e texto de fora não entram pela metade.
- A imagem sai a **48 px/m** (faixa típica do spec). O exportador mede o PNG
  gerado para achar a região que ele cobre; se nenhuma bater com 1 px de
  tolerância, para e mostra as medidas, sem gravar nada desalinhado.
- Quando a anotação fora do recorte deixa a imagem maior que o recorte
  (comum em plantas ampliadas), o px/m sai do contorno da vista
  (`View.Outline`, a extensão que o Revit enquadra, conferida pela altura) e a
  posição do recorte na imagem é achada por registro: as bordas das paredes e
  pilares cortados deslizam sobre as linhas da imagem até encaixar. Só vale
  um encaixe claramente melhor que os outros; a amostra sai com
  `source.alinhamento: "registrado"` e a nota no `review.json`.
- O lote vai para `%USERPROFILE%\floorplans-dataset\` (fora de `Documentos`,
  que costuma estar no OneDrive, cuja sincronização trava arquivos): `images/`,
  `masks_semantic/`, `masks_instance/`, `labels/` (os quatro do dataset),
  `geom/`, `qa/` (imagem + máscara a 40%, para conferir o alinhamento),
  `reports/`, `rejected/`, `manifest.json`, `classes.json`, `registry.json`
  e `review.json`. Reexportar a mesma vista reaproveita o id (900001 em
  diante) e substitui a amostra.
- O Revit enquadra a imagem exportada pela extensão do que está desenhado.
  Para essa extensão ser o recorte, o comando mostra a região de recorte
  dentro de um grupo de transação que sofre **rollback** logo após a
  exportação: o modelo termina exatamente como estava, e o Revit não pede
  para salvar nada. As bordas do recorte aparecem como uma moldura fina na
  imagem.
- A máscara é cortada no recorte do modelo, como o Revit corta os gráficos.
- Toda exportação deixa em `debug/` o PNG cru do Revit e as medidas usadas
  para alinhar (`view_<id>_export.png`, `view_<id>_medidas.txt`).

### Exportar várias vistas (lote no modelo aberto)

Botão **"Extração" → "Exportar várias vistas"**: exporta muitas plantas sem
abrir uma a uma.

- A tela lista as plantas do modelo agrupadas como no navegador de projeto
  (plantas de piso, estruturais, de área e de forro), com checkbox no grupo e
  em cada vista. As que estão em folhas vêm marcadas. As que não podem virar
  amostra (sem recorte, recorte não retangular, planta de forro) aparecem em
  cinza com o motivo.
- Resoluções da spec v4 §6: ralo 22, típico 48 e denso 100 px/m, uma ou mais.
  A mesma vista em outra resolução é outra amostra (chave `view_id@px`; a de
  48 px/m mantém a chave e o id antigos).
- Uma vista que falha não para o lote. Dá para cancelar entre uma vista e
  outra. No fim aparece um resumo, e `lotes/lote_AAAAMMDD_HHMMSS.json` traz o
  resultado e o tempo de cada vista e resolução.

## Build

Alvo: Revit 2024 (.NET Framework 4.8). Não é necessário ter o Revit
instalado para compilar — o build usa o pacote NuGet
`Revit_All_Main_Versions_API_x64` (já em cache local).

```
dotnet build
```

O Revit carrega o plugin direto da saída do build, por um manifesto na
pasta de add-ins do usuário (o Revit só lê manifestos na raiz dessa pasta):

```
%AppData%\Autodesk\Revit\Addins\2024\FloorplansRevitExtractor.addin
```

com `<Assembly>` apontando para o caminho completo de
`src\FloorplansRevitExtractor\bin\Debug\FloorplansRevitExtractor.dll`. As
dependências (`FloorplansRevitExtractor.Core.dll` e as do `System.Text.Json`)
ficam na mesma pasta. Não crie um segundo manifesto com o mesmo AddInId: o
Revit acusa conflito. Com o Revit aberto, o build falha porque a DLL fica
travada.

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
