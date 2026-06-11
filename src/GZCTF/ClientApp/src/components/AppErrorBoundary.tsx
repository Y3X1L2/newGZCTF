import { Button, Center, Group } from '@mantine/core'
import { AlertTriangle, RefreshCcw } from 'lucide-react'
import { Component, type ReactNode } from 'react'
import { YinyuHeartbeatIcon, YinyuHexField } from '@Components/yinyu/YinyuUI'

interface Props {
  children: ReactNode
}

interface State {
  hasError: boolean
  error?: Error
}

export class AppErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false }

  static getDerivedStateFromError(error: Error) {
    return { hasError: true, error }
  }

  render() {
    if (this.state.hasError) {
      return (
        <Center mih="100dvh" px="md" py="xl" className="yy-standalone-shell yy-error-stage">
          <article className="state-page panel-card state-danger yy-error-boundary">
            <YinyuHexField cells={72} />
            <div className="error-code">500</div>
            <div className="yy-error-heading">
              <AlertTriangle size={42} />
              <YinyuHeartbeatIcon label="error signal" />
            </div>
            <h3>页面加载错误</h3>
            <p>{this.state.error?.message || '未知错误'}</p>
            <Group mt="md" className="yy-error-actions">
              <Button
                variant="outline"
                leftSection={<RefreshCcw size={15} />}
                onClick={() => {
                  this.setState({ hasError: false })
                  window.location.reload()
                }}
              >
                重新加载
              </Button>
            </Group>
          </article>
        </Center>
      )
    }

    return this.props.children
  }
}
