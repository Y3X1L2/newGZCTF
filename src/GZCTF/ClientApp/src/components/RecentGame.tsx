import dayjs from 'dayjs'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { GameFlagGlyph, GameStatus } from '@Components/GameCard'
import { YinyuHexField, YinyuStatusPill, YinyuStatusTone } from '@Components/yinyu/YinyuUI'
import { useLanguage } from '@Utils/I18n'
import { getGameStatus } from '@Hooks/useGame'
import { BasicGameInfoModel } from '@Api'

export interface RecentGameProps {
  game: BasicGameInfoModel
}

function statusTone(status: GameStatus): YinyuStatusTone {
  if (status === GameStatus.OnGoing) return 'success'
  if (status === GameStatus.Coming) return 'warm'
  return 'neutral'
}

export const RecentGame: FC<RecentGameProps> = ({ game, ...others }) => {
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const { title } = game
  const { startTime, endTime, status } = getGameStatus(game)
  const duration = status === GameStatus.OnGoing ? endTime.diff(dayjs(), 'h') : endTime.diff(startTime, 'h')
  const tone = statusTone(status)

  return (
    <Link {...others} to={`/games/${game.id}`} className="recent-game-card panel-card">
      <YinyuHexField cells={28} />
      <YinyuStatusPill
        tone={tone}
        state={status === GameStatus.OnGoing ? 'running' : status === GameStatus.Coming ? 'open' : 'idle'}
      >
        {status}
      </YinyuStatusPill>
      <GameFlagGlyph tone={tone} />
      <h4>&gt; {title}</h4>
      <dl>
        <div>
          <dt>{status === GameStatus.Coming ? t('game.content.start_at') : t('game.content.end_at')}</dt>
          <dd>
            {status === GameStatus.Coming
              ? dayjs(startTime).locale(locale).format('L LT')
              : dayjs(endTime).locale(locale).format('L LT')}
          </dd>
        </div>
        <div>
          <dt>{status === GameStatus.OnGoing ? t('game.content.remaining_time') : t('game.content.total_time')}</dt>
          <dd>{t('game.content.duration', { hours: duration })}</dd>
        </div>
      </dl>
    </Link>
  )
}
