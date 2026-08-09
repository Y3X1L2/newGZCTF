import { BookOpenCheck, Flag, Network, ScrollText, Swords, Trophy } from 'lucide-react'
import { GameType } from '@Api'

export interface GameModuleDefinition {
  id: string
  label: string
  shortLabel: string
  description: string
  icon: typeof Flag
  types: readonly GameType[]
  implemented: boolean
}

export const gameModuleDefinitions = [
  {
    id: 'challenges',
    label: 'CTF 题目',
    shortLabel: '题目',
    description: '查看题面、附件和实例环境',
    icon: Flag,
    types: [GameType.Jeopardy, GameType.Mixed],
    implemented: true,
  },
  {
    id: 'scoreboard',
    label: '积分榜',
    shortLabel: '积分榜',
    description: '查看战队排名与得分变化',
    icon: Trophy,
    types: [GameType.Jeopardy, GameType.Mixed],
    implemented: true,
  },
  {
    id: 'theory',
    label: '理论考试',
    shortLabel: '理论考试',
    description: '进入试卷并保存答题草稿',
    icon: BookOpenCheck,
    types: [GameType.Theory, GameType.Mixed],
    implemented: true,
  },
  {
    id: 'theory-scoreboard',
    label: '理论榜单',
    shortLabel: '理论榜单',
    description: '查看理论考试独立排名',
    icon: ScrollText,
    types: [GameType.Theory, GameType.Mixed],
    implemented: true,
  },
  {
    id: 'awdp',
    label: 'AWDP 工作区',
    shortLabel: 'AWDP',
    description: '攻击、修补与服务状态',
    icon: Swords,
    types: [GameType.AWDP, GameType.Mixed],
    implemented: true,
  },
  {
    id: 'pentest',
    label: '渗透演练',
    shortLabel: '渗透',
    description: '进入多网段渗透任务',
    icon: Network,
    types: [GameType.Penetration, GameType.Mixed],
    implemented: true,
  },
] satisfies readonly GameModuleDefinition[]

export function gameModulesFor(type?: GameType) {
  const resolvedType = type ?? GameType.Jeopardy
  return gameModuleDefinitions.filter((module) => module.types.some((candidate) => candidate === resolvedType))
}
