import { Text, Title } from '@mantine/core'
import {
  mdiAccountHardHatOutline,
  mdiChartTimelineVariant,
  mdiHexagonMultipleOutline,
  mdiSchoolOutline,
  mdiServerNetwork,
  mdiShieldCheckOutline,
  mdiTrophyOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import gsap from 'gsap'
import { FC, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { WithNavBar } from '@Components/WithNavbar'
import { LogoDistortion } from '@Components/yinyu/grid-distortion/LogoDistortion'
import { YinyuGradientText } from '@Components/yinyu/YinyuReactBits'
import { YinyuHexField } from '@Components/yinyu/YinyuUI'
import { PLATFORM_DESCRIPTION, PLATFORM_SLOGAN, PLATFORM_TITLE } from '@Utils/Brand'
import { useIsMobile } from '@Utils/ThemeOverride'
import { usePageTitle } from '@Hooks/usePageTitle'
import classes from '@Styles/About.module.css'

const platformType = '\u5b89\u5168\u7efc\u5408\u6f14\u7ec3\u5e73\u53f0'

const features = [
  {
    icon: mdiTrophyOutline,
    title: '\u8d5b\u4e8b\u8fd0\u8425',
    description: '\u8986\u76d6\u62a5\u540d\u3001\u5206\u7ec4\u3001\u9898\u76ee\u3001\u901a\u77e5\u3001\u6392\u884c\u548c\u8d5b\u540e\u5f52\u6863\u3002',
  },
  {
    icon: mdiShieldCheckOutline,
    title: '\u653b\u9632\u6f14\u7ec3',
    description: '\u652f\u6301 AWDP \u5b9e\u4f8b\u3001\u653b\u51fb\u63d0\u4ea4\u3001\u8865\u4e01\u9a8c\u8bc1\u548c\u8f6e\u6b21\u8ba1\u5206\u3002',
  },
  {
    icon: mdiServerNetwork,
    title: '\u5206\u5e03\u5f0f\u9776\u573a',
    description: '\u7edf\u4e00\u7ba1\u7406\u8282\u70b9\u3001\u955c\u50cf\u3001\u865a\u62df\u673a\u5b9e\u4f8b\u548c\u8fdc\u7a0b\u8c03\u5ea6\u72b6\u6001\u3002',
  },
  {
    icon: mdiHexagonMultipleOutline,
    title: '\u7406\u8bba\u9898\u5e93',
    description: '\u9898\u5e93\u3001\u8bd5\u5377\u3001\u7b54\u9898\u3001\u81ea\u52a8\u5224\u5206\u4e0e\u6210\u7ee9\u5c55\u793a\u4e00\u4f53\u5316\u3002',
  },
  {
    icon: mdiChartTimelineVariant,
    title: '\u5b9e\u65f6\u89c2\u6d4b',
    description: '\u628a\u65e5\u5fd7\u3001\u6307\u6807\u3001\u6392\u540d\u4e0e\u6bd4\u8d5b\u8d8b\u52bf\u653e\u8fdb\u540c\u4e00\u6761\u53ef\u8bfb\u94fe\u8def\u3002',
  },
  {
    icon: mdiAccountHardHatOutline,
    title: '\u5b89\u5168\u6821\u9a8c',
    description: '\u56f4\u7ed5\u6743\u9650\u3001\u5ba1\u6838\u3001\u8d44\u6e90\u9650\u5236\u548c\u5b9e\u4f8b\u72b6\u6001\u4fdd\u6301\u53ef\u63a7\u8fb9\u754c\u3002',
  },
  {
    icon: mdiSchoolOutline,
    title: '\u5f00\u53d1\u8005',
    description: '\u56db\u5ddd\u5927\u5b66\u7f51\u7edc\u9776\u573a',
    accent: true,
  },
]

const rows = [features.slice(0, 2), features.slice(2, 5), features.slice(5)]

const About: FC = () => {
  const { t } = useTranslation()
  const isMobile = useIsMobile()
  const stageRef = useRef<HTMLElement>(null)

  usePageTitle(t('common.title.about'))

  useEffect(() => {
    const stage = stageRef.current
    if (!stage || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return undefined

    const ctx = gsap.context(() => {
      gsap.from('.yy-about-logo-distortion', {
        opacity: 0,
        scale: 0.94,
        duration: 0.78,
        ease: 'expo.out',
      })

      gsap.from('.yy-about-hive-cell', {
        opacity: 0,
        y: 12,
        scale: 0.94,
        duration: 0.52,
        ease: 'power3.out',
        stagger: 0.035,
      })
    }, stage)

    return () => ctx.revert()
  }, [])

  return (
    <WithNavBar minWidth={0} width="var(--container)">
      <section ref={stageRef} className={`yy-page-frame yy-about-page ${classes.container}`} data-mobile={isMobile || undefined}>
        <div className="yy-about-stage">
          <div className="yy-about-logo-panel" aria-hidden="true">
            <LogoDistortion className="yy-about-logo-distortion" />
          </div>

          <div className="yy-about-copy">
            <div className="yy-about-heading">
              <span className="yy-section-kicker">PLATFORM PROFILE</span>
              <Title order={1} className="yy-brand-title">
                <span>
                  <YinyuGradientText tone="silver">{PLATFORM_TITLE}</YinyuGradientText>
                </span>
                <em>
                  <YinyuGradientText tone="signal">{platformType}</YinyuGradientText>
                </em>
              </Title>
              <Text className="yy-about-slogan">{PLATFORM_SLOGAN}</Text>
              <Text className="yy-about-description">{PLATFORM_DESCRIPTION}</Text>
            </div>

            <div className="yy-about-hive" aria-label="YINYU platform capabilities">
              {rows.map((row, rowIndex) => (
                <div key={rowIndex} className="yy-about-hive-row">
                  {row.map((feature) => (
                    <article key={feature.title} className={`yy-about-hive-cell ${feature.accent ? 'is-accent' : ''}`}>
                      <YinyuHexField cells={18} />
                      <span className="yy-about-hive-icon" aria-hidden="true">
                        <Icon path={feature.icon} size={1.08} />
                      </span>
                      <strong>{feature.title}</strong>
                      <p>{feature.description}</p>
                    </article>
                  ))}
                </div>
              ))}
            </div>
          </div>
        </div>
      </section>
    </WithNavBar>
  )
}

export default About
