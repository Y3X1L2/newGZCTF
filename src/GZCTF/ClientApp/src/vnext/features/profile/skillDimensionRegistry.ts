export interface SkillDimensionDefinition {
  id: string
  shortLabel: string
  description: string
}

export const skillDimensionRegistry: SkillDimensionDefinition[] = [
  { id: 'web', shortLabel: 'Web', description: 'Web 安全' },
  { id: 'pwn', shortLabel: 'Pwn', description: '二进制利用' },
  { id: 'reverse', shortLabel: 'RE', description: '逆向工程' },
  { id: 'crypto', shortLabel: 'Crypto', description: '密码学' },
  { id: 'forensics-ir', shortLabel: 'IR', description: '取证与应急响应' },
  { id: 'pentest-osint', shortLabel: 'Pentest', description: '渗透与情报' },
  { id: 'misc-ai-ppc', shortLabel: 'Misc', description: '综合、AI 与 PPC' },
  { id: 'other', shortLabel: 'Other', description: '其他安全方向' },
]

export function dimensionDefinition(id: string) {
  return skillDimensionRegistry.find((item) => item.id === id) ?? {
    id,
    shortLabel: id,
    description: id,
  }
}
