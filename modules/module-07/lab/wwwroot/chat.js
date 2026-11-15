const sessions = {A: [], B: []};
let active = "A";
let current;
const byId = id => document.getElementById(id);
const buttons = [...document.querySelectorAll("[data-session]")];
const status = text => byId("status").textContent = text;

function show() {
  byId("turns").replaceChildren(...sessions[active].map(turn => {
    const row = document.createElement("p");
    row.dataset.role = turn.role;
    row.textContent = (turn.role === "user" ? "You: " : "Assistant: ") + turn.content;
    return row;
  }));
  for (const button of buttons) {
    button.setAttribute("aria-pressed", String(button.dataset.session === active));
    button.disabled = Boolean(current);
  }
  byId("send").disabled = Boolean(current) || sessions[active].length >= 14;
  byId("cancel").disabled = !current;
  byId("reset").disabled = Boolean(current);
}

for (const button of buttons) button.onclick = () => {
  active = button.dataset.session;
  status("Ready");
  show();
};
byId("reset").onclick = () => {
  sessions[active] = [];
  status("Ready");
  show();
};
byId("cancel").onclick = () => current?.abort();

async function* events(response) {
  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let pending = "";
  try {
    while (true) {
      const {value, done} = await reader.read();
      if (done) return;
      pending += value;
      const lines = pending.split("\n");
      pending = lines.pop();
      for (const line of lines) if (line) yield JSON.parse(line);
    }
  } finally {
    await reader.cancel();
    reader.releaseLock();
  }
}

document.querySelector("form").onsubmit = async event => {
  event.preventDefault();
  if (current || sessions[active].length >= 14) return;
  const input = byId("prompt").value.trim();
  if (!input) return;
  const controller = new AbortController();
  const history = sessions[active];
  const userTurn = {role:"user", content:input};
  current = controller;
  let reply = "";
  let correlation = "";
  status("Waiting for a reply");
  show();
  try {
    const response = await fetch("/chat", {
      method:"POST",
      headers:{"content-type":"application/json"},
      body:JSON.stringify({messages:[...history,userTurn],scenario:byId("scenario").value}),
      signal:controller.signal
    });
    if (response.status === 429) throw new Error("Application limit reached. Try again in a minute.");
    if (!response.ok) throw new Error("Request rejected.");
    correlation = response.headers.get("X-Correlation-Id") ?? "";
    let done = false;
    for await (const event of events(response)) {
      if (event.kind === "error") throw new Error(event.text);
      if (event.kind === "delta") {
        reply += event.text;
        byId("preview").textContent = reply;
        status("Receiving reply");
      }
      if (event.kind === "done") done = true;
    }
    if (!done || !reply.trim()) throw new Error("Reply was incomplete.");
    history.push(userTurn,{role:"assistant",content:reply});
    byId("prompt").value = "";
    status(history.length >= 14 ? "Turn limit reached. Clear this session to continue." : "Reply complete");
  } catch (error) {
    status(controller.signal.aborted ? "Cancelled. Try again when ready." :
      error.message + (correlation ? " Request " + correlation : ""));
  } finally {
    byId("preview").textContent = "";
    current = undefined;
    show();
  }
};

const mode = await fetch("/mode").then(response => response.json());
byId("mode").textContent = mode.mode;
byId("simulation").hidden = mode.mode !== "Local stub";
show();
