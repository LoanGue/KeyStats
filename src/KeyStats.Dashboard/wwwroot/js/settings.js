// Page Réglages : seuils de détection et applications exclues (/api/settings).

import { getJson } from "./api.js";
import { appsCard } from "./settings-apps.js";
import { refusedAlert } from "./settings-card.js";
import { thresholdsCard } from "./settings-thresholds.js";
import { el } from "./states.js";

export async function renderSettings(view, ctx) {
  const settings = await getJson("/api/settings");
  if (!ctx.isCurrent()) return;
  // Un fichier refusé donne la vue par défaut, pas ses valeurs : aucune carte ne l'affiche.
  if (!settings.readable) {
    view.replaceChildren(refusedAlert());
    return;
  }
  const grid = el("div", "settings-grid");
  grid.append(thresholdsCard(settings), appsCard(settings));
  view.replaceChildren(grid);
}
