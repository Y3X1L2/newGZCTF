import { Group, MantineColor, Stack, Text } from '@mantine/core'
import cx from 'clsx'
import { ComponentPropsWithoutRef, FC, memo } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { YinyuStatusText } from '@Components/yinyu/YinyuReactBits'
import { YinyuHexField, YinyuStatusTone } from '@Components/yinyu/YinyuUI'
import { useLanguage } from '@Utils/I18n'
import { useConfig } from '@Hooks/useConfig'
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
  if (status === GameStatus.OnGoing) return '进行中'
  if (status === GameStatus.Coming) return '待开始'
  return '已结束'
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
  const { config } = useConfig()

  return (
    <span className={cx('game-flag-glyph', `glyph-${tone}`, classes.glyph)} aria-hidden="true">
      {poster ? <span className={classes.glyphPoster} style={{ backgroundImage: `url(${poster})` }} /> : null}
      <BrandMark className={classes.glyphBrand} src={config.logoUrl} />
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
          <YinyuStatusText tone={tone} className={classes.status}>
            {statusText}
          </YinyuStatusText>
        </Group>
        <Text lineClamp={2} className={cx('yy-readable-text', classes.summary)}>
          {summary || '暂无演练简介，管理员可在赛事编辑页补充目标、规则与注意事项。'}
        </Text>
        <dl className={classes.metaGrid}>
          <div>
            <dt>{'模式'}</dt>
            <dd>{toLimitTag(t, limit)}</dd>
          </div>
          <div>
            <dt>{'周期'}</dt>
            <dd>{t('game.content.duration', { hours: duration })}</dd>
          </div>
          <div>
            <dt>{'开始'}</dt>
            <dd>{startTime.locale(locale).format('L LT')}</dd>
          </div>
          <div>
            <dt>{'结束'}</dt>
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
