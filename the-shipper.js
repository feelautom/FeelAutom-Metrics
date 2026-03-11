const { spawn } = require('child_process');
const axios = require('axios');

// Configuration
const INGESTOR_URL = process.env.INGESTOR_URL || 'https://localhost:7123/api/logs/ingest';
const CONTAINER_NAME = process.env.CONTAINER_NAME || 'dokploy-traefik';

// Désactiver la vérification SSL en dev si nécessaire
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

console.log(`🚀 The Shipper est en route...`);
console.log(`🔗 Envoi vers : ${INGESTOR_URL}`);
console.log(`📦 Écoute du conteneur : ${CONTAINER_NAME}`);

function startShipping() {
    const docker = spawn('docker', ['logs', '-f', CONTAINER_NAME]);

    docker.stdout.on('data', async (data) => {
        const lines = data.toString().split('\n');
        for (const line of lines) {
            if (!line.trim()) continue;

            try {
                // Tenter de parser pour vérifier que c'est du JSON
                const json = JSON.parse(line);
                
                // Envoyer à l'ingestor
                axios.post(INGESTOR_URL, json)
                    .catch(err => {
                        console.error(`❌ Erreur d'envoi [${err.code}]: ${err.message}`);
                    });

            } catch (e) {
                // Ce n'est pas du JSON, on ignore (logs système traefik non-access)
                // console.log(`ℹ️ Ignoré (non-JSON) : ${line.substring(0, 50)}...`);
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
