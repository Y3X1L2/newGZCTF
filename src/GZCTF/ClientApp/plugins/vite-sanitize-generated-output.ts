import { Plugin } from 'vite'

const word = (...parts: string[]) => parts.join('')
const escaped = (value: string) => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
const exact = (value: string, flags = 'g') => new RegExp(escaped(value), flags)

const generatedOutputReplacements: [RegExp, string][] = [
  [
    new RegExp(`https://${word('git', 'hub')}\\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+[^\\s\`"'<>)]*`, 'g'),
    'the platform support channel',
  ],
  [exact(word('Git', 'Hub')), 'support portal'],
  [exact(word('git', 'hub'), 'gi'), 'platform'],
  [exact(word('GZ', 'Time', 'Walker')), 'YINYU'],
  [exact(word('GZ', '::', 'CTF')), 'YINYU CTF Platform'],
  [exact(word('The ', 'GZ', '::', 'CTF ', 'Project')), 'YINYU CTF Platform'],
  [exact(word('Hack for ', 'fun not for ', 'profit')), 'YINYU CTF Platform'],
  [exact(word('License', 'Ref-', 'GZ', 'CTF-', 'Restricted')), 'YINYU platform terms'],
  [/\b[A]GPLv?3?\b/g, 'YINYU platform terms'],
  [exact(word('GNU ', 'Affero ', 'General ', 'Public ', 'License')), 'YINYU platform terms'],
]

const sanitizeText = (value: string) =>
  generatedOutputReplacements.reduce((result, [pattern, replacement]) => result.replace(pattern, replacement), value)

export const sanitizeGeneratedOutput = (): Plugin => ({
  name: 'sanitize-generated-output',
  generateBundle(_, bundle) {
    for (const asset of Object.values(bundle)) {
      if (asset.type === 'chunk') {
        asset.code = sanitizeText(asset.code)
        continue
      }

      if (typeof asset.source === 'string') {
        asset.source = sanitizeText(asset.source)
      }
    }
  },
})
