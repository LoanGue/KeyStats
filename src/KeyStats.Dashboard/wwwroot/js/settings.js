// Page Réglages : seuils de détection et applications exclues (/api/settings).

import { getJson } from "./api.js";
import { appsCard } from "./settings-apps.js";
import { thresholdsCard } from "./settings-thresholds.js";
import { el } from "./states.js";

function unreadableAlert() {
  const box = el("div", "settings-alert");
  box.setAttribute("role", "alert");
  box.append(
    el(
      "p",
      null,
      "Le fichier settings.json est refusé : une valeur y est hors bornes ou mal écrite. L'agent garde ses réglages par défaut, et cette page ne réécrit pas le fichier. Corrigez-le ou supprimez-le, puis rouvrez la page.",
    ),
  );
  return box;
}

export async function renderSettings(view, ctx) {
  const settings = await getJson("/api/settings");
  if (!ctx.isCurrent()) return;
  const locked = !settings.readable;
  const grid = el("div", "settings-grid");
  grid.append(thresholdsCard(settings, locked), appsCard(settings, locked));
  view.replaceChildren(...(locked ? [unreadableAlert()] : []), grid);
}
