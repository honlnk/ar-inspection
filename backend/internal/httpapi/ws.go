package httpapi

import (
	"context"
	"net/url"
	"strings"
	"time"

	"github.com/coder/websocket"
	"github.com/gin-gonic/gin"
)

// wsWriteTimeout 单条事件写超时：慢消费者超时后由服务端断开，不阻塞其他连接。
const wsWriteTimeout = 5 * time.Second

// handleWS 处理 GET /api/ws：仅管理员（路由组已限制角色）。
// 协议为服务端单向推送：客户端不发送业务帧，连接只用于接收事件；
// CloseRead 等待客户端关闭并自动回应，收到任何数据帧视为关闭信号。
func (h *Handler) handleWS(c *gin.Context) {
	conn, err := websocket.Accept(c.Writer, c.Request, &websocket.AcceptOptions{
		// 浏览器 WebSocket 无法自定义 Authorization 头：鉴权依赖握手 Cookie，
		// Origin 校验防止其他站点页面携带 Cookie 发起跨站连接
		OriginPatterns: h.wsOriginPatterns,
	})
	if err != nil {
		// 升级失败已由 websocket.Accept 写入 403 响应，这里只记录
		_ = c.Error(err)
		return
	}
	ctx := conn.CloseRead(c.Request.Context())

	ch := h.hub.Subscribe()
	defer h.hub.Unsubscribe(ch)
	defer conn.CloseNow()

	for {
		select {
		case payload, ok := <-ch:
			if !ok {
				conn.Close(websocket.StatusNormalClosure, "")
				return
			}
			writeCtx, cancel := context.WithTimeout(ctx, wsWriteTimeout)
			writeErr := conn.Write(writeCtx, websocket.MessageText, payload)
			cancel()
			if writeErr != nil {
				return // 写失败（超时/客户端断开）：结束本连接
			}
		case <-ctx.Done():
			return
		}
	}
}

// originToPatterns 把允许的 CORS 来源（完整 URL）转为 websocket 握手的
// host 匹配模式（coder/websocket 按 Origin 的 host 部分做 path.Match）。
func originToPatterns(origins []string) []string {
	patterns := make([]string, 0, len(origins))
	for _, o := range origins {
		o = strings.TrimSpace(o)
		if o == "" {
			continue
		}
		if u, err := url.Parse(o); err == nil && u.Host != "" {
			patterns = append(patterns, u.Host)
		} else {
			patterns = append(patterns, o) // 已是 host 形式的配置直接使用
		}
	}
	return patterns
}
