// Graphiques de la page Vitesse tirés des sessions : nuage et médiane glissante, précision, répartition.

import { legend } from "./chart-card.js";
import { baseOption, token, valueAxis } from "./charts.js";
import { appName } from "./filters.js";
import { duration, escapeHtml, nf, nf1, sessionCount, wpm } from "./format.js";
import { el } from "./states.js";

const WINDOW = 10; // sessions de la médiane glissante
const BIN = 5; // largeur d'une tranche de l'histogramme, en mots/min
const HOUR = 3600000;
const DAY = 24 * HOUR;

const when = new Intl.DateTimeFormat("fr-FR", {
  weekday: "short",
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
});
const dayTick = new Intl.DateTimeFormat("fr-FR", {
  day: "numeric",
  month: "short",
});
const hourTick = new Intl.DateTimeFormat("fr-FR", {
  hour: "2-digit",
  minute: "2-digit",
});
const dayLong = new Intl.DateTimeFormat("fr-FR", {
  weekday: "long",
  day: "numeric",
  month: "long",
});

export function median(values) {
  const s = [...values].sort((a, b) => a - b);
  const mid = s.length >> 1;
  return s.length % 2 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
}

const round1 = (v) => Math.round(v * 10) / 10;
const pct = (v) => `${nf1.format(v * 100)}\u202f%`;

/** Infobulle d'une session : « date · application », puis le détail. */
export const sessionTip = (s, detail) =>
  `<b>${escapeHtml(`${when.format(s.startMs)} · ${appName(s.appId)}`)}</b><br>${detail}`;

/** Regroupe des valeurs par clé, dans l'ordre des clés. */
function groups(items, keyOf) {
  const map = new Map();
  for (const item of items) {
    const key = keyOf(item);
    if (!map.has(key)) map.set(key, []);
    map.get(key).push(item);
  }
  return map;
}

// Sur moins de deux jours, l'heure ; minuit porte la date pour lever l'ambiguïté.
const hourOrDay = (v) => {
  const d = new Date(v);
  return d.getHours() || d.getMinutes()
    ? hourTick.format(v)
    : dayTick.format(v);
};

const xTime = (formatter, extra) => ({
  type: "time",
  axisLine: { lineStyle: { color: token("--border-strong") } },
  axisTick: { show: false },
  splitLine: { show: false },
  axisLabel: {
    color: token("--muted"),
    fontSize: 12,
    hideOverlap: true,
    formatter,
  },
  ...extra,
});

export function sessionsCard(sessions) {
  const points = sessions.map((s) => [s.startMs, round1(s.realWpm)]);
  const rolling = sessions.map((s, i) => {
    const last = sessions.slice(Math.max(0, i - WINDOW + 1), i + 1);
    return [s.startMs, round1(median(last.map((x) => x.realWpm)))];
  });
  const first = sessions[0].startMs;
  const lastStart = sessions[sessions.length - 1].startMs;
  const span = lastStart - first;
  const days = groups(sessions, (s) => new Date(s.startMs).toDateString());
  const base = baseOption();
  return {
    wide: true,
    title: "Sessions et médiane glissante",
    note: `Chaque point est une session ; la ligne suit la médiane des ${WINDOW} dernières sessions.`,
    aside: legend([
      ["Session", "dot", "--chart-session"],
      ["Médiane glissante", "line", "--chart-median"],
    ]),
    label: `Vitesse réelle de ${sessionCount(sessions.length)} et médiane glissante des ${WINDOW} dernières`,
    headers: ["Jour", "Sessions", "Vitesse réelle médiane"],
    rows: [...days.values()].map((day) => [
      dayLong.format(day[0].startMs),
      nf.format(day.length),
      wpm(median(day.map((s) => s.realWpm))),
    ]),
    option: {
      ...base,
      // Une session seule ou une période très courte : une heure de marge de chaque côté.
      xAxis:
        span < 2 * DAY
          ? xTime(
              hourOrDay,
              span < HOUR ? { min: first - HOUR, max: lastStart + HOUR } : {},
            )
          : xTime((v) => dayTick.format(v)),
      yAxis: valueAxis({ name: "mots/min", scale: true }),
      series: [
        {
          type: "scatter",
          data: points,
          symbolSize: 7,
          itemStyle: { color: token("--chart-session"), opacity: 0.85 },
          tooltip: {
            formatter: ({ dataIndex }) => {
              const s = sessions[dataIndex];
              return sessionTip(
                s,
                `${wpm(s.realWpm)} · ${duration(s.durationMs)}`,
              );
            },
          },
        },
        {
          type: "line",
          data: rolling,
          showSymbol: false,
          z: 3,
          lineStyle: { color: token("--chart-median"), width: 2.5 },
          itemStyle: { color: token("--chart-median") },
          tooltip: { show: false },
        },
      ],
    },
  };
}

export function accuracyCard(sessions) {
  const prose = sessions.filter((s) => s.accuracy != null);
  const card = {
    title: "Vitesse en fonction de la précision",
    note: "Une session de texte par point. Plus à droite : moins de corrections.",
  };
  if (!prose.length)
    return {
      ...card,
      empty:
        "Aucune session de texte sur cette période : la précision ne se mesure que sur le texte, pas sur le code.",
    };
  const bands = groups(
    [...prose].sort((a, b) => a.accuracy - b.accuracy),
    (s) => Math.min(99, Math.floor(s.accuracy * 100)),
  );
  return {
    ...card,
    label: `Vitesse réelle selon la précision, ${sessionCount(prose.length)} de texte`,
    headers: ["Précision", "Sessions", "Vitesse réelle médiane"],
    rows: [...bands].map(([band, list]) => [
      `${band} à ${band + 1}\u202f%`,
      nf.format(list.length),
      wpm(median(list.map((s) => s.realWpm))),
    ]),
    option: {
      ...baseOption(),
      grid: { left: 8, right: 16, top: 32, bottom: 28, containLabel: true },
      xAxis: valueAxis({
        name: "précision (%) →",
        nameLocation: "end",
        nameGap: 0,
        nameTextStyle: {
          color: token("--muted"),
          fontSize: 12,
          align: "right",
          verticalAlign: "top",
          padding: [26, 0, 0, 0],
        },
        scale: true,
        splitLine: { show: false },
      }),
      yAxis: valueAxis({ name: "mots/min", scale: true }),
      series: [
        {
          type: "scatter",
          data: prose.map((s) => [round1(s.accuracy * 100), round1(s.realWpm)]),
          symbolSize: 7,
          itemStyle: { color: token("--chart"), opacity: 0.7 },
          tooltip: {
            formatter: ({ dataIndex }) => {
              const s = prose[dataIndex];
              return sessionTip(
                s,
                `${pct(s.accuracy)} de précision · ${wpm(s.realWpm)}`,
              );
            },
          },
        },
      ],
    },
  };
}

function peakAside(peak) {
  const box = el("div", "speed-peak");
  const value = el(
    "div",
    "speed-peak-value",
    peak == null ? "—" : nf.format(Math.round(peak)),
  );
  if (peak != null) value.append(el("span", null, "\u00a0mots/min"));
  box.append(el("div", null, "Pointe"), value);
  return box;
}

const marker = (x, color, type, text, position) => ({
  xAxis: x,
  lineStyle: { color, type, width: type === "solid" ? 2 : 1.5 },
  label: {
    formatter: text,
    position,
    color: token("--text"),
    fontSize: 12,
  },
});

export function distributionCard(sessions) {
  const speeds = sessions.map((s) => s.realWpm);
  const peaks = sessions.map((s) => s.peakWpm).filter((v) => v != null);
  const peak = peaks.length ? peaks.reduce((a, b) => Math.max(a, b)) : null;
  const mid = median(speeds);
  const fastest = speeds.reduce((a, b) => Math.max(a, b));
  const lo = Math.floor(speeds.reduce((a, b) => Math.min(a, b)) / BIN) * BIN;
  const bins = Array(Math.floor((fastest - lo) / BIN) + 1).fill(0);
  for (const v of speeds) bins[Math.floor((v - lo) / BIN)]++;
  // Une tranche vide de plus : avec un seul point, ECharts donnerait à la barre toute la largeur de l'axe.
  const data = [...bins, 0].map((n, i) => [lo + i * BIN + BIN / 2, n]);
  const hi = Math.max(
    lo + data.length * BIN,
    Math.ceil((peak ?? 0) / BIN) * BIN,
  );
  const range = (i) => `${lo + i * BIN} à ${lo + (i + 1) * BIN}\u00a0mots/min`;
  const marks = [
    marker(
      mid,
      token("--text"),
      [4, 4],
      `médiane ${nf.format(Math.round(mid))}`,
      "end",
    ),
  ];
  if (peak != null)
    marks.push(
      marker(peak, token("--chart-peak"), "solid", "pointe", "insideEndTop"),
    );
  return {
    title: "Répartition des vitesses",
    note: "Nombre de sessions par tranche de vitesse ; la pointe est la meilleure fenêtre de 30\u00a0s.",
    aside: peakAside(peak),
    label: `Histogramme des vitesses par tranche de ${BIN}\u00a0mots/min ; médiane ${wpm(mid)}${peak == null ? "" : `, pointe ${wpm(peak)}`}`,
    headers: ["Vitesse réelle", "Sessions"],
    rows: bins.flatMap((n, i) => (n ? [[range(i), nf.format(n)]] : [])),
    option: {
      ...baseOption(),
      xAxis: valueAxis({
        min: peak == null ? lo : Math.min(lo, Math.floor(peak / BIN) * BIN),
        max: hi,
        minInterval: BIN,
        splitLine: { show: false },
      }),
      yAxis: valueAxis({ name: "sessions", minInterval: 1 }),
      series: [
        {
          type: "bar",
          data,
          barWidth: "92%",
          itemStyle: { color: token("--chart"), borderRadius: [4, 4, 0, 0] },
          tooltip: {
            formatter: ({ dataIndex }) =>
              `<b>${range(dataIndex)}</b><br>${sessionCount(bins[dataIndex] ?? 0)}`,
          },
          markLine: {
            symbol: "none",
            silent: true,
            animation: false,
            data: marks,
          },
        },
      ],
    },
  };
}
