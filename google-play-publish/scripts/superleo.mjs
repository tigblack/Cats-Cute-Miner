#!/usr/bin/env node
// Dependency-free client for Superleo's stateless, JSON-response MCP endpoint.
// Adapted from the studio's superleo-monetization client; no dependency on that folder.
import { readFileSync, writeFileSync, existsSync, mkdirSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

export function loadConfig(read = readFileSync, cwd = process.cwd(), moduleUrl = import.meta.url, explicitPath) {
  const primary = fileURLToPath(new URL("../assets/.env", moduleUrl));
  const supplied = fileURLToPath(new URL("../assets/.env.superleo", moduleUrl));
  const perGame = resolve(cwd, "release/google-play/superleo.local.env");
  const legacy = resolve(cwd, ".env.superleo");
  const paths = explicitPath ? [resolve(cwd, explicitPath)] : [primary, supplied, perGame, legacy];
  for (const path of paths) {
    let text;
    try {
      text = read(path, "utf8");
    }
    catch (error) {
      if (error.code === "ENOENT" && !explicitPath) continue;
      throw new Error("Cannot read Superleo config file; check file permissions.");
    }
    // An existing but invalid primary file must never select another identity.
    return parseConfig(text);
  }
  throw new Error(
    "No personal MCP key is bundled. When MCP is not configured, use the supplied panel in the browser under the current employee's account; browser workflow does not require this key. For an already issued personal MCP key: Create assets/.env from assets/.env.superleo.example, or pass --config <private-file>.",
  );
}

export function parseConfig(text) {
  const values = {};
  for (const line of text.replace(/^\uFEFF/, "").split(/\r?\n/)) {
    if (!line.trim() || line.trim().startsWith("#")) continue;
    const match = line.match(/^\s*(SUPERLEO_URL|SUPERLEO_API_KEY)\s*=\s*(.*?)\s*$/);
    if (!match || Object.hasOwn(values, match[1])) throw new Error("Invalid or duplicate Superleo config setting");
    let value = match[2];
    if (/^["']/.test(value)) {
      if (value.at(-1) !== value[0]) throw new Error("Unclosed quote in Superleo config");
      value = value.slice(1, -1);
    }
    values[match[1]] = value;
  }
  let url;
  try {
    url = new URL(values.SUPERLEO_URL);
  }
  catch {
    throw new Error("Invalid SUPERLEO_URL");
  }
  const local = ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname);
  if (
    (url.protocol !== "https:" && !(local && url.protocol === "http:"))
    || url.username || url.password || url.search || url.hash || url.pathname !== "/mcp"
  ) {
    throw new Error("SUPERLEO_URL must be HTTPS /mcp (HTTP allowed only on loopback)");
  }
  const key = values.SUPERLEO_API_KEY;
  if (!key || !/^slmcp_[A-Za-z0-9_-]+$/.test(key)) {
    throw new Error("Set a personal SuperLeo MCP key in the selected config file");
  }
  return { url: url.href, key };
}

export async function callTool(config, name, args = {}, fetchImpl = fetch) {
  if (!["superleo_access", "superleo_monetization_catalog", "superleo_monetization"].includes(name)) {
    throw new Error("Unsupported tool");
  }
  if (!args || typeof args !== "object" || Array.isArray(args)) throw new Error("Arguments must be a JSON object");
  let version = "2025-03-26";
  async function rpc(id, method, params) {
    let response;
    try {
      response = await fetchImpl(config.url, {
        method: "POST",
        redirect: "error",
        // Only execution may need the browser worker's 10-minute allowance.
        signal: AbortSignal.timeout(method === "tools/call" ? 12 * 60 * 1000 : 30 * 1000),
        headers: {
          Authorization: `Bearer ${config.key}`,
          "Content-Type": "application/json",
          Accept: "application/json, text/event-stream",
          "MCP-Protocol-Version": version,
        },
        body: JSON.stringify({ jsonrpc: "2.0", ...(id === undefined ? {} : { id }), method, params }),
      });
    }
    catch {
      throw new Error(
        "Connection failed or timed out; no automatic retry. Read current state before repeating a write.",
      );
    }
    if (!response.ok) {
      throw new Error(`Superleo HTTP ${response.status}; check deployment, key and permissions. No automatic retry.`);
    }
    if (id === undefined) {
      await response.body?.cancel();
      return;
    }
    let message;
    try {
      message = await response.json();
    }
    catch {
      throw new Error("Expected a Superleo JSON MCP response");
    }
    if (message.id !== id || message.error || !message.result) {
      throw new Error("Superleo MCP request failed or returned an invalid response");
    }
    return message.result;
  }
  const init = await rpc(1, "initialize", {
    protocolVersion: version,
    capabilities: {},
    clientInfo: { name: "google-play-publish-skill", version: "2.0.0" },
  });
  if (typeof init.protocolVersion !== "string") throw new Error("Missing MCP protocol version");
  version = init.protocolVersion;
  await rpc(undefined, "notifications/initialized", {});
  return rpc(2, "tools/call", { name, arguments: args });
}

export function parseCli(argv) {
  const positional = [], options = {};
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (["--config", "--private-output"].includes(arg)) {
      if (Object.hasOwn(options, arg) || !argv[i + 1] || argv[i + 1].startsWith("--")) {
        throw new Error("Missing or duplicate CLI option");
      }
      options[arg] = argv[++i];
    } else if (arg.startsWith("--")) throw new Error("Unknown CLI option");
    else positional.push(arg);
  }
  const [command, argsFile, ...extra] = positional;
  const names = { access: "superleo_access", catalog: "superleo_monetization_catalog", call: "superleo_monetization" };
  if (!names[command] || extra.length || (command === "call" ? !argsFile : argsFile)) {
    throw new Error("Usage: node <skill>/scripts/superleo.mjs access|catalog|call <request.json> [--config <private-file>] [--private-output <new-ignored-file>]");
  }
  return { command, argsFile, tool: names[command], configPath: options["--config"], outputPath: options["--private-output"] };
}

export function businessFailed(result) {
  if (result?.isError) return true;
  function visit(value) {
    if (!value || typeof value !== "object") return false;
    if (value.success === false || value.ok === false) return true;
    return Object.values(value).some(visit);
  }
  if (visit(result?.structuredContent)) return true;
  return (result?.content || []).some(block => {
    if (block.type !== "text") return false;
    try { return visit(JSON.parse(block.text)); } catch { return false; }
  });
}

export function redactResult(value, knownSecrets = []) {
  function clean(item) {
    if (typeof item === "string") {
      let safe = item;
      for (const secret of knownSecrets.filter(Boolean)) safe = safe.split(secret).join("[REDACTED]");
      safe = safe.replace(/\bslmcp_[A-Za-z0-9_-]+\b/g, "[REDACTED]");
      try { return JSON.stringify(clean(JSON.parse(safe))); } catch { return safe; }
    }
    if (Array.isArray(item)) return item.map(clean);
    if (!item || typeof item !== "object") return item;
    return Object.fromEntries(Object.entries(item).map(([key, field]) => {
      const normalized = key.toLowerCase().replace(/[^a-z0-9]/g, "");
      const privateField = /token$|secret$|password|apikey|sdkkey|authorization|cookie|credential|privatekey|receipt/.test(normalized);
      return [key, privateField && field != null ? "[REDACTED]" : clean(field)];
    }));
  }
  return clean(value);
}

export async function main(argv = process.argv.slice(2), dependencies = {}) {
  const options = parseCli(argv);
  const read = dependencies.read || readFileSync;
  const output = dependencies.stdout || (text => process.stdout.write(text));
  const config = loadConfig(read, dependencies.cwd || process.cwd(), import.meta.url, options.configPath);
  let args = {};
  if (options.argsFile) {
    try {
      args = JSON.parse(read(resolve(dependencies.cwd || process.cwd(), options.argsFile), "utf8"));
    }
    catch {
      throw new Error("Cannot read request JSON");
    }
  }
  const privatePath = options.outputPath ? resolve(dependencies.cwd || process.cwd(), options.outputPath) : null;
  if (privatePath) {
    if (existsSync(privatePath)) throw new Error("Private output already exists; use a new path. No remote request was sent.");
    mkdirSync(dirname(privatePath), { recursive: true });
  }
  const result = await (dependencies.call || callTool)(config, options.tool, args);
  if (privatePath) {
    try { writeFileSync(privatePath, JSON.stringify(result, null, 2) + "\n", { mode: 0o600, flag: "wx" }); }
    catch { throw new Error("Could not save private response. Remote action may have succeeded; inspect current state before repeating it."); }
    output(JSON.stringify({ privateOutput: privatePath, failed: businessFailed(result) }) + "\n");
  } else output(JSON.stringify(redactResult(result, [config.key]), null, 2) + "\n");
  return businessFailed(result) ? 1 : 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main().then(code => { process.exitCode = code; }).catch(error => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
