# KeyStats

Mesure ta frappe réelle sous Windows : vitesse pendant les vraies sessions d'écriture, fautes de frappe corrigées, rythme par paire de lettres, raccourcis, touches les plus utilisées. Un agent léger tourne en permanence ; une fenêtre de statistiques s'ouvre à la demande.

> Projet en cours de construction. L'installation et le mode d'emploi seront décrits ici à la fin du développement.

## Lancer l'agent (développement)

```powershell
dotnet publish src/KeyStats.Agent -c Release -o publish\agent
publish\agent\KeyStats.Agent.exe --db $env:TEMP\keystats-test.db
```

Sans `--db`, l'agent écrit dans `%LOCALAPPDATA%\KeyStats\keystats.db`. Il tourne jusqu'à la fin de la session Windows : verrouillage et veille suspendent l'enregistrement, qui reprend au déverrouillage. `--probe <secondes> [--db <chemin>]` capture pendant la durée donnée puis affiche des compteurs, sans aucun contenu.

## Icône de notification

- Clic gauche : pause de 5 minutes, et 5 minutes de plus à chaque clic. L'icône passe en gris avec un badge pause ; l'infobulle donne l'heure de reprise.
- Clic droit : pause jusqu'à reprise manuelle, reprise immédiate, quitter.
- Pendant une pause, rien n'est enregistré, ni compteurs ni brut.

Windows 11 range les nouvelles icônes dans le menu déroulant de la zone de notification. Pour garder KeyStats visible : Paramètres › Personnalisation › Barre des tâches › Autres icônes de la barre d'état système, puis activer KeyStats.

## Vie privée

- Tout reste sur ton PC. Aucun envoi réseau, aucun port ouvert.
- Le texte tapé et les titres de fenêtres ne sont jamais écrits en clair : ils sont chiffrés avec une clé publique, et seule ta session Windows Hello (ou ta phrase de secours) permet de les relire.
- Les gestionnaires de mots de passe et le bureau à distance sont exclus par défaut.
- Un clic sur l'icône de notification met l'enregistrement en pause 5 minutes, chaque clic suivant ajoute 5 minutes.
