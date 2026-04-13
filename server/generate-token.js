/**
 * generate-token.js
 * 
 * Script de un solo uso para obtener el Refresh Token de Google OAuth2.
 * 
 * Requisitos previos:
 *   1. Ir a https://console.cloud.google.com/apis/credentials
 *   2. Crear un "OAuth 2.0 Client ID" de tipo "Web application"
 *   3. En "Authorized redirect URIs" agregar: http://localhost:3333/callback
 *   4. Copiar el Client ID y Client Secret
 *
 * Uso:
 *   node generate-token.js <CLIENT_ID> <CLIENT_SECRET>
 *
 * Resultado:
 *   Se abrirá el navegador para que autorices el acceso.
 *   Al finalizar, se imprimirá el REFRESH_TOKEN que debes configurar en Render.
 */

const http = require('http');
const { URL } = require('url');

const CLIENT_ID = process.argv[2];
const CLIENT_SECRET = process.argv[3];
const REDIRECT_URI = 'http://localhost:3333/callback';
const SCOPES = [
    'https://www.googleapis.com/auth/drive.file',
    'https://www.googleapis.com/auth/drive.readonly'
];

if (!CLIENT_ID || !CLIENT_SECRET) {
    console.error('\n❌ Uso: node generate-token.js <CLIENT_ID> <CLIENT_SECRET>\n');
    console.error('Obtené estas credenciales desde:');
    console.error('https://console.cloud.google.com/apis/credentials\n');
    process.exit(1);
}

// 1. Construir la URL de autorización
const authUrl = new URL('https://accounts.google.com/o/oauth2/v2/auth');
authUrl.searchParams.set('client_id', CLIENT_ID);
authUrl.searchParams.set('redirect_uri', REDIRECT_URI);
authUrl.searchParams.set('response_type', 'code');
authUrl.searchParams.set('scope', SCOPES.join(' '));
authUrl.searchParams.set('access_type', 'offline');
authUrl.searchParams.set('prompt', 'consent');

console.log('\n==========================================');
console.log('  SYNC-CAD — Generador de Refresh Token');
console.log('==========================================\n');
console.log('1. Abrí esta URL en tu navegador:\n');
console.log(`   ${authUrl.toString()}\n`);
console.log('2. Iniciá sesión con la cuenta de Google donde está la carpeta de Drive.');
console.log('3. Autorizá los permisos solicitados.');
console.log('4. Serás redirigido a localhost:3333 — este script capturará el código.\n');
console.log('Esperando autorización...\n');

// 2. Levantar un servidor temporal para capturar el callback
const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, 'http://localhost:3333');

    if (url.pathname !== '/callback') {
        res.writeHead(404);
        res.end('Not found');
        return;
    }

    const code = url.searchParams.get('code');
    const error = url.searchParams.get('error');

    if (error) {
        res.writeHead(400);
        res.end(`Error de autorización: ${error}`);
        console.error(`\n❌ Error: ${error}`);
        server.close();
        process.exit(1);
    }

    if (!code) {
        res.writeHead(400);
        res.end('No se recibió código de autorización.');
        return;
    }

    // 3. Intercambiar el código por tokens
    try {
        const tokenResponse = await fetch('https://oauth2.googleapis.com/token', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: new URLSearchParams({
                code,
                client_id: CLIENT_ID,
                client_secret: CLIENT_SECRET,
                redirect_uri: REDIRECT_URI,
                grant_type: 'authorization_code',
            }),
        });

        const tokens = await tokenResponse.json();

        if (tokens.error) {
            throw new Error(`${tokens.error}: ${tokens.error_description}`);
        }

        // 4. Mostrar el resultado
        console.log('✅ ¡Token obtenido exitosamente!\n');
        console.log('==========================================');
        console.log('  CONFIGURAR EN RENDER (Variables de Entorno):');
        console.log('==========================================\n');
        console.log(`  GOOGLE_CLIENT_ID     = ${CLIENT_ID}`);
        console.log(`  GOOGLE_CLIENT_SECRET = ${CLIENT_SECRET}`);
        console.log(`  GOOGLE_REFRESH_TOKEN = ${tokens.refresh_token}`);
        console.log('\n==========================================\n');

        res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
        res.end(`
            <html>
            <body style="font-family: sans-serif; text-align: center; padding: 50px; background: #1a1a2e; color: #e0e0e0;">
                <h1 style="color: #4ade80;">✅ ¡Autorización exitosa!</h1>
                <p>Ya podés cerrar esta ventana.</p>
                <p>Revisá la consola para ver las variables de entorno.</p>
            </body>
            </html>
        `);
    } catch (err) {
        console.error(`\n❌ Error al obtener token: ${err.message}`);
        res.writeHead(500);
        res.end(`Error: ${err.message}`);
    }

    server.close();
});

server.listen(3333, () => {
    // Intentar abrir el navegador automáticamente
    const { exec } = require('child_process');
    const openCmd = process.platform === 'win32' ? 'start' :
                    process.platform === 'darwin' ? 'open' : 'xdg-open';
    exec(`${openCmd} "${authUrl.toString().replace(/&/g, '^&')}"`);
});
