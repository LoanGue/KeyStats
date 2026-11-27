// Carte « Chevauchement » de la page Raccourcis : nature, durées et paires qui se chevauchent le plus.

import { binsOption, span, trimBins } from "./bins.js";
import { legend } from "./chart-card.js";
import { nf, pct, shortDelay as ms } from "./format.js";
import { el } from "./states.js";
import { stack } from "./typos-correction.js";

const overlaps = (n) => `${nf.format(n)} chevauchement${n > 1 ? "s" : ""}`;

/** Touche affichée comme une touche de clavier. */
export const keycap = (name) => el("kbd", "shortcuts-key", name);

const KINDS = [
  ["rolls", "Roulement entre deux lettres", "--chart"],
  ["shiftHeld", "Maj tenue", "--chart-typing"],
  ["shortcuts", "Raccourci", "--chart-green"],
];

function split(overlap) {
  const sum = KINDS.reduce((s, [k]) => s + overlap[k], 0);
  const box = el("div", "shortcuts-split");
  box.append(
    stack(
      KINDS.map(([k, , color]) => [overlap[k], color]),
      "thin",
    ),
    legend(
      KINDS.map(([k, label, color]) => [
        `${label} ${pct(overlap[k] / sum)}`,
        "square",
        color,
      ]),
    ),
  );
  return box;
}

function pairRow(p, max) {
  const row = el("li", "shortcuts-pair");
  const keys = el("span", "shortcuts-keys");
  keys.append(keycap(p.first), keycap(p.second));
  const bar = el("span", "shortcuts-bar light");
  bar.setAttribute("aria-hidden", "true");
  bar.style.width = `${(100 * p.count) / max}%`;
  keys.setAttribute("aria-hidden", "true");
  // Comme la maquette : la barre porte le nombre, le texte la médiane ; tout est lu en clair.
  const said = `${p.first} puis ${p.second} : ${overlaps(p.count)}, médiane`;
  const median = el("span", "shortcuts-pair-median");
  median.append(el("span", "sr-only", said), ms(p.medianMs));
  row.title = `${overlaps(p.count)}, médiane ${ms(p.medianMs)}`;
  row.append(keys, bar, median);
  return row;
}

function pairs(list) {
  const box = el("div", "shortcuts-pairs");
  box.append(el("h3", null, "Paires qui se chevauchent le plus"));
  if (!list.length) {
    box.append(
      el(
        "p",
        "shortcuts-none",
        "Aucune paire de lettres ne se chevauche au moins cinq fois sur cette période.",
      ),
    );
    return box;
  }
  const ul = el("ul");
  ul.append(...list.map((p) => pairRow(p, list[0].count)));
  box.append(ul);
  return box;
}

/** Chiffre en haut à droite : « Médiane 35 ms ». */
function figure(label, value) {
  const box = el("div", "rhythm-figure");
  box.append(el("span", null, label), el("strong", null, value));
  return box;
}

export function overlapCard(overlap, durations, pairList) {
  const median = durations.medianMs == null ? "—" : ms(durations.medianMs);
  const total = KINDS.reduce((s, [k]) => s + overlap[k], 0);
  const card = {
    title: "Chevauchement",
    note: "Temps pendant lequel deux touches sont enfoncées en même temps.",
    className: "shortcuts-overlap",
    aside: figure("Médiane", median),
  };
  if (!total)
    return {
      ...card,
      empty:
        "Aucune touche tenue pendant l'appui d'une autre sur cette période.",
    };
  const bins = trimBins(durations.bins);
  const after = pairs(pairList);
  if (!bins.length) {
    // Durées mesurées depuis peu : les jours analysés avant n'en ont pas.
    const box = el("div");
    box.append(
      el(
        "p",
        "chart chart-missing",
        "Aucune durée de chevauchement mesurée sur cette période. Un recalcul depuis le brut l'ajoute aux jours analysés avant sa mise en place.",
      ),
      after,
    );
    return { ...card, body: split(overlap), after: box };
  }
  return {
    ...card,
    body: split(overlap),
    label: `Répartition des durées de chevauchement entre deux lettres, médiane ${median}`,
    headers: ["Durée du chevauchement", "Chevauchements"],
    rows: bins.map((b) => [span(b), nf.format(b.count)]),
    option: binsOption(bins, {
      count: overlaps,
      axis: "durée du chevauchement (ms) →",
      color: "--chart",
      tick: (v) => nf.format(Math.round(v)),
    }),
    after,
  };
}
