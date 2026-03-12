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

## 🖥️ Exploration du Dashboard

L'interface est découpée en plusieurs sections spécialisées pour une gestion efficace de votre infrastructure :

### 1. Dashboard Global (Accueil)
La tour de contrôle. Elle offre une vue d'ensemble immédiate des indicateurs clés de performance (KPIs) sur les dernières 24 heures : nombre total de requêtes, taux d'erreur, trafic bot vs humain, et le top des IPs les plus actives.

### 2. Flux de Logs (Temps Réel)
Un flux "live" de tout ce qui transite par votre reverse-proxy Traefik.
- **Filtrage puissant** : Filtrez par domaine, chemin, ou statut HTTP.
- **Exports** : Boutons dédiés pour exporter vos données filtrées en **CSV** ou **JSON** pour des analyses externes.

### 3. Analyses Avancées
Une vue granulaire par domaine pour comprendre les performances de chaque micro-service. Visualisez la latence moyenne, les codes d'erreurs les plus fréquents et la typologie des terminaux utilisés (Mobile vs Desktop).

### 4. Sécurité & SOC
L'espace dédié à la protection de votre serveur.
- **Bans Actifs** : Liste des IPs actuellement bloquées avec raison et date d'expiration.
- **Rapports IA** : Consultez le journal des analyses effectuées par l'analyste SOC IA, avec le résumé de ses décisions.
- **Scores de Menace** : Surveillez les IPs suspectes avant même qu'elles ne soient bannies.

### 5. Paramètres Système
Gestion de la configuration sans redémarrer les services :
- **Whitelist Dynamique** : Excluez vos propres IPs pour ne pas fausser les statistiques.
- **Éditeur de Prompt IA** : Personnalisez les instructions envoyées à Gemini. Dictez-lui sa politique de sécurité (soyez plus ou moins sévère selon vos besoins).
- **Maintenance BDD** : Statistiques de stockage et outils de purge pour contrôler la rétention des données.

---

## 🔔 Système d'Événements Métiers

FeelAutom-Metrics n'est pas qu'un analyseur de logs Traefik. Il permet à vos applications externes d'envoyer des événements personnalisés via une API simple.

### Envoyer un événement (Exemple en cURL) :
```bash
curl -X POST https://api-metrics.votre-domaine.com/api/events \
     -H "X-Api-Key: VOTRE_CLE_API" \
     -H "Content-Type: application/json" \
     -d '{
       "category": "Auth",
       "message": "Nouvel utilisateur inscrit",
       "metadata": { "plan": "PRO", "source": "referral" }
     }'
```
Ces événements apparaîtront instantanément dans votre flux et pourront être corrélés avec les logs réseau.

---

## 🏗️ Architecture

1.  **The Shipper (Node.js)** : Agent ultra-léger qui "tail" les logs Docker de Traefik et les expédie vers l'ingesteur.
2.  **Ingestor (.NET 9)** : API haute performance chargée de l'enrichissement (GeoIP, UA Parsing) et du calcul de sécurité.
3.  **Dashboard (Blazor Server)** : Interface interactive riche utilisant SignalR pour les mises à jour en temps réel.
4.  **PostgreSQL** : Base de données robuste pour le stockage des logs et des métriques.

---

## 🛠️ Installation

```bash
# 1. Cloner le projet
git clone https://github.com/feelautom/FeelAutom-Metrics.git
cd FeelAutom-Metrics

# 2. Configurer le .env (voir exemple dans le README)
# 3. Lancer
docker-compose up -d
```

---

## 📄 Licence

Distribué sous la licence **MIT**. Voir `LICENSE` pour plus d'informations.

---
*Développé avec ❤️ par FeelAutom.*
