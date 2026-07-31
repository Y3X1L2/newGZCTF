import { PackageCheck } from 'lucide-react'
import type { TeamLabBootstrapReference } from '../../api/teamlabContracts'
import { InspectorSection, KeyValueEditor, NumberInput, TextInput, ToggleInput } from './InspectorFields'

export function BootstrapEditor({
  bootstrap,
  onChange,
  readOnly,
}: {
  bootstrap: TeamLabBootstrapReference | null
  onChange: (bootstrap: TeamLabBootstrapReference | null) => void
  readOnly?: boolean
}) {
  return (
    <InspectorSection icon={<PackageCheck aria-hidden="true" size={16} />} title="服务注入">
      <ToggleInput
        checked={bootstrap !== null}
        disabled={readOnly}
        description="仅引用已声明并签名的 Bootstrap Profile"
        label="启用 Bootstrap"
        onChange={(enabled) => onChange(enabled ? { profileId: '', version: 1, parameters: {} } : null)}
      />
      {bootstrap ? (
        <>
          <TextInput disabled={readOnly} label="Profile ID" onChange={(profileId) => onChange({ ...bootstrap, profileId })} value={bootstrap.profileId} />
          <NumberInput disabled={readOnly} label="版本" min={1} onChange={(version) => onChange({ ...bootstrap, version })} value={bootstrap.version} />
          <KeyValueEditor
            emptyText="暂无公开参数。敏感值应由运行时 secret 注入，不在拓扑中保存。"
            label="公开参数"
            onChange={(parameters) => onChange({ ...bootstrap, parameters })}
            readOnly={readOnly}
            values={bootstrap.parameters}
          />
        </>
      ) : null}
    </InspectorSection>
  )
}
