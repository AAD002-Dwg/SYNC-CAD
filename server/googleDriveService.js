const { google } = require('googleapis');
const path = require('path');
const fs = require('fs');

// Permisos necesarios: Acceso a archivos creados por la app o compartidios con ella
const SCOPES = ['https://www.googleapis.com/auth/drive.file', 'https://www.googleapis.com/auth/drive.readonly'];
const CREDENTIALS_PATH = path.join(__dirname, 'credentials.json');

/**
 * Inicializa el servicio de Google Drive usando una Service Account
 */
async function getDriveService() {
    const credentialsEnv = process.env.GOOGLE_CREDENTIALS;
    let auth;

    if (credentialsEnv) {
        console.log('✅ googleDriveService: Usando credenciales desde variable de entorno.');
        try {
            const keys = JSON.parse(credentialsEnv);
            auth = new google.auth.GoogleAuth({
                credentials: keys,
                scopes: SCOPES,
            });
        } catch (err) {
            console.error('❌ Error al parsear GOOGLE_CREDENTIALS:', err.message);
            return null;
        }
    } else if (fs.existsSync(CREDENTIALS_PATH)) {
        console.log('📂 googleDriveService: Usando archivo credentials.json local.');
        auth = new google.auth.GoogleAuth({
            keyFile: CREDENTIALS_PATH,
            scopes: SCOPES,
        });
    } else {
        console.warn('⚠️ googleDriveService: No se encontraron credenciales (ni variable ni archivo).');
        return null;
    }

    const authClient = await auth.getClient();
    return google.drive({ version: 'v3', auth: authClient });
}

/**
 * Sube un archivo a Google Drive. Si el archivo ya existe, crea una nueva versión.
 */
async function uploadFile(filename, stream, folderId) {
    const drive = await getDriveService();
    if (!drive) throw new Error('Google Drive no configurado (falta credentials.json)');

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
async function downloadFile(filename, folderId) {
    const drive = await getDriveService();
    if (!drive) throw new Error('Google Drive no configurado');

    const searchRes = await drive.files.list({
        q: `name = '${filename}' and '${folderId}' in parents and trashed = false`,
        fields: 'files(id)',
    });

    if (searchRes.data.files.length === 0) {
        throw new Error(`Archivo no encontrado en Drive: ${filename}`);
    }

    const fileId = searchRes.data.files[0].id;
    return drive.files.get(
        { fileId: fileId, alt: 'media' },
        { responseType: 'stream' }
    );
}

/**
 * Lista los archivos de la carpeta
 */
async function listFiles(folderId) {
    const drive = await getDriveService();
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

module.exports = { uploadFile, downloadFile, listFiles };
