import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import process from 'node:process'

const root = process.cwd()
const sourceRoot = join(root, 'src')
const baseline = JSON.parse(readFileSync(join(root, 'scripts/frontend-architecture-baseline.json'), 'utf8'))
const postcssConfig = readFileSync(join(root, 'postcss.config.mjs'), 'utf8')

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
let vnextInlineStyles = 0

if (/postcss-preset-mantine|mantine-breakpoint/.test(postcssConfig)) {
  failures.push('postcss.config.mjs: vNext build must not depend on Mantine unit or breakpoint transforms')
}

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

    if (file === 'src/App.tsx' && /@mantine\//.test(content)) {
      failures.push(`${file}: the vNext application root may not restore Mantine providers or styles`)
    }

    if (file.startsWith('src/vnext/')) {
      const imports = [...content.matchAll(/(?:from\s+|import\s+)['"]([^'"]+)['"]/g)].map((match) => match[1])
      vnextInlineStyles += (content.match(/\bstyle=\{/g) || []).length

      if (imports.some((specifier) => specifier.startsWith('@mantine/'))) {
        failures.push(`${file}: vNext may not depend on Mantine`)
      }
      if (/\byy-[a-z0-9-]+/i.test(content)) failures.push(`${file}: vNext may not use legacy yy-* classes`)
      if (
        imports.some((specifier) =>
          /(?:^|\/)(?:components|pages|styles)(?:\/|$)/.test(specifier.replaceAll('\\', '/'))
        )
      ) {
        failures.push(`${file}: vNext may not import legacy visual components, pages, or global styles`)
      }
      if (file.endsWith('.tsx') && imports.some((specifier) => specifier.endsWith('.css') && !specifier.endsWith('.module.css'))) {
        failures.push(`${file}: vNext TSX files may only import CSS modules`)
      }
      if (file.includes('/features/training/') && imports.some((specifier) => /(?:^|\/)games(?:\/|$)/.test(specifier))) {
        failures.push(`${file}: training must not depend on the games feature`)
      }
      if (file.includes('/features/games/') && imports.some((specifier) => /(?:^|\/)training(?:\/|$)/.test(specifier))) {
        failures.push(`${file}: games must not depend on the training feature`)
      }
      if (file.endsWith('.tsx') && /\bfetch\s*\(/.test(content)) {
        failures.push(`${file}: page and view components must call feature API adapters instead of fetch`)
      }
      if (/window\.(?:confirm|prompt)\s*\(/.test(content)) {
        failures.push(`${file}: use VNextConfirmDialog instead of browser confirmation APIs`)
      }
      if (/document\.querySelector/.test(content)) {
        failures.push(`${file}: do not query the document to coordinate React forms or views`)
      }
      if (file.endsWith('.tsx') && file !== 'src/vnext/shared/MarkdownContent.tsx' && /dangerouslySetInnerHTML/.test(content)) {
        failures.push(`${file}: sanitized HTML rendering is centralized in MarkdownContent`)
      }
      if (file.endsWith('.tsx')) {
        const lines = content.split(/\r?\n/).length
        if (lines > baseline.vnext.maxTsxLines) {
          failures.push(`${file}: ${lines} lines exceeds vNext budget ${baseline.vnext.maxTsxLines}`)
        }
      }
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
if (vnextInlineStyles > baseline.vnext.maxInlineStyles) {
  failures.push(`vNext inline style count ${vnextInlineStyles} exceeds ${baseline.vnext.maxInlineStyles}`)
}

for (const [file, budget] of Object.entries({ ...baseline.pageLineBudgets, ...baseline.componentLineBudgets })) {
  const path = join(root, file)
  if (!statSync(path).isFile()) continue
  const lines = readFileSync(path, 'utf8').split(/\r?\n/).length
  if (lines > budget) failures.push(`${file}: ${lines} lines exceeds ${budget}`)
}

console.log(
  `[architecture] inline=${inlineStyles}, yy-classes=${yinyuClassReferences}, global-css=${globalCssLines}, vnext-inline=${vnextInlineStyles}`
)
if (failures.length) {
  console.error(`[architecture] violations:\n${failures.map((item) => `  - ${item}`).join('\n')}`)
  process.exit(1)
}
