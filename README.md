# 📊 FeelAutom-Metrics

[![Version](https://img.shields.io/badge/version-0.0.11-blue.svg)](./version.txt)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512bd4.svg)](https://dotnet.microsoft.com/download)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ed.svg)](https://www.docker.com/)

**FeelAutom-Metrics** est une plateforme d'observabilité et de sécurité intelligente conçue pour surveiller les infrastructures web basées sur **Traefik**. Elle ingère, enrichit et analyse les logs d'accès en temps réel pour offrir une visibilité complète et une protection automatisée via IA.

---

## 🚀 Fonctionnalités Clés

- **📈 Dashboard Temps Réel** : Visualisez le trafic global, les erreurs, la latence et les volumes de données via une interface moderne (Blazor Server).
- **🤖 Analyste SOC IA (Gemini)** : Un agent intelligent analyse les comportements suspects et décide des bannissements en fonction de règles de sécurité personnalisables.
- **🛡️ Sécurité Avancée** :
    - Détection automatique de menaces (Scanners, Path Discovery, Brute Force).
    - Système de scoring d'IP.
    - Whitelist dynamique (via UI) et statique (via Env).
    - Protection contre le "Request Burst" (adapté aux comportements Next.js).
- **🌍 Enrichissement de Données** : Géolocalisation des IPs (MaxMind) et parsing avancé des User-Agents.
- **📁 Export de Données** : Exportez vos logs et événements au format CSV ou JSON directement depuis l'interface.
- **🐳 Docker Native** : Déploiement ultra-rapide via Docker Compose.

---

## 📸 Screenshots

*(Placeholders pour vos futurs screenshots)*

| Dashboard Global | Analyse de Sécurité |
| :---: | :---: |
| ![Dashboard](./docs/screenshots/dashboard.png) | ![Security](./docs/screenshots/security.png) |

---

## 🏗️ Architecture

Le projet est découpé en micro-services :

1.  **The Shipper (Node.js)** : Un agent léger qui surveille les conteneurs Traefik et "expédie" les logs vers l'ingesteur.
2.  **Ingestor (.NET 9 Web API)** : Le coeur du système. Reçoit les logs, les enrichit (GeoIP), calcule les scores de menace et gère les bannissements.
3.  **Dashboard (.NET 9 Blazor Server)** : L'interface d'administration et de visualisation.
4.  **PostgreSQL** : Stockage persistant des logs, événements et configurations.

---

## 🛠️ Installation & Déploiement

### Pré-requis
- Docker & Docker Compose
- Une clé API Google Gemini (optionnel, pour l'IA)

### Configuration rapide
1. Clonez le dépôt :
   ```bash
   git clone https://github.com/votre-compte/FeelAutom-Metrics.git
   cd FeelAutom-Metrics
   ```

2. Créez un fichier `.env` à la racine :
   ```bash
   # Sécurité
   INVESTIGATOR_API_KEY="votre_cle_secrete_partagee"
   AUTH_PASSWORD="mot_de_passe_dashboard"
   WHITELISTED_IPS="votre_ip_publique"

   # IA Analyste (Optionnel)
   GEMINI_API_KEY="votre_cle_gemini"
   AI_ANALYST_MODEL="gemini-3.1-pro-preview"
   AI_ANALYST_INTERVAL=15
   ```

3. Lancez l'infrastructure :
   ```bash
   docker-compose up -d
   ```

Le dashboard sera accessible sur `http://localhost:8080` (ou via votre domaine configuré dans Traefik).

---

## ⚙️ Personnalisation du Prompt IA

Vous pouvez désormais éditer les instructions de l'analyste SOC directement depuis l'onglet **Paramètres > Analyste IA**. Utilisez des variables comme `{{THREATS}}`, `{{BANS}}` ou `{{SCORES}}` pour injecter les données réelles dans vos instructions personnalisées.

---

## 📄 Licence

Distribué sous la licence **MIT**. Voir `LICENSE` pour plus d'informations.

---

## 🤝 Contribution

Les contributions sont les bienvenues ! N'hésitez pas à ouvrir une Issue ou une Pull Request pour améliorer le projet.

---
*Développé avec ❤️ par FeelAutom.*
