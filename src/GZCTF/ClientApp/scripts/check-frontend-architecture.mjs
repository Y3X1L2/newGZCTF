import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import process from 'node:process'

const root = process.cwd()
const sourceRoot = join(root, 'src')
const baseline = JSON.parse(readFileSync(join(root, 'scripts/frontend-architecture-baseline.json'), 'utf8'))

const walk = (directory) =>
  readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name)
    return entry.isDirectory() ? walk(path) : [path]
  })

const sourceFiles = walk(sourceRoot).filter((path) => /\.(?:ts|tsx|css)$/.test(path))
const normalized = (path) => relative(root, path).replaceAll('\\', '/')
const failures = []
let inlineStyles = 0
let yinyuClassReferences = 0
let globalCssLines = 0

for (const path of sourceFiles) {
  const file = normalized(path)
  const content = readFileSync(path, 'utf8')

  if (/\.(?:ts|tsx)$/.test(path)) {
    inlineStyles += (content.match(/\bstyle=\{/g) || []).length
    yinyuClassReferences += (content.match(/\byy-[a-z0-9-]+/gi) || []).length

    if (!file.startsWith('src/api-client/') && !file.startsWith('src/generated/') && /from\s+['"][^'"]*generated\/Api['"]/.test(content)) {
      failures.push(`${file}: generated API must be consumed through @Api`)
    }

    if (file.startsWith('src/pages/') && /import\s+['"]\.[^'"]+\.css['"]/.test(content) && !/\.module\.css['"]/.test(content)) {
      failures.push(`${file}: route pages may only import CSS modules`)
    }
  }

  if (file === 'src/styles/YinyuDesignLab.css' || file === 'src/styles/YinyuTheme.css' || file === 'src/styles/YinyuRefinement.css') {
    globalCssLines += content.split(/\r?\n/).length
  }
}

if (inlineStyles > baseline.maxInlineStyles) failures.push(`inline style count ${inlineStyles} exceeds ${baseline.maxInlineStyles}`)
if (yinyuClassReferences > baseline.maxYinyuClassReferences) {
  failures.push(`yy-* reference count ${yinyuClassReferences} exceeds ${baseline.maxYinyuClassReferences}`)
}
if (globalCssLines > baseline.maxGlobalCssLines) failures.push(`global CSS lines ${globalCssLines} exceeds ${baseline.maxGlobalCssLines}`)

for (const [file, budget] of Object.entries({ ...baseline.pageLineBudgets, ...baseline.componentLineBudgets })) {
  const path = join(root, file)
  if (!statSync(path).isFile()) continue
  const lines = readFileSync(path, 'utf8').split(/\r?\n/).length
  if (lines > budget) failures.push(`${file}: ${lines} lines exceeds ${budget}`)
}

console.log(`[architecture] inline=${inlineStyles}, yy-classes=${yinyuClassReferences}, global-css=${globalCssLines}`)
if (failures.length) {
  console.error(`[architecture] violations:\n${failures.map((item) => `  - ${item}`).join('\n')}`)
  process.exit(1)
}
