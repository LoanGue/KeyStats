// Carte « Fréquence des lettres » : part de chaque lettre tapée, avec les traits du français et de l'anglais.

import { swatch } from "./chart-card.js";
import { baseOption, token, valueAxis } from "./charts.js";
import { escapeHtml, nf, pct } from "./format.js";
import { el } from "./states.js";

/** Trait horizontal sur toute la largeur d'une catégorie, à la hauteur de la valeur de référence. */
export function tickSeries(name, values, color, dashed) {
  return {
    type: "custom",
    id: name,
    name,
    data: values.map((v, i) => [i, v]),
    z: 3,
    renderItem: (params, api) => {
      const [x, y] = api.coord([api.value(0), api.value(1)]);
      const half = Math.max(api.size([1, 0])[0] / 2 - 4, 3);
      return {
        type: "line",
        shape: { x1: x - half, y1: y, x2: x + half, y2: y },
        style: {
          stroke: color,
          lineWidth: 3,
          lineCap: "round",
          lineDash: dashed ? [5, 3] : null,
        },
      };
    },
  };
}

/** Graphique en barres « Vous » et traits de référence, valeurs en parts (0–1). */
export function comparisonOption(categories, mine, references) {
  return {
    ...baseOption(),
    grid: { left: 8, right: 8, top: 16, bottom: 8, containLabel: true },
    tooltip: {
      ...baseOption().tooltip,
      trigger: "axis",
      axisPointer: { type: "shadow" },
      formatter: (items) => {
        const i = items[0].dataIndex;
        const lines = [
          ["Vous", mine[i]],
          ...references.map((r) => [r.name, r.values[i]]),
        ];
        return `<strong>${escapeHtml(categories[i])}</strong><br>${lines
          .map(([n, v]) => `${escapeHtml(n)} : ${pct(v)}`)
          .join("<br>")}`;
      },
    },
    xAxis: {
      type: "category",
      data: categories,
      axisTick: { show: false },
      axisLine: { lineStyle: { color: token("--border-strong") } },
      axisLabel: { color: token("--muted-strong"), fontSize: 12 },
    },
    yAxis: valueAxis({
      axisLabel: { formatter: (v) => `${nf.format(Math.round(v * 100))} %` },
    }),
    series: [
      {
        type: "bar",
        id: "Vous",
        name: "Vous",
        data: mine,
        barCategoryGap: "30%",
        itemStyle: { color: token("--chart"), borderRadius: [4, 4, 0, 0] },
      },
      ...references.map((r) => tickSeries(r.name, r.values, r.color, r.dashed)),
    ],
  };
}

function toggle() {
  const label = el("label", "letters-toggle");
  const box = el("input");
  box.type = "checkbox";
  label.append(box, "Comparer aussi à l'anglais");
  return { label, box };
}

export function frequencyCard(letters) {
  const total = letters.reduce((s, l) => s + l.count, 0);
  const card = {
    title: "Fréquence des lettres",
    note: "Part de chaque lettre dans ce que vous tapez ; les traits marquent la langue de référence.",
    wide: true,
    className: "letters-frequency",
    bind: () => {},
  };
  if (!total)
    return { ...card, empty: "Aucune lettre tapée sur cette période." };
  const categories = letters.map((l) => l.letter);
  const mine = letters.map((l) => l.count / total);
  const french = {
    name: "Français",
    values: letters.map((l) => l.french),
    color: token("--text"),
  };
  const english = {
    name: "Anglais",
    values: letters.map((l) => l.english),
    color: token("--chart-english"),
    dashed: true,
  };
  const option = (withEnglish) => {
    const o = comparisonOption(
      categories,
      mine,
      withEnglish ? [french, english] : [french],
    );
    // Lettres de l'axe en chasse fixe, comme dans la maquette.
    Object.assign(o.xAxis.axisLabel, {
      fontFamily: token("--mono"),
      fontSize: 13,
    });
    return o;
  };
  const label = (withEnglish) =>
    `Part de chaque lettre de a à z dans votre frappe, comparée au français${withEnglish ? " et à l'anglais" : ""}`;

  // Trait pointillé : la couleur passe par currentColor, swatch() poserait un fond plein.
  const dashed = el("span", "swatch line dashed");
  dashed.setAttribute("aria-hidden", "true");
  dashed.style.color = "var(--chart-english)";
  const englishKey = el("span");
  englishKey.append(dashed, "Anglais");
  englishKey.hidden = true;
  const you = el("span");
  you.append(swatch("--chart"), "Vous");
  const fr = el("span");
  fr.append(swatch("--text", "line"), "Français");
  const { label: switcher, box } = toggle();
  const aside = el("div", "chart-legend letters-legend");
  aside.append(you, fr, englishKey, switcher);

  return {
    ...card,
    aside,
    label: label(false),
    headers: ["Lettre", "Vous", "Français", "Anglais"],
    rows: letters.map((l, i) => [
      l.letter,
      pct(mine[i]),
      pct(l.french),
      pct(l.english),
    ]),
    option: option(false),
    // Appelé une fois le graphique tracé : un choix fait pendant le chargement est appliqué tout de suite.
    bind: (node) => {
      if (!node) return;
      const apply = () => {
        englishKey.hidden = !box.checked;
        node.setAttribute("aria-label", label(box.checked));
        window.echarts
          ?.getInstanceByDom(node)
          ?.setOption(option(box.checked), { replaceMerge: ["series"] });
      };
      box.addEventListener("change", apply);
      if (box.checked) apply();
    },
  };
}
