// Page Activité et origine : quand et dans quelles applications on écrit (/api/activity).

import { appsCard } from "./activity-apps.js";
import { calendarCard } from "./activity-calendar.js";
import { dayCard } from "./activity-day.js";
import { hoursCard } from "./activity-hours.js";
import { getJson } from "./api.js";
import { chartCard } from "./chart-card.js";
import { el, showEmptyPeriod } from "./states.js";

export async function renderActivity(view, ctx) {
  // La journée ignore les filtres ; le seuil de pause vient des réglages, facultatif pour la légende.
  const [data, day, settings] = await Promise.all([
    getJson(`/api/activity${ctx.query}`),
    getJson("/api/activity/day"),
    getJson("/api/settings").catch(() => null),
  ]);
  if (!ctx.isCurrent()) return;
  // Le calendrier ignore la période : la période est vide quand aucune appli n'y a de temps d'écriture.
  if (!data.apps.length) {
    showEmptyPeriod(view, ctx);
    return;
  }
  const grid = el("div", "chart-grid");
  grid.append(
    ...[
      calendarCard(data),
      hoursCard(data.hours),
      appsCard(data.apps, ctx),
      dayCard(day, settings?.thresholds?.activeGapMs ?? null, ctx),
    ]
      .map(chartCard)
      .map((c) => c.section),
  );
  view.replaceChildren(grid);
  // Sur un écran étroit, le calendrier défile : il s'ouvre sur les semaines récentes.
  const scroll = view.querySelector(".activity-scroll");
  scroll.scrollLeft = scroll.scrollWidth;
}
