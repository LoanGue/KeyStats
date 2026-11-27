// Page Rythme : enchaînements qui ralentissent, durées d'appui, doigté AZERTY indicatif (/api/rhythm).

import { getJson } from "./api.js";
import { chartCard, drawCharts } from "./chart-card.js";
import { bigramsCard } from "./rhythm-bigrams.js";
import { el, showEmptyPeriod } from "./states.js";
import { unfilteredKeys } from "./typos.js";

/** Rien à montrer : ni appui de caractère, ni enchaînement, ni durée d'appui. */
const isEmpty = (data) =>
  !data.charPresses && !data.bigrams.length && !data.hold.bins.length;

export async function renderRhythm(view, ctx) {
  const data = await getJson(`/api/rhythm${ctx.query}`);
  if (!ctx.isCurrent()) return;
  if (isEmpty(data)) {
    showEmptyPeriod(view, ctx);
    return;
  }
  // Toute la page vient des tables par jour, qui ignorent l'application, la nature et le clavier.
  const footnote = data.dayTablesUnfiltered ? unfilteredKeys : undefined;
  const cards = [bigramsCard(data.bigrams)].map((c) =>
    chartCard({ ...c, footnote }),
  );
  const grid = el("div", "chart-grid");
  grid.append(...cards.map((c) => c.section));
  view.replaceChildren(grid);
  await drawCharts(cards, ctx.isCurrent);
}
