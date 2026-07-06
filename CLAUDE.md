# CLAUDE.md — SWDataExtractor

Herramienta C#/.NET 10 que extrae datos de archivos SolidWorks (piezas, ensambles) a una BD
SQLite consultable: propiedades, features (roscas, chaflanes), estructura de ensambles, BOM.
También escribe propiedades por lotes con auditoría, y funciona como administrador de
proyectos SolidWorks (explorador de carpetas, duplicados, referencias rotas, dashboard de
proyecto) sobre los datos ya extraídos. Meta: uso a nivel empresa.

**Principio de diseño — "instala y funciona":** minimizar fricción de licencias/credenciales
SolidWorks. Toda funcionalidad nueva debe funcionar sobre datos ya extraídos en la BD (sin
SW abierto ni licencia DocManager) salvo que sea técnicamente imposible; si depende de SW,
debe quedar explícitamente marcada y aislada como opcional, nunca como requisito del flujo
principal. Debe funcionar igual con distintas versiones de SolidWorks instaladas.

## Fuente de verdad del diseño

El esquema de BD, interfaces, DTOs y criterios de aceptación están DEFINIDOS en
`docs/DISENO.md`. Implementar tal cual; ante ambigüedad o imposibilidad técnica,
preguntar y registrar en DECISIONES.md. No proponer rediseños.

## Arquitectura (fija — no cambiar sin aprobación del usuario)

- 4 capas: UI (WPF) → Servicios (Core) → Extracción → Datos (EF Core + SQLite, SQL portable a SQL Server).
- Extracción dual tras la interfaz `IExtractorCad`:
  - `DocManager`: Document Manager API. Rápida, sin abrir SW. Propiedades, configuraciones,
    estructura de ensambles, previews, escritura de propiedades.
  - `SwApi`: API completa COM. Requiere SW abierto. Features, roscas Hole Wizard, chaflanes, material.
  - Modos de usuario: Rápido / Profundo / Auto (DocManager siempre; SwApi solo en nuevos/modificados por hash SHA256).
- Logging: Serilog. Excel: ClosedXML. Diagramas: Graphviz en `docs/diagramas/*.dot`.
- Extensibilidad (DISENO.md §1b): datos nuevos → `datos_extra_json` primero; funcionalidad
  nueva post-F2 → flag en `Funcionalidades` ocultable en UI; migraciones EF siempre aditivas.

## Solución

```
src/ Core | Data | DocManager | SwApi | Batch | UI     tests/ Tests
docs/ ESTADO.md (vivo) | DECISIONES.md | AUDITORIA.md | diagramas/
```

## Fases (trabajar solo en la fase actual según ESTADO.md)

0. Cimientos: solución, Serilog, esquema EF + migración, diagramas. Salida: build + `ef database update` OK.
1. DocManager: escaneo recursivo → BD, incremental por hash+fecha. Salida: carpeta de prueba indexada con reporte de errores.
2. SwApi: features/roscas/chaflanes + robustez (timeout por archivo, reinicio de SW, apertura silenciosa, liberación COM). Salida: sobrevive carpeta tortura.
3. BOM: indentado/aplanado desde tabla componentes, tornillería Toolbox, where-used, diff de BOM.
4. Escritura de propiedades: diccionario estándar en BD, edición por lotes con diff previo, historial de auditoría.
5. UI WPF: grilla filtrable, detalle con preview, cola de trabajos, exportar Excel.
6. Empresa: escaneo programado, SQL Server opcional, roles.
7. Explorador y gestión de proyectos (ver DISENO.md §6). Sin esquema nuevo, sin requerir SW:
   - 7a. Explorador de carpetas/archivos, reporte de duplicados exactos (hash), reporte de
     referencias rotas (deuda pendiente desde el criterio de aceptación F1), reporte de
     posibles versiones del mismo archivo, dashboard de proyecto (ensamble + BOM + salud).
   - 7b (futura, no iniciar sin acuerdo explícito): copiar/mover archivos con aviso de
     referencias (usa la tabla `componentes` ya existente); actualizar `ruta` por hash en vez
     de re-extraer al detectar que un archivo se movió.
   - 7c (futura, opcional, requiere SW abierto — evaluar si de verdad hace falta dado el
     principio "instala y funciona"): Pack and Go real vía SwApi para reescribir referencias
     al mover un proyecto completo.

## Reglas (obligatorias, sin excepciones)

1. **Commits: NUNCA los ejecutas** (bloqueado por permisos). Al cerrar cada unidad lógica,
   sugiere: archivos + mensaje Conventional Commits en español (`feat:`, `fix:`, `docs:`...).
2. **ESTADO.md**: léelo al iniciar sesión; actualízalo al cerrar (fase, hecho, pendientes,
   UN siguiente paso concreto). Máximo 1 página: resume, no acumules.
3. Decisiones de arquitectura → registrar en `docs/DECISIONES.md` (fecha, contexto, decisión).
4. Cambia esquema BD o arquitectura → actualizar el `.dot` y regenerar SVG (`dot -Tsvg`).
5. **API de SolidWorks: si no estás seguro de una firma/comportamiento, NO la inventes.**
   Escríbela y márcala `// VERIFICAR-API:` para que el usuario la valide en help.solidworks.com.
6. COM: liberar todo objeto (`finally` + `Marshal.ReleaseComObject` o wrapper). Prohibido
   encadenar objetos COM en una línea; usar variables intermedias.
7. Errores por archivo, nunca por lote: archivo corrupto → log + continuar. El lote no se detiene.
8. Código/identificadores en inglés; comentarios, logs, UI y docs en español.
9. Preguntar antes de: nuevo paquete NuGet, cambiar esquema ya migrado, tocar `.claude/settings.json`.
10. Cierre de cada respuesta de implementación: resumen breve + commit(s) sugerido(s) + siguiente paso.

## Eficiencia de tokens (importante)

- No re-leas archivos completos que no vas a modificar; usa búsqueda dirigida (grep) primero.
- Para entender el sistema, lee los `.dot` de `docs/diagramas/` antes que el código.
- No pegues en tus respuestas código que no cambió; muestra solo diffs o archivos nuevos.
- No repitas el contenido de CLAUDE.md ni ESTADO.md en tus respuestas.

## Contexto técnico crítico

- Clave DocManager (`SwDmLicenseKey`): user-secrets o variable de entorno. JAMÁS en código ni en git.
- Solo `DocManager` y `SwApi` referencian DLLs de SW; el resto de la solución compila sin SW instalado.
- Apertura silenciosa: `OpenDoc6` Silent + Lightweight, suprimir diálogos.
- Propiedades existen a nivel documento Y configuración: modelo de datos y UI distinguen ambos siempre.
- Archivo de versión SW más nueva que la instalada → marcar `version_no_soportada`, no fallar.
