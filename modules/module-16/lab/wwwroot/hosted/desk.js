"use strict";

const byId = id => document.getElementById(id);
const session = byId("session");
const signIn = byId("sign-in");
const signOut = byId("sign-out");
const desk = byId("desk");
const approve = byId("approve");
const ticket = byId("ticket");
const callback = byId("callback");
const receipt = byId("receipt");
let client, scopes, account, proposal;

function setAccount(value) {
  account = value;
  proposal = null;
  approve.hidden = true;
  desk.querySelectorAll("form").forEach(form => {
    form.reset();
    form.querySelector("output").textContent = "";
  });
  desk.disabled = !account;
  signOut.hidden = !account;
  signIn.hidden = Boolean(account);
  session.textContent = account ? "Signed in." : "Sign in to access your support tools.";
}

async function setup() {
  const response = await fetch("/hosted/config");
  if (!response.ok) throw new Error();
  const { tenantId, clientId, scope, receiptsEnabled } = await response.json();
  receipt.hidden = !receiptsEnabled;
  scopes = [scope];
  client = new msal.PublicClientApplication({
    auth: { clientId, authority: "https://login.microsoftonline.com/" + tenantId,
      redirectUri: location.origin + "/hosted/redirect.html" },
    cache: { cacheLocation: "memoryStorage" }
  });
  await client.initialize();
  setAccount(null);
  signIn.disabled = false;
}

signIn.onclick = async () => {
  signIn.disabled = true;
  try {
    const result = await client.loginPopup({ scopes, prompt: "select_account" });
    setAccount(result.account);
  } catch {
    session.textContent = "Sign-in did not complete. Check that pop-ups are allowed and try again.";
  } finally { signIn.disabled = false; }
};
signOut.onclick = async () => {
  const previous = account;
  setAccount(null);
  try { await client.logoutPopup({ account: previous, postLogoutRedirectUri: location.origin + "/hosted/redirect.html" }); }
  catch { session.textContent = "The support session cleared. Microsoft sign-out did not complete."; }
  finally { await client.clearCache({ account: previous }); }
};

const states = {
  denied: "This action is unavailable for your account.",
  missing: "No order was found for your account.",
  already_applied: "The callback has already been requested.",
  approval_expired: "The proposal expired. Prepare a new request.",
  retry_later: "The write could not be confirmed. Try again.",
  unsupported_image: "Choose a PNG or JPEG receipt image.",
  image_too_large: "Choose an image up to 100 KB.",
  invalid_receipt: "Use a clear image of a single receipt.",
  order_required: "Enter the order ID, for example ORD-1042.",
  receipt_review: "The receipt could not be matched to this order. Check the order ID and try a readable image.",
  unavailable: "A dependency is unavailable. Try again later.",
  applied: "Callback requested."
};

async function run(form, path, body, onResult, uploadHeaders) {
  const output = form.querySelector("output");
  const active = account;
  desk.disabled = true;
  signOut.disabled = true;
  try {
    const result = await client.acquireTokenSilent({ account: active, scopes });
    const reply = await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...uploadHeaders, Authorization: "Bearer " + result.accessToken },
      body: uploadHeaders ? body : JSON.stringify(body)
    });
    const correlation = reply.headers.get("X-Correlation-Id");
    const data = [401, 403].includes(reply.status) ? { status: "denied" } : await reply.json();
    const message = onResult?.(data) || states[data.status] || data.value || "The request could not be completed.";
    output.textContent = [message, data.citation && "Source: " + data.citation,
      correlation && "Correlation ID: " + correlation].filter(Boolean).join("\n");
  } catch (error) {
    if (error instanceof msal.InteractionRequiredAuthError) {
      setAccount(null);
      session.textContent = "Your session expired. Sign in again.";
    } else { output.textContent = "The request could not be completed. Try again."; }
  } finally {
    desk.disabled = !account;
    signOut.disabled = false;
  }
}

byId("answer").onsubmit = event => {
  event.preventDefault();
  void run(event.currentTarget, "/answer", { text: byId("question").value });
};
byId("order").onsubmit = event => {
  event.preventDefault();
  void run(event.currentTarget, "/order", { id: byId("order-id").value });
};
receipt.onsubmit = event => {
  event.preventDefault();
  const input = byId("receipt-file");
  const image = input.files[0];
  const output = receipt.querySelector("output");
  if (!image || !["image/png", "image/jpeg"].includes(image.type)) {
    output.textContent = states.unsupported_image;
    return;
  }
  if (image.size > 100000) {
    output.textContent = states.image_too_large;
    return;
  }
  output.textContent = "Checking receipt…";
  void run(receipt, "/attachment", image, () => { input.value = ""; }, {
    "Content-Type": image.type,
    "X-Order-Id": byId("receipt-order").value
  });
};
ticket.oninput = () => {
  proposal = null;
  approve.hidden = true;
  callback.querySelector("output").textContent = "";
};
callback.onsubmit = event => {
  event.preventDefault();
  proposal = null;
  approve.hidden = true;
  const id = ticket.value;
  void run(callback, "/propose", { ticket: id, action: "Request callback" }, data => {
    if (data.status !== "approval_required") return;
    proposal = data.value;
    approve.hidden = false;
    return "Review: request a callback for " + id + ". Approve to apply this change.";
  });
};
approve.onclick = () => {
  if (!proposal) return;
  void run(callback, "/approve", { proposal }, data => {
    if (["applied", "already_applied", "approval_expired", "denied"].includes(data.status)) {
      proposal = null;
      approve.hidden = true;
    }
  });
};

setup().catch(() => { session.textContent = "Sign-in is temporarily unavailable."; });
