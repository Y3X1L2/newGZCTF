import { Activity, BookOpenCheck, Boxes, CircleGauge, FileClock, ListTodo, Server, Settings2, Trophy } from 'lucide-react'

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
    label: '后续',
    items: [{ id: 'system', label: '系统配置', route: '/admin/system', icon: Settings2, implemented: false }],
  },
]
