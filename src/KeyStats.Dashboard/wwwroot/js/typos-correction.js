// Carte « Types de fautes » de la page Fautes, avec le délai de détection ; barres empilées partagées.

import { binsOption, span, trimBins } from "./bins.js";
import { swatch } from "./chart-card.js";
import { nf, pct, shortDelay as ms } from "./format.js";
import { el } from "./states.js";

/** Somme des compteurs d'un objet ({ substitutions: 3, … }). */
export const total = (counts) =>
  Object.values(counts).reduce((a, b) => a + b, 0);

/** Barre empilée décorative : [valeur, jeton CSS], les parts nulles en moins. */
export function stack(parts, className) {
  const bar = el("div", `typos-stack ${className}`);
  bar.setAttribute("aria-hidden", "true");
  for (const [value, color] of parts.filter(([v]) => v > 0)) {
    const part = el("span");
    part.style.flex = String(value);
    part.style.background = `var(${color})`;
    bar.append(part);
  }
  return bar;
}

const faults = (n) => `${nf.format(n)} faute${n > 1 ? "s" : ""}`;

const TYPES = [
  ["substitutions", "Substitution", "une touche pour une autre", "--chart"],
  ["omissions", "Oubli", "lettre manquante", "--chart-typing"],
  ["insertions", "Ajout", "lettre en trop", "--chart-green"],
  ["transpositions", "Inversion", "deux lettres permutées", "--chart-amber"],
];

export function typesCard(types, delay) {
  const card = {
    title: "Types de fautes",
    note: "Déduits de ce que vous effacez puis retapez.",
    className: "typos-delay",
  };
  const sum = total(types);
  if (!sum)
    return { ...card, empty: "Aucune faute corrigée sur cette période." };
  const body = el("div", "typos-types");
  const keys = el("div", "typos-types-legend");
  for (const [key, label, detail, color] of TYPES) {
    const item = el("div", "typos-legend-item");
    const text = el("span", "typos-legend-text", label);
    text.append(el("span", "typos-muted", ` — ${detail}`));
    item.append(
      swatch(color),
      text,
      el("span", "typos-muted", pct(types[key] / sum)),
    );
    keys.append(item);
  }
  const head = el("div", "typos-delay-head");
  const median = el("span", "typos-muted", "médiane ");
  median.append(
    el("strong", null, delay.medianMs == null ? "—" : ms(delay.medianMs)),
  );
  head.append(el("h3", null, "Délai de détection"), median);
  body.append(
    stack(
      TYPES.map(([key, , , color]) => [types[key], color]),
      "thin",
    ),
    keys,
    el("div", "typos-rule"),
    head,
  );
  const bins = trimBins(delay.bins);
  if (!bins.length) return { ...card, body };
  return {
    ...card,
    body,
    label: `Répartition du délai entre la faute et le premier retour arrière, médiane ${ms(delay.medianMs)}`,
    headers: ["Délai", "Fautes"],
    rows: bins.map((b) => [span(b), nf.format(b.count)]),
    option: binsOption(bins, {
      count: faults,
      axis: "temps entre la faute et le retour arrière →",
    }),
  };
}
