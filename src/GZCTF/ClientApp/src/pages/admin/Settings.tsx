import {
  Button,
  Divider,
  FileInput,
  Grid,
  Group,
  Text,
  NumberInput,
  SimpleGrid,
  Stack,
  Switch,
  TextInput,
  Title,
  ActionIcon,
  Tooltip,
} from '@mantine/core'
import { mdiCheck, mdiContentSaveOutline, mdiDeleteOutline, mdiPlus, mdiRestore } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { LogoBox } from '@Components/LogoBox'
import { AdminPage } from '@Components/admin/AdminPage'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { PLATFORM_DESCRIPTION, PLATFORM_TITLE, joinPlatformSlogans, splitPlatformSlogans } from '@Utils/Brand'
import { getInputNumber, showErrorMsg } from '@Utils/Shared'
import { IMAGE_MIME_TYPES } from '@Utils/Shared'
import { OnceSWRConfig, useCaptchaConfig, useConfig } from '@Hooks/useConfig'
import api, { AccountPolicy, ConfigEditModel, ContainerPolicy, GlobalConfig } from '@Api'
import misc from '@Styles/Misc.module.css'

const Configs: FC = () => {
  const { data: configs, mutate } = api.admin.useAdminGetConfigs(OnceSWRConfig)
  const { mutate: mutateCaptchaConfig } = useCaptchaConfig()

  const { mutate: mutateConfig } = useConfig()
  const [disabled, setDisabled] = useState(false)
  const [globalConfig, setGlobalConfig] = useState<GlobalConfig | null>()
  const [accountPolicy, setAccountPolicy] = useState<AccountPolicy | null>()
  const [containerPolicy, setContainerPolicy] = useState<ContainerPolicy | null>()
  const [logoFile, setLogoFile] = useState<File | null>(null)
  const [sloganLines, setSloganLinesState] = useState<string[]>([])

  const { t } = useTranslation()

  const [saved, setSaved] = useState(true)

  const setSloganLines = (lines: string[]) => {
    setSloganLinesState(lines)
    setGlobalConfig({ ...globalConfig, slogan: joinPlatformSlogans(lines) })
  }

  useEffect(() => {
    if (configs) {
      setContainerPolicy(configs.containerPolicy)
      setGlobalConfig(configs.globalConfig)
      setAccountPolicy(configs.accountPolicy)
      setSloganLinesState(splitPlatformSlogans(configs.globalConfig?.slogan))
    }
  }, [configs])

  const updateConfig = async (conf: ConfigEditModel) => {
    setDisabled(true)

    try {
      await api.admin.adminUpdateConfigs(conf)

      if (logoFile) {
        await api.admin.adminUpdateLogo({ file: logoFile })
        setLogoFile(null)
      }

      mutate()
      mutateConfig()
      mutateCaptchaConfig()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onResetLogo = async () => {
    setDisabled(true)
    setLogoFile(null)

    try {
      await api.admin.adminResetLogo()
      mutate()
      mutateConfig()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onSave = () => {
    updateConfig({
      globalConfig,
      accountPolicy,
      containerPolicy,
    })
    setSaved(false)
    setTimeout(() => {
      setSaved(true)
    }, 500)
  }

  return (
    <AdminPage isLoading={!configs}>
      <Stack w="100%" gap="md" className="yy-admin-settings-page">
        <YinyuPanel p="md" className="admin-panel yy-settings-panel">
          <Group justify="space-between" align="center" className="yy-settings-title-row">
            <Title order={2}>{t('admin.content.settings.platform.title')}</Title>
            <Button
              variant="filled"
              size="md"
              leftSection={<Icon path={saved ? mdiContentSaveOutline : mdiCheck} size={1} />}
              onClick={onSave}
              disabled={!saved || disabled}
            >
              {t('admin.button.save')}
            </Button>
          </Group>
          <Divider />
          <Grid columns={4} align="center">
            <Grid.Col span={1}>
              <TextInput
                label={t('admin.content.settings.platform.name.label')}
                description={t('admin.content.settings.platform.name.description')}
                placeholder={PLATFORM_TITLE}
                disabled={disabled}
                value={globalConfig?.title ?? ''}
                onChange={(e) => {
                  setGlobalConfig({ ...globalConfig, title: e.currentTarget.value })
                }}
              />
            </Grid.Col>
            <Grid.Col span={1}>
              <Stack gap={6}>
                <Group justify="space-between" align="center" gap="xs" wrap="nowrap">
                  <Text size="sm" fw={500}>
                    {t('admin.content.settings.platform.slogan.label')}
                  </Text>
                  <ActionIcon
                    size="sm"
                    variant="subtle"
                    disabled={disabled}
                    onClick={() => setSloganLines([...sloganLines, ''])}
                    aria-label={t('common.button.add')}
                  >
                    <Icon path={mdiPlus} size={0.75} />
                  </ActionIcon>
                </Group>
                <Text size="xs" c="dimmed">
                  {t('admin.content.settings.platform.slogan.description')}
                </Text>
                {sloganLines.map((line, index) => (
                  <Group key={index} gap={6} wrap="nowrap">
                    <TextInput
                      size="xs"
                      aria-label={`${t('admin.content.settings.platform.slogan.label')} ${index + 1}`}
                      disabled={disabled}
                      value={line}
                      onChange={(e) => {
                        const next = [...sloganLines]
                        next[index] = e.currentTarget.value
                        setSloganLines(next)
                      }}
                    />
                    <ActionIcon
                      size="sm"
                      variant="subtle"
                      color="red"
                      disabled={disabled || sloganLines.length <= 1}
                      onClick={() => setSloganLines(sloganLines.filter((_, idx) => idx !== index))}
                      aria-label={t('common.button.delete')}
                    >
                      <Icon path={mdiDeleteOutline} size={0.75} />
                    </ActionIcon>
                  </Group>
                ))}
              </Stack>
            </Grid.Col>
            <Grid.Col span={1}>
              <FileInput
                size="sm"
                label={t('admin.content.settings.platform.logo.label')}
                description={t('admin.content.settings.platform.logo.description')}
                placeholder={
                  globalConfig?.faviconHash
                    ? t('admin.placeholder.settings.logo.custom')
                    : t('admin.placeholder.settings.logo.default')
                }
                disabled={disabled}
                accept={IMAGE_MIME_TYPES.join(',')}
                value={logoFile}
                onChange={setLogoFile}
                rightSection={
                  <Tooltip label={t('common.button.reset')}>
                    <ActionIcon onClick={onResetLogo}>
                      <Icon path={mdiRestore} size={0.85} />
                    </ActionIcon>
                  </Tooltip>
                }
              />
            </Grid.Col>
            <Grid.Col p={0} span={1}>
              <Group gap="sm" align="flex-end" justify="center">
                {[20, 40, 60, 80].map((size) => (
                  <Stack align="center" justify="space-between" gap={0} key={size}>
                    <LogoBox size={size} url={logoFile ? URL.createObjectURL(logoFile) : undefined} />
                    <Text fw="bold" ta="center" size="xs">
                      {size}px
                    </Text>
                  </Stack>
                ))}
              </Group>
            </Grid.Col>
            <Grid.Col span={2}>
              <TextInput
                label={t('admin.content.settings.platform.description.label')}
                description={t('admin.content.settings.platform.description.description')}
                placeholder={PLATFORM_DESCRIPTION}
                disabled={disabled}
                value={globalConfig?.description ?? ''}
                onChange={(e) => {
                  setGlobalConfig({ ...globalConfig, description: e.currentTarget.value })
                }}
              />
            </Grid.Col>
          </Grid>
        </YinyuPanel>
        <YinyuPanel p="md" className="admin-panel yy-settings-panel">
          <Title order={2}>{t('admin.content.settings.account.title')}</Title>
          <Divider />
          <SimpleGrid cols={4}>
            <Switch
              checked={accountPolicy?.allowRegister ?? true}
              disabled={disabled}
              label={SwitchLabel(
                t('admin.content.settings.account.allow_register.label'),
                t('admin.content.settings.account.allow_register.description')
              )}
              onChange={(e) =>
                setAccountPolicy({
                  ...accountPolicy,
                  allowRegister: e.currentTarget.checked,
                })
              }
            />
            <Switch
              checked={accountPolicy?.emailConfirmationRequired ?? false}
              disabled={disabled}
              label={SwitchLabel(
                t('admin.content.settings.account.email_confirmation_required.label'),
                t('admin.content.settings.account.email_confirmation_required.description')
              )}
              onChange={(e) =>
                setAccountPolicy({
                  ...accountPolicy,
                  emailConfirmationRequired: e.currentTarget.checked,
                })
              }
            />
            <Switch
              checked={accountPolicy?.activeOnRegister ?? true}
              disabled={disabled}
              label={SwitchLabel(
                t('admin.content.settings.account.auto_active.label'),
                t('admin.content.settings.account.auto_active.description')
              )}
              onChange={(e) =>
                setAccountPolicy({
                  ...accountPolicy,
                  activeOnRegister: e.currentTarget.checked,
                })
              }
            />
            <Switch
              checked={accountPolicy?.useCaptcha ?? false}
              disabled={disabled}
              label={SwitchLabel(
                t('admin.content.settings.account.use_captcha.label'),
                t('admin.content.settings.account.use_captcha.description')
              )}
              onChange={(e) =>
                setAccountPolicy({
                  ...accountPolicy,
                  useCaptcha: e.currentTarget.checked,
                })
              }
            />
          </SimpleGrid>
          <TextInput
            label={t('admin.content.settings.account.email_domain_list.label')}
            description={t('admin.content.settings.account.email_domain_list.description')}
            placeholder={t('admin.placeholder.settings.email_domain_list')}
            value={accountPolicy?.emailDomainList ?? ''}
            onChange={(e) => {
              setAccountPolicy({ ...accountPolicy, emailDomainList: e.currentTarget.value })
            }}
          />
        </YinyuPanel>
        <YinyuPanel p="md" className="admin-panel yy-settings-panel">
          <Title order={2}>{t('admin.content.settings.container.title')}</Title>
          <Divider />
          <SimpleGrid cols={4} className={misc.alignCenter}>
            <NumberInput
              label={t('admin.content.settings.container.default_lifetime.label')}
              description={t('admin.content.settings.container.default_lifetime.description')}
              placeholder="120"
              min={1}
              max={7200}
              disabled={disabled}
              value={containerPolicy?.defaultLifetime ?? 120}
              onChange={(e) => {
                const number = getInputNumber(e)
                if (isNaN(number)) return
                setContainerPolicy({ ...containerPolicy, defaultLifetime: number })
              }}
            />
            <NumberInput
              label={t('admin.content.settings.container.extension_duration.label')}
              description={t('admin.content.settings.container.extension_duration.description')}
              placeholder="120"
              min={1}
              max={7200}
              disabled={disabled}
              value={containerPolicy?.extensionDuration ?? 120}
              onChange={(e) => {
                const number = getInputNumber(e)
                if (isNaN(number)) return
                setContainerPolicy({ ...containerPolicy, extensionDuration: number })
              }}
            />
            <NumberInput
              label={t('admin.content.settings.container.renewal_window.label')}
              description={t('admin.content.settings.container.renewal_window.description')}
              placeholder="10"
              min={1}
              max={360}
              disabled={disabled}
              value={containerPolicy?.renewalWindow ?? 10}
              onChange={(e) => {
                const number = getInputNumber(e)
                if (isNaN(number)) return
                setContainerPolicy({ ...containerPolicy, renewalWindow: number })
              }}
            />
            <Switch
              checked={containerPolicy?.autoDestroyOnLimitReached ?? true}
              disabled={disabled}
              label={SwitchLabel(
                t('admin.content.settings.container.auto_destroy.label'),
                t('admin.content.settings.container.auto_destroy.description')
              )}
              onChange={(e) =>
                setContainerPolicy({
                  ...containerPolicy,
                  autoDestroyOnLimitReached: e.currentTarget.checked,
                })
              }
            />
          </SimpleGrid>
        </YinyuPanel>
      </Stack>
    </AdminPage>
  )
}

export default Configs
