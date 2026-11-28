// Carte « Export CSV » : fichiers de statistiques en clair, écrits par /api/export dans un dossier choisi
// dans la fenêtre de Windows. La page ne donne que la période, les filtres et les fichiers voulus.

import { postJson } from "./api.js";
import { button, settingsCard, statusLine } from "./settings-card.js";
import { el } from "./states.js";

let serial = 0;

const texts = {
  picking:
    "Choisissez le dossier dans la fenêtre de Windows. Elle peut s'ouvrir derrière cette fenêtre.",
  none: "Cochez au moins un type de données.",
  dates: "Indiquez deux dates dans l'ordre, la seconde au plus aujourd'hui.",
  invalid: "Ce dossier ne convient pas : choisissez un dossier existant.",
  failed:
    "L'export a échoué. Vérifiez que le dossier est accessible en écriture, puis réessayez.",
};

const failure = (error) =>
  error.status
    ? `La demande a échoué (erreur ${error.status}). Réessayez.`
    : "Le tableau de bord n'a pas répondu. Réessayez.";

/** Jour local au format aaaa-mm-jj. */
function isoDay(date) {
  const pad = (n) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function checkbox(label, checked) {
  const row = el("label", "settings-check");
  const input = el("input");
  input.type = "checkbox";
  input.checked = checked;
  row.append(input, label);
  return { row, input };
}

function dateField(label, value, max) {
  const row = el("label", "settings-date");
  const input = el("input");
  input.type = "date";
  input.value = value;
  input.max = max;
  row.append(el("span", null, label), input);
  return { row, input };
}

/** query : filtres de l'en-tête sous forme de requête (ctx.query). */
export function exportCard(query) {
  const id = ++serial;
  const { section } = settingsCard(
    "Export CSV",
    "Un fichier par type de données, dans le dossier de votre choix.",
  );

  const period = el("label", "settings-select");
  const label = el("span", "settings-label", "Période");
  const select = el("select");
  for (const [value, text] of [
    ["all", "Tout l'historique"],
    ["filters", "Période des filtres"],
    ["custom", "Personnalisée…"],
  ]) {
    const option = el("option", null, text);
    option.value = value;
    select.append(option);
  }
  period.append(label, select);

  const today = new Date();
  const monthAgo = new Date(today);
  monthAgo.setDate(today.getDate() - 29);
  const from = dateField("Du", isoDay(monthAgo), isoDay(today));
  const to = dateField("Au", isoDay(today), isoDay(today));
  const range = el("div", "settings-range");
  range.id = `export-range-${id}`;
  range.append(from.row, to.row);
  range.hidden = true;
  select.setAttribute("aria-controls", range.id);
  select.addEventListener("change", () => {
    range.hidden = select.value !== "custom";
  });

  const days = checkbox("Statistiques par jour", true);
  const sessions = checkbox("Statistiques par session", true);
  const keys = checkbox("Statistiques par touche", false);
  const checks = el("div", "settings-checks");
  checks.append(days.row, sessions.row, keys.row);

  const status = statusLine();
  const run = button("Exporter en CSV", true, start);
  const actions = el("div", "settings-actions");
  actions.append(run);
  section.append(period, range, checks, actions, status.node);

  /** Marque le champ de date fautif, ou aucun. */
  function markDates(bad) {
    for (const input of [from.input, to.input])
      input.setAttribute("aria-invalid", String(input === bad));
  }
  for (const f of [from, to])
    f.input.addEventListener("input", () => markDates(null));

  /** Corps de la demande, ou un message quand le choix ne peut pas partir. */
  function request() {
    const body = {
      filters: select.value === "filters" ? query.slice(1) : "period=all",
      days: days.input.checked,
      sessions: sessions.input.checked,
      keys: keys.input.checked,
    };
    if (!body.days && !body.sessions && !body.keys) return texts.none;
    if (select.value !== "custom") return body;
    const max = isoDay(new Date());
    const [a, b] = [from.input.value, to.input.value];
    const bad =
      !a || a > max ? from.input : !b || b > max || a > b ? to.input : null;
    markDates(bad);
    if (bad) return texts.dates;
    return { ...body, from: a, to: b };
  }

  async function start() {
    const body = request();
    if (typeof body === "string") {
      status.show(body, true);
      if (body === texts.dates)
        range.querySelector("[aria-invalid=true]").focus();
      return;
    }
    run.disabled = true;
    status.show(texts.picking);
    let result;
    try {
      result = await postJson("/api/export", body);
    } catch (error) {
      result = { error };
    }
    run.disabled = false;
    const [text, bad] = describe(result);
    status.show(text, bad);
    if (!section.contains(document.activeElement)) run.focus();
  }

  return section;
}

function describe(result) {
  if (result.error) return [failure(result.error), true];
  if (result.outcome === "cancelled") return [""];
  if (result.outcome === "invalid") return [texts.invalid, true];
  if (result.outcome !== "done") return [texts.failed, true];
  const n = result.files.length;
  return [
    `${n === 1 ? "Fichier écrit" : `${n} fichiers écrits`} dans ${result.folder} : ${result.files.join(", ")}.`,
  ];
}
