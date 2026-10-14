import assert from "node:assert/strict";
import { resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { callTool, loadConfig, parseConfig, parseCli, redactResult, businessFailed, main } from "./superleo.mjs";

const settings = "SUPERLEO_URL=https://dostupai.aimulticast.org/mcp\nSUPERLEO_API_KEY=slmcp_fixture";
const config = parseConfig(settings);

test("loads assets/.env relative to the script independently of cwd", () => {
  const paths = [];
  assert.deepEqual(
    loadConfig(path => {
      paths.push(path);
      return settings;
    }, resolve("different-game")),
    config,
  );
  assert.deepEqual(paths, [fileURLToPath(new URL("../assets/.env", import.meta.url))]);
});

test("uses legacy game-root config only when all earlier config locations are absent", () => {
  const paths = [];
  const cwd = resolve("fixture-game");
  assert.deepEqual(
    loadConfig(path => {
      paths.push(path);
      if (paths.length < 4) throw Object.assign(new Error("missing"), { code: "ENOENT" });
      return settings;
    }, cwd),
    config,
  );
  assert.equal(paths.length, 4);
  assert.equal(paths[3], resolve(cwd, ".env.superleo"));
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
  assert.deepEqual(requests.map(r => r.method), ["initialize", "notifications/initialized", "tools/call"]);
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
      error => !error.message.includes(config.key),
    );
    assert.equal(count, 1);
  }
});

test("Facebook and Adjust named operations use the same client without adding identity", async () => {
  for (
    const operation of [
      "facebook_apps",
      "facebook_browser_health",
      "facebook_create_app",
      "facebook_refresh_app",
      "facebook_project_config",
      "adjust_settings",
      "adjust_apps",
      "adjust_find_app",
      "adjust_create_app",
      "adjust_ticket_status",
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
        result: request.method === "initialize"
          ? { protocolVersion: "2025-03-26" }
          : { isError: false, content: [] },
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
      error => !error.message.includes(config.key),
    );
  }
});


test("supplied assets/.env.superleo is recognized without changing identity on errors", () => {
  const paths = [];
  assert.deepEqual(loadConfig(path => {
    paths.push(path);
    if (paths.length === 1) throw Object.assign(new Error("missing"), {code:"ENOENT"});
    return settings;
  }), config);
  assert.equal(paths[1], fileURLToPath(new URL("../assets/.env.superleo", import.meta.url)));
  let count = 0;
  assert.throws(() => loadConfig(() => {
    if (++count === 1) throw Object.assign(new Error("missing"), {code:"ENOENT"});
    return "invalid";
  }));
  assert.equal(count, 2);
});

test("explicit config never falls back to another existing identity", () => {
  const paths = [];
  assert.deepEqual(loadConfig(path => {paths.push(path);return settings;}, resolve("game"), import.meta.url, "operator.env"), config);
  assert.deepEqual(paths, [resolve("game/operator.env")]);
  let count = 0;
  assert.throws(() => loadConfig(() => {count++;throw Object.assign(new Error("missing"), {code:"ENOENT"});}, resolve("game"), import.meta.url, "missing.env"));
  assert.equal(count, 1);
});

test("CLI retains exact request file and rejects ambiguous options", () => {
  const options = parseCli(["call", "request.json", "--config", "private.env", "--private-output", "private.json"]);
  assert.equal(options.tool, "superleo_monetization");
  assert.equal(options.configPath, "private.env");
  assert.equal(options.outputPath, "private.json");
  for (const argv of [["access","request.json"],["call"],["access","--config"],["access","--config","one","--config","two"],["catalog","--other","x"]]) {
    assert.throws(() => parseCli(argv));
  }
});

test("SDK credentials in JSON text content are masked while resource IDs remain readable", () => {
  const result = {content:[{type:"text",text:JSON.stringify({success:true,data:{appId:"12345",sdkKey:"fixture-sdk",facebookClientToken:"fixture-client",adUnitId:"unit-7",authorization:"Bearer fixture",note:config.key}})}]};
  const safe = redactResult(result, [config.key]);
  const serialized = JSON.stringify(safe);
  for (const value of ["fixture-sdk","fixture-client","Bearer fixture",config.key]) assert.equal(serialized.includes(value),false);
  const data = JSON.parse(safe.content[0].text).data;
  assert.equal(data.appId,"12345");assert.equal(data.adUnitId,"unit-7");
  assert.equal(redactResult({structuredContent:{sdk_key:"nested-secret"}}).structuredContent.sdk_key,"[REDACTED]");
});

test("transport success does not hide backend or per-product failures", () => {
  assert.equal(businessFailed({content:[{type:"text",text:JSON.stringify({success:true,data:{success:false,error:"provider"}})}]}),true);
  assert.equal(businessFailed({content:[{type:"text",text:JSON.stringify({success:true,data:{results:[{backend:{ok:true},googlePlay:{ok:false}}]}})}]}),true);
  assert.equal(businessFailed({isError:true}),true);
  assert.equal(businessFailed({content:[{type:"text",text:JSON.stringify({success:true,data:{requiresConfirmation:true,providerValidated:false}})}]}),false);
});

test("sensitive container fields cannot expose unlabelled nested values", () => {
  const result = {content:[{type:"text",text:JSON.stringify({success:true,data:{
    credentials:{value:"nested-fixture",account:{value:"second-fixture"}},
    cookies:[{name:"session",value:"cookie-fixture"}],
    sdk_key:{value:"sdk-fixture"},appId:"12345"
  }})}]};
  const safe = redactResult(result);
  const serialized = JSON.stringify(safe);
  for (const value of ["nested-fixture","second-fixture","cookie-fixture","sdk-fixture"]) {
    assert.equal(serialized.includes(value),false);
  }
  assert.equal(JSON.parse(safe.content[0].text).data.appId,"12345");
});

test("CLI returns failure status and sanitized public output without altering confirmation", async () => {
  let sent;
  const output = [];
  const exitCode = await main(["call","request.json","--config","operator.env"], {
    cwd:resolve("fixture-game"),
    read:path=>path.endsWith("request.json")?JSON.stringify({operation:"max_create",confirm:false}):settings,
    stdout:text=>output.push(text),
    call:async(c,n,args)=>{sent={c,n,args};return {content:[{type:"text",text:JSON.stringify({success:true,data:{success:false,sdkKey:"private-fixture"}})}]};}
  });
  assert.equal(exitCode,1);assert.equal(sent.args.confirm,false);
  assert.equal(output.join("").includes("private-fixture"),false);
});

test("private responses preserve SDK values and refuse overwrite before a remote call", async () => {
  const {mkdtempSync,writeFileSync,readFileSync,rmSync} = await import('node:fs');
  const dir = mkdtempSync(resolve('.superleo-tests-'));
  try {
    writeFileSync(resolve(dir,'operator.env'),settings);
    const output = [], result = {content:[{type:'text',text:JSON.stringify({success:true,data:{sdkKey:'private-fixture'}})}]};
    let calls = 0;
    const options = ['access','--config','operator.env','--private-output','response.local.json'];
    assert.equal(await main(options,{cwd:dir,stdout:text=>output.push(text),call:async()=>{calls++;return result;}}),0);
    assert.equal(output.join('').includes('private-fixture'),false);
    assert.deepEqual(JSON.parse(readFileSync(resolve(dir,'response.local.json'),'utf8')),result);
    await assert.rejects(main(options,{cwd:dir,stdout:()=>{},call:async()=>{calls++;return result;}}),/already exists/);
    assert.equal(calls,1);
  } finally {rmSync(dir,{recursive:true,force:true});}
});
