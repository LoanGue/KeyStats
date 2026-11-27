// Page Clavier : carte de chaleur AZERTY du portable, pavé numérique compris (/api/keyboard).

import { getJson } from "./api.js";
import { heatmap } from "./keyboard-heatmap.js";
import { measures, prepare } from "./keyboard-measures.js";
import { el, showEmptyPeriod } from "./states.js";

const unfiltered =
  "Les appuis suivent le filtre de clavier ; le filtre d'application ou de nature ne s'y applique pas.";

/** Carte de chaleur des appuis. */
function heatCard(data) {
  const m = measures.appuis;
  const section = el("section", "card chart-card keyboard-card wide");
  const head = el("div", "chart-head");
  const text = el("div");
  text.append(
    el("h2", null, "Carte de chaleur AZERTY — clavier du portable"),
    el("p", null, m.help),
  );
  head.append(text);
  section.append(head, heatmap(data, "appuis"));
  if (data.dayTablesUnfiltered)
    section.append(el("p", "chart-footnote", unfiltered));
  return section;
}

export async function renderKeyboard(view, ctx) {
  const raw = await getJson(`/api/keyboard${ctx.query}`);
  if (!ctx.isCurrent()) return;
  if (!raw.presses) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const data = prepare(raw);
  const grid = el("div", "chart-grid");
  grid.append(heatCard(data));
  view.replaceChildren(grid);
}
