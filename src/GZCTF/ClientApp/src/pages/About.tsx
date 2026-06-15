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

const platformType = '安全综合演练平台'

const features = [
  {
    icon: mdiTrophyOutline,
    title: '赛事运营',
    description: '覆盖报名、分组、题目、通知、排行和赛后归档。',
  },
  {
    icon: mdiShieldCheckOutline,
    title: '攻防演练',
    description: '支持 AWDP 实例、攻击提交、补丁验证和轮次计分。',
  },
  {
    icon: mdiServerNetwork,
    title: '分布式靶场',
    description: '统一管理节点、镜像、虚拟机实例和远程调度状态。',
  },
  {
    icon: mdiHexagonMultipleOutline,
    title: '理论题库',
    description: '题库、试卷、答题、自动判分与成绩展示一体化。',
  },
  {
    icon: mdiChartTimelineVariant,
    title: '实时观测',
    description: '把日志、指标、排名与比赛趋势放进同一条可读链路。',
  },
  {
    icon: mdiAccountHardHatOutline,
    title: '安全校验',
    description: '围绕权限、审核、资源限制和实例状态保持可控边界。',
  },
  {
    icon: mdiSchoolOutline,
    title: '开发者',
    description: '四川大学网络靶场',
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
