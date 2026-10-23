#!/usr/bin/env node
// Dependency-free client for Superleo's stateless, JSON-response MCP endpoint.
import { readFileSync } from "node:fs";
import { hostname } from "node:os";
import { resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

// Attribution only, never put these values into tool arguments.email (credential owner).
export function operatorHeaders(device = hostname(), email = process.env.SUPERLEO_OPERATOR_EMAIL || "") {
  if (email && (email.length > 160 || !/^[A-Za-z0-9._+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$/.test(email))) {
    throw new Error("SUPERLEO_OPERATOR_EMAIL must be an email address (audit attribution only)");
  }
  return {
    "X-Superleo-Device": device.replace(/[^A-Za-z0-9._-]/g, "_").slice(0, 160) || "unknown",
    ...(email ? { "X-Superleo-Operator-Email": email } : {}),
  };
}

export function loadConfig(read = readFileSync, cwd = process.cwd(), moduleUrl = import.meta.url) {
  const primary = fileURLToPath(new URL("../assets/.env", moduleUrl));
  const skillConfig = fileURLToPath(new URL("../assets/.env.superleo", moduleUrl));
  const legacy = resolve(cwd, ".env.superleo");
  for (const path of [primary, skillConfig, legacy]) {
    let text;
    try {
      text = read(path, "utf8");
    }
    catch (error) {
      if (error.code === "ENOENT") continue;
      throw new Error("Cannot read Superleo config file; check file permissions.");
    }
    // An existing but invalid file must never select another identity.
    return parseConfig(text);
  }
  throw new Error(
    "Create assets/.env.superleo inside superleo-monetization from assets/.env.superleo.example; enter the key locally. assets/.env and legacy game-root .env.superleo are also supported.",
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
    throw new Error("Set a personal or monetization-team SuperLeo MCP key in the selected config file");
  }
  return { url: url.href, key };
}

export async function callTool(config, name, args = {}, fetchImpl = fetch) {
  if (!["superleo_access", "superleo_monetization_catalog", "superleo_monetization"].includes(name)) {
    throw new Error("Unsupported tool");
  }
  if (!args || typeof args !== "object" || Array.isArray(args)) throw new Error("Arguments must be a JSON object");
  const attribution = operatorHeaders();
  let version = "2025-03-26";
  async function rpc(id, method, params) {
    let response;
    try {
      response = await fetchImpl(config.url, {
        method: "POST",
        redirect: "error",
        signal: AbortSignal.timeout(12 * 60 * 1000), // browser onboarding worker allows up to 10 minutes
        headers: {
          ...attribution,
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
    clientInfo: { name: "superleo-monetization-skill", version: "1.0.0" },
  });
  if (typeof init.protocolVersion !== "string") throw new Error("Missing MCP protocol version");
  version = init.protocolVersion;
  await rpc(undefined, "notifications/initialized", {});
  return rpc(2, "tools/call", { name, arguments: args });
}

export async function main(argv = process.argv.slice(2)) {
  const [command, argsFile, ...extra] = argv;
  const names = { access: "superleo_access", catalog: "superleo_monetization_catalog", call: "superleo_monetization" };
  if (!names[command] || extra.length || (command === "call" ? !argsFile : argsFile)) {
    throw new Error("Usage (from game folder): node <skill>/scripts/superleo.mjs access|catalog|call <request.json>");
  }
  const config = loadConfig();
  let args = {};
  if (argsFile) {
    try {
      args = JSON.parse(readFileSync(resolve(argsFile), "utf8"));
    }
    catch {
      throw new Error("Cannot read request JSON");
    }
  }
  const result = await callTool(config, names[command], args);
  // Never echo the transport credential, even if a remote response reflects it.
  process.stdout.write(JSON.stringify(result, null, 2).split(config.key).join("[REDACTED]") + "\n");
  if (result.isError) process.exitCode = 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
