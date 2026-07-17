import { Eye, EyeOff } from 'lucide-react'
import {
  ChangeEventHandler,
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
  useState,
} from 'react'
import styles from './FormControls.module.css'

type FieldChromeProps = {
  label: string
  hint?: string
  error?: string | null
  required?: boolean
}

function FieldChrome({ label, hint, error, required, children }: FieldChromeProps & { children: ReactNode }) {
  return (
    <label className={styles.field}>
      <span className={styles.label}>
        {label}
        {required ? <em>必填</em> : null}
      </span>
      {children}
      {error ? <small className={styles.error}>{error}</small> : hint ? <small>{hint}</small> : null}
    </label>
  )
}

export function TextField({
  label,
  hint,
  error,
  required,
  onChange,
  onValueChange,
  ...props
}: FieldChromeProps &
  InputHTMLAttributes<HTMLInputElement> & {
    onValueChange?: (value: string) => void
  }) {
  const handleChange: ChangeEventHandler<HTMLInputElement> | undefined =
    onChange || onValueChange
      ? (event) => {
          onChange?.(event)
          onValueChange?.(event.currentTarget.value)
        }
      : undefined

  return (
    <FieldChrome error={error} hint={hint} label={label} required={required}>
      <input aria-invalid={Boolean(error)} onChange={handleChange} required={required} {...props} />
    </FieldChrome>
  )
}

export function PasswordField({
  label,
  hint,
  error,
  required,
  onChange,
  onValueChange,
  ...props
}: FieldChromeProps &
  Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & {
    onValueChange?: (value: string) => void
  }) {
  const [visible, setVisible] = useState(false)
  const handleChange: ChangeEventHandler<HTMLInputElement> | undefined =
    onChange || onValueChange
      ? (event) => {
          onChange?.(event)
          onValueChange?.(event.currentTarget.value)
        }
      : undefined

  return (
    <FieldChrome error={error} hint={hint} label={label} required={required}>
      <span className={styles.passwordControl}>
        <input
          aria-invalid={Boolean(error)}
          onChange={handleChange}
          required={required}
          type={visible ? 'text' : 'password'}
          {...props}
        />
        <button
          aria-label={visible ? '隐藏密码' : '显示密码'}
          onClick={() => setVisible((current) => !current)}
          type="button"
        >
          {visible ? <EyeOff aria-hidden="true" size={17} /> : <Eye aria-hidden="true" size={17} />}
        </button>
      </span>
    </FieldChrome>
  )
}

export function TextAreaField({
  label,
  hint,
  error,
  required,
  onChange,
  onValueChange,
  ...props
}: FieldChromeProps &
  TextareaHTMLAttributes<HTMLTextAreaElement> & {
    onValueChange?: (value: string) => void
  }) {
  const handleChange: ChangeEventHandler<HTMLTextAreaElement> | undefined =
    onChange || onValueChange
      ? (event) => {
          onChange?.(event)
          onValueChange?.(event.currentTarget.value)
        }
      : undefined

  return (
    <FieldChrome error={error} hint={hint} label={label} required={required}>
      <textarea aria-invalid={Boolean(error)} onChange={handleChange} required={required} {...props} />
    </FieldChrome>
  )
}

export function SelectField({
  label,
  hint,
  error,
  required,
  children,
  onChange,
  onValueChange,
  ...props
}: FieldChromeProps &
  SelectHTMLAttributes<HTMLSelectElement> & {
    onValueChange?: (value: string) => void
  }) {
  const handleChange: ChangeEventHandler<HTMLSelectElement> | undefined =
    onChange || onValueChange
      ? (event) => {
          onChange?.(event)
          onValueChange?.(event.currentTarget.value)
        }
      : undefined

  return (
    <FieldChrome error={error} hint={hint} label={label} required={required}>
      <select aria-invalid={Boolean(error)} onChange={handleChange} required={required} {...props}>
        {children}
      </select>
    </FieldChrome>
  )
}

export function FileField({
  label,
  hint,
  accept,
  onChange,
}: {
  label: string
  hint?: string
  accept?: string
  onChange: (file: File | null) => void
}) {
  return (
    <FieldChrome hint={hint} label={label}>
      <input
        accept={accept}
        className={styles.fileInput}
        onChange={(event) => onChange(event.currentTarget.files?.[0] ?? null)}
        type="file"
      />
    </FieldChrome>
  )
}

export function ToggleField({
  checked,
  label,
  description,
  disabled,
  onChange,
}: {
  checked: boolean
  label: string
  description?: string
  disabled?: boolean
  onChange: (checked: boolean) => void
}) {
  return (
    <label className={styles.toggleField}>
      <span>
        <strong>{label}</strong>
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
