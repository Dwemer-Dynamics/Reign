import { execFileSync, spawn } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const toolRoot = path.dirname(fileURLToPath(import.meta.url));
const workspaceRoot = path.resolve(toolRoot, "../../..");
const outputRoot = argumentValue("--output")
  ? path.resolve(argumentValue("--output"))
  : path.join(workspaceRoot, ".codex-build", "ui-preview", "codex-chat-e2e");
const previewBaseUrl = argumentValue("--url") || "http://127.0.0.1:5177/tools/GauntletXmlPreviewer/index.html";
const edgePath = argumentValue("--edge") || "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
const reportPath = path.join(outputRoot, "codex-chat-e2e.json");
const debugPort = 19000 + Math.floor(Math.random() * 5000);
const profileRoot = path.join(outputRoot, `edge-profile-${process.pid}-${Date.now()}`);
const receiptId = `REIGN_CODEX_CONTEXT_RECEIPT_${randomUUID()}`;
const errors = [];
const startedUtc = new Date().toISOString();
const startedAt = Date.now();
const guardedSourceStateBefore = guardedSourceState();
let browserEvidence = null;

fs.mkdirSync(outputRoot, { recursive: true });
fs.mkdirSync(profileRoot, { recursive: true });

class CdpClient {
  constructor(url) {
    this.url = url;
    this.socket = null;
    this.nextId = 0;
    this.pending = new Map();
  }

  async connect() {
    this.socket = new WebSocket(this.url);
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error("Edge DevTools WebSocket connection timed out.")), 5000);
      this.socket.addEventListener("open", () => { clearTimeout(timeout); resolve(); }, { once: true });
      this.socket.addEventListener("error", () => { clearTimeout(timeout); reject(new Error("Edge DevTools WebSocket connection failed.")); }, { once: true });
    });
    this.socket.addEventListener("message", (event) => {
      const message = JSON.parse(String(event.data));
      if (message.id == null) return;
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(message.error.message || "DevTools command failed."));
      else pending.resolve(message.result || {});
    });
  }

  send(method, params = {}) {
    const id = ++this.nextId;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.socket.send(JSON.stringify({ id, method, params }));
    });
  }

  close() {
    this.socket?.close();
  }
}

if (!fs.existsSync(edgePath)) throw new Error(`Microsoft Edge was not found at ${edgePath}. Pass --edge with its exact path.`);

const edge = spawn(edgePath, [
  "--headless=new",
  "--disable-gpu",
  "--hide-scrollbars",
  "--no-first-run",
  "--disable-features=msEdgeFirstRunExperience",
  `--remote-debugging-port=${debugPort}`,
  `--user-data-dir=${profileRoot}`,
  "about:blank"
], { stdio: "ignore", windowsHide: true });

try {
  await waitForEndpoint(`http://127.0.0.1:${debugPort}/json/version`, 12000);
  const target = await createTarget(debugPort);
  const cdp = new CdpClient(target.webSocketDebuggerUrl);
  await cdp.connect();
  await cdp.send("Page.enable");
  await cdp.send("Runtime.enable");
  await cdp.send("Emulation.setDeviceMetricsOverride", { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });

  const url = new URL(previewBaseUrl);
  url.searchParams.set("prefab", "ReignRoyalCouncilScreen");
  url.searchParams.set("viewport", "1920x1080");
  url.searchParams.set("uiScale", "1");
  url.searchParams.set("runtimeScale", "native");
  await cdp.send("Page.navigate", { url: url.href });
  const ready = await waitForState(cdp, (state) => (
    state?.fileName === "ReignRoyalCouncilScreen.xml"
      && state.renderedNodes > 0
      && state.codexConnected
      && state.threadId
      && !state.codexBusy
  ), 90000, "Royal Council preview and embedded Codex connection");

  const selection = await evaluate(cdp, `(() => {
    const api = window.__reignPreviewer;
    const title = api.renderer.renderedElements.find((element) => element.__gauntlet?.id === "RoyalCouncilTitle");
    if (!title) return { ok: false, error: "RoyalCouncilTitle was not rendered." };
    const path = title.__gauntlet.path;
    const selected = api.selectByPath(path);
    if (!selected) return { ok: false, error: "RoyalCouncilTitle could not be selected by its XML path." };
    const context = api.codexChat.getContext();
    const originalSend = api.codexChat.socket.send.bind(api.codexChat.socket);
    window.__reignCodexE2eOutbound = [];
    api.codexChat.socket.send = (payload) => {
      window.__reignCodexE2eOutbound.push(String(payload));
      return originalSend(payload);
    };
    return { ok: true, path, context };
  })()`);
  if (!selection?.ok) throw new Error(selection?.error || "The Royal Council title could not be selected.");

  const requestedReply = [
    "This is an automated transport audit. Do not edit files, install, deploy, launch Bannerlord, or run modification commands.",
    "Read the generated Current preview context in this turn and reply with exactly one line in this format:",
    `${receiptId} | file=ReignRoyalCouncilScreen.xml | tag=TextWidget | id=RoyalCouncilTitle | token=present | path=present | bindings=present | geometry=present | diagnostics=present | pending=present | install=present | deployment_guard=present`
  ].join(" ");
  await evaluate(cdp, `(() => {
    const prompt = document.getElementById("codexPrompt");
    prompt.value = ${JSON.stringify(requestedReply)};
    prompt.dispatchEvent(new Event("input", { bubbles: true }));
    document.getElementById("sendCodexPrompt").click();
    return true;
  })()`);

  const completed = await waitForState(cdp, (state) => (
    state?.codexConnected
      && !state.codexBusy
      && state.assistantMessages.some((message) => message.includes(receiptId))
  ), 240000, "embedded Codex no-op context receipt");
  const finalAssistantMessage = [...completed.assistantMessages].reverse().find((message) => message.includes(receiptId)) || "";
  const outbound = completed.outbound
    .map((payload) => safeJsonParse(payload))
    .filter(Boolean)
    .findLast((message) => message.method === "turn/start");
  const outboundPrompt = outbound?.params?.input?.find((item) => item.type === "text")?.text || "";
  const transmittedContext = parseTransmittedContext(outboundPrompt);
  const contextChecks = {
    schema: transmittedContext?.schema === "reign-ui-codex-selection-context-v1",
    file: transmittedContext?.fileName === "ReignRoyalCouncilScreen.xml",
    token: Boolean(transmittedContext?.selection?.token),
    path: transmittedContext?.selection?.path === selection.path,
    tag: transmittedContext?.selection?.tag === "TextWidget",
    id: transmittedContext?.selection?.id === "RoyalCouncilTitle",
    bindings: Array.isArray(transmittedContext?.selection?.bindings),
    geometry: finiteGeometry(transmittedContext?.selection?.renderedGeometry),
    diagnostics: Array.isArray(transmittedContext?.diagnostics) && Number.isInteger(transmittedContext?.diagnosticCount),
    pendingChanges: Object.hasOwn(transmittedContext || {}, "pendingXmlChanges"),
    installState: Boolean(transmittedContext?.installSync),
    deploymentGuard: outboundPrompt.includes("Do not install or deploy it.")
  };
  for (const [name, passed] of Object.entries(contextChecks)) {
    if (!passed) errors.push(`Outbound Codex context check failed: ${name}.`);
  }
  if (!outboundPrompt.includes(receiptId)) errors.push("The captured turn/start payload did not contain the unique receipt id.");
  for (const expected of [
    receiptId,
    "file=ReignRoyalCouncilScreen.xml",
    "tag=TextWidget",
    "id=RoyalCouncilTitle",
    "token=present",
    "path=present",
    "bindings=present",
    "geometry=present",
    "diagnostics=present",
    "pending=present",
    "install=present",
    "deployment_guard=present"
  ]) {
    if (!finalAssistantMessage.includes(expected)) errors.push(`Codex receipt omitted ${expected}.`);
  }

  browserEvidence = {
    previewUrl: url.href,
    threadId: completed.threadId || ready.threadId,
    selectedWidget: {
      fileName: transmittedContext?.fileName || "",
      token: transmittedContext?.selection?.token || "",
      path: transmittedContext?.selection?.path || "",
      line: transmittedContext?.selection?.line ?? null,
      tag: transmittedContext?.selection?.tag || "",
      id: transmittedContext?.selection?.id || "",
      bindings: transmittedContext?.selection?.bindings || [],
      renderedGeometry: transmittedContext?.selection?.renderedGeometry || null
    },
    contextChecks,
    outboundPromptSha256: sha256(outboundPrompt),
    assistantReceipt: finalAssistantMessage,
    assistantReceiptSha256: sha256(finalAssistantMessage),
    codexStatus: completed.codexStatus,
    outboundMessageCount: completed.outbound.length
  };
  cdp.close();
} catch (error) {
  errors.push(error?.stack || error?.message || String(error));
} finally {
  if (!edge.killed) edge.kill();
  if (edge.exitCode == null) {
    await Promise.race([
      new Promise((resolve) => edge.once("exit", resolve)),
      delay(3000)
    ]);
  }
  try {
    fs.rmSync(profileRoot, { recursive: true, force: true });
  } catch {
    // A lingering Edge helper can briefly retain its run-owned profile on Windows.
  }
}

const guardedSourceStateAfter = guardedSourceState();
const sourceUnchanged = guardedSourceStateBefore.sha256 === guardedSourceStateAfter.sha256;
if (!sourceUnchanged) errors.push("Guarded Reign source state changed during the no-op Codex turn.");
const report = {
  schema: "reign-ui-codex-chat-e2e-v1",
  startedUtc,
  completedUtc: new Date().toISOString(),
  durationMs: Date.now() - startedAt,
  previewBaseUrl,
  receiptId,
  ok: errors.length === 0,
  sourceUnchanged,
  guardedSourceStateBefore,
  guardedSourceStateAfter,
  browserEvidence,
  errors
};
fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
process.stdout.write(`${JSON.stringify({
  schema: report.schema,
  ok: report.ok,
  durationMs: report.durationMs,
  sourceUnchanged,
  threadId: browserEvidence?.threadId || "",
  selectedWidget: browserEvidence?.selectedWidget || null,
  contextChecks: browserEvidence?.contextChecks || {},
  assistantReceipt: browserEvidence?.assistantReceipt || "",
  reportPath,
  errors
}, null, 2)}\n`);
if (errors.length) process.exitCode = 1;

function argumentValue(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] || "" : "";
}

function guardedSourceState() {
  const scopes = [
    "ReignBeta/GUI/Prefabs",
    "ReignBeta/src",
    "ReignBeta/tools/GauntletXmlPreviewer",
    "ReignBetaServer",
    "ReignMcp",
    "docs/agent/TESTING_TOOL_GUIDE.md",
    "reign.testing.json"
  ];
  const workingDiff = runGit(["diff", "--binary", "--no-ext-diff", "--", ...scopes]);
  const stagedDiff = runGit(["diff", "--cached", "--binary", "--no-ext-diff", "--", ...scopes]);
  const untracked = runGit(["ls-files", "--others", "--exclude-standard", "--", ...scopes])
    .split(/\r?\n/)
    .map((value) => value.trim())
    .filter(Boolean)
    .sort();
  const hash = createHash("sha256");
  hash.update(workingDiff);
  hash.update("\0staged\0");
  hash.update(stagedDiff);
  hash.update("\0untracked\0");
  for (const relativePath of untracked) {
    hash.update(relativePath);
    hash.update("\0");
    const absolutePath = path.join(workspaceRoot, relativePath);
    if (fs.existsSync(absolutePath) && fs.statSync(absolutePath).isFile()) hash.update(fs.readFileSync(absolutePath));
    hash.update("\0");
  }
  return {
    sha256: hash.digest("hex"),
    workingDiffSha256: sha256(workingDiff),
    stagedDiffSha256: sha256(stagedDiff),
    untrackedPaths: untracked
  };
}

function runGit(args) {
  return execFileSync("git", args, {
    cwd: workspaceRoot,
    encoding: "utf8",
    maxBuffer: 128 * 1024 * 1024,
    windowsHide: true
  });
}

async function createTarget(port) {
  const response = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: "PUT" });
  if (!response.ok) throw new Error(`Edge target creation failed: ${response.status} ${response.statusText}`);
  return response.json();
}

async function waitForEndpoint(url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url, { cache: "no-store" });
      if (response.ok) return response.json();
      lastError = new Error(`${response.status} ${response.statusText}`);
    } catch (error) {
      lastError = error;
    }
    await delay(100);
  }
  throw new Error(`Edge DevTools did not become ready: ${lastError?.message || "timeout"}`);
}

async function waitForState(cdp, predicate, timeoutMs, label) {
  const deadline = Date.now() + timeoutMs;
  let lastState = null;
  while (Date.now() < deadline) {
    try {
      lastState = await evaluate(cdp, `(() => {
        const api = window.__reignPreviewer;
        if (!api) return null;
        const chat = api.codexChat;
        return {
          ...api.getState(),
          codexConnected: Boolean(chat.connected),
          codexBusy: Boolean(chat.busy),
          threadId: chat.threadId || "",
          codexStatus: document.getElementById("codexConnectionStatus")?.textContent?.trim() || "",
          assistantMessages: [...document.querySelectorAll("#codexTranscript .codex-message.assistant")].map((node) => node.textContent || ""),
          outbound: [...(window.__reignCodexE2eOutbound || [])]
        };
      })()`);
      if (predicate(lastState)) return lastState;
    } catch {
      // Page navigation replaces the execution context; retry against the new one.
    }
    await delay(200);
  }
  throw new Error(`Timed out waiting for ${label}; last state: ${JSON.stringify(lastState)}`);
}

async function evaluate(cdp, expression) {
  const response = await cdp.send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description || response.exceptionDetails.text || "Browser evaluation failed.");
  return response.result?.value ?? null;
}

function parseTransmittedContext(prompt) {
  const prefix = "Current preview context (generated by the previewer):\n";
  const suffix = "\n\nImplement this request in the workspace.";
  const start = prompt.indexOf(prefix);
  const end = prompt.lastIndexOf(suffix);
  if (start < 0 || end <= start) return null;
  return safeJsonParse(prompt.slice(start + prefix.length, end));
}

function safeJsonParse(value) {
  try { return JSON.parse(String(value)); } catch { return null; }
}

function finiteGeometry(value) {
  return value != null && [value.x, value.y, value.width, value.height].every(Number.isFinite) && value.width > 0 && value.height > 0;
}

function sha256(value) {
  return createHash("sha256").update(String(value || ""), "utf8").digest("hex");
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
