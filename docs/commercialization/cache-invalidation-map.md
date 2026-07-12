# Cache Invalidation Map

Redis 仅承载可重建投影和短期协调数据，PostgreSQL 始终是业务事实来源。未登记在本文件和 `CachePolicyCatalog` 的业务缓存不得上线。

| Projection | Key dimensions | Revision owner | L1 / L2 TTL | Maximum stale | Mutation triggers | Redis failure | Verification |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Scoreboard | game ID, global revision, game revision | PostgreSQL `ProjectionRevisions` | 2s / 30s | 0s across revision | Game、Submission、Participation、AWDP、Penetration score mutations；显式管理刷新 | bypass to PostgreSQL generator | cache policy tests；scoreboard integration suite |
| TheoryStatistics | game ID, global revision, game revision | PostgreSQL `ProjectionRevisions` | 5s / 60s | 0s across revision | TheoryPaper、TheoryAnswerSheet、game mutations | bypass to PostgreSQL result builder | theory API integration suite |
| TrainingStatistics | user ID, global revision, user revision | PostgreSQL `ProjectionRevisions` | 5s / 60s | 0s across revision | course structure/enrollment global changes；progress、submission、check-in、theory sheet user changes | bypass to PostgreSQL overview builder | training API integration suite |
| ClientConfig | global | explicit tag | 30s / 10m | 10m | all `[CacheFlush(client-config)]` settings and logo changes | bypass to options/config facts | configuration integration suite |
| Index | global | explicit tag | 30s / 10m | 10m | title and description changes | rebuild from current template/config | index handler tests |
| Favicon | global | explicit tag | 30s / 10m | 10m | favicon hash changes or missing blob | resolve current config then fallback to embedded icon | favicon handler tests |
| CaptchaConfig | global | explicit tag | 30s / 10m | 10m | captcha settings changes | rebuild from current options | info API tests |
| GameList | global | explicit tag | 5s / 60s | 60s | game create/update/delete/status changes | query PostgreSQL | game API tests |
| RecentGames | global | explicit tag | 5s / 60s | 60s | game create/update/delete/status changes and hourly time-window refresh | query PostgreSQL | game API tests |
| GameDetails | game ID | explicit tag | 5s / 2m | 2m | game/division changes | query PostgreSQL | game detail tests |
| Posts | global | explicit tag | 10s / 5m | 5m | post create/update/delete | query PostgreSQL | post API tests |
| GameNotices | game ID | explicit tag | 5s / 60s | 60s | notice create/update/delete | query PostgreSQL | notice API tests |
| ExerciseAvailability | global | explicit tag | 10s / 60s | 60s | exercise availability mutations | query PostgreSQL | exercise repository tests |

## Invariants

- Revision-consistent keys include both global and resource revision. A failed Redis removal therefore cannot expose a previous PostgreSQL revision.
- Tag-invalidated projections always carry both policy-wide and resource tags. Global invalidation does not enumerate keys.
- Cache keys contain hashed resource dimensions. Usernames, team names, tokens, flags and IP addresses are not embedded in Redis keys.
- `PlatformCache` catches cache transport failures and executes the PostgreSQL factory once for that request; it never converts a cache error into a business failure.
- Cache policy metrics use only policy/purpose and status labels. Resource IDs are not metric labels.
