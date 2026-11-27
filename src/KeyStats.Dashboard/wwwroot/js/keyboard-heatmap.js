// Carte de chaleur de la page Clavier : niveaux de couleur, touches à choisir et légende.

import { keyName, numpad, rows } from "./keyboard-layout.js";
import { measures } from "./keyboard-measures.js";
import { el } from "./states.js";

/**
 * Niveau de 1 à 5 de chaque valeur, 0 sans mesure. Avec root, sur la racine carrée de zéro au maximum,
 * et 0 aussi sans appui : la racine garde distinguables des appuis très inégaux (l'espace face aux
 * lettres rares). Sinon, de la plus petite à la plus grande valeur mesurée.
 */
function leveller(values, root) {
  const f = root ? Math.sqrt : (v) => v;
  const empty = (v) => v == null || (root && !v);
  const shown = values.filter((v) => !empty(v)).map(f);
  const lo = root ? 0 : Math.min(...shown);
  const hi = Math.max(...shown);
  return (v) => {
    if (empty(v)) return 0;
    if (hi <= lo) return 3;
    return 1 + Math.min(4, Math.floor((5 * (f(v) - lo)) / (hi - lo + 1e-9)));
  };
}

const allKeys = [...rows.flatMap((r) => r.keys), ...numpad];

/** Valeurs de la mesure m pour chaque touche qui a un code. */
function valuesOf(data, m) {
  const values = new Map();
  for (const k of allKeys) {
    if (k.code == null || values.has(k.code)) continue;
    const info = data.byCode.get(k.code) ?? { presses: 0 };
    values.set(k.code, m.value(info, data));
  }
  return values;
}

/** Couleur d'un niveau : 0 sans mesure, puis de faible à élevé sur la rampe froide ou chaude. */
const heat = (level, warm) =>
  `var(--heat-${level && warm ? "warm-" : ""}${level})`;

/** Touche colorée selon son niveau : un bouton qui la choisit, décrit par sa valeur. */
function keyNode(k, value, level, m, onPick) {
  const node = el(k.code == null || k.twin ? "span" : "button", "key");
  node.style.background = heat(level, m.warm);
  if (level === 0) node.classList.add("empty");
  if (level >= 4) node.classList.add("strong");
  if (k.shown.length > 2) node.classList.add("long");
  if (k.pad) node.style.gridArea = `span ${k.span} / span ${k.cols}`;
  else node.style.width = `${k.width * 48 - 4}px`;
  if (k.shifted) node.append(el("span", "key-shift", k.shifted));
  node.append(el("span", null, k.shown));
  if (k.code != null) {
    node.dataset.code = k.code;
    node.addEventListener("click", () => onPick(k.code));
  }
  if (k.code == null || k.twin) {
    // La moitié basse d'Entrée se clique aussi, mais un seul bouton la représente.
    node.setAttribute("aria-hidden", "true");
    return node;
  }
  node.type = "button";
  node.setAttribute("aria-pressed", "false");
  const name = k.shifted ? `${keyName(k)} ${k.shifted}` : keyName(k);
  const text = value == null ? "pas de mesure" : m.text(value);
  node.setAttribute("aria-label", `Touche ${name} : ${text}`);
  return node;
}

/** Portable et pavé, colorés selon la mesure nommée measure ; onPick(code) au clic d'une touche. */
export function heatmap(data, measure, onPick) {
  const m = measures[measure];
  const values = valuesOf(data, m);
  const level = leveller([...values.values()], m.root);
  const keyOf = (k) => {
    const v = k.code == null ? null : values.get(k.code);
    return keyNode(k, v, level(v), m, onPick);
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
  box.setAttribute("role", "group");
  box.setAttribute("aria-label", `Carte de chaleur du clavier : ${m.legend}`);
  box.append(main, pad);
  const scroll = el("div", "keys-scroll");
  scroll.append(box);
  return scroll;
}

/** Légende : de « aucune » à « élevé » sur la rampe de la mesure. */
export function heatLegend(measure) {
  const m = measures[measure];
  const ramp = el("span", "heat-ramp");
  ramp.setAttribute("aria-hidden", "true");
  for (let i = 1; i <= 5; i++) {
    const step = el("span");
    step.style.background = heat(i, m.warm);
    ramp.append(step);
  }
  const none = el("span", "heat-none");
  const noneBox = el("span");
  noneBox.setAttribute("aria-hidden", "true");
  none.append(noneBox, m.root ? "aucun appui" : "aucune mesure");
  const scale = el("div", "heat-scale");
  scale.append(
    el("span", null, m.legend),
    none,
    el("span", "muted", "faible"),
    ramp,
    el("span", "muted", "élevé"),
  );
  const foot = el("div", "heat-legend");
  foot.append(scale);
  return foot;
}
