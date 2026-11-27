// Briques communes des cartes de la page Réglages : en-tête, ligne d'état, enregistrement.

import { postJson } from "./api.js";
import { el } from "./states.js";

/** Carte de réglage : titre et phrase d'explication ; le contenu suit dans section. */
export function settingsCard(title, text, className) {
  const section = el(
    "section",
    ["card settings-card", className].filter(Boolean).join(" "),
  );
  const head = el("div", "settings-head");
  const titles = el("div");
  titles.append(el("h2", null, title));
  if (text) titles.append(el("p", null, text));
  head.append(titles);
  section.append(head);
  return { section, head };
}

export function button(label, primary, onClick) {
  const node = el("button", primary ? "btn btn-primary" : "btn", label);
  node.type = "button";
  if (onClick) node.addEventListener("click", onClick);
  return node;
}

/** Ligne d'état d'une carte, lue par les lecteurs d'écran ; bad pour un échec. */
export function statusLine() {
  const node = el("p", "settings-status");
  node.setAttribute("role", "status");
  node.hidden = true;
  return {
    node,
    show(text, bad) {
      node.textContent = text;
      node.classList.toggle("bad", Boolean(bad));
      node.hidden = !text;
    },
  };
}

const failures = {
  invalid: "Valeur refusée : rien n'a été enregistré.",
  unreadable:
    "Le fichier settings.json est refusé : il n'est pas réécrit. Corrigez-le ou supprimez-le, puis rouvrez cette page.",
  failed: "Le fichier settings.json n'a pas pu être écrit. Réessayez.",
};

/**
 * Envoie patch à /api/settings et affiche le résultat dans status. Renvoie la réponse, ou null si la
 * requête a échoué. applied et pending : messages quand l'agent a pris le réglage, ou pas.
 */
export async function saveSettings(patch, status, { applied, pending }) {
  let result;
  try {
    result = await postJson("/api/settings", patch);
  } catch {
    status.show(failures.failed, true);
    return null;
  }
  if (result.outcome !== "saved") status.show(failures[result.outcome], true);
  else status.show(result.agentApplied ? applied : pending);
  return result;
}
