// Point d'entrée de l'interface : tout passe par fetch sur la même origine (https://keystats.app/).
// Hors WebView2, ouvrir la page avec un serveur de fichiers et un dossier api/ d'exemple.

const $ = (id) => document.getElementById(id);

async function getJson(path, init) {
  const response = await fetch(path, init);
  if (!response.ok) throw new Error(String(response.status));
  return response.json();
}

async function loadStatus() {
  try {
    const status = await getJson("/api/status");
    const setup = status.setup
      ? "Chiffrement en place."
      : "Chiffrement pas encore configuré : seules les statistiques sont enregistrées.";
    const agent = status.agentRunning ? "Agent en marche." : "Agent arrêté.";
    $("status").textContent = `${setup} ${agent}`;
  } catch {
    $("status").textContent = "État illisible.";
  }
}

function showKey(state) {
  $("key").hidden = !state.setup;
  $("key-state").textContent = state.unlocked
    ? "Déverrouillées pour cette fenêtre : la clé est effacée à sa fermeture."
    : "Verrouillées : la vue à la milliseconde, les titres de fenêtre et le recalcul demandent la clé.";
  $("unlock").hidden = state.unlocked;
  $("hello").hidden = !state.hello;
  $("phrase-form").hidden = !state.passphrase;
  $("lock").hidden = !state.unlocked;
}

async function loadKey() {
  try {
    showKey(await getJson("/api/key"));
  } catch {
    $("key").hidden = true;
  }
}

// Une seule demande à la fois : un double clic lancerait deux invites Windows Hello.
function setBusy(busy) {
  for (const control of $("key").querySelectorAll("button, input")) {
    control.disabled = busy;
  }
}

async function post(path, body, failure) {
  $("key-message").textContent = "";
  setBusy(true);
  try {
    const state = await getJson(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    showKey(state);
    if (!state.unlocked && failure) $("key-message").textContent = failure;
  } catch {
    $("key-message").textContent = "La demande a échoué.";
  } finally {
    setBusy(false);
  }
}

$("hello").addEventListener("click", () =>
  post(
    "/api/key/unlock",
    { method: "hello" },
    "Windows Hello n'a pas déverrouillé les données.",
  ),
);

$("phrase-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const input = $("phrase");
  const passphrase = input.value;
  input.value = "";
  await post(
    "/api/key/unlock",
    { method: "passphrase", passphrase },
    "Phrase de secours incorrecte.",
  );
});

$("lock").addEventListener("click", () => post("/api/key/lock", {}, null));

loadStatus();
loadKey();
