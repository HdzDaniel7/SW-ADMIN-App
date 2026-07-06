# DECISIONES DE ARQUITECTURA

> Registro de decisiones. Formato: fecha, contexto, decisión, alternativas descartadas.

---

## 2026-07-04 — El Explorador como centro de operaciones; extracción por carpeta; detalle agregado del BOM

**Contexto:** feedback del usuario tras usar la v1.0.0: la gestión de carpetas escondida en
Configuración es engorrosa cuando ya existe una pantalla dedicada a carpetas (Explorador);
la extracción por lote pertenece al Explorador (por carpeta, no solo global); y en ensambles
quería ver/exportar las propiedades/roscas/etc. de TODAS las piezas del BOM.

**Decisión — gestión de carpetas en el Explorador:** barra en la pestaña Carpetas con
➕ Agregar (multiselección, indexa de inmediato), ➖ Quitar (con la pregunta de purgar datos),
🗑 Purgar omitidos. La lógica sigue en `EscaneadorCarpetas`; la pestaña de Configuración se
conserva (misma lógica de servicio detrás, cero duplicación de reglas). **Quitar aplica solo
a carpetas RAÍZ** (`CarpetaEsRaiz` calculado contra `CarpetasEscaneo`); para subcarpetas la
idea futura registrada es "excluir subcarpeta" vía `PatronesExcluidos` — no implementada.

**Decisión — estrategia de extracción por lote (evaluada explícitamente a pedido):**
**secuencial abrir→extraer→cerrar, un archivo a la vez.** Razones técnicas: (1) la API COM de
SolidWorks es STA — llamadas "paralelas" contra una instancia se serializan igual, no hay
ganancia; (2) abrir muchos documentos a la vez dispara el consumo de memoria de SW y lo
desestabiliza (razón del reinicio-cada-N ya previsto en F2); (3) el pipeline existente ya
tiene timeout por archivo y error-por-archivo-sin-frenar-lote. Memoria constante, cancelable
entre archivos. Alternativas descartadas: paralelizar DocManager con múltiples instancias
(ganancia menor, complejidad alta — reevaluar solo si el lote con licencia resulta lento);
abrir toda la carpeta a la vez (inestable por diseño).

**Implementación:** `ProcesarPendientesAsync` gana `carpetaFiltro` (prefijo de ruta con límite
de segmento — **las subcarpetas funcionan gratis**: extraer un nodo del árbol procesa toda su
rama). Explorador: "▶ Extraer esta carpeta" (nodo seleccionado, raíz o subcarpeta),
"⏬ Extraer todos" (movido aquí; ELIMINADO de la grilla Archivos junto con su código), barra
de progreso determinada (x de y) + "⛔ Cancelar" + toast de resumen — mismo patrón que ya se
verificó en la grilla.

**Decisión — detalle agregado del BOM:** CheckBox "Incluir componentes del BOM en las
pestañas" en el encabezado del detalle (visible solo en ensambles, sticky entre selecciones).
Activado: Propiedades/Configuraciones/Features/Físicas/Roscas consultan el archivo + todos los
`ArchivoId` del BOM indentado, con columna "Componente" indicando el origen (vacía para el
archivo propio). Los grids de Configuraciones/Roscas/Features/Físicas pasan a records wrapper
(`FilaConfigDetalle` etc. — las entidades EF no admiten el campo de origen); `PropiedadVista`
gana `Componente` opcional al final (no rompe llamadas posicionales). El Excel de propiedades
hereda la columna automáticamente (exporta la colección visible). La forma de UI elegida entre
las que propuso el usuario (checklist/toggle/combo): checkbox simple — un estado binario no
amerita más.

**Verificación:** build limpio, 55/55 tests, verificación visual por UI Automation: barra de
gestión y extracción visibles en Carpetas con habilitación correcta (Quitar solo en raíces),
"Extraer todos" ya no está en Archivos, checkbox del BOM visible y activable en un ensamble
con la columna Componente presente. **Bonus confirmado en la misma captura: las miniaturas del
shell funcionan** — el thumbnail del ensamble se ve en el detalle sin licencia ni SW abierto
(primera confirmación visual real de esa función). Los datos del usuario muestran estados
"error" reales (probó "Extraer todos" sin SW abierto — el tooltip de la celda explica la causa).

---

## 2026-07-04 — Tanda "todas las recomendaciones" + paquete distribuible v1.0.0

**Contexto:** el usuario aprobó implementar la lista completa de recomendaciones y agregó el
requisito de distribución: que la app se pueda copiar/descargar y usar en cualquier equipo sin
preparación, con acceso directo al escritorio "como una aplicación común".

**A — Robustez de base:**
- SQLite en modo **WAL** (`PRAGMA journal_mode=WAL` en `InicializarAsync`, idempotente,
  persistido en el .db): UI abierta + Batch programado escribiendo a la vez sin bloqueos.
- **Instancia única** con `Mutex Local\SWDataExtractor_UI` en `App.OnStartup` (dos instancias
  contra el mismo SQLite podían pisarse — pasó durante las verificaciones).
- **Excepciones globales**: `DispatcherUnhandledException` (log + diálogo con la ruta del log,
  la app sigue viva), `AppDomain.UnhandledException` y `TaskScheduler.UnobservedTaskException`.
- **Log de la UI a archivo** con Serilog (`%LOCALAPPDATA%\SWDataExtractor\logs\ui-*.log`,
  rotación diaria) — mismos paquetes que ya usaba el Batch, no se agregaron identidades nuevas.
- Versión real (1.0.0 en csproj) mostrada en "Acerca de".

**B — UX de grilla:** fechas ISO → `dd/MM/yyyy HH:mm` y bytes → KB/MB (converters);
tooltip con `mensaje_error` sobre la celda de estado (antes un "error" no decía su causa);
menú contextual (abrir/carpeta/extraer, con clic derecho seleccionando la fila bajo el
cursor); tamaño/posición/maximizado de la ventana persistidos en `ajustes_app`
(con validación contra el escritorio virtual para no perder la ventana al quitar un monitor).

**C — Extracción por lote:** `ProcesarPendientesAsync` ahora recibe `IProgress<ProgresoLote>`
y devuelve `ResumenLote` (total/ok/errores/cancelado); la cancelación es cooperativa ENTRE
archivos (lo procesado queda persistido, sin excepción). Botón "⏬ Extraer todos" +
"⛔ Cancelar" visible durante el lote, barra de progreso determinada (x de y) y toast con el
resumen al terminar (éxito/errores/cancelado).

**D — Explorador:** total de "espacio recuperable" en Duplicados; exportación de los 4
reportes a un Excel multi-hoja; **doble clic en cualquier fila de reporte navega a la sección
Archivos con ese archivo seleccionado** (evento `VerDetalleSolicitado` →
`MainViewModel` → `ArchivosViewModel.SeleccionarPorIdAsync`) — los reportes dejan de ser
"sin salida".

**E — Cumplimiento de propiedades:** `ObtenerIncumplimientosAsync` cruza
`diccionario_propiedades` (activas+obligatorias) contra las propiedades con valor de los
archivos extraídos OK (pendientes/error se excluyen: aún no tienen propiedades, señalarlos
sería ruido). Pestaña "✔ Cumplimiento" en el Explorador + hoja en el Excel. 2 tests nuevos
(55 total).

**F — Paquete distribuible ("instala y funciona" literal):**
- **Formato elegido: carpeta portable / ZIP autocontenido**, NO instalador MSI/Inno. Razones:
  cero fricción (copiar y ejecutar, sin permisos de administrador), la BD viaja junto al exe
  (portable entre equipos por USB/red), y no requiere infraestructura de firma/instalación.
  Un instalador queda como opción futura si se necesita despliegue corporativo con GPO.
- `dotnet publish` **self-contained single-file** (win-x64, comprimido) para UI y Batch en la
  misma carpeta: no requiere .NET instalado en el equipo destino. UI ≈ 86 MB, Batch ≈ 54 MB,
  ZIP total ≈ 123 MB.
- **Ícono propio** (`src/UI/Recursos/app.ico`, monograma "SW" sobre el azul de acento #1D4E89,
  4 tamaños con entradas PNG) generado por `herramientas/generar-icono.ps1` y embebido vía
  `ApplicationIcon` — el exe se ve como una aplicación común en Explorador/barra de tareas.
- **Acceso directo al escritorio** por dos vías: menú Herramientas → "Crear acceso directo en
  el escritorio" (WScript.Shell vía COM, con liberación de objetos) y el script
  `Crear acceso directo.ps1` incluido en el paquete (para crearlo sin abrir la app).
- `herramientas/publicar.ps1` automatiza todo: publish de ambos exes + limpieza de .pdb +
  `LEEME-INSTALACION.txt` + script de acceso directo + ZIP versionado. **Lección de encoding:**
  los .ps1 deben guardarse con BOM UTF-8 — PowerShell 5.1 los lee como ANSI sin BOM y los
  caracteres multibyte (— acentos) rompen el parser.
- `LEEME-INSTALACION.txt` cubre: requisitos (solo Windows x64; SolidWorks opcional), instalar,
  primeros pasos, dónde viven BD/logs, tarea programada, y **guía de BD compartida** vía
  SQL Server en appsettings (cierra la recomendación #16).
- `dist/` agregado a `.gitignore`.

**Verificación:** build limpio, **55/55 tests**, y prueba real del paquete: se ejecutó
`publicar.ps1` completo, se lanzó `dist\SWDataExtractor\SWDataExtractor.UI.exe` y se capturó
la ventana — experiencia de primera instalación correcta (BD nueva creada junto al exe con
WAL activo, migraciones aplicadas, tema claro por defecto, secciones y botón "Extraer todos"
presentes). La BD del smoke test se eliminó para dejar `dist/` prístino; el ZIP se genera
antes de cualquier prueba, así que queda limpio.

**Pendiente de validación por el usuario:** probar el ZIP en OTRO equipo real (el smoke test
corrió en la misma máquina de desarrollo — no valida ausencia de dependencias del entorno,
aunque self-contained single-file lo garantiza por diseño); flujo de lote con SW real.

---

## 2026-07-04 — Fase 7a implementada + miniaturas vía shell de Windows

**Contexto:** implementación de la Fase 7a acordada (`DISENO.md` §6) y del spike de miniaturas
sin licencia. Verificada en vivo contra los datos reales del usuario (7 proyectos, 61 archivos).

**Decisión — Fase 7a:**
- Nuevo `ServicioAnalisisProyecto` (Application, inyecta `AppDbContext` + `EscaneadorCarpetas`
  para las raíces de escaneo): árbol de carpetas derivado de `archivos.ruta`, archivos por
  carpeta, duplicados exactos por hash, referencias rotas, posibles versiones (heurística de
  nombre normalizado — sufijos `_v2`, `_final`, `(3)`, `rev A`, fechas — iterada hasta
  estabilizar, agrupando por raíz+tipo y exigiendo hashes distintos para no solapar con el
  reporte de duplicados), ensambles top-level (no referenciados como hijo por nadie) y salud
  de proyecto calculada sobre el BOM indentado existente. 16 tests nuevos (53 total).
- Sin raíces de escaneo guardadas, las raíces del árbol se derivan del propio conjunto de
  rutas (directorio que no es descendiente de ningún otro del conjunto) — descubierto por test.
- Nueva sección "Explorador" en el sidebar (índice 1) con `ExploradorView`: pestañas Carpetas
  (TreeView + grilla con abrir archivo/carpeta), Duplicados, Referencias rotas, Posibles
  versiones y Dashboard de proyecto (combo de top-level + tarjetas de salud + BOM con estado
  de extracción por componente). Análisis automático al entrar por primera vez.
- **Sin flag nuevo en `Funcionalidades`**: los reportes son de solo consulta sobre datos ya
  extraídos — mismo nivel de acceso que la grilla base de Archivos, disponible para todos los
  roles incluido Visualizador. 7b (copiar/mover) SÍ requerirá flag + rol cuando se haga.
- **Hallazgo con datos reales:** la extracción vía SwApi no marca `es_toolbox` (la detección
  COM está pendiente de validar con SW real), así que la tornillería del Toolbox aparecía como
  "referencia rota" (3 de 4 filas en los datos del usuario eran tornillos de
  `C:\SOLIDWORKS Data\browser\...`). Heurística por ruta (`\SOLIDWORKS Data\`, `\Toolbox\`,
  `\browser\`) para excluirlas del reporte hasta que la extracción la marque de forma
  confiable — queda pendiente implementar la detección real en `ExtractorSwApi`.
- Fechas del explorador formateadas legibles (`dd/MM/yyyy HH:mm`) vía `FormatearFecha` en los
  DTOs — primer paso de la mejora general de fechas (la grilla principal aún muestra ISO).

**Decisión — miniaturas sin licencia:** `ServicioMiniaturas` (UI) pide la miniatura al shell
de Windows (`SHCreateItemFromParsingName` + `IShellItemImageFactory.GetImage`, APIs
documentadas de shell32 — no aplica regla VERIFICAR-API de SolidWorks). Es el MISMO mecanismo
del Explorador de Windows: SolidWorks instala su thumbnail provider al instalarse, así que hay
previews **sin licencia DocManager y sin SW abierto**. `DetalleArchivoViewModel` lo usa como
fallback cuando no existe el PNG de DocManager (`Preview` pasa de `BitmapImage` a
`ImageSource`). Si SW no está instalado, devuelve null y no se muestra preview — degradación
elegante alineada con "instala y funciona".

**Verificación:** build limpio, 53/53 tests, y batería visual real por UI Automation +
capturas: sección Explorador con árbol de 7 proyectos y conteos, selección de carpeta lista
sus 8 archivos con fechas legibles, reporte de rotas con datos reales, dashboard con tarjetas
de salud (8 componentes / 7 pendientes / 1 error) y BOM coloreado. `arquitectura.dot`
actualizado con el servicio nuevo (SVG no regenerado: `dot` no está instalado en esta máquina
— regenerar con `dot -Tsvg docs/diagramas/arquitectura.dot -o docs/diagramas/arquitectura.svg`).

**Alternativas descartadas:** flag de Funcionalidades para los reportes (ver arriba);
implementar la detección Toolbox real vía COM ahora (requiere SW abierto para validar la
firma — pospuesto con heurística de ruta como puente).

---

## 2026-07-04 — Sistema de temas rehecho desde cero (v2): sin Mica, diccionarios intercambiables

**Contexto:** el usuario reportó una batería de bugs visuales usando la app de verdad: barra de
título negra, zonas negras pegadas al volver de oscuro a claro (panel de detalle, toolbar,
pestañas en azul oscuro), texto negro ilegible en el grid en modo oscuro, botones y encabezados
que se quedaban claros en oscuro, doble selección persistente, panel de detalle tapando
columnas al abrir, y BOM mostrando rutas completas del Toolbox en vez de nombres.

**Causas raíz (verificadas contra el código fuente de WPF-UI, no especuladas):**
1. `ApplicationThemeManager.Apply(tema)` usa **backdrop "Mica" por defecto**, y su
   `WindowBackgroundManager.UpdateBackground` → `WindowBackdrop.RemoveBackground` QUITA el
   fondo real de la ventana para que DWM pinte el efecto. Cuando Mica no renderiza → negro
   (título, panel de detalle); al volver a claro, el fondo nunca se restaura → zonas negras
   pegadas. **Fix:** siempre `Apply(tema, WindowBackdropType.None, updateAccent: false)`.
2. El intercambio del diccionario de tema del manager podía no aplicarse de forma confiable;
   además `UpdateDictionary` REEMPLAZA la instancia de `ThemesDictionary` por un
   `ResourceDictionary` plano, rompiendo cambios posteriores. **Fix:** setear
   `ThemesDictionary.Theme` directamente (setter público que cambia su `Source`) antes de
   llamar al manager — doble seguro.
3. Las asignaciones sueltas de brushes a `Application.Current.Resources` (enfoque v1)
   sombreaban claves del tema base entre cambios. **Fix:** paleta propia en diccionarios
   intercambiables (`Temas/TemaPlanoTecnico.xaml` / `TemaConsola.xaml`) que se quitan y se
   re-agregan AL FINAL de los MergedDictionaries en cada cambio (el último gana). Definen:
   overrides de WPF-UI (`ApplicationBackgroundBrush`, `WindowBackground`,
   `ControlFillColorDefaultBrush`, `DividerStrokeColorDefaultBrush`, claves de pestaña activa
   con acento) + claves propias (`SwdeBarraTituloBrush`, `SwdeFilaAlternaBrush`).
4. El `Foreground` heredado de las celdas del DataGrid (vía `ListViewItemForeground` del
   estilo base) no se re-resolvía al alternar el tema EN VIVO (sí en arranque). **Fix doble:**
   setter `Foreground={DynamicResource TextFillColorPrimaryBrush}` en el estilo propio de
   DataGrid (camino que sí se actualiza, igual que los encabezados) + recarga del grid tras
   cambiar tema (`AlternarTemaAsync` llama `CargarCommand`), que regenera los contenedores de
   fila bajo el tema nuevo (elimina cualquier residuo: selección, foreground, triggers).
5. Paleta oscura suavizada por feedback: de negro `#14171C` a azul grisáceo `#171D26`/
   `#202834` (el negro chocaba contra el texto).

**Otros fixes de la misma ronda:**
- **Doble selección (definitivo):** `CargarAsync` ahora crea una colección y una vista NUEVAS
  por carga (`ArchivosVista` pasa a propiedad observable) en vez de `Clear+Add` sobre la misma
  instancia — reemplazar el ItemsSource resetea el estado interno de selección del DataGrid.
  La multi-selección se sincroniza explícitamente tras restaurar (el `SelectionChanged` del
  grid puede dispararse antes de que el ItemsSource nuevo aplique, dejando el panel colapsado).
- **Espacio al abrir:** el panel de detalle y su separador se COLAPSAN por completo sin
  selección (`BoolADimensionConverter` sobre `ColumnDefinition.Width/MinWidth`) — la grilla usa
  todo el ancho al arrancar. Además la lista se carga sola al abrir (antes quedaba vacía hasta
  "Recargar").
- **Franja de título:** `Border` con `SwdeBarraTituloBrush` + divisor inferior detrás de
  TitleBar+Menu en las 3 ventanas; `WindowBackdropType="None"` y `Background` explícito.
- **BOM Toolbox:** componentes no indexados en `archivos` (tornillería del Toolbox) mostraban
  la ruta completa como nombre (`COALESCE(a.nombre, c.ruta_referenciada)`) — ahora se muestra
  solo `Path.GetFileName(...)` y se agregó `ConfiguracionUsada` a `ItemBom` (parámetro opcional
  al final, no rompe llamadas posicionales), al CTE, a la grilla BOM y al Excel — en tornillería
  la configuración lleva la designación ("M8x1.25 x 30").
- **EstadoColorConverter:** tonos medios legibles en ambos temas y `UnsetValue` para estados
  desconocidos (hereda el foreground del tema en vez de negro fijo); verdes/rojos de
  Configuración actualizados igual.

**Verificación:** build limpio, 37/37 tests, y batería visual real por capturas de ventana +
UI Automation (sin robar foco): arranque en oscuro ✓, alternado en vivo oscuro→claro sin zonas
pegadas ✓, claro→oscuro con texto legible ✓, selección de fila expande el detalle con pestañas
correctas en ambos temas ✓, panel colapsado al arrancar ✓, carga automática ✓.

**Alternativas descartadas:** conservar Mica solo cuando esté disponible (indeterminista, y el
usuario ya lo sufrió — se prefiere color plano determinista); parchear más claves sueltas en
código (es exactamente el enfoque v1 que causó los estados pegados).

---

## 2026-07-03 — Segunda ronda de pulido visual: DataGrid/GroupBox/pestañas no seguían el tema

**Contexto:** feedback directo del usuario tras usar la app: el grid de archivos y las tablas
del panel de detalle se quedaban blancas sin importar el tema, los bordes/textos se veían con
"negros directos" poco atractivos, y las pestañas de herramientas/propiedades no se distinguían
bien entre sí.

**Causa raíz encontrada:** varias vistas fijaban colores literales en vez de recursos del tema:
`AlternatingRowBackground="#F5F5F5"` en 4 archivos (`DetalleArchivoView`, `ColaTrabajoView`,
`HistorialView`, `PropiedadLoteWindow`), `Foreground="Gray"/"DimGray"/"#444"` y
`BorderBrush="#CCC"` sueltos. Además, `GroupBox` **no tiene estilo propio en WPF-UI**
(confirmado contra el código fuente de la librería — no está entre los controles que
`ControlsDictionary` reestila), así que en `ConfiguracionWindow` quedaba con el bisel gris
clásico de Windows y texto negro por defecto, fuera de lugar contra el resto de la app.

**Decisión:**
- Estilo `DataGrid` centralizado en `App.xaml` (antes solo fijaba `CanUserAddRows`/etc.):
  ahora también fija `Background`/`RowBackground` = superficie del tema,
  `AlternatingRowBackground` = fondo de la app (efecto cebra sutil sin color fijo), y
  `BorderBrush`/`HorizontalGridLinesBrush`/`VerticalGridLinesBrush` = divisor del tema. Se
  quitaron los `AlternatingRowBackground="#F5F5F5"` locales de las 4 vistas (quedaban ganando
  por ser más específicos que el estilo de `App.xaml`, anulando el fix si no se quitaban).
- Reemplazados todos los `Foreground`/`BorderBrush` con color fijo por
  `TextFillColorSecondaryBrush`/`DividerStrokeColorDefaultBrush` (recursos ya usados en el
  resto de la app). Se dejaron sin tocar los colores semánticos deliberados (verde/rojo de
  éxito/error en `ConfiguracionWindow`, amarillo de advertencia en `PropiedadLoteWindow`,
  `EstadoColorConverter`) — esos son información, no marca, y ya estaba decidido mantenerlos
  fijos (ver entrada de rediseño visual anterior).
- `GroupBox` en `ConfiguracionWindow` con plantilla propia tipo "card": borde con
  `DividerStrokeColorDefaultBrush`, fondo `ControlFillColorDefaultBrush`, encabezado con
  `TextFillColorPrimaryBrush`.
- Pestañas con más separación: `ServicioTema.Aplicar` ahora también sobrescribe
  `TabViewSelectedItemBorderBrush` (antes `CardStrokeColorDefault`, un gris casi imperceptible)
  con el acento recién calculado por `ApplicationAccentColorManager`, y
  `TabViewItemHeaderBackgroundSelected` con un fondo de acento suave — reutiliza colores ya
  calculados en vez de inventar nuevos. **Se descartó** un `Style` de `TabItem` con
  `BorderThickness`/`BorderBrush` en `App.xaml`: no tenía ningún efecto porque la plantilla de
  `TabItem` de WPF-UI hardcodea `BorderThickness="1,1,1,0"` en el XAML (no usa
  `TemplateBinding`) y fija el borde de la pestaña seleccionada apuntando directo al recurso
  `TabViewSelectedItemBorderBrush`, no a la propiedad `BorderBrush` del control — cualquier
  `Setter` a nivel `Style` para esas propiedades queda sin efecto. Verificado contra el código
  fuente de WPF-UI antes de descartarlo (no se dejó a medias sin entender por qué no funcionaba).
- `ConfiguracionWindow` y `PropiedadLoteWindow` pasan de `Window` plano a `ui:FluentWindow` con
  `ui:TitleBar` (antes solo `MainWindow` lo tenía) — sin esto, esas dos ventanas secundarias
  tenían chrome nativo de Windows (título y fondo blanco del sistema) sin relación con el tema
  activo, mientras que los controles de adentro sí seguían el tema — inconsistente.

**Verificación:** se relanzó la app y se confirmó con capturas reales (ver entrada anterior
sobre el método seguro de captura) que el grid y el sidebar/toolbar respetan el tema en ambos
modos. Se detectó que la TitleBar y el panel de detalle capturaban en negro **solo
inmediatamente después de alternar el tema en caliente vía automatización** — una app recién
abierta directamente en modo oscuro (sin alternar en caliente) capturó esas mismas zonas
correctamente. Conclusión: es una limitación de `PrintWindow` capturando una región recién
recompuesta (no alcanza a esperar el siguiente frame), no un bug de renderizado real — no se
reportó como pendiente.

**Implicaciones:** sin cambios de esquema. Build limpio, 37/37 tests.

**Alternativas descartadas:** `Style` de `TabItem` a nivel `App.xaml` (ver arriba, confirmado
sin efecto contra la plantilla real); tocar los colores semánticos de éxito/error/advertencia
(fuera de alcance del pedido, ya es una decisión tomada).

---

## 2026-07-03 — Primera verificación visual real de la app; ajuste de colores; método de captura

**Contexto:** hasta ahora todo el rediseño visual se había hecho sin pantalla interactiva. En
esta sesión sí hubo acceso a un entorno Windows real, así que se abrió la app de verdad.

**Hallazgos y correcciones:**
- El tinte de "Plano técnico" (`#F7F9FC`) era casi indistinguible del blanco por defecto de
  WPF-UI (`#FAFAFA`) en pantalla — se subió a `#EAF1FA` (fondo) y `#B9CBDE` (bordes).
- El sistema de acento (`ApplicationAccentColorManager`) estaba configurado pero **ningún
  control lo usaba** — se agregó explícitamente al ítem seleccionado del sidebar
  (`AccentFillColorSecondaryBrush` + borde `SystemAccentColorSecondaryBrush`) y al botón
  "Extraer (rápido)" (`AccentFillColorDefaultBrush`/`TextOnAccentFillColorPrimaryBrush`) —
  este último tenía además una clave de recurso equivocada (`TextOnAccentFillColorPrimary` es
  un `Color`, no un `Brush`; hacía falta el sufijo `Brush`) que se corrigió tras verificar
  contra el XAML fuente de WPF-UI (`Accent.xaml`, `Light.xaml`).
- Confirmado con captura real: sidebar, toolbar (con wrap) y grid se ven correctos en ambos
  temas, y el toggle persiste entre relanzamientos de la app.
- **Sin confirmar**: la TitleBar y el panel de detalle aparecían negros en capturas tomadas con
  la API `PrintWindow` en modo claro (pero no en modo oscuro, donde negro y el fondo real casi
  no se distinguen). La captura de pantalla completa original (antes de estos cambios) sí había
  mostrado esas mismas zonas correctamente en blanco — así que la sospecha razonable es que es
  una limitación de `PrintWindow` con regiones WPF compuestas (la `TitleBar` de WPF-UI y el
  `ContentPresenter` del panel de detalle), no un bug de la app. No se pudo confirmar con
  captura de pantalla completa sin robarle el foco a otras ventanas activas del usuario
  (VS Code, SolidWorks) — se decidió no arriesgar esa interferencia y dejarlo pendiente de que
  el usuario lo confirme directamente.

**Incidente durante la verificación:** al simular un clic con coordenadas de pantalla para
probar el toggle de tema, una instancia de SolidWorks que ya estaba cargando en segundo plano
(con `gimbal_3axis_reverse` abierto y cambios sin guardar) robó el foco de la ventana antes de
que el clic llegara a destino. No se interactuó con SolidWorks ni con ese archivo en ningún
momento. A partir de ahí se cambió de método (ver abajo).

**Decisión — método de captura/interacción para verificar UI en sesiones futuras:**
1. Interactuar con controles vía **UI Automation** (`System.Windows.Automation`,
   `AutomationElement.FromHandle` + `InvokePattern`) en vez de simular clics por coordenadas de
   pantalla — no depende de foco ni de qué ventana esté encima, cero riesgo de tocar otra app.
2. Para capturar contenido, usar `PrintWindow` por handle de proceso como primera opción (no
   roba foco), aceptando que puede fallar (mostrar negro) en regiones WPF compuestas — si algo
   sale sospechosamente negro, no asumir que es un bug real sin descartar primero el artefacto
   de captura.
3. **Nunca usar `SetForegroundWindow`/clics por coordenadas de pantalla completa** salvo que no
   quede otra alternativa y se avise antes — puede robarle el foco al usuario en medio de su
   propio trabajo (pasó con su VS Code en esta sesión).

**Implicaciones:** sin cambios de esquema ni de arquitectura. Verificado: build limpio, 37/37
tests, y ahora también verificación visual parcial real (primera vez).

---

## 2026-07-03 — Tema claro/oscuro (Plano técnico / Consola) con acento compartido; fix checkbox BOM

**Contexto:** de las 5 direcciones visuales propuestas (artifact), el usuario eligió "Plano
técnico" como modo claro y "Consola" como modo oscuro, pidiendo tonos similares entre ambos
(no dos identidades visuales distintas). También reportó que el checkbox "Incluir" de la
pestaña BOM no respondía al clic.

**Decisión — checkbox BOM:** `DataGridCheckBoxColumn` en WPF requiere 2 clics cuando la grilla
es de solo lectura por defecto (el primero solo selecciona la celda; recién el segundo entra en
modo edición y togglea) — el usuario lo percibía como "no deja seleccionar". Se reemplazó por
`DataGridTemplateColumn` con un `CheckBox` explícito enlazado `TwoWay`, que responde al primer
clic — mismo patrón ya usado en los botones 📄/📂 de `ArchivosView`.

**Decisión — temas:** nuevo `ServicioTema` (capa UI) aplica y persiste (en `ajustes_app`, clave
`TemaApp`) el tema activo:
- Usa `Wpf.Ui.Appearance.ApplicationThemeManager.Apply(tema)` para la base correcta de
  texto/controles de cada modo (no se reinventa).
- Usa `ApplicationAccentColorManager.Apply(colorBase, tema)` con **un solo color de acento**
  (`#1D4E89`, el azul de "Plano técnico") para ambos modos — WPF-UI deriva automáticamente las
  variantes de brillo/saturación correctas según el tema (documentado en su sistema de accent
  colors). Así se logra el pedido de "tonos similares" sin elegir dos acentos distintos a mano.
- Sobrescribe directamente `ApplicationBackgroundBrush`, `DividerStrokeColorDefaultBrush` y
  `ControlFillColorDefaultBrush` en `Application.Current.Resources` (asignación directa al
  diccionario de nivel superior, no fusión de ResourceDictionary — evita pelear con el
  reencadenado interno de `StaticResource` que usa WPF-UI interinamente) con los tonos de cada
  dirección visual: `#F7F9FC`/`#FFFFFF`/`#D4DEE8` (Plano técnico) y `#14171C`/`#1B222B`/`#2A323D`
  (Consola). Los colores de texto y de cada control específico (`ButtonBackground`, etc.) se
  dejan tal cual los define WPF-UI para Light/Dark — no se tocan, para no arriesgar contraste
  ilegible sin poder verificarlo visualmente.
- Toggle nuevo en el pie del sidebar (`MainViewModel.AlternarTemaCommand`), persiste el cambio
  y se aplica en `App.xaml.cs` al arrancar (antes de mostrar `MainWindow`, para no mostrar el
  tema por defecto y saltar al elegido después).

**Implicaciones:** sin paquetes nuevos (todo dentro de `WPF-UI`, ya aprobado). Sin cambio de
esquema (nueva clave en `ajustes_app`, tabla genérica ya existente). Verificado: build limpio,
37/37 tests, arranque de la app sin excepciones (se ve la consulta a `TemaApp` ejecutarse en el
log de arranque). **No se pudo confirmar visualmente** que el resultado coincide con el mockup
del artifact — la técnica (accent compartido + overrides de fondo/borde) es sólida y
documentada, pero los valores exactos de contraste quedan pendientes de un vistazo real.

**Alternativas descartadas:** dos acentos distintos por tema (el usuario pidió explícitamente
tonos similares); reescribir todos los tokens de color de WPF-UI (`ButtonBackground`,
`CardBackground`, etc. — decenas de claves) para un match 1:1 con el mockup — riesgo alto sin
poder verificar visualmente, y WPF-UI ya resuelve razonablemente esos tokens por tema.

---

## 2026-07-03 — Bug de doble selección tras Extraer; ofrecer abrir si SW no está corriendo; responsividad

**Contexto:** feedback de uso real: después de "Extraer (rápido)"/"Extraer profundo" quedaban
2 filas marcadas en el grid (la presionada y la de abajo); y la ventana se veía mal en tamaños
reducidos (barra de herramientas larga, columnas del panel de detalle muy angostas).

**Decisión:**
- `ArchivosViewModel.CargarAsync()` limpia explícitamente `ArchivoSeleccionado`/
  `ArchivosSeleccionados` ANTES de `Archivos.Clear()` y los restaura por `Id` después de
  repoblar. Causa raíz: `Clear()+Add()` dispara un `Reset` en la `ObservableCollection`; sin
  limpiar la selección antes, el `DataGrid` podía quedar con una fila marcada por índice viejo
  además de la correcta por referencia nueva. Solo se restaura `ArchivoSeleccionado` (bindeado
  TwoWay); `ArchivosSeleccionados` se deja limpio porque su sincronización con el grid es
  unidireccional (code-behind) y restaurarlo dejaría "Editar propiedades" habilitado sin
  ninguna fila resaltada.
- Nuevo `OfrecerAbrirSiFaltaSw`: si la extracción termina en error con el mensaje exacto de
  `ExtractorSwApi` ("SolidWorks no está en ejecución..."), pregunta con `MessageBox` si abrir
  el archivo (reutiliza `AbrirArchivo`, ya existente) para que el usuario pueda reintentar
  cuando SW cargue. No reintenta automáticamente (tiempo de arranque de SW es impredecible).
- Responsividad: `ArchivosView` cambia su `ToolBar` (esconde controles detrás de una flecha de
  overflow al angostarse, sin aviso) por un `WrapPanel` (los controles bajan de línea, visibles
  siempre). `MinWidth` del grid/detalle maestro-detalle baja de 380/320 a 260/240;
  `MainWindow.MinWidth/MinHeight` baja de 960x600 a 720x480; sidebar de 200 a 176px. Columna de
  preview en `DetalleArchivoView` pasa de ancho fijo 160 a `Auto`+`MaxWidth=110` y se oculta
  (`NuloAVisibilidadConverter` nuevo) cuando no hay imagen, en vez de reservar espacio siempre.

**Implicaciones:** sin cambio de esquema. Verificado con build limpio, 37/37 tests y arranque
de la app sin excepciones — el ajuste de proporciones específico (tamaños, espaciado) no se
pudo confirmar visualmente en esta sesión; pendiente feedback o captura de pantalla del usuario.

**Alternativas descartadas:** reintento automático de extracción tras abrir SW (tiempo de carga
de SW no es determinístico, podría reintentar antes de que SW esté listo y confundir más que
ayudar); usar `ToolBar` con `Style` que fuerce `IsOverflowOpen` — sigue escondiendo controles,
no resuelve el problema real.

---

## 2026-07-03 — Selección de filas para exportar BOM: exclusión del aplanado por pieza, no por instancia

**Contexto:** pestaña BOM del panel de detalle ahora permite marcar/desmarcar filas antes de
exportar a Excel (todo marcado por defecto). El BOM aplanado agrupa por `Ruta` (una pieza
puede aparecer varias veces en el árbol indentado con distinta instancia/padre).

**Decisión:** si el usuario desmarca una fila del árbol indentado, la pieza (por `Ruta`) se
excluye de TODAS sus ocurrencias en la hoja aplanada, no solo de la instancia desmarcada —
la selección es una decisión a nivel de pieza, no de instancia individual. Alternativa
descartada: recalcular cantidades aplanadas restando solo la cantidad de la instancia
desmarcada (más fiel a "qué marcó el usuario" pero mucho más complejo y menos predecible,
ya que una misma pieza podría quedar parcialmente incluida sin indicación visual clara en la
hoja aplanada).

Implementación: `ItemBomSeleccionable` (`src/UI/ViewModels/ItemBomSeleccionable.cs`) envuelve
el record inmutable `ItemBom` con una propiedad `Incluido` observable. La hoja indentada
exportada usa exactamente las filas marcadas; la hoja aplanada se obtiene de
`ObtenerBomAplanadoAsync` (BOM completo) filtrando fuera las `Ruta` de las filas desmarcadas.

---

## 2026-07-03 — Clave DocManager cifrada con DPAPI; ordenar por Estado; notificaciones no bloqueantes

**Contexto:** seguimiento a las "recomendaciones adicionales" de la sesión de rediseño
visual, con aprobación explícita del usuario para el paquete NuGet nuevo.

**Decisión:**
- `ServicioLicencias` cifra la clave DocManager con DPAPI
  (`System.Security.Cryptography.ProtectedData`, `DataProtectionScope.CurrentUser`) antes de
  guardarla en `ajustes_app`; antes quedaba en texto plano. Al leer, si el valor no descifra
  (formato inválido o guardado en otra máquina/usuario) se trata como texto plano — cubre
  también el caso de valores guardados en la sesión anterior antes de este cambio, sin
  necesitar una migración explícita. Cubierto por 5 tests nuevos (`ServicioLicenciasTests`)
  que verifican el round-trip, que el valor en BD no es el texto plano, el fallback legado,
  ausencia de clave y borrado.
- Columna "Estado" del grid de Archivos (un `DataGridTemplateColumn`) ahora tiene
  `SortMemberPath="EstadoRapido"` para que el clic en el encabezado ordene — las demás
  columnas (`DataGridTextColumn`) ya eran ordenables por defecto sin cambios, WPF lo hace
  solo con columnas simples.
- Nuevo `ServicioNotificaciones` (Singleton, capa UI) envuelve `Wpf.Ui.Controls.Snackbar` +
  `SnackbarPresenter`. `MainWindow` aloja el `SnackbarPresenter` (flotante, esquina inferior
  derecha) y conecta el servicio al arrancar. Reemplaza los `MessageBox.Show(...,
  Information)` de confirmación pura (sin decisión) en `ConfiguracionWindow` — registrar/
  eliminar/ejecutar tarea programada — por toasts no bloqueantes. Los `MessageBox` de
  decisión sí/no y de error se mantienen sin cambios (si requieren que el usuario confirme o
  vea el error, deben bloquear).

**Implicaciones:** `SWDataExtractor.Application.csproj` gana la dependencia
`System.Security.Cryptography.ProtectedData`; `ServicioLicencias` se marca
`[SupportedOSPlatform("windows")]` (DPAPI es exclusivo de Windows, igual que el resto de la
app). Cambiar la clave sigue requiriendo reiniciar la app (sin cambios ahí). Sin cambio de
esquema. Verificado: build limpio, 37/37 tests, arranque de la app sin excepciones.

**Alternativas descartadas:** dejar la clave en texto plano (descartada tras aprobación
explícita del usuario para cifrarla); usar `ISnackbarService`/`IContentDialogService` de
WPF-UI (viven en `WPF-UI.Abstractions`, paquete no evaluado — se optó por un servicio propio
minimalista sobre `Snackbar`/`SnackbarPresenter`, que sí están en el paquete `WPF-UI` ya
aprobado).

---

## 2026-07-03 — Rediseño visual UI: WPF-UI (Fluent) sin NavigationView; sidebar propio

**Contexto:** pedido explícito de rediseño visual fuerte + UX de selección/detalle confusa
(cualquier clic en el grid saltaba a una pestaña "Detalle" aparte, perdiendo de vista la
lista). El usuario eligió la librería **WPF-UI** (paquete NuGet `WPF-UI`, Fluent/Windows 11)
sobre estilos XAML a mano.

**Decisión:**
- `App.xaml` fusiona `ui:ThemesDictionary` + `ui:ControlsDictionary`, lo que reestiliza
  automáticamente todos los controles nativos (Button, DataGrid, TextBox, ComboBox, ListBox,
  PasswordBox) sin tocar cada uno individualmente.
- `MainWindow` pasa a `ui:FluentWindow` con `ui:TitleBar`. La navegación principal (antes un
  `TabControl` de 4 pestañas) se reemplaza por un sidebar propio (`ListBox` estilizado,
  `SelectedIndex` ligado a `MainViewModel.SeccionActiva`) con 3 secciones: Archivos, Cola de
  trabajos, Historial.
- **Se evitó deliberadamente `ui:NavigationView`.** Su región de contenido
  (`NavigationViewContentPresenter`) hereda de `Frame` y está diseñada para navegación
  Página/Frame vía `INavigationService`/`IPageService` — abstracciones que viven en paquetes
  NuGet aparte (`WPF-UI.Abstractions`, `WPF-UI.DependencyInjection`) no aprobados en esta
  sesión, y convertir las 4 `UserControl` existentes a `Page` + implementar esos servicios es
  una superficie de cambio grande que no se pudo probar visualmente (sin acceso a pantalla
  interactiva; solo se pudo verificar que la app arranca sin excepciones vía
  `dotnet run` con timeout). Un sidebar de `ListBox` + `ContentPresenter`s con visibilidad
  condicional (`IndiceAVisibilidadConverter`) da un resultado visualmente similar con control
  total y cero dependencias nuevas de navegación.
- Los íconos siguen siendo emoji Unicode (ya usados en toda la app) en vez de
  `ui:SymbolIcon`/`SymbolRegular`: no se pudo confirmar contra el código fuente de WPF-UI el
  nombre exacto de cada miembro del enum `SymbolRegular` (es un archivo generado, no
  versionado como fuente legible), y un nombre de ícono inventado rompe la compilación XAML.
  Los emoji son código ya probado en este proyecto.
- **Maestro-detalle:** `DetalleArchivoView` deja de ser una pestaña aparte; `ArchivosView` la
  aloja como panel embebido a la derecha del grid (con `GridSplitter`), inyectada por DI en
  el constructor de `ArchivosView`. `MainViewModel` ya no cambia de pestaña al seleccionar una
  fila — solo recarga el panel de detalle in-place. Esto resuelve la confusión reportada
  ("seleccionar para extraer" vs. "seleccionar para ver detalle" ya no son acciones en
  pantallas distintas).
- **Abrir archivo:** cada fila tiene botones 📄 (abre con la app asociada vía
  `Process.Start(UseShellExecute:true)`) y 📂 (abre el Explorador con `/select,`). Doble clic
  en la fila también abre el archivo — patrón estándar de Explorador de Windows.

**Implicaciones:** `SWDataExtractor.UI.csproj` gana la dependencia `WPF-UI`. Ningún cambio de
esquema ni de las capas Core/Application/Data. Verificado: build limpio y arranque de la app
sin excepciones (`dotnet run` con timeout) tras cada cambio — **no se pudo verificar
visualmente el resultado final** (sin pantalla interactiva en esta sesión); pendiente que el
usuario confirme apariencia y dé feedback de ajuste.

**Alternativas descartadas:** MaterialDesignInXaml (el usuario prefirió Fluent/WPF-UI);
`ui:NavigationView` con Page/Frame completo (ver arriba); estilos XAML 100% a mano (el
usuario prefirió una librería para ir más rápido).

---

## 2026-07-03 — Clave DocManager configurable desde la UI (Configuración → Licencias)

**Contexto:** la clave `SwDmLicenseKey` solo se podía configurar por terminal
(`dotnet user-secrets set`). El usuario pidió una pantalla para "seleccionar licencias o
perfiles" ligados a lo que SolidWorks pueda requerir.

**Decisión:** nuevo servicio `ServicioLicencias` (Application) lee/escribe la clave en
`ajustes_app` (clave `DocManagerLicenciaKey`), mismo patrón que `CarpetasEscaneo`. Nueva
pestaña "Licencias" en `ConfiguracionWindow` con un `PasswordBox` (enmascarado) + botones
Guardar/Quitar. En `App.xaml.cs`/`Batch/Program.cs`, los factories de `ExtractorDocManager`/
`EscritorDocManager` leen la clave de la BD primero (vía `sp.CreateScope()` síncrono — seguro
porque los singletons `IExtractorCad` se resuelven recién cuando algo pide
`OrquestadorExtraccion`, después de `InicializarAsync`) y caen a `IConfiguration`
(user-secrets/appsettings) si no hay nada guardado.

**Implicaciones:** la clave queda en texto plano en la BD SQLite local — mismo nivel de
confianza que el resto de `ajustes_app` (carpetas, rol) en una app de un solo usuario en
máquina local; no es un secreto protegido criptográficamente. Cambiar la clave requiere
reiniciar la aplicación (los singletons no se recargan en caliente) — se avisa en la UI.

**Alternativas descartadas:** sistema de "perfiles" múltiples nombrados (el usuario pidió
alcance acotado: solo el campo de clave por ahora); cifrar con DPAPI vía
`System.Security.Cryptography.ProtectedData` (paquete NuGet adicional no evaluado con el
usuario en esta sesión — candidato para una futura mejora si se decide reforzar esto).

---

## 2026-07-03 — SwApi como sustituto liviano de DocManager para alcance Rápida

**Contexto:** sin `SwDmLicenseKey`, `ExtractorDocManager` falla siempre y antes de este
cambio `ExtractorSwApi` estaba registrado primero en DI con `Capacidades = Profunda`; como
`Profunda` incluye todos los bits de `Rapida` (`Profunda = Rapida | Features | Roscas`), la
selección por intersección (`(alcance & caps) != Ninguno`) hacía que SwApi ganara *siempre*,
incluso para `ModoExtraccion.Rapido` — y `ExtraerAsync` no miraba `solicitud.Alcance`, así
que ejecutaba features/roscas/físicas igual de caro que en modo Profundo. En la práctica
"Modo Rápido" no era más rápido que "Modo Profundo" y DocManager quedaba inalcanzable.

**Decisión:**
1. `ExtractorSwApi.ExtraerAsync` ahora respeta `solicitud.Alcance`: solo recorre el árbol de
   features/roscas si el alcance pedido incluye `Features`/`Roscas` (el paso caro), y gatea
   físicas/componentes por sus propios bits.
2. `OrquestadorExtraccion` selecciona candidatos por **superset** de capacidades
   (`(alcance & caps) == alcance`, no solo intersección no vacía) y prueba varios en orden de
   registro: si el primero devuelve `Estado != Ok` (p. ej. DocManager sin licencia), reintenta
   con el siguiente antes de marcar error. Un timeout no reintenta con el siguiente candidato
   (evita duplicar la espera).
3. DI: `DocManager` se registra primero, `SwApi` segundo (`App.xaml.cs`, `Batch/Program.cs`).
   Para alcance `Profunda` esto no cambia nada (solo SwApi cubre ese superset); para `Rapida`
   ahora se intenta DocManager primero y, si falla, SwApi hace una pasada liviana (sin
   features/roscas) como sustituto sin licencia.
4. Efecto colateral corregido: `EstadoProfundo`/`FechaExtrProfunda` nunca se escribían en
   éxito ni en fallo — un archivo con alcance Profunda exitoso se reprocesaba profundamente
   en cada ciclo Auto para siempre, y un fallo puramente profundo degradaba `EstadoRapido` ya
   "ok" a "error" (perdía su estado rápido válido). Ahora éxito/fallo/timeout de alcance
   Profunda actualiza `EstadoProfundo`, no `EstadoRapido`. `ProcesarPendientesAsync` amplía su
   filtro para incluir archivos con Rápida "ok" pero Profunda pendiente/fallida (si no,
   quedaban invisibles para el ciclo Auto).

**Implicaciones:** sin cambio de esquema ni de `IExtractorCad`/`AlcanceExtraccion` (ya traían
los bits necesarios). Sin tests previos sobre `OrquestadorExtraccion` (no había ninguno) —
verificado con build limpio y los 32 tests existentes en verde; falta validar con SW real que
el modo liviano efectivamente reduce el tiempo de extracción Rápida.

**Alternativas descartadas:** implementar un tercer adaptador de solo-propiedades vía OLE
Structured Storage (spike ejecutado el mismo día, descartado — SW moderno no usa contenedor
OLE2, ver sección correspondiente en `docs/AUDITORIA.md`).

---

## 2026-07-03 — Guardar carpetas re-escanea de inmediato y ofrece purgar las quitadas

**Contexto:** Auditoría F6: al agregar/quitar carpetas en `ConfiguracionWindow`, el cambio se
guardaba en `ajustes_app` pero no tenía efecto hasta el siguiente escaneo manual o programado.
Los archivos bajo una carpeta quitada quedaban marcados `omitido` para siempre — la BD acumulaba
registros huérfanos sin que el usuario tuviera forma de limpiarlos desde la UI.

**Decisión:** `GuardarCarpetas_Click` ahora: (1) calcula qué carpetas se quitaron comparando
contra el valor previo en BD; (2) si hay carpetas quitadas, pregunta al usuario si desea eliminar
de la BD (cascada EF ya configurada) los archivos indexados bajo esas rutas; (3) siempre dispara
`EscaneadorCarpetas.EscanearAsync()` inmediatamente tras guardar, para que las carpetas agregadas
queden indexadas sin pasos adicionales. Se agrega botón "Purgar omitidos de la BD" para limpiar
en cualquier momento archivos ya marcados `omitido` (por cualquier causa, no solo remoción de
carpeta). Todo el flujo ocurre desde la UI; no requiere editar `appsettings.json` ni código.

**Implicaciones:** `ConfiguracionWindow` ahora inyecta `EscaneadorCarpetas` (ya registrado como
Scoped en `App.xaml.cs`, se resuelve sin problema porque la ventana se abre dentro de un scope
en `MainWindow.xaml.cs`). Sin cambio de esquema.

**Alternativas descartadas:** Purgar automáticamente sin preguntar (riesgo de pérdida de datos
si el usuario quitó la carpeta por error); dejar el purgado solo para el próximo escaneo
programado (no resuelve el problema de acumulación de huérfanos, motivo original del pedido).

---

## 2026-07-03 — UI referencia SwApi para extracción profunda sin clave DocManager

**Contexto:** Sin clave DocManager, la extracción Rápida falla siempre. El usuario necesita
poder extraer features/roscas/masa desde la UI cuando SW está abierto.

**Decisión:** `SWDataExtractor.UI.csproj` referencia `SWDataExtractor.SwApi`. Se registra
`ExtractorSwApi` (Singleton) ANTES de `ExtractorDocManager` en App.xaml.cs. Se agrega el
comando `ExtraerProfundoAsync` (ModoExtraccion.Profundo) con su botón "Extraer profundo (SW)".

**Implicaciones:** La UI ahora compila solo si los ensamblados de SolidWorks están presentes
(necesidad de SolidWorks instalado para compilar). Se añade botón "▶▶ Extraer profundo (SW)"
en ArchivosView. Bug de selección de extractor corregido: `(alcance & caps) != Ninguno`
(antes `caps.HasFlag(alcance & caps)` pasaba siempre para intersection=0).

**Alternativas descartadas:** Mantener UI solo con DocManager (bloquea testing sin clave).

---

## 2026-07-03 — Reinicio de SW tras timeout: advertencia en log, no kill de proceso

**Contexto:** DISENO.md §2 especifica "matar proceso SLDWORKS.exe, reiniciar SW, continuar"
cuando se vence el timeout por archivo.

**Decisión:** Implementar solo la advertencia en log cuando se alcanza el límite de N archivos
(`_archivosDesdeUltimoReinicio >= _reiniciarCadaN`). No se implementa el kill/reinicio de proceso.

**Razón técnica:** Matar SLDWORKS.exe requiere `Process.Kill()` sobre un proceso de usuario
con posible HWND visible; puede dejar documentos corruptos o locks de archivo. El reinicio
programático también implica esperar a que SW arranque y conectarse nuevamente al ROT — lógica
compleja que necesita validación con un entorno SW real.

**Pendiente:** Implementar en una iteración posterior con acceso a SW y la clave DocManager.
Issue: agregar reinicio real con `Process.GetProcessesByName("SLDWORKS")` + espera de ROT.

---

## 2026-07-03 — Apertura Lightweight no aplicable para extracción de features

**Contexto:** DISENO.md §2 menciona "apertura Silent + Lightweight" en `OpenDoc6`.

**Decisión:** Se usa solo `swOpenDocOptions_Silent` (valor 1). No se combina con Lightweight.

**Razón técnica:** La extracción profunda recorre el árbol de features (`FirstFeature` /
`GetNextFeature`) y accede a `GetDefinition()` de cada feature — operaciones que requieren el
modelo completamente resuelto. Un documento Lightweight tiene sus features sin resolver; acceder
a `GetDefinition` dispararía la resolución igualmente (o fallaría). El DISENO.md mencionó
Lightweight como optimización de apertura, pero no es compatible con la extracción de features.

---

## 2026-07-02 — Framework .NET 10 en lugar de .NET 8

**Contexto:** DISENO.md especificaba `net8.0`, pero el entorno de desarrollo solo tiene instalado
el SDK de .NET 10 (10.0.301). El template `dotnet new classlib -f net8.0` no es válido.

**Decisión:** Usar `net10.0` / `net10.0-windows` como TFM en todos los proyectos.

**Implicaciones:**
- El archivo de solución se generó como `.slnx` (nuevo formato de .NET 10), no `.sln`.
- EF Core y Serilog se resuelven en sus versiones para .NET 10.
- SQLitePCLRaw anclado a 3.50.3 para eliminar vulnerabilidad NU1903 de la versión 2.1.11.
- No hay cambio de arquitectura; la decisión es puramente de plataforma/SDK.

**Alternativas descartadas:** Instalar SDK de .NET 8 en paralelo (no necesario; .NET 10 es LTS-next
y soporta todas las APIs requeridas).

---

## 2026-07-03 — SQL Server opcional vía detección automática de cadena de conexión

**Contexto:** F6 requiere SQL Server como alternativa a SQLite para entornos de empresa.
Las migraciones EF Core son provider-específicas; las migraciones SQLite existentes no corren en SQL Server.

**Decisión:** Detectar el proveedor en `DbContextExtensions.EsSqlServer(cadena)` (busca `Server=` o
`Initial Catalog=`). Para SQLite → `MigrateAsync()`. Para SQL Server → `EnsureCreatedAsync()`.
Migración SQL Server dedicada queda para cuando sea necesaria (`--provider SqlServer`).

**Implicaciones:** Sin cambio de esquema ni de entidades. Agrega paquete `Microsoft.EntityFrameworkCore.SqlServer`.

**Alternativas descartadas:** Duplicar DbContext por proveedor (mantenimiento doble).

---

## 2026-07-03 — Sistema de roles con 3 niveles persistido en ajustes_app

**Contexto:** F6 requiere control de acceso por rol. La tabla `ajustes_app` ya existe en el esquema.

**Decisión:** 3 roles — `Visualizador / Operador / Administrador` — persistidos como string en
`ajustes_app` con clave `"RolActual"`. `ServicioRoles` (Scoped) lee/escribe el rol y mapea a
`ConfiguracionFuncionalidades`. `FuncionalidadesViewModel` (Singleton) refleja los permisos activos
en la UI vía bindings. Al arrancar, la app carga el rol guardado y aplica las funcionalidades.

**Implicaciones:** Sin cambio de esquema. `ConfiguracionWindow` tiene una nueva pestaña "Roles y permisos".
El botón "Editar propiedades" en ArchivosView se oculta cuando `EscrituraPropiedades=false`.

**Alternativas descartadas:**
- Roles en tabla nueva (requiere migración).
- Claims/Identity (sobredimensionado para uso en máquina local).

---

## 2026-07-02 — Proyecto Application como capa de servicios compartida

**Contexto:** CLAUDE.md define "4 capas: UI → Servicios (Core) → Extracción → Datos". Los servicios
de orquestación (EscaneadorCarpetas, OrquestadorExtraccion, ServicioBom, ServicioPropiedades) necesitan
acceso a AppDbContext (Data) y a los contratos (Core). Ponerlos en Core crearía un ciclo (Data → Core
y Core → Data). Ponerlos solo en Batch impediría reutilizarlos desde la UI.

**Decisión:** Crear `src/Application` (net10.0) que referencia Core + Data. Batch y UI referencian
Application. Es la implementación correcta de la capa "Servicios" mencionada en CLAUDE.md.

**Implicaciones:** 8 proyectos en lugar de 7. Ningún cambio de esquema ni de contratos.

**Alternativas descartadas:**
- Servicios en Batch (Worker Service no referenciable desde UI).
- Servicios en Core con Core→Data (ciclo de dependencias).
- Duplicar servicios en Batch y UI (mantenimiento imposible).

---

## 2026-07-05 — ExtractorStep: extracción de .stp/.step sin SolidWorks (encabezado ISO-10303-21)

**Contexto:** Los .stp/.step siempre terminaban en "error": DocManager no los soporta (solo
formatos nativos SW) y SwApi los intentaba importar con `OpenDoc6`, que según la documentación
de SolidWorks solo abre documentos nativos (la importación real requiere `LoadFile4`). Además,
importar un STEP en SW es lento y casi no aporta datos (sin propiedades custom ni features).

**Decisión:** Tercer `IExtractorCad` — `ExtractorStep` en `src/Application/Servicios` (sin
dependencia de SW, como el resto de Application). Lee el encabezado de texto ISO-10303-21 del
propio archivo (FILE_NAME, FILE_DESCRIPTION, FILE_SCHEMA) y publica los datos como propiedades
a nivel documento con prefijo `STEP_` (nombre, fecha, autor, organización, preprocesador,
sistema de origen, esquema AP203/AP214/AP242). Capacidades = `Rapida` (mismo patrón que SwApi
con `Profunda`: declara qué alcances atiende, no que devuelva todos los datos). Registrado en
DI entre DocManager y SwApi, para que en modo Rápido gane sin SW; SwApi queda de respaldo y
sigue siendo el único candidato para alcance Profunda. En modo Auto, un STEP con Rápida "ok"
ya no pide Profunda (no tiene árbol de features nativo — evitaba reintentos eternos contra SW).
Default de `ExtensionesIncluidas` alineado con appsettings.json (agrega .stp/.step).

**Implicaciones:** Sin cambio de esquema (las propiedades van a la tabla `propiedades`
existente, nivel documento). Cumple "instala y funciona": STEP extraíble sin SW ni licencia.
`arquitectura.dot` actualizado (SVG pendiente de Graphviz). Tests: 66/66 (9 unitarios del
parser + 2 de integración orquestador→BD).

**Alternativas descartadas:**
- Corregir la importación en SwApi con `LoadFile4` (requiere SW abierto, lento, casi sin datos;
  contradice "instala y funciona"). Se conserva el camino SwApi existente solo como respaldo.
- Guardar los datos en `datos_extra_json` (son propiedades del documento: encajan en la tabla
  `propiedades` y así se ven en el detalle y en el export Excel sin código nuevo).

---

## 2026-07-05 — BOM interno de STEP en datos_extra_json (no en tabla componentes)

**Contexto:** Un STEP de ensamble trae su estructura completa en la sección DATA
(`NEXT_ASSEMBLY_USAGE_OCCURRENCE` → `PRODUCT_DEFINITION` → `PRODUCT`). Se puede extraer sin
SW, pero sus componentes viven DENTRO del archivo: no existen como archivos propios.

**Decisión:** `ExtractorStep` parsea la sección DATA (streaming, sentencia por sentencia,
tolera comentarios `/*…*/` y sentencias malformadas) y guarda el árbol en
`archivos.datos_extra_json` bajo la clave `"bom_step"` (raíz, total de ocurrencias, árbol
nombre/cantidad/hijos), según DISENO §1b (datos nuevos → datos_extra_json primero). Cantidad
= nº de NAUO del mismo par padre/hijo. Se agrega `DatosExtraJson` opcional a
`ResultadoExtraccion` y su persistencia (solo si no es null). La pestaña BOM del detalle
renderiza este árbol para archivos tipo "step" (filas de solo lectura, ruta "(interno del
STEP)"). Propiedad `STEP_Componentes` con el total. El tipo en BD sigue siendo "step".

**Implicaciones:** Sin cambio de esquema ni de la tabla `componentes` — los reportes de
referencias rotas y where-used no se contaminan con componentes que no son archivos.
Verificado contra 4 STEP reales (SolidWorks 2012–2018 y Alibre/ST-DEVELOPER, AP203 y AP214,
hasta 75 ocurrencias, < 110 ms cada uno). Lo que un STEP NO permite llenar: configuraciones
(no existen en el formato), features/roscas (solo geometría B-rep, sin árbol de operaciones)
y físicas fiables (masa/material requerirían un kernel geométrico).

**Alternativas descartadas:**
- Insertar en `componentes` con rutas sintéticas (rompería referencias rotas/where-used).
- Columna nueva con flag "componente virtual" (migración de esquema para un caso aislado).
