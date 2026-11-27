// Cartes de la page Fautes sur le texte tapé : précision par jour.

import { baseOption, token, valueAxis } from "./charts.js";
import { nf, nf1 } from "./format.js";
import { el } from "./states.js";

const MIN_DAY = 200; // caractères de texte qu'il faut à un jour pour être « meilleur jour »

const dayTick = new Intl.DateTimeFormat("fr-FR", {
  day: "numeric",
  month: "short",
});
const dayLong = new Intl.DateTimeFormat("fr-FR", {
  weekday: "long",
  day: "numeric",
  month: "long",
});
const dateOf = (iso) => new Date(`${iso}T12:00:00`);
const pct = (v) => `${nf1.format(v * 100)} %`;

function figure(label, value) {
  const box = el("div");
  box.append(
    el("div", "typos-figure-label", label),
    el("div", "typos-figure-value", value == null ? "—" : pct(value)),
  );
  return box;
}

export function accuracyCard(data) {
  const card = {
    title: "Précision dans le temps",
    note: "Part des frappes qui ne sont pas effacées ensuite, jour par jour.",
    wide: true,
  };
  const days = data.days.filter((d) => d.accuracy != null);
  if (!days.length)
    return {
      ...card,
      empty:
        "Aucune session de texte sur cette période : la précision ne se mesure que sur le texte, pas sur le code.",
    };
  const best = days
    .filter((d) => d.typed >= MIN_DAY)
    .reduce((a, d) => (a && a.accuracy >= d.accuracy ? a : d), null);
  const aside = el("div", "typos-figures");
  aside.append(
    figure("Sur la période", data.accuracy),
    figure("Meilleur jour", best?.accuracy),
  );
  const round = (v) => (v == null ? null : Math.round(v * 1000) / 10);
  return {
    ...card,
    aside,
    label: `Précision par jour, ${pct(data.accuracy)} sur la période`,
    headers: ["Jour", "Caractères tapés", "Précision"],
    rows: days.map((d) => [
      dayLong.format(dateOf(d.day)),
      nf.format(d.typed),
      pct(d.accuracy),
    ]),
    option: {
      ...baseOption(),
      tooltip: { ...baseOption().tooltip, trigger: "axis" },
      xAxis: {
        type: "category",
        boundaryGap: false,
        data: data.days.map((d) => d.day),
        axisLine: { lineStyle: { color: token("--border-strong") } },
        axisTick: { show: false },
        axisLabel: {
          color: token("--muted"),
          fontSize: 12,
          hideOverlap: true,
          formatter: (v) => dayTick.format(dateOf(v)),
        },
      },
      yAxis: valueAxis({
        name: "%",
        scale: true,
        max: 100,
        axisLabel: { formatter: (v) => nf.format(v) },
      }),
      series: [
        {
          type: "line",
          data: data.days.map((d) => round(d.accuracy)),
          showSymbol: false,
          lineStyle: { color: token("--chart"), width: 2 },
          itemStyle: { color: token("--chart") },
          areaStyle: { color: token("--chart-area"), opacity: 0.6 },
          tooltip: {
            valueFormatter: (v) => (v == null ? "—" : `${nf1.format(v)} %`),
          },
        },
      ],
    },
  };
}
