// Point d'entrée de l'interface : tout passe par fetch sur la même origine (https://keystats.app/).
// Hors WebView2, ouvrir la page avec un serveur de fichiers et un dossier api/ d'exemple.

async function loadStatus() {
  const target = document.getElementById("status");
  try {
    const response = await fetch("/api/status");
    if (!response.ok) throw new Error(String(response.status));
    const status = await response.json();
    const setup = status.setup
      ? "Chiffrement en place."
      : "Chiffrement pas encore configuré : seules les statistiques sont enregistrées.";
    const agent = status.agentRunning ? "Agent en marche." : "Agent arrêté.";
    target.textContent = `${setup} ${agent}`;
  } catch {
    target.textContent = "État illisible.";
  }
}

loadStatus();
