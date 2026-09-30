# ADR-003: Detalle por ámbito confiable separado de ingestión

- Status: proposed
- Change: s03-proyeccion-idempotente
- Date: 2026-09-29

## Contexto
REQ-detalle-sesion-ambito-001/002 y s03-contract-001 requieren lectura por sede/sonda, separada de identidad de máquina y limitada a desarrollo/pruebas.

## Decisión
GET /api/v1/sessions/{eventId} obtiene el ámbito de un feature interno distinto del de ingestión. Endpoint admite ese mecanismo solo en Development/Testing y aplica guard aun con proveedor DI sustituido. Consulta parametrizada por la triple identidad; 401 sin contexto, 404 ausente/ajeno y 200 con los cinco campos acordados.

## Alternativas
- Headers/query o identidad de sonda: permiten confundir autoridad y ámbito solicitado.
- OIDC/RBAC ahora: corresponde a S12 y amplía alcance.

## Consecuencias
Tests verifican separación de identidades y rechazo en Production. Host no crea identidad de lectura desde solicitudes externas. No habilita acceso humano productivo; S12 debe sustituir conscientemente este adaptador conservando el aislamiento.
