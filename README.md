# KeyStats

Mesure ta frappe réelle sous Windows : vitesse pendant les vraies sessions d'écriture, fautes de frappe corrigées, rythme par paire de lettres, raccourcis, touches les plus utilisées. Un agent léger tourne en permanence ; une fenêtre de statistiques s'ouvre à la demande.

> Projet en cours de construction. L'installation et le mode d'emploi seront décrits ici à la fin du développement.

## Lancer l'agent (développement)

```powershell
dotnet publish src/KeyStats.Agent -c Release -o publish\agent
publish\agent\KeyStats.Agent.exe --db $env:TEMP\keystats-test.db
```

Sans `--db`, l'agent écrit dans `%LOCALAPPDATA%\KeyStats\keystats.db`. Il tourne jusqu'à la fin de la session Windows : verrouillage et veille suspendent l'enregistrement, qui reprend au déverrouillage. `--probe <secondes> [--db <chemin>]` capture pendant la durée donnée puis affiche des compteurs, sans aucun contenu.

## Vie privée

- Tout reste sur ton PC. Aucun envoi réseau, aucun port ouvert.
- Le texte tapé et les titres de fenêtres ne sont jamais écrits en clair : ils sont chiffrés avec une clé publique, et seule ta session Windows Hello (ou ta phrase de secours) permet de les relire.
- Les gestionnaires de mots de passe et le bureau à distance sont exclus par défaut.
- Un clic sur l'icône de notification met l'enregistrement en pause 5 minutes, chaque clic suivant ajoute 5 minutes.
