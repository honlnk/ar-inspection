# 计划 006 4.3：管理端多阶段镜像——Node + pnpm（与本机开发同一版本）构建静态产物，
# 固定 Nginx 镜像对外服务。构建时 API 基址置空串：前端走同源相对路径，
# 由 Nginx 把 /api 反向代理到后端容器，摆脱对宿主机 localhost:8080 的依赖。
FROM node:24-alpine AS build
WORKDIR /src

# corepack 随 Node 镜像内置；固定与本机开发一致的 pnpm 版本
RUN corepack enable pnpm && corepack install -g pnpm@10.21.0

# 先拷贝清单装依赖，利用层缓存；锁文件冻结安装
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY . .
ENV VITE_API_BASE_URL=""
RUN pnpm build

FROM nginx:1.29-alpine
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 80
