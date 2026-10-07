# Changelog

## [Sin publicar]

### Added

- **Identidad hacia Keycloak por TLS (ADR-025)**: `Identity:TrustedCaPaths` fija las CA que pueden firmar el certificado del proveedor para el canal de descubrimiento y claves; el host no arranca con un fichero inutilizable.
- **Rotación y retirada de claves (ADR-026)**: una clave nueva se acepta en la primera petición; una clave retirada deja de valer en ≈5 min (`Identity:KeysRefreshMinutes`, por defecto 5) porque se desactiva la configuración «última buena conocida» de la librería, que la mantenía aceptada más de 8 minutos.
- **Consola rediseñada** (oscura, estilo Linear/Attio): barra lateral con usuario y rol, rangos de tiempo en un clic, filtros como etiquetas editables con menú «+ Filtro» y atajo `/`, ayuda contextual para consultas de más de 24 h, fechas relativas con el instante UTC exacto, estados de carga/vacío/error y detalle con JSON legible.
- **Laboratorio de demo**: `deploy/compose.demo.yaml`, `scripts/lab/seed-demo.mjs`, `scripts/lab/demo-credentials.mjs`, configuración Angular `lab` (`npm run start:lab`) y las guías de demo e instalación en `docs/`.

### Changed

- El inicio de sesión de la consola ya no se queda mudo si el proveedor no responde, y un 401 sin proveedor de identidad configurado explica la causa.
- El laboratorio sube a 8 las consultas retenidas por usuario (`Search__Leases__MaxPerSubject`); el valor de producción (2) no cambia.

## [0.1.0] - 2026-09-30

### Added

- **Vista Angular del detalle (S04)**: ficha interna en `src/monitoring-web` para una sesión sintética proyectada, con estados de carga, éxito, vacío y error. El ámbito de lectura sale de la configuración del servidor en Development y Testing (`TrustedSessionReadScope`). La vista no se publica con el host. Ciclo SDD completo (ruta standard) con TDD estricto. Verificación: PASS WITH WARNINGS; 122 pruebas .NET y 15 pruebas Angular. Las advertencias de aserción de sede/sonda y de procedencia TDD quedan en el seguimiento del archivo.

### Changed

- **Integración continua**: el job verifica la solución .NET y la suite Angular con Node 24.16.0, y comprueba que el host publicado no incluye la SPA.
- **Hoja de ruta**: S01–S04 quedan como ruta vertical implementada. S05 y los slices posteriores siguen pendientes.
