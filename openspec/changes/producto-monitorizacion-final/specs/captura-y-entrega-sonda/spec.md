# captura-y-entrega-sonda Specification

## Purpose

Extraer y correlacionar metadatos pasivos por sonda y entregarlos con pérdida contabilizada y almacenamiento local acotado (S05–S07).

## Requirements

### Requirement: Extracción pasiva aislada {#REQ-captura-y-entrega-sonda-001}

Cada sonda MUST configurar interfaz y sede/sonda independientemente y extraer metadatos desde SPAN/TAP mediante `tshark`. MUST NOT transmitir tráfico activo ni persistir PCAP/payload. MUST contar paquetes vistos, descartados por captura, errores de parser y reinicios. Una línea inválida MUST NOT crear eventos ni parar las líneas válidas; fallo/ausencia de `tshark` MUST informar estado degradado y reintentar con espera acotada, sin bucle ocupado.

#### Scenario: Fuentes y parser

- GIVEN dos sondas y una línea inválida entre líneas válidas
- WHEN se extraen metadatos
- THEN los válidos conservan su ámbito y el error queda contado sin detener captura

### Requirement: Correlación inferida acotada {#REQ-captura-y-entrega-sonda-002}

La correlación MUST usar cinco-tupla bidireccional canonizada más sede/sonda/VLAN, incluida «sin etiqueta»; los extremos del primer paquete fijan origen/destino. MUST cerrar por inactividad TCP 5 min, UDP 1 min o duración 1 h, con parámetros/versiones explícitos. NAT, VLAN, otras sondas o sesiones posteriores al cierre MUST NOT fusionarse. Otros protocolos MUST producir observaciones, sin sesión. Captura asimétrica/reinicio MUST marcar parcialidad cuando no puede acreditar continuidad; MUST NOT afirmar reconstrucción TCP exacta. Memoria, flujos activos y tiempo de espera MUST tener cotas configuradas; saturación MUST contar descarte y alertar.

#### Scenario: Dirección inversa y ámbitos

- GIVEN paquetes inversos de una cinco-tupla y paquetes iguales de otra VLAN/sonda
- WHEN se correlacionan
- THEN solo los inversos del mismo ámbito comparten sesión inferida

#### Scenario: Cierre y reinicio

- GIVEN reloj controlado con inactividad, duración máxima o reinicio
- WHEN se alcanza el cierre aplicable
- THEN se emite identidad estable, motivo y parcialidad, sin unir el siguiente flujo al anterior

### Requirement: Spool durable y reintento {#REQ-captura-y-entrega-sonda-003}

La sonda MUST persistir evento y sus IDs antes del envío y conservarlos tras reinicio hasta HTTP 200 de cuerpo vacío posterior al commit. MUST reenviar idéntico contenido, incluidos tiempos, con lotes ≤500 eventos/1 MiB. Timeout, ACK perdido, 429 y 5xx MUST conservar datos; reintentos MUST usar backoff exponencial con jitter, espera máxima60 s y respetar `Retry-After` válido. 400/409 MUST aislar el lote con causa para diagnóstico sin contarlo como ACK; 401/403 MUST suspender envíos y alertar identidad, conservando spool. MUST existir límite de solicitudes concurrentes por sonda.

#### Scenario: ACK perdido

- GIVEN aceptación durable cuyo ACK no llegó a la sonda
- WHEN reinicia y reenvía el lote
- THEN conserva IDs/contenido y lo retira solo tras ACK válido sin duplicar sesiones

### Requirement: Cuota local dimensionada y pérdida visible {#REQ-captura-y-entrega-sonda-004}

La cuota por sonda MUST cubrir ≥4 h a tasa sostenida R más una ráfaga5× de15 min dentro del corte: al menos `(4 h×R + 15 min×4R)×bytes/evento×(1+margen)`, incluyendo sobrecarga medida. El margen de laboratorio MUST iniciar en25%; S17 MUST validar/ajustar tasa, tamaños y margen. Disco MUST estar acotado; al agotarse MUST descartar primero eventos no confirmados más antiguos, persistir contador de pérdida y alertar. MUST publicar ocupación, edad, reintentos y drenaje; el descarte MUST NOT contabilizarse como entrega.

#### Scenario: Corte y spool lleno

- GIVEN corte4 h, ráfaga y cuota ensayada, seguido de exceso adicional
- WHEN el spool alcanza su límite
- THEN entrega lo conservado idempotentemente y contabiliza/alerta lo descartado sin exceder disco
