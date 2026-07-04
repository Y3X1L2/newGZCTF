-- init.lua - NebulaMind cache-broker 种子数据注入
-- ============================================================
-- 通过 redis-cli --eval 执行，Flag 作为 ARGV[1] 传入。
-- 种子数据包括：队列任务、worker 心跳、任务结果、队列配置、队列统计。
--
-- D1 Flag 注入位置：task:result:task_003 的 metadata.debug.envSnapshot.INTERNAL_QUEUE_TOKEN
-- 这是一个开启了 debug 环境快照的旧任务，Flag 作为内部队列令牌被意外记录。
-- ============================================================

local flag = ARGV[1] or 'flag{d1_redis_queue_info_placeholder}'
local cache_broker_host = ARGV[2] or ''
if cache_broker_host == '' then
    error('NM_CACHE_BROKER_HOST is required')
end
local redis_url = 'redis://' .. cache_broker_host .. ':6379/2'

-- ============================================================
-- 1. 队列任务 (LPUSH 到各业务队列)
-- ============================================================

-- document-parse-queue: 文档解析队列（document-worker 消费）
local parse_tasks = {
    cjson.encode({
        taskId = "task_008",
        type = "document-parse",
        payload = {
            sourceUrl = "s3://nm-uploads/2026/06/customer-contract-v3.pdf",
            format = "pdf",
            tenantId = "nm-tenant-004"
        },
        priority = "normal",
        retries = 0,
        createdAt = "2026-06-19T07:42:18Z",
        consumerGroup = "document-workers"
    }),
    cjson.encode({
        taskId = "task_009",
        type = "document-ocr",
        payload = {
            sourceUrl = "s3://nm-uploads/2026/06/scanned-invoice-batch.zip",
            format = "image",
            tenantId = "nm-tenant-002"
        },
        priority = "high",
        retries = 0,
        createdAt = "2026-06-19T07:45:33Z",
        consumerGroup = "document-workers"
    }),
    cjson.encode({
        taskId = "task_010",
        type = "document-embed",
        payload = {
            documentId = "kb-doc-0451",
            model = "nm-embed-v2",
            tenantId = "nm-tenant-001"
        },
        priority = "low",
        retries = 0,
        createdAt = "2026-06-19T07:48:02Z",
        consumerGroup = "document-workers"
    })
}

for _, task in ipairs(parse_tasks) do
    redis.call('LPUSH', 'document-parse-queue', task)
end

-- model-train-queue: 模型训练队列
redis.call('LPUSH', 'model-train-queue', cjson.encode({
    taskId = "train_0042",
    type = "model-finetune",
    payload = {
        modelId = "nm-llm-7b-v3",
        dataset = "ds_customer_001",
        epochs = 3,
        gpuType = "A100"
    },
    priority = "high",
    retries = 0,
    createdAt = "2026-06-19T06:15:00Z",
    consumerGroup = "model-trainers"
}))

-- notification-queue: 通知队列
redis.call('LPUSH', 'notification-queue', cjson.encode({
    taskId = "notif_1024",
    type = "email-notification",
    payload = {
        template = "parse-complete",
        recipients = {"ops@nebulamind.ai"},
        taskId = "task_001"
    },
    priority = "normal",
    retries = 0,
    createdAt = "2026-06-19T07:12:55Z",
    consumerGroup = "notification-workers"
}))

-- document-dlq: 死信队列（失败任务归档）
redis.call('LPUSH', 'document-dlq', cjson.encode({
    taskId = "task_002_old",
    type = "document-convert",
    payload = {
        sourceUrl = "s3://nm-uploads/2026/06/legacy-doc.docx",
        format = "docx"
    },
    priority = "normal",
    retries = 3,
    failedReason = "conversion timeout after 120s",
    createdAt = "2026-06-18T22:10:44Z",
    failedAt = "2026-06-18T22:12:51Z",
    consumerGroup = "document-workers"
}))

-- ============================================================
-- 2. 任务结果 (SET task:result:*)
-- task_003 是"旧任务"，metadata 中包含 D1 Flag
-- ============================================================

-- task_001: 正常完成的文档解析任务
redis.call('SET', 'task:result:task_001', cjson.encode({
    taskId = "task_001",
    status = "completed",
    result = {
        pages = 8,
        extractedText = "NebulaMind Q1 2026 Financial Report\nRevenue: $4.2M (+18% YoY)\nOperating Margin: 23%\n...",
        metadata = {
            format = "pdf",
            sizeBytes = 2456789,
            language = "zh-CN",
            ocrUsed = false
        },
        duration = 2.34
    },
    metadata = {
        workerId = "document-worker-01",
        processedAt = "2026-06-19T07:12:55Z",
        source = "portal-upload",
        tenantId = "nm-tenant-001"
    },
    timestamp = 1781851175000
}))

-- task_002: OCR 任务完成
redis.call('SET', 'task:result:task_002', cjson.encode({
    taskId = "task_002",
    status = "completed",
    result = {
        pages = 3,
        extractedText = "Invoice #NM-2026-0451\nVendor: CloudProvider Inc\nAmount: $12,450.00\nDue: 2026-07-15",
        metadata = {
            format = "png",
            sizeBytes = 892341,
            language = "en-US",
            ocrUsed = true,
            ocrConfidence = 0.96
        },
        duration = 5.12
    },
    metadata = {
        workerId = "document-worker-02",
        processedAt = "2026-06-19T07:18:22Z",
        source = "portal-upload",
        tenantId = "nm-tenant-002"
    },
    timestamp = 1781851502000
}))

-- task_003: 旧任务（debug 模式），metadata 中包含 D1 Flag
-- 这是一个开启了 debug 环境快照的内部文档解析任务，
-- Flag 作为 INTERNAL_QUEUE_TOKEN 环境变量值被意外记录在 metadata.debug.envSnapshot 中。
redis.call('SET', 'task:result:task_003', cjson.encode({
    taskId = "task_003",
    status = "completed",
    result = {
        pages = 15,
        extractedText = "Internal Architecture Review - NebulaMind Platform v2\n... [content redacted for brevity] ...",
        metadata = {
            format = "pdf",
            sizeBytes = 4521987,
            language = "en-US",
            ocrUsed = false,
            classification = "internal"
        },
        duration = 4.87
    },
    metadata = {
        workerId = "document-worker-01",
        processedAt = "2026-06-15T14:23:11Z",
        source = "internal-doc-batch",
        tenantId = "nm-internal",
        debug = {
            enabled = true,
            reason = "customer-reported parse anomaly - verbose logging enabled",
            envSnapshot = {
                REDIS_URL = redis_url,
                INTERNAL_QUEUE_TOKEN = flag,
                LOG_LEVEL = "DEBUG",
                WORKER_CONCURRENCY = "4",
                MAX_RETRIES = "3"
            },
            traceId = "trace-003-debug-20260615"
        }
    },
    timestamp = 1781526591000
}))

-- task_004: 失败的文档转换任务
redis.call('SET', 'task:result:task_004', cjson.encode({
    taskId = "task_004",
    status = "failed",
    result = {
        error = "conversion_timeout",
        errorMessage = "Document conversion exceeded 120s timeout",
        partialPages = 4
    },
    metadata = {
        workerId = "document-worker-02",
        processedAt = "2026-06-19T03:45:12Z",
        source = "scheduled-batch",
        tenantId = "nm-tenant-003",
        retries = 3,
        lastError = "convert process killed: signal SIGTERM"
    },
    timestamp = 1781839512000
}))

-- task_005: 完成的文档摘要任务
redis.call('SET', 'task:result:task_005', cjson.encode({
    taskId = "task_005",
    status = "completed",
    result = {
        pages = 22,
        extractedText = "NebulaMind Platform Technical Specification v2.1...",
        summary = "This document describes the architecture of the NebulaMind AI platform, including microservice boundaries, data flow, security zones, and deployment topology.",
        metadata = {
            format = "pdf",
            sizeBytes = 6892341,
            language = "en-US",
            model = "nm-summarize-v1"
        },
        duration = 8.45
    },
    metadata = {
        workerId = "document-worker-01",
        processedAt = "2026-06-19T06:22:08Z",
        source = "portal-upload",
        tenantId = "nm-tenant-001"
    },
    timestamp = 1781848928000
}))

-- ============================================================
-- 3. Worker 心跳 (SET worker:heartbeat:*)
-- ============================================================

redis.call('SET', 'worker:heartbeat:document-worker-01', cjson.encode({
    workerId = "document-worker-01",
    hostname = "document-worker",
    status = "active",
    lastSeen = "2026-06-19T07:49:33Z",
    version = "2026.06.1",
    consumerGroup = "document-workers",
    processedTasks = 1547,
    uptime = "3d 14h 22m",
    concurrency = 4
}))

redis.call('SET', 'worker:heartbeat:document-worker-02', cjson.encode({
    workerId = "document-worker-02",
    hostname = "document-worker",
    status = "active",
    lastSeen = "2026-06-19T07:49:31Z",
    version = "2026.06.1",
    consumerGroup = "document-workers",
    processedTasks = 1489,
    uptime = "3d 14h 22m",
    concurrency = 4
}))

redis.call('SET', 'worker:heartbeat:ai-console-api', cjson.encode({
    workerId = "ai-console-api",
    hostname = "ai-console-api",
    status = "active",
    lastSeen = "2026-06-19T07:49:35Z",
    version = "2026.06.1",
    consumerGroup = "api-workers",
    processedTasks = 8924,
    uptime = "7d 02h 15m",
    concurrency = 2
}))

-- ============================================================
-- 4. Worker 注册信息 (SET worker:info:*)
-- ============================================================

redis.call('SET', 'worker:info:document-worker', cjson.encode({
    workerId = "document-worker",
    service = "document-worker",
    version = "2026.06.1",
    zone = "business",
    networks = {"dmz-service", "biz-core"},
    port = 8080,
    consumerGroup = "document-workers",
    taskTypes = {"document-parse", "document-ocr", "document-embed", "document-summarize", "document-classify", "document-extract", "document-convert"},
    maxRetries = 3,
    visibilityTimeout = 300,
    deadLetterQueue = "document-dlq",
    registeredAt = "2026-06-16T01:22:00Z"
}))

redis.call('SET', 'worker:info:ai-console-api', cjson.encode({
    workerId = "ai-console-api",
    service = "ai-console-api",
    version = "2026.06.1",
    zone = "business",
    networks = {"biz-core", "data-plane"},
    port = 8080,
    consumerGroup = "api-workers",
    taskTypes = {"api-request", "audit-export", "graphql-query"},
    registeredAt = "2026-06-12T08:00:00Z"
}))

-- ============================================================
-- 5. 队列配置 (SET queue:config)
-- ============================================================

redis.call('SET', 'queue:config', cjson.encode({
    queues = {
        {
            name = "document-parse-queue",
            consumerGroup = "document-workers",
            taskTypes = {"document-parse", "document-ocr", "document-embed", "document-summarize", "document-classify", "document-extract", "document-convert"},
            maxRetries = 3,
            visibilityTimeout = 300,
            deadLetterQueue = "document-dlq",
            maxPayloadSize = 52428800
        },
        {
            name = "model-train-queue",
            consumerGroup = "model-trainers",
            taskTypes = {"model-finetune", "model-eval", "model-deploy"},
            maxRetries = 1,
            visibilityTimeout = 3600,
            deadLetterQueue = "model-train-dlq",
            maxPayloadSize = 104857600
        },
        {
            name = "notification-queue",
            consumerGroup = "notification-workers",
            taskTypes = {"email-notification", "sms-notification", "webhook-notification"},
            maxRetries = 5,
            visibilityTimeout = 60,
            deadLetterQueue = "notification-dlq",
            maxPayloadSize = 1048576
        }
    },
    broker = {
        type = "redis",
        version = "7.2",
        maxmemory = "128mb",
        maxmemoryPolicy = "allkeys-lru",
        persistence = "none"
    },
    retryPolicy = {
        backoff = "exponential",
        maxDelay = 30,
        initialDelay = 1
    },
    updatedAt = "2026-06-19T07:00:00Z"
}))

-- ============================================================
-- 6. 队列统计 (SET queue:stats)
-- ============================================================

redis.call('SET', 'queue:stats', cjson.encode({
    ["document-parse-queue"] = {pending = 3, processing = 1, completed = 1547, failed = 12, deadLettered = 8},
    ["model-train-queue"] = {pending = 1, processing = 0, completed = 42, failed = 3, deadLettered = 1},
    ["notification-queue"] = {pending = 1, processing = 0, completed = 8924, failed = 23, deadLettered = 5},
    ["document-dlq"] = {pending = 1, processing = 0, completed = 0, failed = 0, deadLettered = 8},
    totalTasks = 10536,
    totalWorkers = 3,
    activeWorkers = 3,
    updatedAt = "2026-06-19T07:49:35Z"
}))

-- ============================================================
-- 7. 队列索引 (SET queue:index - 便于选手发现所有队列与 key 前缀)
-- ============================================================

redis.call('SET', 'queue:index', cjson.encode({
    queues = {"document-parse-queue", "model-train-queue", "notification-queue", "document-dlq"},
    taskResultPrefix = "task:result:",
    workerHeartbeatPrefix = "worker:heartbeat:",
    workerInfoPrefix = "worker:info:",
    configKeys = {"queue:config", "queue:stats", "queue:index"},
    note = "NebulaMind internal queue registry"
}))

-- ============================================================
-- 8. 内部 Broker 信息 (SET internal:broker-info - 增加真实感)
-- ============================================================

redis.call('SET', 'internal:broker-info', cjson.encode({
    service = "cache-broker",
    role = "task-cache-and-message-queue",
    zone = "business",
    version = "2026.06.1",
    redisVersion = "7.2.4",
    auth = "disabled",
    authNote = "auth disabled for internal business network - rely on network isolation",
    connectedClients = 3,
    usedMemory = "12.4M",
    maxMemory = "128M",
    uptime = "3d 14h 22m"
}))

return 'OK: NebulaMind cache-broker seed data injected (flag in task:result:task_003)'
