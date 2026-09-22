import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

const arg = (name) => process.argv[process.argv.indexOf(name) + 1];
assert.ok(process.argv.includes("--output"), "Pass --output with a task-owned evidence directory.");
const output = path.resolve(arg("--output"));
const edgePath = process.argv.includes("--edge") ? arg("--edge") : "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const baseUrl = process.argv.includes("--url") ? arg("--url") : "http://127.0.0.1:5177/tools/GauntletXmlPreviewer/index.html?codex=off";
assert.ok(["127.0.0.1", "localhost"].includes(new URL(baseUrl).hostname), "Use the provider-free local preview server.");
fs.mkdirSync(output, { recursive: true });
const port = 24000 + Math.floor(Math.random() * 4000);
const profile = path.join(output, `edge-profile-${process.pid}-${Date.now()}`);
const edge = spawn(edgePath, ["--headless=new", "--disable-gpu", "--no-first-run", "--disable-features=msEdgeFirstRunExperience",
  `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, "about:blank"], { stdio: "ignore", windowsHide: true });
let socket;
let report;
try {
  let target;
  const deadline = Date.now() + 15000;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`http://127.0.0.1:${port}/json/list`);
      target = (await response.json()).find(item => item.type === "page");
      if (target) break;
    } catch { /* The dedicated headless browser is still starting. */ }
    await new Promise(resolve => setTimeout(resolve, 150));
  }
  assert.ok(target, "Dedicated headless Edge did not start.");
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    socket.addEventListener("open", resolve, { once: true });
    socket.addEventListener("error", reject, { once: true });
  });
  let sequence = 0;
  const pending = new Map();
  socket.addEventListener("message", event => {
    const message = JSON.parse(String(event.data));
    const request = pending.get(message.id);
    if (!request) return;
    pending.delete(message.id);
    clearTimeout(request.timeout);
    if (message.error) request.reject(new Error(message.error.message));
    else request.resolve(message.result);
  });
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence;
    const timeout = setTimeout(() => { pending.delete(id); reject(new Error(`DevTools timed out: ${method}`)); }, 20000);
    pending.set(id, { resolve, reject, timeout });
    socket.send(JSON.stringify({ id, method, params }));
  });
  await send("Page.enable");
  await send("Page.navigate", { url: baseUrl });
  for (let attempt = 0; attempt < 100; attempt++) {
    const response = await send("Runtime.evaluate", { expression: "document.readyState === 'complete'", returnByValue: true });
    if (response.result?.value) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  const response = await send("Runtime.evaluate", {
    awaitPromise: true, returnByValue: true,
    expression: `(async () => {
      await document.fonts.ready;
      const { GauntletRenderer } = await import('./js/renderer.js');
      const stage = document.createElement('div');
      stage.style.cssText = 'position:fixed;left:0;top:0;width:1000px;height:1000px;';
      document.body.appendChild(stage);
      const renderer = new GauntletRenderer(stage);
      const plain = 'There are stories enough for a long evening, from the busy market to the travelers who arrive with each caravan. Tell us which part of the journey you remember most.';
      const samples = [];
      for (const width of [240, 420]) {
        for (const kind of ['TextWidget', 'RichTextWidget']) {
          for (const formatted of [false, true]) {
            const text = formatted && kind === 'RichTextWidget' ? '<span style="Action">A quiet smile accompanies the reply.</span> ' + plain : plain;
            const offset = formatted && kind === 'TextWidget' ? ' PositionXOffset="3" PositionYOffset="2"' : '';
            const xml = '<Prefab><Window><Widget WidthSizePolicy="Fixed" HeightSizePolicy="CoverChildren" SuggestedWidth="' + width + '"><Children><' + kind + ' WidthSizePolicy="StretchToParent" HeightSizePolicy="CoverChildren" Brush="Reign.Chat.ActionText.17" Brush.Font="ReignSerifDynamic" Brush.FontSize="17" Text="@Body"' + offset + ' /></Children></Widget></Window></Prefab>';
            renderer.render(xml, 'MultilineContract.xml', { Body: text });
            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            const element = stage.querySelector('.text');
            const range = document.createRange(); range.selectNodeContents(element);
            const textBounds = range.getBoundingClientRect();
            const bounds = element.getBoundingClientRect();
            samples.push({ kind, formatted, width, height: bounds.height, textHeight: textBounds.height,
              positionYOffset: Number(element.dataset.positionYOffset || 0),
              scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
              parentHeight: element.parentElement.getBoundingClientRect().height,
              italicCount: element.querySelectorAll('em').length });
          }
        }
      }
      stage.remove();
      return samples;
    })()`
  });
  assert.ok(!response.exceptionDetails, JSON.stringify(response.exceptionDetails));
  const samples = response.result.value;
  report = { schema: "reign-ui-multiline-cover-children-contract-v1", generatedUtc: new Date().toISOString(), samples,
    passed: samples.length === 8 && samples.every(sample => sample.height > 35
      && sample.positionYOffset === (sample.formatted && sample.kind === "TextWidget" ? 2 : 0)
      && sample.scrollHeight <= sample.clientHeight + sample.positionYOffset + 1 && sample.textHeight <= sample.height + 1
      && sample.parentHeight >= sample.height - 1 && sample.italicCount === (sample.formatted && sample.kind === "RichTextWidget" ? 1 : 0)) };
  fs.writeFileSync(path.join(output, "multiline-cover-children-contract.json"), JSON.stringify(report, null, 2) + "\n");
  assert.equal(report.passed, true, JSON.stringify(report));
  console.log(JSON.stringify({ passed: true, cases: samples.length, output }));
} finally {
  socket?.close();
  edge.kill();
}
