// Cartes de doigté de la page Rythme : charge par doigt, même doigt, alternance des mains, côté de la touche Maj.

import { pct } from "./format.js";
import { el } from "./states.js";
import { stack } from "./typos-correction.js";

const NAMES = {
  Pinky: "Auriculaire",
  Ring: "Annulaire",
  Middle: "Majeur",
  Index: "Index",
  Thumb: "Pouce",
};

// Ordre de la maquette : de l'auriculaire au pouce à gauche, du pouce à l'auriculaire à droite.
const HANDS = [
  [
    "left",
    "Main gauche",
    "--chart",
    ["Pinky", "Ring", "Middle", "Index", "Thumb"],
  ],
  [
    "right",
    "Main droite",
    "--chart-typing",
    ["Thumb", "Index", "Middle", "Ring", "Pinky"],
  ],
];

/** Part de n dans total, ou « — » sans total. */
const share = (n, total) => (total ? pct(n / total) : "—");

function hand(label, color, fingers, total, max) {
  const box = el("div", "rhythm-hand");
  box.append(el("span", "rhythm-hand-title", label));
  for (const [name, presses] of fingers) {
    const row = el("div", "rhythm-finger");
    const bar = el("span", "rhythm-finger-bar");
    bar.setAttribute("aria-hidden", "true");
    bar.style.background = `var(${color})`;
    bar.style.width = `${(presses / max) * 100}%`;
    row.append(
      el("span", null, name),
      bar,
      el("span", null, share(presses, total)),
    );
    box.append(row);
  }
  return box;
}

export function fingersCard(fingers) {
  const card = {
    title: "Charge par doigt",
    note: "Part des appuis, selon la position standard de frappe à dix doigts.",
  };
  const byName = new Map(fingers.map((f) => [f.finger, f.presses]));
  const total = fingers.reduce((sum, f) => sum + f.presses, 0);
  if (!total)
    return {
      ...card,
      empty: "Aucun appui attribué à un doigt sur cette période.",
    };
  const max = Math.max(...fingers.map((f) => f.presses));
  const hands = el("div", "rhythm-hands");
  const sums = [];
  for (const [side, label, color, order] of HANDS) {
    const rows = order.map((f) => [NAMES[f], byName.get(side + f) ?? 0]);
    sums.push([rows.reduce((sum, [, n]) => sum + n, 0), color]);
    hands.append(hand(label, color, rows, total, max));
  }
  const split = el("div", "rhythm-split");
  const legend = el("div", "rhythm-split-legend");
  legend.append(
    el("strong", null, "Main gauche"),
    el(
      "span",
      null,
      `${share(sums[0][0], total)} · ${share(sums[1][0], total)}`,
    ),
    el("strong", null, "Main droite"),
  );
  split.append(legend, stack(sums, "rhythm-split-bar"));
  const body = el("div", "rhythm-fingers");
  body.append(hands, split);
  return { ...card, body };
}

function stat(title, value, text) {
  const box = el("div", "rhythm-stat");
  box.append(
    el("h2", null, title),
    el("strong", null, value),
    el("p", null, text),
  );
  return box;
}

/** Trois chiffres côte à côte, sans titre de carte : la maquette n'en a pas. */
export function flowSection(flow, capitals, footnote) {
  const capitalTotal =
    capitals.opposite + capitals.sameSide + capitals.capsLock;
  const section = el("section", "card wide rhythm-flow");
  section.append(
    stat(
      "Bigrammes du même doigt",
      share(flow.sameFinger, flow.bigrams),
      "Deux touches de suite tapées par le même doigt, comme « ce » sur un AZERTY : le doigt doit voyager au lieu d'attendre.",
    ),
    stat(
      "Alternance des mains",
      share(flow.alternating, flow.bigrams),
      "Part des enchaînements qui passent d'une main à l'autre. L'alternance laisse une main se préparer pendant que l'autre frappe.",
    ),
    stat(
      "Maj prise avec la main opposée",
      share(capitals.opposite, capitalTotal),
      "Majuscules faites avec la touche Maj de l'autre main que la lettre. Le reste : Maj du même côté, ou Verr. Maj.",
    ),
  );
  if (footnote) section.append(el("p", "chart-footnote", footnote));
  return section;
}
