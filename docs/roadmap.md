# Hoja de ruta de la primera entrega de producción

La primera entrega debe operar en varias sedes, con continuidad, seguridad y capacidad demostradas. [ADR-013](architecture/decisions/ADR-013.md) fija el **perfil de aceptación propuesto**: cuatro sedes, ocho sondas, 10 millones de sesiones inferidas al día, 30 días consultables y 20 consultas simultáneas. Son objetivos de ensayo; aún no se ha medido que la arquitectura los cumpla ni se han ratificado las políticas corporativas. **S01/S02/S03 implementan y prueban la base, ingestión durable y proyección sintética idempotente con detalle por ámbito en desarrollo/pruebas; S04 y los slices posteriores siguen pendientes.**

## Decisiones y fuentes

| Fuente | Uso en esta hoja de ruta |
|---|---|
| [Brief](product/brief.md) y [alcance F-01–F-09](product/functional-scope.md) | Valor, funciones, matriz de acceso y exclusiones. |
| [ADR-013](architecture/decisions/ADR-013.md), [ADR-014](architecture/decisions/ADR-014.md), [modelo S00](architecture/modelo-capacidad-s00.md) y [brechas G-01–G-09](roadmap-gaps.md) | Objetivos de aceptación, decisión de diseño por etapas, hipótesis de capacidad y evidencias pendientes. ADR-013 sustituye las cifras de piloto de ADR-012. |
| [ADR-010](architecture/decisions/ADR-010.md) y [ADR-011](architecture/decisions/ADR-011.md) | Piezas candidatas, alternativas y observabilidad con responsable operativo. La decisión S00 adopta PostgreSQL provisionalmente. |
| [ADR-012](architecture/decisions/ADR-012.md) | Reglas de sesión, identidad, roles y transporte que no contradicen ADR-013; sus límites de piloto son históricos. |
| [Base técnica](architecture/technical-baseline.md) y [flujo](architecture/flujo-completo.md) | Responsabilidades, enlaces, fallos y condiciones de evolución. |

El [caso y el análisis inicial en Markdown](references/processed/README.md) son fuentes orientativas. Los ADR explican cada adopción o descarte de herramientas; un resultado nuevo que cambie una decisión exige un ADR sucesor.

## Desarrollo y puertas

Los [slices de desarrollo](development/slices.md) son entregas pequeñas y revisables. Su orden evita fijar el esquema definitivo de sesiones antes de conocer el coste y la aptitud de la ruta de datos.

| Orden | Slices | Resultado comprobable | Puerta |
|---|---|---|---|
| 0 | [S00](development/slices.md#s00) | Modelo reproducible con hipótesis, rangos, cálculos de volumen y sensibilidad; decisión arquitectónica provisional y riesgos registrados. | S01 puede comenzar con el almacén candidato y criterios explícitos de revisión. La capacidad, los costes y la recuperación se acreditan con ensayos posteriores. |
| 1 | [S01–S04](development/slices.md#s01) | Base reproducible y ruta vertical con datos sintéticos: ingestión → bandeja → worker → API → Angular. | Contrato y almacenamiento conforme a la decisión provisional S00; CI reproduce pruebas. |
| 2 | [S05–S08](development/slices.md#s05) | Captura, sesiones inferidas, spool por sonda, reenvío, idempotencia y cuarentena. | Descartes y duplicados reconciliables; desconexión y ráfaga medidas. |
| 3 | [S09–S11](development/slices.md#s09) | Consultas selectivas de 30 días, candidatos e inventario manual auditado. | Mezcla de consultas y aislamiento de sede/sonda comprobados. |
| 4 | [S12–S16](development/slices.md#s12) | OIDC/RBAC, TLS/mTLS, señales y guardia, retención, alta disponibilidad, copias y PITR. | Políticas de datos y matriz ratificadas; conmutación y restauración ensayadas. |
| 5 | [S17–S18](development/slices.md#s17) | Ensayo extremo a extremo con datos representativos y decisión de liberación. | Se cumplen los objetivos de ADR-013 y las [validaciones de producción](roadmap-gaps.md#validaciones-para-liberar-la-primera-entrega-de-producción), o se revisa alcance y arquitectura antes de liberar. |

**S00 precede al desarrollo:** define hipótesis trazables, calcula escenarios y registra una elección revisable. No requiere esperar una ventana de operación prolongada ni disponer de una captura representativa para iniciar S01. Las siguientes entregas usan TDD estricto y un presupuesto orientativo de 400 líneas cambiadas por revisión; se subdivide cualquier slice que no quepa sin perder una comprobación propia.

El desarrollo no presupone que ya existan IdP, CA, telemetría, guardia o alta disponibilidad corporativos. G-07 exige inventariarlos y escoger una alternativa operable donde falten. La plataforma de producción solo se libera al cerrar las puertas de datos reales, seguridad, capacidad y continuidad.
