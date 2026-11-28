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

/** Bandeau d'un settings.json refusé : la page ne montre alors aucune carte, faute de valeurs sûres. */
export function refusedAlert() {
  const box = el("div", "settings-alert");
  box.setAttribute("role", "alert");
  box.append(
    el(
      "p",
      null,
      "Le fichier settings.json est refusé : une valeur y est hors bornes ou mal écrite. L'agent garde les réglages qu'il a déjà, ou les valeurs par défaut à son prochain démarrage, et cette page ne réécrit pas le fichier. Corrigez-le ou supprimez-le, puis rouvrez la page.",
    ),
  );
  return box;
}

/**
 * Remplace les cartes par le bandeau d'un settings.json refusé, trouvé à l'enregistrement. Le focus était
 * dans une carte qui disparaît : il passe au bandeau. node : un nœud de la carte, détaché si la page a changé.
 */
export function showRefused(node) {
  const grid = node.closest(".settings-grid");
  if (!grid) return;
  const alert = refusedAlert();
  alert.tabIndex = -1;
  grid.replaceWith(alert);
  alert.focus();
}

const failures = {
  invalid: "Valeur refusée : rien n'a été enregistré.",
  failed: "Le fichier settings.json n'a pas pu être écrit. Réessayez.",
};

/**
 * Envoie patch à /api/settings et affiche le résultat dans status. Renvoie la réponse, ou null si la
 * requête a échoué. applied et pending : messages quand l'agent a pris le réglage, ou pas ; restarted,
 * s'il est donné, quand l'agent a été relancé pour le prendre. Un fichier refusé entre l'ouverture
 * de la page et l'enregistrement remplace les cartes par le bandeau et renvoie null. Les appelants ne
 * touchent ensuite qu'aux nœuds de leur carte, détachés si la page a changé : pas de ctx.isCurrent().
 */
export async function saveSettings(
  patch,
  status,
  { applied, pending, restarted = applied },
) {
  let result;
  try {
    result = await postJson("/api/settings", patch);
  } catch {
    status.show(failures.failed, true);
    return null;
  }
  if (result.outcome === "unreadable") {
    showRefused(status.node);
    return null;
  }
  if (result.outcome !== "saved") status.show(failures[result.outcome], true);
  else if (result.restarted) status.show(restarted);
  else status.show(result.agentApplied ? applied : pending);
  return result;
}
