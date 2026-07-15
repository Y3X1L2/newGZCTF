import { useLocation } from 'react-router'
import { DataState } from '../../shared/Primitives'

export function AdminPendingPage() {
  const location = useLocation()
  return <DataState description={`管理路由 ${location.pathname} 尚未进入本阶段实现范围。`} title="该管理页面待建设" />
}
