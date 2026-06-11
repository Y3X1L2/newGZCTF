import { Button, Center, Group, Textarea } from '@mantine/core'
import { AlertTriangle, RefreshCcw, Trash2 } from 'lucide-react'
import { FC } from 'react'
import { FallbackProps, getErrorMessage } from 'react-error-boundary'
import { useTranslation } from 'react-i18next'
import { YinyuHexField, YinyuHeartbeatIcon } from '@Components/yinyu/YinyuUI'
import { clearLocalCache } from '@Utils/Cache'

function getErrorStack(thrown: unknown): string | undefined {
  if (typeof thrown === 'object' && thrown !== null && 'stack' in thrown && typeof thrown.stack === 'string') {
    return thrown.stack
  }

  return getErrorMessage(thrown)
}

export const ErrorFallback: FC<FallbackProps> = ({ error, resetErrorBoundary }: FallbackProps) => {
  const { t } = useTranslation()

  return (
    <Center mih="100dvh" px="md" py="xl" className="yy-standalone-shell yy-error-stage">
      <article className="state-page panel-card state-danger yy-error-boundary">
        <YinyuHexField cells={72} />
        <div className="error-code">500</div>
        <div className="yy-error-heading">
          <AlertTriangle size={42} />
          <YinyuHeartbeatIcon label="error signal" />
        </div>
        <h3>{t('common.error.encountered')}</h3>
        <p>{getErrorMessage(error)}</p>
        <Textarea value={getErrorStack(error)} autosize minRows={10} maxRows={18} tabIndex={-1} />
        <Group mt="md" className="yy-error-actions">
          <Button variant="outline" leftSection={<RefreshCcw size={15} />} onClick={resetErrorBoundary}>
            {t('common.button.try_again')}
          </Button>
          <Button variant="outline" leftSection={<Trash2 size={15} />} onClick={clearLocalCache}>
            {t('common.tab.account.clean_cache')}
          </Button>
        </Group>
      </article>
    </Center>
  )
}
