const WebSocket = require('ws');
const http = require('http');

const PORT = process.env.PORT || 3000;
const SERVER = `ws://localhost:${PORT}`;
const HTTP_SERVER = `http://localhost:${PORT}`;
const PROJECT_ID = 'PROJ-TEST-AC601';

console.log(`[TESTER] Inicializando Bots Carla y Beto apuntando a ${SERVER}...`);

const carla = new WebSocket(SERVER);
const beto = new WebSocket(SERVER);

// Estado de Optimistic UI local de los bots
let carlaState = { props: {} };
let betoState = { props: {} };

// Funciones Auxiliares
const sleep = ms => new Promise(r => setTimeout(r, ms));

const sendWithLatency = (ws, payload, latencyMs) =>
  new Promise(r => setTimeout(() => {
    ws.send(JSON.stringify(payload));
    r();
  }, latencyMs));

let globalClientSeq = Date.now(); // Siempre mayor que cualquier valor anterior en seqCache del Hub

const makeDelta = (userId, op, entityId, props) => ({
  op,
  id: entityId,
  projectId: PROJECT_ID,
  client_seq: globalClientSeq++,
  server_seq: null,
  user: userId,
  state: 'LIVE',
  props: props || {}
});

function arraysEqual(a, b) {
    if (a === b) return true;
    if (a == null || b == null) return false;
    if (a.length !== b.length) return false;
    for (var i = 0; i < a.length; ++i) {
      if (Math.abs(a[i] - b[i]) > 0.0001) return false; // Tolerancia
    }
    return true;
}

// ── FASE 5: Verificación AC-403 vía HTTP ──────────────────────────────────
function fetchSnapshot() {
    return new Promise((resolve, reject) => {
        http.get(`${HTTP_SERVER}/api/snapshot`, (res) => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                try {
                    const entities = JSON.parse(data);
                    // Convertir array a map para facilitar uso en tester
                    const map = {};
                    entities.forEach(e => map[e.id] = e);
                    resolve({ entities: map });
                } catch (e) {
                    reject(e);
                }
            });
        }).on('error', reject);
    });
}

// ── El Verificador LWW ───────────────────────────────────────────────────
async function waitForMergedState(entityId, expected, timeoutMs = 3000) {
    return new Promise((resolve, reject) => {
      const deadline = setTimeout(() => {
        carla.removeAllListeners('message');
        beto.removeAllListeners('message');
        reject(new Error(`[AC-401 ❌] Timeout: estado fusionado no llegó en ${timeoutMs}ms`));
      }, timeoutMs);
  
      let carlaSaw = false, betoSaw = false;
      // Reset states at start of test so they don't pollute each other
      carlaState.props = carlaState.props || {};
      betoState.props = betoState.props || {};
  
      const check = (userId, msg, localState) => {
        if (msg.id !== entityId) return;
        
        // AutoCAD-like local merge
        if (msg.props) {
            localState.props = { ...(localState.props || {}), ...msg.props };
        }
        
        const colorOk = expected.expectedColor == null ||
                        localState.props.color === expected.expectedColor;
        const geomOk  = expected.expectedEnd == null ||
                        arraysEqual(localState.props.geom?.end, expected.expectedEnd) ||
                        arraysEqual(localState.props.geom?.center, expected.expectedCenter);
  
        if (colorOk && geomOk) {
          console.log(`[${userId.toUpperCase()} ✅] Estado fusionado correcto:`, JSON.stringify(localState.props));
          userId === 'carla' ? (carlaSaw = true) : (betoSaw = true);
        } else {
          // Aún no llegan todos los deltas
        }
  
        if (carlaSaw && betoSaw) {
          clearTimeout(deadline);
          console.log('[AC-401/402 ✅] PASS — Ambos clientes convergieron al estado final esperado.');
          carla.removeAllListeners('message');
          beto.removeAllListeners('message');
          resolve();
        }
      };
  
      carla.on('message', raw => check('carla', JSON.parse(raw), carlaState));
      beto.on('message',  raw => check('beto',  JSON.parse(raw), betoState));
    });
  }

// ── EJECUCIÓN (Interfaz de Comandos TTY) ──────────────────────────────────
const readline = require('readline').createInterface({
    input: process.stdin,
    output: process.stdout
});

console.log(`
====== H-SYNC NEURAL AC-601 TESTER ======
Espera a que Alan (Tú en AutoCAD) dibuje las geometrías antes de disparar las fases.

Comandos disponibles:
 f3 <UUID>  : Disparar Mutación Concurrente Segura (Alan dibuja LINE)
 f4 <UUID>  : Disparar Colisión Sangrienta LWW (Alan dibuja CIRCLE)
 f5 <U1,U2> : Simular Desconexión y Borrado (Alan usa COPY)
 q          : Salir
=========================================
`);

readline.on('line', async (line) => {
    const args = line.trim().split(' ');
    const cmd = args[0];

    try {
        if (cmd === 'f3' && args[1]) {
            const uuid = args[1];
            console.log(`\n[FASE 3] Iniciando mutación concurrente en UUID: ${uuid}...`);
            
            const deltaCarla = makeDelta('carla', 'UPDATE', uuid, { color: 1 });
            const deltaBeto = makeDelta('beto', 'UPDATE', uuid, { geom: { start: [0,0,0], end: [20,20,0] } });

            // Optimistic UI update (como haría AutoCAD)
            carlaState.props = { ...(carlaState.props||{}), ...deltaCarla.props };
            betoState.props = { ...(betoState.props||{}), ...deltaBeto.props };

            await Promise.all([
                sendWithLatency(carla, deltaCarla, 50),
                sendWithLatency(beto,  deltaBeto,  150),
            ]);
            console.log('[FASE 3] Deltas enviados. Esperando respuesta del servidor...');
            await waitForMergedState(uuid, { expectedColor: 1, expectedEnd: [20, 20, 0] });
        }
        else if (cmd === 'f4' && args[1]) {
            const uuid = args[1];
            console.log(`\n[FASE 4] Iniciando colisión sangrienta (LWW) en UUID: ${uuid}...`);
            
            // Hack para el Test: Forzar la existencia de la entidad en el Hub antes de colisionar
            // (Ya que AutoCAD todavía no envía el CREATE nativo)
            const deltaCreate = makeDelta('carla', 'CREATE', uuid, { geom: { center: [0, 0, 0], radius: 1 } });
            await sendWithLatency(carla, deltaCreate, 0);
            await sleep(100); // Esperar que el Hub lo asiente
            
            const deltaCarla = makeDelta('carla', 'UPDATE', uuid, { geom: { center: [100, 100, 0], radius: 5 } });
            const deltaBeto = makeDelta('beto', 'UPDATE', uuid, { geom: { center: [-50, -50, 0], radius: 5 } });

            carlaState.props = { ...(carlaState.props||{}), ...deltaCarla.props };
            betoState.props = { ...(betoState.props||{}), ...deltaBeto.props };

            // Diferencia letal de 5ms
            await sendWithLatency(carla, deltaCarla, 0);
            await sendWithLatency(beto,  deltaBeto,  5);

            console.log('[FASE 4] Colisión disparada con delta 5ms. Verificando convergencia a favor de Beto...');
            await waitForMergedState(uuid, { expectedCenter: [-50, -50, 0] });
        }
        else if (cmd === 'f5' && args[1]) {
            const uuids = args[1].split(',');
            console.log(`\n[FASE 5] Simulando borrado de 2 círculos en background...`);
            
            for (const uuid of uuids) {
                const delta = makeDelta('carla', 'DELETE', uuid, {});
                await sendWithLatency(carla, delta, 50);
                console.log(`[CARLA] Borró círculo ${uuid}`);
                await sleep(500);
            }

            console.log('[FASE 5] Alan, reconecta tu WiFi y haz algo en AutoCAD. El tester verificará el Snapshot HTTP.');
            const snapshot = await fetchSnapshot();
            console.log(`[SNAPSHOT HTTP] Entidades activas totales: ${Object.keys(snapshot.entities).length}`);
            
            let passed = true;
            for (const uuid of uuids) {
                if (snapshot.entities[uuid]) {
                    console.log(`❌ ERROR: El UUID ${uuid} sigue existiendo en el Snapshot del Servidor.`);
                    passed = false;
                } else {
                    console.log(`✅ OK: El UUID ${uuid} NO existe (fue purgado por Carla correctamente).`);
                }
            }
            if(passed) console.log('[FASE 5 ✅] AC-403 Validado: El Snapshot purga los objetos desconectados correctamente.');
        }
        else if (cmd === 'q') {
            process.exit(0);
        }
        else {
            console.log('Comando inválido. Usa: f3 <uuid>, f4 <uuid> o f5 <uuid1,uuid2>');
        }
    } catch (e) {
        console.error(e.message);
    }
});
