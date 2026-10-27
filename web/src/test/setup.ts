import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// vitest 未启用 globals，React Testing Library 的自动清理需在此显式注册
afterEach(cleanup)
