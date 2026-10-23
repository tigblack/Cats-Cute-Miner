import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { buildDataSafetyCsv, parseCsv } from "./data-safety.mjs";

const template = readFileSync(new URL("../assets/data-safety-template.csv", import.meta.url), "utf8");
const spec = JSON.parse(readFileSync(new URL("../assets/data-safety-multicast-default.json", import.meta.url), "utf8"));

test("studio defaults fill only the declared types and purposes, keeping the template rows", () => {
  const rows = parseCsv(buildDataSafetyCsv(template, spec));
  assert.equal(rows.length, parseCsv(template).length);
  const get = (q, r = "") => rows.find(row => row[0] === q && row[1] === r)?.[2];
  assert.equal(get("PSL_DATA_COLLECTION_ENCRYPTED_IN_TRANSIT"), "TRUE");
  assert.equal(get("PSL_DATA_TYPES_PERSONAL", "PSL_NAME"), "", "template example answers are cleared");
  const declared = rows.filter(r => r[0].startsWith("PSL_DATA_TYPES_") && r[2] === "TRUE").map(r => r[1]).sort();
  assert.deepEqual(declared, Object.keys(spec.types).sort());
  const usage = "PSL_DATA_USAGE_RESPONSES:PSL_CRASH_LOGS:";
  assert.equal(get(`${usage}PSL_DATA_USAGE_COLLECTION_AND_SHARING`, "PSL_DATA_USAGE_ONLY_SHARED"), "TRUE");
  assert.equal(get(`${usage}DATA_USAGE_USER_CONTROL`, "PSL_DATA_USAGE_USER_CONTROL_REQUIRED"), "TRUE");
  assert.equal(get(`${usage}DATA_USAGE_COLLECTION_PURPOSE`, "PSL_ANALYTICS"), "TRUE");
  assert.equal(get(`${usage}DATA_USAGE_COLLECTION_PURPOSE`, "PSL_ADVERTISING"), "");
  assert.throws(() => buildDataSafetyCsv(template, { types: { PSL_NOPE: ["PSL_ANALYTICS"] } }), /Unknown data type/);
});

test("an app that collects nothing answers only the first question", () => {
  const rows = parseCsv(buildDataSafetyCsv(template, { types: {} }));
  const answered = rows.filter(row => row[2] !== "" && row[0] !== "Question ID (machine readable)");
  assert.deepEqual(answered.map(row => [row[0], row[2]]), [["PSL_DATA_COLLECTION_COLLECTS_PERSONAL_DATA", "FALSE"]]);
});
