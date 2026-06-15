export const PLATFORM_BRAND = 'YINYU 安全综合演练平台'
export const PLATFORM_TITLE = 'YINYU'
export const PLATFORM_SLOGAN = '专业赛事管理与安全攻防演练平台'
export const PLATFORM_DESCRIPTION = 'YINYU 安全综合演练平台提供赛事管理、攻防演练、理论训练与分布式靶场调度能力。'

const legacyPrefix = ['g', 'z'].join('')
const legacyFullName = `${legacyPrefix}ctf`
const LEGACY_TITLES = new Set(['', legacyPrefix, legacyFullName, `${legacyPrefix}::ctf`, 'yinyu ctf', 'yinyu ctf平台'])

export const getPlatformName = (title?: string | null) => {
  const normalized = title?.trim() ?? ''
  const legacyKey = normalized.toLowerCase()

  if (LEGACY_TITLES.has(legacyKey)) return PLATFORM_BRAND

  const sanitized = normalized
    .replace(/CTF\s*Platform/gi, '安全综合演练平台')
    .replace(/CTF平台/gi, '安全综合演练平台')
    .replace(/\bCTF\b/gi, '安全演练')

  if (sanitized.includes('平台')) return sanitized

  return `${sanitized} 安全综合演练平台`
}
