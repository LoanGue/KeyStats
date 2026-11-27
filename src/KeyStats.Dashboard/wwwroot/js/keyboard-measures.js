// Mesures de la carte de chaleur du clavier, calculées sur les données de /api/keyboard.

import { nf } from "./format.js";

/**
 * Mesures de la carte. value(k, data) : nombre ou null sans mesure ; text : sa forme lisible.
 * Les niveaux suivent la racine carrée : des appuis très inégaux restent distinguables.
 */
export const measures = {
  appuis: {
    label: "Appuis",
    help: "Nombre d’appuis par touche sur la période.",
    value: (k) => k.presses ?? 0,
    text: (v) => `${nf.format(v)} appui${v > 1 ? "s" : ""}`,
  },
};

/** Données de la route, avec les touches rangées par code. */
export const prepare = (data) => ({
  ...data,
  byCode: new Map(data.keys.map((k) => [k.code, k])),
});
