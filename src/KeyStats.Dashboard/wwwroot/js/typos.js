// Page Fautes : précision, fautes les plus fréquentes et manière de les corriger (/api/typos).

import { getJson } from "./api.js";
import { chartCard, drawCharts } from "./chart-card.js";
import { el, showEmptyPeriod } from "./states.js";
import { typesCard } from "./typos-correction.js";
import { accentsCard, accuracyCard, pairsCard } from "./typos-text.js";

// Les tables par jour ignorent l'application, la nature et le clavier choisis.
const unfiltered =
  "Le filtre d'application, de nature ou de clavier ne s'applique pas à cette carte : elle couvre toute la prose de la période.";

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
  const footnote = data.dayTablesUnfiltered ? unfiltered : undefined;
  const cards = [
    accuracyCard(data),
    { ...pairsCard(data.pairs), footnote },
    { ...accentsCard(data.accents), footnote },
    { ...typesCard(data.types, data.delay), footnote },
  ].map(chartCard);
  const grid = el("div", "chart-grid");
  grid.append(...cards.map((c) => c.section));
  view.replaceChildren(grid);
  await drawCharts(cards, ctx.isCurrent);
}
