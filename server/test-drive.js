require('dotenv').config();
const driveService = require('./googleDriveService');
const fs = require('fs');

async function test() {
    console.log("--- TEST DE INTEGRACIÓN GOOGLE DRIVE ---");
    const folderId = process.env.GOOGLE_DRIVE_FOLDER_ID;
    
    if (!folderId) {
        console.error("❌ ERROR: GOOGLE_DRIVE_FOLDER_ID no definido en .env");
        return;
    }

    try {
        console.log("1. Listando archivos...");
        const files = await driveService.listFiles(folderId);
        console.log(`✅ Archivos encontrados: ${files.length}`);
        files.forEach(f => console.log(` - ${f.name} (${f.id})`));

        // Para probar subida necesitaríamos un archivo real y credentials.json
        // console.log("2. Probando subida simulada...");
        // ...
    } catch (err) {
        if (err.message.includes('NO ENCONTRADO')) {
            console.log("ℹ️ Prueba parcial ok: El servicio detectó correctamente que falta credentials.json");
        } else {
            console.error("❌ Error inesperado:", err.message);
        }
    }
}

test();
