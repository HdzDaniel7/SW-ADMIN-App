# DISEÑO — SWDataExtractor

> Especificación aprobada por el usuario. Sonnet la IMPLEMENTA tal cual en Fase 0-2.
> Cualquier desviación necesaria se pregunta primero y se registra en DECISIONES.md.

---

## 1. Esquema de base de datos

Convenciones: tablas y columnas en snake_case español (la BD la leerán ingenieros, no solo código).
Entidades C# en PascalCase inglés mapeadas con EF Core. Todas las tablas tienen `id INTEGER PK AUTOINCREMENT`.
Fechas en UTC (`TEXT` ISO-8601 en SQLite). Booleanos como INTEGER 0/1.

### archivos
Registro maestro de cada archivo encontrado en el escaneo.

| columna | tipo | notas |
|---|---|---|
| ruta | TEXT UNIQUE NOT NULL | ruta absoluta normalizada |
| nombre | TEXT NOT NULL | con extensión |
| tipo | TEXT NOT NULL | enum: `pieza`, `ensamble`, `plano`, `otro` |
| hash_sha256 | TEXT | del contenido; base de la extracción incremental |
| tamano_bytes | INTEGER | |
| fecha_mod_disco | TEXT | última modificación del archivo en disco |
| version_sw | INTEGER | año de la versión que lo guardó (p. ej. 2024) |
| autor | TEXT | propiedad summary del documento |
| ruta_preview | TEXT | PNG extraído, en carpeta caché configurable |
| fecha_extr_rapida | TEXT | última extracción DocManager exitosa |
| fecha_extr_profunda | TEXT | última extracción SwApi exitosa |
| estado_rapido | TEXT NOT NULL | enum estados (abajo), default `pendiente` |
| estado_profundo | TEXT NOT NULL | idem |
| mensaje_error | TEXT | último error, legible |
| origen | TEXT NOT NULL | default `sistema_archivos`; futuro: `pdm`, `importado`, otro CAD |
| datos_extra_json | TEXT | bolsa de extensión: datos futuros sin migrar esquema |

Enum estados: `pendiente`, `ok`, `error`, `timeout`, `version_no_soportada`, `bloqueado`, `omitido`.
Índices: `ruta` (unique), `hash_sha256`, `tipo`, `estado_rapido`, `estado_profundo`.

### configuraciones
| columna | tipo | notas |
|---|---|---|
| archivo_id | FK archivos, CASCADE | |
| nombre | TEXT NOT NULL | |
| es_activa | INTEGER | configuración activa al guardar |
| es_derivada | INTEGER | derived configuration |

Unique(archivo_id, nombre).

### propiedades
Propiedades personalizadas, a nivel documento (configuracion_id NULL) o de configuración.

| columna | tipo | notas |
|---|---|---|
| archivo_id | FK archivos, CASCADE | |
| configuracion_id | FK configuraciones, NULL | NULL = nivel documento |
| nombre | TEXT NOT NULL | |
| valor | TEXT | expresión cruda (puede contener fórmulas "SW-Mass...") |
| valor_resuelto | TEXT | valor evaluado |
| tipo | TEXT | `texto`, `numero`, `fecha`, `si_no` |

Unique(archivo_id, configuracion_id, nombre). Índice: `nombre`.

### propiedades_fisicas
Por configuración (material y masa cambian entre configuraciones).

| columna | tipo |
|---|---|
| archivo_id | FK archivos, CASCADE |
| configuracion_id | FK configuraciones, CASCADE |
| material | TEXT |
| densidad_kg_m3 | REAL |
| masa_kg | REAL |
| volumen_m3 | REAL |
| area_m2 | REAL |

Unique(archivo_id, configuracion_id).

### componentes
Relación directa padre→hijo (UN nivel). La jerarquía completa y las cantidades totales
se calculan por recursión (CTE `WITH RECURSIVE`), no se almacenan aplanadas.

| columna | tipo | notas |
|---|---|---|
| ensamble_archivo_id | FK archivos, CASCADE | el padre |
| ensamble_config_id | FK configuraciones | configuración del padre |
| componente_archivo_id | FK archivos, NULL | NULL si la referencia está rota |
| ruta_referenciada | TEXT NOT NULL | ruta tal cual la guarda el ensamble (para diagnóstico de rotas) |
| configuracion_usada | TEXT | nombre de la config del hijo usada |
| cantidad | INTEGER NOT NULL | instancias agregadas de esa combinación |
| suprimido | INTEGER | |
| es_toolbox | INTEGER | tornillería/hardware de Toolbox |
| es_envelope | INTEGER | componentes envelope se excluyen del BOM |
| datos_extra_json | TEXT | extensión futura (n.º ítem BOM, costos, etc.) |

Unique(ensamble_archivo_id, ensamble_config_id, ruta_referenciada, configuracion_usada, suprimido).
Índices: ensamble_archivo_id, componente_archivo_id (este índice ES el where-used).

### features
Árbol de operaciones (solo extracción profunda).

| columna | tipo | notas |
|---|---|---|
| archivo_id | FK archivos, CASCADE | |
| nombre | TEXT NOT NULL | nombre en el árbol ("Chaflán1") |
| tipo_sw | TEXT NOT NULL | crudo de `GetTypeName2` ("Chamfer", "HoleWzd", "Fillet"...) |
| categoria | TEXT NOT NULL | enum propio: `chaflan`, `redondeo`, `barreno_asistente`, `rosca`, `extrusion`, `corte`, `revolucion`, `barrido`, `patron`, `referencia`, `otro` |
| parametros_json | TEXT | JSON con parámetros según categoría (distancia, ángulo, radio...) |
| suprimido | INTEGER | en la configuración activa |
| orden | INTEGER | posición en el árbol |

Índices: archivo_id, categoria, tipo_sw.
En cada extracción profunda se BORRAN e insertan de nuevo los features del archivo (delete-insert, no merge).

### roscas
Desnormalizada a propósito: es la consulta estrella del proyecto.

| columna | tipo | notas |
|---|---|---|
| archivo_id | FK archivos, CASCADE | |
| feature_id | FK features, CASCADE | el Hole Wizard/Thread de origen |
| designacion | TEXT NOT NULL | "M8x1.25", "1/4-20 UNC" |
| estandar | TEXT | "ISO", "ANSI Inch"... |
| tipo_barreno | TEXT | `roscado`, `pasante`, `abocardado`, `avellanado`, `otro` |
| diametro_nominal_mm | REAL | |
| paso_mm | REAL | NULL en roscas en pulgadas; guardar hilos_por_pulgada |
| hilos_por_pulgada | REAL | NULL en métricas |
| profundidad_rosca_mm | REAL | |
| profundidad_barreno_mm | REAL | |
| pasante | INTEGER | |
| cantidad | INTEGER NOT NULL | instancias del feature (incluye puntos del sketch y patrones si es resoluble; si no, 1 + advertencia) |

Índices: designacion, archivo_id.

### diccionario_propiedades
Catálogo de propiedades estándar de la empresa (configurable, no hardcodeado).

| columna | tipo | notas |
|---|---|---|
| nombre | TEXT UNIQUE NOT NULL | "NumeroParte", "Material", "Proveedor"... |
| tipo | TEXT NOT NULL | `texto`, `numero`, `fecha`, `si_no`, `lista` |
| valores_permitidos_json | TEXT | para tipo `lista` |
| obligatoria | INTEGER | para reportes de cumplimiento |
| nivel | TEXT | `documento`, `configuracion`, `ambos` |
| descripcion | TEXT | |
| activa | INTEGER | |

### historial_propiedades
Auditoría de toda escritura hecha por la herramienta.

| columna | tipo |
|---|---|
| lote_id | TEXT NOT NULL (GUID por operación de lote) |
| archivo_id | FK archivos |
| configuracion | TEXT NULL |
| propiedad | TEXT NOT NULL |
| valor_anterior | TEXT |
| valor_nuevo | TEXT |
| usuario | TEXT NOT NULL |
| fecha | TEXT NOT NULL |
| resultado | TEXT (`ok`, `error_bloqueado`, `error_otro`) |

Índices: lote_id, archivo_id.

### trabajos_extraccion
Cola de procesamiento; comunica Batch con la UI.

| columna | tipo |
|---|---|
| archivo_id | FK archivos |
| tipo | TEXT (`rapida`, `profunda`) |
| estado | TEXT (`pendiente`, `en_proceso`, `ok`, `error`, `timeout`, `cancelado`) |
| intentos | INTEGER default 0 (máx configurable, default 2) |
| fecha_encolado / fecha_inicio / fecha_fin | TEXT |
| duracion_ms | INTEGER |
| mensaje | TEXT |

Índice: estado, tipo.

### etiquetas / archivo_etiquetas
Clasificación libre a nivel aplicación (proyecto, cliente, estado interno...) SIN tocar los
archivos de SolidWorks ni el esquema. Cubre necesidades futuras de organización.

**etiquetas**: `id PK · nombre TEXT UNIQUE · color TEXT · descripcion TEXT · activa INTEGER`
**archivo_etiquetas**: `archivo_id FK CASCADE · etiqueta_id FK CASCADE` — Unique(archivo_id, etiqueta_id)

### ajustes_app
Clave-valor persistente para configuración que debe vivir en la BD (no en appsettings):
preferencias de UI, feature flags por despliegue, contadores, vistas guardadas de filtros.

`id PK · clave TEXT UNIQUE NOT NULL · valor TEXT · descripcion TEXT`

---

## 1b. Principios de extensibilidad (aplicar en TODO el desarrollo)

1. **Columnas `datos_extra_json`**: cuando surja un dato nuevo puntual, primero va al JSON;
   solo se promueve a columna real (migración) cuando se filtra/indexa con frecuencia.
   Regla: nunca rechazar "no cabe en el esquema" — cabe en el JSON.
2. **Feature flags**: toda funcionalidad posterior a Fase 2 se registra en
   `appsettings → Funcionalidades` (bool) y la UI la OCULTA cuando está en false.
   La app nace con más capacidades apagables, no con carencias.
3. **`archivos.origen`** prepara la entrada de futuras fuentes (PDM, otros CAD): un nuevo
   origen = nuevo adaptador `IExtractorCad` + valor de enum, cero cambios en el resto.
4. **Etiquetas** cubren cualquier clasificación futura (proyecto, cliente, línea de producto)
   sin migraciones ni tocar archivos de SW.
5. **Migraciones EF Core siempre aditivas** en lo posible (agregar, no renombrar/borrar),
   para que BDs de usuarios existentes actualicen sin dolor.

## 2. Contratos de código (Core)

```csharp
// ===== Enums =====
[Flags]
public enum AlcanceExtraccion
{
    Ninguno      = 0,
    Propiedades  = 1,   // custom properties doc + config
    Estructura   = 2,   // componentes de ensamble
    Fisicas      = 4,   // material, masa, volumen
    Preview      = 8,
    Features     = 16,  // árbol de operaciones
    Roscas       = 32,
    Rapida       = Propiedades | Estructura | Fisicas | Preview,
    Profunda     = Rapida | Features | Roscas
}

public enum EstadoExtraccion { Ok, Error, Timeout, VersionNoSoportada, Bloqueado, Omitido }

public enum TipoArchivoCad { Pieza, Ensamble, Plano, Otro }

// ===== Interfaz principal (implementan DocManager y SwApi) =====
public interface IExtractorCad
{
    string Nombre { get; }                       // "DocManager" | "SwApi"
    AlcanceExtraccion Capacidades { get; }       // DocManager: Rapida. SwApi: Profunda.
    bool PuedeProcesar(string ruta);             // por extensión
    Task<ResultadoExtraccion> ExtraerAsync(SolicitudExtraccion solicitud, CancellationToken ct);
}

// ===== Escritura (solo DocManager la implementa) =====
public interface IEscritorPropiedades
{
    Task<ResultadoEscrituraLote> EscribirAsync(LoteEscritura lote, CancellationToken ct);
}

// ===== DTOs de extracción (records, sin dependencia de SW) =====
public record SolicitudExtraccion(string Ruta, AlcanceExtraccion Alcance);

public record ResultadoExtraccion
{
    public required EstadoExtraccion Estado { get; init; }
    public string? MensajeError { get; init; }
    public DatosArchivo? Archivo { get; init; }
    public IReadOnlyList<DatosConfiguracion> Configuraciones { get; init; } = [];
    public IReadOnlyList<DatosPropiedad> Propiedades { get; init; } = [];
    public IReadOnlyList<DatosPropiedadesFisicas> Fisicas { get; init; } = [];
    public IReadOnlyList<DatosComponente> Componentes { get; init; } = [];
    public IReadOnlyList<DatosFeature> Features { get; init; } = [];
    public IReadOnlyList<DatosRosca> Roscas { get; init; } = [];
    public IReadOnlyList<string> Advertencias { get; init; } = [];   // p.ej. "patrón no resoluble, cantidad=1"
}

public record DatosArchivo(TipoArchivoCad Tipo, int? VersionSw, string? Autor, byte[]? PreviewPng);
public record DatosConfiguracion(string Nombre, bool EsActiva, bool EsDerivada);
public record DatosPropiedad(string? Configuracion, string Nombre, string? Valor, string? ValorResuelto, string Tipo);
public record DatosPropiedadesFisicas(string Configuracion, string? Material, double? DensidadKgM3,
                                      double? MasaKg, double? VolumenM3, double? AreaM2);
public record DatosComponente(string RutaReferenciada, string? ConfiguracionUsada, int Cantidad,
                              bool Suprimido, bool EsToolbox, bool EsEnvelope);
public record DatosFeature(string Nombre, string TipoSw, string Categoria, string? ParametrosJson,
                           bool Suprimido, int Orden);
public record DatosRosca(string FeatureNombre, string Designacion, string? Estandar, string TipoBarreno,
                         double? DiametroNominalMm, double? PasoMm, double? HilosPorPulgada,
                         double? ProfundidadRoscaMm, double? ProfundidadBarrenoMm, bool Pasante, int Cantidad);

// ===== DTOs de escritura =====
public record LoteEscritura(string Usuario, IReadOnlyList<CambioPropiedad> Cambios);
public record CambioPropiedad(string Ruta, string? Configuracion, string Propiedad, string ValorNuevo);
public record ResultadoEscrituraLote(string LoteId, IReadOnlyList<ResultadoCambio> Resultados);
public record ResultadoCambio(CambioPropiedad Cambio, string? ValorAnterior, string Resultado, string? Mensaje);
```

### Servicios de Core (orquestación)

- **`OrquestadorExtraccion`**: recibe archivos + modo (`Rapido`/`Profundo`/`Auto`), decide qué
  extractor usar, encola en `trabajos_extraccion`, persiste resultados.
  Regla del modo Auto: Rápida si `hash != hash_guardado || estado_rapido != ok`;
  Profunda además si `estado_profundo != ok || fecha_extr_profunda < fecha_mod_disco`.
- **`EscaneadorCarpetas`**: recorre carpetas (configurables, con exclusiones), calcula hash,
  hace upsert en `archivos`, detecta eliminados (marcar, no borrar).
- **`ServicioBom`**: BOM indentado y aplanado vía CTE recursiva; filtros excluir_toolbox,
  excluir_suprimidos, excluir_envelope; diff entre dos ensambles/fechas; where-used.
- **`ServicioPropiedades`**: lectura tabulada, validación contra diccionario, preparación de
  lotes de escritura con diff previo, registro en historial.

### Robustez SwApi (Fase 2 — parte del contrato, no opcional)

- Timeout por archivo configurable (default 300 s) vía `CancellationTokenSource`; al vencer:
  matar proceso SLDWORKS.exe, marcar `timeout`, reiniciar SW, continuar con el siguiente.
- SW se inicia con `/b` (sin splash) y se reutiliza entre archivos; reinicio forzado cada
  N archivos (default 50) para evitar degradación por memoria.
- Todo acceso COM en clase wrapper `ComScope : IDisposable` que registra y libera objetos.
- Apertura: `OpenDoc6` con Silent + Lightweight (resolver componentes solo si el alcance lo exige).
  // VERIFICAR-API: opciones exactas de swOpenDocOptions_e al implementar.

---

## 3. Configuración (appsettings.json)

```json
{
  "Extraccion": {
    "CarpetasRaiz": [],
    "ExtensionesIncluidas": [".sldprt", ".sldasm"],
    "PatronesExcluidos": ["~$*", "*\\backup\\*"],
    "TimeoutPorArchivoSegundos": 300,
    "ReiniciarSwCadaNArchivos": 50,
    "MaxReintentos": 2,
    "CarpetaCachePreviews": "%LOCALAPPDATA%/SWDataExtractor/previews"
  },
  "BaseDatos": { "CadenaConexion": "Data Source=swdata.db" },
  "Funcionalidades": {
    "ExtraccionProfunda": true,
    "EscrituraPropiedades": true,
    "Etiquetas": true,
    "ComparacionBom": true,
    "ExportacionExcel": true
  },
  "Serilog": { "MinimumLevel": "Information" }
}
```
`SwDmLicenseKey` va en user-secrets (`dotnet user-secrets set SwDmLicenseKey "..."`), nunca aquí.

---

## 4. Diagramas

- `docs/diagramas/arquitectura.dot` — capas, adaptadores, flujo de datos.
- `docs/diagramas/esquema-bd.dot` — ER con las 13 tablas y sus relaciones.
Regenerar: `dot -Tsvg docs/diagramas/X.dot -o docs/diagramas/X.svg`.

## 5. Criterios de aceptación por fase (resumen ejecutable)

- **F0**: `dotnet build` OK; `dotnet ef database update` crea las 13 tablas; SVGs generados.
- **F1**: escaneo de carpeta de prueba llena `archivos/configuraciones/propiedades/fisicas/componentes`;
  segundo escaneo sin cambios no reprocesa nada (verificar por log); referencias rotas quedan
  con `componente_archivo_id NULL` y visibles en un reporte.
- **F2**: extracción profunda de las piezas de prueba llena `features` y `roscas`; conteo de
  roscas por designación coincide con verificación manual de 5 piezas; carpeta tortura completa
  el lote sin intervención (errores registrados, cero cuelgues).
- **F7a**: con una BD ya poblada, los 4 reportes (duplicados, rotas, posibles versiones,
  dashboard de proyecto) muestran resultados correctos sin ninguna llamada a DocManager/SwApi
  ni SolidWorks abierto — verificable cerrando SW por completo y confirmando que igual funcionan.

---

## 6. Fase 7 — Explorador y gestión de proyectos

Acordado con el usuario 2026-07-03. Principio rector: **cero dependencia de SW en vivo ni de
licencia DocManager** para todo lo descrito en 7a — se calcula sobre `archivos`/`componentes`
ya poblados por el escaneo/extracción existentes. Sin migraciones EF: ninguna consulta de
esta fase requiere columnas nuevas.

### 7a. Reportes y navegación (alcance de esta fase — implementar primero)

- **Explorador de archivos**: árbol de carpetas (derivado de `archivos.ruta`, agrupando por
  segmentos de carpeta — no requiere tabla de carpetas propia) + lista de archivos de la
  carpeta seleccionada, con el mismo estado/acciones que el grid de Archivos hoy (abrir,
  abrir carpeta, ver detalle). "Proyecto" = carpeta raíz de `CarpetasEscaneo` (ya existente).
- **Duplicados exactos**: `archivos` agrupado por `hash_sha256` con `COUNT(*) > 1` (excluir
  `estado_rapido = 'omitido'`). Mostrar rutas del grupo + tamaño + fecha; sin acción
  automática, es un reporte para que el usuario decida.
- **Referencias rotas**: `componentes WHERE componente_archivo_id IS NULL`, con
  `ruta_referenciada` (lo que el ensamble esperaba) y el ensamble padre (`ensamble_archivo_id`).
  Este reporte estaba en el criterio de aceptación de F1 y no se había construido la pantalla.
- **Posibles versiones del mismo archivo**: heurística, NO exacta — agrupar por nombre
  normalizado (quitar extensión y sufijos comunes de versión: `_v\d+`, `-v\d+`, `\(\d+\)`,
  `_final`, `_old`, `_copia`/`_copy`, fechas al final) dentro de la misma carpeta raíz +
  mismo `tipo`. Mostrar el grupo ordenado por `fecha_mod_disco` descendente (la más reciente
  primero). Es sugerencia para revisión manual — nunca fusiona ni borra automáticamente. Los
  patrones exactos de normalización son detalle de implementación, ajustables sin romper el
  contrato (no son parte del esquema de BD).
- **Dashboard de proyecto**: elegir un ensamble top-level → mostrar en una sola pantalla su
  BOM (ya existe `ServicioBom.ObtenerBomIndentadoAsync`), salud de extracción de cada
  componente (`estado_rapido`/`estado_profundo`), y dónde más se usa cada pieza del árbol
  (`ServicioBom.ObtenerWhereUsedAsync`, ya existe).

### 7b/7c — diferidas, no iniciar sin acuerdo explícito posterior

Ver CLAUDE.md fase 7b/7c. 7b (copiar/mover con aviso de referencias) reutiliza `componentes`
para advertir antes de mover un archivo referenciado; actualiza `archivos.ruta` por
`hash_sha256` en vez de re-extraer. 7c (Pack and Go real vía SwApi) requiere SW abierto —
evaluar primero si 7b ya cubre la necesidad real antes de construir esto, dado el principio
"instala y funciona".
