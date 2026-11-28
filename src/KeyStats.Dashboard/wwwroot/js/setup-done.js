// Étape « Confirmation » de l'assistant de première installation : ce qui est configuré.

import { el } from "./states.js";
import { action, icon, intro } from "./setup-ui.js";

/**
 * hello : Windows Hello activé ; restarted : agent relancé. finish(hash) ferme l'assistant et ouvre
 * la page donnée (null : la page vers laquelle un lien mène déjà).
 */
export function done({ hello, restarted }, finish) {
  const badge = el("span", "setup-done");
  badge.append(icon("check"));
  const { head, h1 } = intro(
    4,
    "Tout est prêt",
    restarted
      ? "L'agent enregistre désormais aussi le détail touche par touche et les titres de fenêtre, chiffrés."
      : "L'agent n'a pas pu être relancé : il enregistrera le détail chiffré à son prochain démarrage.",
  );
  const list = el("ul", "setup-summary");
  const item = (name, value, ok) => {
    const li = el("li");
    const right = el("span", ok ? "setup-yes" : "setup-no");
    right.append(icon(ok ? "check" : "dash"), value);
    li.append(el("span", null, name), right);
    list.append(li);
    return right;
  };
  item("Chiffrement", "Actif", true);
  item("Agent", restarted ? "Relancé" : "Pas relancé", restarted);
  item("Phrase de secours", "Définie", true);
  const helloValue = item(
    "Windows Hello",
    hello ? "Activé" : "Non activé · ",
    hello,
  );
  if (!hello) {
    const link = el("a", null, "à faire dans Réglages");
    link.href = "#/reglages";
    link.addEventListener("click", () => finish(null));
    helloValue.append(link);
  }
  const foot = el("div", "setup-foot setup-foot-end");
  foot.append(
    action("Ouvrir la vue d'ensemble", true, () => finish("#/ensemble"), true),
  );
  return { body: [badge, head, list, foot], focus: h1 };
}
