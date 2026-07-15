import {
  Binary,
  Blocks,
  BrainCircuit,
  Braces,
  Cpu,
  Globe2,
  KeyRound,
  Network,
  Puzzle,
  Radar,
  ScanSearch,
  Siren,
  Smartphone,
  Wrench,
} from 'lucide-react'
import { ChallengeCategory } from '@Api'

export const challengeCategoryMeta: Record<ChallengeCategory, { label: string; icon: typeof Puzzle }> = {
  [ChallengeCategory.Misc]: { label: '综合', icon: Puzzle },
  [ChallengeCategory.Crypto]: { label: '密码学', icon: KeyRound },
  [ChallengeCategory.Pwn]: { label: '二进制', icon: Binary },
  [ChallengeCategory.Web]: { label: 'Web', icon: Globe2 },
  [ChallengeCategory.Reverse]: { label: '逆向', icon: ScanSearch },
  [ChallengeCategory.Blockchain]: { label: '区块链', icon: Blocks },
  [ChallengeCategory.Forensics]: { label: '数字取证', icon: Braces },
  [ChallengeCategory.Hardware]: { label: '硬件', icon: Cpu },
  [ChallengeCategory.Mobile]: { label: '移动安全', icon: Smartphone },
  [ChallengeCategory.PPC]: { label: '编程', icon: Wrench },
  [ChallengeCategory.AI]: { label: '人工智能', icon: BrainCircuit },
  [ChallengeCategory.Pentest]: { label: '渗透测试', icon: Network },
  [ChallengeCategory.OSINT]: { label: '开源情报', icon: Radar },
  [ChallengeCategory.IR]: { label: '应急响应', icon: Siren },
}

export function categoryMeta(category: ChallengeCategory | string) {
  return challengeCategoryMeta[category as ChallengeCategory] ?? challengeCategoryMeta[ChallengeCategory.Misc]
}
