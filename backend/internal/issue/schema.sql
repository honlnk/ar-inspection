-- issues 单表：字段与架构文档 6.1 节一致。
-- 列名使用蛇形命名属于存储内部实现，HTTP 层仍输出 position{x,y,z}、createdAt、updatedAt。
-- 时间列为带 +08:00 偏移的 RFC 3339 字符串。
CREATE TABLE IF NOT EXISTS issues (
    id          TEXT PRIMARY KEY,
    title       TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    priority    TEXT NOT NULL CHECK (priority IN ('low', 'medium', 'high')),
    status      TEXT NOT NULL CHECK (status IN ('open', 'in_progress', 'resolved')),
    position_x  REAL NOT NULL,
    position_y  REAL NOT NULL,
    position_z  REAL NOT NULL,
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);

-- 列表查询索引：createdAt 从新到旧，同时间按 id 保证稳定顺序。
CREATE INDEX IF NOT EXISTS idx_issues_created_at_id ON issues (created_at DESC, id);
