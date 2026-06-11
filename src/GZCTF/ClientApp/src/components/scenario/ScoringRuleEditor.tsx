import { Card, Select, NumberInput, Group, ActionIcon, Button, Text } from '@mantine/core'

interface ScoringRule {
  submissionType: string
  weight: number
  verificationMode: string
  maxAttempts: number
  scoreDecay: string
}

interface ScoringRuleEditorProps {
  rules: ScoringRule[]
  onChange: (rules: ScoringRule[]) => void
}

const SUBMISSION_TYPES = [
  { value: 'Flag', label: 'Flag' },
  { value: 'Writeup', label: '解题报告' },
  { value: 'IP', label: '攻击者 IP' },
  { value: 'Credential', label: '关键凭证' },
]

const VERIFICATION_MODES = [
  { value: 'AutoExact', label: '自动精确匹配' },
  { value: 'AutoRegex', label: '自动正则匹配' },
  { value: 'ManualReview', label: '人工评审' },
]

const SCORE_DECAYS = [
  { value: 'None', label: '无递减' },
  { value: 'Half', label: '每次减半' },
  { value: 'Linear', label: '线性递减' },
]

export default function ScoringRuleEditor({ rules, onChange }: ScoringRuleEditorProps) {
  const totalWeight = rules.reduce((sum, r) => sum + r.weight, 0)
  const isValid = Math.abs(totalWeight - 100) < 0.01

  const updateRule = (index: number, updates: Partial<ScoringRule>) => {
    const updated = [...rules]
    updated[index] = { ...updated[index], ...updates }
    onChange(updated)
  }

  const addRule = () => {
    onChange([
      ...rules,
      { submissionType: 'Flag', weight: 0, verificationMode: 'AutoExact', maxAttempts: 10, scoreDecay: 'None' },
    ])
  }

  const removeRule = (index: number) => {
    onChange(rules.filter((_, i) => i !== index))
  }

  return (
    <div>
      <Group justify="space-between" mb="md">
        <Text fw={500}>评分规则配置</Text>
        <Text c={isValid ? 'green' : 'red'} fw={700}>
          权重总和: {totalWeight}%
        </Text>
      </Group>

      {rules.map((rule, i) => (
        <Card key={i} shadow="sm" padding="sm" mt="sm" withBorder>
          <Group>
            <Select
              label="类型"
              data={SUBMISSION_TYPES}
              value={rule.submissionType}
              onChange={(v) => updateRule(i, { submissionType: v ?? 'Flag' })}
              w={140}
            />
            <NumberInput
              label="权重 (%)"
              value={rule.weight}
              min={0}
              max={100}
              w={100}
              data-testid={`weight-${rule.submissionType}`}
              onChange={(v) => updateRule(i, { weight: Number(v) || 0 })}
            />
            <Select
              label="验证"
              data={VERIFICATION_MODES}
              value={rule.verificationMode}
              onChange={(v) => updateRule(i, { verificationMode: v ?? 'AutoExact' })}
              w={160}
            />
            <NumberInput
              label="最大尝试"
              value={rule.maxAttempts}
              min={0}
              w={100}
              onChange={(v) => updateRule(i, { maxAttempts: Number(v) || 0 })}
            />
            <Select
              label="递减"
              data={SCORE_DECAYS}
              value={rule.scoreDecay}
              onChange={(v) => updateRule(i, { scoreDecay: v ?? 'None' })}
              w={120}
            />
            <ActionIcon color="red" onClick={() => removeRule(i)} mt="lg">
              ×
            </ActionIcon>
          </Group>
        </Card>
      ))}

      <Button variant="outline" mt="md" onClick={addRule}>
        + 添加评分规则
      </Button>
    </div>
  )
}
