# KeyStats

KeyStats mesure la frappe réelle au clavier sous Windows (disposition AZERTY) : vitesse pendant les vraies sessions d'écriture, fautes de frappe corrigées et leur coût, rythme par paire de lettres, raccourcis, touches les plus utilisées, activité par application et par heure. Un agent très léger tourne en permanence et enregistre ; un tableau de bord s'ouvre à la demande pour consulter les statistiques.

## Vie privée

- Les données restent sur la machine locale. Aucun envoi réseau, aucun port ouvert : le tableau de bord sert sa page depuis la mémoire, sans serveur, et aucune bibliothèque n'est chargée depuis Internet.
- Le texte tapé et les titres de fenêtres ne sont jamais écrits en clair, ni dans la base, ni dans un journal ou un fichier temporaire. Ils sont chiffrés (AES-256-GCM, clé dérivée par ECDH). L'agent ne détient que la clé publique : il peut chiffrer, jamais relire. La clé privée n'existe en clair que dans la mémoire du tableau de bord, après Windows Hello ou la phrase de secours, et elle est effacée au verrouillage et à la fermeture.
- Les statistiques agrégées (vitesses, compteurs par touche, fautes par paire de lettres) sont stockées en clair : ce sont des compteurs et des durées, pas le texte.
- Les frappes injectées par un logiciel sont ignorées. Les gestionnaires de mots de passe (KeePass, KeePassXC, Bitwarden, 1Password), le bureau à distance et les invites d'identifiants de Windows sont exclus par défaut, et les champs de mot de passe détectés : seul le nombre de touches y est compté, sans contenu ni analyse.
- Un vidage mémoire du moteur de la page peut contenir la phrase saisie ou du texte déchiffré. Ces rapports ne sont pas envoyés à Microsoft, le tableau de bord interdit leur écriture dans son profil WebView2 et supprime ce qui y serait quand même arrivé. Reste hors de portée un vidage du tableau de bord lui-même par le rapport d'erreurs de Windows.

## Configuration requise

- Windows 10 version 2004 (build 19041) ou plus récent, ou Windows 11, en 64 bits.
- [Runtime WebView2](https://developer.microsoft.com/microsoft-edge/webview2/), déjà présent sur Windows 11 et sur les Windows 10 à jour.
- Pour construire depuis les sources : [SDK .NET 10](https://dotnet.microsoft.com/download) et les outils de compilation C++ de Visual Studio (charge de travail « Développement Desktop en C++ »), exigés par la compilation NativeAOT de l'agent. Le tableau de bord utilise le runtime .NET 10 fourni par le SDK.
- Windows Hello (visage, empreinte ou code PIN) est facultatif : la phrase de secours suffit.

Aucun droit d'administrateur n'est nécessaire.

## Installation

Depuis la racine du dépôt :

```powershell
powershell -ExecutionPolicy Bypass -File tools\install.ps1
```

Le script compile l'agent (NativeAOT) et le tableau de bord, les copie dans `%LOCALAPPDATA%\Programs\KeyStats\`, inscrit l'agent au démarrage de la session (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), crée le raccourci « KeyStats » dans le menu Démarrer, puis lance l'agent et ouvre le tableau de bord. Au premier lancement, un assistant configure le chiffrement : phrase de secours, puis Windows Hello si disponible.

Relancer le même script met à jour l'installation : l'agent en cours reçoit l'ordre de s'arrêter après avoir écrit ce qu'il détient, puis les fichiers sont remplacés. Le dossier de données n'est jamais modifié par l'installation. `-NoStart` installe sans lancer l'agent ni le tableau de bord.

## Désinstallation

```powershell
powershell -ExecutionPolicy Bypass -File tools\uninstall.ps1
```

Le script arrête l'agent et le tableau de bord, retire l'entrée de démarrage, le raccourci et le dossier `%LOCALAPPDATA%\Programs\KeyStats\`. Les données sont conservées dans `%LOCALAPPDATA%\KeyStats\`, ce que le script rappelle : une réinstallation retrouve tout l'historique. Pour les supprimer aussi, après confirmation :

```powershell
powershell -ExecutionPolicy Bypass -File tools\uninstall.ps1 -RemoveData
```

Cette suppression est définitive. Un dossier de sauvegarde choisi hors du dossier de données reste en place, de même que la clé Windows Hello créée par l'assistant. `-WhatIf` affiche ce qui serait retiré sans rien modifier.

## Utilisation

### Icône de notification

Windows 11 range les nouvelles icônes dans le menu déroulant de la zone de notification. Pour garder KeyStats visible : Paramètres › Personnalisation › Barre des tâches › Autres icônes de la barre d'état système, puis activer KeyStats. Il est aussi possible de faire glisser l'icône du menu déroulant vers la barre des tâches.

- Clic gauche : pause de 5 minutes, et 5 minutes de plus à chaque clic. L'icône passe en gris avec un badge pause ; l'infobulle donne l'heure de reprise. Le compteur en direct confirme chaque clic pendant 1,5 seconde (« ⏸ 15 min »).
- Clic droit : pause jusqu'à reprise manuelle, reprise immédiate, quitter.
- Pendant une pause, rien n'est enregistré, ni compteurs ni texte. Le verrouillage de la session et la mise en veille suspendent aussi l'enregistrement, qui reprend au déverrouillage.

### Tableau de bord

Le raccourci « KeyStats » du menu Démarrer ouvre le tableau de bord : vue d'ensemble, vitesse, fautes, clavier, rythme, lettres et mots, raccourcis, activité et origine, détail à la milliseconde, réglages. Les statistiques s'affichent sans déverrouillage.

Les vues qui relisent le texte tapé ou les titres de fenêtres, marquées d'un cadenas, demandent de déverrouiller les données chiffrées par Windows Hello ou par la phrase de secours. La clé reste alors en mémoire jusqu'au bouton « Verrouiller » ou à la fermeture de la fenêtre. La phrase de secours est la seule façon de relire les données si Windows Hello n'est plus disponible (nouvelle machine, réinstallation de Windows) : elle doit être conservée à part.

La page Réglages modifie les applications exclues, les seuils d'analyse, le compteur en direct, le dossier de sauvegarde et la phrase de secours, propose l'export CSV et le recalcul de l'historique.

### Compteur en direct

Quand une vraie frappe commence (au moins 12 caractères et 2 espaces en 6 secondes), un petit compteur apparaît dans un coin de la zone de travail de l'écran où se trouve la fenêtre active, à l'écart de la barre des tâches (en bas à droite par défaut) : vitesse en mots par minute, et en caractères par minute en dessous. Après 4 secondes sans frappe, il affiche le bilan de la rafale (vitesse et précision) pendant 3 secondes, puis disparaît. Il laisse passer les clics, ne prend jamais le focus et n'apparaît ni dans Alt+Tab ni sur les captures et partages d'écran. Il reste caché quand une application occupe tout l'écran (jeu, vidéo) et en mode présentation. Une pause le cache aussitôt. `liveCounter.enabled` à `false` dans `settings.json` le désactive ; `liveCounter.position` choisit le coin et `liveCounter.opacity` l'opacité, de 20 à 100 % (voir [docs/settings.md](docs/settings.md)).

## Emplacements

| Contenu | Emplacement |
| --- | --- |
| Programmes | `%LOCALAPPDATA%\Programs\KeyStats\` |
| Base de données | `%LOCALAPPDATA%\KeyStats\keystats.db` |
| Réglages | `%LOCALAPPDATA%\KeyStats\settings.json` (voir [docs/settings.md](docs/settings.md)) |
| Sauvegarde | `%LOCALAPPDATA%\KeyStats\backup\keystats.db`, ou le dossier choisi dans Réglages |
| Profil WebView2 | `%LOCALAPPDATA%\KeyStats\webview\` |

La sauvegarde est une copie miroir de la base, mise à jour une fois par nuit à partir de 3 h, ou à la première occasion si la machine était éteinte. Le texte et les titres y restent chiffrés comme dans la base. Un dossier sur un autre disque ou synchronisé protège contre la perte du disque principal.

## Développement

```powershell
dotnet build KeyStats.slnx -warnaserror
dotnet format KeyStats.slnx --verify-no-changes
dotnet test --project tests/KeyStats.Core.Tests
dotnet publish src/KeyStats.Agent -c Release   # NativeAOT
```

Règles du projet dans [CLAUDE.md](CLAUDE.md), méthode de contribution dans [CONTRIBUTING.md](CONTRIBUTING.md), historique dans [CHANGELOG.md](CHANGELOG.md).

### Agent sur une base de test

```powershell
dotnet publish src/KeyStats.Agent -c Release -o publish\agent
publish\agent\KeyStats.Agent.exe --db $env:TEMP\keystats-test.db
```

Sans `--db`, l'agent écrit dans `%LOCALAPPDATA%\KeyStats\keystats.db`. `--probe <secondes> [--db <chemin>]` capture pendant la durée donnée puis affiche des compteurs, sans aucun contenu.

### Commandes de l'agent

Un seul agent tourne par base et par session Windows : un second lancement s'arrête aussitôt. L'agent écoute un tube nommé `\\.\pipe\KeyStats.Agent.<session>.<clé>`, réservé à l'utilisateur courant, où la clé dérive du chemin de la base. Chaque connexion porte une commande en ASCII suivie d'un saut de ligne, et reçoit une ligne en réponse (`ok`, `unknown` ou `failed`) :

| Commande | Effet |
| --- | --- |
| `snooze` | 5 minutes de pause de plus, comme un clic sur l'icône |
| `pause` | pause jusqu'à reprise manuelle |
| `resume` | fin de la pause par l'icône |
| `flush` | écrit tout ce qui est en attente avant de répondre ; `failed` si l'écriture échoue |
| `reload` | relit `settings.json` : applis exclues et dossier de sauvegarde appliqués tout de suite, seuils d'analyse au prochain démarrage ; `failed` si le fichier est refusé ou si l'agent ne peut pas les transmettre à la capture, les réglages en place sont alors gardés |
| `quit` | arrête l'agent, comme « Quitter KeyStats » |

Aucun texte ni titre de fenêtre ne passe par ce tube. La commande `snooze` du tube n'affiche pas de confirmation sur le compteur.

### Tableau de bord sur une base de test

```powershell
dotnet run --project src/KeyStats.Dashboard -- --db $env:TEMP\keystats-test.db
```

La fenêtre affiche une page WebView2 servie en mémoire sous `https://keystats.app/` : les fichiers de `wwwroot` et les routes `/api/…` passent par l'interception des requêtes, sans serveur ni port ouvert. Toute autre adresse est refusée. Le profil WebView2 est rangé dans `webview\`, à côté de la base. Le corps de la requête qui porte la phrase de secours est effacé après usage, mais les chaînes JavaScript et .NET restent en mémoire jusqu'au passage du ramasse-miettes : un vidage après plantage peut donc la contenir (voir « Vie privée »).
