// Mesures de la carte de chaleur du clavier, calculées sur les données de /api/keyboard.

import { nf, pct, shortDelay } from "./format.js";

/** Écart à la moyenne des touches : « +12 % », « −8 % ». */
const gap = (r) => {
  const v = Math.round((r - 1) * 100);
  return `${v > 0 ? "+" : v < 0 ? "−" : ""}${nf.format(Math.abs(v))} %`;
};

/**
 * Mesures du sélecteur. value(k, data) : nombre, ou null sans mesure ; text : sa forme lisible ;
 * root : niveaux sur la racine carrée, de zéro au maximum (des appuis très inégaux restent distinguables) ;
 * warm : rampe chaude, pour les mesures où une valeur élevée est un défaut.
 */
export const measures = {
  appuis: {
    label: "Appuis",
    legend: "Nombre d'appuis",
    help: "Nombre d'appuis par touche sur la période.",
    value: (k) => k.presses ?? 0,
    text: (v) => `${nf.format(v)} appui${v > 1 ? "s" : ""}`,
    root: true,
  },
  part: {
    label: "% du total",
    legend: "Part du total",
    help: "Part de chaque touche dans l'ensemble des appuis.",
    value: (k, data) => (k.presses ?? 0) / data.presses,
    text: pct,
    root: true,
  },
  faute: {
    label: "Taux de faute",
    legend: "Taux de faute",
    help: "Part des caractères de prose tapés sur cette touche qui sont des fautes.",
    value: (k) => k.errorRate ?? null,
    text: pct,
    warm: true,
  },
  duree: {
    label: "Durée d'appui",
    legend: "Durée d'appui médiane",
    help: "Temps médian entre l'enfoncement et le relâchement de la touche.",
    value: (k) => k.holdMs ?? null,
    text: shortDelay,
  },
  lenteur: {
    label: "Lenteur",
    legend: "Lenteur",
    help: "Délai médian avant cette touche, comparé à la moyenne des touches.",
    value: (k, data) =>
      k.delayMs != null && data.meanDelay ? k.delayMs / data.meanDelay : null,
    text: (r) => `${gap(r)} par rapport à la moyenne`,
    warm: true,
  },
};

/** Données de la route, avec la moyenne des délais médians et les touches rangées par code. */
export function prepare(data) {
  const delays = data.keys.map((k) => k.delayMs).filter((d) => d != null);
  return {
    ...data,
    meanDelay: delays.length
      ? delays.reduce((a, b) => a + b, 0) / delays.length
      : null,
    byCode: new Map(data.keys.map((k) => [k.code, k])),
  };
}
