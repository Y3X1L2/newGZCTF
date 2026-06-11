import { Alert, Button, Text } from '@mantine/core'
import { Component, type ErrorInfo, type ReactNode } from 'react'

interface Props {
  children: ReactNode
  fallback?: ReactNode
}

interface State {
  hasError: boolean
  error: Error | null
}

export default class ScenarioErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    console.error('[ScenarioErrorBoundary]', error, errorInfo)
  }

  render(): ReactNode {
    if (this.state.hasError) {
      if (this.props.fallback) return this.props.fallback

      return (
        <Alert color="red" title="出错了" variant="filled" mt="md">
          <Text size="sm" mb="md">
            场景页面发生了意外错误。请尝试刷新页面，或联系管理员。
          </Text>
          {this.state.error && (
            <Text size="xs" c="gray.3" mb="md">
              {this.state.error.message}
            </Text>
          )}
          <Button variant="white" size="xs" onClick={() => this.setState({ hasError: false, error: null })}>
            重试
          </Button>
        </Alert>
      )
    }

    return this.props.children
  }
}
