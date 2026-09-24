# Flujo completo de la primera entrega de producción

**Estado:** arquitectura candidata según [ADR-010](decisions/ADR-010.md), [ADR-011](decisions/ADR-011.md), [ADR-013](decisions/ADR-013.md) y [ADR-014](decisions/ADR-014.md). [S00](../development/slices.md#s00) modela capacidad y habilita PostgreSQL provisionalmente; [S17](../development/slices.md#s17) valida la ruta integrada con datos representativos. Ni capacidad ni continuidad están demostradas todavía.

```mermaid
flowchart LR
  subgraph SEDES[Varias sedes y sondas: perfil inicial 4 sedes, 8 sondas]
    TAP[SPAN o TAP: copia de tráfico]
    CAP[tshark: extrae campos y cuenta pérdida]
    COR[Adaptador .NET: infiere sesiones y observaciones]
    SP[(Spool persistente por sonda)]
    TAP -->|tráfico copiado; sonda fuera de la ruta| CAP
    CAP -->|campos; sin PCAP ni payload guardado| COR
    COR -->|lotes JSON con IDs, sitio, sonda y UTC| SP
  end

  subgraph SERVIDOR[Servicios .NET en red privada]
    ING[Ingestión: mTLS, valida y confirma]
    WORK[Worker: proyección, reintento y cuarentena]
    API[API: consultas selectivas y RBAC]
    DOM[Dominio: candidatos e inventario manual]
    RET[Retención y borrado]
  end

  subgraph ALMACEN[Almacén: PostgreSQL candidato para sesiones, sujeto a S17]
    INBOX[(Bandeja durable)]
    SES[(Sesiones: 30 días consultables)]
    OB[(Observaciones)]
    CAND[(Candidatos)]
    DEV[(Inventario confirmado)]
    Q[(Cuarentena)]
  end

  subgraph PERSONAS[Acceso privado de personas]
    USER[Analista, auditor, administrador]
    UI[Angular]
    IDP[IdP OIDC apto]
    USER --> UI
    UI <-->|sesión OIDC| IDP
  end

  subgraph CONTINUIDAD[Continuidad y operación: capacidades por confirmar]
    HA[HA gestionada o conmutación operada]
    REP[(Réplica del almacén)]
    WAL[(Copia base y archivo WAL externos)]
    PITR[Restauración aislada y PITR]
    OBS[Receptor de métricas y alertas]
    ONCALL[Guardia y runbooks]
    PKI[CA y rotación de certificados]
    HA -->|conmutación ordinaria| REP
    WAL -->|recuperación de desastre| PITR
    OBS -->|alerta con destinatario| ONCALL
  end

  SP -->|envío/reintento del mismo lote; mTLS| ING
  ING -->|commit único y durable| INBOX
  INBOX -->|confirmación| ING
  ING -->|ACK después del commit| SP
  INBOX -->|pendientes| WORK
  WORK -->|sesiones válidas| SES
  WORK -->|observaciones válidas| OB
  WORK -->|inválidos y causa mínima| Q
  WORK -->|marca procesado; atomicidad solo si comparte almacén| INBOX
  OB -->|MAC y ámbito| DOM
  DOM -->|crea candidatos; no fusiona por IP| CAND
  UI -->|HTTPS y token| API
  API -->|tiempo, sitio/sonda, IP y cursor| SES
  API -->|consulta autorizada| CAND
  API -->|consulta autorizada| DEV
  API -->|cambio manual autorizado| DOM
  DOM -->|alta, corrección o fusión auditada| DEV
  DOM -->|confirma o rechaza| CAND
  RET -->|expira sesiones al cumplir 30 días| SES
  RET -->|procesados según política; conserva pendientes| INBOX
  RET -->|plazo aprobado| Q

  ALMACEN -.->|replicación; lag medido| REP
  ALMACEN -.->|copia consistente y WAL continuo| WAL
  CAP -.->|descartes| OBS
  SP -.->|ocupación, edad, pérdida y drenaje| OBS
  ING -.->|ACK, duplicados y errores| OBS
  WORK -.->|backlog y cuarentena| OBS
  API -.->|latencia y denegaciones| OBS
  HA -.->|estado de conmutación| OBS
  WAL -.->|copia y antigüedad| OBS
  PKI -.->|emite/rota/revoca certificado| SP
  PKI -.->|certificado de servidor| ING
```

Las flechas continuas son datos y acciones; las punteadas son operación. Cada sonda conserva IDs hasta ACK y dimensiona su spool para **al menos 4 h a su tasa sostenida más una ráfaga 5× de 15 min**, con margen estimado en S00. Si se agota, el descarte se contabiliza y alerta. El worker proyecta y marca procesado en una transacción solo si ambas escrituras comparten almacén transaccional. Si una decisión posterior separa los almacenes, un ADR fija idempotencia y recuperación ante fallos parciales antes de implementarla. Los inválidos se aíslan y los pendientes siguen siendo reintentables.

La API, ingestión y worker comparten módulos de dominio, aunque pueden escalarse por proceso. OIDC identifica personas; mTLS identifica sondas. La consulta de más de 24 h hasta 30 días requiere sitio/sonda e IP de un extremo, con cursor, página máxima 100 y timeout ≤ 10 s. La [matriz de acceso](../product/functional-scope.md#matriz-de-acceso) rige cada operación.

PostgreSQL es la primera opción candidata. **S17 puede cambiar el almacén de sesiones** si no cumple los objetivos y coste de [ADR-013](decisions/ADR-013.md); ese cambio requiere ADR y evita dos históricos autoritativos permanentes. Si permanece PostgreSQL, la continuidad requiere HA y réplica más copia base y WAL externos: una réplica no sustituye una copia. La conmutación y PITR se demuestran en S16–S17. El receptor, la guardia y la CA se inventarían y asignan antes de producción; [ADR-011](decisions/ADR-011.md) explica cuándo Prometheus o Grafana cubrirían una carencia real.
