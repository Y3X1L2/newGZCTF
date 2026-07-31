import { HeartPulse } from 'lucide-react'
import type { TeamLabHealthCheck } from '../../api/teamlabContracts'
import { InspectorSection, NumberInput, SelectInput, ToggleInput } from './InspectorFields'

export function HealthCheckEditor({
  healthCheck,
  onChange,
  readOnly,
}: {
  healthCheck: TeamLabHealthCheck | null
  onChange: (healthCheck: TeamLabHealthCheck | null) => void
  readOnly?: boolean
}) {
  return (
    <InspectorSection icon={<HeartPulse aria-hidden="true" size={16} />} title="健康检查">
      <ToggleInput
        checked={healthCheck !== null}
        disabled={readOnly}
        description="用于判断服务是否真正可用"
        label="启用健康检查"
        onChange={(enabled) => onChange(enabled ? { kind: 'tcp', port: 80 } : null)}
      />
      {healthCheck ? (
        <>
          <SelectInput
            disabled={readOnly}
            label="检查协议"
            onChange={(kind) => onChange({ ...healthCheck, kind: kind === 'http' ? 'http' : 'tcp' })}
            value={healthCheck.kind}
          >
            <option value="tcp">TCP</option>
            <option value="http">HTTP</option>
          </SelectInput>
          <NumberInput disabled={readOnly} label="检查端口" max={65535} min={1} onChange={(port) => onChange({ ...healthCheck, port })} value={healthCheck.port} />
        </>
      ) : null}
    </InspectorSection>
  )
}
