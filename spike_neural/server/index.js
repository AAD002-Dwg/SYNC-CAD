const { WebSocketServer, WebSocket } = require('ws');

const wss = new WebSocketServer({ port: 3002 });

console.log('SPIKE NEURAL SERVER (Raw WebSockets)');
console.log('Listening on ws://localhost:3002');
console.log('------------------------------------');

let messageCount = 0;
let lastLogTime = Date.now();

wss.on('connection', (ws, req) => {
    const ip = req.socket.remoteAddress;
    console.log(`[+] Client connected: ${ip}`);

    ws.on('message', (data, isBinary) => {
        messageCount++;
        
        // Log throughput per second
        const now = Date.now();
        if (now - lastLogTime >= 1000) {
            console.log(`[THROUGHPUT] ${messageCount} msg/sec`);
            messageCount = 0;
            lastLogTime = now;
        }

        // Broadcast to all OTHER clients
        wss.clients.forEach(function each(client) {
            if (client !== ws && client.readyState === WebSocket.OPEN) {
                client.send(data, { binary: isBinary });
            }
        });
    });

    ws.on('close', () => {
        console.log(`[-] Client disconnected: ${ip}`);
    });
    
    ws.on('error', console.error);
});
