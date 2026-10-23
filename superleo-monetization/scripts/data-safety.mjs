#!/usr/bin/env node
// Fill Google's Data safety CSV template (assets/data-safety-template.csv, the official sample from
// support.google.com/googleplay/android-developer/answer/10787469) from a small JSON spec, for
// play_set_data_safety. The template's example answers are cleared first.
//   node scripts/data-safety.mjs <spec.json> > data-safety.csv
// spec: { "encryptedInTransit": true, "deletionRequest": true,
//         "types": { "PSL_DEVICE_ID": ["PSL_ANALYTICS", "PSL_ADVERTISING"], ... } }
// Every listed type is declared collected and shared, required (users cannot opt out), not ephemeral,
// with the same purposes for collection and sharing.
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

export function parseCsv(text) {
  const rows = [];
  let row = [], field = "", quoted = false;
  for (let i = 0; i < text.length; i++) {
    const char = text[i];
    if (quoted) {
      if (char === "\"" && text[i + 1] === "\"") {
        field += "\"";
        i++;
      }
      else if (char === "\"") quoted = false;
      else field += char;
    }
    else if (char === "\"") quoted = true;
    else if (char === ",") {
      row.push(field);
      field = "";
    }
    else if (char === "\n" || char === "\r") {
      if (char === "\r" && text[i + 1] === "\n") i++;
      row.push(field);
      rows.push(row);
      row = [];
      field = "";
    }
    else field += char;
  }
  if (field || row.length) {
    row.push(field);
    rows.push(row);
  }
  return rows.filter(r => r.length > 1);
}
const quote = value => /[",\n\r]/.test(value) ? `"${value.replace(/"/g, "\"\"")}"` : value;

export function buildDataSafetyCsv(template, spec) {
  const [header, ...rows] = parseCsv(template);
  const types = spec.types || {};
  const known = new Set(rows.filter(r => r[0].startsWith("PSL_DATA_TYPES_")).map(r => r[1]));
  for (const [type, purposes] of Object.entries(types)) {
    if (!known.has(type)) throw new Error(`Unknown data type ${type}`);
    if (!Array.isArray(purposes) || !purposes.length) throw new Error(`${type}: at least one purpose`);
  }
  const collects = Object.keys(types).length > 0;
  const value = ([question, response]) => {
    if (question === "PSL_DATA_COLLECTION_COLLECTS_PERSONAL_DATA") return collects ? "TRUE" : "FALSE";
    // Asked only when the app collects data; left empty for "no data collected".
    if (question === "PSL_DATA_COLLECTION_ENCRYPTED_IN_TRANSIT") {
      return collects
        ? (spec.encryptedInTransit ? "TRUE" : "FALSE")
        : "";
    }
    if (question === "PSL_DATA_COLLECTION_USER_REQUEST_DELETE") {
      return collects
        ? (spec.deletionRequest ? "TRUE" : "FALSE")
        : "";
    }
    if (question.startsWith("PSL_DATA_TYPES_")) return types[response] ? "TRUE" : "";
    const usage = /^PSL_DATA_USAGE_RESPONSES:([A-Z_]+):(.+)$/.exec(question);
    if (!usage || !types[usage[1]]) return "";
    const [, type, part] = usage;
    if (part === "PSL_DATA_USAGE_COLLECTION_AND_SHARING") return "TRUE";
    if (part === "PSL_DATA_USAGE_EPHEMERAL") return "FALSE";
    if (part.endsWith("DATA_USAGE_USER_CONTROL")) {
      return response === "PSL_DATA_USAGE_USER_CONTROL_REQUIRED"
        ? "TRUE"
        : "";
    }
    if (part.endsWith("DATA_USAGE_COLLECTION_PURPOSE") || part.endsWith("DATA_USAGE_SHARING_PURPOSE")) {
      return types[type].includes(response) ? "TRUE" : "";
    }
    return "";
  };
  return [header, ...rows.map(row => [row[0], row[1], value(row), row[3], row[4]])]
    .map(row => row.map(cell => quote(cell ?? "")).join(","))
    .join("\n") + "\n";
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const here = dirname(fileURLToPath(import.meta.url));
  const template = readFileSync(join(here, "..", "assets", "data-safety-template.csv"), "utf8");
  const spec = JSON.parse(readFileSync(process.argv[2], "utf8"));
  process.stdout.write(buildDataSafetyCsv(template, spec));
}
