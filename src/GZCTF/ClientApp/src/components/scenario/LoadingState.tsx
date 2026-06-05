import { Skeleton, Stack, Text, type MantineSpacing } from '@mantine/core';

interface LoadingStateProps {
  lines?: number;
  height?: number;
  mt?: MantineSpacing;
}

export function LoadingState({ lines = 3, height = 20, mt = 'md' }: LoadingStateProps) {
  return (
    <Stack mt={mt}>
      <Text c="dimmed" size="sm">加载中...</Text>
      {Array.from({ length: lines }).map((_, i) => (
        <Skeleton key={i} height={height} />
      ))}
    </Stack>
  );
}

interface EmptyStateProps {
  message?: string;
  mt?: MantineSpacing;
}

export function EmptyState({ message = '暂无数据', mt = 'md' }: EmptyStateProps) {
  return (
    <Text c="dimmed" ta="center" mt={mt} py="xl">
      {message}
    </Text>
  );
}
