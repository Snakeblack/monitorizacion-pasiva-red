# ADR-001: SPA Angular 22.2.0 fuera del host

- Status: proposed
- Change: s04-vista-angular
- Date: 2026-09-30

## Context

No hay frontend ni versión de Angular en el repo. El detalle ya es `GET /api/v1/sessions/{eventId}`. La vista interna no debe publicarse con el host. El CLI global instalado es 21.2.10; npm publica `@angular/core`, `@angular/cli` y `@angular/build` en 22.2.0.

## Decision

Crear la SPA en `src/monitoring-web/` con CLI 22.2.0 y npm. El runner es `@angular/build:unit-test`. El host no la sirve ni la referencia. Domain y Persistence tampoco.

## Alternatives

- CLI global 21.2.10: no es el estable publicado.
- Estáticos en `Monitoring.Host`: el publish llevaría la vista interna.
- Playwright o un Vitest aparte: fuera de alcance, y el builder oficial ya ejecuta en Node.

## Consequences

CI necesita Node `^22.22.3 || ^24.15.0 || >=26` (comprobado: 24.16.0) y un lockfile. Generar con `npx --yes @angular/cli@22.2.0`. Quitar el directorio revierte la UI sin tocar el contrato HTTP de S03.
