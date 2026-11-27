// ECharts, servi depuis vendor/ (aucun CDN). Le fichier n'est chargé qu'à la première vue qui trace un graphique.

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
