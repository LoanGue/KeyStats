// Carte « Calendrier » de la page Activité : temps d'écriture par jour sur 53 semaines (maquette Activite).

import { describedBy, fillTable } from "./chart-card.js";
import { cell, rampLegend } from "./activity-ramp.js";
import { duration, nf } from "./format.js";
import { el } from "./states.js";

const months = [
  "janv.",
  "févr.",
  "mars",
  "avr.",
  "mai",
  "juin",
  "juil.",
  "août",
  "sept.",
  "oct.",
  "nov.",
  "déc.",
];
// Jours aaaa-mm-jj lus en minuit UTC : les formateurs partagés suivent le fuseau local, qui décalerait la date.
const monthLong = new Intl.DateTimeFormat("fr-FR", {
  month: "long",
  year: "numeric",
  timeZone: "UTC",
});
const dayFull = new Intl.DateTimeFormat("fr-FR", {
  weekday: "long",
  day: "numeric",
  month: "long",
  year: "numeric",
  timeZone: "UTC",
});

/** Jour aaaa-mm-jj décalé de n jours, en dates UTC pour ignorer les changements d'heure. */
export function addDays(iso, n) {
  const d = new Date(`${iso}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}

/** Une ligne par mois couvert : mois, temps d'écriture, jours écrits. */
function monthRows(calendar) {
  const byMonth = new Map();
  for (const { day, writingMs } of calendar) {
    const key = day.slice(0, 7);
    const m = byMonth.get(key) ?? { ms: 0, days: 0 };
    m.ms += writingMs;
    m.days += 1;
    byMonth.set(key, m);
  }
  return [...byMonth].map(([key, m]) => [
    monthLong.format(new Date(`${key}-01T00:00:00Z`)),
    duration(m.ms),
    nf.format(m.days),
  ]);
}

export function calendarCard(data) {
  const written = new Map(data.calendar.map((d) => [d.day, d.writingMs]));
  const level = levels(data.calendar.map((d) => d.writingMs));
  const grid = el("div", "activity-calendar");
  const labels = el("div", "activity-days");
  labels.setAttribute("aria-hidden", "true");
  for (const name of ["lun.", "", "mer.", "", "ven.", "", ""])
    labels.append(el("span", null, name));
  const weeks = el("div", "activity-weeks");
  weeks.setAttribute("aria-hidden", "true");
  let previous = -1;
  for (let w = 0; w < 53; w++) {
    const monday = addDays(data.calendarFrom, w * 7);
    const month = Number(monday.slice(5, 7)) - 1;
    const column = el("div", "activity-week");
    const first = month !== previous && Number(monday.slice(8)) <= 7;
    column.append(el("span", "activity-month", first ? months[month] : ""));
    previous = month;
    for (let d = 0; d < 7; d++) {
      const day = addDays(monday, d);
      if (day > data.to) {
        column.append(el("span", "activity-cell future"));
        continue;
      }
      const ms = written.get(day) ?? 0;
      const date = dayFull.format(new Date(`${day}T00:00:00Z`));
      const node = cell(level(ms), `${date} : ${ms ? duration(ms) : "rien"}`);
      if (day === data.to) node.classList.add("today");
      column.append(node);
    }
    weeks.append(column);
  }
  grid.append(labels, weeks);
  const { hidden, table } = describedBy(grid);
  grid.setAttribute(
    "aria-label",
    "Calendrier : temps d'écriture par jour sur les 12 derniers mois",
  );
  fillTable(
    table,
    ["Mois", "Temps d'écriture", "Jours écrits"],
    monthRows(data.calendar),
  );
  const scroll = el("div", "activity-scroll");
  scroll.append(grid);
  const body = el("div");
  body.append(scroll, hidden);
  return {
    title: "Calendrier",
    note: "Temps d'écriture par jour sur les 12 derniers mois.",
    aside: rampLegend(),
    wide: true,
    body,
  };
}
