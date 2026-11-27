// Carte « Durées d'appui » de la page Rythme : répartition, médiane et taux de chevauchement.

import { binsOption, span, trimBins } from "./bins.js";
import { nf, pct, shortDelay as ms } from "./format.js";
import { el } from "./states.js";

const presses = (n) => `${nf.format(n)} appui${n > 1 ? "s" : ""}`;

// Schéma fixe, sans donnée : la touche B descend avant que la touche A remonte.
const OVERLAP_SVG = `<svg viewBox="0 0 440 74" width="100%" role="img" aria-label="Schéma : la touche B est enfoncée avant le relâchement de la touche A">
<rect class="rhythm-key" x="40" y="8" width="200" height="20" rx="4"/><text x="48" y="22">touche A</text>
<rect class="rhythm-key" x="180" y="36" width="190" height="20" rx="4"/><text x="248" y="50">touche B</text>
<rect class="rhythm-overlap" x="180" y="4" width="60" height="56" rx="4"/>
<text class="rhythm-overlap-label" x="210" y="72" text-anchor="middle">chevauchement</text>
<path class="rhythm-axis" d="M0 64H440"/></svg>`;

/** Titre et chiffre d'une sous-partie : « Médiane 95 ms ». */
export function figure(label, value) {
  const box = el("div", "rhythm-figure");
  box.append(el("span", null, label), el("strong", null, value));
  return box;
}

function overlap(charPresses, overlaps) {
  const box = el("div", "rhythm-overlap-box");
  const head = el("div", "rhythm-overlap-head");
  head.append(
    el("h3", null, "Taux de chevauchement"),
    el("strong", null, charPresses ? pct(overlaps / charPresses) : "—"),
  );
  const schema = el("div", "rhythm-schema");
  schema.innerHTML = OVERLAP_SVG;
  box.append(
    el("div", "typos-rule"),
    head,
    el(
      "p",
      null,
      "Part des appuis où la touche suivante est enfoncée avant que la précédente soit relâchée.",
    ),
    schema,
  );
  return box;
}

export function holdCard(hold, charPresses, overlaps) {
  const median = hold.medianMs == null ? "—" : ms(hold.medianMs);
  const card = {
    title: "Durées d'appui",
    note: "Temps entre l'enfoncement et le relâchement.",
    className: "rhythm-hold",
    aside: figure("Médiane", median),
    after: overlap(charPresses, overlaps),
  };
  const bins = trimBins(hold.bins);
  if (!bins.length)
    return {
      ...card,
      empty: "Aucune durée d'appui mesurée sur cette période.",
    };
  return {
    ...card,
    label: `Répartition des durées d'appui, médiane ${median}`,
    headers: ["Durée d'appui", "Appuis"],
    rows: bins.map((b) => [span(b), nf.format(b.count)]),
    option: binsOption(bins, {
      count: presses,
      axis: "durée d'appui (ms) →",
      color: "--chart",
      // L'axe est légendé en ms : pas de « 1,2 s » au-delà d'une seconde.
      tick: (v) => nf.format(Math.round(v)),
    }),
  };
}
