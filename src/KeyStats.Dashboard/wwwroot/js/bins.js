// Histogramme d'une durée en cases logarithmiques ({ fromMs, toMs, count }), commun aux pages.

import { baseOption, token, valueAxis } from "./charts.js";
import { shortDelay as ms } from "./format.js";

/** « 120 ms à 160 ms », ou « 3 s et plus » pour la dernière case. */
export const span = (b) =>
  b.toMs == null
    ? `${ms(b.fromMs)} et plus`
    : `${ms(b.fromMs)} à ${ms(b.toMs)}`;

/** Les cases de la première à la dernière non vide ; [] si toutes sont vides. */
export function trimBins(bins) {
  const first = bins.findIndex((b) => b.count);
  if (first < 0) return [];
  return bins.slice(first, bins.findLastIndex((b) => b.count) + 1);
}

/** Barres d'un histogramme : count(n) décrit une case dans l'infobulle, axis légende l'axe horizontal,
 * color est le jeton CSS des barres, tick(ms) écrit les graduations. */
export function binsOption(
  bins,
  { count, axis, color = "--chart-typing", tick = ms },
) {
  return {
    ...baseOption(),
    grid: { left: 8, right: 8, top: 8, bottom: 28, containLabel: true },
    tooltip: {
      ...baseOption().tooltip,
      formatter: (p) => {
        const b = bins[p.dataIndex];
        return `${span(b)}<br><b>${count(b.count)}</b>`;
      },
    },
    xAxis: {
      type: "category",
      data: bins.map((b) => tick(b.fromMs)),
      axisLine: { lineStyle: { color: token("--border-strong") } },
      axisTick: { show: false },
      axisLabel: { color: token("--muted"), fontSize: 11, hideOverlap: true },
    },
    yAxis: valueAxis({ axisLabel: { show: false }, axisLine: { show: false } }),
    graphic: [
      {
        type: "text",
        right: 0,
        bottom: 0,
        style: { text: axis, fill: token("--muted"), fontSize: 12 },
      },
    ],
    series: [
      {
        type: "bar",
        data: bins.map((b) => b.count),
        barCategoryGap: "8%",
        itemStyle: {
          color: token(color),
          borderRadius: [4, 4, 0, 0],
        },
      },
    ],
  };
}
