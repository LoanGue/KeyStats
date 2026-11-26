# KeyStats

Mesure ta frappe réelle sous Windows : vitesse pendant les vraies sessions d'écriture, fautes de frappe corrigées, rythme par paire de lettres, raccourcis, touches les plus utilisées. Un agent léger tourne en permanence ; une fenêtre de statistiques s'ouvre à la demande.

> Projet en cours de construction. L'installation et le mode d'emploi seront décrits ici à la fin du développement.

## Lancer l'agent (développement)

```powershell
dotnet publish src/KeyStats.Agent -c Release -o publish\agent
publish\agent\KeyStats.Agent.exe --db $env:TEMP\keystats-test.db
```

Sans `--db`, l'agent écrit dans `%LOCALAPPDATA%\KeyStats\keystats.db`. Il tourne jusqu'à la fin de la session Windows : verrouillage et veille suspendent l'enregistrement, qui reprend au déverrouillage. `--probe <secondes> [--db <chemin>]` capture pendant la durée donnée puis affiche des compteurs, sans aucun contenu.

## Compteur en direct

Quand une vraie frappe commence (au moins 12 caractères et 2 espaces en 6 secondes), un petit compteur apparaît en bas à droite de l'écran où se trouve la fenêtre active, au-dessus de la barre des tâches : vitesse en mots par minute, et en caractères par minute en dessous. Après 4 secondes sans frappe, il affiche le bilan de la rafale (vitesse et précision) pendant 3 secondes, puis disparaît. Il laisse passer les clics, ne prend jamais le focus et n'apparaît ni dans Alt+Tab ni sur les captures et partages d'écran. Il reste caché quand une application occupe tout l'écran (jeu, vidéo) et en mode présentation. Une pause le cache aussitôt. `liveCounter.enabled` à `false` dans `settings.json` le désactive.

## Icône de notification

- Clic gauche : pause de 5 minutes, et 5 minutes de plus à chaque clic. L'icône passe en gris avec un badge pause ; l'infobulle donne l'heure de reprise. Le compteur confirme chaque clic pendant 1,5 seconde (« ⏸ 15 min ») ; la commande `snooze` du tube ne l'affiche pas.
- Clic droit : pause jusqu'à reprise manuelle, reprise immédiate, quitter.
- Pendant une pause, rien n'est enregistré, ni compteurs ni brut.

Windows 11 range les nouvelles icônes dans le menu déroulant de la zone de notification. Pour garder KeyStats visible : Paramètres › Personnalisation › Barre des tâches › Autres icônes de la barre d'état système, puis activer KeyStats.

## Commandes de l'agent

Un seul agent tourne par base et par session Windows : un second lancement s'arrête aussitôt. L'agent écoute un tube nommé `\\.\pipe\KeyStats.Agent.<session>.<clé>`, réservé à l'utilisateur courant, où la clé dérive du chemin de la base. Chaque connexion porte une commande en ASCII suivie d'un saut de ligne, et reçoit une ligne en réponse (`ok`, `unknown` ou `failed`) :

| Commande | Effet |
| --- | --- |
| `snooze` | 5 minutes de pause de plus, comme un clic sur l'icône |
| `pause` | pause jusqu'à reprise manuelle |
| `resume` | fin de la pause par l'icône |
| `flush` | écrit tout ce qui est en attente avant de répondre ; `failed` si l'écriture échoue |
| `reload` | relit `settings.json` : applis exclues et dossier de sauvegarde appliqués tout de suite, seuils d'analyse au prochain démarrage ; `failed` si le fichier est refusé ou si l'agent ne peut pas les transmettre à la capture, les réglages en place sont alors gardés |
| `quit` | arrête l'agent, comme « Quitter KeyStats » |

Aucun texte ni titre de fenêtre ne passe par ce tube.

## Tableau de bord (développement)

```powershell
dotnet run --project src/KeyStats.Dashboard -- --db $env:TEMP\keystats-test.db
```

Sans `--db`, il ouvre `%LOCALAPPDATA%\KeyStats\keystats.db`. La fenêtre affiche une page WebView2 servie en mémoire sous `https://keystats.app/` : les fichiers de `wwwroot` et les routes `/api/…` passent par l'interception des requêtes, sans serveur ni port ouvert. Toute autre adresse est refusée. `/api/status` indique si le chiffrement est configuré et si un agent tourne sur la même base. Le profil WebView2 est rangé dans `webview\`, à côté de la base.

Une fois le chiffrement configuré, la page propose de déverrouiller les données chiffrées par Windows Hello ou par la phrase de secours. La clé privée reste alors dans la mémoire du tableau de bord seulement, jusqu'au bouton « Verrouiller » ou à la fermeture de la fenêtre. Le corps de la requête qui porte la phrase est effacé après usage, mais les chaînes JavaScript et .NET restent en mémoire jusqu'au passage du ramasse-miettes : un vidage après plantage peut donc la contenir (voir « Vie privée » pour ce qui en est fait).

## Vie privée

- Les données restent sur la machine locale. Aucun envoi réseau, aucun port ouvert.
- Le texte tapé et les titres de fenêtres ne sont jamais écrits en clair : ils sont chiffrés avec une clé publique, et seuls Windows Hello ou la phrase de secours permettent de les relire.
- Les rapports de plantage du moteur de la page ne sont pas envoyés à Microsoft. Un vidage mémoire peut contenir la phrase saisie ou du texte déchiffré : le tableau de bord le supprime dans les secondes qui suivent le plantage, et à chaque ouverture s'il n'a pas pu le faire. Reste hors de portée un vidage du tableau de bord lui-même par le rapport d'erreurs de Windows.
- Les gestionnaires de mots de passe et le bureau à distance sont exclus par défaut.
- Un clic sur l'icône de notification met l'enregistrement en pause 5 minutes, chaque clic suivant ajoute 5 minutes.
