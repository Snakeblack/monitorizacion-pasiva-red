# Guía de instalación para un agente (LLM) o un equipo nuevo

Documento operativo para **instalar, arrancar y verificar** este proyecto en otro equipo. Está escrito para que lo ejecute un agente de código, pero una persona puede seguirlo igual. Cada paso trae su **comprobación**; no avances si una comprobación falla: ve a [Fallos conocidos](#fallos-conocidos-y-su-arreglo).

Resultado final: el laboratorio completo corriendo (núcleo + identidad real + observabilidad + datos de demo) y las pruebas en verde. Para usarlo y enseñarlo, ver la [guía de demo](../demo/guia-de-demo.md).

## 0. Reglas para el agente

1. **No inventes versiones ni nombres.** Todo lo fijado está en `global.json`, `deploy/versions.env`, `src/monitoring-web/package.json` y `.github/workflows/ci.yml`. Si algo no coincide con esta guía, manda el repositorio.
2. **Pregunta antes de instalar software del sistema** (Docker, .NET, Node, PowerShell, JDK) o de cambiar ajustes globales (WSL, Docker, almacén de certificados). Di qué instalarás y con qué comando.
3. **No cambies la confianza de certificados del sistema ni del navegador.** El laboratorio está diseñado para no necesitarlo.
4. **No subas secretos.** `lab-secrets/` está en `.gitignore` y contiene contraseñas del laboratorio; no lo pegues en commits, issues ni logs compartidos.
5. **Nada de commits automáticos.** Los mensajes de commit y PR no deben atribuir autoría a ningún modelo ni herramienta (convención del repositorio).
6. Los comandos están en **PowerShell 7** (`pwsh`) porque los scripts de laboratorio son `.ps1`; los de Node y Docker son idénticos en Linux, macOS y Windows.
7. Todo es **idempotente**: repetir un paso es seguro. No uses `down --volumes` salvo que el usuario quiera borrar los datos.

## 1. Requisitos

| Herramienta | Versión | Comprobación | Notas |
|---|---|---|---|
| Git | cualquiera reciente | `git --version` | |
| **.NET SDK** | **10.0.303** (`global.json`, `rollForward: latestFeature`) | `dotnet --version` → `10.0.x` | Cualquier SDK 10.0 posterior sirve; el repositorio lo valida. |
| **Node.js** | **24.16.0** (CI) · mínimo 22.22.3 | `node --version` | npm 12 (`packageManager` en la SPA). |
| **Docker** | Docker Engine o Docker Desktop con Compose v2 | `docker --version` y `docker compose version` | **≥ 8 GiB de RAM disponibles** para el daemon (ver Windows). |
| **PowerShell** | 7.x | `pwsh --version` | Necesario para `core.ps1`, `prepare.ps1`. |
| **JDK** | 21 o superior (`javac` y `jar` en el PATH) | `javac -version` | Compila una transformación pura de Kafka Connect. |
| Navegador | Chrome/Edge/Firefox actual | | Para la consola. |
| (Solo E2E) Chromium de Playwright | lo instala Playwright | `npx playwright install chromium` | Opcional. |

Puertos libres en `127.0.0.1`: **4200** (consola), **5080** (API), **8081** (Keycloak), **9090** (Prometheus), **9200** (Elasticsearch), **8083** (Connect). Compruébalos antes (`netstat`/`ss -ltn`).

Espacio: unos **6–8 GiB** de imágenes y volúmenes. Red: la primera vez descarga imágenes Docker y tres plugins de Maven/Confluent con **hash SHA-256 verificado** (si falla la verificación, para; no la saltes).

### Particularidades por sistema

- **Windows (Docker Desktop + WSL2):** reserva memoria a WSL. En `%UserProfile%\.wslconfig`:
  ```
  [wsl2]
  memory=8GB
  ```
  y aplica con `wsl --shutdown`. Con 4 GB el motor acaba devolviendo errores 500. Usa **Docker Desktop** (por defecto los scripts lo usan; solo con `MONITORING_COMPOSE_VIA_WSL=1` usan el daemon de una distro Ubuntu). En Git Bash antepón `MSYS_NO_PATHCONV=1` a los `docker exec` con rutas de Linux.
- **Linux:** el usuario debe poder usar Docker sin `sudo` (grupo `docker`). Usa el socket local; no hace falta ninguna variable.
- **macOS:** Docker Desktop con ≥ 8 GiB asignados en *Settings → Resources*.
- **CRLF:** el repositorio está normalizado por git; no conviertas finales de línea a mano.

## 2. Obtener el código y comprobar herramientas

```powershell
git clone <url-del-repositorio> monitorizacion-pasiva-red
cd monitorizacion-pasiva-red
dotnet --version; node --version; docker compose version; pwsh --version; javac -version
```
**Comprobación:** los cinco comandos imprimen versión y ninguno falla. Si falta alguno, instálalo (pregunta antes, regla 2) y repite.

## 3. Compilar y probar sin Docker (rápido)

```powershell
dotnet restore Monitoring.slnx
dotnet build Monitoring.slnx --no-restore
npm --prefix src/monitoring-web ci
npm --prefix src/monitoring-web test
npm --prefix src/monitoring-web run build
```
**Comprobación:** compila sin errores; las pruebas de la consola terminan con `Tests 178 passed` (el número puede crecer, pero ninguna falla) y el build de producción genera `dist/`.

## 4. Pruebas .NET (necesitan Docker)

```powershell
dotnet test Monitoring.slnx
```
Las pruebas de integración crean su propio PostgreSQL desechable con Testcontainers. **Comprobación:** `Correctas!` con 0 errores. Dos casos conocidos que **no** son tu fallo: las pruebas `RealTshark*` fallan si el equipo no tiene `tshark` instalado (es opcional), y con poca memoria Docker pueden desaparecer contenedores `Ryuk`; ejecuta con el núcleo del laboratorio parado y reintenta.

## 5. Levantar el laboratorio

Elige un nombre de proyecto `monitoring-*` y úsalo siempre igual:

```powershell
$env:MONITORING_COMPOSE_PROJECT = 'monitoring-local'

# 5.1 Núcleo: genera lab-secrets/, descarga y verifica plugins, compila la transformación, publica la API y construye imágenes.
pwsh scripts/lab/core.ps1 -Action up -Project monitoring-local
```
**Comprobación:** termina sin error. `pwsh scripts/lab/core.ps1 -Action health -Project monitoring-local` lista los servicios `postgres kafka connect elasticsearch api` en `running`/`healthy` y `migrate`, `bootstrap` con `ExitCode 0`.

```powershell
# 5.2 Índice, alias y conectores
node scripts/lab/pipeline.mjs
```
**Comprobación:** acaba sin excepción; `curl http://127.0.0.1:8083/connectors` solo funciona tras el paso 5.3 (el puerto aún no está publicado); mientras tanto el propio script espera a que ambos conectores estén `RUNNING`.

```powershell
# 5.3 Keycloak real + API con OIDC + Prometheus + puertos de demo + exportadores
docker compose --env-file deploy/versions.env -p monitoring-local `
  -f compose.yaml -f deploy/compose.e2e.yaml -f deploy/compose.identity.yaml -f deploy/compose.demo.yaml `
  --profile observability --profile observability-lab up -d --wait --wait-timeout 300
```
**Comprobación:** todos `Healthy`. Y:
```powershell
curl http://127.0.0.1:8081/realms/monitoring/.well-known/openid-configuration   # JSON con "issuer":"http://127.0.0.1:8081/realms/monitoring"
curl http://127.0.0.1:5080/health/live                                          # 200
curl http://127.0.0.1:9090/-/ready                                              # Prometheus listo
curl http://127.0.0.1:8083/connectors                                           # ["monitoring-source","monitoring-sink"]
```

```powershell
# 5.4 Datos de demo (80 sesiones en 2 sitios + 10 candidatos de inventario). Repetible.
node scripts/lab/seed-demo.mjs
```
**Comprobación:** imprime `Projected: 80 demo sessions…`. En unos segundos `curl "http://127.0.0.1:9200/sessions-read/_search?q=eventId:demo-a-001"` devuelve 1 resultado.

```powershell
# 5.5 Consola web (terminal aparte, se queda en primer plano)
npm --prefix src/monitoring-web run start:lab
```
**Comprobación:** `http://localhost:4200` responde y redirige a Keycloak (`http://127.0.0.1:8081/realms/monitoring/...`).

```powershell
# 5.6 Usuarios y contraseña de la demo
node scripts/lab/demo-credentials.mjs
```
Muestra los cuatro usuarios (`admin-inventario`, `analista`, `auditor`, `sin-ambito`) y la contraseña aleatoria de este equipo (`lab-secrets/keycloak-lab.txt`). **No la copies a ningún documento del repositorio.**

## 6. Verificación final (E2E, opcional pero recomendada)

```powershell
cd tests/e2e
npm ci
npx playwright install chromium
$env:E2E_IDENTITY = '1'; $env:MONITORING_COMPOSE_PROJECT = 'monitoring-local'
npx playwright test identity-keycloak identity-rotation
```
**Comprobación:** `9 passed` (la rotación espera ≈ 1,5 min porque para y arranca Keycloak a propósito). Los specs de identidad pueden correr con la consola `start:lab` encendida (Playwright la reutiliza y cada spec fija su propia configuración de autenticación). Si repites `identity-keycloak` en menos de 10 min, el administrador puede recibir un 429 (límite de consultas retenidas; ver Fallos). `identity-key-retirement` (≈ 7 min) solo con `E2E_SLOW=1`.

## 7. Parar, reiniciar, borrar

| Acción | Comando |
|---|---|
| Parar conservando datos | `pwsh scripts/lab/core.ps1 -Action down -Project monitoring-local` (o `docker compose -p monitoring-local down`) |
| Volver a arrancar | repite 5.1 y 5.3 (los secretos y volúmenes se reutilizan) |
| **Borrar todo** (pregunta antes) | `docker compose -p monitoring-local down --volumes` y, si se quiere, `lab-secrets/` |
| Tras cambiar código del API | `pwsh scripts/lab/prepare.ps1`; `docker compose … build migrate`; recrear `api` (la imagen del API la construye el servicio `migrate`) |

## Fallos conocidos y su arreglo

| Síntoma | Causa | Arreglo |
|---|---|---|
| `A .NET 10.0 SDK is required.` | SDK equivocado en el PATH | Instala el SDK 10.0.x (`global.json`) |
| `Java compiler 21 or newer is required…` | No hay `javac` | Instala un JDK 21+ y abre una terminal nueva |
| `Plugin checksum failed` | Descarga corrupta o interceptada | Borra `deploy/connect/vendor/*` y repite; **no** desactives la verificación |
| Docker devuelve 500 / contenedores mueren | Poca memoria para el daemon | 8 GiB (ver Windows) |
| `address already in use` | Puerto ocupado | Identifica el proceso (`netstat -ano`/`ss -ltnp`) y libéralo |
| Keycloak tarda >3 min | Primera arranque descargando/importando el realm | Espera; `docker logs monitoring-local-keycloak-1` |
| Consola: «sin proveedor de identidad» | Se arrancó con `start` en vez de `start:lab` | `npm --prefix src/monitoring-web run start:lab` |
| Keycloak: `Invalid parameter: redirect_uri` | Origen distinto de `localhost:4200`/`127.0.0.1:4200` | Usa uno de esos |
| Todo da 401 ≈1 min tras recrear Keycloak | Claves de firma nuevas aún no leídas | Espera un minuto |
| 429 al recargar mucho la consola | Límite de consultas retenidas por usuario | El laboratorio ya lo sube a 8 (`Search__Leases__MaxPerSubject`); espera 10 min o reinicia el servicio `api` |
| «Índice retrasado» en la consola | Restos de pruebas antiguas en la autoridad sin indexar | Para una demo limpia usa volúmenes nuevos; ver la guía de demo |
| Pruebas `RealTshark*` fallan | `tshark` no instalado | Opcional; ignora o instala Wireshark/tshark 4.x |

## Dónde mirar si algo no cuadra

- Visión y límites del laboratorio: [`docs/development/stack.md`](stack.md).
- Decisiones de arquitectura (incluidas identidad y TLS): [`docs/architecture/decisions/`](../architecture/decisions/README.md) — en particular ADR-022 a ADR-026.
- Alertas y qué hacer: [`docs/runbooks/pipeline-alerts.md`](../runbooks/pipeline-alerts.md).
- CI como especificación ejecutable del arranque limpio: `.github/workflows/ci.yml` (job `stack`).
