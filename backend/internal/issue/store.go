package issue

import "context"

// Store 定义存储层的领域操作，HTTP 层通过此接口访问数据，不直接拼接 SQL。
// 问题创建与状态修改留给后续业务计划，本期只包含骨架所需能力。
type Store interface {
	// Init 建表并执行轻量检查；失败时服务不得启动，也不降级为内存存储。
	Init(ctx context.Context) error

	// HealthCheck 对存储做轻量检查，供健康检查接口判断存储可用性。
	HealthCheck(ctx context.Context) error

	// List 按 createdAt 从新到旧返回全部问题；空库返回空切片而不是 nil。
	List(ctx context.Context) ([]Issue, error)

	Close() error
}
