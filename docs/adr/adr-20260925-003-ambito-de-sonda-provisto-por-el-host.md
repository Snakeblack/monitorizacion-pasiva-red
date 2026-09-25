# ADR-003: Ámbito de sonda provisto por el host

- Status: proposed
- Change: s02-contrato-ingestion-durable
- Date: 2026-09-24

## Context

`REQ-contrato-ingestion-v1-002` exige comparar el ámbito declarado con una identidad confiable. S02 usa sondas sintéticas en un entorno aislado; mTLS está previsto para S13.

## Decision

El host proporciona `(siteId, sensorId)` por un contexto inyectable de confianza. El endpoint no acepta cabeceras ni campos del cliente como fuente de esa identidad: sin contexto devuelve 401 y ante discrepancia devuelve 403. Las pruebas aportan un contexto de servidor; S13 conectará el adaptador mTLS antes de aceptar sondas reales.

## Alternatives

- Usar los IDs del cuerpo o cabeceras sin autenticación: permitiría escribir en otro ámbito.
- Implementar mTLS completo en S02: adelantaría gestión de certificados y despliegue fuera del slice aprobado.

## Consequences

El límite de confianza es verificable ahora y reutilizable por S13. La ingestión S02 no queda habilitada para emisores reales hasta conectar y verificar una identidad de transporte apta.
