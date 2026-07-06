Ejecuta una auditoría de fase (usar con el modelo más avanzado disponible, p. ej. Opus).
Sigue el protocolo de docs/AUDITORIA.md y entrega el reporte en ese mismo archivo bajo una
nueva sección con la fecha. Resumen del alcance:

1. **Arquitectura**: ¿el código respeta las 4 capas y la interfaz IExtractorCad? ¿Hay lógica
   de negocio filtrada en UI o en adaptadores? Lee primero los .dot de docs/diagramas y
   verifica que siguen reflejando la realidad.
2. **Robustez COM**: busca (grep) objetos COM sin liberar, encadenamientos prohibidos,
   ausencia de timeouts o de manejo de errores por archivo.
3. **Marcadores pendientes**: lista todos los `// VERIFICAR-API:` y `// TODO:` vigentes.
4. **Deuda técnica**: duplicación, métodos >80 líneas, clases con demasiadas responsabilidades.
   Prioriza: solo lo que valga la pena arreglar, con costo/beneficio.
5. **Eficiencia de tokens del proyecto**: ¿CLAUDE.md sigue siendo mínimo y vigente?
   ¿ESTADO.md está en 1 página? ¿DECISIONES.md tiene entradas obsoletas que resumir?
   Propón podas concretas de contexto.
6. **Seguridad**: claves o rutas hardcodeadas, datos sensibles en logs o en git.

Entrega: reporte priorizado (crítico / recomendado / opcional) con acciones concretas.
NO apliques cambios en esta sesión: solo diagnostica. Los arreglos se hacen después en
sesiones normales con Sonnet, uno por uno.
