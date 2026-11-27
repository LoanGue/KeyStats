// Mesures de la carte de chaleur du clavier, calculées sur les données de /api/keyboard.

import { nf } from "./format.js";

/**
 * Mesures de la carte. value(k, data) : nombre ; text : sa forme lisible.
 */
export const measures = {
  appuis: {
    help: "Nombre d'appuis par touche sur la période.",
    value: (k) => k.presses ?? 0,
    text: (v) => `${nf.format(v)} appui${v > 1 ? "s" : ""}`,
  },
};

/** Données de la route, avec les touches rangées par code. */
export const prepare = (data) => ({
  ...data,
  byCode: new Map(data.keys.map((k) => [k.code, k])),
});
