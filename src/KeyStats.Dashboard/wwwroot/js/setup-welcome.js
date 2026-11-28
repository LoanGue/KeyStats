// Étape « Bienvenue » de l'assistant de première installation : ce qui est chiffré, ce qui ne l'est pas.

import { el } from "./states.js";
import { action, icon, intro, note } from "./setup-ui.js";

/** next : passage à l'étape 2. */
export function welcome(next) {
  const { head, h1 } = intro(
    1,
    "Bienvenue",
    "L'application mesure votre frappe au clavier pour vous montrer votre vitesse, vos fautes et votre rythme. Les données restent sur cet ordinateur.",
  );
  const tile = (name, title, text) => {
    const node = el("div", "setup-tile");
    const t = el("span", "setup-tile-title");
    t.append(icon(name), title);
    node.append(t, el("span", null, text));
    return node;
  };
  const tiles = el("div", "setup-tiles");
  tiles.append(
    tile(
      "lock",
      "Chiffré",
      "Le détail touche par touche (chaque appui, sa durée, son moment) et les titres de fenêtre. Ils ne s'ouvrent qu'avec Windows Hello ou votre phrase de secours.",
    ),
    tile(
      "chart",
      "Lisible sans déverrouiller",
      "Les statistiques : totaux, vitesses, taux de faute, répartition par application. Elles s'affichent directement.",
    ),
  );
  const foot = el("div", "setup-foot setup-foot-end");
  foot.append(action("Commencer", true, next, true));
  const body = [
    head,
    tiles,
    note(
      "info",
      "info",
      "Tant que cet assistant n'est pas terminé, l'agent n'enregistre que les statistiques : aucun détail touche par touche, aucun titre de fenêtre.",
    ),
    foot,
  ];
  return { body, focus: h1, wide: true };
}
