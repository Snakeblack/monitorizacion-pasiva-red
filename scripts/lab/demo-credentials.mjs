import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// Writes lab-secrets/credenciales-demo.txt (ignored by git, because it contains the laboratory password) and prints the same text, so the
// users, their roles and the addresses to try are always in one place:   node scripts/lab/demo-credentials.mjs
const root = fileURLToPath(new URL('../..', import.meta.url));
const password = readFileSync(new URL('../../lab-secrets/keycloak-lab.txt', import.meta.url), 'utf8').trim();

const text = `CREDENCIALES DE LA DEMO (laboratorio, solo en este equipo)
=============================================================

Consola web ........ http://localhost:4200   (tambien http://127.0.0.1:4200)
Inicio de sesion ... te redirige a Keycloak; escribe el USUARIO (no un correo)

Usuario              Rol                          Que ve
-------------------  ---------------------------  ----------------------------------------------
admin-inventario     Administrador de inventario  Sesiones de los dos sitios; confirma/edita inventario
analista             Analista                     Solo el sitio A (pipeline-site)
auditor              Auditor                      Solo el sitio B (lab-site-b)
sin-ambito           Analista sin ambito          Nada: el API responde 403 (prueba de denegacion)

Contrasena (la misma para los cuatro):
  ${password}

Consola de administracion de Keycloak: http://127.0.0.1:8081/admin
  Usuario: lab-admin      Contrasena: la misma de arriba

Prometheus .... http://127.0.0.1:9090      (solo si arrancaste la demo completa)
Elasticsearch . http://127.0.0.1:9200
Kafka Connect . http://127.0.0.1:8083/connectors

Si Keycloak dice "Invalid username or password" con los datos correctos, espera unos minutos:
bloquea temporalmente al usuario tras varios intentos fallidos.
`;
writeFileSync(new URL('../../lab-secrets/credenciales-demo.txt', import.meta.url), text);
console.log(text);
console.log(`(guardado en ${root}lab-secrets/credenciales-demo.txt)`);
