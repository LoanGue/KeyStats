// Cartes de la page Fautes sur le coût des corrections et la manière d'effacer.

import { nf, nf1, pct } from "./format.js";
import { swatch } from "./chart-card.js";
import { el } from "./states.js";
import { stack } from "./typos-correction.js";

const nf2 = new Intl.NumberFormat("fr-FR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

function tile(label, value, caption) {
  const box = el("div", "typos-tile");
  box.append(
    el("span", "typos-tile-label", label),
    el("span", "typos-tile-value", value),
    el("span", "typos-tile-caption", caption),
  );
  return box;
}

function line(label, value) {
  const row = el("div", "typos-cost-line");
  row.append(el("span", null, label), el("span", "typos-muted", value));
  return row;
}

export function costCard(cost) {
  const card = {
    title: "Coût des corrections",
    note: "Ce que chaque faute vous fait retaper.",
  };
  if (!cost.keys)
    return {
      ...card,
      empty:
        "Aucune session de texte sur cette période : le coût des corrections ne se mesure que sur le texte.",
    };
  const tiles = el("div", "typos-tiles");
  tiles.append(
    tile(
      "Effacements collatéraux",
      cost.typos ? nf1.format(cost.collateral / cost.typos) : "—",
      "caractères justes effacés par faute, pour atteindre l'erreur",
    ),
    tile(
      "KSPC",
      cost.kspc == null ? "—" : nf2.format(cost.kspc),
      "touches par caractère conservé ; 1,00 = aucune touche perdue",
    ),
  );
  const lines = el("div", "typos-cost-lines");
  lines.append(
    line("Fautes corrigées", nf.format(cost.typos)),
    line(
      "Touches perdues en corrections",
      `${nf.format(cost.lostKeys)} · ${pct(cost.lostKeys / cost.keys)} du total`,
    ),
  );
  const body = el("div", "typos-cost");
  body.append(tiles, lines);
  return { ...card, body };
}

export function deletionCard(deletion) {
  const card = {
    title: "Manière d'effacer",
    note: "Part des caractères effacés selon la méthode, sur toute la frappe, code compris.",
    wide: true,
  };
  const { presses, held, word, wordPresses } = deletion;
  const total = presses + held + word;
  if (!total) return { ...card, empty: "Aucun effacement sur cette période." };
  const perWord = wordPresses
    ? ` · au moins ${nf1.format(word / wordPresses)} caractères en moyenne`
    : "";
  const ways = [
    [
      presses,
      "Retour arrière, appui par appui",
      "un caractère à chaque appui",
      "--chart",
    ],
    [
      held,
      "Retour arrière maintenu",
      "la répétition automatique efface en rafale",
      "--chart-typing",
    ],
    [word, "Ctrl + Retour arrière", `un mot entier${perWord}`, "--chart-green"],
  ];
  const keys = el("div", "typos-ways");
  for (const [value, label, detail, color] of ways) {
    const text = el("div");
    const title = el("div", "typos-way-title", `${label} `);
    title.append(el("span", "typos-muted", pct(value / total)));
    text.append(title, el("div", "typos-muted", detail));
    const item = el("div", "typos-way");
    item.append(swatch(color), text);
    keys.append(item);
  }
  const body = el("div", "typos-deletion");
  body.append(
    stack(
      ways.map(([v, , , c]) => [v, c]),
      "thick",
    ),
    keys,
  );
  return { ...card, body };
}
