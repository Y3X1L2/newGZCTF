import { Save } from 'lucide-react'
import { TextField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback } from '../../../shared/Interaction'
import { AdminEditorSection } from '../shared/AdminWorkbench'
import styles from './AdminSystemPage.module.css'

export function TrainingContainerQuotaPanel({ draft, onChange, available, valid, dirty, saving, disabled, onSave }: {
  draft: string
  onChange: (value: string) => void
  available: boolean
  valid: boolean
  dirty: boolean
  saving: boolean
  disabled: boolean
  onSave: () => void
}) {
  return (
    <AdminEditorSection description="按用户跨课程统计，运行中的容器与排队中的启动请求共同占用额度。" title="培训容器额度">
      <div className={styles.sectionBody}>
        <TextField
          disabled={!available || disabled}
          error={available && !valid ? '请输入 0–2147483647 的整数。' : null}
          hint="0 表示不限个人数量。保存后新建请求立即使用新上限；降低上限不会停止或销毁已有实例。"
          label="每名用户培训容器上限"
          max={2147483647}
          min={0}
          onValueChange={onChange}
          step={1}
          type="number"
          value={draft}
        />
        {!available ? <InlineFeedback tone="danger">当前服务未提供培训额度配置，请刷新或检查服务版本。</InlineFeedback> : null}
        <div className={styles.sectionActions}>
          <span>{dirty ? '存在未保存修改' : '配置已同步'}</span>
          <ActionButton disabled={!available || !dirty || !valid || disabled} icon={<Save size={16} />} onClick={onSave} tone="primary" type="button">
            {saving ? '保存中' : '保存培训额度'}
          </ActionButton>
        </div>
      </div>
    </AdminEditorSection>
  )
}
