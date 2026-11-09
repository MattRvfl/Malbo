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

## Configuration

Tout se règle dans **Réglages** (icône de la barre des tâches ou roue crantée dans l'île).

- **Discord (vocal)** : crée une appli sur [discord.com/developers](https://discord.com/developers/applications) **avec ton propre compte** (Discord ne donne l'accès au vocal par RPC qu'au propriétaire de l'appli et aux *App Testers*). Colle l'*Application ID* (Client ID) et le *Client Secret*, ajoute la redirection `http://127.0.0.1` dans OAuth2, garde l'appli Discord de bureau ouverte et clique « Connecter Discord ». Sans ça, Malbo détecte quand même tes appels en local (micro utilisé par Discord).
- **Spotify** : crée une appli sur [developer.spotify.com](https://developer.spotify.com/dashboard) avec la Redirect URI `http://127.0.0.1:8898/callback`, colle le Client ID puis « Connecter Spotify ».
- **Agenda** : URL ICS (Google Agenda › Paramètres › Adresse secrète au format iCal).
- **Steam** : clé API + SteamID64.
- **FPS** : chemin de [PresentMon](https://github.com/GameTechDev/PresentMon/releases).

Réglages et données : `%AppData%\Malbo\settings.json` · journal d'erreurs : `%AppData%\Malbo\error.log`.
