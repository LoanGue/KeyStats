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
  const frequency = frequencyCard(data.letters);
  // Chaque carte porte la note de sa source à côté de sa définition.
  const cards = [
    [frequency, `Références : ${frenchLetters} ; ${englishLetters}.`],
    [
      wordsCard(data.words),
      `Référence française : ${frenchWords}. Ce français parlé a des mots un peu plus courts qu'un texte écrit.`,
    ],
    [lengthsCard(data.wordLengths), `Référence française : ${frenchWords}.`],
  ].map(([spec, note]) =>
    chartCard({
      ...spec,
      footnote: [note, data.dayTablesUnfiltered && unfiltered]
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
