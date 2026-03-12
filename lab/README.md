# 🧪 FeelAutom-Metrics | Lab Demo

Ce dossier contient une infrastructure complète de test pour découvrir les fonctionnalités de **FeelAutom-Metrics**. En une seule commande, vous déployez un reverse-proxy Traefik, un site de démonstration, et toute la stack d'observabilité et de sécurité.

## 🧱 Contenu du Lab

- **Traefik v3** : Configuré avec logs JSON et support du ForwardAuth.
- **Demo Site (Whoami)** : Un petit serveur web qui affiche les headers de la requête. Il est protégé par le système de sécurité FeelAutom.
- **FeelAutom Stack** : Ingestor, Dashboard, PostgreSQL et un Shipper léger.

## 🚀 Lancement Rapide

### 1. Préparer l'environnement
Assurez-vous d'avoir Docker et Docker Compose installés sur votre machine (ou instance OVH).

```bash
cd lab
# Créez un fichier .env avec vos configurations
cp .env.example .env
```

### 2. Configurer les domaines (Local ou Serveur)
Si vous testez en local, ajoutez ces lignes à votre fichier `/etc/hosts` (ou `C:\Windows\System32\drivers\etc\hosts`) :
```text
127.0.0.1 dashboard.local
127.0.0.1 site.local
```
*(Si vous êtes sur un serveur distant, remplacez ces domaines par votre IP dans le fichier .env)*

### 3. Démarrer la stack
```bash
docker compose up -d
```

## 🎯 Scénarios de Test

Une fois la stack lancée, vous pouvez tester les fonctionnalités de sécurité :

### A. Accès légitime
Ouvrez [http://site.local](http://site.local). La page s'affiche instantanément. Allez sur le Dashboard ([http://dashboard.local](http://dashboard.local)) pour voir votre log apparaître.

### B. Simulation de menace (Path Scan)
Tentez d'accéder à un fichier sensible :
```bash
curl http://site.local/.env
```
Répétez l'opération plusieurs fois. Votre score de menace va augmenter dans l'onglet **Sécurité > Scores**.

### C. Test de la Quarantaine (Tarpitting)
Une fois que votre score dépasse 100, tentez d'accéder à nouveau au site. Vous remarquerez que la requête met **15 secondes** à répondre. C'est le mode quarantaine qui s'est activé automatiquement.

### D. Auto-Ban
Continuez vos requêtes suspectes jusqu'à atteindre 200 points. Le système vous renverra alors une erreur **403 Forbidden** immédiate. Vous êtes officiellement banni !

## 🛠️ Maintenance
Pour tout arrêter et nettoyer les volumes :
```bash
docker compose down -v
```

---
*Ce lab est conçu à des fins de démonstration uniquement.*
