const { google } = require('googleapis');
const path = require('path');
const fs = require('fs');

// Permisos necesarios: Acceso a archivos creados por la app o compartidos con ella
const SCOPES = ['https://www.googleapis.com/auth/drive.file', 'https://www.googleapis.com/auth/drive.readonly'];
const CREDENTIALS_PATH = path.join(__dirname, 'credentials.json');

/**
 * Inicializa el servicio de Google Drive.
 * Prioridad de autenticación:
 *   1. Token dinámico del Estudio (refreshToken pasado como argumento)
 *   2. OAuth2 (refresh token global en .env)
 *   3. Service Account local (legacy)
 */
async function getDriveService(studioRefreshToken = null) {
    let authClient;
    const clientId     = process.env.GOOGLE_CLIENT_ID;
    const clientSecret = process.env.GOOGLE_CLIENT_SECRET;

    // ── Opción 1: Token específico del Estudio ──────────
    if (studioRefreshToken && clientId && clientSecret) {
        // console.log('✅ googleDriveService: Usando OAuth2 del Estudio.');
        const oauth2Client = new google.auth.OAuth2(clientId, clientSecret);
        oauth2Client.setCredentials({ refresh_token: studioRefreshToken });
        authClient = oauth2Client;
    }
    // ── Opción 2: OAuth2 Global (refresh token en .env) ──────────
    else if (clientId && clientSecret && process.env.GOOGLE_REFRESH_TOKEN) {
        console.log('✅ googleDriveService: Usando OAuth2 Global (env).');
        const oauth2Client = new google.auth.OAuth2(clientId, clientSecret);
        oauth2Client.setCredentials({ refresh_token: process.env.GOOGLE_REFRESH_TOKEN });
        authClient = oauth2Client;
    }

    // ── Opción 2: Service Account desde variable de entorno ───────
    else if (process.env.GOOGLE_CREDENTIALS) {
        console.log('⚠️ googleDriveService: Usando Service Account (variable de entorno). Nota: las SA tienen 0 bytes de cuota.');
        try {
            const keys = JSON.parse(process.env.GOOGLE_CREDENTIALS);
            const auth = new google.auth.GoogleAuth({
                credentials: keys,
                scopes: SCOPES,
            });
            authClient = await auth.getClient();
        } catch (err) {
            console.error('❌ Error al parsear GOOGLE_CREDENTIALS:', err.message);
            return null;
        }
    }

    // ── Opción 3: Service Account desde archivo local ─────────────
    else if (fs.existsSync(CREDENTIALS_PATH)) {
        console.log('📂 googleDriveService: Usando archivo credentials.json local.');
        const auth = new google.auth.GoogleAuth({
            keyFile: CREDENTIALS_PATH,
            scopes: SCOPES,
        });
        authClient = await auth.getClient();
    }

    // ── Sin credenciales ──────────────────────────────────────────
    else {
        console.warn('⚠️ googleDriveService: No se encontraron credenciales.');
        console.warn('   Configurá GOOGLE_CLIENT_ID + GOOGLE_CLIENT_SECRET + GOOGLE_REFRESH_TOKEN');
        console.warn('   o GOOGLE_CREDENTIALS para activar Google Drive.');
        return null;
    }

    return google.drive({ version: 'v3', auth: authClient });
}

/**
 * Sube un archivo a Google Drive. Si el archivo ya existe, crea una nueva versión.
 */
async function uploadFile(filename, stream, folderId, studioRefreshToken = null) {
    const drive = await getDriveService(studioRefreshToken);
    if (!drive) throw new Error('Google Drive no configurado');

    // 1. Buscar si el archivo ya existe en esa carpeta
    const query = `name = '${filename}' and '${folderId}' in parents and trashed = false`;
    const searchRes = await drive.files.list({
        q: query,
        fields: 'files(id, name)',
    });

    const media = {
        mimeType: filename.endsWith('.dwg') ? 'application/acad' : 'application/octet-stream',
        body: stream,
    };

    if (searchRes.data.files.length > 0) {
        // 2a. Existe: Actualizamos el contenido (Google Drive gestiona el versionado interno)
        const fileId = searchRes.data.files[0].id;
        console.log(`[DRIVE] Actualizando versión de: ${filename} (ID: ${fileId})`);
        return drive.files.update({
            fileId: fileId,
            media: media,
            fields: 'id, name',
        });
    } else {
        // 2b. No existe: Creamos uno nuevo
        const fileMetadata = {
            name: filename,
            parents: [folderId],
        };
        console.log(`[DRIVE] Creando nuevo archivo: ${filename}`);
        return drive.files.create({
            resource: fileMetadata,
            media: media,
            fields: 'id, name',
        });
    }
}

/**
 * Obtiene un stream de descarga para un archivo específico por nombre
 */
async function downloadFile(filename, folderId, studioRefreshToken = null) {
    const drive = await getDriveService(studioRefreshToken);
    if (!drive) throw new Error('Google Drive no configurado');

    // Técnica infalible: Listar todo y buscar coincidencia en JS
    const res = await drive.files.list({
        q: `'${folderId}' in parents and trashed = false`,
        fields: 'files(id, name)',
    });

    const targetClean = filename.toLowerCase().replace(".dwg", "").trim();
    const file = res.data.files.find(f => {
        const driveNameClean = f.name.toLowerCase().replace(".dwg", "").trim();
        return driveNameClean === targetClean;
    }) || res.data.files.find(f => f.name.toLowerCase().includes(targetClean));

    if (!file) {
        throw new Error(`Archivo no encontrado en Drive: ${filename}`);
    }

    console.log(`[DRIVE] Descargando por ID directo: ${file.name} (ID: ${file.id})`);
    const response = await drive.files.get(
        { fileId: file.id, alt: 'media' },
        { responseType: 'stream' }
    );
    return response.data;
}

/**
 * Lista los archivos de la carpeta
 */
async function listFiles(folderId, studioRefreshToken = null) {
    const drive = await getDriveService(studioRefreshToken);
    if (!drive) return [];

    const res = await drive.files.list({
        q: `'${folderId}' in parents and trashed = false and (name contains '.dwg' or name contains '.dxf')`,
        fields: 'files(id, name, modifiedTime, size)',
        orderBy: 'modifiedTime desc'
    });
    
    // Adaptar formato de Drive al formato que espera el servidor/historial
    return res.data.files.map(f => ({
        name: f.name,
        id: f.id,
        modified: f.modifiedTime,
        size: f.size
    }));
}

/**
 * Crea una carpeta en Google Drive
 */
async function createFolder(folderName, parentFolderId, studioRefreshToken = null) {
    const drive = await getDriveService(studioRefreshToken);
    if (!drive) throw new Error('Google Drive no configurado');

    const fileMetadata = {
        name: folderName,
        mimeType: 'application/vnd.google-apps.folder',
        parents: [parentFolderId],
    };

    const res = await drive.files.create({
        resource: fileMetadata,
        fields: 'id, name',
    });
    console.log(`[DRIVE] Carpeta creada: ${folderName} (ID: ${res.data.id})`);
    return res.data;
}

/**
 * Lista las carpetas (proyectos) dentro de una carpeta padre
 */
async function listFolders(folderId, studioRefreshToken = null) {
    const drive = await getDriveService(studioRefreshToken);
    if (!drive) return [];

    const res = await drive.files.list({
        // Solo obtener subcarpetas en la carpeta padre
        q: `'${folderId}' in parents and trashed = false and mimeType = 'application/vnd.google-apps.folder'`,
        fields: 'files(id, name, createdTime)',
        orderBy: 'name'
    });
    
    return res.data.files.map(f => ({
        id: f.id,
        name: f.name,
        createdTime: f.createdTime
    }));
}

/**
 * Lista TODOS los archivos DWG/DXF en la carpeta raíz y en todas las subcarpetas (proyectos).
 * Devuelve cada archivo con su parentFolderId para poder reconstruir la relación archivo→proyecto.
 * @returns {Array<{name, id, modified, size, parentFolderId}>}
 */
async function listAllFilesRecursive(rootFolderId, studioRefreshToken = null) {
    const drive = await getDriveService(studioRefreshToken);
    if (!drive) return [];

    const allFiles = [];

    // 1. Archivos en la raíz
    const rootFiles = await listFiles(rootFolderId, studioRefreshToken);
    for (const f of rootFiles) {
        allFiles.push({ ...f, parentFolderId: rootFolderId });
    }

    // 2. Subcarpetas (proyectos)
    const folders = await listFolders(rootFolderId, studioRefreshToken);
    for (const folder of folders) {
        try {
            const folderFiles = await listFiles(folder.id, studioRefreshToken);
            for (const f of folderFiles) {
                allFiles.push({ ...f, parentFolderId: folder.id });
            }
        } catch (err) {
            console.error(`[DRIVE] Error listando carpeta proyecto ${folder.name} (${folder.id}):`, err.message);
            // Seguir con el siguiente proyecto
        }
    }

    return allFiles;
}

/**
 * Mueve un archivo de una carpeta a otra en Google Drive.
 */
async function moveFile(fileId, newParentId, studioRefreshToken = null) {
    const drive = await getDriveService(studioRefreshToken);
    if (!drive) throw new Error('Google Drive no configurado');

    // 1. Obtener los padres actuales para poder eliminarlos
    const file = await drive.files.get({
        fileId: fileId,
        fields: 'parents'
    });
    const previousParents = (file.data.parents || []).join(',');

    // 2. Mover el archivo (añadir nuevo padre y quitar los antiguos)
    return drive.files.update({
        fileId: fileId,
        addParents: newParentId,
        removeParents: previousParents,
        fields: 'id, parents'
    });
}

module.exports = { 
    uploadFile, 
    downloadFile, 
    listFiles, 
    createFolder, 
    listFolders, 
    listAllFilesRecursive,
    moveFile 
};
