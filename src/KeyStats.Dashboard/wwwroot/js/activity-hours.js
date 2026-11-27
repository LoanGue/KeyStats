// Carte « Heure × jour de la semaine » de la page Activité (maquette Activite).

import { cell, levels } from "./activity-ramp.js";
import { describedBy, fillTable } from "./chart-card.js";
import { duration } from "./format.js";
import { el } from "./states.js";

const days = ["lun.", "mar.", "mer.", "jeu.", "ven.", "sam.", "dim."];
const dayNames = [
  "lundi",
  "mardi",
  "mercredi",
  "jeudi",
  "vendredi",
  "samedi",
  "dimanche",
];

/** Une ligne par jour de la semaine : total et heure la plus chargée. */
function dayRows(hours) {
  return hours.map((row, d) => {
    const total = row.reduce((a, b) => a + b, 0);
    const peak = row.indexOf(Math.max(...row));
    return [
      dayNames[d],
      duration(total),
      total ? `${peak}\u00a0h à ${peak + 1}\u00a0h` : "aucune",
    ];
  });
}

export function hoursCard(hours) {
  const level = levels(hours.flat());
  const grid = el("div", "activity-hours");
  grid.append(el("span"));
  for (let h = 0; h < 24; h++)
    grid.append(el("span", "activity-hour", h % 6 ? "" : `${h}\u00a0h`));
  hours.forEach((row, d) => {
    grid.append(el("span", "activity-weekday", days[d]));
    row.forEach((ms, h) =>
      grid.append(
        cell(
          level(ms),
          `${dayNames[d]}, ${h}\u00a0h à ${h + 1}\u00a0h : ${ms ? duration(ms) : "rien"}`,
        ),
      ),
    );
  });
  const { hidden, table } = describedBy(grid);
  grid.setAttribute(
    "aria-label",
    "Carte de chaleur : temps d'écriture par heure et par jour de la semaine",
  );
  fillTable(
    table,
    ["Jour", "Temps d'écriture", "Heure la plus chargée"],
    dayRows(hours),
  );
  const body = el("div");
  body.append(grid, hidden);
  return {
    title: "Heure × jour de la semaine",
    note: "Temps d'écriture cumulé sur la période.",
    body,
  };
}
