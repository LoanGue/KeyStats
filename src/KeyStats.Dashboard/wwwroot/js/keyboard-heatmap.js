// Carte de chaleur de la page Clavier : niveaux de couleur et touches.

import { keyName, numpad, rows } from "./keyboard-layout.js";
import { measures } from "./keyboard-measures.js";
import { el } from "./states.js";

/** Niveau de 1 à 5 sur la racine carrée de la valeur, de zéro au maximum ; 0 sans appui. */
function leveller(values) {
  const hi = Math.sqrt(Math.max(...values));
  return (v) =>
    v ? 1 + Math.min(4, Math.floor((5 * Math.sqrt(v)) / (hi + 1e-9))) : 0;
}

const allKeys = [...rows.flatMap((r) => r.keys), ...numpad];

/** Valeurs de la mesure pour chaque touche qui a un code. */
function valuesOf(data, measure) {
  const m = measures[measure];
  const values = new Map();
  for (const k of allKeys) {
    if (k.code == null || values.has(k.code)) continue;
    const info = data.byCode.get(k.code) ?? { presses: 0 };
    values.set(k.code, m.value(info, data));
  }
  return values;
}

/** Couleur d'un niveau : 0 sans appui, puis de faible à élevé. */
const heat = (level) => `var(--heat-${level})`;

/** Touche colorée selon son niveau, décrite aux lecteurs d'écran par sa valeur. */
function keyNode(k, value, level, measure) {
  const m = measures[measure];
  const node = el("span", "key");
  node.style.background = heat(level);
  if (level === 0) node.classList.add("empty");
  if (level >= 4) node.classList.add("strong");
  if (k.shown.length > 2) node.classList.add("long");
  if (k.pad) node.style.gridArea = `span ${k.span} / span ${k.cols}`;
  else node.style.width = `${k.width * 48 - 4}px`;
  if (k.shifted) node.append(el("span", "key-shift", k.shifted));
  node.append(el("span", null, k.shown));
  if (k.code == null) {
    node.setAttribute("aria-hidden", "true");
    return node;
  }
  node.setAttribute("role", "img");
  const name = k.shifted ? `${keyName(k)} ${k.shifted}` : keyName(k);
  node.setAttribute(
    "aria-label",
    `Touche ${name} : ${value == null ? "pas de mesure" : m.text(value)}`,
  );
  return node;
}

/** Portable et pavé, colorés selon measure. */
export function heatmap(data, measure) {
  const values = valuesOf(data, measure);
  const level = leveller([...values.values()]);
  const keyOf = (k) => {
    const v = k.code == null ? null : values.get(k.code);
    return keyNode(k, v, level(v), measure);
  };
  const main = el("div", "keys-main");
  for (const r of rows) {
    const row = el("div", "keys-row");
    row.style.height = `${r.height}px`;
    row.append(...r.keys.map(keyOf));
    main.append(row);
  }
  const pad = el("div", "keys-pad");
  pad.append(...numpad.map(keyOf));
  const box = el("div", "keys");
  box.append(main, pad);
  const scroll = el("div", "keys-scroll");
  scroll.append(box);
  return scroll;
}
