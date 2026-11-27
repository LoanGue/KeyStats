// Page Raccourcis : raccourcis les plus utilisés, chevauchements et touches tenues ensemble (/api/shortcuts).

import { getJson } from "./api.js";
import { chartCard, drawCharts } from "./chart-card.js";
import { nf } from "./format.js";
import { keycap, overlapCard } from "./shortcuts-overlap.js";
import { el, showEmptyPeriod } from "./states.js";
import { unfilteredKeys } from "./typos.js";

/** Rien à montrer : ni appui compté, ni raccourci, ni chevauchement. */
const isEmpty = (data) =>
  !data.chords.length &&
  !data.top.length &&
  !data.overlap.rolls &&
  !data.overlap.shiftHeld &&
  !data.overlap.shortcuts;

function shortcut(keys) {
  const box = el("span", "shortcuts-keys");
  keys.forEach((k, i) => {
    if (i) box.append(el("span", "shortcuts-plus", "+"));
    box.append(keycap(k));
  });
  return box;
}

function topRow(s, max) {
  const bar = el("span", "shortcuts-bar");
  bar.setAttribute("aria-hidden", "true");
  bar.style.width = `${(100 * s.count) / max}%`;
  const cells = [shortcut(s.keys), bar, nf.format(s.count)];
  const tr = el("tr");
  for (const cell of cells) {
    const td = el("td");
    td.append(cell);
    tr.append(td);
  }
  // La barre redit le nombre : la colonne est masquée aux lecteurs d'écran.
  tr.cells[1].setAttribute("aria-hidden", "true");
  // Lu « Ctrl plus C » plutôt que touche par touche.
  tr.cells[0].firstChild.setAttribute("aria-hidden", "true");
  tr.cells[0].append(el("span", "sr-only", s.keys.join(" plus ")));
  return tr;
}

function topCard(top) {
  const card = {
    title: "Raccourcis les plus utilisés",
    note: "Combinaisons avec Ctrl, Alt, Win ou AltGr, hors caractères spéciaux.",
  };
  if (!top.length)
    return { ...card, empty: "Aucun raccourci sur cette période." };
  const head = el("tr");
  head.append(
    ...["Raccourci", "Utilisation", "Fois"].map((h) => el("th", null, h)),
  );
  head.cells[1].setAttribute("aria-hidden", "true");
  const thead = el("thead");
  thead.append(head);
  const body = el("tbody");
  body.append(...top.map((s) => topRow(s, top[0].count)));
  const table = el("table", "shortcuts-top");
  table.setAttribute("aria-label", "Raccourcis");
  table.append(thead, body);
  return { ...card, body: table };
}

export async function renderShortcuts(view, ctx) {
  const data = await getJson(`/api/shortcuts${ctx.query}`);
  if (!ctx.isCurrent()) return;
  if (isEmpty(data)) {
    showEmptyPeriod(view, ctx);
    return;
  }
  // Toute la page vient des tables par jour, qui ignorent l'application, la nature et le clavier.
  const footnote = data.dayTablesUnfiltered ? unfilteredKeys : undefined;
  const cards = [
    topCard(data.top),
    overlapCard(data.overlap, data.durations, data.pairs),
  ].map((c) => chartCard({ ...c, footnote }));
  const grid = el("div", "chart-grid");
  grid.append(...cards.map((c) => c.section));
  view.replaceChildren(grid);
  await drawCharts(cards, ctx.isCurrent);
}
