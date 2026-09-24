# Caso práctico: aplicación de monitorización de red

> Versión de consulta en español del documento inglés. Mantiene la estructura, los requisitos, las variantes y los ejercicios del caso; reformula los ejemplos para facilitar su lectura. La fuente prevalece si surge una duda de interpretación. El documento es material del ejercicio, no una orden de implementación literal.

**Fuente:** [`network-monitoring.pdf`](../raw/network-monitoring.pdf), 22 páginas. **Trazabilidad:** los números de página y sección siguientes remiten al PDF.

## 1. Especificación técnica de la arquitectura (pp. 2–6)

### 1.1 Visión general (pp. 2–3)

El sistema combina tres vistas: arquitectura base de sonda, Kafka, almacenamiento de sesiones, interfaz y PKI; variante de sonda que captura con `tshark`; y ampliación con descubrimiento de dispositivos, gestión web, DLL de dominio compartida, indexación de sesiones en Elasticsearch mediante Kafka Connect y RBAC con Keycloak. Está dirigido a entornos industriales o empresariales donde importan la observación pasiva, la visibilidad de activos y las comunicaciones seguras.

La sonda recibe una copia del tráfico de un puerto SPAN o TAP, extrae metadatos de conexiones (IP y puertos de origen/destino, protocolo, marcas de tiempo, duración y bytes) y publica eventos estructurados JSON o Avro en Kafka. Un consumidor valida, enriquece, deduplica y guarda sesiones en una base relacional. La API y la interfaz consultan sesiones y dispositivos. EJBCA emite certificados X.509 para mTLS interno. Keycloak autentica a las personas y suministra roles para la autorización.

### 1.2 Componentes (pp. 3–4)

| Componente | Responsabilidades del caso |
|---|---|
| Sonda de red | Microservicio próximo al segmento; `tshark` en interfaz promiscua; identifica flujos y sesiones, descubre MAC/IP, ARP, LLDP/CDP; publica `sessions.detected` y `devices.detected`; certificado EJBCA para Kafka. |
| Kafka | Tres o más brokers como ejemplo de alta disponibilidad; desacopla productores y consumidores; particiones para paralelismo según clave; TLS/mTLS para clientes. |
| Procesador y API | Tecnología abierta entre Java/Spring Boot, Python/FastAPI o Node.js/Express; consume, valida, enriquece, deduplica y aplica TTL; persiste en PostgreSQL/TimescaleDB; REST o gRPC para filtrar/listar/ver sesiones y gestionar dispositivos (`GET /devices`, `POST /devices`). Arquitectura limpia/hexagonal con adaptadores de entrada y salida. |
| Aplicación web | SPA o aplicación renderizada en servidor; inicio de sesión OIDC en Keycloak; listado, filtrado y gestión de dispositivos; envía token Bearer a la API, que aplica RBAC. |
| EJBCA | CA interna; emisión, renovación y revocación de certificados de sondas, brokers, API, consola de integración y, opcionalmente, Kafka Connect. |
| Keycloak | Usuarios, clientes, roles y grupos; OIDC/OAuth2; tokens ID y de acceso; validación de JWT y RBAC en backend. |

### 1.3 Estilo, herramientas, seguridad y despliegue (pp. 4–6)

El caso propone microservicios y eventos, publicación/suscripción y arquitectura limpia por servicio. La sonda puede envolver `tshark` con C, Go o Python. Propone Kafka y Schema Registry; backend REST/gRPC; PostgreSQL o TimescaleDB; SPA React, Angular o Vue; EJBCA y Keycloak. Son opciones del enunciado, no una elección cerrada de lenguaje o interfaz.

Cada cliente y servidor internos presenta certificado EJBCA. Kafka exige autenticación de cliente (`ssl.client.auth=required`) y ACL vinculadas a identidades. La API valida firma, caducidad y emisor del token de usuario y aplica roles. Kafka escala mediante brokers y particiones; API con réplicas tras balanceador; base de datos con recursos, réplicas y particionado temporal; sondas por segmento y sin estado salvo configuración. Se contemplan servicios analíticos, SIEM/SOAR, detección de anomalías, fingerprinting y nuevas exportaciones como extensiones.

## 2. Variante de sonda con `tshark` (pp. 6–8)

La sonda ejecuta `tshark` sobre la interfaz SPAN/TAP. `-f` filtra en captura y `-Y` filtra tras decodificar. El ejemplo extrae `ip.src`, `ip.dst`, puertos TCP, `frame.time_epoch` e `ip.proto` con `-T fields`; `-l` fuerza salida en líneas para transmisión continua. Pueden agregarse MAC, VLAN, LLDP/CDP y DNS.

Un proceso envoltorio lee la salida línea a línea, la convierte en entidades `Session` y `Device` y publica eventos con esquema estable. Un pipe `tshark | nc | kafka-console-producer` se reserva como prototipo; en producción el proceso fino debe parsear, validar, serializar y publicar. Backend, Kafka Connect y futuros consumidores leen Kafka sin depender del motor de captura. La sonda se autentica con certificado EJBCA, Kafka autoriza mediante ACL; el transporte mTLS protege el canal, aunque esto no equivale a una firma independiente de cada evento.

## 3. Ampliación del sistema (pp. 8–10)

El descubrimiento observa pares MAC/IP, ARP, LLDP/CDP y otros mensajes pasivos. Cada dispositivo puede incluir MAC, IP asociadas, nombre de host e información opcional de interfaz, VLAN o puerto. La sonda publica `devices.detected`.

La API expone `GET /devices` y `POST /devices`; la UI lista y crea manualmente. El caso añade una *consola de integración* que consume `devices.detected`, deserializa con una DLL de dominio compartida y llama a `POST /devices`. Así, altas manuales y automáticas pasan por las mismas reglas de validación, unicidad y consistencia de un inventario autoritativo. La DLL contiene `Session`, `Device`, tipos de apoyo y enumeraciones, y la usan sonda, backend y consola. Se reconoce explícitamente el acoplamiento entre servicios a cambio de menos mapeos y mayor alineación de tipos.

La ampliación guarda sesiones en base relacional y las indexa además en Elasticsearch. Kafka Connect ejecuta un sink de Elasticsearch que lee `sessions.detected`, con URL del destino, nombre de índice, reintentos y otros parámetros. El caso plantea como pregunta de mentoría cómo enviar sesiones de Kafka a Elasticsearch de forma escalable y mantenible; su respuesta prevista es Kafka Connect con ese sink. Esto forma parte del caso original, aunque la propuesta arquitectónica española recomienda otra solución inicial.

### Ejercicios específicos de arquitectura (pp. 10–11)

1. Dibujar componentes (sonda, Kafka, backend/API, consola, Kafka Connect, base relacional, Elasticsearch y UI) y despliegue (máquinas, contenedores o Kubernetes, zonas de red y fronteras).
2. Escribir ADR para Kafka frente a HTTP directo, DLL de dominio compartida y Kafka Connect hacia Elasticsearch, cada uno con contexto, alternativas y consecuencias.
3. Comparar opciones para Kafka, Elasticsearch y la DLL por complejidad, rendimiento y mantenibilidad.
4. Definir atributos de calidad y métricas medibles: latencia de extremo a extremo, disponibilidad de Kafka/API, presupuesto de errores y umbrales de alerta.

## 4. Identidad y acceso con Keycloak (pp. 11–13)

Keycloak gestiona usuarios humanos, aplicaciones cliente, roles, grupos y flujos de autenticación (login, MFA opcional y recuperación). La UI inicia sesión por OIDC, recibe tokens ID y de acceso JWT, y envía el token de acceso en `Authorization: Bearer`. La API valida firma, audiencia, emisor y caducidad, extrae identidad y roles y protege `/sessions`, `/devices` y administración. EJBCA/mTLS se ocupa de la identidad y del canal entre componentes; Keycloak de la identidad y permisos de usuarios.

Modelo ilustrativo, pendiente de concretar: `ROLE_ADMIN` administra configuración, dispositivos, conectores y delegación; `ROLE_ANALYST` consulta, filtra y exporta sin operaciones críticas; `ROLE_AUDITOR` solo lee; `ROLE_INTEGRATION` crea o actualiza dispositivos y no usa funciones interactivas. El ejemplo permite `GET /sessions` y `GET /devices` a analista y auditor, `POST /devices` a administrador e integración, y administración solo al administrador. La API obtiene claves JWKS de Keycloak y usa middleware/filtros para roles. La consola puede usar credenciales de cliente con `ROLE_INTEGRATION`, junto a mTLS.

Ejercicios: diseñar roles y permisos según necesidades reales; diagramar login, tokens y llamada a API; elaborar matriz endpoint/rol; justificar Keycloak frente a IdentityServer, Auth0 o Cognito mediante un ADR.

## 5. Ejercicios de introducción a la arquitectura (pp. 13–16)

### 5.1 Mentalidad arquitectónica

- Comparar la nota de quien implementa `POST /devices` con la de quien diseña el flujo completo sonda → Kafka → consola → backend → UI: alcance local frente a sistema, detalles frente a fronteras y efectos globales.
- Para tres decisiones (Kafka, DLL y Kafka Connect), distinguir beneficio del sprint e impacto futuro en mantenimiento, evolución, rendimiento y autonomía del equipo.
- Cambiar prioridades para tres escenarios hipotéticos: operador regulado de infraestructura crítica, startup SaaS de visibilidad de red y herramienta interna de un pequeño equipo; proponer dos cambios de arquitectura para cada uno.
- Esbozar componentes de una función, como filtrar sesiones o crear dispositivos, y anotar fronteras, dos compromisos y motivos de elección.

### 5.2 Atributos de calidad

Identificar al menos ocho atributos (rendimiento, seguridad, escalabilidad, mantenimiento, fiabilidad, usabilidad, interoperabilidad, etc.), priorizarlos con un stakeholder hipotético y evaluar apoyo y debilidades de la arquitectura para los tres más importantes. Redactar de tres a cinco escenarios **estímulo–entorno–respuesta**; mapear componentes y ajustes necesarios. El ejemplo exige consultas de sesiones por debajo de un segundo en p95 bajo un pico de `N` sondas; se ofrece como ejercicio, no como SLO ya aprobado. Crear una matriz de impacto positivo, negativo o neutro de Kafka, DLL, Kafka Connect, Keycloak, EJBCA y la descomposición de servicios sobre los atributos.

### 5.3 Funciones de aptitud

Proponer al menos cinco funciones que midan rendimiento de `/sessions`, seguridad de JWT/TLS y dependencias, reglas estructurales (UI sin acceso directo a base de datos, sonda sin paquetes exclusivos del backend) y fiabilidad. Indicar qué se mide, herramienta o script y umbral. Automatizar dos o tres reglas en CI o infraestructura (por ejemplo mediante pruebas de arquitectura, política como código o pruebas de infraestructura), frecuencia y resultado al fallar.

### 5.4 Métricas y herramientas

Diseñar panel con tres a cinco métricas por categoría, origen y objetivo; mapear atributo → métrica → herramienta para rendimiento, seguridad, mantenimiento y fiabilidad. El caso da como ejemplos JMeter, APM, Prometheus, SonarQube y escáneres. Interpretar una tendencia hipotética de tres sprints: `/sessions` pasa de 150 a 350 ms, índice de mantenibilidad baja diez puntos, tasa de error estable y aparece una vulnerabilidad transitiva alta; priorizar medidas y prevención. Cerrar con reflexión de una página sobre visión global, compromisos, negocio, aptitud y métricas.

## 6. Ejercicios de diseño de software (pp. 16–18)

- **Componentes:** clasificar al menos cuatro componentes por presentación, negocio o datos; minimizar acoplamiento; refactorizar un ejemplo con UI que accede a la base; descomponer un gestor monolítico en módulos cohesionados y explicar sus flujos.
- **Estilos:** seleccionar y justificar estilos para web de contenido variable, herramienta interna y juego multijugador; comparar monolito, microservicios y capas, patrones asociados y efectos de cambios de escala; estudiar un sistema conocido y su evolución.
- **ADRs:** redactar uno para base de datos, frontend o despliegue; identificar tres decisiones de otro caso; crear una plantilla de estado, contexto, decisión, alternativas y consecuencias, y aplicarla a un ejemplo como una caché Redis.
- **Diagramas:** representar componentes/dependencias y despliegue/nodos/red; interpretar y criticar un diagrama, indicar partes confusas y cuándo conviene una vista adicional o secuencia.

## 7. Ejercicios de implementación y mantenimiento (pp. 18–20)

- Implementar esqueletos de componentes que respeten fronteras, refactorizar código que mezcla UI y persistencia, y planear/medir una optimización de un componente crítico (asincronía, caché o pooling).
- Diseñar pruebas unitarias, de integración y de sistema; implementar pruebas de ejemplo con dobles y aserciones; inyectar un fallo y documentar qué detectan y qué falta. JUnit, pytest y Selenium son ejemplos de herramientas, no requisitos.
- Diseñar CI/CD con checkout, build, pruebas y despliegue; comparar despliegue blue-green, canary y rolling; automatizar una parte, por ejemplo contenedor y compose o script. GitHub Actions, Jenkins, GitLab CI, Docker y proveedores públicos aparecen como ejemplos.
- Planificar KPI y alertas; leer registros y paneles para un informe de incidente; revisar regularmente diagramas/ADRs, refactorizaciones y comunicación cuando crezcan usuarios o se añada un cliente.

## 8. Ejercicios de habilidades y estrategia (pp. 21–22)

- Explicar una idea técnica compleja a una persona no técnica mediante beneficios y analogías.
- Facilitar un desacuerdo entre desarrolladores sénior escuchando razones, conectando con objetivos y acordando una decisión.
- Practicar escucha y resolución de un conflicto de estándares de código.
- Relacionar iniciativas arquitectónicas con una estrategia empresarial y métricas; definir dos o tres métricas North Star.
- Dibujar un mapa Wardley de necesidades, cadena de valor y madurez para orientar decisiones de construir u obtener componentes.
- Realizar Event Storming de un proceso con eventos, comandos y agregados, y registrar hallazgos.
- Elaborar un registro de riesgos técnicos y de negocio con impacto, probabilidad y mitigación.

## Qué se ha normalizado y qué sigue abierto

Se eliminaron repeticiones de maquetación, se tradujeron los términos narrativos y se agruparon ejemplos genéricos. Se conservaron todas las capacidades, alternativas y familias de ejercicios de las ocho secciones. No se han convertido los ejemplos de SLO, roles, stack o despliegue en requisitos confirmados. La propuesta en español discrepa de la variante ampliada del caso en almacenamiento, indexación, integración y DLL; las decisiones documentadas deben explicar esa diferencia.
