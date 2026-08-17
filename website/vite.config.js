import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

let sseClients = [];

// Custom Vite Plugin for Instant Zero-Latency LAN Phone-to-PC Remote Bridge
function lanRemoteBridgePlugin() {
  return {
    name: 'lan-remote-bridge-plugin',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        // Endpoint 1: Phone POSTs touch/move/action data over LAN Wi-Fi
        if (req.url.startsWith('/api/send-event') && req.method === 'POST') {
          let body = '';
          req.on('data', chunk => { body += chunk; });
          req.on('end', () => {
            try {
              const data = JSON.parse(body);
              // Broadcast to PC browser tabs
              sseClients.forEach(client => {
                client.write(`data: ${JSON.stringify(data)}\n\n`);
              });
              res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
              res.end(JSON.stringify({ status: 'ok' }));
            } catch (e) {
              res.writeHead(400);
              res.end('invalid json');
            }
          });
          return;
        }

        // Endpoint 2: PC browser opens Server-Sent Events stream for instant packets
        if (req.url.startsWith('/api/events-stream')) {
          res.writeHead(200, {
            'Content-Type': 'text/event-stream',
            'Cache-Control': 'no-cache',
            'Connection': 'keep-alive',
            'Access-Control-Allow-Origin': '*'
          });
          sseClients.push(res);
          req.on('close', () => {
            sseClients = sseClients.filter(c => c !== res);
          });
          return;
        }

        next();
      });
    }
  };
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), lanRemoteBridgePlugin()],
  server: {
    host: true, // Listens on 0.0.0.0 for LAN Wi-Fi devices
    port: 3000,
    open: true
  }
});
