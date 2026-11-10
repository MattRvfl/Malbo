<p align="center"><img src="Assets/malbo.png" width="96" alt="Malbo"></p>

<h1 align="center">Malbo</h1>
<p align="center">Une Dynamic Island pour Windows : musique, paroles, notifications, widgets et plus, dans une île flottante en haut de l'écran.</p>

---

## Fonctionnalités

**Île**
- Île compacte animée (effet ressort), qui s'ouvre au survol ou au clic
- Île coupée en deux : une bulle à côté pour une 2ᵉ activité (minuteur, appel, téléchargement)
- Aperçus temporaires (notifications, volume, luminosité, alertes) : glisser vers le haut pour fermer
- Clic droit : menu rapide · masquage automatique en plein écran · thèmes clair / sombre / auto

**Média**
- Spotify, navigateurs, Deezer, VLC… : pochette, contrôles, progression, visualiseur
- **Paroles synchronisées** (lrclib.net), dans l'île compacte et en grand, façon Apple Music
- Pochette floutée en fond, couleur d'accent tirée de la pochette
- Aléatoire / répétition, et avec Spotify connecté : j'aime et ajout à une playlist
- Volume par application

**Notifications**
- Discord, Telegram, WhatsApp, Messenger, Slack, Teams, mails… affichées dans l'île à la place des bannières Windows
- Clic = ouvre l'appli · glisser = supprimer · historique, Ne pas déranger, filtre par appli
- Détection des appels Discord (qui parle : toi / les autres)

**Outils**
- Minuteur, chrono, Pomodoro avec mode Focus (coupe les notifications)
- Alarmes, to-do, étagère de fichiers en glisser-déposer, historique du presse-papiers, captures d'écran
- Onglet Lancer : calculatrice et convertisseur (`10 km en miles`, `100 usd en eur`), raccourcis, verrouiller / veille / redémarrer / éteindre

**Système & infos**
- CPU, RAM, GPU, réseau, batterie, Wi-Fi, disques, Bluetooth, micro / caméra utilisés, téléchargements
- Météo avec alerte pluie, horloges mondiales, agenda (ICS), crypto, bourse, scores sportifs, amis Steam
- Compteur FPS en jeu (via PresentMon)

**Nouveau en 1.3 (alpha)**
- Onglet **Bureau** : fenêtres ouvertes (clic = basculer), choix de la sortie audio, Wi-Fi / Bluetooth, pipette + règle, clip de jeu, notes rapides, derniers titres écoutés, profils (Normal, Travail, Jeu, Nuit)
- **Activités en direct** : tes scripts affichent une progression dans l'île via une petite API locale (voir plus bas)
- **Mode appel** : Teams, Zoom, Meet, Discord… détectés par le micro → chrono, notifications en pause, micro coupé partout avec `Ctrl+Alt+M`
- Minuteurs nommés (`15 min thé`) et lancement d'applis depuis l'onglet Lancer
- Presse-papiers : recherche, épingles, **texte d'une capture** (OCR hors ligne)
- Notifications regroupées par conversation, heures calmes, mots-clés prioritaires, rappel plus tard
- Traduction des paroles (option), molette sur l'île = volume, île qui suit l'écran de la souris, couleur d'accent perso
- Canal bêta, sauvegarde / restauration des réglages, jetons chiffrés

**Extensible** : plugins (DLL implémentant `IIslandPlugin` dans `%AppData%\Malbo\plugins`).

## Installation

Prérequis : Windows 10 (2004+) ou 11, [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download).

```powershell
dotnet build -c Release
.\bin\Release\net10.0-windows10.0.22621.0\Malbo.exe
```

Pour l'installer sur le PC : icône de la barre des tâches › **Installer Malbo sur ce PC** (raccourcis Menu Démarrer + Bureau, désinstallation via Paramètres Windows).

## Mises à jour automatiques

Malbo vérifie au démarrage puis toutes les 6 h le manifeste `update.json` à la racine de ce dépôt (à défaut, la dernière release GitHub), télécharge le nouvel exe, **vérifie son SHA-256**, puis se remplace et redémarre dès que l'île est au repos.

Publier une version :

1. Augmente `<Version>` dans `DynamicIsland.csproj`.
2. `.\publish.ps1 -Notes "Ce qui change"` → produit `Malbo.exe` et `update.json` (version, url, sha256).
3. Crée la release `vX.Y.Z` sur GitHub et joins `Malbo.exe`.
4. Commit + push `update.json` : tous les Malbo installés se mettent à jour.

```json
{ "version": "1.2.0", "url": "https://github.com/MattRvfl/Malbo/releases/download/v1.2.0/Malbo.exe",
  "sha256": "…", "notes": "…", "mandatory": false }
```

`mandatory: true` force l'installation même si l'installation automatique est désactivée dans les réglages.

## Raccourcis

| Touches | Action |
|---|---|
| `Ctrl+Alt+I` | Ouvrir / épingler l'île |
| `Ctrl+Alt+H` | Masquer / afficher |
| `Ctrl+Alt+Espace` | Lecture / pause |
| `Ctrl+Alt+M` | Couper / réactiver le micro pour toutes les applis |
| `Échap` | Refermer l'île |
| `Ctrl+Tab`, `Ctrl+1…9` | Changer d'onglet (île ouverte) |
| `Tab` | Parcourir les boutons au clavier |
| Molette sur l'île compacte | Volume |
| Clic molette sur l'île | Lecture / pause |

## Activités en direct (API locale)

Malbo écoute sur `127.0.0.1` uniquement (port 8765 par défaut). Chaque requête doit porter le jeton affiché dans **Réglages › Activités en direct**.

```powershell
$h = @{ 'X-Malbo-Token' = 'TON_JETON' }
# créer ou mettre à jour une activité (progress : 0 à 1)
Invoke-RestMethod http://127.0.0.1:8765/activity -Method Post -Headers $h -ContentType 'application/json' `
  -Body '{"id":"build","title":"Compilation","body":"étape 3/8","progress":0.4}'
# terminer (affiche une alerte) / retirer sans alerte / simple message
Invoke-RestMethod http://127.0.0.1:8765/activity -Method Post -Headers $h -ContentType 'application/json' -Body '{"id":"build","title":"Compilation","done":true}'
Invoke-RestMethod http://127.0.0.1:8765/activity/build -Method Delete -Headers $h
Invoke-RestMethod http://127.0.0.1:8765/notify -Method Post -Headers $h -ContentType 'application/json' -Body '{"title":"Rendu prêt","body":"video.mp4"}'
```

Une activité sans nouvelle depuis 10 minutes disparaît (champ `ttl` en secondes pour changer ce délai).

## Configuration

Tout se règle dans **Réglages** (icône de la barre des tâches ou roue crantée dans l'île).

- **Discord (vocal)** : crée une appli sur [discord.com/developers](https://discord.com/developers/applications) **avec ton propre compte** (Discord ne donne l'accès au vocal par RPC qu'au propriétaire de l'appli et aux *App Testers*). Colle l'*Application ID* (Client ID) et le *Client Secret*, ajoute la redirection `http://127.0.0.1` dans OAuth2, garde l'appli Discord de bureau ouverte et clique « Connecter Discord ». Sans ça, Malbo détecte quand même tes appels en local (micro utilisé par Discord).
- **Spotify** : crée une appli sur [developer.spotify.com](https://developer.spotify.com/dashboard) avec la Redirect URI `http://127.0.0.1:8898/callback`, colle le Client ID puis « Connecter Spotify ».
- **Agenda** : URL ICS (Google Agenda › Paramètres › Adresse secrète au format iCal).
- **Steam** : clé API + SteamID64.
- **FPS** : chemin de [PresentMon](https://github.com/GameTechDev/PresentMon/releases).

Réglages et données : `%AppData%\Malbo\settings.json` · journal d'erreurs : `%AppData%\Malbo\error.log`.

## Crédits

Icônes : [Phosphor Icons](https://phosphoricons.com) (licence MIT), embarquées dans `Assets/Fonts`.
