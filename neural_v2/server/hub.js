const { WebSocketServer, WebSocket } = require('ws');

const PORT = process.env.PORT || 3000;

// Estado en Memoria (RAM-based para Fase 1)
const seqCache = new Map(); // Para Idempotencia (AC-203)
const deltaHistory = [];    // Historial de la sala (Fallback de Redis)
const stateMap = new Map(); // AC-401: Mapeo exacto UUID -> Estado Consolidado
let globalServerSeq = 1000;

// Temporizadores de latidos (Heartbeat - AC-305)
const aliveTimers = new Map();

// AC-403: Servidor HTTP para Validación de Estado
const http = require('http');
const server = http.createServer((req, res) => {
    if (req.url === '/api/snapshot' && req.method === 'GET') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(Array.from(stateMap.values())));
    } else {
        res.writeHead(404);
        res.end();
    }
});
server.listen(PORT, () => {
    console.log(`[H-SYNC HUB] Iniciando servidor HTTP/WS NEURAL en puerto ${PORT}...`);
});

const wss = new WebSocketServer({ server });

wss.on('connection', (ws, req) => {
    const ip = req.socket.remoteAddress;
    ws.userId = null; // Se asienta durante el CONNECT_REQ

    // Configurar Timeout inicial de 120s
    resetHeartbeatTimer(ws);

    ws.on('message', (data) => {
        try {
            const payload = JSON.parse(data.toString('utf8'));

            // Handshake & Lifecycle Routing
            switch (payload.type) {
                case 'CONNECT_REQ':
                    handleConnectReq(ws, payload);
                    break;
                case 'ALIVE_HEARTBEAT':
                    resetHeartbeatTimer(ws);
                    break;
                case 'DISCONNECT_REQ':
                    handleDisconnect(ws);
                    break;
                case 'OFFLINE_PUSH':
                    handleOfflinePush(ws, payload);
                    break;
                default:
                    // Es un Delta de Geometría estándar
                    handleStandardDelta(ws, payload);
                    break;
            }
        } catch (error) {
            console.error(`[!] Payload corrupto de ${ip}:`, error.message);
        }
    });

    ws.on('close', () => {
        handleDisconnect(ws);
    });
});

function handleConnectReq(ws, payload) {
    ws.userId = payload.user;
    resetHeartbeatTimer(ws);

    const clientSeq = payload.checkpointSeq || 0;
    const diff = globalServerSeq - clientSeq;

    let syncMode = 'PATCH';
    let payloadData = [];

    // Lógica AC-301 y AC-302
    if (diff > 5000 || clientSeq === 0) {
        syncMode = 'SNAPSHOT';
        // Construir Snapshot a partir del StateMap consolidado (AC-401)
        payloadData = Array.from(stateMap.values()); 
    } else {
        // Entregar solo los deltas que le faltan (PATCH)
        payloadData = deltaHistory.filter(d => d.server_seq > clientSeq);
    }

    ws.send(JSON.stringify({
        type: 'SESSION_INIT',
        syncMode: syncMode,
        serverSeq: globalServerSeq,
        data: payloadData
    }));
}

function handleOfflinePush(ws, payload) {
    // AC-303: Chunks de Offline Push
    // Payload = { type: 'OFFLINE_PUSH', chunkId: 1, total: 3, deltas: [...] }
    payload.deltas.forEach(d => handleStandardDelta(ws, d, false)); // No brodcastear aún
    
    // Ack para que mande el siguiente
    ws.send(JSON.stringify({
        type: 'CHUNK_ACK',
        chunkId: payload.chunkId
    }));
}

function handleStandardDelta(ws, delta, broadcast = true) {
    const cacheKey = `${delta.projectId}_${delta.user}`;
    const lastSeq = seqCache.get(cacheKey) || 0;

    if (delta.client_seq && delta.client_seq <= lastSeq) return; // Idempotencia
    seqCache.set(cacheKey, delta.client_seq);

    delta.server_seq = ++globalServerSeq;
    
    // RAM History
    deltaHistory.push(delta);

    // AC-401: Fusión de Estado (Automerge simulation)
    if (delta.op === 'CREATE') {
        stateMap.set(delta.id, delta);
    } else if (delta.op === 'UPDATE') {
        const existing = stateMap.get(delta.id);
        if (existing) {
            if (delta.props) {
                existing.props = { ...existing.props, ...delta.props };
            }
            existing.user = delta.user;
            existing.server_seq = delta.server_seq;

            const fixMsg = JSON.stringify({
                type: 'RECONCILE_FIX',
                id: delta.id,
                state: existing.props
            });
            const clientCount = [...wss.clients].filter(c => c !== ws && c.readyState === 1).length;
            console.log(`[HUB] RECONCILE_FIX para '${delta.id}' -> ${clientCount} clientes (excluye sender: ${delta.user})`);
            broadcastMessage(fixMsg, ws);
        } else {
            console.log(`[HUB] ⚠️ UPDATE para '${delta.id}' IGNORADO: no existe en stateMap`);
        }
    } else if (delta.op === 'DELETE') {
        stateMap.delete(delta.id);
    }
    
    if (broadcast) {
        broadcastMessage(JSON.stringify(delta), ws);
    }
}

function handleDisconnect(ws) {
    if (aliveTimers.has(ws)) {
        clearTimeout(aliveTimers.get(ws));
        aliveTimers.delete(ws);
    }

    if (ws.userId) {
        console.log(`[-] Usuario desconectado: ${ws.userId}. Limpiando Auras... (AC-304/305)`);
        broadcastMessage(JSON.stringify({
            type: 'CURSOR_REMOVE',
            user: ws.userId
        }));
        ws.userId = null;
    }
}

function resetHeartbeatTimer(ws) {
    if (aliveTimers.has(ws)) {
        clearTimeout(aliveTimers.get(ws));
    }
    
    // AC-305: Expulsión a los 10min sin latido (600s para desarrollo)
    const timer = setTimeout(() => {
        console.log(`[!] Timeout de sesión por inactividad. Expulsando socket...`);
        ws.terminate(); 
        handleDisconnect(ws);
    }, 600000);

    aliveTimers.set(ws, timer);
}

function broadcastMessage(rawMsg, excludeWs = null) {
    wss.clients.forEach((client) => {
        if (client !== excludeWs && client.readyState === WebSocket.OPEN) {
            client.send(rawMsg);
        }
    });
}
