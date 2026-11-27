// Graphiques de la page Vitesse : vitesse par application, par heure, et frappe contre débit réel par semaine.

import { legend } from "./chart-card.js";
import { baseOption, token, valueAxis } from "./charts.js";
import { appName } from "./filters.js";
import { isoWeek, nf, sessionCount, wpm } from "./format.js";
import { median } from "./speed-sessions.js";
import { el } from "./states.js";

const HOURS = 24;

const categoryAxis = (labels, extra) => ({
  type: "category",
  data: labels,
  axisLine: { lineStyle: { color: token("--border-strong") } },
  axisTick: { show: false },
  ...extra,
  axisLabel: { color: token("--muted"), fontSize: 12, ...extra?.axisLabel },
});

/** Médiane de la vitesse réelle de chaque groupe de sessions. */
function medians(sessions, keyOf) {
  const map = new Map();
  for (const s of sessions) {
    const key = keyOf(s);
    if (!map.has(key)) map.set(key, []);
    map.get(key).push(s.realWpm);
  }
  return [...map].map(([key, speeds]) => ({
    key,
    count: speeds.length,
    value: median(speeds),
  }));
}

export function appsCard(sessions) {
  const apps = medians(sessions, (s) => s.appId).sort(
    (a, b) => b.value - a.value,
  );
  const fastest = apps[0].value;
  const list = el("ul", "speed-apps");
  for (const app of apps) {
    const row = el("li");
    const bar = el("span", "speed-app-bar");
    bar.setAttribute("aria-hidden", "true");
    bar.style.width = `${fastest > 0 ? (app.value / fastest) * 100 : 0}%`;
    const track = el("span", "speed-app-track");
    track.append(bar);
    const name = el("span", "speed-app-name", appName(app.key));
    name.title = sessionCount(app.count);
    row.append(name, track, el("span", "speed-app-value", wpm(app.value)));
    list.append(row);
  }
  return {
    title: "Vitesse par application",
    note: "Vitesse réelle médiane, de la plus rapide à la plus lente.",
    body: list,
    footnote: "Les applications exclues dans Réglages n'apparaissent pas.",
  };
}

const hourRange = (h) => `${h}\u00a0h à ${h + 1}\u00a0h`;

export function hoursCard(sessions) {
  // Heure locale du navigateur : après un changement de fuseau, les anciennes sessions suivent le nouveau.
  const byHour = new Map(
    medians(sessions, (s) => new Date(s.startMs).getHours()).map((h) => [
      h.key,
      h,
    ]),
  );
  const hours = Array.from({ length: HOURS }, (_, h) => byHour.get(h) ?? null);
  return {
    title: "Vitesse par heure de la journée",
    note: "Vitesse réelle médiane ; une heure sans frappe reste vide.",
    label: `Vitesse réelle médiane pour chacune des ${HOURS} heures de la journée`,
    headers: ["Heure", "Vitesse réelle médiane", "Sessions"],
    rows: hours.map((h, i) => [
      hourRange(i),
      h ? wpm(h.value) : "aucune session",
      h ? nf.format(h.count) : "0",
    ]),
    option: {
      ...baseOption(),
      xAxis: categoryAxis(
        hours.map((_, h) => `${h}\u00a0h`),
        {
          axisLabel: {
            interval: (i) => i % 6 === 0 || i === HOURS - 1,
          },
        },
      ),
      yAxis: valueAxis({ name: "mots/min" }),
      series: [
        {
          type: "bar",
          data: hours.map((h) => (h ? Math.round(h.value * 10) / 10 : null)),
          barWidth: "80%",
          itemStyle: { color: token("--chart"), borderRadius: [4, 4, 0, 0] },
          tooltip: {
            formatter: ({ dataIndex }) => {
              const h = hours[dataIndex];
              return `<b>${hourRange(dataIndex)}</b><br>${wpm(h.value)} · ${sessionCount(h.count)}`;
            },
          },
        },
      ],
    },
  };
}

const weekOf = (w) => `${isoWeek(w.monday)}${w.partial ? " (en cours)" : ""}`;
const weekName = (w) => `S${weekOf(w)}`;
const speedOf = (v) => (v == null ? "aucune session" : wpm(v));

export function weeksCard(weeks) {
  // La semaine en cours, partielle, est tracée en couleur atténuée.
  const bars = (key, color) => ({
    type: "bar",
    name: key === "typingWpm" ? "Vitesse de frappe" : "Débit réel",
    barGap: "8%",
    barCategoryGap: "40%",
    data: weeks.map((w) => ({
      value: w[key] == null ? null : Math.round(w[key] * 10) / 10,
      itemStyle: { opacity: w.partial ? 0.45 : 1 },
    })),
    itemStyle: { color, borderRadius: [4, 4, 0, 0] },
  });
  const base = baseOption();
  return {
    wide: true,
    title: "Vitesse de frappe et débit réel",
    note: "La vitesse de frappe compte toutes les touches, corrections comprises. Le débit réel ne garde que le texte conservé : l'écart entre les deux, c'est le coût des corrections.",
    aside: legend([
      ["Vitesse de frappe", "square", "--chart-typing"],
      ["Débit réel", "square", "--chart"],
    ]),
    label: `Vitesse de frappe et débit réel des ${weeks.length} dernières semaines`,
    headers: ["Semaine", "Vitesse de frappe", "Débit réel"],
    rows: weeks.map((w) => [
      weekName(w),
      speedOf(w.typingWpm),
      speedOf(w.realWpm),
    ]),
    option: {
      ...base,
      xAxis: categoryAxis(
        weeks.map((w) =>
          w.partial ? `S${isoWeek(w.monday)}\nen cours` : weekName(w),
        ),
      ),
      yAxis: valueAxis({ name: "mots/min" }),
      tooltip: {
        ...base.tooltip,
        trigger: "axis",
        axisPointer: { type: "shadow" },
        formatter: (items) => {
          const w = weeks[items[0].dataIndex];
          return `<b>Semaine ${weekOf(w)}</b><br>Vitesse de frappe : ${speedOf(w.typingWpm)}<br>Débit réel : ${speedOf(w.realWpm)}`;
        },
      },
      series: [
        bars("typingWpm", token("--chart-typing")),
        bars("realWpm", token("--chart")),
      ],
    },
  };
}
