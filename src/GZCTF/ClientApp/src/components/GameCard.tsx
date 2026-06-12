import { Group, MantineColor, Stack, Text } from '@mantine/core'
import cx from 'clsx'
import { ComponentPropsWithoutRef, FC, memo } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { YinyuHexField, YinyuStatusPill, YinyuStatusTone } from '@Components/yinyu/YinyuUI'
import { useLanguage } from '@Utils/I18n'
import { getGameStatus, toLimitTag } from '@Hooks/useGame'
import { BasicGameInfoModel } from '@Api'
import classes from '@Styles/GameCard.module.css'

export enum GameStatus {
  Coming = 'coming',
  OnGoing = 'ongoing',
  Ended = 'ended',
}

export const GameColorMap = new Map<GameStatus, MantineColor>([
  [GameStatus.Coming, 'yellow'],
  [GameStatus.OnGoing, 'green'],
  [GameStatus.Ended, 'blue'],
])

interface GameCardProps extends Omit<ComponentPropsWithoutRef<typeof Link>, 'to'> {
  game: BasicGameInfoModel
  compact?: boolean
}

export function gameStatusTone(status: GameStatus): YinyuStatusTone {
  if (status === GameStatus.OnGoing) return 'success'
  if (status === GameStatus.Coming) return 'warm'
  return 'neutral'
}

export function gameStatusState(status: GameStatus) {
  if (status === GameStatus.OnGoing) return 'running'
  if (status === GameStatus.Coming) return 'open'
  return 'idle'
}

export function gameStatusLabel(status: GameStatus) {
  if (status === GameStatus.OnGoing) return '\u8fdb\u884c\u4e2d'
  if (status === GameStatus.Coming) return '\u5f85\u5f00\u59cb'
  return '\u5df2\u7ed3\u675f'
}

export function compareGamesForDisplay(a: BasicGameInfoModel, b: BasicGameInfoModel) {
  const aStatus = getGameStatus(a)
  const bStatus = getGameStatus(b)
  const rank = {
    [GameStatus.OnGoing]: 0,
    [GameStatus.Coming]: 1,
    [GameStatus.Ended]: 2,
  }
  const rankDiff = rank[aStatus.status] - rank[bStatus.status]

  if (rankDiff !== 0) return rankDiff
  if (aStatus.status === GameStatus.Ended) return bStatus.endTime.valueOf() - aStatus.endTime.valueOf()

  return aStatus.startTime.valueOf() - bStatus.startTime.valueOf()
}

export const GameFlagGlyph: FC<{ tone?: YinyuStatusTone; poster?: string | null }> = memo(({ tone = 'neutral', poster }) => {
  return (
    <span className={cx('game-flag-glyph', `glyph-${tone}`, classes.glyph)} aria-hidden="true">
      {poster ? <span className={classes.glyphPoster} style={{ backgroundImage: `url(${poster})` }} /> : null}
      <BrandMark className={classes.glyphBrand} />
      <YinyuHexField cells={18} />
    </span>
  )
})

export const GameCard: FC<GameCardProps> = memo(({ game, compact = false, className, ...others }) => {
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const { summary, title, limit, poster } = game
  const { startTime, endTime, status } = getGameStatus(game)
  const duration = endTime.diff(startTime, 'hours')
  const tone = gameStatusTone(status)
  const statusText = gameStatusLabel(status)

  return (
    <Link
      {...others}
      to={`/games/${game.id}`}
      className={cx('game-index-card game-event-row panel-card', compact && 'is-compact', classes.root, className)}
    >
      <YinyuHexField cells={32} />
      <div className={classes.poster}>
        {poster ? (
          <img src={poster} alt={title ?? 'game poster'} className={classes.posterImage} loading="lazy" />
        ) : (
          <div className={classes.posterFallback}>
            <GameFlagGlyph tone={tone} />
          </div>
        )}
      </div>
      <Stack gap="sm" className={classes.content}>
        <Group justify="space-between" align="flex-start" wrap="nowrap" className={classes.header}>
          <div>
            <span className={classes.kicker}>YINYU EXERCISE</span>
            <h4>{title}</h4>
          </div>
          <YinyuStatusPill tone={tone} state={gameStatusState(status)} className={classes.status}>
            {statusText}
          </YinyuStatusPill>
        </Group>
        <Text lineClamp={2} className={cx('yy-readable-text', classes.summary)}>
          {summary || '\u6682\u65e0\u6f14\u7ec3\u7b80\u4ecb\uff0c\u7ba1\u7406\u5458\u53ef\u5728\u8d5b\u4e8b\u7f16\u8f91\u9875\u8865\u5145\u76ee\u6807\u3001\u89c4\u5219\u4e0e\u6ce8\u610f\u4e8b\u9879\u3002'}
        </Text>
        <dl className={classes.metaGrid}>
          <div>
            <dt>{'\u6a21\u5f0f'}</dt>
            <dd>{toLimitTag(t, limit)}</dd>
          </div>
          <div>
            <dt>{'\u5468\u671f'}</dt>
            <dd>{t('game.content.duration', { hours: duration })}</dd>
          </div>
          <div>
            <dt>{'\u5f00\u59cb'}</dt>
            <dd>{startTime.locale(locale).format('L LT')}</dd>
          </div>
          <div>
            <dt>{'\u7ed3\u675f'}</dt>
            <dd>{endTime.locale(locale).format('L LT')}</dd>
          </div>
        </dl>
      </Stack>
      <span className={classes.chevron} aria-hidden="true">
        &gt;
      </span>
    </Link>
  )
})
