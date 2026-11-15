import assert from "node:assert/strict";

class Element {
  value = "";
  textContent = "";
  dataset = {};
  children = [];
  attrs = {};
  replaceChildren(...children) { this.children = children; }
  setAttribute(name, value) { this.attrs[name] = value; }
}

const ids = new Map(["turns","send","cancel","reset","prompt","preview","status","scenario","mode","simulation"]
  .map(id=>[id,new Element()]));
ids.get("scenario").value = "normal";
const buttons = ["A","B"].map(session=>Object.assign(new Element(),{dataset:{session}}));
const form = new Element();
globalThis.document = {
  getElementById:id=>ids.get(id),
  querySelectorAll:selector=>selector==="[data-session]" ? buttons : [],
  querySelector:selector=>selector==="form" ? form : null,
  createElement:()=>new Element()
};
const fetchOriginal = globalThis.fetch;
globalThis.fetch = (path,opts) => fetchOriginal(new URL(path,"http://127.0.0.1:8407"),opts);
await import("./wwwroot/chat.js");
assert.equal(ids.get("mode").textContent,"Local stub");
assert.equal(ids.get("simulation").hidden,false);

function submit(value, scenario="normal") {
  ids.get("prompt").value = value;
  ids.get("scenario").value = scenario;
  return form.onsubmit({preventDefault(){}});
}
const getTurns = () => ids.get("turns").children.map(row=>row.textContent);
await submit("Where is order A-103?");
assert.equal(getTurns().length,2);
assert.match(getTurns()[1],/A-103/);
buttons[1].onclick();
assert.deepEqual(getTurns(),[]);
await submit("Where is order B-204?");
assert.match(getTurns()[1],/B-204/);
assert.doesNotMatch(getTurns()[1],/A-103/);
buttons[0].onclick();
const saved = getTurns();
assert.match(saved[1],/A-103/);
const pending = submit("Can you check again?");
assert.equal(ids.get("send").disabled,true);
assert.equal(ids.get("cancel").disabled,false);
for(let i=0; !ids.get("preview").textContent && i<80; i++)
  await new Promise(resolve=>setTimeout(resolve,50));
assert.equal(ids.get("status").textContent,"Receiving reply");
const observedPreview = ids.get("preview").textContent;
ids.get("cancel").onclick();
await pending;
assert.match(ids.get("status").textContent,/Cancelled/);
assert.deepEqual(getTurns(),saved);
assert.equal(ids.get("preview").textContent,"");
await submit("Can you check again?","throttle");
assert.match(ids.get("status").textContent,/The service is busy/);
assert.deepEqual(getTurns(),saved);
await submit("Can you check again?","timeout");
assert.match(ids.get("status").textContent,/The request timed out/);
assert.deepEqual(getTurns(),saved);
console.log(JSON.stringify({result:"PASS",scope:"Actual chat.js with Node DOM fixture and live local ASP.NET server",
  scenarios:["session switching","independent histories","pending controls","cancel","simulated throttling","simulated timeout"],
  observedPreview, sessionA:saved},null,2));
