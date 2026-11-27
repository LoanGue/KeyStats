// Mesures de la carte de chaleur du clavier, calculées sur les données de /api/keyboard.

import { nf } from "./format.js";

/**
 * Mesures de la carte. value(k, data) : nombre ou null sans mesure ; text : sa forme lisible ;
 * root : niveaux sur la racine carrée (des appuis très inégaux restent distinguables).
 */
export const measures = {
  appuis: {
    label: "Appuis",
    legend: "Nombre d’appuis",
    help: "Nombre d’appuis par touche sur la période.",
    value: (k) => k.presses || null,
    text: (v) => `${nf.format(v)} appui${v > 1 ? "s" : ""}`,
    root: true,
  },
};

/** Données de la route, avec les touches rangées par code. */
export const prepare = (data) => ({
  ...data,
  byCode: new Map(data.keys.map((k) => [k.code, k])),
});
