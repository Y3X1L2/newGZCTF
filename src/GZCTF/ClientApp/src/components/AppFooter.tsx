import { Center, Divider, Stack, Text } from '@mantine/core'
import { FC } from 'react'
import { FooterRender } from '@Components/FooterRender'
import { MainIcon } from '@Components/icon/MainIcon'
import { PLATFORM_BRAND } from '@Utils/Brand'
import { useIsMobile } from '@Utils/ThemeOverride'
import { useConfig } from '@Hooks/useConfig'
import classes from '@Styles/AppFooter.module.css'

export const AppFooter: FC = () => {
  const { config } = useConfig()
  const isMobile = useIsMobile()

  return (
    <>
      <div className={classes.spacer} />
      <div className={classes.wrapper}>
        <Center mx="auto" h="100%">
          <Stack gap="sm" w={isMobile ? '100%' : '80%'}>
            <Stack w="100%" align="center" gap={2}>
              <MainIcon size={isMobile ? '3rem' : '4rem'} />
              <Text fw="bold" size={isMobile ? '2rem' : '2.5rem'}>
                {PLATFORM_BRAND}
              </Text>
            </Stack>
            {isMobile ? (
              <>
                <Text size="sm" ta="center" fw={400} c="dimmed">
                  {PLATFORM_BRAND}
                </Text>
                {config.footerInfo && <Divider />}
              </>
            ) : (
              <Divider
                label={
                  <Text size="sm" ta="center" fw={400} c="dimmed">
                    {PLATFORM_BRAND}
                  </Text>
                }
                labelPosition="center"
              />
            )}
            {config.footerInfo && <FooterRender source={config.footerInfo} />}
          </Stack>
        </Center>
      </div>
    </>
  )
}
