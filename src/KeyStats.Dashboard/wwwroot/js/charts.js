// ECharts, servi depuis vendor/ (aucun CDN). Le fichier n'est chargé qu'à la première vue qui trace un graphique.

import { nf } from "./format.js";

let loading = null;

/** Renvoie l'objet echarts, après avoir chargé le script une seule fois. */
export function loadCharts() {
  if (window.echarts) return Promise.resolve(window.echarts);
  loading ??= new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "vendor/echarts/echarts.min.js";
    const fail = () => {
      loading = null;
      script.remove();
      reject(
        new Error("vendor/echarts/echarts.min.js : chargement impossible"),
      );
    };
    // Un fichier chargé qui ne définit pas echarts compte comme un échec, sans rester en cache.
    script.onload = () => (window.echarts ? resolve(window.echarts) : fail());
    script.onerror = fail;
    document.head.append(script);
  });
  return loading;
}

/** Valeur d'une variable CSS de :root, pour que les graphiques suivent les jetons. */
export const token = (name) =>
  getComputedStyle(document.documentElement).getPropertyValue(name).trim();

export const reducedMotion = () =>
  matchMedia("(prefers-reduced-motion: reduce)").matches;

/** Réglages communs : animation, police, marges et infobulle sombre de la maquette. */
export function baseOption() {
  return {
    animation: !reducedMotion(),
    textStyle: { fontFamily: token("--font") },
    grid: { left: 8, right: 16, top: 32, bottom: 8, containLabel: true },
    tooltip: {
      trigger: "item",
      backgroundColor: token("--text"),
      borderWidth: 0,
      textStyle: { color: token("--on-primary"), fontSize: 12 },
    },
  };
}

/** Axe de valeurs gradué en français ; extra le complète ou le remplace. */
export function valueAxis(extra) {
  const muted = token("--muted");
  return {
    type: "value",
    nameTextStyle: { color: muted, fontSize: 12, align: "left" },
    splitLine: { lineStyle: { color: token("--border") } },
    axisLine: { lineStyle: { color: token("--border-strong") } },
    ...extra,
    axisLabel: {
      color: muted,
      fontSize: 12,
      formatter: (v) => nf.format(v),
      ...extra?.axisLabel,
    },
  };
}

/** Trace dans node et suit sa taille ; l'ancien graphique de la vue est libéré. */
export function mount(echarts, node, option) {
  const chart = echarts.init(node, null, { renderer: "svg" });
  chart.setOption(option);
  const observer = new ResizeObserver(() => {
    if (!node.isConnected) {
      observer.disconnect();
      chart.dispose();
    } else chart.resize();
  });
  observer.observe(node);
  return chart;
}
