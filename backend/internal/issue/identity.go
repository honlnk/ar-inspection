package issue

import (
	"crypto/rand"
	"fmt"
	"time"
)

// beijingTZ 固定 UTC+08:00 业务时区，不依赖机器本地时区设置。
var beijingTZ = time.FixedZone("CST", 8*60*60)

// FieldGenerator 生成服务端字段（ID 与时间）的小函数集。
// 默认实现使用 crypto/rand 与真实北京时间；通过结构体注入便于测试覆盖失败路径，
// 不引入依赖注入框架。
type FieldGenerator struct {
	NewID  func() (string, error)
	NewNow func() (string, error)
}

// DefaultFieldGenerator 返回默认生成器：crypto/rand UUID v4 与秒级 RFC 3339 北京时间。
func DefaultFieldGenerator() FieldGenerator {
	return FieldGenerator{NewID: newUUIDv4, NewNow: nowBeijingRFC3339}
}

// newUUIDv4 用 crypto/rand 生成 UUID v4 格式字符串；随机源失败时返回错误，
// 不降级为可预测 ID。
func newUUIDv4() (string, error) {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		return "", fmt.Errorf("读取随机源失败: %w", err)
	}
	b[6] = (b[6] & 0x0f) | 0x40 // 版本号固定为 4
	b[8] = (b[8] & 0x3f) | 0x80 // 变体固定为 RFC 4122
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:16]), nil
}

// nowBeijingRFC3339 返回当前北京时间的秒级 RFC 3339 字符串（带 +08:00 偏移）。
func nowBeijingRFC3339() (string, error) {
	return time.Now().In(beijingTZ).Format(time.RFC3339), nil
}
