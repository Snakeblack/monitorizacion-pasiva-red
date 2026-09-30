# ADR-002: Ámbito de lectura solo desde configuración del servidor

- Status: proposed
- Change: s04-vista-angular
- Date: 2026-09-30

## Context

`HostContextTrustedSessionReadContextProvider` lee `ITrustedSessionReadContextFeature` y nada de la petición. `Program.cs` no instala ese feature, así que el host público responde 401. La spec pide instalar sede y sonda desde configuración solo en Development y Testing.

## Decision

En esos dos entornos, un middleware fija el feature con `TrustedSessionRead:SiteId` y `TrustedSessionRead:SensorId`. Hacen falta los dos valores. No lee cabeceras, query ni cuerpo. En cualquier otro entorno el middleware no se registra y el guard de `SessionEndpoint` sigue igual.

## Alternatives

- Ámbito enviado por el navegador: la petición autorizaría la sede.
- Instalar el feature en Production: anula el 401 aunque el guard lo tape después.
- OIDC en este slice: corresponde a S12.

## Consequences

Con las dos variables, el detalle de desarrollo puede devolver 200 sin cabeceras de ámbito. Sin ellas, sigue el 401. Las pruebas que sustituyen el proveedor no cambian. Revertir es quitar el middleware y su registro.
