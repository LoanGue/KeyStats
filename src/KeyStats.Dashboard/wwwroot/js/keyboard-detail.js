// Détail d'une touche de la page Clavier : mesures de la période et appuis minute par minute (/api/keyboard/minutes).

import { baseOption, token, valueAxis } from "./charts.js";
import { dayLong, nf, pct, shortDelay } from "./format.js";
import { gap, measures } from "./keyboard-measures.js";
import { el } from "./states.js";

const appuis = measures.appuis.text;
const hour = (minute) => `${Math.floor(minute / 60)} h`;
const clock = (minute) =>
  `${hour(minute)} ${String(minute % 60).padStart(2, "0")}`;

function tile(label, value, caption) {
  const box = el("div", "key-tile");
  box.append(
    el("span", "key-tile-label", label),
    el("span", "key-tile-value", value ?? "—"),
    el("span", "key-tile-caption", caption),
  );
  return box;
}

/** Les quatre mesures de la touche ; « — » sans mesure. */
function tiles(info, data) {
  const v = (id) => measures[id].value(info, data);
  const lenteur = v("lenteur");
  const box = el("div", "key-tiles");
  box.append(
    tile("% du total", pct(v("part")), "des appuis"),
    tile(
      "Taux de faute",
      info.errorRate == null ? null : pct(info.errorRate),
      "de la prose tapée",
    ),
    tile(
      "Durée d'appui",
      info.holdMs == null ? null : shortDelay(info.holdMs),
      "médiane",
    ),
    tile(
      "Lenteur",
      lenteur == null ? null : gap(lenteur),
      "délai avant elle, face à la moyenne",
    ),
  );
  return box;
}

/** Appuis de chaque minute du jour, zéros compris : une minute sans appui n'est pas un trou. */
function series(minutes) {
  const last = minutes.length ? minutes[minutes.length - 1].minute : 0;
  const length = Math.max(1440, last + 1);
  const values = new Array(length).fill(0);
  for (const m of minutes) values[m.minute] = m.presses;
  return values.map((p, i) => [i, p]);
}

function minuteOption(points, length) {
  const base = baseOption();
  const muted = token("--muted");
  return {
    ...base,
    grid: { ...base.grid, top: 16 },
    xAxis: {
      type: "value",
      min: 0,
      max: length,
      interval: 360,
      axisLine: { lineStyle: { color: token("--border-strong") } },
      axisTick: { show: false },
      splitLine: { show: false },
      axisLabel: { color: muted, fontSize: 12, formatter: hour },
    },
    yAxis: valueAxis({ minInterval: 1 }),
    tooltip: {
      ...base.tooltip,
      trigger: "axis",
      formatter: (items) =>
        `<b>${clock(items[0].value[0])}</b><br>${appuis(items[0].value[1])}`,
    },
    series: [
      {
        type: "line",
        data: points,
        showSymbol: false,
        lineStyle: { color: token("--chart"), width: 1.5 },
        itemStyle: { color: token("--chart") },
      },
    ],
  };
}

/** Appuis de chaque heure, pour le tableau masqué du graphique. */
function hourRows(minutes) {
  const hours = new Map();
  for (const m of minutes) {
    const h = Math.floor(m.minute / 60);
    hours.set(h, (hours.get(h) ?? 0) + m.presses);
  }
  return [...hours].map(([h, n]) => [
    `${hour(h * 60)} – ${hour(h * 60 + 60)}`,
    nf.format(n),
  ]);
}

/** Spécification de chartCard pour la touche k (de keyboard-layout) et ses minutes. */
export function detailCard(k, name, data, minutes, footnote) {
  const info = data.byCode.get(k.code) ?? { presses: 0 };
  const cap = el("kbd", "key-cap", k.shown);
  cap.setAttribute("aria-hidden", "true");
  const day =
    minutes.day && dayLong.format(new Date(`${minutes.day}T12:00:00`));
  const head = el("div", "key-detail-head");
  head.append(el("h3", null, "Appuis par minute"));
  if (day)
    head.append(el("span", null, `${day}, dernier jour où elle a servi`));
  const body = el("div", "key-detail");
  body.append(tiles(info, data), head);
  const card = {
    title: `Touche ${name}`,
    note: `Total sur la période : ${appuis(info.presses ?? 0)}.`,
    aside: cap,
    body,
    className: "key-detail-card",
    footnote,
  };
  if (!minutes.day) {
    body.append(
      el("p", "key-detail-none", "Cette touche n'a pas servi sur la période."),
    );
    return card;
  }
  const points = series(minutes.minutes);
  return {
    ...card,
    label: `Appuis de la touche ${name} minute par minute, ${day} ; le tableau donne les appuis par heure.`,
    headers: ["Heure", "Appuis"],
    rows: hourRows(minutes.minutes),
    option: minuteOption(points, points.length),
  };
}
