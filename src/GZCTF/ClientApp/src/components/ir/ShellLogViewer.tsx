import { Text, Code } from '@mantine/core'

interface ShellLogEntry {
  timestamp: string
  command: string
  output?: string
}

interface ShellLogViewerProps {
  entries: ShellLogEntry[]
  maxHeight?: number
}

export default function ShellLogViewer({ entries, maxHeight = 400 }: ShellLogViewerProps) {
  if (entries.length === 0) return <Text c="dimmed">暂无操作日志</Text>

  return (
    <div style={{ maxHeight, overflowY: 'auto', background: '#1a1a2e', borderRadius: 4, padding: '0.75rem' }}>
      {entries.map((entry, i) => (
        <div key={i} style={{ marginBottom: '0.5rem', fontFamily: 'monospace', fontSize: 13 }}>
          <Text span c="dimmed" size="xs">
            [{entry.timestamp}]
          </Text>{' '}
          <Code style={{ background: 'transparent', color: '#50fa7b' }}>$ {entry.command}</Code>
          {entry.output && (
            <Text c="gray.4" size="xs" style={{ whiteSpace: 'pre-wrap', marginTop: 2 }}>
              {entry.output}
            </Text>
          )}
        </div>
      ))}
    </div>
  )
}
