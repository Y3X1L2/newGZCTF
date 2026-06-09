export const PLATFORM_BRAND = 'YINYU CTF平台'
export const PLATFORM_TITLE = 'YINYU'
export const PLATFORM_SLOGAN = '专业赛事管理与攻防演练平台'
export const PLATFORM_DESCRIPTION = 'YINYU CTF平台提供赛事管理、攻防演练、理论赛与分布式靶场调度能力。'

const legacyPrefix = ['g', 'z'].join('')
const legacyFullName = `${legacyPrefix}ctf`
const LEGACY_TITLES = new Set(['', legacyPrefix, legacyFullName, `${legacyPrefix}::ctf`])

export const getPlatformName = (title?: string | null) => {
  const normalized = title?.trim() ?? ''
  const legacyKey = normalized.toLowerCase()

  if (LEGACY_TITLES.has(legacyKey)) return PLATFORM_BRAND

  if (normalized.includes('CTF') || normalized.includes('平台')) return normalized

  return `${normalized} CTF平台`
}
