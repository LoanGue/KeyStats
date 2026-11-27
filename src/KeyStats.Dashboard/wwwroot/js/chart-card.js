// Carte de graphique commune aux pages : titre, légende, zone ECharts et tableau masqué de ses valeurs.

import { loadCharts, mount } from "./charts.js";
import { el } from "./states.js";

export const chartsMissing =
  "Le graphique n'a pas pu être affiché : le fichier vendor/echarts/echarts.min.js est introuvable.";

let serial = 0;

/** Rend node lisible comme une image décrite par un tableau masqué, à placer à côté de lui. */
export function describedBy(node) {
  node.setAttribute("role", "img");
  // Masqué dans un bloc : un tableau ne se réduit pas sous la largeur de son contenu.
  const hidden = el("div", "sr-only");
  const table = el("table");
  table.id = `chart-values-${++serial}`;
  hidden.append(table);
  node.setAttribute("aria-describedby", table.id);
  return { hidden, table };
}

/** Remplit table : une ligne d'en-têtes, puis une ligne par liste de cellules. */
export function fillTable(table, headers, rows) {
  const line = (tag, cells) => {
    const tr = el("tr");
    tr.append(...cells.map((c) => el(tag, null, c)));
    return tr;
  };
  table.replaceChildren(
    line("th", headers),
    ...rows.map((cells) => line("td", cells)),
  );
}

/** Remplace un graphique qui n'a pas pu être tracé par un message lisible. */
export function chartFailed(node, text = chartsMissing) {
  node.classList.add("chart-missing");
  node.removeAttribute("role");
  node.removeAttribute("aria-label");
  node.textContent = text;
}

/** Légende : [libellé, forme (dot, line, square), jeton CSS de la couleur]. */
export function legend(items) {
  const box = el("div", "chart-legend");
  for (const [label, shape, color] of items) {
    const item = el("span");
    const swatch = el("span", `swatch ${shape}`);
    swatch.setAttribute("aria-hidden", "true");
    swatch.style.background = `var(${color})`;
    item.append(swatch, label);
    box.append(item);
  }
  return box;
}

/**
 * Carte d'un graphique. spec : title, note (texte ou nœuds), aside (nœud en haut à droite), wide,
 * className, footnote, et soit empty (message à la place du graphique), soit body (contenu HTML
 * déjà lisible), label, headers, rows (tableau masqué) et option, l'un ou l'autre ou les deux.
 */
export function chartCard(spec) {
  const section = el(
    "section",
    ["card chart-card", spec.wide && "wide", spec.className]
      .filter(Boolean)
      .join(" "),
  );
  const head = el("div", "chart-head");
  const text = el("div");
  const note = el("p");
  note.append(...[spec.note].flat());
  text.append(el("h2", null, spec.title), note);
  head.append(text);
  if (spec.aside) head.append(spec.aside);
  section.append(head);
  let node = null;
  if (spec.empty) section.append(el("p", "chart chart-missing", spec.empty));
  else if (spec.body) section.append(spec.body);
  if (!spec.empty && spec.option) {
    node = el("div", "chart");
    const { hidden, table } = describedBy(node);
    node.setAttribute("aria-label", spec.label);
    fillTable(table, spec.headers, spec.rows);
    section.append(node, hidden);
  }
  if (spec.footnote) section.append(el("p", "chart-footnote", spec.footnote));
  return { section, node, option: node ? spec.option : null };
}

/** Trace les cartes une fois dans la page ; sans ECharts, seules leurs zones de graphique changent. */
export async function drawCharts(cards, isCurrent) {
  const drawn = cards.filter((c) => c.node);
  const shown = () => drawn.filter((c) => c.node.isConnected);
  let echarts;
  try {
    echarts = await loadCharts();
  } catch {
    if (isCurrent()) for (const c of shown()) chartFailed(c.node);
    return;
  }
  if (!isCurrent()) return;
  for (const c of shown()) mount(echarts, c.node, c.option);
}
