// Page Clavier : carte de chaleur AZERTY du portable, pavé numérique compris (/api/keyboard).

import { getJson } from "./api.js";
import { chartCard } from "./chart-card.js";
import { heatmap } from "./keyboard-heatmap.js";
import { measures, prepare } from "./keyboard-measures.js";
import { el, showEmptyPeriod } from "./states.js";

const unfiltered =
  "Les appuis suivent le filtre de clavier ; le filtre d'application ou de nature ne s'y applique pas.";

/** Carte de chaleur des appuis. */
const heatCard = (data) =>
  chartCard({
    title: "Carte de chaleur AZERTY — clavier du portable",
    note: measures.appuis.help,
    body: heatmap(data, "appuis"),
    wide: true,
    className: "keyboard-card",
    footnote: data.dayTablesUnfiltered ? unfiltered : undefined,
  }).section;

export async function renderKeyboard(view, ctx) {
  const raw = await getJson(`/api/keyboard${ctx.query}`);
  if (!ctx.isCurrent()) return;
  if (!raw.presses) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const grid = el("div", "chart-grid");
  grid.append(heatCard(prepare(raw)));
  view.replaceChildren(grid);
}
