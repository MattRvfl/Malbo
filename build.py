#!/usr/bin/env python3
"""Génère les pages statiques du site Malbo (par défaut dans son propre dossier)."""
import sys, pathlib

OUT = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parent
REPO = "https://github.com/MattRvfl/Malbo"
REL = REPO + "/releases/latest"
import json
_rel = json.loads((pathlib.Path(__file__).resolve().parent / "assets/data/releases.json").read_text(encoding="utf-8"))
LATEST = _rel[0] if _rel else None                       # version la plus récente (peut être une pré-version)
VERSION = LATEST["tag_name"].lstrip("v") if LATEST else "1.1.0"
PRE = bool(LATEST and LATEST["prerelease"])
LATEST_URL = LATEST["html_url"] if LATEST else REL

P = {
 "music": '<path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/>',
 "bell": '<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/>',
 "timer": '<line x1="10" x2="14" y1="2" y2="2"/><line x1="12" x2="15" y1="14" y2="11"/><circle cx="12" cy="14" r="8"/>',
 "cpu": '<rect x="4" y="4" width="16" height="16" rx="2"/><rect x="9" y="9" width="6" height="6"/><path d="M15 2v2M15 20v2M2 15h2M2 9h2M20 15h2M20 9h2M9 2v2M9 20v2"/>',
 "plug": '<path d="M12 22v-5"/><path d="M9 8V2"/><path d="M15 8V2"/><path d="M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8Z"/>',
 "download": '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" x2="12" y1="15" y2="3"/>',
 "github": '<path d="M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4"/><path d="M9 18c-4.51 2-5-2-7-2"/>',
 "keyboard": '<rect width="20" height="16" x="2" y="4" rx="2"/><path d="M6 8h.01M10 8h.01M14 8h.01M18 8h.01M8 12h.01M12 12h.01M16 12h.01M7 16h10"/>',
 "cloud": '<path d="M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z"/>',
 "mic": '<path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><line x1="12" x2="12" y1="19" y2="22"/>',
 "island": '<rect x="2" y="8" width="20" height="8" rx="4"/>',
 "arrow": '<path d="M5 12h14"/><path d="m12 5 7 7-7 7"/>',
 "arrow-ur": '<path d="M7 7h10v10"/><path d="M7 17 17 7"/>',
 "check": '<path d="M20 6 9 17l-5-5"/>',
 "copy": '<rect width="14" height="14" x="8" y="8" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>',
 "sliders": '<line x1="4" x2="4" y1="21" y2="14"/><line x1="4" x2="4" y1="10" y2="3"/><line x1="12" x2="12" y1="21" y2="12"/><line x1="12" x2="12" y1="8" y2="3"/><line x1="20" x2="20" y1="21" y2="16"/><line x1="20" x2="20" y1="12" y2="3"/><line x1="2" x2="6" y1="14" y2="14"/><line x1="10" x2="14" y1="8" y2="8"/><line x1="18" x2="22" y1="16" y2="16"/>',
 "monitor": '<rect width="20" height="14" x="2" y="3" rx="2"/><line x1="8" x2="16" y1="21" y2="21"/><line x1="12" x2="12" y1="17" y2="21"/>',
 "zap": '<path d="M13 2 3 14h9l-1 8 10-12h-9l1-8z"/>',
 "code": '<polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/>',
 "menu": '<line x1="4" x2="20" y1="7" y2="7"/><line x1="4" x2="20" y1="12" y2="12"/><line x1="4" x2="20" y1="17" y2="17"/>',
 "refresh": '<path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/><path d="M8 16H3v5"/>',
 "folder": '<path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>',
 "heart": '<path d="M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z"/>',
 "info": '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/>',
 "message": '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>',
 "contrast": '<circle cx="12" cy="12" r="10"/><path d="M12 18a6 6 0 0 0 0-12v12z"/>',
 "gauge": '<path d="m12 14 4-4"/><path d="M3.34 19a10 10 0 1 1 17.32 0"/>',
 "calendar": '<rect width="18" height="18" x="3" y="4" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>',
 "chart": '<path d="M3 3v18h18"/><path d="m7 15 4-5 4 3 5-7"/>',
 "history": '<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l4 2"/>',
 "link": '<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>',
 "lock": '<rect width="18" height="11" x="3" y="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
 "globe": '<circle cx="12" cy="12" r="10"/><path d="M2 12h20"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>',
 "clock": '<circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/>',
 "users": '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>',
 "book": '<path d="M4 19.5v-15A2.5 2.5 0 0 1 6.5 2H20v20H6.5a2.5 2.5 0 0 1 0-5H20"/>',
}
FILL = {
 "play": '<polygon points="6 3 20 12 6 21 6 3"/>',
 "pause": '<rect x="5" y="3" width="5" height="18" rx="1.5"/><rect x="14" y="3" width="5" height="18" rx="1.5"/>',
 "next": '<polygon points="4 4 15 12 4 20 4 4"/><rect x="17" y="4" width="3" height="16" rx="1"/>',
 "discord": '<path d="M20.317 4.3698a19.7913 19.7913 0 00-4.8851-1.5152.0741.0741 0 00-.0785.0371c-.211.3753-.4447.8648-.6083 1.2495-1.8447-.2762-3.68-.2762-5.4868 0-.1636-.3933-.4058-.8742-.6177-1.2495a.077.077 0 00-.0785-.037 19.7363 19.7363 0 00-4.8852 1.515.0699.0699 0 00-.0321.0277C.5334 9.0458-.319 13.5799.0992 18.0578a.0824.0824 0 00.0312.0561c2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a.0777.0777 0 00.0842-.0276c.4616-.6304.8731-1.2952 1.226-1.9942a.076.076 0 00-.0416-.1057c-.6528-.2476-1.2743-.5495-1.8722-.8923a.077.077 0 01-.0076-.1277c.1258-.0943.2517-.1923.3718-.2914a.0743.0743 0 01.0776-.0105c3.9278 1.7933 8.18 1.7933 12.0614 0a.0739.0739 0 01.0785.0095c.1202.099.246.1981.3728.2924a.077.077 0 01-.0066.1276 12.2986 12.2986 0 01-1.873.8914.0766.0766 0 00-.0407.1067c.3604.698.7719 1.3628 1.225 1.9932a.076.076 0 00.0842.0286c1.961-.6067 3.9495-1.5219 6.0023-3.0294a.077.077 0 00.0313-.0552c.5004-5.177-.8382-9.6739-3.5485-13.6604a.061.061 0 00-.0312-.0286zM8.02 15.3312c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9555-2.4189 2.157-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.9555 2.4189-2.1569 2.4189zm7.9748 0c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9554-2.4189 2.1569-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.946 2.4189-2.1568 2.4189Z"/>',
 "prev": '<polygon points="20 4 9 12 20 20 20 4"/><rect x="4" y="4" width="3" height="16" rx="1"/>',
}

def ic(name, cls=""):
    if name in FILL:
        return f'<svg class="i i-fill {cls}" viewBox="0 0 24 24" aria-hidden="true">{FILL[name]}</svg>'
    return f'<svg class="i {cls}" viewBox="0 0 24 24" aria-hidden="true">{P[name]}</svg>'

NAV = [("index.html", "Accueil"), ("features.html", "Fonctionnalités"), ("download.html", "Télécharger"),
       ("docs.html", "Guide"), ("changelog.html", "Journal"), ("about.html", "À propos")]

def page(fname, title, desc, body, scripts=(), private=False):
    nav = "".join(
        f'<a href="{h}"{" aria-current=\"page\"" if h == fname else ""}>{t}</a>' for h, t in NAV)
    return f'''<!doctype html>
<html lang="fr"{" data-notrack" if private else ""}>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title}</title>
<meta name="description" content="{desc}">
<meta name="theme-color" content="#0a0a0a">{chr(10) + '<meta name="robots" content="noindex, nofollow">' if private else ""}
<meta property="og:title" content="{title}">
<meta property="og:description" content="{desc}">
<meta property="og:type" content="website">
<meta property="og:image" content="assets/img/malbo.png">
<link rel="icon" href="favicon.ico" sizes="any">
<link rel="apple-touch-icon" href="assets/img/malbo.png">
<link rel="preload" href="assets/fonts/montserrat-latin.woff2" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="assets/css/style.css">
<script>document.documentElement.classList.add('js')</script>
<script src="assets/js/config.js"></script>
</head>
<body>
<a class="skip" href="#main">Aller au contenu</a>
<header class="header">
  <div class="wrap">
    <a class="brand" href="index.html"><img src="assets/img/malbo.png" alt="" width="34" height="34">Malbo</a>
    <button class="burger" aria-label="Menu" aria-expanded="false" aria-controls="nav">{ic("menu")}</button>
    <nav class="nav" id="nav" aria-label="Navigation principale">{nav}</nav>
    <a class="icon-btn" href="discord.html" aria-label="Rejoindre le Discord" title="Rejoindre le Discord">{ic("discord")}</a>
    <a class="btn btn-primary btn-sm" href="download.html">{ic("download")}Télécharger</a>
  </div>
</header>
<main id="main">
<div class="wrap">
{body}
</div>
</main>
<footer class="footer">
  <div class="wrap">
    <div>
      <a class="brand" href="index.html"><img src="assets/img/malbo.png" alt="" width="34" height="34">Malbo</a>
      <p>Une île flottante pour Windows : musique, paroles, notifications, widgets et plus. Projet indépendant, non affilié à Apple, Microsoft ou Spotify.</p>
    </div>
    <div>
      <h4>Site</h4>
      <ul>{"".join(f'<li><a href="{h}">{t}</a></li>' for h, t in NAV)}</ul>
    </div>
    <div>
      <h4>Projet</h4>
      <ul>
        <li><a href="{REPO}" rel="noopener">Code source sur GitHub</a></li>
        <li><a href="{REPO}/releases" rel="noopener">Versions</a></li>
        <li><a href="discord.html">Serveur Discord</a></li>
        <li><a href="{REPO}/issues" rel="noopener">Signaler un bug</a></li>
        <li><a href="dashboard.html" rel="nofollow">Statistiques</a></li>
      </ul>
    </div>
  </div>
</footer>
<script src="assets/js/track.js" defer></script>
<script src="assets/js/main.js" defer></script>{"".join(f'{chr(10)}<script src="assets/js/{x}" defer></script>' for x in scripts)}
</body>
</html>
'''

def card(cls, icon, title, text, extra="", tag="div", href=None):
    a = f' href="{href}"' if href else ""
    return f'''<{tag} class="card reveal {cls}"{a}>
  <div class="ico">{ic(icon)}</div>
  <h3>{title}</h3>
  <p>{text}</p>{extra}
</{tag}>'''

def li(items):
    return '<ul class="list">' + "".join(f"<li>{ic('check')}<span>{x}</span></li>" for x in items) + "</ul>"

def code(src):
    return f'<div class="code"><button class="copy" aria-label="Copier">{ic("copy")}</button><pre>{src}</pre></div>'

MORE = f'<span class="more">En savoir plus {ic("arrow-ur")}</span>'

CTA = f'''<div class="bento" style="margin-top:var(--gap)">
<div class="card card-white cta reveal">
  <div><h2>Prêt à essayer Malbo&nbsp;?</h2><p>Gratuit, un seul fichier, rien d'autre à installer.</p></div>
  <a class="btn btn-dark btn-lg" href="download.html">{ic("download")}Télécharger pour Windows</a>
</div>
</div>'''

# ------------------------------------------------------------------ Accueil
ISLAND = f'''<div class="card card-flush c5 reveal">
  <div class="stage">
    <div class="stage-row">
      <div class="island" data-state="music" tabindex="0" role="img" aria-label="Démonstration de l'île Malbo : musique, notification, minuteur, lecteur ouvert">
        <div class="view v-music"><span class="cover"></span><span class="v-title grow">Nuit blanche</span><span class="bars"><span></span><span></span><span></span><span></span></span></div>
        <div class="view v-notif"><span class="avatar">{ic("message")}</span><span class="grow"><span class="v-title" style="display:block">Léa · Discord</span><span class="v-sub" style="display:block">On lance une partie ce soir ?</span></span></div>
        <div class="view v-open">
          <div class="open-top"><span class="cover"></span><span class="grow"><span class="v-title" style="display:block">Nuit blanche</span><span class="v-sub" style="display:block">Les Insulaires</span></span>{ic("heart")}</div>
          <div class="prog"><i></i></div>
          <div class="ctrls">{ic("prev")}{ic("pause", "big")}{ic("next")}</div>
        </div>
      </div>
      <div class="bubble"><span class="bubble-in"><svg class="ring" viewBox="0 0 24 24"><circle class="t" cx="12" cy="12" r="9"/><circle class="p" cx="12" cy="12" r="9"/></svg><span class="tnum">24:59</span></span></div>
    </div>
    <div class="stage-cap"><div class="dots"><i></i><i></i><i></i><i></i></div>Survolez l'île pour l'ouvrir</div>
  </div>
</div>'''

index = f'''<div class="bento">
<section class="card card-grad hero c7 reveal">
  <img class="hero-logo" src="assets/img/malbo.png" alt="Logo Malbo" width="68" height="68">
  <h1>Une Dynamic Island <em>pour Windows.</em></h1>
  <p class="lead">Musique, paroles synchronisées, notifications, minuteurs et infos système, dans une île flottante en haut de votre écran.</p>
  <div class="hero-actions">
    <a class="btn btn-primary btn-lg" href="download.html">{ic("download")}Télécharger</a>
    <a class="btn btn-ghost btn-lg" href="features.html">Découvrir {ic("arrow")}</a>
  </div>
  <div class="hero-foot">
    <a class="round" href="{REPO}" rel="noopener" aria-label="Malbo sur GitHub">{ic("github")}</a>
    <a class="round" href="discord.html" aria-label="Rejoindre le Discord">{ic("discord")}</a>
    <a class="round" href="docs.html" aria-label="Guide">{ic("book")}</a>
    <a class="chip" style="margin-left:6px;text-decoration:none" href="changelog.html">v{VERSION}</a>
    <span class="chip">Windows 10 &amp; 11</span>
  </div>
</section>
{ISLAND}
{card("c3", "music", "Média", "Spotify, navigateurs, Deezer, VLC… Pochette, contrôles, progression et visualiseur.", MORE, "a", "features.html#media")}
{card("c3", "bell", "Notifications", "Discord, WhatsApp, Slack, mails… dans l'île, à la place des bannières Windows.", MORE, "a", "features.html#notifications")}
{card("c3", "timer", "Outils", "Minuteur, Pomodoro, alarmes, to-do, presse-papiers, calculatrice et convertisseur.", MORE, "a", "features.html#outils")}
<div class="card card-white c3 reveal">
  <div class="ico">{ic("download")}</div>
  <h3>Envie d'essayer&nbsp;?</h3>
  <p>Un seul fichier, gratuit, mises à jour automatiques.</p>
  <a class="btn btn-dark btn-sm" style="margin-top:18px" href="download.html">Télécharger {ic("arrow")}</a>
</div>
</div>

<div class="section-head">
  <div><span class="eyebrow">En détail</span><h2>Tout ce qui compte,<br>sans quitter ce que vous faites.</h2></div>
  <p>L'île reste discrète, s'ouvre au survol ou au clic, et se masque toute seule en plein écran.</p>
</div>

<div class="bento">
<div class="card c7 reveal">
  <div class="ico">{ic("mic")}</div>
  <h3>Paroles synchronisées</h3>
  <p>Ligne par ligne, dans l'île compacte ou en grand, avec la pochette floutée en fond.</p>
  <div class="lyrics" aria-hidden="true"><span>Les lumières de la ville</span><span class="on">défilent sous mes yeux</span><span>et la nuit recommence</span></div>
</div>
<div class="card c5 reveal">
  <div class="ico">{ic("cpu")}</div>
  <h3>Système en un coup d'œil</h3>
  <p>CPU, RAM, GPU, réseau, batterie, Wi-Fi, disques, Bluetooth, et un compteur FPS en jeu.</p>
  <div class="meters" aria-hidden="true">
    <div class="meter"><span>CPU</span><b><i data-w="23%" style="width:0"></i></b><em>23 %</em></div>
    <div class="meter"><span>RAM</span><b><i data-w="58%" style="width:0"></i></b><em>58 %</em></div>
    <div class="meter"><span>GPU</span><b><i data-w="41%" style="width:0"></i></b><em>41 %</em></div>
  </div>
</div>
<div class="card c4 reveal">
  <div class="ico">{ic("keyboard")}</div>
  <h3>Raccourcis globaux</h3>
  <p>Pilotez l'île sans lâcher le clavier.</p>
  <div style="margin-top:20px">
    <div class="keys"><kbd>Ctrl</kbd><kbd>Alt</kbd><kbd>I</kbd><small>Ouvrir</small></div>
    <div class="keys"><kbd>Ctrl</kbd><kbd>Alt</kbd><kbd>H</kbd><small>Masquer</small></div>
    <div class="keys"><kbd>Ctrl</kbd><kbd>Alt</kbd><kbd>Espace</kbd><small>Lecture</small></div>
  </div>
</div>
<div class="card c4 reveal">
  <div class="ico">{ic("contrast")}</div>
  <h3>Clair, sombre, auto</h3>
  <p>L'île suit le thème de Windows, ou celui que vous choisissez. La couleur d'accent vient de la pochette en cours.</p>
  <div class="themes" aria-hidden="true"><i></i><i></i><i></i></div>
</div>
<a class="card c4 reveal" href="docs.html#plugins">
  <div class="ico">{ic("plug")}</div>
  <h3>Extensible par plugins</h3>
  <p>Ajoutez vos propres onglets : une DLL qui implémente <code>IIslandPlugin</code>, déposée dans le dossier des plugins.</p>
  {MORE}
</a>
<a class="card c6 reveal" href="discord.html">
  <div class="ico">{ic("discord")}</div>
  <h3>Rejoignez le Discord</h3>
  <p>Aide, idées, avant-premières des prochaines versions : la communauté Malbo se retrouve sur Discord.</p>
  <span class="more">Rejoindre le serveur {ic("arrow-ur")}</span>
</a>
<a class="card c6 reveal" href="changelog.html">
  <div class="ico">{ic("history")}</div>
  <h3>Journal des versions</h3>
  <p>Nouveautés, correctifs et notes de développement, version après version. Dernière en date : <strong style="color:#fff">{VERSION}</strong>.</p>
  <span class="more">Lire le journal {ic("arrow-ur")}</span>
</a>
</div>
{CTA}'''

# ----------------------------------------------------------- Fonctionnalités
def feat(id_, cls, icon, title, items):
    return f'<section class="card reveal {cls}" id="{id_}"><div class="ico">{ic(icon)}</div><h3>{title}</h3>{li(items)}</section>'

features = f'''<div class="bento">
<div class="card card-grad page-head reveal">
  <span class="eyebrow">Fonctionnalités</span>
  <h1>Une seule île,<br>tout votre quotidien.</h1>
  <p>Malbo regroupe au même endroit ce que vous consultez cent fois par jour, sans fenêtre de plus à gérer.</p>
  <div class="toc">
    <a class="chip" href="#ile">Île</a><a class="chip" href="#media">Média</a><a class="chip" href="#notifications">Notifications</a>
    <a class="chip" href="#outils">Outils</a><a class="chip" href="#systeme">Système &amp; infos</a><a class="chip" href="#extensible">Plugins</a>
  </div>
</div>
{feat("ile", "c6", "island", "L'île", [
  "<strong>Compacte et animée</strong> (effet ressort), elle s'ouvre au survol ou au clic.",
  "<strong>Île coupée en deux</strong> : une bulle à côté pour une 2ᵉ activité (minuteur, appel, téléchargement).",
  "<strong>Aperçus temporaires</strong> : notifications, volume, luminosité, alertes. Glissez vers le haut pour fermer.",
  "Clic droit pour le menu rapide, masquage automatique en plein écran.",
  "Thèmes clair, sombre ou automatique."])}
{feat("media", "c6", "music", "Média", [
  "<strong>Spotify, navigateurs, Deezer, VLC…</strong> : pochette, contrôles, progression, visualiseur.",
  "<strong>Paroles synchronisées</strong> (lrclib.net), dans l'île compacte et en grand.",
  "Pochette floutée en fond, couleur d'accent tirée de la pochette.",
  "Aléatoire et répétition. Avec Spotify connecté : j'aime et ajout à une playlist.",
  "Volume par application."])}
{feat("notifications", "c4", "bell", "Notifications", [
  "<strong>Discord, Telegram, WhatsApp, Messenger, Slack, Teams, mails…</strong> affichées dans l'île.",
  "Clic pour ouvrir l'appli, glisser pour supprimer.",
  "Historique, Ne pas déranger, filtre par appli.",
  "Détection des appels Discord : qui parle, vous ou les autres."])}
{feat("outils", "c4", "timer", "Outils", [
  "<strong>Minuteur, chrono, Pomodoro</strong> avec mode Focus qui coupe les notifications.",
  "Alarmes et to-do.",
  "Étagère de fichiers en glisser-déposer, historique du presse-papiers, captures d'écran.",
  "<strong>Onglet Lancer</strong> : calculatrice et convertisseur (<code>10 km en miles</code>, <code>100 usd en eur</code>), raccourcis.",
  "Verrouiller, veille, redémarrer, éteindre."])}
{feat("systeme", "c4", "cpu", "Système &amp; infos", [
  "<strong>CPU, RAM, GPU, réseau, batterie</strong>, Wi-Fi, disques, Bluetooth.",
  "Micro et caméra en cours d'utilisation, téléchargements.",
  "Météo avec alerte pluie, horloges mondiales, agenda (ICS).",
  "Crypto, bourse, scores sportifs, amis Steam.",
  "<strong>Compteur FPS</strong> en jeu, via PresentMon."])}
<a class="card c8 reveal" id="extensible" href="docs.html#plugins">
  <div class="ico">{ic("plug")}</div>
  <h3>Extensible</h3>
  <p>Malbo charge vos plugins au démarrage : une DLL qui implémente <code>IIslandPlugin</code>, déposée dans <code>%AppData%\\Malbo\\plugins</code>, et votre vue apparaît dans l'île.</p>
  <span class="more">Écrire un plugin {ic("arrow-ur")}</span>
</a>
<a class="card c4 reveal" href="docs.html#raccourcis">
  <div class="ico">{ic("keyboard")}</div>
  <h3>Raccourcis clavier</h3>
  <p>Ouvrir, masquer, lecture / pause : trois raccourcis globaux.</p>
  <span class="more">Voir le guide {ic("arrow-ur")}</span>
</a>
</div>
{CTA}'''

# -------------------------------------------------------------- Télécharger
download = f'''<div class="bento">
<div class="card card-grad page-head c8 reveal">
  <span class="eyebrow">Télécharger</span>
  <h1>Malbo pour Windows.</h1>
  <p>Un seul fichier <code>Malbo.exe</code>, autonome : le runtime .NET est inclus, il n'y a rien d'autre à installer.</p>
  <div class="hero-actions">
    <a class="btn btn-primary btn-lg" href="{LATEST_URL}" rel="noopener">{ic("download")}Télécharger la {VERSION}{" (pré-version)" if PRE else ""}</a>
    <a class="btn btn-ghost btn-lg" href="{REL}" rel="noopener">Dernière version stable</a>
  </div>
  <div class="chips" style="margin-top:26px"><span class="chip">64 bits (x64)</span><span class="chip">Gratuit</span><a class="chip" style="text-decoration:none" href="changelog.html">Toutes les versions et leurs notes</a></div>
</div>
<div class="card c4 reveal">
  <div class="ico">{ic("monitor")}</div>
  <h3>Configuration requise</h3>
  {li(["<strong>Windows 10</strong> version 2004 ou plus récent, ou <strong>Windows 11</strong>.",
       "Processeur 64 bits (x64).",
       "Facultatif : un compte Spotify, PresentMon pour le compteur FPS.",
       "L'exécutable n'est pas signé : Windows SmartScreen peut demander une confirmation au premier lancement."])}
</div>
<div class="card c6 reveal">
  <div class="ico">{ic("zap")}</div>
  <h3>Installation en trois étapes</h3>
  <ol class="steps" style="margin-top:20px">
    <li><span><strong>Téléchargez</strong>Récupérez <code>Malbo.exe</code> dans la dernière version publiée sur GitHub.</span></li>
    <li><span><strong>Lancez</strong>Double-cliquez : l'île apparaît en haut de l'écran et une icône s'ajoute à la barre des tâches.</span></li>
    <li><span><strong>Installez (facultatif)</strong>Icône de la barre des tâches › <em>Installer Malbo sur ce PC</em> : raccourcis Menu Démarrer et Bureau, désinstallation via les Paramètres Windows.</span></li>
  </ol>
</div>
<div class="card c6 reveal">
  <div class="ico">{ic("refresh")}</div>
  <h3>Mises à jour automatiques</h3>
  <p>Depuis la 1.2, Malbo se met à jour tout seul à partir des versions publiées sur GitHub.</p>
  {li(["Vérification <strong>au démarrage, puis toutes les 6 h</strong>.",
       "Installation automatique quand l'île est au repos (désactivable dans <strong>Réglages › Mises à jour</strong>).",
       "Le fichier téléchargé est vérifié par son empreinte <strong>SHA-256</strong> avant de remplacer l'ancien.",
       "<strong>Canal bêta</strong> en option, pour recevoir aussi les pré-versions."])}
</div>
<div class="card reveal">
  <div class="ico">{ic("code")}</div>
  <h3>Compiler depuis les sources</h3>
  <p>Il vous faut le <a href="https://dotnet.microsoft.com/download" rel="noopener">SDK .NET 10</a>. Clonez le dépôt puis compilez :</p>
  {code(f'''git clone {REPO}.git
cd Malbo
dotnet build -c Release
.\\bin\\Release\\net10.0-windows10.0.22621.0\\Malbo.exe''')}
  <p style="margin-top:18px">Pour produire l'exécutable autonome d'une version, lancez <code>.\\publish.ps1</code> : il crée <code>Malbo.exe</code>, à joindre à la release GitHub.</p>
</div>
<a class="card c6 reveal" href="docs.html">
  <div class="ico">{ic("book")}</div>
  <h3>Et ensuite&nbsp;?</h3>
  <p>Connecter Spotify, ajouter votre agenda, activer le compteur FPS : tout est dans le guide.</p>
  <span class="more">Ouvrir le guide {ic("arrow-ur")}</span>
</a>
<a class="card c6 reveal" href="{REPO}/issues" rel="noopener">
  <div class="ico">{ic("message")}</div>
  <h3>Un souci&nbsp;?</h3>
  <p>Le journal d'erreurs se trouve dans <code>%AppData%\\Malbo\\error.log</code>. Joignez-le à votre signalement sur GitHub.</p>
  <span class="more">Signaler un bug {ic("arrow-ur")}</span>
</a>
</div>'''

# -------------------------------------------------------------------- Guide
PLUGIN = '''<span class="c">// Bibliothèque de classes WPF qui référence Malbo.dll</span>
using System.Windows;
using System.Windows.Controls;
using DynamicIsland;

public class HelloPlugin : IIslandPlugin
{
    public string Name  =&gt; "Hello";
    public string Glyph =&gt; "\\uE76E";   <span class="c">// glyphe Segoe Fluent Icons</span>

    public UIElement CreateView() =&gt;
        new TextBlock { Text = "Bonjour depuis mon plugin !" };
}'''

docs = f'''<div class="bento">
<div class="card card-grad page-head reveal">
  <span class="eyebrow">Guide</span>
  <h1>Prendre Malbo en main.</h1>
  <p>Tout se règle dans <strong>Réglages</strong> : icône de la barre des tâches, ou roue crantée dans l'île.</p>
  <div class="toc">
    <a class="chip" href="#raccourcis">Raccourcis</a><a class="chip" href="#gestes">Gestes</a><a class="chip" href="#configuration">Configuration</a>
    <a class="chip" href="#plugins">Plugins</a><a class="chip" href="#donnees">Fichiers &amp; données</a>
  </div>
</div>
<section class="card c7 reveal" id="raccourcis">
  <div class="ico">{ic("keyboard")}</div>
  <h3>Raccourcis clavier</h3>
  <table class="table">
    <thead><tr><th>Touches</th><th>Action</th></tr></thead>
    <tbody>
      <tr><td><span class="keys"><kbd>Ctrl</kbd><kbd>Alt</kbd><kbd>I</kbd></span></td><td>Ouvrir / épingler l'île</td></tr>
      <tr><td><span class="keys"><kbd>Ctrl</kbd><kbd>Alt</kbd><kbd>H</kbd></span></td><td>Masquer / afficher</td></tr>
      <tr><td><span class="keys"><kbd>Ctrl</kbd><kbd>Alt</kbd><kbd>Espace</kbd></span></td><td>Lecture / pause</td></tr>
    </tbody>
  </table>
</section>
<section class="card c5 reveal" id="gestes">
  <div class="ico">{ic("island")}</div>
  <h3>Gestes</h3>
  {li(["<strong>Survol ou clic</strong> : ouvrir l'île.",
       "<strong>Clic droit</strong> : menu rapide.",
       "<strong>Glisser vers le haut</strong> : fermer un aperçu.",
       "<strong>Clic sur une notification</strong> : ouvrir l'appli. Glisser : la supprimer.",
       "<strong>Glisser-déposer un fichier</strong> : le ranger sur l'étagère."])}
</section>
</div>

<div class="section-head" id="configuration">
  <div><span class="eyebrow">Configuration</span><h2>Connecter vos services.</h2></div>
  <p>Tous ces réglages sont facultatifs : Malbo fonctionne dès le premier lancement.</p>
</div>
<div class="bento">
<section class="card c6 reveal">
  <div class="ico">{ic("music")}</div>
  <h3>Spotify</h3>
  <p>Pour le bouton j'aime et l'ajout à une playlist.</p>
  <ol class="steps" style="margin-top:20px">
    <li><span>Créez une appli sur <a href="https://developer.spotify.com/dashboard" rel="noopener">developer.spotify.com</a>.</span></li>
    <li><span>Ajoutez la Redirect URI <code>http://127.0.0.1:8898/callback</code>.</span></li>
    <li><span>Collez le Client ID dans les Réglages, puis cliquez sur <strong>Connecter Spotify</strong>.</span></li>
  </ol>
</section>
<section class="card c6 reveal">
  <div class="ico">{ic("calendar")}</div>
  <h3>Agenda</h3>
  <p>Malbo lit n'importe quel agenda au format ICS.</p>
  <p>Avec Google Agenda : <strong style="color:#fff">Paramètres › Adresse secrète au format iCal</strong>, puis collez l'URL dans les Réglages.</p>
</section>
<section class="card c6 reveal">
  <div class="ico">{ic("message")}</div>
  <h3>Amis Steam</h3>
  <p>Renseignez votre <strong style="color:#fff">clé API Steam</strong> et votre <strong style="color:#fff">SteamID64</strong> pour voir vos amis en ligne dans l'île.</p>
</section>
<section class="card c6 reveal">
  <div class="ico">{ic("gauge")}</div>
  <h3>Compteur FPS</h3>
  <p>Téléchargez <a href="https://github.com/GameTechDev/PresentMon/releases" rel="noopener">PresentMon</a> et indiquez son chemin dans les Réglages : les FPS s'affichent dans l'île pendant vos parties.</p>
</section>
<section class="card c8 reveal" id="plugins">
  <div class="ico">{ic("plug")}</div>
  <h3>Écrire un plugin</h3>
  <p>Créez une bibliothèque de classes WPF qui référence <code>Malbo.dll</code>, implémentez <code>IIslandPlugin</code>, puis déposez la DLL dans <code>%AppData%\\Malbo\\plugins</code>. Elle est chargée au prochain démarrage.</p>
  {code(PLUGIN)}
</section>
<section class="card c4 reveal" id="donnees">
  <div class="ico">{ic("folder")}</div>
  <h3>Fichiers &amp; données</h3>
  <p>Tout reste sur votre PC, dans <code>%AppData%\\Malbo</code>.</p>
  {li(["<code>settings.json</code> : réglages et données.",
       "<code>error.log</code> : journal d'erreurs.",
       "<code>plugins\\</code> : vos extensions."])}
</section>
</div>
{CTA}'''

# ----------------------------------------------------------------- À propos
about = f'''<div class="bento">
<div class="card card-grad page-head c8 reveal">
  <span class="eyebrow">À propos</span>
  <h1>Une île, pensée pour Windows.</h1>
  <p>Malbo part d'une idée simple : la musique, les notifications et les petites infos du quotidien méritent mieux que des bannières qui surgissent et des fenêtres à chercher. Tout tient dans une île, en haut de l'écran.</p>
</div>
<div class="card card-white c4 reveal" style="display:flex;flex-direction:column;justify-content:space-between;gap:24px">
  <img src="assets/img/malbo.png" alt="Logo Malbo" width="84" height="84" style="border-radius:22px">
  <div><div class="big-num">v{VERSION}</div><p>Dernière version{" (pré-version)" if PRE else ""}, pour Windows 10 et 11.</p></div>
</div>
{card("c4", "island", "Discrète", "L'île reste compacte, se masque en plein écran et ne s'ouvre que lorsque vous en avez besoin.")}
{card("c4", "zap", "Vivante", "Animations à ressort, visualiseur audio, couleur d'accent tirée de la pochette en cours.")}
{card("c4", "folder", "Locale", "Vos réglages et vos données restent sur votre PC, dans <code>%AppData%\\Malbo</code>.")}
<div class="card c6 reveal">
  <div class="ico">{ic("code")}</div>
  <h3>Sous le capot</h3>
  <p>Une application de bureau native, écrite en C#.</p>
  <div class="chips" style="margin-top:20px"><span class="chip">C#</span><span class="chip">WPF</span><span class="chip">.NET 10</span><span class="chip">NAudio</span><span class="chip">API Windows</span></div>
</div>
<div class="card c6 reveal">
  <div class="ico">{ic("heart")}</div>
  <h3>Merci à</h3>
  {li(['<a href="https://lrclib.net" rel="noopener">lrclib.net</a> pour les paroles synchronisées.',
       '<a href="https://github.com/GameTechDev/PresentMon" rel="noopener">PresentMon</a> pour la mesure des FPS.',
       '<a href="https://github.com/naudio/NAudio" rel="noopener">NAudio</a> pour l\'audio.',
       'La police <a href="https://github.com/JulietaUla/Montserrat" rel="noopener">Montserrat</a> (licence OFL) utilisée sur ce site.'])}
</div>
<a class="card c6 reveal" href="{REPO}" rel="noopener">
  <div class="ico">{ic("github")}</div>
  <h3>Le code est sur GitHub</h3>
  <p>Malbo est développé par MattRvfl. Parcourez le code, suivez les versions, proposez une amélioration.</p>
  <span class="more">Voir le dépôt {ic("arrow-ur")}</span>
</a>
<a class="card c6 reveal" href="{REPO}/issues" rel="noopener">
  <div class="ico">{ic("message")}</div>
  <h3>Une idée, un bug&nbsp;?</h3>
  <p>Les retours se passent dans les issues GitHub : signalement de bug, demande de fonctionnalité, question.</p>
  <span class="more">Ouvrir une issue {ic("arrow-ur")}</span>
</a>
<div class="card reveal">
  <div class="ico">{ic("info")}</div>
  <h3>Mentions</h3>
  <p>Malbo est un projet indépendant. « Dynamic Island » est une marque d'Apple Inc. ; Windows est une marque de Microsoft ; Spotify, Discord, Steam et les autres noms cités appartiennent à leurs propriétaires respectifs. Malbo n'est affilié à aucune de ces sociétés.</p>
</div>
<div class="card reveal" id="confidentialite">
  <div class="ico">{ic("lock")}</div>
  <h3>Confidentialité du site</h3>
  <p>Ce site mesure son audience de façon anonyme et sans cookie : page vue, site de provenance, type d'appareil, navigateur, langue et fuseau horaire. Aucune adresse IP, aucun nom ni identifiant durable n'est enregistré ; un identifiant aléatoire, effacé à la fermeture de l'onglet, sert seulement à compter une visite. Les données sont hébergées en Europe (Supabase, Paris). Le réglage « Ne pas me pister » de votre navigateur est respecté.</p>
  <p><label class="check"><input type="checkbox" id="optout"> Ne pas compter mes visites sur ce navigateur</label></p>
</div>
</div>
{CTA}'''

notfound = f'''<div class="bento">
<div class="card card-grad page-head reveal" style="text-align:center;padding:96px 24px">
  <div class="stage-row" style="margin-bottom:36px"><div class="island" style="cursor:default"><div class="view" style="opacity:1;transform:none;filter:none;justify-content:center"><span class="tnum">404</span></div></div></div>
  <h1 style="margin-top:0">Cette page a pris le large.</h1>
  <p style="margin-left:auto;margin-right:auto">L'adresse demandée n'existe pas, ou plus.</p>
  <div class="hero-actions" style="justify-content:center"><a class="btn btn-primary btn-lg" href="index.html">Retour à l'accueil {ic("arrow")}</a></div>
</div>
</div>'''


# ------------------------------------------------------------------ Journal
changelog = f'''<div class="bento">
<div class="card card-grad page-head c8 reveal">
  <span class="eyebrow">Journal</span>
  <h1>Versions, correctifs<br>et notes de dev.</h1>
  <p>Tout ce qui change dans Malbo, version après version. Cette page se met à jour toute seule à chaque nouvelle publication sur GitHub.</p>
  <div class="toc">
    <a class="chip" href="#versions">Versions</a><a class="chip" href="#dev">Activité de développement</a>
    <a class="chip" href="{REPO}/releases.atom" rel="noopener">Flux RSS</a>
  </div>
</div>
<div class="card card-white c4 reveal" style="display:flex;flex-direction:column;justify-content:space-between;gap:24px">
  <div class="ico">{ic("history")}</div>
  <div><div class="big-num">v{VERSION}</div><p>Dernière version publiée{" (pré-version)" if PRE else ""}. <span id="rel-count">{len(_rel)}</span> versions au total.</p>
  <a class="btn btn-dark btn-sm" style="margin-top:16px" href="download.html">Télécharger {ic("arrow")}</a></div>
</div>
</div>

<div class="section-head" id="versions">
  <div><span class="eyebrow">Versions</span><h2>Notes de version.</h2></div>
  <div class="seg" role="group" aria-label="Filtrer les versions">
    <button data-filter="all" aria-pressed="true">Toutes</button><button data-filter="stable" aria-pressed="false">Stables</button><button data-filter="pre" aria-pressed="false">Pré-versions</button>
  </div>
</div>
<div class="rels" id="releases"><div class="card"><p>Chargement des versions…</p></div></div>

<div class="section-head" id="dev">
  <div><span class="eyebrow">Dev log</span><h2>Activité de développement.</h2></div>
  <p>Les dernières modifications du code, telles qu'elles arrivent dans le dépôt.</p>
</div>
<div class="bento">
<div class="card c8 reveal"><ul class="log" id="devlog"><li><p>Chargement…</p></li></ul></div>
<div class="card c4 reveal">
  <div class="ico">{ic("message")}</div>
  <h3>Un bug à signaler&nbsp;?</h3>
  <p>Décrivez-le sur GitHub ou sur le Discord, avec le fichier <code>%AppData%\\Malbo\\error.log</code>. Les correctifs apparaissent ici dès qu'ils sont publiés.</p>
  <div class="hero-actions" style="margin-top:18px">
    <a class="btn btn-primary btn-sm" href="{REPO}/issues" rel="noopener">Ouvrir une issue</a>
    <a class="btn btn-ghost btn-sm" href="discord.html">{ic("discord")}Discord</a>
  </div>
</div>
</div>'''

# ------------------------------------------------------------------ Discord
discord = f'''<div class="bento">
<div class="card card-grad page-head reveal" style="text-align:center;padding:88px 24px">
  <div class="ico" style="margin:0 auto 26px;width:72px;height:72px;border-radius:22px;background:#fff;color:#000;border-color:#fff">{ic("discord")}</div>
  <h1 style="margin-top:0" id="dc-title">Direction le Discord de Malbo…</h1>
  <p style="margin-left:auto;margin-right:auto" id="dc-text">Vous allez être redirigé dans un instant.</p>
  <div class="hero-actions" style="justify-content:center">
    <a class="btn btn-primary btn-lg" id="dc-btn" href="index.html" hidden>{ic("discord")}Ouvrir Discord</a>
    <a class="btn btn-ghost btn-lg" href="index.html">Retour au site</a>
  </div>
</div>
</div>
<script>
(function () {{
  var url = (window.MALBO || {{}}).discord, btn = document.getElementById('dc-btn');
  if (!url) {{
    document.getElementById('dc-title').textContent = 'Le serveur Discord arrive bientôt.';
    document.getElementById('dc-text').textContent = "Le lien d'invitation n'est pas encore en ligne. En attendant, suivez le projet sur GitHub.";
    return;
  }}
  btn.href = url; btn.hidden = false;
  window.addEventListener('load', function () {{
    if (window.malboTrack) window.malboTrack('click', 'discord');
    setTimeout(function () {{ location.replace(url); }}, 700);
  }});
}})();
</script>'''

# ------------------------------------------------------------- Statistiques
def panel(cls, icon, title, body_id, note=""):
    n = f'<p class="panel-note">{note}</p>' if note else ""
    return f'<section class="card {cls}"><div class="panel-head">{ic(icon)}<h3>{title}</h3></div>{n}<div class="blist" id="{body_id}"></div></section>'

dashboard = f'''<div class="bento" id="login">
<div class="card card-grad page-head" style="max-width:560px;margin:40px auto;grid-column:1/-1;width:100%">
  <div class="ico">{ic("lock")}</div>
  <h1 style="margin-top:0;font-size:34px">Statistiques du site</h1>
  <p>Cette page est privée. Entrez le mot de passe du tableau de bord.</p>
  <form id="login-form" class="login">
    <input type="password" id="pass" autocomplete="current-password" placeholder="Mot de passe" aria-label="Mot de passe" required>
    <button class="btn btn-primary" type="submit">Ouvrir {ic("arrow")}</button>
  </form>
  <p class="err" id="login-err" role="alert"></p>
  <p style="font-size:13.5px"><a href="?demo">Voir un exemple avec des données de démonstration</a></p>
</div>
</div>

<div id="dash" hidden>
<div class="dash-top">
  <div><span class="eyebrow">Tableau de bord</span><h1>D'où viennent vos visiteurs.</h1></div>
  <div class="dash-tools">
    <div class="seg" role="group" aria-label="Période">
      <button data-days="7" aria-pressed="false">7 j</button><button data-days="30" aria-pressed="true">30 j</button><button data-days="90" aria-pressed="false">90 j</button><button data-days="365" aria-pressed="false">12 mois</button>
    </div>
    <button class="icon-btn" id="refresh" aria-label="Actualiser" title="Actualiser">{ic("refresh")}</button>
    <button class="icon-btn" id="logout" aria-label="Se déconnecter" title="Se déconnecter">{ic("lock")}</button>
  </div>
</div>
<p class="demo-note" id="demo-note" hidden>{ic("info")}Données de démonstration : ces chiffres sont inventés pour montrer le tableau de bord. <a href="dashboard.html">Voir les vraies données</a></p>

<div class="kpis" id="kpis"></div>

<div class="bento">
<section class="card c8">
  <div class="panel-head">{ic("chart")}<h3 id="chart-title">Visites par jour</h3>
    <div class="seg seg-sm" role="group" aria-label="Mesure affichée">
      <button data-metric="visits" aria-pressed="true">Visites</button><button data-metric="pageviews" aria-pressed="false">Pages vues</button><button data-metric="downloads" aria-pressed="false">Téléch.</button><button data-metric="discord" aria-pressed="false">Discord</button>
    </div>
  </div>
  <div class="chart" id="chart"></div>
</section>
{panel("c4", "globe", "Canaux", "channels", "Par quel type de chemin les visiteurs arrivent.")}
<section class="card">
  <div class="panel-head">{ic("users")}<h3>Provenance détaillée</h3></div>
  <p class="panel-note">Chaque réseau ou site, avec ce qu'il rapporte vraiment : visites, clics sur Télécharger, clics vers Discord et taux de conversion.</p>
  <div id="sources" class="scroll-x"></div>
</section>
{panel("c6", "link", "Sites référents", "referrers", "Les sites web qui ont un lien vers Malbo.")}
<section class="card c6">
  <div class="panel-head">{ic("zap")}<h3>Campagnes</h3></div>
  <p class="panel-note">Vos liens suivis, publication par publication.</p>
  <div id="campaigns" class="scroll-x"></div>
</section>
{panel("c6", "book", "Pages les plus vues", "pages")}
<section class="card c6">
  <div class="panel-head">{ic("clock")}<h3>Heures de visite</h3></div>
  <p class="panel-note">Pages vues par heure de la journée (heure de Paris) : le bon moment pour publier.</p>
  <div class="hours" id="hours"></div>
</section>
{panel("c4", "monitor", "Appareils", "devices")}
{panel("c4", "globe", "Navigateurs", "browsers")}
{panel("c4", "cpu", "Systèmes", "os")}
{panel("c6", "message", "Langues", "langs")}
{panel("c6", "globe", "Fuseaux horaires", "zones", "Indique la région des visiteurs sans relever leur adresse IP.")}
<section class="card c7">
  <div class="panel-head">{ic("link")}<h3>Générateur de liens suivis</h3></div>
  <p class="panel-note">TikTok, Instagram, Discord et la plupart des applis masquent la provenance. Partagez un lien différent par réseau : chaque clic est alors attribué au bon endroit.</p>
  <div class="lb">
    <label>Réseau ou site<input id="lb-src" list="lb-list" value="tiktok" spellcheck="false"></label>
    <datalist id="lb-list"><option value="tiktok"><option value="youtube"><option value="discord"><option value="instagram"><option value="x"><option value="reddit"><option value="twitch"><option value="snapchat"><option value="github"></datalist>
    <label>Campagne (facultatif)<input id="lb-camp" placeholder="ex. video-demo" spellcheck="false"></label>
  </div>
  <div class="lb-out">
    <label>Vers l'accueil<span><input id="lb-site" readonly><button class="btn btn-ghost btn-sm" data-copy="lb-site">Copier</button></span></label>
    <label>Vers le téléchargement<span><input id="lb-dl" readonly><button class="btn btn-ghost btn-sm" data-copy="lb-dl">Copier</button></span></label>
    <label>Vers le Discord (redirection comptée)<span><input id="lb-discord" readonly><button class="btn btn-ghost btn-sm" data-copy="lb-discord">Copier</button></span></label>
  </div>
</section>
<section class="card c5">
  <div class="panel-head">{ic("history")}<h3>Activité récente</h3></div>
  <ul class="recent" id="recent"></ul>
</section>
</div>
<p class="dash-foot"><span id="updated"></span> · <label class="check"><input type="checkbox" id="own"> Ne pas compter mes propres visites sur ce navigateur</label></p>
</div>'''

PAGES = [
 ("index.html", "Malbo, une Dynamic Island pour Windows", "Musique, paroles synchronisées, notifications, widgets et infos système dans une île flottante en haut de l'écran. Gratuit, pour Windows 10 et 11.", index),
 ("features.html", "Fonctionnalités · Malbo", "Île animée, média et paroles synchronisées, notifications, minuteurs, infos système, plugins : tout ce que fait Malbo.", features),
 ("download.html", "Télécharger · Malbo", "Téléchargez Malbo pour Windows 10 et 11 : un seul fichier autonome, mises à jour automatiques.", download),
 ("docs.html", "Guide · Malbo", "Raccourcis, gestes, configuration de Spotify, de l'agenda, de Steam et du compteur FPS, écriture de plugins.", docs),
 ("about.html", "À propos · Malbo", "Le projet Malbo : l'idée, la technique, les remerciements et les mentions.", about),
 ("changelog.html", "Journal des versions · Malbo", "Notes de version, correctifs et activité de développement de Malbo, mis à jour à chaque publication.", changelog, ("changelog.js",), False),
 ("discord.html", "Discord · Malbo", "Rejoignez le serveur Discord de Malbo.", discord, (), False),
 ("dashboard.html", "Statistiques · Malbo", "Tableau de bord privé des visites du site Malbo.", dashboard, ("dashboard.js",), True),
 ("404.html", "Page introuvable · Malbo", "Cette page n'existe pas.", notfound),
]
for f, t, d, b, *rest in PAGES:
    (OUT / f).write_text(page(f, t, d, b, *rest), encoding="utf-8")
(OUT / ".nojekyll").write_text("")
print("ok", [p[0] for p in PAGES])
