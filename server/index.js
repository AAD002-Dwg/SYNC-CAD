const express = require('express');
const http = require('http');
const { Server } = require('socket.io');
const multer = require('multer');
const cors = require('cors');
const path = require('path');
const fs = require('fs');
const os = require('os');
require('dotenv').config();
const driveService = require('./googleDriveService');
const authService = require('./authService');

const app = express();
const server = http.createServer(app);
const io = new Server(server, {
    cors: { origin: '*', methods: ['GET', 'POST'] }
});

// ── Helpers ──────────────────────────────────────────────────
function getLocalIP() {
    const interfaces = os.networkInterfaces();
    for (const name of Object.keys(interfaces)) {
        for (const iface of interfaces[name]) {
            if (iface.family === 'IPv4' && !iface.internal) return iface.address;
        }
    }
    return 'localhost';
}
const LOCAL_IP = getLocalIP();

app.use(cors());
app.use(express.json());
app.use(express.static(path.join(__dirname, '../client/dist')));

// ── Multi-tenant: Studio Registry ────────────────────────────
const STUDIOS_FILE = path.join(__dirname, 'studios.json');
let studios = {};

function loadStudios() {
    try {
        if (fs.existsSync(STUDIOS_FILE))
            studios = JSON.parse(fs.readFileSync(STUDIOS_FILE, 'utf8'));
    } catch { studios = {}; }
}
loadStudios();

// ── Per-studio Runtime State ──────────────────────────────────
// layerLocks schema: { [layerName]: { user, lockedAt } }
const studioState = {};
function getStudioState(studioId) {
    if (!studioState[studioId])
        studioState[studioId] = { syncHistory: [], layerLocks: {} };
    return studioState[studioId];
}

// ── Per-studio Persistent Data ────────────────────────────────
const DATA_DIR = path.join(__dirname, 'data');
if (!fs.existsSync(DATA_DIR)) fs.mkdirSync(DATA_DIR);

function dataPath(studioId) {
    return path.join(DATA_DIR, `app-data-${studioId}.json`);
}
function loadData(studioId) {
    try {
        const p = dataPath(studioId);
        if (fs.existsSync(p)) return JSON.parse(fs.readFileSync(p, 'utf8'));
    } catch { /* fall through */ }
    return { projects: [], fileMeta: {} };
}
function saveData(studioId, data) {
    fs.writeFileSync(dataPath(studioId), JSON.stringify(data, null, 2));
}

// ── Studio Middleware ─────────────────────────────────────────
function requireStudio(req, res, next) {
    // Aceptamos Token (JWT) en Authorization header o StudioKey legacy
    const authHeader = req.headers.authorization;
    const legacyKey = req.headers['x-studio-key'];
    let studioId = null;
    let userName = null;

    if (authHeader && authHeader.startsWith('Bearer ')) {
        const token = authHeader.split(' ')[1];
        try {
            const decoded = authService.verifySessionToken(token);
            studioId = decoded.studioId;
            userName = decoded.name || decoded.userName; // Web or Desktop token
        } catch (err) {
            return res.status(401).json({ error: 'Token inválido o expirado' });
        }
    } else if (legacyKey) {
        // Fallback for older plugin versions
        studioId = legacyKey;
        userName = req.body?.user || req.query?.user || 'Usuario Plugin (Legacy)';
    } else {
        return res.status(401).json({ error: 'Falta token de autorización o Studio Key' });
    }

    const studio = studios[studioId];
    if (!studio) {
        return res.status(403).json({ error: 'Studio inválido o no encontrado' });
    }

    req.studioId = studioId;
    req.studio = studio;
    req.userName = userName; // Set identity for further routes
    next();
}

// ── TTL Cleanup for Locks (30 min inactivity) ─────────────────
const LOCK_TTL_MS = 30 * 60 * 1000;
setInterval(() => {
    const now = Date.now();
    for (const studioId of Object.keys(studioState)) {
        const state = studioState[studioId];
        let changed = false;
        for (const [layer, lock] of Object.entries(state.layerLocks)) {
            if (now - new Date(lock.lockedAt).getTime() > LOCK_TTL_MS) {
                delete state.layerLocks[layer];
                changed = true;
            }
        }
        if (changed) io.to(studioId).emit('lock_update', state.layerLocks);
    }
}, 60 * 1000);

// ── Multer ────────────────────────────────────────────────────
const storage = multer.diskStorage({
    destination: (req, file, cb) => {
        const uploadPath = path.join(__dirname, 'uploads');
        if (!fs.existsSync(uploadPath)) fs.mkdirSync(uploadPath);
        cb(null, uploadPath);
    },
    filename: (req, file, cb) => cb(null, file.originalname)
});
const upload = multer({ storage });

// ── API: Health / Info (no auth) ──────────────────────────────
app.get('/api/status', (req, res) => {
    const key = req.headers['x-studio-key'];
    const studio = key ? studios[key] : null;
    const state = studio ? getStudioState(key) : null;
    res.json({
        message: 'Servidor CAD Sync activo',
        serverIp: LOCAL_IP,
        studio: studio ? { name: studio.name } : null,
        history: state ? state.syncHistory : [],
        locks: state ? state.layerLocks : {}
    });
});




// ── API: Auth (Google OAuth) ──────────────────────────────────
app.post('/api/auth/google/admin-link', async (req, res) => {
    const { code, studioId, adminPin } = req.body;
    // PIN Validation prevents guests from replacing the Drive
    if (!adminPin || adminPin !== process.env.STUDIO_ADMIN_PIN) {
        return res.status(403).json({ error: 'PIN de administrador incorrecto' });
    }
    try {
        const tokens = await authService.exchangeCodeForTokens(code);
        if (tokens.refresh_token) {
            if (!studios[studioId]) studios[studioId] = { name: "Studio " + studioId };
            studios[studioId].refreshToken = tokens.refresh_token; 
            fs.writeFileSync(STUDIOS_FILE, JSON.stringify(studios, null, 2));
            res.json({ message: 'Drive del Estudio vinculado exitosamente', studioId });
        } else {
            res.status(400).json({ error: 'No se obtuvo refresh_token. Revoca permisos e intenta de nuevo.' });
        }
    } catch (err) {
        console.error('Error Admin Link:', err);
        res.status(500).json({ error: 'Error vinculando Google Drive' });
    }
});

app.post('/api/auth/google/user-login', async (req, res) => {
    const { credential, studioId } = req.body;
    try {
        // Verifica la identidad en Google
        const user = await authService.verifyGoogleToken(credential);
        if (!studios[studioId]) return res.status(404).json({ error: 'Studio no encontrado' });
        
        // Genera token de sesión JWT
        const sessionToken = authService.generateSessionToken({ 
            googleId: user.googleId, 
            name: user.name, 
            studioId: studioId 
        });
        
        res.json({ token: sessionToken, user });
    } catch (err) {
        res.status(401).json({ error: 'Login fallido' });
    }
});

app.post('/api/auth/desktop-token', requireStudio, (req, res) => {
    // Permite al usuario web generar un token para pegar en AutoCAD
    const token = authService.generateDesktopToken(req.studioId, req.userName);
    res.json({ desktopToken: token });
});

// ── API: Studios (admin) ──────────────────────────────────────
app.get('/api/studios', (req, res) => {
    // Devuelve lista pública de nombres (sin exponer folderIds ni keys)
    const list = Object.values(studios).map(s => ({ name: s.name }));
    res.json(list);
});

// ── API: Files ────────────────────────────────────────────────
app.get('/api/files', requireStudio, async (req, res) => {
    try {
        const folderId = req.query.projectId || req.studio.folderId || process.env.GOOGLE_DRIVE_FOLDER_ID;
        if (!folderId) return res.json([]);
        const files = await driveService.listFiles(folderId, req.studio.refreshToken);
        res.json(files.map(f => f.name));
    } catch (err) {
        console.error('Error listado Drive:', err);
        res.status(500).json({ error: 'Error al listar archivos de Drive' });
    }
});

// ── API: Locks ────────────────────────────────────────────────
app.post('/api/lock', requireStudio, (req, res) => {
    const { layer } = req.body;
    const user = req.userName || req.body.user;
    const state = getStudioState(req.studioId);
    const existing = state.layerLocks[layer];
    if (existing && existing.user !== user) {
        return res.status(403).json({
            error: `La capa ${layer} está siendo editada por ${existing.user}`
        });
    }
    state.layerLocks[layer] = { user, lockedAt: new Date().toISOString() };
    io.to(req.studioId).emit('lock_update', state.layerLocks);
    res.json({ message: `Capa ${layer} reservada para ${user}` });
});

app.post('/api/unlock', requireStudio, (req, res) => {
    const { layer } = req.body;
    const user = req.userName || req.body.user;
    const state = getStudioState(req.studioId);
    if (state.layerLocks[layer] && state.layerLocks[layer].user === user) {
        delete state.layerLocks[layer];
        io.to(req.studioId).emit('lock_update', state.layerLocks);
    }
    res.json({ message: 'Capa liberada' });
});

// Heartbeat: refresca lockedAt para evitar que el TTL expire mientras el usuario trabaja
// POST { layers: ["MUROS","SOLADOS"], user: "Juan" }
app.post('/api/lock/heartbeat', requireStudio, (req, res) => {
    const { layers } = req.body;
    const user = req.userName || req.body.user;
    if (!Array.isArray(layers) || !user)
        return res.status(400).json({ error: 'layers (array) y user requeridos' });
    const state = getStudioState(req.studioId);
    let refreshed = 0;
    const now = new Date().toISOString();
    for (const layer of layers) {
        if (state.layerLocks[layer]?.user === user) {
            state.layerLocks[layer].lockedAt = now;
            refreshed++;
        }
    }
    res.json({ refreshed });
});

// Batch check: POST { layers: ["MUROS","SOLADOS"] }
// Returns: { "MUROS": { locked: false }, "SOLADOS": { locked: true, by: "usuario" } }
app.post('/api/locks/check', requireStudio, (req, res) => {
    const { layers } = req.body;
    if (!Array.isArray(layers))
        return res.status(400).json({ error: 'layers debe ser un array' });
    const state = getStudioState(req.studioId);
    const result = {};
    for (const layer of layers) {
        const lock = state.layerLocks[layer];
        result[layer] = lock
            ? { locked: true, by: lock.user, since: lock.lockedAt }
            : { locked: false };
    }
    res.json(result);
});

// ── API: Sync (Upload) ────────────────────────────────────────
app.post('/api/sync', requireStudio, upload.single('file'), async (req, res) => {
    const { layer, projectId } = req.body;
    const user = req.userName || req.body.user || 'Unknown';
    const file = req.file;
    if (!file) return res.status(400).json({ error: 'No se subió ningún archivo' });

    try {
        // Usamos el projectId (carpeta hija) si viene, sino la raíz del estudio
        const targetFolder = projectId || req.studio.folderId || process.env.GOOGLE_DRIVE_FOLDER_ID;
        if (!targetFolder) throw new Error('folderId no configurado para este estudio');

        const fileStream = fs.createReadStream(file.path);
        await driveService.uploadFile(file.originalname, fileStream, targetFolder, req.studio.refreshToken);
        fs.unlinkSync(file.path);

        const syncEntry = {
            user: user || 'Usuario Desconocido',
            layer: layer || null,
            filename: file.originalname,
            timestamp: new Date().toISOString()
        };

        const appData = loadData(req.studioId);
        if (!appData.fileMeta) appData.fileMeta = {};
        appData.fileMeta[file.originalname] = {
            uploadedBy: syncEntry.user,
            uploadedAt: syncEntry.timestamp,
            layer: layer || null,
            projectId: req.body.projectId || (appData.fileMeta[file.originalname]?.projectId ?? null)
        };
        saveData(req.studioId, appData);

        const state = getStudioState(req.studioId);
        state.syncHistory.unshift(syncEntry);
        if (state.syncHistory.length > 50) state.syncHistory.pop();

        io.to(req.studioId).emit('sync_update', syncEntry);
        res.json({ message: 'Sincronización exitosa', entry: syncEntry });
    } catch (err) {
        console.error('Error Sync Drive:', err);
        res.status(500).json({ error: 'Error al sincronizar con Google Drive' });
    }
});
// ── API: Version ────────────────────────────────────────────────
app.get('/api/version', (req, res) => {
    try {
        const p = path.join(__dirname, '../version.json');
        if (fs.existsSync(p)) return res.json(JSON.parse(fs.readFileSync(p, 'utf8')));
    } catch { }
    res.json({ version: "Desconocida" });
});

// ── API: Download ─────────────────────────────────────────────
app.get('/api/download/:filename', requireStudio, async (req, res) => {
    try {
        const folderId = req.query.projectId || req.studio.folderId || process.env.GOOGLE_DRIVE_FOLDER_ID;
        const filename = req.params.filename;
        const stream = await driveService.downloadFile(filename, folderId, req.studio.refreshToken);
        res.setHeader('Content-disposition', 'attachment; filename=' + filename);
        res.setHeader('Content-type', 'application/octet-stream');
        stream.pipe(res);
    } catch (err) {
        console.error('Error descarga Drive:', err);
        res.status(404).json({ error: 'Archivo no encontrado en Drive' });
    }
});

// ── API: Projects ─────────────────────────────────────────────
app.get('/api/projects', requireStudio, async (req, res) => {
    const data = loadData(req.studioId);
    const localProjects = data.projects ?? [];

    try {
        const rootFolderId = req.studio.folderId || process.env.GOOGLE_DRIVE_FOLDER_ID;
        if (!rootFolderId) return res.json(localProjects);

        // Fetch folders from Drive
        const driveFolders = await driveService.listFolders(rootFolderId, req.studio.refreshToken);

        const updatedProjects = [];
        let dataChanged = false;

        const localMap = new Map();
        for (const lp of localProjects) {
            localMap.set(lp.id, lp);
        }

        for (const df of driveFolders) {
            if (localMap.has(df.id)) {
                updatedProjects.push(localMap.get(df.id));
            } else {
                // Auto-discover new folder
                updatedProjects.push({
                    id: df.id,
                    name: df.name,
                    color: '#55AAFF', // Default color
                    createdAt: df.createdTime || new Date().toISOString()
                });
                dataChanged = true;
            }
        }

        if (localProjects.length !== updatedProjects.length || dataChanged) {
            data.projects = updatedProjects;
            saveData(req.studioId, data);
        }

        res.json(updatedProjects);
    } catch (err) {
        console.error('Error auto-descubriendo proyectos en Drive:', err);
        res.json(localProjects); // Fallback
    }
});

app.post('/api/projects', requireStudio, async (req, res) => {
    const { name, color } = req.body;
    if (!name) return res.status(400).json({ error: 'Nombre requerido' });
    
    try {
        const rootFolderId = req.studio.folderId || process.env.GOOGLE_DRIVE_FOLDER_ID;
        // Crea la carpeta en Drive usando las credenciales del estudio
        const driveFolder = await driveService.createFolder(name.trim(), rootFolderId, req.studio.refreshToken);

        const data = loadData(req.studioId);
        const project = {
            id: driveFolder.id, // El ID ahora es el Folder ID de Drive
            name: name.trim(),
            color: color || '#55AAFF',
            createdAt: new Date().toISOString()
        };
        data.projects.push(project);
        saveData(req.studioId, data);
        res.json(project);
    } catch (err) {
        console.error('Error creando proyecto en Drive:', err);
        res.status(500).json({ error: 'Error al crear carpeta en Google Drive' });
    }
});

app.delete('/api/projects/:id', requireStudio, (req, res) => {
    const data = loadData(req.studioId);
    data.projects = data.projects.filter(p => p.id !== req.params.id);
    saveData(req.studioId, data);
    res.json({ message: 'Eliminado' });
});

// ── API: File Metadata ────────────────────────────────────────
app.get('/api/files/meta', requireStudio, (req, res) => {
    const data = loadData(req.studioId);
    res.json(data.fileMeta ?? {});
});

app.post('/api/files/meta', requireStudio, (req, res) => {
    const { filename, projectId } = req.body;
    if (!filename) return res.status(400).json({ error: 'filename requerido' });
    const data = loadData(req.studioId);
    if (!data.fileMeta) data.fileMeta = {};
    data.fileMeta[filename] = {
        ...(data.fileMeta[filename] ?? {}),
        projectId: projectId ?? null
    };
    saveData(req.studioId, data);
    res.json({ ok: true });
});

// ── Socket.io ─────────────────────────────────────────────────
// Validate studio key on handshake
io.use((socket, next) => {
    const key = socket.handshake.auth?.studioKey;
    if (!key || !studios[key])
        return next(new Error('Studio Key inválido'));
    socket.studioId = key;
    socket.userName = socket.handshake.auth?.user || 'Desconocido';
    next();
});

io.on('connection', (socket) => {
    socket.join(socket.studioId);
    const state = getStudioState(socket.studioId);

    // DEBUG: log connections
    const roomSize = io.sockets.adapter.rooms.get(socket.studioId)?.size || 0;
    console.log(`[SOCKET] ✅ Conectado: ${socket.userName} | Studio: ${socket.studioId} | Sala: ${roomSize} usuario(s) | ID: ${socket.id}`);

    // Send current state to the new client
    socket.emit('lock_update', state.layerLocks);

    // Plan 3: Ghost Cursors — relay cursor position to studio room (never persisted)
    let cursorLogCount = 0;
    socket.on('cursor_move', ({ x, y, z }) => {
        cursorLogCount++;
        // Log solo los primeros 3 y luego cada 100 para no saturar
        if (cursorLogCount <= 3 || cursorLogCount % 100 === 0) {
            const targets = (io.sockets.adapter.rooms.get(socket.studioId)?.size || 1) - 1;
            console.log(`[CURSOR] ${socket.userName} → (${x?.toFixed?.(1)}, ${y?.toFixed?.(1)}, ${z?.toFixed?.(1) || 0}) | Reenviando a ${targets} usuario(s) | #${cursorLogCount}`);
        }
        socket.to(socket.studioId).emit('cursor_move', {
            user: socket.userName,
            x, y, z: z || 0
        });
    });

    // Notify room when a user disconnects so ghost cursors are cleaned up
    socket.on('disconnect', () => {
        const remainingSize = io.sockets.adapter.rooms.get(socket.studioId)?.size || 0;
        console.log(`[SOCKET] ❌ Desconectado: ${socket.userName} | Studio: ${socket.studioId} | Quedan: ${remainingSize} usuario(s)`);
        socket.to(socket.studioId).emit('cursor_remove', { user: socket.userName });
    });
});

// ── SPA Catch-all ─────────────────────────────────────────────
app.get('*', (req, res) => {
    const indexPath = path.join(__dirname, '../client/dist/index.html');
    if (fs.existsSync(indexPath)) {
        res.sendFile(indexPath);
    } else {
        res.send('Servidor Activo. (Frontend no compilado aún)');
    }
});

// ── Start ─────────────────────────────────────────────────────
const PORT = process.env.PORT || 3001;
server.listen(PORT, '0.0.0.0', () => {
    console.log('\n==========================================');
    console.log('CAD SYNC SERVER — MULTI-TENANT + LIVE');
    console.log(`Local:     http://localhost:${PORT}`);
    console.log(`Red Local: http://${LOCAL_IP}:${PORT}`);
    console.log(`Estudios:  ${Object.keys(studios).join(', ') || 'ninguno cargado'}`);
    console.log('==========================================\n');
});
