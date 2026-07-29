import {
  Activity,
  BookOpenCheck,
  Boxes,
  CircleGauge,
  FileClock,
  GraduationCap,
  Dumbbell,
  ListTodo,
  Server,
  Settings2,
  ShieldCheck,
  Trophy,
  Users,
} from 'lucide-react'

export interface AdminNavigationItem {
  id: string
  label: string
  route: string
  icon: typeof Activity
  implemented: boolean
}

export interface AdminNavigationGroup {
  label: string
  items: AdminNavigationItem[]
}

export const adminNavigation: AdminNavigationGroup[] = [
  {
    label: '概览',
    items: [{ id: 'dashboard', label: '运行概览', route: '/admin/dashboard', icon: CircleGauge, implemented: true }],
  },
  {
    label: '业务',
    items: [
      { id: 'games', label: '赛事管理', route: '/admin/games', icon: Trophy, implemented: true },
      { id: 'exercises', label: '练习题库', route: '/admin/exercises', icon: Dumbbell, implemented: true },
      { id: 'theory-bank', label: '理论题库', route: '/admin/theory-bank', icon: BookOpenCheck, implemented: true },
    ],
  },
  {
    label: '资源',
    items: [
      { id: 'images', label: '环境模板', route: '/admin/images', icon: Boxes, implemented: true },
      { id: 'instances', label: '运行实例', route: '/admin/instances', icon: Activity, implemented: true },
    ],
  },
  {
    label: '运维',
    items: [
      { id: 'nodes', label: '节点管理', route: '/admin/nodes', icon: Server, implemented: true },
      { id: 'queue', label: '部署队列', route: '/admin/queue', icon: ListTodo, implemented: true },
      { id: 'logs', label: '系统日志', route: '/admin/logs', icon: FileClock, implemented: true },
    ],
  },
  {
    label: '治理',
    items: [
      { id: 'users', label: '用户管理', route: '/admin/users', icon: Users, implemented: true },
      { id: 'teams', label: '战队管理', route: '/admin/teams', icon: ShieldCheck, implemented: true },
      { id: 'student-groups', label: '学员组', route: '/admin/student-groups', icon: GraduationCap, implemented: true },
      { id: 'system', label: '系统设置', route: '/admin/system', icon: Settings2, implemented: true },
    ],
  },
]
