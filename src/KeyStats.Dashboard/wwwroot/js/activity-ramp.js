// Rampe à cinq niveaux commune au calendrier et à la grille horaire de la page Activité.

import { el } from "./states.js";

/** Niveau 0 à 4 d'une valeur : 0 sans écriture, puis par quartile des valeurs non nulles. */
export function levels(values) {
  const sorted = values.filter((v) => v > 0).sort((a, b) => a - b);
  const cut = [0.25, 0.5, 0.75].map(
    (q) => sorted[Math.floor(q * (sorted.length - 1))],
  );
  return (v) => (v > 0 ? 1 + cut.filter((c) => v > c).length : 0);
}

/** Case décorative d'une rampe ; titre lisible au survol. */
export function cell(level, title) {
  const node = el("span", `activity-cell level-${level}`);
  if (title) node.title = title;
  return node;
}

/** Légende « moins … plus » de la rampe. */
export function rampLegend() {
  const box = el("div", "activity-ramp");
  box.append(el("span", null, "moins"));
  for (let l = 0; l < 5; l++) {
    const swatch = cell(l);
    swatch.setAttribute("aria-hidden", "true");
    box.append(swatch);
  }
  box.append(el("span", null, "plus"));
  return box;
}
