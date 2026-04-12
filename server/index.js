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

const app = express();
const server = http.createServer(app);
const io = new Server(server, {
    cors: {
        origin: "*",
        methods: ["GET", "POST"]
    }
});

// Detectar IP local para facilitar conexiones
function getLocalIP() {
    const interfaces = os.networkInterfaces();
    for (const name of Object.keys(interfaces)) {
        for (const iface of interfaces[name]) {
            if (iface.family === 'IPv4' && !iface.internal) {
                return iface.address;
            }
        }
    }
    return 'localhost';
}

const LOCAL_IP = getLocalIP();

app.use(cors());
app.use(express.json());

// Servir archivos estáticos del Frontend (para el deploy en Render)
app.use(express.static(path.join(__dirname, '../client/dist')));

// Configuración de Multer
const storage = multer.diskStorage({
    destination: (req, file, cb) => {
        const uploadPath = path.join(__dirname, 'uploads');
        if (!fs.existsSync(uploadPath)) fs.mkdirSync(uploadPath);
        cb(null, uploadPath);
    },
    filename: (req, file, cb) => cb(null, file.originalname)
});
const upload = multer({ storage });

// Memoria de estado
let syncHistory = [];
let layerLocks = {}; // { layerName: { user: 'name', timestamp: '...' } }

// API Endpoints
app.get('/api/files', async (req, res) => {
    try {
        const folderId = process.env.GOOGLE_DRIVE_FOLDER_ID;
        if (!folderId) return res.json([]);
        const files = await driveService.listFiles(folderId);
        // Devolvemos solo los nombres para mantener compatibilidad con el frontend actual
        res.json(files.map(f => f.name));
    } catch (err) {
        console.error("Error listado Drive:", err);
        res.status(500).json({ error: "Error al listar archivos de Drive" });
    }
});

app.get('/api/status', (req, res) => {
    res.json({ 
        message: "Servidor CAD Sync activo", 
        serverIp: LOCAL_IP,
        history: syncHistory,
        locks: layerLocks
    });
});

// Reservar una capa (Modelo A)
app.post('/api/lock', (req, res) => {
    const { layer, user } = req.body;
    if (layerLocks[layer] && layerLocks[layer].user !== user) {
        return res.status(403).json({ error: `La capa ${layer} está siendo editada por ${layerLocks[layer].user}` });
    }
    layerLocks[layer] = { user, timestamp: new Date().toISOString() };
    io.emit('lock_update', layerLocks);
    res.json({ message: `Capa ${layer} reservada para ${user}` });
});

app.post('/api/unlock', (req, res) => {
    const { layer, user } = req.body;
    if (layerLocks[layer] && layerLocks[layer].user === user) {
        delete layerLocks[layer];
        io.emit('lock_update', layerLocks);
    }
    res.json({ message: "Capa liberada" });
});

app.post('/api/sync', upload.single('file'), async (req, res) => {
    const { user, project, layer } = req.body;
    const file = req.file;
    if (!file) return res.status(400).json({ error: "No se subió ningún archivo" });

    try {
        const folderId = process.env.GOOGLE_DRIVE_FOLDER_ID;
        if (!folderId) throw new Error("GOOGLE_DRIVE_FOLDER_ID no configurado en .env");

        // Subir a Drive usando stream desde el archivo temporal de Multer
        const fileStream = fs.createReadStream(file.path);
        await driveService.uploadFile(file.originalname, fileStream, folderId);

        // Borrar archivo temporal local
        fs.unlinkSync(file.path);

        const syncEntry = {
            user: user || "Usuario Desconocido",
            project: project || "Default",
            layer: layer || null,
            filename: file.originalname,
            timestamp: new Date().toISOString()
        };

        syncHistory.unshift(syncEntry);
        if (syncHistory.length > 50) syncHistory.pop();
        
        io.emit('sync_update', syncEntry);
        res.json({ message: "Sincronización con Drive exitosa", entry: syncEntry });
    } catch (err) {
        console.error("Error Sync Drive:", err);
        res.status(500).json({ error: "Error al sincronizar con Google Drive" });
    }
});

app.get('/api/download/:filename', async (req, res) => {
    try {
        const folderId = process.env.GOOGLE_DRIVE_FOLDER_ID;
        const filename = req.params.filename;
        const stream = await driveService.downloadFile(filename, folderId);
        
        res.setHeader('Content-disposition', 'attachment; filename=' + filename);
        res.setHeader('Content-type', 'application/octet-stream');
        stream.pipe(res);
    } catch (err) {
        console.error("Error descarga Drive:", err);
        res.status(404).json({ error: "Archivo no encontrado en Drive" });
    }
});

// Catch-all para el SPA de React
app.get('*', (req, res) => {
    const indexPath = path.join(__dirname, '../client/dist/index.html');
    if (fs.existsSync(indexPath)) {
        res.sendFile(indexPath);
    } else {
        res.send("Servidor Activo. (Frontend no compilado aún)");
    }
});

io.on('connection', (socket) => {
    socket.emit('lock_update', layerLocks);
    socket.on('disconnect', () => {});
});

const PORT = process.env.PORT || 3001;
server.listen(PORT, '0.0.0.0', () => {
    console.log(`\n==========================================`);
    console.log(`CAD SYNC SERVER - MODO CLOUD READY`);
    console.log(`Local: http://localhost:${PORT}`);
    console.log(`Red Local: http://${LOCAL_IP}:${PORT}`);
    console.log(`==========================================\n`);
});
