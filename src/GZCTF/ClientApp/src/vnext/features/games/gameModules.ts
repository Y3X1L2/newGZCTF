import { BookOpenCheck, Flag, Network, ScrollText, Swords, Trophy } from 'lucide-react'
import { GameType } from '@Api'

export interface GameModuleDefinition {
  id: string
  label: string
  shortLabel: string
  icon: typeof Flag
  types: readonly GameType[]
  implemented: boolean
}

export const gameModuleDefinitions = [
  {
    id: 'challenges',
    label: 'CTF 题目',
    shortLabel: '题目',
    icon: Flag,
    types: [GameType.Jeopardy, GameType.Mixed],
    implemented: true,
  },
  {
    id: 'scoreboard',
    label: '积分榜',
    shortLabel: '积分榜',
    icon: Trophy,
    types: [GameType.Jeopardy, GameType.Mixed],
    implemented: true,
  },
  {
    id: 'theory',
    label: '理论考试',
    shortLabel: '理论考试',
    icon: BookOpenCheck,
    types: [GameType.Theory, GameType.Mixed],
    implemented: true,
  },
  {
    id: 'theory-scoreboard',
    label: '理论榜单',
    shortLabel: '理论榜单',
    icon: ScrollText,
    types: [GameType.Theory, GameType.Mixed],
    implemented: true,
  },
  {
    id: 'awdp',
    label: 'AWDP 工作区',
    shortLabel: 'AWDP',
    icon: Swords,
    types: [GameType.AWDP, GameType.Mixed],
    implemented: true,
  },
  {
    id: 'pentest',
    label: '渗透演练',
    shortLabel: '渗透',
    icon: Network,
    types: [GameType.Penetration, GameType.Mixed],
    implemented: true,
  },
] satisfies readonly GameModuleDefinition[]

export function gameModulesFor(type?: GameType) {
  const resolvedType = type ?? GameType.Jeopardy
  return gameModuleDefinitions.filter((module) => module.types.some((candidate) => candidate === resolvedType))
}
