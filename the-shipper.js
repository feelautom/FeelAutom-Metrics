const { spawn } = require('child_process');
const axios = require('axios');

// Configuration
const INGESTOR_URL = process.env.INGESTOR_URL || 'https://localhost:7123/api/logs/ingest';
const CONTAINER_NAME = process.env.CONTAINER_NAME || 'dokploy-traefik';
const API_KEY = process.env.API_KEY || '';

// Désactiver la vérification SSL en dev si nécessaire
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

console.log(`🚀 The Shipper est en route...`);
console.log(`🔗 Envoi vers : ${INGESTOR_URL}`);
console.log(`📦 Écoute du conteneur : ${CONTAINER_NAME}`);

function startShipping() {
    // --since=0s : ne suivre que les nouveaux logs (pas l'historique)
    const docker = spawn('docker', ['logs', '-f', '--since', '0s', CONTAINER_NAME]);

    docker.stdout.on('data', async (data) => {
        const lines = data.toString().split('\n');
        for (const line of lines) {
            if (!line.trim()) continue;

            try {
                const json = JSON.parse(line);

                // Ignorer les logs d'erreur Traefik (pas des access logs)
                if (!json.ClientHost) continue;

                const headers = API_KEY ? { 'X-Api-Key': API_KEY } : {};
                axios.post(INGESTOR_URL, json, { headers })
                    .catch(err => {
                        console.error(`❌ Erreur d'envoi [${err.code}]: ${err.message}`);
                    });

            } catch (e) {
                // Pas du JSON, on ignore
            }
        }
    });

    docker.stderr.on('data', (data) => {
        console.error(`⚠️ Docker Stderr: ${data}`);
    });

    docker.on('close', (code) => {
        console.log(`🔌 Docker process exited with code ${code}. Tentative de redémarrage dans 5s...`);
        setTimeout(startShipping, 5000);
    });
}

startShipping();
