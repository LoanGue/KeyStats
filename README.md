# KeyStats

Mesure de la frappe réelle sous Windows : vitesse pendant les vraies sessions d'écriture, fautes de frappe corrigées, rythme par paire de lettres, raccourcis, touches les plus utilisées. Un agent léger tourne en permanence ; une fenêtre de statistiques s'ouvre à la demande.

> Projet en cours de construction. L'installation et le mode d'emploi seront décrits ici à la fin du développement.

## Vie privée

- Les données restent sur la machine locale. Aucun envoi réseau, aucun port ouvert.
- Le texte tapé et les titres de fenêtres ne sont jamais écrits en clair : ils sont chiffrés avec une clé publique, et seule la session Windows Hello (ou la phrase de secours) permet de les relire.
- Les gestionnaires de mots de passe et le bureau à distance sont exclus par défaut.
- Un clic sur l'icône de notification met l'enregistrement en pause 5 minutes, chaque clic suivant ajoute 5 minutes.
