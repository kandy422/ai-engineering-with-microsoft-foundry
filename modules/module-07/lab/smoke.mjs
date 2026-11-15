import assert from "node:assert/strict";
import {performance} from "node:perf_hooks";

const base = process.env.LAB_URL ?? "http://127.0.0.1:8407";
const mode = await fetch(base + "/mode").then(r => r.json());
assert.equal(mode.mode, "Local stub");
const traces = [];
async function chat(messages, scenario = "normal", signal) {
  return fetch(base + "/chat", {
    method:"POST", headers:{"content-type":"application/json"}, signal,
    body:JSON.stringify({messages, scenario})
  });
}
async function collect(label, messages, scenario) {
  const start = performance.now();
  const response = await chat(messages, scenario);
  assert.equal(response.status, 200);
  const events = (await response.text()).trim().split("\n").map(JSON.parse);
  const id = response.headers.get("x-correlation-id");
  assert.equal(events[0].text, id);
  traces.push({label, messages, scenario, id, elapsedMs:Math.round(performance.now()-start), events});
  return events;
}
const a = [{role:"user",content:"Where is order A-103?"}];
const b = [{role:"user",content:"Where is order B-204?"}];
const first = await collect("session A first turn",a);
const second = await collect("session B first turn",b);
const reply = events => events.filter(e => e.kind === "delta").map(e => e.text).join("");
assert.match(reply(first), /A-103/);
assert.doesNotMatch(reply(second), /A-103/);
assert.match(reply(second), /B-204/);
assert.equal(first.at(-1).kind, "done");
const follow = [...a,{role:"assistant",content:reply(first)},{role:"user",content:"Can you check again?"}];
assert.match(reply(await collect("session A follow-up",follow)), /A-103/);
assert.equal((await collect("simulated throttling",a,"throttle")).at(-1).text,"The service is busy. Try again shortly.");
assert.equal((await collect("simulated timeout",a,"timeout")).at(-1).text,"The request timed out. Try again.");
const controller = new AbortController();
const cancelled = await chat(a,"normal",controller.signal);
const reader = cancelled.body.getReader();
let chunks = "";
while (!chunks.includes('"kind":"delta"')) {
  const {done,value} = await reader.read();
  assert.equal(done,false);
  chunks += new TextDecoder().decode(value);
}
controller.abort();
await assert.rejects(reader.read(), {name:"AbortError"});
traces.push({label:"cancel after first delta", id:cancelled.headers.get("x-correlation-id"), beforeCancel:chunks, result:"AbortError"});
assert.equal((await chat([{role:"assistant",content:"invalid order"}])).status,400);
traces.push({label:"invalid message order",status:400});
for (let i=0;i<5;i++) assert.equal((await chat([{role:"assistant",content:"invalid"}])).status,400);
assert.equal((await chat(a)).status,429);
traces.push({label:"application limit",status:429,providerCalled:false});
console.log(JSON.stringify({mode:mode.mode,result:"PASS",traces},null,2));
