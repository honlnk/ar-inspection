# 计划 006 4.3：后端多阶段镜像——固定 Go 工具链构建、非 root 运行。
# SQLite 为纯 Go 驱动（modernc.org/sqlite），CGO 关闭即可静态编译。
# 本机为 Apple Silicon（linux/arm64），基础镜像均提供 arm64 原生版本，不做 x86 模拟。
FROM golang:1.27-alpine AS build
WORKDIR /src

# 先拷贝依赖清单再下载，利用层缓存；不复制本机数据库、密钥等（见 .dockerignore）
COPY go.mod go.sum ./
RUN go mod download

COPY . .
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /out/server ./cmd/server

FROM alpine:3.22
RUN apk add --no-cache ca-certificates \
    && adduser -D -u 10001 app \
    && mkdir -p /data \
    && chown app:app /data
COPY --from=build /out/server /server

# 默认写入命名卷挂载点；ADDR 默认 :8080 监听所有网卡（真机 Unity 直连用）
ENV ISSUE_DB_PATH=/data/inspection.db
USER app
EXPOSE 8080
ENTRYPOINT ["/server"]
