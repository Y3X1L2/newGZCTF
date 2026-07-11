import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import process from 'node:process'

const root = process.cwd()
const buildDir = join(root, 'build')
const packageInfo = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'))

const git = (args, fallback) => {
  try {
    return execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim()
  } catch {
    return fallback
  }
}

const walk = (directory) =>
  readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name)
    return entry.isDirectory() ? walk(path) : [path]
  })

const files = walk(buildDir)
  .filter((path) => !path.endsWith('frontend-manifest.json'))
  .map((path) => {
    const content = readFileSync(path)
    return {
      path: relative(buildDir, path).replaceAll('\\', '/'),
      bytes: statSync(path).size,
      sha256: createHash('sha256').update(content).digest('hex'),
    }
  })
  .sort((left, right) => left.path.localeCompare(right.path))

const manifest = {
  schemaVersion: 1,
  application: packageInfo.name,
  version: packageInfo.version,
  gitSha: process.env.VITE_APP_GIT_SHA || git(['rev-parse', 'HEAD'], 'unknown'),
  gitName: process.env.VITE_APP_GIT_NAME || git(['describe', '--always', '--dirty'], 'unknown'),
  builtAt: process.env.VITE_APP_BUILD_TIMESTAMP || new Date().toISOString(),
  entry: 'index.html',
  totalBytes: files.reduce((sum, file) => sum + file.bytes, 0),
  files,
}

writeFileSync(join(buildDir, 'frontend-manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`)
console.log(`[artifact] ${files.length} files, ${manifest.totalBytes} bytes, ${manifest.gitSha.slice(0, 12)}`)
