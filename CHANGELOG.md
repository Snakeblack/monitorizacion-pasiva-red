# Changelog

## [0.1.0] - 2026-09-30

### Added

- **Vista Angular del detalle (S04)**: ficha interna en `src/monitoring-web` para una sesión sintética proyectada, con estados de carga, éxito, vacío y error. El ámbito de lectura sale de la configuración del servidor en Development y Testing (`TrustedSessionReadScope`). La vista no se publica con el host. Ciclo SDD completo (ruta standard) con TDD estricto. Verificación: PASS WITH WARNINGS; 122 pruebas .NET y 15 pruebas Angular. Las advertencias de aserción de sede/sonda y de procedencia TDD quedan en el seguimiento del archivo.

### Changed

- **Integración continua**: el job verifica la solución .NET y la suite Angular con Node 24.16.0, y comprueba que el host publicado no incluye la SPA.
- **Hoja de ruta**: S01–S04 quedan como ruta vertical implementada. S05 y los slices posteriores siguen pendientes.
