#!/usr/bin/env node
import { readFileSync, writeFileSync } from "node:fs";

const [, , file] = process.argv;
if (!file) {
  console.error("usage: strip-trailing-whitespace.mjs <file>");
  process.exit(2);
}

const input = readFileSync(file, "utf8");
const output = input
  .split("\n")
  .map((line) => line.replace(/ +\t/gu, "\t").replace(/[ \t]+$/u, ""))
  .join("\n");
writeFileSync(file, output, "utf8");
