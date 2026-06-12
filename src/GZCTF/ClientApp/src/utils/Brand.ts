export const PLATFORM_BRAND = 'YINYU \u5b89\u5168\u7efc\u5408\u6f14\u7ec3\u5e73\u53f0'
export const PLATFORM_TITLE = 'YINYU'
export const PLATFORM_SLOGAN = '\u4e13\u4e1a\u8d5b\u4e8b\u7ba1\u7406\u4e0e\u5b89\u5168\u653b\u9632\u6f14\u7ec3\u5e73\u53f0'
export const PLATFORM_DESCRIPTION = 'YINYU \u5b89\u5168\u7efc\u5408\u6f14\u7ec3\u5e73\u53f0\u63d0\u4f9b\u8d5b\u4e8b\u7ba1\u7406\u3001\u653b\u9632\u6f14\u7ec3\u3001\u7406\u8bba\u8bad\u7ec3\u4e0e\u5206\u5e03\u5f0f\u9776\u573a\u8c03\u5ea6\u80fd\u529b\u3002'

const legacyPrefix = ['g', 'z'].join('')
const legacyFullName = `${legacyPrefix}ctf`
const LEGACY_TITLES = new Set(['', legacyPrefix, legacyFullName, `${legacyPrefix}::ctf`, 'yinyu ctf', 'yinyu ctf\u5e73\u53f0'])

export const getPlatformName = (title?: string | null) => {
  const normalized = title?.trim() ?? ''
  const legacyKey = normalized.toLowerCase()

  if (LEGACY_TITLES.has(legacyKey)) return PLATFORM_BRAND

  const sanitized = normalized
    .replace(/CTF\s*Platform/gi, '\u5b89\u5168\u7efc\u5408\u6f14\u7ec3\u5e73\u53f0')
    .replace(/CTF\u5e73\u53f0/gi, '\u5b89\u5168\u7efc\u5408\u6f14\u7ec3\u5e73\u53f0')
    .replace(/\bCTF\b/gi, '\u5b89\u5168\u6f14\u7ec3')

  if (sanitized.includes('\u5e73\u53f0')) return sanitized

  return `${sanitized} \u5b89\u5168\u7efc\u5408\u6f14\u7ec3\u5e73\u53f0`
}
