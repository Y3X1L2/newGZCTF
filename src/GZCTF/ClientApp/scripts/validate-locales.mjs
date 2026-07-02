import { readdir, readFile } from "node:fs/promises";
import path from "node:path";

const localesRoot = path.resolve("src/locales");

async function collectJsonFiles(dir) {
  const entries = await readdir(dir, { withFileTypes: true });
  const files = await Promise.all(
    entries.map((entry) => {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) return collectJsonFiles(fullPath);
      if (entry.isFile() && entry.name.endsWith(".json")) return [fullPath];
      return [];
    })
  );

  return files.flat();
}

const failures = [];

for (const file of await collectJsonFiles(localesRoot)) {
  const content = await readFile(file, "utf8");

  if (content.charCodeAt(0) === 0xfeff) {
    failures.push(`${path.relative(process.cwd(), file)}: UTF-8 BOM is not allowed`);
    continue;
  }

  try {
    JSON.parse(content);
  } catch (error) {
    failures.push(`${path.relative(process.cwd(), file)}: ${error.message}`);
  }
}

if (failures.length > 0) {
  console.error("Locale JSON validation failed:");
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log("Locale JSON validation passed.");
