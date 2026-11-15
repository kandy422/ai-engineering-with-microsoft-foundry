const customer = "customer-a";
const approve = document.querySelector("#approve");
const ticketResult = document.querySelector("#ticket-result");
let proposal = null;

async function post(route, body) {
  const response = await fetch(route, {
    method: "POST",
    headers: {"Content-Type": "application/json"},
    body: JSON.stringify(body),
  });
  if (!response.ok) throw new Error("Request failed");
  return response.json();
}

const messages = {
  clarify: "Enter a question about the selected topic.",
  no_evidence: "No policy is available for this question.",
  conflicting_evidence: "The policies disagree. Contact support for clarification.",
  unavailable: "The policy service is unavailable. Try again later.",
  denied: "This record is unavailable for your account.",
  missing: "We could not find that order.",
  applied: "Callback requested.",
  already_applied: "The callback was already requested.",
};

function handleForm(form, result, submit) {
  form.addEventListener("submit", async event => {
    event.preventDefault();
    const button = form.querySelector("button");
    button.disabled = true;
    result.textContent = "Working…";
    try {
      await submit(Object.fromEntries(new FormData(form)));
    } catch {
      result.textContent = "Could not reach the support service. Try again.";
    } finally {
      button.disabled = false;
    }
  });
}

const policyResult = document.querySelector("#policy-result");
handleForm(document.querySelector("#policy"), policyResult, async input => {
  const reply = await post("/answer", input);
  policyResult.textContent = reply.status === "answered"
    ? `${reply.value} Source: ${reply.citation}.`
    : messages[reply.status] ?? "No answer is available.";
});

const orderResult = document.querySelector("#order-result");
handleForm(document.querySelector("#order"), orderResult, async input => {
  const reply = await post("/order", {...input, customer});
  orderResult.textContent = reply.status === "found"
    ? `Order ${input.id}: ${reply.value}.`
    : messages[reply.status] ?? "No order status is available.";
});

const ticketForm = document.querySelector("#ticket");
handleForm(ticketForm, ticketResult, async input => {
  proposal = null;
  approve.disabled = true;
  const reply = await post("/propose", {...input, customer, action: "Request callback"});
  if (reply.status === "approval_required") {
    proposal = reply.value;
    approve.disabled = false;
    ticketResult.textContent = `Review: Request a callback on ticket ${input.ticket}.`;
  } else {
    ticketResult.textContent = messages[reply.status] ?? "Could not prepare a callback request.";
  }
});

approve.addEventListener("click", async () => {
  if (!proposal) return;
  approve.disabled = true;
  const review = ticketForm.querySelector("button");
  review.disabled = true;
  try {
    const reply = await post("/approve", {customer, proposal});
    proposal = null;
    ticketResult.textContent = messages[reply.status] ?? "The callback could not be approved.";
  } catch {
    approve.disabled = false;
    ticketResult.textContent = "Approval could not be confirmed. You can retry this approval.";
  } finally {
    review.disabled = false;
  }
});

const attachmentResult = document.querySelector("#attachment-result");
handleForm(document.querySelector("#attachment"), attachmentResult, async ({file}) => {
  const reply = await post("/attachment-check", {mime:file.type, task:"extract", bytes:file.size});
  attachmentResult.textContent = reply.state === "routed"
    ? "Eligible for receipt extraction. This check only sent the file type and size."
    : `Attachment rejected: ${reply.target}.`;
});
