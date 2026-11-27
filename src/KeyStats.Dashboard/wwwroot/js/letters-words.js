// Cartes « Lettres par mot » et « Longueurs de mots » de la page Lettres et mots.

import { legend } from "./chart-card.js";
import { token } from "./charts.js";
import { nf, nf1, pct } from "./format.js";
import { comparisonOption } from "./letters-frequency.js";
import { el } from "./states.js";

const average = (v) => (v == null ? "—" : nf1.format(v));

function line(label, value) {
  const row = el("div", "letters-line");
  row.append(el("span", null, label), el("span", "letters-muted", value));
  return row;
}

export function wordsCard(words) {
  const card = {
    title: "Lettres par mot",
    note: "Longueur moyenne de vos mots.",
  };
  if (!words.count)
    return { ...card, empty: "Aucun mot complet sur cette période." };
  const body = el("div", "letters-words");
  const figure = el("div", "letters-figure");
  figure.append(
    el("span", "letters-value", average(words.average)),
    el("span", "letters-muted", "lettres par mot"),
  );
  const lines = el("div", "letters-lines");
  lines.append(
    line("Français de référence", average(words.french)),
    line("Médiane", words.median == null ? "—" : nf.format(words.median)),
    line("En texte", average(words.prose)),
    line("En code", average(words.code)),
  );
  body.append(
    figure,
    lines,
    el(
      "p",
      "letters-definition",
      "Un mot = une suite de lettres entre deux espaces ou signes de ponctuation.",
    ),
  );
  return { ...card, body };
}

const lengthLabel = (l, last) =>
  l.length === last ? `${l.length}+` : String(l.length);

export function lengthsCard(lengths) {
  const card = {
    title: "Longueurs de mots",
    note: "Part des mots selon leur nombre de lettres.",
    className: "letters-lengths",
  };
  const total = lengths.reduce((s, l) => s + l.count, 0);
  if (!total) return { ...card, empty: "Aucun mot complet sur cette période." };
  card.aside = legend([
    ["Vous", null, "--chart"],
    ["Français", "line", "--text"],
  ]);
  const last = lengths.at(-1).length;
  const categories = lengths.map((l) => lengthLabel(l, last));
  const mine = lengths.map((l) => l.count / total);
  const french = lengths.map((l) => l.french);
  const option = comparisonOption(categories, mine, [
    { name: "Français", values: french, color: token("--text") },
  ]);
  option.grid.bottom = 28;
  option.graphic = [
    {
      type: "text",
      right: 0,
      bottom: 0,
      style: {
        text: "lettres par mot →",
        fill: token("--muted"),
        fontSize: 12,
      },
    },
  ];
  return {
    ...card,
    label:
      "Répartition de vos mots selon leur nombre de lettres, comparée au français",
    headers: ["Lettres", "Vous", "Français"],
    rows: lengths.map((l, i) => [categories[i], pct(mine[i]), pct(french[i])]),
    option,
  };
}
