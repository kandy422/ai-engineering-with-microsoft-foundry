import assert from "node:assert/strict";

const base = "http://127.0.0.1:8407";
assert.equal((await fetch(base + "/mode").then(r => r.json())).mode, "Local stub");
const input = {messages: [{role: "user", content: "Where is order A-103?"}], scenario: "normal"};
const send = body => fetch(base + "/chat", {
  method: "POST", headers: {"content-type": "application/json"}, body: JSON.stringify(body)
});
const read = async response => (await response.text()).trim().split("\n").filter(Boolean).map(JSON.parse);
const mode = process.argv[2];

if (mode === "limit") {
  const limit = Number(process.argv[3]);
  const statuses = [];
  for (let i = 0; i < 3; i++) {
    const response = await send(input);
    statuses.push(response.status);
    if (response.status === 200) assert.equal((await read(response)).at(-1).kind, "done");
    else await response.text();
  }
  assert.deepEqual(statuses, Array.from({length: 3}, (_, i) => i < limit ? 200 : 429));
  console.log(JSON.stringify({limit, input, statuses, result: "PASS"}, null, 2));
} else if (mode === "stream") {
  const cases = {};
  for (const scenario of ["normal", "throttle", "timeout"]) {
    const response = await send({...input, scenario});
    assert.equal(response.status, 200);
    cases[scenario] = {id: response.headers.get("X-Correlation-Id"), events: await read(response)};
  }
  assert.deepEqual(Object.values(cases).map(v => v.events.at(-1).kind), ["done", "error", "error"]);
  console.log(JSON.stringify({input, cases, result: "PASS"}, null, 2));
} else {
  throw new Error("Use limit or stream.");
}
