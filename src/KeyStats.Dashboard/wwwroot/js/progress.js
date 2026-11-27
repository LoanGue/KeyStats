// Courbe de progression semaine par semaine de la vue d'ensemble (vitesse, précision ou temps).

import { loadCharts, mount, reducedMotion, token } from "./charts.js";
import { el } from "./states.js";

const nf = new Intl.NumberFormat("fr-FR");
const nf1 = new Intl.NumberFormat("fr-FR", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

// Mesures de la courbe : valeur d'une semaine (null = aucune session), unité et légende.
const measures = {
  vitesse: {
    label: "Vitesse réelle",
    unit: "mots/min",
    legend: "Vitesse réelle en mots par minute, corrections déduites",
    of: (w) => (w.realWpm == null ? null : Math.round(w.realWpm * 10) / 10),
    format: (v) => `${nf1.format(v)}\u00a0mots/min`,
  },
  precision: {
    label: "Précision",
    unit: "% de frappes justes",
    legend: "Part des frappes qui ne sont pas effacées ensuite",
    of: (w) => (w.accuracy == null ? null : Math.round(w.accuracy * 1000) / 10),
    format: (v) => `${nf1.format(v)}\u202f%`,
  },
  temps: {
    label: "Temps d'écriture",
    unit: "heures",
    legend: "Temps passé à écrire, pauses exclues",
    of: (w) => Math.round((w.writingMs / 3600000) * 10) / 10,
    format: (v) => `${nf1.format(v)}\u00a0h`,
  },
};
let measure = "vitesse";

/** Numéro de semaine ISO 8601 du lundi donné (aaaa-mm-jj). */
function isoWeek(monday) {
  const d = new Date(`${monday}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + 3); // le jeudi de la même semaine
  const jan4 = new Date(Date.UTC(d.getUTCFullYear(), 0, 4));
  return (
    1 +
    Math.round((d - jan4) / 604800000 + (((jan4.getUTCDay() + 6) % 7) - 3) / 7)
  );
}

function chartOption(weeks, id) {
  const m = measures[id];
  const color = token("--chart");
  const muted = token("--muted");
  const grid = token("--border");
  const values = weeks.map((w) => m.of(w));
  const last = weeks.length - 1;
  const partial = weeks[last].partial;
  // La semaine en cours est tracée en pointillé depuis la précédente.
  const done = values.map((v, i) => (partial && i === last ? null : v));
  const current = values.map((v, i) => {
    if (!partial || i < last - 1) return null;
    // Seul le point de la semaine en cours est marqué, en creux.
    return i === last ? { value: v, symbol: "circle", symbolSize: 10 } : v;
  });
  const labels = weeks.map((w) => `S${isoWeek(w.monday)}`);
  const line = { color, width: 2.5 };
  return {
    animation: !reducedMotion(),
    grid: { left: 8, right: 16, top: 32, bottom: 8, containLabel: true },
    textStyle: { fontFamily: token("--font") },
    xAxis: {
      type: "category",
      data: labels,
      boundaryGap: false,
      axisLine: { lineStyle: { color: token("--border-strong") } },
      axisTick: { show: false },
      axisLabel: { color: muted, fontSize: 12 },
    },
    yAxis: {
      type: "value",
      name: m.unit,
      nameTextStyle: { color: muted, fontSize: 12, align: "left" },
      scale: id !== "temps",
      splitLine: { lineStyle: { color: grid } },
      axisLabel: { color: muted, fontSize: 12, formatter: (v) => nf.format(v) },
    },
    tooltip: {
      trigger: "axis",
      backgroundColor: token("--text"),
      borderWidth: 0,
      textStyle: { color: token("--on-primary"), fontSize: 12 },
      formatter: (items) => {
        const i = items[0].dataIndex;
        const v = values[i];
        const head = `Semaine ${isoWeek(weeks[i].monday)}${weeks[i].partial ? " (en cours)" : ""}`;
        return `<b>${head}</b><br>${v == null ? "aucune session" : m.format(v)}`;
      },
    },
    series: [
      {
        type: "line",
        data: done,
        lineStyle: line,
        itemStyle: { color, borderColor: token("--surface"), borderWidth: 2 },
        symbol: "circle",
        symbolSize: 9,
      },
      {
        type: "line",
        data: current,
        lineStyle: { ...line, type: [6, 6] },
        itemStyle: {
          color: token("--surface"),
          borderColor: color,
          borderWidth: 2.5,
        },
        symbol: "none",
        tooltip: { show: false },
      },
    ],
  };
}

/** Tableau masqué qui donne aux lecteurs d'écran les valeurs de la courbe. */
function fillTable(table, weeks, m) {
  const head = el("tr");
  head.append(el("th", null, "Semaine"), el("th", null, m.label));
  const rows = weeks.map((w) => {
    const v = m.of(w);
    const tr = el("tr");
    tr.append(
      el("td", null, `S${isoWeek(w.monday)}${w.partial ? " (en cours)" : ""}`),
      el("td", null, v == null ? "aucune session" : m.format(v)),
    );
    return tr;
  });
  table.replaceChildren(head, ...rows);
}

/** Section de la courbe ; attach() la trace une fois la section dans la page. */
export function renderProgress(weeks) {
  const section = el("section", "card progress");
  const head = el("div", "progress-head");
  const title = el("div");
  const legend = el("p");
  title.append(el("h2", null, "Progression semaine par semaine"), legend);
  const pills = el("div", "measure");
  pills.setAttribute("role", "group");
  pills.setAttribute("aria-label", "Mesure affichée");
  const chartNode = el("div", "progress-chart");
  chartNode.setAttribute("role", "img");
  // Masqué dans un bloc : un tableau ne se réduit pas sous la largeur de son contenu.
  const hidden = el("div", "sr-only");
  const table = el("table");
  table.id = "progress-values";
  hidden.append(table);
  chartNode.setAttribute("aria-describedby", table.id);
  head.append(title, pills);
  section.append(head, chartNode, hidden);

  let chart = null;
  function draw() {
    const m = measures[measure];
    const partial = weeks[weeks.length - 1].partial;
    legend.textContent = partial
      ? `${m.legend} · la dernière semaine est en cours, donc partielle`
      : m.legend;
    chartNode.setAttribute(
      "aria-label",
      `Courbe de progression sur ${weeks.length} semaines : ${m.label.toLowerCase()}`,
    );
    for (const b of pills.children)
      b.setAttribute("aria-pressed", String(b.dataset.measure === measure));
    fillTable(table, weeks, m);
    if (chart) chart.setOption(chartOption(weeks, measure), true);
  }
  for (const [id, m] of Object.entries(measures)) {
    const b = el("button", "measure-btn", m.label);
    b.type = "button";
    b.dataset.measure = id;
    b.addEventListener("click", () => {
      measure = id;
      draw();
    });
    pills.append(b);
  }
  draw();
  // Le graphique se trace une fois la section dans la page, pour qu'il en connaisse la taille.
  return {
    section,
    attach: async () => {
      try {
        const echarts = await loadCharts();
        if (chartNode.isConnected)
          chart = mount(echarts, chartNode, chartOption(weeks, measure));
      } catch {
        // Sans ECharts, seule la courbe manque : les chiffres et le tableau restent.
        chartNode.classList.add("progress-missing");
        chartNode.removeAttribute("role");
        chartNode.removeAttribute("aria-label");
        chartNode.textContent =
          "La courbe n'a pas pu être affichée : le fichier vendor/echarts/echarts.min.js est introuvable.";
      }
    },
  };
}
