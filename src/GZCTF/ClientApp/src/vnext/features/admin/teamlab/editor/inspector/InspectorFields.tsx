import { Plus, Trash2 } from 'lucide-react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { FieldHelpButton } from '../help/FieldHelpButton'
import styles from './TeamLabInspector.module.css'

type DraftCommit = (value: string) => boolean | void

function useCommittedDraft(value: string, onCommit?: DraftCommit) {
  const [draft, setDraft] = useState(value)
  const focused = useRef(false)

  useEffect(() => {
    if (!focused.current) setDraft(value)
  }, [value])

  const commit = () => {
    focused.current = false
    if (!onCommit || draft === value) return
    if (onCommit(draft) === false) setDraft(value)
  }

  return {
    draft,
    setDraft,
    onFocus: () => {
      focused.current = true
    },
    onBlur: commit,
  }
}

function KeyValueInput({
  ariaLabel,
  value,
  onCommit,
  disabled,
  placeholder,
}: {
  ariaLabel: string
  value: string
  onCommit: DraftCommit
  disabled?: boolean
  placeholder: string
}) {
  const draft = useCommittedDraft(value, onCommit)
  return (
    <input
      aria-label={ariaLabel}
      disabled={disabled}
      onBlur={draft.onBlur}
      onChange={(event) => draft.setDraft(event.currentTarget.value)}
      onFocus={draft.onFocus}
      placeholder={placeholder}
      value={draft.draft}
    />
  )
}

export function InspectorSection({ title, icon, children }: { title: ReactNode; icon?: ReactNode; children: ReactNode }) {
  return (
    <section className={styles.section}>
      <h3>
        {icon}
        {title}
      </h3>
      <div className={styles.sectionBody}>{children}</div>
    </section>
  )
}

export function TextInput({
  label,
  value,
  onChange,
  disabled,
  type = 'text',
  min,
  max,
  step,
  hint,
  help,
}: {
  label: string
  value: string | number
  onChange?: DraftCommit
  disabled?: boolean
  type?: 'text' | 'number'
  min?: number
  max?: number
  step?: number
  hint?: string
  help?: string
}) {
  const sourceValue = String(value)
  const draft = useCommittedDraft(sourceValue, onChange)
  return (
    <label className={styles.field}>
      <span>{label}{help ? <FieldHelpButton fieldKey={help} /> : null}</span>
      <input
        disabled={disabled}
        max={max}
        min={min}
        onBlur={draft.onBlur}
        onChange={onChange ? (event) => draft.setDraft(event.currentTarget.value) : undefined}
        onFocus={draft.onFocus}
        onKeyDown={(event) => {
          if (event.key === 'Enter') event.currentTarget.blur()
        }}
        readOnly={!onChange}
        step={step}
        type={type}
        value={draft.draft}
      />
      {hint ? <small>{hint}</small> : null}
    </label>
  )
}

export function TextAreaInput({
  label,
  value,
  onChange,
  disabled,
  hint,
  help,
}: {
  label: string
  value: string
  onChange: (value: string) => void
  disabled?: boolean
  hint?: string
  help?: string
}) {
  const draft = useCommittedDraft(value, onChange)
  return (
    <label className={styles.field}>
      <span>{label}{help ? <FieldHelpButton fieldKey={help} /> : null}</span>
      <textarea
        disabled={disabled}
        onBlur={draft.onBlur}
        onChange={(event) => draft.setDraft(event.currentTarget.value)}
        onFocus={draft.onFocus}
        value={draft.draft}
      />
      {hint ? <small>{hint}</small> : null}
    </label>
  )
}

export function SelectInput({
  label,
  value,
  onChange,
  children,
  disabled,
  help,
}: {
  label: string
  value: string
  onChange: (value: string) => void
  children: ReactNode
  disabled?: boolean
  help?: string
}) {
  return (
    <label className={styles.field}>
      <span>{label}{help ? <FieldHelpButton fieldKey={help} /> : null}</span>
      <select disabled={disabled} onChange={(event) => onChange(event.currentTarget.value)} value={value}>
        {children}
      </select>
    </label>
  )
}

export function ToggleInput({
  label,
  description,
  checked,
  onChange,
  disabled,
  help,
}: {
  label: string
  description?: string
  checked: boolean
  onChange: (checked: boolean) => void
  disabled?: boolean
  help?: string
}) {
  return (
    <label className={styles.toggle}>
      <span>
        <strong>{label}{help ? <FieldHelpButton fieldKey={help} /> : null}</strong>
        {description ? <small>{description}</small> : null}
      </span>
      <input
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.currentTarget.checked)}
        type="checkbox"
      />
      <i aria-hidden="true" />
    </label>
  )
}

export function NumberInput({
  label,
  value,
  onChange,
  disabled,
  min,
  max,
  step = 1,
  help,
}: {
  label: string
  value: number
  onChange: (value: number) => void
  disabled?: boolean
  min?: number
  max?: number
  step?: number
  help?: string
}) {
  return (
    <TextInput
      disabled={disabled}
      help={help}
      label={label}
      max={max}
      min={min}
      onChange={(next) => {
        if (!next.trim()) return false
        const parsed = Number(next)
        if (!Number.isFinite(parsed)) return false
        onChange(parsed)
        return true
      }}
      step={step}
      type="number"
      value={value}
    />
  )
}

export function KeyValueEditor({
  label,
  values,
  onChange,
  readOnly,
  emptyText = '暂无配置',
}: {
  label: string
  values: Readonly<Record<string, string>>
  onChange: (values: Readonly<Record<string, string>>) => void
  readOnly?: boolean
  emptyText?: string
}) {
  const entries = Object.entries(values)
  const updateEntry = (oldKey: string, key: string, value: string) => {
    const nextKey = key.trim()
    if (!nextKey || (nextKey !== oldKey && nextKey in values)) return false
    const next = { ...values }
    delete next[oldKey]
    next[nextKey] = value
    onChange(next)
    return true
  }
  return (
    <div className={styles.keyValueEditor}>
      <div className={styles.inlineHeading}>
        <strong>{label}</strong>
      </div>
      {entries.length === 0 ? <p className={styles.muted}>{emptyText}</p> : null}
      {entries.map(([key, value]) => (
        <div className={styles.keyValueRow} key={key}>
          <KeyValueInput
            ariaLabel={`${label}键`}
            disabled={readOnly}
            onCommit={(nextKey) => updateEntry(key, nextKey, value)}
            placeholder="键"
            value={key}
          />
          <KeyValueInput
            ariaLabel={`${label}值`}
            disabled={readOnly}
            onCommit={(nextValue) => updateEntry(key, key, nextValue)}
            placeholder="值"
            value={value}
          />
          <button
            aria-label={`删除 ${key}`}
            className={styles.iconButton}
            disabled={readOnly}
            onClick={() => {
              const next = { ...values }
              delete next[key]
              onChange(next)
            }}
            title="删除"
            type="button"
          >
            <Trash2 aria-hidden="true" size={15} />
          </button>
        </div>
      ))}
      <button
        className={styles.addButton}
        disabled={readOnly}
        onClick={() => {
          let index = entries.length + 1
          let key = `key_${index}`
          while (key in values) key = `key_${++index}`
          onChange({ ...values, [key]: '' })
        }}
        type="button"
      >
        <Plus aria-hidden="true" size={15} />
        添加配置
      </button>
    </div>
  )
}
