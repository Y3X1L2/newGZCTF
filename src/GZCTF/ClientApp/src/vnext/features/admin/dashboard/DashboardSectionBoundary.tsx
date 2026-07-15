import { ReactNode } from 'react'
import { ErrorBoundary } from 'react-error-boundary'
import { DataState } from '../../../shared/Primitives'

export function DashboardSectionBoundary({ children, name }: { children: ReactNode; name: string }) {
  return (
    <ErrorBoundary
      fallbackRender={() => <DataState description={`${name}渲染失败，其他运行数据仍可使用。`} title={`${name}暂时不可用`} />}
    >
      {children}
    </ErrorBoundary>
  )
}
