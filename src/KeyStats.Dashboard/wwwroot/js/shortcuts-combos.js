// Carte « Touches enfoncées ensemble » de la page Raccourcis : touches tenues par appui et matrice
// des combinaisons de chaque modificateur avec chaque groupe de touches.

import { dayLong, nf, pct } from "./format.js";
import { el } from "./states.js";

const LEVELS = 5;

/** Teinte de 1 (rare) à LEVELS (fréquent), en échelle logarithmique ; 0 si jamais. */
function level(count, max) {
  if (!count) return 0;
  if (max <= 1) return LEVELS;
  return 1 + Math.round(((LEVELS - 1) * Math.log(count)) / Math.log(max));
}

const lastUse = (iso) => dayLong.format(new Date(`${iso}T12:00:00`));

function cellText(modifier, group, c) {
  if (!c.count) return `${modifier} + ${group} : jamais`;
  const times = `${nf.format(c.count)} fois`;
  return `${modifier} + ${group} : ${times}, dernier usage le ${lastUse(c.lastDay)}`;
}

/** Nom affiché d'une ligne : Ctrl + Alt réunit aussi AltGr, que Windows envoie ainsi. */
const rowName = (m) => (m === "Ctrl + Alt" ? "Ctrl + Alt, AltGr" : m);

function matrix(groups, combos) {
  const max = Math.max(
    1,
    ...combos.flatMap((r) => r.cells.map((c) => c.count)),
  );
  const head = el("tr");
  head.append(el("td"), ...groups.map((g) => el("th", null, g)));
  head.cells[0].setAttribute("aria-hidden", "true");
  const thead = el("thead");
  thead.append(head);
  const body = el("tbody");
  for (const row of combos) {
    const tr = el("tr");
    const th = el("th", null, rowName(row.modifier));
    th.scope = "row";
    tr.append(th);
    row.cells.forEach((c, i) => {
      const text = cellText(rowName(row.modifier), groups[i], c);
      const td = el("td");
      const tile = el("span", `shortcuts-tile l${level(c.count, max)}`);
      tile.title = text;
      td.append(tile, el("span", "sr-only", text));
      tr.append(td);
    });
    body.append(tr);
  }
  const table = el("table", "shortcuts-matrix");
  table.setAttribute("aria-label", "Combinaisons de touches");
  table.append(thead, body);
  const scroll = el("div", "shortcuts-scroll");
  scroll.append(table);
  return scroll;
}

function heatLegend() {
  const box = el("div", "shortcuts-heat-legend");
  const ramp = el("span", "shortcuts-ramp");
  ramp.setAttribute("aria-hidden", "true");
  for (let i = 1; i <= LEVELS; i++) ramp.append(el("span", `l${i}`));
  box.append(el("span", null, "rare"), ramp, el("span", null, "fréquent"));
  return box;
}

/** Part des appuis faits avec 1, 2, 3, puis 4 touches ou plus tenues. */
function chordShares(chords) {
  const sum = chords.reduce((s, c) => s + c.presses, 0);
  const buckets = [1, 2, 3, 4].map((keys) =>
    chords
      .filter((c) => (keys < 4 ? c.keys === keys : c.keys >= 4))
      .reduce((s, c) => s + c.presses, 0),
  );
  const box = el("div", "shortcuts-chords");
  box.append(el("h3", null, "Touches tenues à chaque appui"));
  const list = el("dl");
  buckets.forEach((n, i) => {
    const label = i === 3 ? "4 ou plus" : `${i + 1} touche${i ? "s" : ""}`;
    const item = el("div");
    item.append(
      el("dt", null, label),
      el("dd", null, sum ? pct(n / sum) : "—"),
      el("dd", "shortcuts-chord-count", `${nf.format(n)} appuis`),
    );
    list.append(item);
  });
  box.append(list);
  return box;
}

export function combosCard(groups, combos, chords) {
  const body = el("div", "shortcuts-combos");
  body.append(
    matrix(groups, combos),
    el(
      "p",
      "shortcuts-help",
      "Une case grise : combinaison jamais observée sur la période. Survol d'une case : nombre de fois et dernier usage.",
    ),
  );
  if (chords.length) body.append(chordShares(chords));
  return {
    title: "Touches enfoncées ensemble",
    note: "Pour chaque touche tenue (ligne), avec quel groupe de touches elle a été combinée (colonne).",
    wide: true,
    aside: heatLegend(),
    body,
  };
}
