import { Alert, Button, Loader } from '@mantine/core'
import { CircleAlert, Plus } from 'lucide-react'
import type { ReactNode } from 'react'

export function ErrorMessage({ error }: { error: Error | null | undefined }) {
  return error ? (
    <Alert color="red" icon={<CircleAlert size={17} />} role="alert" className="error-message">
      {error.message}
    </Alert>
  ) : null
}
export function Loading() {
  return (
    <div className="loading">
      <Loader size="sm" />
    </div>
  )
}
export function Empty({
  icon,
  title,
  action,
  onAction,
}: {
  icon: ReactNode
  title: string
  action?: string
  onAction?: () => void
}) {
  return (
    <div className="empty">
      <div className="empty-symbol">{icon}</div>
      <h2>{title}</h2>
      {action && (
        <Button leftSection={<Plus size={16} />} onClick={onAction}>
          {action}
        </Button>
      )}
    </div>
  )
}
