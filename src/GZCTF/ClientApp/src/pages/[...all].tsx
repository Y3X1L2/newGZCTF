import { Button, Group } from '@mantine/core'
import { Search } from 'lucide-react'
import { FC, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { YinyuHeartbeatIcon, YinyuHexField } from '@Components/yinyu/YinyuUI'
import { usePageTitle } from '@Hooks/usePageTitle'

const Error404: FC = () => {
  const navigate = useNavigate()
  const location = useLocation()
  const { t } = useTranslation()

  usePageTitle(t('common.title.404'))

  useEffect(() => {
    if (location.pathname !== '/404') {
      navigate('/404')
    }
  }, [location, navigate])

  return (
    <WithNavBar minWidth={0} width="var(--container)" withFooter>
      <section className="yy-page-frame yy-state-stage">
        <article className="state-page panel-card state-neutral yy-state-page yy-large-state">
          <YinyuHexField cells={72} />
          <div className="error-code">404</div>
          <div className="yy-error-heading">
            <Search size={42} />
            <YinyuHeartbeatIcon label="missing route signal" />
          </div>
          <h3>{t('common.content.404.title')}</h3>
          <p>{t('common.content.404.text')}</p>
          <Group className="yy-error-actions">
            <Button onClick={() => navigate('/')}>返回首页</Button>
            <Button variant="outline" onClick={() => navigate('/games')}>
              查看赛事
            </Button>
          </Group>
        </article>
      </section>
    </WithNavBar>
  )
}

export default Error404
