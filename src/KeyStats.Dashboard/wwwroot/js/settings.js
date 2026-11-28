// Page Réglages : seuils de détection, compteur à l'écran, applications exclues (/api/settings),
// chiffrement (/api/key), dossier de sauvegarde (/api/backup) et export CSV (/api/export).

import { getJson } from "./api.js";
import { appsCard } from "./settings-apps.js";
import { backupCard } from "./settings-backup.js";
import { exportCard } from "./settings-export.js";
import { refusedAlert } from "./settings-card.js";
import { keyCard } from "./settings-key.js";
import { liveCard } from "./settings-live.js";
import { thresholdsCard } from "./settings-thresholds.js";
import { el } from "./states.js";

export async function renderSettings(view, ctx) {
  const [settings, backup] = await Promise.all([
    getJson("/api/settings"),
    // Sans réponse, la carte de sauvegarde le dit ; les autres cartes restent utilisables.
    getJson("/api/backup").catch(() => null),
  ]);
  if (!ctx.isCurrent()) return;
  // Un fichier refusé donne la vue par défaut, pas ses valeurs : aucune carte ne l'affiche.
  if (!settings.readable) {
    view.replaceChildren(refusedAlert());
    return;
  }
  const grid = el("div", "settings-grid");
  grid.append(
    thresholdsCard(settings),
    liveCard(settings),
    appsCard(settings),
    keyCard(ctx),
    backupCard(backup, ctx.retry),
    exportCard(ctx.query),
  );
  view.replaceChildren(grid);
}
