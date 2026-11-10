# Site de Malbo

Site vitrine statique de [Malbo](https://github.com/MattRvfl/Malbo) : HTML, CSS et JS sans dépendance ni étape de build obligatoire.

| Page | Fichier |
|---|---|
| Accueil | `index.html` |
| Fonctionnalités | `features.html` |
| Télécharger | `download.html` |
| Guide | `docs.html` |
| À propos | `about.html` |
| Journal des versions (chargé en direct depuis GitHub) | `changelog.html` |
| Redirection vers Discord | `discord.html` |
| Tableau de bord des visites (privé) | `dashboard.html` |
| Page introuvable | `404.html` |

## Voir le site en local

```sh
python3 -m http.server 8000
```

Puis ouvrir <http://localhost:8000>.

## Modifier le contenu

L'en-tête, le pied de page et les icônes sont partagés entre les pages : ils sont définis dans `build.py`, qui régénère les six fichiers HTML.

```sh
python3 build.py
```

Le numéro de version affiché se change en haut de `build.py` (`VERSION`). Le style est dans `assets/css/style.css`, les animations dans `assets/js/main.js`.

## Réglages

Tout est dans `assets/js/config.js` : lien d'invitation Discord, dépôt GitHub, adresse et clé publique du projet Supabase qui stocke les visites.

## Statistiques de visite

`assets/js/track.js` envoie une ligne anonyme par page vue (provenance, appareil, langue ; ni cookie ni adresse IP) à la fonction `track()` du projet Supabase « Malbo ». `dashboard.html` lit les agrégats via `stats(mot de passe, jours)`.

- Liens suivis : ajoutez `?ref=tiktok` (ou tout autre nom) à une adresse du site, et `&utm_campaign=nom` pour distinguer vos publications. Le tableau de bord contient un générateur.
- Changer le mot de passe, dans l'éditeur SQL de Supabase :
  `update dash_secret set hash = extensions.crypt('nouveau-mot-de-passe', extensions.gen_salt('bf', 10));`
- Aperçu sans base : `dashboard.html?demo`.

## Mettre en ligne

Le dossier se publie tel quel sur GitHub Pages, Vercel, Netlify ou n'importe quel hébergement statique. Tous les liens sont relatifs, le site fonctionne donc aussi dans un sous-dossier.

La police Montserrat est hébergée localement (`assets/fonts`, licence OFL).
