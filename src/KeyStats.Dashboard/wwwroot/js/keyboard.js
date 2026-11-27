// Page Clavier : carte de chaleur AZERTY du portable, pavé numérique compris (/api/keyboard).

import { getJson } from "./api.js";
import { chartCard } from "./chart-card.js";
import { heatLegend, heatmap } from "./keyboard-heatmap.js";
import { measures, prepare } from "./keyboard-measures.js";
import { el, showEmptyPeriod } from "./states.js";

const unfiltered =
  "Les appuis suivent le filtre de clavier ; le filtre d'application ou de nature ne s'y applique pas, et les autres mesures couvrent toute la période.";

/** Boutons des mesures ; ils restent en place, le focus ne se perd pas au changement. */
function measurePicker(current, onPick) {
  const group = el("div", "seg small");
  group.setAttribute("role", "group");
  group.setAttribute("aria-label", "Mesure");
  const buttons = Object.entries(measures).map(([id, m]) => {
    const node = el("button", null, m.label);
    node.type = "button";
    node.dataset.measure = id;
    node.addEventListener("click", () => onPick(id));
    return node;
  });
  const press = (id) => {
    for (const b of buttons)
      b.setAttribute("aria-pressed", String(b.dataset.measure === id));
  };
  press(current);
  group.append(...buttons);
  return { group, press };
}

/** Carte de chaleur, redessinée à chaque mesure choisie. */
function heatCard(data) {
  let measure = "appuis";
  const note = el("span");
  note.setAttribute("aria-live", "polite");
  const body = el("div", "keyboard-body");
  const draw = () => {
    note.textContent = measures[measure].help;
    body.replaceChildren(heatmap(data, measure), heatLegend(measure));
    picker.press(measure);
  };
  const picker = measurePicker(measure, (id) => {
    measure = id;
    draw();
  });
  draw();
  return chartCard({
    title: "Carte de chaleur AZERTY — clavier du portable",
    note,
    aside: picker.group,
    body,
    wide: true,
    className: "keyboard-card",
    footnote: data.dayTablesUnfiltered ? unfiltered : undefined,
  }).section;
}

export async function renderKeyboard(view, ctx) {
  const raw = await getJson(`/api/keyboard${ctx.query}`);
  if (!ctx.isCurrent()) return;
  if (!raw.presses) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const grid = el("div", "chart-grid");
  grid.append(heatCard(prepare(raw)));
  view.replaceChildren(grid);
}
