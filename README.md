# 📊 FeelAutom-Metrics

[![Version](https://img.shields.io/badge/version-0.0.11-blue.svg)](./version.txt)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-512bd4.svg)](https://dotnet.microsoft.com/download)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ed.svg)](https://www.docker.com/)

**FeelAutom-Metrics** est une plateforme d'observabilité et de sécurité "all-in-one" conçue pour les infrastructures modernes basées sur **Traefik**. Elle combine monitoring technique, analyse de sécurité par IA et suivi d'événements métiers dans une interface unique et performante.

---

## 🚀 Fonctionnalités Clés

- **📈 Monitoring Holistique** : Ne vous contentez pas de voir les erreurs 404. Suivez la latence, le volume de données et la répartition géographique de votre trafic.
- **🤖 Analyste SOC IA (Gemini)** : Un expert en sécurité virtuel qui surveille vos logs 24/7. Il identifie les comportements complexes (scans furtifs, exploitation de failles) et prend des décisions de bannissement justifiées.
- **🛡️ Bouclier Actif** : Système de scoring d'IP intelligent capable de distinguer un utilisateur réel (même avec du prefetching Next.js intensif) d'un bot malveillant.
- **🎯 Événements Métiers** : Centralisez vos logs techniques et vos succès commerciaux (inscriptions, ventes, erreurs critiques applicatives) au même endroit.
- **🌍 Intelligence Géographique** : Géolocalisation précise via MaxMind pour comprendre d'où vient votre audience.
- **🔐 Privacy First** : Auto-hébergé, vos logs restent sur votre infrastructure.

---

## 🛠️ Installation Rapide

```bash
# 1. Cloner le projet
git clone https://github.com/feelautom/FeelAutom-Metrics.git
cd FeelAutom-Metrics

# 2. Configurer le .env (voir exemple ci-dessous)
# 3. Lancer
docker-compose up -d
```

### Exemple de fichier `.env` :
```bash
# Sécurité & Accès
INVESTIGATOR_API_KEY="votre_cle_secrete_partagee"
AUTH_PASSWORD="votre_mot_de_passe_dashboard"
WHITELISTED_IPS="votre_ip_publique"

# IA Analyste (Optionnel)
GEMINI_API_KEY="votre_cle_google_gemini"
AI_ANALYST_MODEL="gemini-3.1-pro-preview"
AI_ANALYST_INTERVAL=15
```

Le dashboard sera accessible sur `http://localhost:8080` (ou via votre domaine configuré dans Traefik).

---

## 📸 Aperçu de l'interface

| Dashboard Global | Analyse de Sécurité |
| :---: | :---: |
| ![Dashboard](./docs/screenshots/dashboard.png) | ![Security](./docs/screenshots/security.png) |

| Flux de Logs | Statistiques avancées |
| :---: | :---: |
| ![Logs](./docs/screenshots/logs.png) | ![Analytics](./docs/screenshots/analytics.png) |

---

## 🖥️ Exploration du Dashboard

### 1. Dashboard Global (Accueil)
La tour de contrôle. Elle offre une vue d'ensemble immédiate des indicateurs clés de performance (KPIs) : nombre total de requêtes, taux d'erreur, trafic bot vs humain, et le top des IPs les plus actives.

### 2. Flux de Logs (Temps Réel)
Un flux "live" de tout ce qui transite par votre reverse-proxy Traefik avec filtrage puissant et exports **CSV** / **JSON**.

### 3. Analyses Avancées
Une vue granulaire par domaine pour comprendre les performances de chaque micro-service (latence, erreurs, terminaux).

### 4. Sécurité & SOC
L'espace dédié à la protection : liste des bans actifs, rapports d'analyse IA et scores de menace en cours.

### 5. Paramètres Système
Gestion de la whitelist, édition dynamique du **Prompt IA** et maintenance de la base de données.

---

## 🔔 Système d'Événements Métiers

FeelAutom-Metrics permet à vos applications externes d'envoyer des événements personnalisés pour une corrélation parfaite entre logs techniques et activités métiers.

**Endpoint :** `POST https://api-metrics.votre-domaine.com/api/events`  
**Header requis :** `X-Api-Key: <ta clé API>`

### 🛠️ Exemples d'intégration

#### 1. Via curl
```bash
curl -X POST https://api-metrics.votre-domaine.fr/api/events \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: TON_API_KEY" \
  -d '{
    "appName": "votre-site.com",
    "environment": "Production",
    "level": "Information",
    "message": "Nouvel utilisateur inscrit",
    "category": "Auth",
    "userId": "user-123",
    "metadata": {
      "plan": "premium",
      "source": "google-ads"
    }
  }'
```

#### 2. Depuis un site Next.js / Node.js
```javascript
async function trackEvent(event) {
  await fetch('https://api-metrics.votre-domaine.fr/api/events', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Api-Key': process.env.METRICS_API_KEY
    },
    body: JSON.stringify({
      appName: 'votre-site.com',
      environment: process.env.NODE_ENV === 'production' ? 'Production' : 'Development',
      ...event
    })
  });
}

// Utilisation :
trackEvent({ level: 'Information', message: 'Inscription réussie', category: 'Auth', userId: 'user-123' });
```

#### 3. Depuis une application .NET
```csharp
using var http = new HttpClient();
http.DefaultRequestHeaders.Add("X-Api-Key", "TON_API_KEY");

await http.PostAsJsonAsync("https://api-metrics.votre-domaine.fr/api/events", new
{
    appName = "votre-site.com",
    environment = "Production",
    level = "Information",
    message = "Commande validée #1234",
    category = "Order",
    userId = "client-456",
    metadata = new Dictionary<string, string> { ["amount"] = "149.99" }
});
```

### 📋 Champs disponibles

| Champ | Obligatoire | Description |
| :--- | :---: | :--- |
| `appName` | **Oui** | Nom du site/app (ex: t-ia-connect.com) |
| `environment` | Non | Production par défaut |
| `level` | Non | Information, Warning, Error, Critical |
| `message` | **Oui** | Description de l'événement |
| `category` | Non | Catégorie métier (Auth, Payment, Order...) |
| `userId` | Non | ID utilisateur concerné |
| `correlationId` | Non | Pour lier à un request ID Traefik |
| `metadata` | Non | Objet clé/valeur libre (stocké en JSONB) |
| `exceptionMessage` | Non | Message d'erreur si level = Error |
| `stackTrace` | Non | Stack trace si Error |

---

## 🏗️ Architecture

1.  **The Shipper (Node.js)** : Agent ultra-léger qui "tail" les logs Docker de Traefik.
2.  **Ingestor (.NET 9)** : API haute performance (Enrichissement GeoIP, Sécurité).
3.  **Dashboard (Blazor Server)** : Interface interactive riche (SignalR).
4.  **PostgreSQL 17** : Stockage persistant (JSONB pour les métadonnées).

---

## 📄 Licence

Distribué sous la licence **MIT**. Voir `LICENSE` pour plus d'informations.

---
*Développé avec ❤️ par FeelAutom.*
