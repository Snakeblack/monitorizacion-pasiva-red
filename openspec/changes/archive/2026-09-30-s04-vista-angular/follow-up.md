# Seguimiento S04-W001 y S04-W002

La verificación de S04 es PASS WITH WARNINGS. El hallazgo crítico de la integración continua quedó cerrado. Estas dos advertencias no bloquean el archivo y pasan a trabajo posterior.

## S04-W001 — valores de sede y sonda frente a las etiquetas

`session-detail-view-chain.spec.ts` comprueba `event`, `site` y `sensor` con `toContain`. Esas cadenas también aparecen en las etiquetas `eventId`, `siteId` y `sensorId`, así que la aserción puede pasar aunque el valor mostrado sea otro. `occurredAt` y `data` sí se comprueban como valores.

- [ ] Ajustar la cadena para afirmar el valor junto a su etiqueta, con identificadores que no sean subcadenas de esas etiquetas.
- [ ] Mantener la comprobación de cuerpo ajeno en el host.

## S04-W002 — registro TDD sin recibos de ejecución

El bloque `json:strict-tdd-evidence` de `apply-progress.md` no valida: los digest no coinciden con el árbol actual, sobre todo `VistaAngularDeliveryTests.cs`, y no hay recibos de ejecución. Las suites actuales pasaron; eso no reconstruye el orden RED/GREEN anterior.

- [ ] En el siguiente slice, capturar procedencia verificable desde la primera unidad, o documentar la limitación del host.
- [ ] No fabricar recibos ni reescribir el historial de S04 para que el validador quede en verde.

Criterio de cierre: el siguiente slice no hereda estas dos advertencias como si fueran comportamiento de producto. W001 es una aserción de prueba. W002 es procedencia de la evidencia.
