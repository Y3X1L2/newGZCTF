export const PLATFORM_BRAND = 'YINYU 安全综合演练平台'
export const PLATFORM_TITLE = 'YINYU'
export const PLATFORM_SLOGAN = '专业赛事管理与安全攻防演练平台'
export const PLATFORM_DESCRIPTION = 'YINYU 安全综合演练平台提供赛事管理、攻防演练、理论训练与分布式靶场调度能力。'
export const PLATFORM_TYPE = '安全综合演练平台'
export const PLATFORM_TYPEWRITER_SLOGANS = ['演练赛事在线调度', '平台通知实时归档', '靶场服务安全编排']

const legacyPrefix = ['g', 'z'].join('')
const legacyFullName = `${legacyPrefix}ctf`
const LEGACY_TITLES = new Set(['', legacyPrefix, legacyFullName, `${legacyPrefix}::ctf`, 'yinyu ctf', 'yinyu ctf平台'])
const LEGACY_DEFAULT_SLOGANS = new Set(['专业赛事管理与攻防演练平台', PLATFORM_SLOGAN])

export const getPlatformName = (title?: string | null) => {
  const normalized = title?.trim() ?? ''
  const legacyKey = normalized.toLowerCase()

  if (LEGACY_TITLES.has(legacyKey)) return PLATFORM_TITLE

  const sanitized = normalized
    .replace(/CTF\s*Platform/gi, '安全综合演练平台')
    .replace(/CTF平台/gi, '安全综合演练平台')
    .replace(/\bCTF\b/gi, '安全演练')

  return sanitized.trim() || PLATFORM_TITLE
}

export const getPlatformBrand = (title?: string | null) => {
  const name = getPlatformName(title)

  if (name === PLATFORM_TITLE) return PLATFORM_BRAND
  if (name.includes('平台')) return name

  return `${name} ${PLATFORM_TYPE}`
}

export const splitPlatformSlogans = (slogan?: string | null) => {
  const normalized = slogan?.trim() ?? ''

  if (!normalized || LEGACY_DEFAULT_SLOGANS.has(normalized)) return PLATFORM_TYPEWRITER_SLOGANS

  const lines = normalized
    .split(/\r?\n|\r/g)
    .map((line) => line.trim())
    .filter(Boolean)
    .filter((line, index, array) => array.indexOf(line) === index)

  return lines.length > 0 ? lines : PLATFORM_TYPEWRITER_SLOGANS
}

export const joinPlatformSlogans = (slogans: Array<string | null | undefined>) => {
  const lines = slogans
    .map((line) => line?.trim())
    .filter((line): line is string => Boolean(line))
    .filter((line, index, array) => array.indexOf(line) === index)

  return (lines.length > 0 ? lines : PLATFORM_TYPEWRITER_SLOGANS).join('\n')
}

export const getPrimarySlogan = (slogan?: string | null) => splitPlatformSlogans(slogan)[0] ?? PLATFORM_TYPEWRITER_SLOGANS[0]

export const getSloganText = (slogan?: string | null) => splitPlatformSlogans(slogan).join(' / ')
