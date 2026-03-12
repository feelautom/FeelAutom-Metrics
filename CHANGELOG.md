# Changelog

## 2026-03-12
- Fix: Transmission des variables d'environnement IA au Dashboard pour l'affichage du statut.
- Fix: Correction de la syntaxe Blazor (texte nu dans bloc @if) empêchant le build Docker.
- Feature: Refonte de la page Paramètres avec onglets et éditeur de Prompt IA dynamique.
- Chore: Renommage du projet de FeelAuto-Metrics en FeelAutom-Metrics.
- Feature: Ajout de la section Analyste IA dans les paramètres avec affichage du Prompt Système.
- Fix: Exclusion du prefetch Next.js (_rsc) du calcul des rafales de requêtes pour éviter les faux positifs.
- Docs: Ajout du guide d'urgence pour le débannissement manuel par SSH.
- Fix: Correction des exports (CSV/JSON) en utilisant un proxy interne au Dashboard.
- Fix: Intégration de la table ExcludedIps dans le SecurityService pour éviter les bannissements accidentels.

## 2026-03-11
- feat: initial architecture for FeelAuto-Metrics observability
