// Page Vitesse : chaque session de la période et ce qui fait varier la vitesse (/api/speed).

import { getJson } from "./api.js";
import { chartCard, drawCharts } from "./chart-card.js";
import { nf } from "./format.js";
import { appsCard, hoursCard, weeksCard } from "./speed-breakdown.js";
import {
  accuracyCard,
  distributionCard,
  sessionsCard,
} from "./speed-sessions.js";
import { el, showEmptyPeriod } from "./states.js";

const MAX_SESSIONS = 5000; // plafond de /api/speed

export async function renderSpeed(view, ctx) {
  const data = await getJson(`/api/speed${ctx.query}`);
  if (!ctx.isCurrent()) return;
  const { sessions } = data;
  if (!sessions.length) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const cards = [
    sessionsCard(sessions),
    accuracyCard(sessions),
    distributionCard(sessions),
    appsCard(sessions),
    hoursCard(sessions),
    weeksCard(data.weeks),
  ].map(chartCard);
  const grid = el("div", "chart-grid");
  grid.append(...cards.map((c) => c.section));
  const notes = data.truncated
    ? [
        el(
          "p",
          "page-note",
          `La période compte plus de ${nf.format(MAX_SESSIONS)} sessions : seules les ${nf.format(MAX_SESSIONS)} plus récentes sont affichées.`,
        ),
      ]
    : [];
  view.replaceChildren(...notes, grid);
  await drawCharts(cards, ctx.isCurrent);
}
