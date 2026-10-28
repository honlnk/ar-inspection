package httpapi

import (
	"encoding/json"
	"sync"
	"sync/atomic"

	"ar-inspection/backend/internal/issue"
)

// WebSocketEvent 实时事件正文：完整八字段问题对象 + 事件类型 + 单调递增序号。
// 序号由本进程事件中心分配；进程重启后从 1 重新开始，客户端检测回退时以
// 重连后的全量列表为准（计划 006 第 4.2 节）。
type WebSocketEvent struct {
	Type  string      `json:"type"` // issue.created / issue.updated
	Seq   uint64      `json:"seq"`
	Issue issue.Issue `json:"issue"`
}

// Hub 单进程 WebSocket 连接与事件中心：发布在存储事务提交成功之后进行；
// 无连接时事件丢弃（客户端以全量校准兜底）。本期单进程，不引入 Redis。
type Hub struct {
	seq     atomic.Uint64
	mu      sync.Mutex
	clients map[chan []byte]struct{}
}

// NewHub 构建事件中心。
func NewHub() *Hub {
	return &Hub{clients: make(map[chan []byte]struct{})}
}

// Publish 序列化事件并广播给全部连接；单条序列化失败时跳过该事件（不阻塞写库路径）。
func (h *Hub) Publish(eventType string, it issue.Issue) {
	payload, err := json.Marshal(WebSocketEvent{Type: eventType, Seq: h.seq.Add(1), Issue: it})
	if err != nil {
		return
	}
	h.mu.Lock()
	defer h.mu.Unlock()
	for ch := range h.clients {
		// 非阻塞投递：订阅通道缓冲写满（慢消费者）时丢弃该条，由客户端全量校准兜底
		select {
		case ch <- payload:
		default:
		}
	}
}

// Subscribe 注册一个事件通道；缓冲 16 条。
func (h *Hub) Subscribe() chan []byte {
	ch := make(chan []byte, 16)
	h.mu.Lock()
	h.clients[ch] = struct{}{}
	h.mu.Unlock()
	return ch
}

// Unsubscribe 注销事件通道并关闭。
func (h *Hub) Unsubscribe(ch chan []byte) {
	h.mu.Lock()
	if _, ok := h.clients[ch]; ok {
		delete(h.clients, ch)
		close(ch)
	}
	h.mu.Unlock()
}
