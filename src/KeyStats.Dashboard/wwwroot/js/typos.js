// Page Fautes : précision, fautes les plus fréquentes et manière de les corriger (/api/typos).

import { getJson } from "./api.js";
import { chartCard, drawCharts } from "./chart-card.js";
import { el, showEmptyPeriod } from "./states.js";
import { accuracyCard } from "./typos-text.js";

const total = (counts) => Object.values(counts).reduce((a, b) => a + b, 0);

/** Rien à montrer : ni prose dans les sessions retenues, ni faute, ni effacement. */
const isEmpty = (data) =>
  !data.days.some((d) => d.typed) &&
  !data.pairs.length &&
  !total(data.types) &&
  !total(data.deletion);

export async function renderTypos(view, ctx) {
  const data = await getJson(`/api/typos${ctx.query}`);
  if (!ctx.isCurrent()) return;
  if (isEmpty(data)) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const cards = [accuracyCard(data)].map(chartCard);
  const grid = el("div", "chart-grid");
  grid.append(...cards.map((c) => c.section));
  const notes = ["code", "mixed"].includes(ctx.filters.kind)
    ? [
        el(
          "p",
          "page-note",
          "Les fautes ne s'analysent que sur le texte : avec ce filtre de nature, la précision et le coût des corrections restent vides.",
        ),
      ]
    : [];
  view.replaceChildren(...notes, grid);
  await drawCharts(cards, ctx.isCurrent);
}
