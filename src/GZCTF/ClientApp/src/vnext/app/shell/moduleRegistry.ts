import { BookOpenCheck, Boxes, GraduationCap, Home, Network, Settings, ShieldCheck, Trophy, Users } from 'lucide-react'
import { LucideIcon } from 'lucide-react'

export type ModuleGroup = '核心功能' | '学习与训练' | '组织协作' | '管理与运维'

export interface PlatformModule {
  id: string
  label: string
  shortLabel: string
  route: string
  icon: LucideIcon
  group: ModuleGroup
  description: string
  primary?: boolean
  implemented?: boolean
  adminOnly?: boolean
}

export const platformModules: PlatformModule[] = [
  {
    id: 'home',
    label: '平台首页',
    shortLabel: '首页',
    route: '/',
    icon: Home,
    group: '核心功能',
    description: '赛事、课程、通知与继续进行',
    primary: true,
    implemented: true,
  },
  {
    id: 'games',
    label: '赛事中心',
    shortLabel: '赛事',
    route: '/games',
    icon: Trophy,
    group: '核心功能',
    description: '浏览、筛选与进入安全演练赛事',
    primary: true,
    implemented: true,
  },
  {
    id: 'practice',
    label: '自主练习',
    shortLabel: '练习',
    route: '/practice',
    icon: ShieldCheck,
    group: '学习与训练',
    description: '题库训练、专题练习与复盘',
    primary: true,
  },
  {
    id: 'training',
    label: '培训课程',
    shortLabel: '培训',
    route: '/training',
    icon: GraduationCap,
    group: '学习与训练',
    description: '课程、章节、实验与理论作业',
    primary: true,
    implemented: true,
  },
  {
    id: 'teams',
    label: '战队协作',
    shortLabel: '战队',
    route: '/teams',
    icon: Users,
    group: '组织协作',
    description: '队伍成员、邀请和参赛关系',
    primary: true,
    implemented: true,
  },
  {
    id: 'theory',
    label: '理论题库',
    shortLabel: '理论',
    route: '/admin/theory-bank',
    icon: BookOpenCheck,
    group: '学习与训练',
    description: '理论题目、试卷与考试管理',
    adminOnly: true,
    implemented: true,
  },
  {
    id: 'images',
    label: '环境模板',
    shortLabel: '镜像',
    route: '/admin/images',
    icon: Boxes,
    group: '管理与运维',
    description: '镜像、虚拟机模板与分发状态',
    adminOnly: true,
    implemented: true,
  },
  {
    id: 'teamlab',
    label: 'TeamLab',
    shortLabel: '组网',
    route: '/admin/teamlab',
    icon: Network,
    group: '管理与运维',
    description: '多节点拓扑、运行环境与流量观测',
    adminOnly: true,
  },
  {
    id: 'admin',
    label: '平台管理',
    shortLabel: '管理',
    route: '/admin',
    icon: Settings,
    group: '管理与运维',
    description: '赛事、用户、节点和系统配置',
    adminOnly: true,
    implemented: true,
  },
]

export const primaryModules = platformModules.filter((module) => module.primary)

export function isModuleActive(pathname: string, route: string) {
  if (route === '/') return pathname === '/'
  return pathname === route || pathname.startsWith(`${route}/`)
}

export function currentModule(pathname: string) {
  if (pathname === '/posts' || pathname.startsWith('/posts/')) {
    return { ...platformModules[0], label: '平台公告', implemented: true }
  }
  if (pathname === '/settings' || pathname.startsWith('/settings/')) {
    return { ...platformModules[0], label: '账户设置', implemented: true }
  }
  if (pathname.startsWith('/account/')) {
    return { ...platformModules[0], label: '账户访问', implemented: false }
  }
  return (
    [...platformModules]
      .sort((left, right) => right.route.length - left.route.length)
      .find((module) => isModuleActive(pathname, module.route)) ?? platformModules[0]
  )
}
