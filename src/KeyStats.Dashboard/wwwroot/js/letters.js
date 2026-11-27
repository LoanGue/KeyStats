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
  const [frenchLetters, englishLetters, frenchWords] = data.sources;
  const notes = [
    `Références : ${frenchLetters} ; ${englishLetters}.`,
    `Référence française : ${frenchWords}. Ce français parlé a des mots un peu plus courts qu'un texte écrit.`,
    `Référence française : ${frenchWords}.`,
  ];
  const frequency = frequencyCard(data.letters);
  const cards = [
    frequency,
    wordsCard(data.words),
    lengthsCard(data.wordLengths),
  ].map((spec, i) =>
    chartCard({
      ...spec,
      footnote: [notes[i], data.dayTablesUnfiltered && unfiltered]
        .filter(Boolean)
        .join(" "),
    }),
  );
  const grid = el("div", "chart-grid letters-grid");
  grid.append(...cards.map((c) => c.section));
  view.replaceChildren(grid);
  await drawCharts(cards, ctx.isCurrent);
  if (ctx.isCurrent()) frequency.bind(cards[0].node);
}
