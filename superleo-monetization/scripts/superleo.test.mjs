import assert from "node:assert/strict";
import { resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { callTool, loadConfig, operatorHeaders, parseConfig } from "./superleo.mjs";

test("automatic device and optional email are bounded audit headers, not tool identity", () => {
  assert.deepEqual(operatorHeaders("Office-PC", "person@example.com"), {
    "X-Superleo-Device": "Office-PC",
    "X-Superleo-Operator-Email": "person@example.com",
  });
  assert.deepEqual(operatorHeaders("Office PC", ""), { "X-Superleo-Device": "Office_PC" });
  assert.equal(operatorHeaders("a".repeat(200), "")["X-Superleo-Device"].length, 160);
  assert.throws(() => operatorHeaders("PC", "bad\r\nheader@example.com"), /email address/);
});

const settings = "SUPERLEO_URL=https://dostupai.aimulticast.org/mcp\nSUPERLEO_API_KEY=slmcp_fixture";
const config = parseConfig(settings);

test("loads assets/.env relative to the script independently of cwd", () => {
  const paths = [];
  assert.deepEqual(
    loadConfig((path) => {
      paths.push(path);
      return settings;
    }, resolve("different-game")),
    config,
  );
  assert.deepEqual(paths, [fileURLToPath(new URL("../assets/.env", import.meta.url))]);
});

test("loads assets/.env.superleo before game-root config, independently of cwd", () => {
  const paths = [];
  assert.deepEqual(
    loadConfig((path) => {
      paths.push(path);
      if (paths.length === 1) throw Object.assign(new Error("missing"), { code: "ENOENT" });
      return settings;
    }, resolve("different-game")),
    config,
  );
  assert.deepEqual(paths, [
    fileURLToPath(new URL("../assets/.env", import.meta.url)),
    fileURLToPath(new URL("../assets/.env.superleo", import.meta.url)),
  ]);
});

test("uses legacy game-root config only when both skill configs are absent", () => {
  const paths = [];
  const cwd = resolve("fixture-game");
  assert.deepEqual(
    loadConfig((path) => {
      paths.push(path);
      if (paths.length <= 2) throw Object.assign(new Error("missing"), { code: "ENOENT" });
      return settings;
    }, cwd),
    config,
  );
  assert.equal(paths.length, 3);
  assert.equal(paths[2], resolve(cwd, ".env.superleo"));
});

test("invalid or inaccessible primary config never falls back to another key", () => {
  for (
    const first of [() => "", () => {
      throw Object.assign(new Error("private data"), { code: "EACCES" });
    }]
  ) {
    let reads = 0;
    assert.throws(() =>
      loadConfig(() => {
        reads++;
        return first();
      })
    );
    assert.equal(reads, 1);
  }
});

test("invalid or inaccessible assets/.env.superleo never selects a game-root key", () => {
  for (
    const invalid of [() => "", () => {
      throw Object.assign(new Error("private data"), { code: "EACCES" });
    }]
  ) {
    let reads = 0;
    assert.throws(() =>
      loadConfig(() => {
        reads++;
        if (reads === 1) throw Object.assign(new Error("missing"), { code: "ENOENT" });
        return invalid();
      })
    );
    assert.equal(reads, 2);
  }
});

test("missing all config files gives setup instructions", () => {
  assert.throws(() =>
    loadConfig(() => {
      throw Object.assign(new Error("missing"), { code: "ENOENT" });
    }), /Create assets\/\.env/);
});

test("dotenv handles BOM, CRLF, comments and quotes without evaluation", () => {
  assert.deepEqual(
    parseConfig(
      "\uFEFF# local\r\nSUPERLEO_URL=\"https://dostupai.aimulticast.org/mcp\"\r\nSUPERLEO_API_KEY='slmcp_fixture'\r\n",
    ),
    config,
  );
  assert.throws(() => parseConfig(settings.replace("slmcp_fixture", "$(echo secret)")));
});

test("rejects missing keys, duplicate/unknown settings and unsafe URLs", () => {
  for (
    const text of [
      settings.replace("slmcp_fixture", ""),
      settings + "\nSUPERLEO_API_KEY=second",
      settings + "\nOTHER=secret",
    ]
  ) {
    assert.throws(() => parseConfig(text));
  }
  for (
    const url of [
      "http://example.com/mcp",
      "https://user:pass@example.com/mcp",
      "https://example.com/",
      "https://example.com/mcp?token=secret",
      "https://example.com/mcp#secret",
    ]
  ) {
    assert.throws(() => parseConfig(settings.replace(config.url, url)));
  }
  for (const host of ["localhost", "127.0.0.1", "[::1]"]) {
    assert.equal(parseConfig(settings.replace(config.url, `http://${host}:8000/mcp`)).url, `http://${host}:8000/mcp`);
  }
});

test("initializes and calls only requested tool, preserving confirmation and identity", async () => {
  const requests = [];
  const result = await callTool(config, "superleo_monetization", {
    operation: "project_create",
    body: { name: "Fixture" },
    confirm: false,
  }, async (url, options) => {
    assert.equal(url, config.url);
    assert.equal(options.headers.Authorization, `Bearer ${config.key}`);
    assert.match(options.headers["X-Superleo-Device"], /^[A-Za-z0-9._-]{1,160}$/);
    assert.equal(options.redirect, "error");
    const request = JSON.parse(options.body);
    requests.push(request);
    if (request.method === "notifications/initialized") return new Response(null, { status: 202 });
    return Response.json({
      jsonrpc: "2.0",
      id: request.id,
      result: request.method === "initialize"
        ? { protocolVersion: "2025-03-26" }
        : { content: [{ type: "text", text: "fixture result" }] },
    });
  });
  assert.deepEqual(requests.map((r) => r.method), ["initialize", "notifications/initialized", "tools/call"]);
  assert.equal(requests[2].params.arguments.confirm, false);
  assert.equal(Object.hasOwn(requests[2].params.arguments, "email"), false);
  assert.equal(result.content[0].text, "fixture result");
});

test("network errors and HTTP failures are sanitized and never retried", async () => {
  for (
    const fail of [() => {
      throw new Error(config.key);
    }, () => new Response(config.key, { status: 401 })]
  ) {
    let count = 0;
    await assert.rejects(
      callTool(config, "superleo_access", {}, async () => {
        count++;
        return fail();
      }),
      (error) => !error.message.includes(config.key),
    );
    assert.equal(count, 1);
  }
});

test("Facebook named operations use the same client without adding identity or confirmation", async () => {
  for (
    const operation of [
      "facebook_apps",
      "facebook_browser_health",
      "facebook_create_app",
      "facebook_refresh_app",
      "facebook_project_config",
    ]
  ) {
    const args = { operation, body: { projectId: "fixture" } };
    const requests = [];
    await callTool(config, "superleo_monetization", args, async (_url, options) => {
      const request = JSON.parse(options.body);
      requests.push(request);
      if (request.method === "notifications/initialized") return new Response(null, { status: 202 });
      return Response.json({
        jsonrpc: "2.0",
        id: request.id,
        result: request.method === "initialize" ? { protocolVersion: "2025-03-26" } : { isError: false, content: [] },
      });
    });
    assert.deepEqual(requests[2].params, { name: "superleo_monetization", arguments: args });
  }
});

test("a timed out Facebook create call is never repeated", async () => {
  let mutations = 0;
  await assert.rejects(
    callTool(config, "superleo_monetization", {
      operation: "facebook_create_app",
      confirm: true,
      body: { projectId: "fixture" },
    }, async (_url, options) => {
      const request = JSON.parse(options.body);
      if (request.method === "initialize") {
        return Response.json({ id: request.id, result: { protocolVersion: "2025-03-26" } });
      }
      if (request.method === "notifications/initialized") {
        return new Response(null, { status: 202 });
      }
      mutations++;
      throw new Error("timeout after provider committed");
    }),
    /no automatic retry/,
  );
  assert.equal(mutations, 1);
});

test("rejects unrelated tools and invalid JSON arguments before network", async () => {
  const neverFetch = () => {
    assert.fail("must not send request");
  };
  await assert.rejects(callTool(config, "other", {}, neverFetch));
  await assert.rejects(callTool(config, "superleo_access", [], neverFetch));
});

test("does not echo malformed RPC errors or HTML responses", async () => {
  for (
    const response of [Response.json({ id: 1, error: { message: config.key } }), new Response("<html>login</html>")]
  ) {
    await assert.rejects(
      callTool(config, "superleo_access", {}, async () => response),
      (error) => !error.message.includes(config.key),
    );
  }
});
