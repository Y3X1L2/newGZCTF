import { PackageCheck } from 'lucide-react'
import type { TeamLabBootstrapReference } from '../../api/teamlabContracts'
import type { TopologyAssetNode } from '../../model/topologyDocument'
import { FieldHelpButton } from '../help/FieldHelpButton'
import { InspectorSection, ToggleInput } from './InspectorFields'
import { ServiceProfilePicker } from './ServiceProfilePicker'

export function BootstrapEditor({
  bootstrap,
  onChange,
  readOnly,
  assetType,
}: {
  bootstrap: TeamLabBootstrapReference | null
  onChange: (bootstrap: TeamLabBootstrapReference | null) => void
  readOnly?: boolean
  assetType?: TopologyAssetNode['type']
}) {
  const supportsInjection = assetType !== 'docker'

  return (
    <InspectorSection
      icon={<PackageCheck aria-hidden="true" size={16} />}
      title={<span>服务注入 <FieldHelpButton fieldKey="serviceInjection" /></span>}
    >
      <ToggleInput
        checked={bootstrap !== null}
        disabled={readOnly || !supportsInjection && bootstrap === null}
        description={supportsInjection
          ? '只可选择已发布并通过校验的服务配置。'
          : '当前 Docker 执行面不支持服务注入。请选择虚拟机资产进行服务安装和健康检查。'}
        label="启用服务注入"
        onChange={(enabled) => onChange(enabled ? { profileId: '', version: 1, parameters: {} } : null)}
      />
      {bootstrap && supportsInjection ? (
        <>
          <ServiceProfilePicker
            assetKind={assetType ? 'vm' : undefined}
            bootstrap={bootstrap}
            onChange={onChange}
            readOnly={readOnly}
          />
        </>
      ) : null}
    </InspectorSection>
  )
}
