---
name: Monitorización pasiva — vista interna
version: 0.1.0
kind: product-design-system
status: active
defaultMode: hybrid
modes:
  - hybrid
principles:
  - calm-density
  - content-first
  - quiet-chrome
  - semantic-color
  - keyboard-productivity
  - restrained-motion
themes:
  - light
  - dark
fontFamily:
  sans: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
density:
  desktop: compact
controlHeight:
  compact: 30px
  regular: 34px
rowHeight:
  compact: 34px
  regular: 38px
radius:
  xs: 4px
  sm: 6px
  md: 8px
---

# DESIGN — Vista interna de sesión

Especificación visual del producto. Semilla adaptada de `linear-attio-ui` (`assets/DESIGN.template.md`): principios y tokens semánticos originales, sin activos de marca, logotipos ni código propietario.

Modo: **hybrid**. Cromado bajo (navegación y cabecera) + superficie de datos densa (ficha de sesión). No hay listado ni inspector en este slice.

---

## 1. Intención

La interfaz es una herramienta operativa interna:

- rápida, no animada;
- densa, no espaciosa por decoración;
- precisa, no ornamental;
- el trabajo (la ficha) se percibe antes que el cromado.

Regla: reducir decoración antes de reducir información útil.

Prohibido en pantallas de aplicación: portada comercial, tipografía hero, degradados decorativos, glass, glow, blobs o sombras sin jerarquía funcional.

---

## 2. Modo

Usar solo `hybrid`:

- cromado compacto y silencioso;
- ficha/registro como superficie principal;
- un acento semántico;
- movimiento corto;
- temas claro y oscuro.

No mezclar tratamientos componente a componente. Este slice no usa inspector de 320–440 px (no hay listado).

---

## 3. Jerarquía

1. Contenido de la ficha (campos de sesión).
2. Estado de la consulta (éxito, vacío, error) en texto.
3. Acción contextual (reintentar).
4. Navegación y orientación.
5. Cromado persistente.

La navegación queda un paso de contraste por debajo de la superficie de trabajo.

Mecanismos, en este orden: posición, alineación, espaciado, peso tipográfico, cambio de superficie, separador sutil, color semántico.

---

## 4. Color (tokens)

Roles semánticos. Los valores viven en `src/monitoring-web/src/styles.css`. No inventar hex en componentes.

```text
--ui-bg
--ui-surface-1
--ui-surface-2
--ui-surface-elevated
--ui-border-subtle
--ui-border-strong
--ui-text
--ui-text-muted
--ui-text-faint
--ui-icon
--ui-icon-muted
--ui-accent
--ui-accent-soft
--ui-focus
--ui-danger
--ui-warning
--ui-success
```

Un acento para foco, selección y acción primaria. El estado (éxito, vacío, error) se identifica con **texto**, no solo con color.

---

## 5. Tipografía

```text
Inter, ui-sans-serif, system-ui, -apple-system,
BlinkMacSystemFont, "Segoe UI", sans-serif
```

| Rol | Tamaño | Peso |
|---|---:|---:|
| Metadato denso | 11–12px | 400–500 |
| Control / etiqueta | 12–13px | 450–550 |
| Cuerpo / fila | 13–14px | 400–500 |
| Título de vista | 18–24px | 600–700 |

Cuerpo 13–14 px. Título 18–24 px. Nunca escala de landing. Interlineado 1.35–1.5 en cuerpo.

---

## 6. Densidad y radio

Ritmo: 2, 4, 6, 8, 12, 16, 20, 24, 32 px.

Escritorio:

```text
control compacto     28–30px
control regular      32–36px
ítem de navegación   28–34px
fila de ficha        32–40px
icono                14–16px
```

Radio: 4 / 6 / 8 px. Evitar radios de consumidor (16–24 px) en contenedores rutinarios.

Bordes solo donde estructuran (paneles, foco). Sin rectángulo alrededor de cada grupo. Sombra solo en superficies flotantes (no en la ficha estática).

---

## 7. Shell

```text
┌──────────────────────────────────────────────┐
│ cabecera compacta          [plegar nav]      │
├────────────┬─────────────────────────────────┤
│ nav quieta │ superficie de trabajo           │
│ Detalle    │ router-outlet (ficha)           │
└────────────┴─────────────────────────────────┘
```

- Cabecera compacta con identidad de vista.
- Navegación plegable; un ítem: «Detalle».
- El control de plegado es un `<button type="button">` nativo (teclado Enter/Espacio).
- Superficie principal con `min-width: 0`.
- Sidebar expandida ≈ 220–260 px; plegada ≈ 48–64 px.
- Sin portada. Sin inspector.

---

## 8. Ficha

Orden: identidad (`eventId`), metadatos (`siteId`, `sensorId`, `occurredAt`), `data`. Filas 32–40 px. El estado se lee en texto («Sesión en ámbito», «No hay sesión en este ámbito», «No se pudo consultar el detalle»).

Controles interactivos: `<button type="button">` con rol y teclado. Prohibido un `div` clicable como única acción.

---

## 9. Estados de interacción

Todo control define, cuando aplique: default, hover, focus-visible, active, selected, disabled, loading, error. El foco visible no se elimina.

---

## 10. Movimiento

80–120 ms en hover/color. Respetar `prefers-reduced-motion`. Sin `transition: all`, sin entradas de página teatrales.

---

## 11. Accesibilidad

HTML semántico, teclado completo, contraste suficiente, estado no solo por color, nombre accesible en acciones solo-icono, zoom usable.

---

## 12. Microcopy

Mayúsculas de frase. Textos operativos, no de marketing. Carga: «Consultando detalle». Acción: «Reintentar consulta».

---

## 13. Anti-patrones

- Degradados, glass, glow, hero 40–64 px.
- Cards anidados, sombras en secciones estáticas.
- Pills en metadatos ordinarios.
- Colores crudos si existe el token.
- Navegación más ruidosa que el contenido.

---

## 14. Contrato de implementación (Angular 22)

Standalone, `ChangeDetectionStrategy.OnPush`, `input()` / `output()` / `computed()` / signals, control flow `@if` / `@for`. CSS propio con tokens; sin kit de marketing. No añadir dependencias para interacciones que el botón nativo ya cubre.
