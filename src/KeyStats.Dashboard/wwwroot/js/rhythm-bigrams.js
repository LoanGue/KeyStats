// Carte « Bigrammes qui coûtent le plus » de la page Rythme, triable par coût ou par taux de faute.

import { duration, nf, pct, shortDelay } from "./format.js";
import { el } from "./states.js";
import { key } from "./typos-text.js";

const MAX_ROWS = 15;

/** Temps perdu : « 840 ms », « 12,4 s », puis « 3 min » à partir d'une minute. */
const lost = (v) => (v < 60000 ? shortDelay(v) : duration(v));

/** Retard signé : « +42 ms », « −8 ms ». */
const signed = (v) =>
  `${Math.round(v) < 0 ? "−" : "+"}${shortDelay(Math.abs(v))}`;

const label = (c) => (c === " " ? "␣" : c);

// column : index de la colonne triée, assombrie dans l'en-tête.
const sorts = [
  {
    label: "Coût",
    column: 3,
    // Comme la route : un retard sous 0,5 ms s'afficherait « +0 ms ».
    rows: (all) => all.filter((b) => b.costMs > 0 && b.delayMs >= 0.5),
  },
  {
    label: "Taux de faute",
    column: 4,
    rows: (all) =>
      all
        .filter((b) => b.errorRate > 0)
        .sort((x, y) => y.errorRate - x.errorRate || y.count - x.count),
  },
];

/** Barre décorative, de largeur value / max. */
function bar(className, value, max) {
  const fill = el("span", className);
  fill.setAttribute("aria-hidden", "true");
  fill.style.width = max > 0 ? `${(Math.max(value, 0) / max) * 100}%` : "0";
  return fill;
}

function row(b, maxCost, maxRate) {
  // Le flex vit dans un span : posé sur la cellule, il lui ôterait son rôle de cellule.
  const pair = el("span", "rhythm-pair");
  pair.append(
    key(b.a, false),
    key(b.b, false),
    el("span", "rhythm-pair-text", label(b.a) + label(b.b)),
  );
  const cost = el("span", "rhythm-cost");
  cost.append(
    bar("rhythm-cost-bar", b.costMs, maxCost),
    el("span", null, b.costMs > 0 ? lost(b.costMs) : "—"),
  );
  const track = el("span", "rhythm-rate-track");
  track.setAttribute("aria-hidden", "true");
  track.append(bar("rhythm-rate-bar", b.errorRate, maxRate));
  const rate = el("span", "rhythm-rate");
  rate.append(track, el("span", null, pct(b.errorRate)));
  const tr = el("tr");
  for (const cell of [
    pair,
    nf.format(b.count),
    signed(b.delayMs),
    cost,
    rate,
  ]) {
    const td = el("td");
    td.append(cell);
    tr.append(td);
  }
  return tr;
}

function emptyRow(columns) {
  const td = el(
    "td",
    "rhythm-none",
    "Aucun bigramme de ce genre sur cette période.",
  );
  td.colSpan = columns;
  const tr = el("tr");
  tr.append(td);
  return tr;
}

export function bigramsCard(bigrams) {
  const card = {
    title: "Bigrammes qui coûtent le plus",
    note: "Coût = fréquence × retard, le retard étant l'écart avec votre délai médian entre deux touches. Un bigramme fréquent et un peu lent pèse plus qu'un bigramme rare et très lent.",
    wide: true,
  };
  if (!bigrams.length)
    return {
      ...card,
      empty:
        "Aucun enchaînement tapé au moins dix fois sur cette période : le coût ne se mesure pas encore.",
    };
  const group = el("div", "seg small");
  group.setAttribute("role", "group");
  group.setAttribute("aria-label", "Trier par");
  const aside = el("div", "rhythm-sort");
  aside.append(el("span", "rhythm-sort-label", "Trier par"), group);
  const heads = [
    "Bigramme",
    "Fréquence",
    "Retard moyen",
    "Coût",
    "Taux de faute",
  ].map((h) => el("th", null, h));
  const headRow = el("tr");
  headRow.append(...heads);
  const thead = el("thead");
  thead.append(headRow);
  const body = el("tbody");
  const table = el("table", "rhythm-bigrams");
  table.setAttribute("aria-label", "Bigrammes");
  table.append(thead, body);
  const scroll = el("div", "rhythm-scroll");
  scroll.append(table);
  const maxCost = Math.max(...bigrams.map((b) => b.costMs));
  const maxRate = Math.max(...bigrams.map((b) => b.errorRate));
  // Les boutons restent en place : le focus ne se perd pas au changement de tri.
  const buttons = sorts.map((s) => {
    const node = el("button", null, s.label);
    node.type = "button";
    node.addEventListener("click", () => show(s));
    return node;
  });
  group.append(...buttons);
  const show = (sort) => {
    sorts.forEach((s, i) =>
      buttons[i].setAttribute("aria-pressed", String(s === sort)),
    );
    heads.forEach((th, i) => {
      th.classList.toggle("sorted", i === sort.column);
      if (i === sort.column) th.setAttribute("aria-sort", "descending");
      else th.removeAttribute("aria-sort");
    });
    const rows = sort.rows([...bigrams]).slice(0, MAX_ROWS);
    body.replaceChildren(...rows.map((b) => row(b, maxCost, maxRate)));
    if (!rows.length) body.append(emptyRow(heads.length));
  };
  show(sorts[0]);
  return { ...card, aside, body: scroll };
}
