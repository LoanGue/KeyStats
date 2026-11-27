// Page Lettres et mots : fréquence des lettres et longueur des mots, comparées au français (/api/letters).

import { getJson } from "./api.js";
import { chartCard, drawCharts } from "./chart-card.js";
import { frequencyCard } from "./letters-frequency.js";
import { lengthsCard, wordsCard } from "./letters-words.js";
import { el, showEmptyPeriod } from "./states.js";

// Les tables par jour connaissent la nature de session, pas l'application ni le clavier.
const unfiltered =
  "Le filtre d'application ou de clavier ne s'applique pas à cette carte : elle couvre toute la frappe de la période.";

const isEmpty = (data) =>
  !data.words.count && !data.letters.some((l) => l.count);

export async function renderLetters(view, ctx) {
  const data = await getJson(`/api/letters${ctx.query}`);
  if (!ctx.isCurrent()) return;
  if (isEmpty(data)) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const footnote = data.dayTablesUnfiltered ? unfiltered : undefined;
  const frequency = frequencyCard(data.letters);
  const cards = [
    frequency,
    wordsCard(data.words),
    lengthsCard(data.wordLengths),
  ].map((spec) => chartCard({ ...spec, footnote }));
  frequency.bind(cards[0].node);
  const grid = el("div", "chart-grid letters-grid");
  grid.append(...cards.map((c) => c.section));
  view.replaceChildren(grid);
  await drawCharts(cards, ctx.isCurrent);
}
