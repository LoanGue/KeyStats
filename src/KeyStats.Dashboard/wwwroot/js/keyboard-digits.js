// Carte des chiffres de la page Clavier : pour chaque chiffre, rangée du haut ou pavé numérique (/api/keyboard).

import { chartCard, legend } from "./chart-card.js";
import { nf, pct } from "./format.js";
import { el } from "./states.js";

const top = "--chart";
const pad = "--chart-typing";

const unfiltered =
  "Les chiffres couvrent toute la période : les filtres d'application, de nature et de clavier ne s'y appliquent pas.";

/** Barre décorative : la part de la rangée du haut, puis celle du pavé. */
function bar(d) {
  const box = el("span", "digit-bar");
  box.setAttribute("aria-hidden", "true");
  for (const [n, color] of [
    [d.topRow, top],
    [d.numpad, pad],
  ]) {
    if (!n) continue;
    const part = el("span");
    part.style.flex = String(n);
    part.style.background = `var(${color})`;
    box.append(part);
  }
  return box;
}

/** Ligne d'un chiffre : le chiffre, sa barre et sa part au pavé, avec les nombres pour les lecteurs d'écran. */
function row(d) {
  const total = d.topRow + d.numpad;
  const item = el("li", "digit-row");
  const share = total ? `${pct(d.numpad / total)} au pavé` : "jamais tapé";
  const name = el("span", "digit-name", String(d.digit));
  name.setAttribute("aria-hidden", "true");
  item.append(
    el("span", "sr-only", `Chiffre ${d.digit} : `),
    name,
    bar(d),
    el("span", "digit-share", share),
    el(
      "span",
      "sr-only",
      `, ${nf.format(d.topRow)} sur la rangée du haut, ${nf.format(d.numpad)} au pavé`,
    ),
  );
  return item;
}

/** Spécification de chartCard pour les chiffres de data (de 0 à 9), rangés de 1 à 0 comme sur le clavier. */
export function digitsCard(data) {
  const digits = [...data.digits].sort(
    (a, b) => ((a.digit + 9) % 10) - ((b.digit + 9) % 10),
  );
  const numpad = digits.reduce((s, d) => s + d.numpad, 0);
  const total = digits.reduce((s, d) => s + d.topRow + d.numpad, 0);
  const card = {
    title: "Chiffres : rangée du haut ou pavé",
    note: "Pour chaque chiffre, d'où il a été tapé.",
    className: "digits-card",
    footnote: data.dayTablesUnfiltered ? unfiltered : undefined,
  };
  if (!total) return { ...card, empty: "Aucun chiffre tapé sur la période." };
  const list = el("ul", "digit-rows");
  list.setAttribute("aria-label", "Part de chaque chiffre tapée au pavé");
  list.append(...digits.map(row));
  const sum = el("p", "digit-total");
  sum.append("Au total, ", el("strong", null, pct(numpad / total)));
  sum.append(" des chiffres passent par le pavé numérique.");
  const body = el("div", "digits");
  body.append(list, sum);
  return {
    ...card,
    aside: legend([
      ["Rangée du haut", null, top],
      ["Pavé numérique", null, pad],
    ]),
    body,
  };
}
