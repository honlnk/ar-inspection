package issue

import (
	"context"
	"errors"
)

// ErrNotFound 表示按 ID 操作的目标问题不存在；HTTP 层据此映射为 404 issue_not_found。
var ErrNotFound = errors.New("问题不存在")

// Store 定义存储层的领域操作，HTTP 层通过此接口访问数据，不直接拼接 SQL。
type Store interface {
	// Init 建表并执行轻量检查；失败时服务不得启动，也不降级为内存存储。
	Init(ctx context.Context) error

	// HealthCheck 对存储做轻量检查，供健康检查接口判断存储可用性。
	HealthCheck(ctx context.Context) error

	// List 按 createdAt 从新到旧返回全部问题；空库返回空切片而不是 nil。
	List(ctx context.Context) ([]Issue, error)

	// Create 在事务中写入新问题，提交成功后读取并返回完整记录；
	// 写入或提交失败时整体回滚，不返回成功。
	Create(ctx context.Context, it Issue) (Issue, error)

	// UpdateStatus 在事务中更新状态与更新时间，提交成功后读取并返回完整记录；
	// 目标 ID 不存在时返回 ErrNotFound，不产生部分更新。
	UpdateStatus(ctx context.Context, id string, status Status, updatedAt string) (Issue, error)

	Close() error
}
