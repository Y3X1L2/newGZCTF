import { Save } from 'lucide-react'
import { type FormEvent, useEffect, useState } from 'react'
import { NodeCapability } from '@Api'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { TextField, ToggleField } from '../../../shared/FormControls'
import { errorMessage } from '../../../shared/errors'
import type { NodeSummary } from '../api'
import { hasNodeCapability } from './useAdminNodes'
import styles from './NodeCapacitySettings.module.css'

export interface NodeCapacitySettingsValue {
  isSchedulable: boolean
  maxContainers: number
  maxVms: number
}

interface CapacityValidationResult {
  value: NodeCapacitySettingsValue | null
  containerError: string | null
  vmError: string | null
}

function capacityError(value: string, allocated: number, maximum: number, label: string) {
  if (!value.trim()) return `${label}必须是整数。`
  const parsed = Number(value)
  if (!Number.isInteger(parsed)) return `${label}必须是整数。`
  if (parsed < allocated) return `${label}不能低于当前已分配数量 ${allocated}。`
  if (parsed > maximum) return `${label}不能超过 ${maximum}。`
  return null
}

export function validateNodeCapacitySettings(
  maxContainers: string,
  maxVms: string,
  isSchedulable: boolean,
  allocatedContainers: number,
  allocatedVms: number
): CapacityValidationResult {
  const containerError = capacityError(maxContainers, allocatedContainers, 10000, '容器上限')
  const vmError = capacityError(maxVms, allocatedVms, 1000, 'VM 上限')
  return {
    containerError,
    vmError,
    value:
      containerError || vmError
        ? null
        : {
            isSchedulable,
            maxContainers: Number(maxContainers),
            maxVms: Number(maxVms),
          },
  }
}

export function NodeCapacitySettings({
  node,
  disabled = false,
  onSave,
}: {
  node: NodeSummary
  disabled?: boolean
  onSave: (value: NodeCapacitySettingsValue) => Promise<void>
}) {
  const [maxContainers, setMaxContainers] = useState(String(node.maxContainers))
  const [maxVms, setMaxVms] = useState(String(node.maxVms))
  const [isSchedulable, setIsSchedulable] = useState(node.isSchedulable)
  const [saving, setSaving] = useState(false)
  const [submitted, setSubmitted] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'danger' | 'success'; message: string } | null>(null)

  useEffect(() => {
    setMaxContainers(String(node.maxContainers))
    setMaxVms(String(node.maxVms))
    setIsSchedulable(node.isSchedulable)
    setSubmitted(false)
    setFeedback(null)
  }, [node.id, node.isSchedulable, node.maxContainers, node.maxVms])

  const validation = validateNodeCapacitySettings(
    maxContainers,
    maxVms,
    isSchedulable,
    node.allocatedContainers,
    node.allocatedVms
  )
  const unchanged =
    validation.value?.maxContainers === node.maxContainers &&
    validation.value.maxVms === node.maxVms &&
    validation.value.isSchedulable === node.isSchedulable

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setSubmitted(true)
    setFeedback(null)
    if (!validation.value || saving) return

    setSaving(true)
    try {
      await onSave(validation.value)
      setSubmitted(false)
      setFeedback({ tone: 'success', message: '节点调度容量已更新。' })
    } catch (reason) {
      setFeedback({ tone: 'danger', message: errorMessage(reason, '节点容量保存失败。') })
    } finally {
      setSaving(false)
    }
  }

  return (
    <form className={styles.settings} onSubmit={(event) => void submit(event)}>
      <header>
        <div>
          <span>SCHEDULING POLICY</span>
          <h3>调度容量配置</h3>
        </div>
        <p>Docker 与 KVM 能力由 Agent 自动上报；这里仅控制平台允许分配的工作负载数量。</p>
      </header>

      <div className={styles.capabilityFacts}>
        <span data-available={hasNodeCapability(node.capabilities, NodeCapability.Docker) || undefined}>Docker</span>
        <span data-available={hasNodeCapability(node.capabilities, NodeCapability.Kvm) || undefined}>KVM</span>
      </div>

      <ToggleField
        checked={isSchedulable}
        description="关闭后节点不再接收任何新任务，现有任务不受影响。"
        disabled={disabled || saving}
        label="参与平台调度"
        onChange={setIsSchedulable}
      />

      <div className={styles.fields}>
        <TextField
          disabled={disabled || saving}
          error={submitted ? validation.containerError : null}
          hint={`已分配 ${node.allocatedContainers}，其中预留 ${node.reservedContainers}`}
          inputMode="numeric"
          label="容器开启上限"
          max={10000}
          min={node.allocatedContainers}
          onValueChange={setMaxContainers}
          step={1}
          type="number"
          value={maxContainers}
        />
        <TextField
          disabled={disabled || saving}
          error={submitted ? validation.vmError : null}
          hint={`已分配 ${node.allocatedVms}，其中预留 ${node.reservedVms}`}
          inputMode="numeric"
          label="虚拟机开启上限"
          max={1000}
          min={node.allocatedVms}
          onValueChange={setMaxVms}
          step={1}
          type="number"
          value={maxVms}
        />
      </div>

      <div className={styles.actions}>
        <ActionButton
          disabled={disabled || saving || unchanged}
          icon={<Save aria-hidden="true" size={16} />}
          tone="primary"
          type="submit"
        >
          {saving ? '正在保存' : '保存容量配置'}
        </ActionButton>
      </div>
      {feedback ? <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback> : null}
    </form>
  )
}
