package issue

// Priority 问题优先级，三端统一枚举，取值与 PRD/架构文档一致。
type Priority string

const (
	PriorityLow    Priority = "low"
	PriorityMedium Priority = "medium"
	PriorityHigh   Priority = "high"
)

// Valid 判断优先级是否为合法枚举值。
func (p Priority) Valid() bool {
	switch p {
	case PriorityLow, PriorityMedium, PriorityHigh:
		return true
	default:
		return false
	}
}

// Status 处理状态，三端统一枚举，取值与 PRD/架构文档一致。
type Status string

const (
	StatusOpen       Status = "open"
	StatusInProgress Status = "in_progress"
	StatusResolved   Status = "resolved"
)

// Valid 判断状态是否为合法枚举值。
func (s Status) Valid() bool {
	switch s {
	case StatusOpen, StatusInProgress, StatusResolved:
		return true
	default:
		return false
	}
}

// Position 是 AR 标记的三维坐标；JSON 字段名三端统一为小写 x/y/z。
type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

// Issue 是问题实体的传输结构；字段名与 PRD 第 7.1 节完全一致，
// 不得增删或改名。时间为带 +08:00 偏移的 RFC 3339 字符串，
// 由后端生成，客户端不得伪造。
type Issue struct {
	ID          string   `json:"id"`
	Title       string   `json:"title"`
	Description string   `json:"description"`
	Priority    Priority `json:"priority"`
	Status      Status   `json:"status"`
	Position    Position `json:"position"`
	CreatedAt   string   `json:"createdAt"`
	UpdatedAt   string   `json:"updatedAt"`
}
