/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** 后端 API 基础地址，来自 .env.local 等环境文件，默认 http://localhost:8080 */
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
