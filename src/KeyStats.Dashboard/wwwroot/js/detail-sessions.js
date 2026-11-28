// Liste des sessions de la page Détail (maquette Détail) : recherche par application ou date, liste
// à sélection unique navigable aux flèches.

import { appName } from "./filters.js";
import { duration, nf, wpm } from "./format.js";
import { el } from "./states.js";

const day = new Intl.DateTimeFormat("fr-FR", {
  weekday: "short",
  day: "numeric",
  month: "short",
});
const time = new Intl.DateTimeFormat("fr-FR", {
  hour: "2-digit",
  minute: "2-digit",
});

/** « ven. 27 nov. · 14:32 », pour la liste et le sous-titre de la frise. */
export const when = (ms) =>
  `${day.format(new Date(ms))} · ${time.format(new Date(ms))}`;

/** « Session du ven. 27 nov. à 14:32 ». */
export const sessionTitle = (s) =>
  `Session du ${day.format(new Date(s.startMs))} à ${time.format(new Date(s.startMs))}`;

/** Sans casse ni accents : « déc » trouve « Déc. ». */
const fold = (text) =>
  text.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();

function option(s) {
  const node = el("button", "detail-session");
  node.type = "button";
  node.setAttribute("role", "option");
  node.tabIndex = -1;
  const head = when(s.startMs);
  const line = `${appName(s.appId)} · ${duration(s.durationMs)} · ${wpm(s.wpm)}`;
  node.append(
    el("span", "detail-session-when", head),
    el("span", "detail-session-what", line),
  );
  node.dataset.search = fold(`${head} ${line}`);
  return node;
}

/** Carte des sessions ; onPick(session) à chaque choix, la première est choisie d'emblée. */
export function sessionsCard(data, onPick) {
  const section = el("section", "card detail-sessions");
  const head = el("div", "detail-sessions-head");
  head.append(
    el("h2", null, "Sessions"),
    el("p", null, "De la plus récente à la plus ancienne."),
  );
  const label = el("label", "detail-search");
  const input = el("input");
  input.type = "search";
  input.placeholder = "Application ou date";
  label.append(el("span", null, "Rechercher"), input);
  const list = el("div", "detail-session-list");
  list.setAttribute("role", "listbox");
  list.setAttribute("aria-label", "Sessions");
  const options = data.sessions.map(option);
  list.append(...options);
  const none = el("p", "detail-none", "Aucune session ne correspond.");
  none.hidden = true;
  section.append(head, label, list, none);
  if (data.truncated)
    section.append(
      el(
        "p",
        "detail-none",
        `Seules les ${nf.format(data.sessions.length)} sessions les plus récentes de la période sont listées.`,
      ),
    );

  let current = -1;
  function pick(i, focus) {
    if (current >= 0) {
      options[current].setAttribute("aria-selected", "false");
      options[current].tabIndex = -1;
    }
    current = i;
    options[i].setAttribute("aria-selected", "true");
    options[i].tabIndex = 0;
    if (focus) options[i].focus();
    onPick(data.sessions[i]);
  }

  options.forEach((node, i) => {
    node.setAttribute("aria-selected", "false");
    node.addEventListener("click", () => i !== current && pick(i, false));
  });
  list.addEventListener("keydown", (event) => {
    const shown = options.filter((o) => !o.hidden);
    const at = shown.indexOf(options[current]);
    const moves = {
      ArrowDown: at + 1,
      ArrowUp: at - 1,
      Home: 0,
      End: shown.length - 1,
    };
    if (!(event.key in moves) || !shown.length) return;
    event.preventDefault();
    const next =
      shown[Math.max(0, Math.min(moves[event.key], shown.length - 1))];
    const i = options.indexOf(next);
    if (i !== current) pick(i, true);
    else next.focus();
  });
  input.addEventListener("input", () => {
    const words = fold(input.value).split(/\s+/).filter(Boolean);
    let first = -1;
    options.forEach((node, i) => {
      node.hidden = !words.every((w) => node.dataset.search.includes(w));
      if (!node.hidden && first < 0) first = i;
    });
    none.hidden = first >= 0;
    // La liste garde une option atteignable au clavier quand la choisie est masquée.
    for (const node of options) node.tabIndex = -1;
    const reach = options[current]?.hidden ? options[first] : options[current];
    if (reach) reach.tabIndex = 0;
  });

  pick(0, false);
  return section;
}
