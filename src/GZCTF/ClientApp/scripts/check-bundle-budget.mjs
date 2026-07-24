import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { gzipSync } from 'node:zlib'
import process from 'node:process'

const buildDir = join(process.cwd(), 'build')
const hardJsGzipBudget = 900 * 1024
const advisoryJsGzipBudget = 350 * 1024
const hardCssGzipBudget = 350 * 1024

const walk = (directory) =>
  readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name)
    return entry.isDirectory() ? walk(path) : [path]
  })

const assets = walk(buildDir)
  .filter((path) => /\.(?:js|css)$/.test(path))
  .map((path) => ({
    path,
    name: relative(buildDir, path).replaceAll('\\', '/'),
    raw: statSync(path).size,
    gzip: gzipSync(readFileSync(path)).byteLength,
    content: readFileSync(path, 'utf8'),
  }))
  .sort((left, right) => right.gzip - left.gzip)

const failures = []
for (const asset of assets) {
  if (asset.name.endsWith('.js') && asset.gzip > hardJsGzipBudget) failures.push(`${asset.name}: JS gzip ${asset.gzip}`)
  if (asset.name.endsWith('.css') && asset.gzip > hardCssGzipBudget) failures.push(`${asset.name}: CSS gzip ${asset.gzip}`)
  if (/@mantine\/|MantineProvider|--mantine-scale/.test(asset.content)) {
    failures.push(`${asset.name}: vNext production artifact contains Mantine runtime or transformed units`)
  }
}

console.log('[bundle] largest assets:')
for (const asset of assets.slice(0, 12)) {
  const warning = asset.name.endsWith('.js') && asset.gzip > advisoryJsGzipBudget ? ' advisory-limit' : ''
  console.log(`  ${asset.name} raw=${asset.raw} gzip=${asset.gzip}${warning}`)
}

if (failures.length) {
  console.error(`[bundle] hard budget exceeded:\n${failures.map((item) => `  - ${item}`).join('\n')}`)
  process.exit(1)
}
