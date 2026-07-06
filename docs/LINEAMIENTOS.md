# LINEAMIENTOS PARA INICIAR — SWDataExtractor

## 1. Checklist: qué debes tener ANTES de empezar

### Software obligatorio
- [ ] **Windows 10/11** (la API COM de SolidWorks solo existe en Windows)
- [ ] **SolidWorks instalado y licenciado** (tu licencia actual)
- [ ] **Clave del Document Manager API** — solicitarla YA en el portal de clientes de
      SolidWorks (customerportal.solidworks.com → API Support). Puede tardar días. Gratuita
      con suscripción activa.
- [ ] **.NET 8 SDK** (dotnet.microsoft.com)
- [ ] **VS Code** con extensiones: C# Dev Kit, y opcionalmente SQLite Viewer
- [ ] **Node.js 18+** y **Claude Code**: `npm install -g @anthropic-ai/claude-code`
      (docs: https://docs.claude.com/en/docs/claude-code/overview)
- [ ] **Git** instalado y repositorio inicializado (local; remoto privado opcional)
- [ ] **Graphviz** (ya lo tienes) — verificar que `dot -V` funcione en la terminal
- [ ] **DB Browser for SQLite** (opcional pero muy útil para inspeccionar la BD)

### Referencias de SolidWorks que usarás en los proyectos C#
- [ ] `SolidWorks.Interop.sldworks`, `SolidWorks.Interop.swconst` (carpeta api\redist de tu
      instalación de SW)
- [ ] `SwDocumentMgr.dll` (Document Manager, misma carpeta de redistribución)

### Datos de prueba
- [ ] **Carpeta de prueba "buena"**: 15–30 piezas y 2–3 ensambles reales tuyos, con roscas
      de Hole Wizard, chaflanes y tornillería Toolbox
- [ ] **Carpeta "tortura"**: archivos corruptos, referencias rotas, versiones viejas,
      un ensamble grande, piezas importadas (STEP guardado como SLDPRT). Esta carpeta
      define si la herramienta es robusta.

### Conocimiento de apoyo (tenerlo a mano, no memorizarlo)
- [ ] Documentación API: help.solidworks.com → API Help (SW API y Document Manager API)
- [ ] Conceptos EF Core: migraciones, DbContext

## 2. Estructura de arranque del repositorio

Copia estos archivos a la raíz de tu repo antes de la primera sesión:

```
tu-repo/
├── CLAUDE.md                  ← contexto estable del proyecto
├── LINEAMIENTOS.md            ← este archivo
├── .claude/
│   └── settings.json          ← bloqueo de commits y comandos peligrosos
├── docs/
│   ├── ESTADO.md              ← documento vivo (estado, pendientes, siguiente paso)
│   ├── DECISIONES.md          ← crear vacío con solo el título
│   └── diagramas/             ← carpeta vacía
└── .gitignore                 ← plantilla VisualStudio + agregar *.db, appsettings.local.json
```

## 3. Flujo de trabajo por sesión (SIEMPRE igual)

1. Abrir terminal en la raíz del repo → `claude`
2. Primera instrucción de cada sesión: **`/inicio`**
3. Aprobar o ajustar el plan. Claude trabaja SOLO en la fase actual.
4. Al cerrar un bloque de trabajo, Claude sugiere el/los commits. **Tú los ejecutas** en otra
   terminal: `git add ... && git commit -m "..."`. Revisa el diff antes (`git diff --staged`).
5. Antes de cerrar la sesión: **`/cierre`**
6. Commit final de la sesión incluyendo ESTADO.md.

## 4. Reglas de commits (para ti)

- Un commit por unidad lógica (no "avances del día" gigantes).
- Formato Conventional Commits en español: `feat: extracción de roscas Hole Wizard`,
  `fix: liberación COM en cierre de documento`, `docs: actualiza ESTADO fase 2`.
- Nunca commitear: `*.db`, la clave del Document Manager, `appsettings.local.json`, `bin/`, `obj/`.
- Tag al cerrar cada fase: `git tag fase-0`, `fase-1`, ...

## 5. Uso de Graphviz en el proyecto

- Los diagramas SON código: `docs/diagramas/*.dot`, versionados en git.
- Diagramas mínimos: `arquitectura.dot` (capas y adaptadores) y `esquema-bd.dot` (ER).
- Regla para Claude (ya está en CLAUDE.md): si cambia el esquema o la arquitectura,
  actualiza el .dot y regenera: `dot -Tsvg docs/diagramas/esquema-bd.dot -o docs/diagramas/esquema-bd.svg`
- Beneficio: en cualquier sesión futura, Claude puede leer el .dot y entender el sistema
  sin recorrer todo el código.

## 6. Estrategia de modelos

- **Todo el trabajo se hace con Claude Sonnet 4.6** (modelo por defecto). El terreno está
  preparado para él: reglas explícitas en CLAUDE.md, comandos slash y fases cerradas.
- **Auditorías con el modelo más avanzado disponible** (Opus 4.8 hoy) al cerrar cada fase:
  `/model` → Opus → `/auditoria` → commit del reporte → volver a Sonnet.
  Protocolo completo en docs/AUDITORIA.md.
- Regla de oro de costos: el modelo caro DIAGNOSTICA, el modelo eficiente EJECUTA.

## 6b. Comandos slash disponibles (ahorran tokens en cada sesión)

- `/inicio`  → lee ESTADO.md, propone plan del día, espera tu aprobación.
- `/cierre`  → verifica build, actualiza ESTADO.md y diagramas, sugiere commits.
- `/auditoria` → protocolo de auditoría (solo con modelo avanzado).

## 7. Señales de alerta (parar y revisar)

- Claude propone cambiar el esquema de BD ya migrado sin preguntarte → recordarle regla 9 del CLAUDE.md.
- Código COM que encadena llamadas (`swApp.ActiveDoc.Extension...`) → violación de regla 6.
- Aparecen firmas de API de SolidWorks sin el marcador `// VERIFICAR-API:` y no las reconoces
  → pedir que las marque y validarlas contra help.solidworks.com.
- ESTADO.md crece sin control → pedir que resuma lo histórico.
