import { Card, Skeleton, Group } from '@mantine/core'

export function SkeletonCard() {
  return (
    <Card shadow="sm" padding="md" withBorder>
      <Group justify="space-between" mb="xs">
        <Skeleton height={20} width="60%" />
        <Skeleton height={20} width={40} circle />
      </Group>
      <Skeleton height={12} width="80%" mb="sm" />
      <Skeleton height={8} width="100%" mb="xs" />
      <Skeleton height={8} width="100%" mb="xs" />
    </Card>
  )
}
