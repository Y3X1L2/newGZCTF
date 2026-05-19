import { Component, type ReactNode } from 'react';
import { Alert, Button, Container, Text } from '@mantine/core';

interface Props { children: ReactNode; }
interface State { hasError: boolean; error?: Error; }

export class AppErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };
  static getDerivedStateFromError(error: Error) { return { hasError: true, error }; }
  render() {
    if (this.state.hasError) {
      return (
        <Container size="sm" py="xl">
          <Alert color="red" title="页面加载错误" mb="md">
            <Text size="sm">{this.state.error?.message || '未知错误'}</Text>
          </Alert>
          <Button onClick={() => { this.setState({ hasError: false }); window.location.reload(); }}>
            重新加载
          </Button>
        </Container>
      );
    }
    return this.props.children;
  }
}
