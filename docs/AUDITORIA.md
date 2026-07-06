# PROTOCOLO DE AUDITORÍA — SWDataExtractor

## Estrategia de modelos

- **Trabajo diario:** Claude Sonnet 4.6 (implementación, fixes, docs). Es el modelo por defecto.
- **Auditorías:** modelo más avanzado disponible (Claude Opus 4.8 hoy; el que exista mañana).
  Cambiar con `/model` solo para la sesión de auditoría y regresar a Sonnet al terminar.

## Cuándo auditar

- Al cerrar cada fase (obligatorio, antes del tag `fase-N`).
- Cada ~2 semanas de trabajo continuo si una fase se alarga.
- Antes de decisiones grandes (migrar a SQL Server, empezar la UI, multiusuario).

## Cómo auditar

1. Sesión limpia de Claude Code, `/model` → modelo avanzado.
2. Ejecutar `/auditoria`.
3. El auditor SOLO diagnostica (no cambia código) y escribe su reporte abajo en este archivo,
   en una sección nueva `## Auditoría YYYY-MM-DD — Fase N`, con hallazgos priorizados:
   - 🔴 Crítico: corregir antes de continuar la siguiente fase.
   - 🟡 Recomendado: agendar en pendientes de ESTADO.md.
   - ⚪ Opcional: registrar y decidir después.
4. Commit del reporte: `docs: auditoría fase N`.
5. Los arreglos se ejecutan DESPUÉS, en sesiones normales con Sonnet, tomando los ítems
   del reporte uno por uno (esto mantiene barata la corrección: el modelo caro piensa,
   el modelo eficiente ejecuta).

## Auditoría de eficiencia de tokens (parte del protocolo)

El auditor debe revisar y podar el "peso muerto" de contexto:
- CLAUDE.md: eliminar reglas ya internalizadas en el código o que ya no aplican; debe
  mantenerse por debajo de ~1.5 páginas.
- ESTADO.md: comprimir historial a 3-5 líneas; el detalle vive en git log.
- DECISIONES.md: fusionar decisiones supersedidas.
- Diagramas .dot desactualizados = contexto que engaña; corregirlos es prioridad 🔴.

## Historial de auditorías

(las secciones se agregan abajo, la más reciente primero)

---

## Auditoría 2026-07-06 — v1.1.0, pre-publicación a GitHub

Alcance: todo lo agregado desde la auditoría 2026-07-03 (ExtractorStep con BOM interno,
agrupación por carpeta, fix de recarga del detalle, release v1.1.0) + revisión de qué es
seguro subir al repositorio remoto. Solo diagnóstico; sin cambios de código.

### 🔴 Crítico

1. **`arquitectura.svg` desactualizado respecto a su `.dot`** — el `.dot` se actualizó el
   2026-07-05 (nodo ExtractorStep) pero el `.svg` es del 2026-07-02: quien mire el SVG en
   GitHub verá una arquitectura de 2 extractores que ya no es real. Graphviz sigue sin estar
   en esta máquina (`dot` no está en PATH). Acción: instalar Graphviz y
   `dot -Tsvg docs/diagramas/arquitectura.dot -o docs/diagramas/arquitectura.svg` antes del
   primer push, o subir solo señalando que el `.dot` es la fuente de verdad.

2. **Nada más crítico.** Capas intactas: interop de SolidWorks solo en `DocManager` y
   `SwApi` (1 referencia cada uno); `ExtractorStep` vive en Application y no depende de SW.
   Sin claves ni secretos en código o appsettings (`LicenciaKey` vacío, clave real cifrada
   con DPAPI en BD); `CarpetasRaiz` sigue vacío.

### 🟡 Recomendado

3. **Datos obsoletos en la grilla por `DbContext` compartido con tracking** — hallazgo
   derivado del bug del detalle (arreglado el 2026-07-05 re-notificando la selección): las
   extracciones lanzadas desde el Explorador usan un scope/DbContext PROPIO
   (`MainWindow.xaml.cs` crea scopes), pero `ArchivosViewModel` recarga con un contexto
   de vida larga con tracking — EF devuelve las instancias rastreadas con valores VIEJOS
   (el identity map no sobreescribe). Síntoma esperable: estados que no se refrescan en la
   pestaña Archivos tras extraer desde el Explorador, hasta reiniciar. Reproducir y, si se
   confirma, usar `AsNoTracking()` en la consulta de la grilla (o `ChangeTracker.Clear()`
   antes de recargar). No se tocó por estar fuera del alcance del pedido de hoy.

4. **`DECISIONES.md` llegó a 23 entradas** (umbral de fusión era ~15, auditoría anterior
   ítem 10). Sigue siendo útil pero ya cuesta ~840 líneas de contexto. Fusionar las
   supersedidas (p. ej. las 2 de carpetas de escaneo 2026-07-03 pueden resumirse en una).

5. **`LINEAMIENTOS.md` quedó obsoleto** — dice ".NET 8" (el proyecto es net10.0), "Graphviz
   (ya lo tienes)" (no está instalado) y es un checklist de arranque ya cumplido. Moverlo a
   `docs/` como histórico o actualizarlo; hoy es contexto que engaña en la raíz del repo.

6. **Vigente de la auditoría anterior (sin cambios porque SwApi/DocManager no se tocaron):**
   timeout no interrumpe llamadas COM colgadas (ítem 3), `ComScope` muerto (ítem 4),
   14 marcadores `VERIFICAR-API` sin validar (11 en SwApi, 3 en DocManager),
   `ExtraerAsync` SwApi ~145 líneas y `PersistirResultadoAsync` ~115 líneas.
   `DetalleArchivoViewModel.CargarArchivoAsync` creció a ~100 líneas con el BOM de STEP —
   candidato a dividir cuando se toque de nuevo.

### ⚪ Opcional

7. **Nombres con codificación rara en STEP antiguos** — nombres de producto con bytes fuera
   de ASCII sin escape `\X2\` (p. ej. "Peça" exportado como byte 0x87 de CP850 en el SG90)
   se muestran con un carácter extraño. La norma no cubre estos exportadores; decodificar
   por heurística de codepage es frágil. Vivir con ello salvo que moleste en producción.

8. **`ItemBomSeleccionable` con checkbox "Incluir" también aparece en el BOM de STEP**,
   donde exportar BOM a Excel no aplica (el comando sale temprano si `Tipo != "ensamble"`).
   Cosmético: ocultar la columna o habilitar el export para STEP.

### Revisión "qué subir al repositorio" (pedido explícito de esta sesión)

- **Excluir (ya cubierto por `.gitignore`, verificado):** `bin/`, `obj/`, `*.db*` (swdata.db
  raíz + src/Batch + wal/shm — contienen rutas personales y metadatos de archivos del
  usuario), `logs/`, `dist/` (los ZIP pesan 128 MB y **exceden el límite duro de 100 MB por
  archivo de GitHub** — el push fallaría), `*.user`, `secrets.json`, `.vs/`, `.idea/`.
- **Incluir:** `src/`, `tests/`, `docs/` (incluidos `.dot`/`.svg`), `herramientas/`,
  `CLAUDE.md`, `.claude/` (settings de permisos y comandos — sin secretos), `.gitignore`,
  `SWDataExtractor.slnx`, README nuevo.
- **Aviso consciente:** los docs (`ESTADO/DECISIONES/AUDITORIA`) mencionan rutas locales
  tipo `C:\Users\dany_\Downloads\...` como evidencia de pruebas. En un repo público
  cualquiera las verá; si molesta, anonimizarlas antes del push (buscar `dany_` en docs/).
- Los interop de SolidWorks NO están en el repo (se referencian por HintPath local) —
  correcto: son DLLs propietarias de Dassault y no deben redistribuirse por git.

### Verificaciones sin hallazgos

- Sin `TODO`/`FIXME` en `src/`. Sin encadenamientos COM nuevos (SwApi/DocManager sin tocar).
- Errores por archivo: `ExtractorStep` sigue la regla (error → resultado, nunca excepción
  al lote; sentencias DATA malformadas se saltan una a una).
- CLAUDE.md (~90 líneas) y ESTADO.md (~72 líneas) dentro de los límites del protocolo.
- Tests 70/70; los nuevos cubren parser de encabezado, comentarios `/*…*/`, estructura
  NAUO, y la integración orquestador→BD de STEP (incluye `datos_extra_json`).

---

## Auditoría 2026-07-03 — Post-F6

**Nota de proceso:** el usuario pidió explícitamente en esta sesión corregir el flujo de
carpetas de escaneo y aplicar todos los arreglos necesarios, no solo diagnosticar. Se
desvía del protocolo estándar (que reserva los arreglos para sesiones Sonnet posteriores)
por instrucción directa. Los cambios aplicados están marcados **[APLICADO]**; el resto queda
como diagnóstico para sesiones futuras.

### 🔴 Crítico

1. **Fuga de referencia COM de `swApp` en cada extracción** — `src/SwApi/ExtractorSwApi.cs`
   `ObtenerSwApp()` crea una RCW nueva vía ROT (`GetActiveObject`) en cada llamada a
   `ExtraerAsync`, pero el `finally` solo liberaba `doc`, nunca `swApp`. En un lote de miles
   de archivos esto acumula referencias COM sin liberar sobre el proceso de SolidWorks.
   **[APLICADO]** — se agregó `Marshal.ReleaseComObject(swApp)` al `finally`.

2. **Carpetas quitadas de la lista de escaneo no se limpiaban de la BD** — al quitar una
   carpeta en `ConfiguracionWindow`, sus archivos quedaban marcados `omitido` para siempre
   (el próximo escaneo los detecta como "no en disco" pero nunca se borran); no había forma
   desde la UI de purgarlos, y guardar la lista de carpetas no tenía efecto hasta el
   siguiente escaneo manual o programado. **[APLICADO]** — `GuardarCarpetas_Click` ahora
   compara contra el valor previo en BD, ofrece purgar (borrado en cascada) los archivos de
   las carpetas quitadas, y dispara un `EscanearAsync()` inmediato para que las carpetas
   agregadas queden indexadas sin pasos adicionales. Se agregó botón "Purgar omitidos de la
   BD" para limpieza manual en cualquier momento. Ver `docs/DECISIONES.md` 2026-07-03.

3. **Timeout por archivo no interrumpe una llamada COM bloqueada** — en
   `OrquestadorExtraccion` el timeout se implementa con `CancellationTokenSource.CancelAfter`,
   pero `ExtractorSwApi.ExtraerAsync` solo verifica `ct.ThrowIfCancellationRequested()` entre
   pasos; una llamada bloqueante como `OpenDoc6` (líneas 80/84/95) sobre un archivo corrupto
   que SolidWorks no logra abrir puede colgarse indefinidamente sin que el timeout la corte —
   justo el escenario que el timeout existe para cubrir ("sobrevive carpeta tortura", F2).
   **No aplicado**: requiere ejecutar la llamada COM en un thread aparte con abort forzado
   (`Thread.Interrupt`/kill de proceso), que es un cambio delicado de validar sin una
   instancia real de SolidWorks con archivos que reproduzcan el cuelgue. Recomendado como
   primer punto a probar con acceso a SW + clave DocManager (ver también la decisión
   2026-07-03 sobre reinicio de SW ya diferida por el mismo motivo).

### 🟡 Recomendado

4. **`ComScope` es código muerto** — `src/SwApi/ComScope.cs` define un wrapper
   `IDisposable` para liberar objetos COM en orden inverso, pero no se usa en ningún lado;
   `ExtractorSwApi.cs` y `ExtractorDocManager.cs` repiten manualmente el patrón
   `try/finally` + `Marshal.ReleaseComObject` unas 15+ veces. Adoptarlo eliminaría la
   duplicación, pero es un refactor mecánico sobre lógica COM sensible al orden de
   liberación — mejor hacerlo en una sesión dedicada con acceso a SW para probar cada
   método tocado, no a ciegas.

5. **Faltaban marcadores `VERIFICAR-API` en DocManager** — `ExtractorDocManager.cs` usaba
   firmas no confirmadas (`GetDocument`, `GetPreviewPNGBitmap`, `GetComponents`) sin la
   marca que exige la regla 5 de CLAUDE.md, a diferencia de `ExtractorSwApi.cs` que sí las
   documenta (10 marcadores ahí). **[APLICADO]** — se agregaron los 3 marcadores faltantes.
   Los 10 existentes en `ExtractorSwApi.cs` siguen pendientes de validar contra
   help.solidworks.com (líneas 75, 275, 287, 291, 352, 356, 364, 395, 412, 416).

6. **`ConfiguracionWindow.xaml.cs` mezclaba lógica de negocio con code-behind de UI** —
   el borrado en cascada por prefijo de carpeta y la purga de omitidos hacían consultas
   EF Core directas en la ventana en vez de vivir en la capa Application, violando el
   patrón ya establecido (CRUD simple sí, reglas de negocio no). **[APLICADO]** — se movió
   todo a `EscaneadorCarpetas` (`ObtenerCarpetasGuardadasAsync`, `GuardarCarpetasAsync`,
   `BorrarArchivosBajoCarpetasAsync`, `PurgarOmitidosAsync`); la ventana solo recolecta
   input y confirma con el usuario.

7. **Ruta personal hardcodeada en `appsettings.json` versionado** — tanto
   `src/Batch/appsettings.json` como `src/UI/appsettings.json` tenían
   `C:\Users\dany_\Downloads\haas-5c-indexer-4-spindle-1.snapshot.1` como `CarpetasRaiz`.
   Es solo un fallback (la BD manda), pero es una ruta local de una máquina específica
   filtrada al control de versiones. **[APLICADO]** — se vació a `[]` en ambos archivos.

8. **No existía `.gitignore` ni repositorio git inicializado** — el directorio de trabajo
   no tiene `.git` (confirmado por el entorno de esta sesión). `bin/`, `obj/`, `*.db`
   (`swdata.db` en raíz, `src/Batch/swdata.db`, `src/Data/swdata_design.db`) y
   `src/Batch/logs/*.log` están presentes en el árbol sin nada que los excluya del primer
   commit. **[APLICADO]** — se creó `.gitignore` en la raíz excluyendo `bin/`, `obj/`,
   `*.db*`, `logs/`, `*.user`, `secrets.json`. Pendiente: `git init` cuando el usuario lo
   decida (no se ejecuta automáticamente).

9. **`OrquestadorExtraccion.PersistirResultadoAsync` (~110 líneas) y
   `ExtractorSwApi.ExtraerAsync` (~145 líneas)** hacen demasiado en un solo método
   (configuraciones + propiedades + físicas + componentes + features todo inline). No se
   tocó en esta sesión: son los métodos centrales de persistencia/extracción, con
   comportamiento validado por los 32 tests — dividirlos sin una sesión dedicada de
   revisión arriesga regresiones silenciosas en un área crítica.

### ⚪ Opcional

10. **`DECISIONES.md` crecerá con el tiempo** — hoy tiene 8 entradas, todavía manejable.
    Ninguna está claramente supersedida todavía; revisar de nuevo cuando pase de ~15
    entradas y fusionar las que ya no aporten contexto activo.

11. **CLAUDE.md y ESTADO.md siguen dentro de los límites del protocolo** (≈1.5 y ≈1 página
    respectivamente) — sin poda necesaria por ahora.

### Verificación de capas y robustez COM (sin hallazgos nuevos)

- Las 4 capas se respetan: `Core`/`Data` no referencian DLLs de SolidWorks; solo
  `DocManager` y `SwApi` lo hacen. `UI` referencia `SwApi`/`DocManager` únicamente en el
  composition root (`App.xaml.cs`), consistente con `Batch/Program.cs`.
  `docs/diagramas/arquitectura.dot` y `esquema-bd.dot` siguen reflejando la realidad —
  no requieren regenerarse.
- No se encontraron encadenamientos COM prohibidos (`x.GetY().GetZ()` en una línea): todos
  los call sites ya usan variables intermedias.
  El manejo de errores por archivo es correcto en `OrquestadorExtraccion`,
  `ExtractorSwApi` y `EscritorDocManager` — un archivo con error nunca aborta el lote.
- No se encontraron `TODO`/`FIXME` sueltos en `src/` (solo los `VERIFICAR-API` ya listados).

### Recomendaciones para compensar la falta de licencia DocManager

Sin `SwDmLicenseKey`, el modo Rápido (`ExtractorDocManager`) falla siempre y el único
camino funcional es `SwApi`, que exige SolidWorks abierto — no hay extracción realmente
"ligera" hoy. Opciones evaluadas, de mayor a menor impacto:

- **❌ Lectura directa de propiedades vía OLE Structured Storage — spike ejecutado, resultado
  negativo, NO implementado.** La hipótesis era que `.sldprt`/`.sldasm` fueran documentos
  compuestos OLE (como los `.doc`/`.xls` viejos de Office), leíbles con `OpenMcdf` sin SW ni
  licencia. Se validó empíricamente contra 6 archivos reales del usuario
  (`adjustable-laser-mount-1.snapshot.2\*.SLDPRT/.SLDASM`, encontrados ya indexados en
  `swdata.db`): **ninguno** tiene la firma OLE2 (`D0 CF 11 E0 A1 B1 1A E1`); los primeros
  bytes varían por archivo (hash/checksum) seguidos de un patrón fijo `00 00 00 04`, y no
  aparecen strings como `SummaryInformation`/`CustomProperty`/`PropertySet` en ningún punto
  del binario. Confirma lo que reportan fuentes de terceros: SolidWorks abandonó el
  contenedor OLE2 en versiones modernas (~2015+) por un formato binario propietario no
  documentado. **Conclusión: este camino no es viable** para las versiones de SW que
  produjeron los archivos de prueba del usuario — no se agregó el paquete NuGet ni se
  escribió el adaptador. Intentar leer el formato actual requeriría ingeniería inversa de
  un binario propietario no documentado, fuera del alcance razonable (viola el espíritu de
  la regla 5 de CLAUDE.md: no inventar sin poder verificar).
- **✅ "Modo Rápido vía SwApi" — implementado** (ver `docs/DECISIONES.md` 2026-07-03).
  `ExtractorSwApi` ahora respeta `solicitud.Alcance` y omite features/roscas cuando no se
  piden; `OrquestadorExtraccion` prueba DocManager primero para alcance Rápida y cae a SwApi
  (liviano) si falla por falta de licencia. Sigue exigiendo SW abierto, pero ya no requiere
  la licencia DocManager para tener una extracción "rápida" que de verdad sea más liviana
  que la profunda. Pendiente: validar con SW real que el tiempo baja como se espera.
- **⚪ Mantener una instancia de SW residente** (ya implícito hoy: se reutiliza vía ROT)
  — evaluar si conviene que `Batch` lance y mantenga SW abierto entre ciclos programados
  en vez de depender de que el usuario lo tenga abierto manualmente; reduce fricción del
  modo Profundo sin resolver el problema de fondo (sigue sin haber modo verdaderamente
  ligero sin SW).

### Resumen de cambios aplicados esta sesión

- `src/SwApi/ExtractorSwApi.cs` — liberar `swApp` en el `finally` de `ExtraerAsync`.
- `src/DocManager/ExtractorDocManager.cs` — 3 marcadores `VERIFICAR-API` agregados.
- `src/Application/Servicios/EscaneadorCarpetas.cs` — nuevos métodos
  `ObtenerCarpetasGuardadasAsync`, `GuardarCarpetasAsync`, `BorrarArchivosBajoCarpetasAsync`,
  `PurgarOmitidosAsync`.
- `src/UI/Views/ConfiguracionWindow.xaml.cs` / `.xaml` — guardar carpetas ahora
  purga/re-escanea automáticamente; nuevo botón "Purgar omitidos de la BD".
- `src/Batch/appsettings.json`, `src/UI/appsettings.json` — `CarpetasRaiz` vaciado (ya no
  hardcodea una ruta personal).
- `.gitignore` — creado en la raíz.
- `src/SwApi/ExtractorSwApi.cs` — respeta `solicitud.Alcance` (omite features/roscas/físicas/
  componentes si no se piden); habilita el modo liviano.
- `src/Application/Servicios/OrquestadorExtraccion.cs` — selección de extractor por superset
  de capacidades + reintento con el siguiente candidato; `EstadoProfundo`/`FechaExtrProfunda`
  ahora se escriben en éxito/fallo/timeout sin degradar `EstadoRapido`; `ProcesarPendientesAsync`
  incluye archivos con Profunda pendiente.
- `src/UI/App.xaml.cs`, `src/Batch/Program.cs` — DocManager registrado antes que SwApi.
- `docs/DECISIONES.md` — 2 nuevas entradas 2026-07-03 (guardar carpetas + purga; SwApi como
  sustituto liviano de DocManager).
- Verificado: build de la solución sin errores/advertencias, 32/32 tests en verde tras
  cada cambio. Sin cobertura de tests sobre `OrquestadorExtraccion` (no existía antes) —
  recomendado agregar tests unitarios para la selección/fallback de extractores en una
  próxima sesión, con un `IExtractorCad` de prueba (fake) que simule éxito/error/timeout.
