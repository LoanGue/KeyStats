// Carte « Seuils de détection » : pause et écart de session, en secondes à l'écran, en ms dans settings.json.

import { nf } from "./format.js";
import { el } from "./states.js";
import {
  button,
  saveSettings,
  settingsCard,
  statusLine,
} from "./settings-card.js";

// Bornes de docs/settings.md : 1 ms à 600 000 ms, la pause au plus égale à l'écart de session.
const maxMs = 600000;
const seconds = new Intl.NumberFormat("fr-FR", { maximumFractionDigits: 1 });

let serial = 0;

function field(label) {
  const row = el("label", "settings-field");
  const input = el("input");
  input.type = "number";
  input.min = "0.1";
  input.max = String(maxMs / 1000);
  input.step = "0.1";
  input.inputMode = "decimal";
  input.id = `threshold-${++serial}`;
  row.append(el("span", null, label), input, el("span", "settings-unit", "s"));
  return { row, input };
}

const toMs = (input) => Math.round(Number(input.value) * 1000);
const setSeconds = (input, ms) => {
  input.value = String(ms / 1000);
};

/** Message d'erreur des deux champs, ou chaîne vide s'ils sont acceptables. */
function check(active, session) {
  const bad = (ms) => !Number.isFinite(ms) || ms < 1 || ms > maxMs;
  if (bad(active) || bad(session))
    return `Chaque seuil va de 0,1 à ${nf.format(maxMs / 1000)} secondes.`;
  if (active > session)
    return "La pause ne peut pas dépasser l'écart qui ouvre une nouvelle session.";
  return "";
}

export function thresholdsCard(settings, locked) {
  const { section } = settingsCard(
    "Seuils de détection",
    "S'appliquent aux nouvelles frappes. Pour l'historique, lancez un recalcul.",
  );
  const active = field("Pause au-delà de");
  const session = field("Nouvelle session après");
  const error = el("p", "field-error");
  error.id = `threshold-error-${serial}`;
  error.hidden = true;
  for (const f of [active, session]) {
    f.input.setAttribute("aria-describedby", error.id);
    f.input.addEventListener("input", () => {
      error.hidden = true;
      f.input.removeAttribute("aria-invalid");
    });
  }
  const status = statusLine();
  const fill = (t) => {
    setSeconds(active.input, t.activeGapMs);
    setSeconds(session.input, t.sessionGapMs);
  };
  fill(settings.thresholds);

  const reset = button("Valeurs par défaut", false, () => {
    fill(settings.defaults);
    error.hidden = true;
    status.show(
      `Valeurs par défaut : pause au-delà de ${seconds.format(settings.defaults.activeGapMs / 1000)} s, nouvelle session après ${seconds.format(settings.defaults.sessionGapMs / 1000)} s. Enregistrez pour les appliquer.`,
    );
  });
  const save = button("Enregistrer", true, async () => {
    const patch = {
      activeGapMs: toMs(active.input),
      sessionGapMs: toMs(session.input),
    };
    const message = check(patch.activeGapMs, patch.sessionGapMs);
    error.textContent = message;
    error.hidden = !message;
    for (const f of [active, session])
      f.input.setAttribute("aria-invalid", message ? "true" : "false");
    if (message) return;
    save.disabled = true;
    const result = await saveSettings(patch, status, {
      applied: "Enregistré. L'agent a redémarré pour appliquer les seuils.",
      pending:
        "Enregistré. L'agent appliquera les seuils à son prochain démarrage.",
    });
    save.disabled = false;
    if (result) fill(result.settings.thresholds);
  });
  const actions = el("div", "settings-actions");
  actions.append(reset, save);
  section.append(active.row, session.row, error, actions, status.node);
  for (const node of [active.input, session.input, reset, save])
    node.disabled = locked;
  return section;
}
