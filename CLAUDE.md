# KeyStats

Analyse continue de la frappe clavier sous Windows (AZERTY), avec un agent permanent très léger et un tableau de bord ouvert à la demande. Le plan de référence est dans l'historique du projet ; ce fichier fixe les règles qui ne bougent pas.

## Structure

| Dossier                          | Rôle                                                                                          | Cible                                                        |
| -------------------------------- | --------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| `src/KeyStats.Core`              | Algorithmes (sessions, vitesse, fautes, bigrammes), stockage SQLite, format brut, chiffrement | `net10.0`, sans dépendance Windows, compatible AOT           |
| `src/KeyStats.Agent`             | Capture Raw Input, fenêtre active, icône de notification, compteur en direct                  | `net10.0-windows`, NativeAOT, Win32 via `LibraryImport`      |
| `src/KeyStats.Dashboard`         | Fenêtre WinForms + WebView2, API JSON interne, Windows Hello                                  | `net10.0-windows10.0.19041.0`                                |
| `src/KeyStats.Dashboard/wwwroot` | Interface HTML/CSS/JS, ECharts en local                                                       | servi par `WebResourceRequested` sur `https://keystats.app/` |
| `tests/KeyStats.Core.Tests`      | Tests xUnit sur des flux d'événements synthétiques                                            | `net10.0`                                                    |
| `tools/`                         | `keystats-tools` (rejeu, recalcul), `install.ps1`, `uninstall.ps1`                            |                                                              |

## Règles impératives

1. **Jamais de texte tapé ni de titre de fenêtre en clair sur disque.** Ni dans la base, ni dans un journal, un fichier temporaire, un message d'exception ou une trace. Le brut et les titres sont chiffrés (AES-256-GCM, clé dérivée d'une époque ECDH). Les journaux de diagnostic ne contiennent que des compteurs, des codes d'erreur et des noms d'exécutables.
2. **L'agent ne détient que la clé publique.** La clé privée n'existe en clair que dans la mémoire du tableau de bord, après Windows Hello ou la phrase de secours, et elle est effacée à la fermeture.
3. **`KeyStats.Core` reste sans dépendance Windows** : pas de P/Invoke, pas de WinForms, pas de WinRT. Tout ce qui touche Win32 vit dans l'agent ou le tableau de bord, derrière une interface que Core consomme.
4. **Compatible NativeAOT** : pas de réflexion, JSON par `JsonSerializerContext` généré, pas de `dynamic`. Aucun avertissement de trimming/AOT accepté dans Core et l'agent.
5. **Chemin chaud sans allocation** : du callback Raw Input jusqu'à l'analyse, on manipule des structs dans des tampons préalloués.
6. **Aucun port réseau.** Le tableau de bord sert tout via `WebResourceRequested`. Aucune dépendance chargée depuis un CDN.
7. **Frappes injectées ignorées** (`hDevice == NULL` pour le clavier). Applis exclues (gestionnaires de mots de passe, bureau à distance) et champs de mot de passe détectés (`FocusState.Secret`) : seulement le nombre de touches, ni brut, ni `key_minute`, ni analyse de texte.
8. **Horodatages en UTC, en millisecondes.** Les statistiques par jour sont rangées dans le jour local du fuseau en vigueur au moment de la frappe (`LocalDayClock`), changements d'heure compris. L'agent appelle `LocalDayClock.SetZone` sur `WM_TIMECHANGE`, après `TimeZoneInfo.ClearCachedData()`. Après un changement de fuseau, seul le recalcul depuis le brut remet les anciens jours dans le nouveau fuseau. Migrations du schéma par `PRAGMA user_version`.

## Emplacements à l'exécution

- Binaires installés : `%LOCALAPPDATA%\Programs\KeyStats\`
- Données : `%LOCALAPPDATA%\KeyStats\` (`keystats.db`, `settings.json`, `backup\`)
- Démarrage : clé `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

## Commandes

```powershell
dotnet build KeyStats.slnx -warnaserror
dotnet format KeyStats.slnx --verify-no-changes
dotnet test --project tests/KeyStats.Core.Tests
dotnet publish src/KeyStats.Agent -c Release   # NativeAOT
```

## Méthode de travail

- `main` est en lecture seule. Une issue, une branche et une PR par étape du plan (`TODO.md`), fusionnée par rebase après revue : chaque commit arrive sur `main`, il doit donc tenir seul (message conventionnel, une seule préoccupation) et les commits de retouche sont repliés avant le premier push. Format, compilation sans avertissement et tests se vérifient en local (voir `CONTRIBUTING.md`) ; la CI (`.github/workflows/pr-gate.yml`, exigée par la règle de `main`) ne contrôle que la taille et la section `## Tests` du corps de la PR. Au-delà de 400 lignes, la PR porte le label `large-pr` et une justification.
- Les tests passent avant chaque commit.
- Interface du tableau de bord : skill `frontend-design:frontend-design`. Tester les pages dans le navigateur intégré avec des données d'exemple (`wwwroot` fonctionne aussi hors WebView2).
- Textes de l'interface en français, sans formules creuses ni chiffres inventés.
