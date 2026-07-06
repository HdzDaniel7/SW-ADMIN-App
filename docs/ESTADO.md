# ESTADO DEL PROYECTO — SWDataExtractor

> Documento vivo. Claude lo lee al iniciar cada sesión y lo actualiza al terminar.
> Formato: mantener las 5 secciones. Máximo ~1 página; lo histórico se resume, no se acumula.

## Fase actual

**v1.1.0 — F7a completa + todas las recomendaciones + paquete distribuible + Explorador como
centro de operaciones + extracción STEP sin SW (propiedades + BOM interno).** Build 0
errores, 70/70 tests. Paquete v1.1.0 publicado en dist/ (2026-07-05).

## Completado

- **F0–F7a**: extracción dual (DocManager con licencia / SwApi, con modo Rápido liviano de
  respaldo), BOM con selección de filas y export, escritura de propiedades con auditoría,
  roles, tarea programada, UI WPF-UI con temas Plano técnico/Consola verificados en pantalla,
  Explorador (carpetas, duplicados con espacio recuperable, referencias rotas, posibles
  versiones, cumplimiento del diccionario, dashboard por proyecto) — todo sobre BD, sin SW.

- **Tanda "todas las recomendaciones" (2026-07-04, `DECISIONES.md`)**: WAL en SQLite,
  instancia única, excepciones globales + log de UI a archivo, fechas/tamaños legibles,
  tooltip con causa de error, menú contextual, ventana recordada, **extracción por lote con
  progreso/cancelación/toast**, export de reportes a Excel, doble clic en reportes → detalle,
  reporte de cumplimiento, miniaturas vía shell sin licencia, versión real en Acerca de.

- **Paquete distribuible v1.0.0**: `herramientas/publicar.ps1` genera carpeta portable + ZIP
  (~123 MB) con UI y Batch self-contained single-file (no requieren .NET instalado), ícono
  propio, `LEEME-INSTALACION.txt` (incluye guía de BD compartida SQL Server) y creación de
  acceso directo al escritorio (menú Herramientas o script incluido). Smoke test real del exe
  publicado: arranca con BD nueva junto al exe, WAL activo, migraciones OK.

- **Explorador como centro de operaciones (2026-07-04, feedback de uso real)**: gestión de
  carpetas (agregar/quitar raíces con opción de purga/purgar omitidos) movida a la pestaña
  Carpetas; extracción por lote movida al Explorador con "▶ Extraer esta carpeta" (funciona
  también en subcarpetas — filtro por prefijo de ruta) y "⏬ Extraer todos", ambas
  secuenciales abrir→extraer→cerrar (COM de SW es STA; decisión documentada), con progreso
  x/y, cancelación y toast. Detalle de ensambles: checkbox "Incluir componentes del BOM"
  agrega los datos de todas las piezas en Propiedades/Configuraciones/Features/Físicas/Roscas
  con columna "Componente", heredado por el Excel. **Miniaturas del shell confirmadas
  visualmente** (thumbnail del ensamble visible sin licencia ni SW). NOTA: el paquete dist/
  es anterior a estos cambios — re-ejecutar `herramientas/publicar.ps1` antes de distribuir.

- **Extracción STEP sin SolidWorks (2026-07-05, `DECISIONES.md`)**: nuevo `ExtractorStep`
  (Application) lee el encabezado ISO-10303-21 de .stp/.step → propiedades `STEP_*` (nombre,
  autor, organización, sistema origen, esquema AP) sin SW ni licencia; los .step ya no quedan
  en "error". Modo Auto ya no reintenta Profunda en STEP. **BOM interno de STEP** (2026-07-05,
  `DECISIONES.md`): la sección DATA (NAUO/PRODUCT) se parsea sin SW → árbol en
  `datos_extra_json` clave "bom_step", visible en la pestaña BOM del detalle; propiedad
  `STEP_Componentes`; verificado con 4 STEP reales (SW 2012–2018, Alibre; AP203/AP214).
  Configuraciones/features/físicas NO existen en el formato STEP (imposible llenarlas).
  Columna "Abrir" con ancho fijo. Toggle "🗂 Agrupar por carpeta" (ON por defecto). FIX:
  el detalle no se recargaba tras extraer (instancia rastreada no disparaba PropertyChanged).
  Versión 1.1.0 (UI y Batch) publicada en `dist/SWDataExtractor-v1.1.0-win-x64.zip` — pasos
  de `publicar.ps1` ejecutados con `dotnet publish` + zip directo (PowerShell bloqueado en
  la sesión); el resultado es equivalente.

- **Preparación para GitHub (2026-07-06)**: auditoría v1.1.0 registrada en `AUDITORIA.md`
  (1 crítico: regenerar `arquitectura.svg`; hallazgo nuevo: posible dato obsoleto en grilla
  por DbContext con tracking al extraer desde Explorador). `README.md` completo creado
  (características, arquitectura, instalación, consejos; espera 4 capturas en
  `docs/capturas/` — ver su LEEME.txt). `.gitignore` reforzado (dist/ fuera: ZIPs > límite
  100 MB de GitHub; BDs y logs fuera). `LINEAMIENTOS.md` movido a `docs/` (histórico).
  Remoto destino: https://github.com/HdzDaniel7/SW-ADMIN-App.git — commit/push los hace el
  usuario.

## Pendientes

- [ ] **Probar el ZIP en otro equipo real** (el smoke test corrió en la máquina de desarrollo).
- [ ] **git init + primer push** — lo ejecuta el usuario (comandos en el cierre de sesión
  2026-07-06); antes: tomar las 4 capturas de `docs/capturas/LEEME.txt`.
- [ ] Regenerar `docs/diagramas/arquitectura.svg` (🔴 auditoría 2026-07-06: el SVG no
  muestra ExtractorStep; falta Graphviz).
- [ ] Validar con SW real: lote completo, modo Rápido vía SwApi, timeout COM colgado,
  detección real de Toolbox (hoy heurística por ruta).
- [ ] Regenerar `docs/diagramas/arquitectura.svg` (falta Graphviz en esta máquina).
- [ ] Fase 7b/7c (copiar/mover con aviso de referencias; Pack and Go) — NO iniciar sin acuerdo.

## Siguiente paso recomendado

Tomar las 4 capturas de `docs/capturas/LEEME.txt` (de paso valida los .step re-extraídos y
la agrupación por carpeta) y hacer el primer push a
https://github.com/HdzDaniel7/SW-ADMIN-App.git con los comandos del cierre de sesión.
Después: probar el ZIP v1.1.0 en otro equipo (pendiente que viene de v1.0.0).
