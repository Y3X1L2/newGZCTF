import { Container, Text, Stack } from '@mantine/core'

export function EmptyState({ title = '暂无数据', description = '' }: { title?: string; description?: string }) {
  return (
    <Container py="xl" style={{ textAlign: 'center' }}>
      <Stack align="center">
        <Text size="lg" fw={500} c="dimmed">
          {title}
        </Text>
        {description && (
          <Text size="sm" c="dimmed">
            {description}
          </Text>
        )}
      </Stack>
    </Container>
  )
}
