import { SelectInput, TextAreaInput, TextInput } from './InspectorFields'

const record = (value: unknown): Record<string, unknown> | null =>
  value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null

export function DeviceParametersEditor({ schema, value, disabled, onChange }: {
  schema: unknown; value: string | null; disabled?: boolean; onChange: (value: string) => void
}) {
  let values: Record<string, unknown> | null = null
  try { values = record(JSON.parse(value?.trim() || '{}')) } catch { /* Keep invalid drafts editable. */ }
  const definition = record(schema)
  const properties = record(definition?.properties)
  const required = Array.isArray(definition?.required) ? definition.required : []
  const fields = Object.entries(properties ?? {}).map(([key, value]) => ({ key, schema: record(value) }))
  const simple = values !== null && fields.length > 0 && fields.every(field =>
    field.schema && ['string', 'integer', 'number', 'boolean'].includes(String(field.schema.type)))
  const set = (key: string, value: unknown) => {
    const next = { ...values }
    if (value === undefined) delete next[key]
    else next[key] = value
    onChange(JSON.stringify(next))
  }
  const raw = <TextAreaInput label="设备参数 JSON" value={value ?? ''} disabled={disabled} onChange={onChange}
    hint="参数随发布冻结；不要填写密码或令牌。" />
  if (!simple) return raw
  return <>
    {fields.map(({ key, schema }) => {
      const field = schema!
      const label = `${typeof field.title === 'string' ? field.title : key}${required.includes(key) ? '（必填）' : ''}`
      const options = Array.isArray(field.enum) ? field.enum : field.type === 'boolean' ? [true, false] : null
      if (options) return <SelectInput key={key} label={label} disabled={disabled}
        value={values![key] === undefined ? '' : JSON.stringify(values![key])}
        onChange={selected => set(key, selected === '' ? undefined : JSON.parse(selected))}>
        <option value="">未设置</option>{options.map(option => <option key={JSON.stringify(option)} value={JSON.stringify(option)}>
          {option === true ? '是' : option === false ? '否' : String(option)}</option>)}
      </SelectInput>
      const numeric = field.type === 'number' || field.type === 'integer'
      return <TextInput key={key} label={label} disabled={disabled} value={String(values![key] ?? '')} type={numeric ? 'number' : 'text'}
        min={typeof field.minimum === 'number' ? field.minimum : undefined} max={typeof field.maximum === 'number' ? field.maximum : undefined}
        step={field.type === 'integer' ? 1 : undefined} hint={typeof field.description === 'string' ? field.description : undefined}
        onChange={next => {
          if (numeric && next !== '' && (!Number.isFinite(Number(next)) || field.type === 'integer' && !Number.isInteger(Number(next)))) return false
          set(key, next === '' ? undefined : numeric ? Number(next) : next)
        }} />
    })}
    <details><summary>高级参数</summary>{raw}</details>
  </>
}
