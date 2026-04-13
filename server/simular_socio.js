const { io } = require("socket.io-client");

const SERVER_URL = "https://sync-cad.onrender.com";
const STUDIO_KEY = "ESTUDIO_DEMO_01";
const USER_NAME  = "Socio Virtual (Antigravity)";

console.log(`🚀 Conectando a ${SERVER_URL} como ${USER_NAME}...`);

const socket = io(SERVER_URL, {
    auth: { studioKey: STUDIO_KEY, user: USER_NAME }
});

socket.on("connect", () => {
    console.log("✅ Conectado al servidor de Render.");
    console.log("📍 Simulando movimiento circular en AutoCAD...");
    
    // Escuchar si recibimos movimientos de otros (ej: del usuario real)
    socket.on("cursor_move", (data) => {
        console.log(`✨ [RECIBIDO] Cursor de ${data.user} en (${data.x.toFixed(1)}, ${data.y.toFixed(1)})`);
    });

    let angle = 0;
    setInterval(() => {
        // Reducimos el radio a 5 unidades y centramos cerca de (5,5) 
        // para que sea visible en el zoom actual del usuario.
        const x = 5 + 5 * Math.cos(angle);
        const y = 5 + 5 * Math.sin(angle);
        const z = 0;

        socket.emit("cursor_move", { x, y, z });
        angle += 0.05; 
        
        if (Math.floor(angle * 20) % 100 === 0) {
            console.log(`[SIM] Enviando: (${x.toFixed(1)}, ${y.toFixed(1)})`);
        }
    }, 100); 
});

socket.on("connect_error", (err) => {
    console.error("❌ Error de conexión:", err.message);
    process.exit(1);
});

socket.on("disconnect", () => {
    console.log("❌ Desconectado del servidor.");
});
